using System.Globalization;
using CL.Storage;
using CL.Storage.Configuration;
using CodeLogic;
using StorageHub.Agent.Scheduling;
using StorageHub.Agent.Sync;
using StorageHub.Contracts.Results;
using StorageHub.Domain.Identifiers;
using StorageHub.Domain.Storage;
using StorageHub.Persistence;
using StorageHub.Persistence.Scheduling;
using StorageHub.Persistence.Sync;
using StorageHub.Storage.Abstractions;
using StorageHub.Storage.CodeLogic;
using StorageHub.Storage.Models;
using StorageHub.Sync;
using StorageHub.Sync.Persistence;
using StorageHub.Transfers;

namespace StorageHub.Agent.IntegrationTests;

/// <summary>
/// Proves that a schedule actually moves bytes, rather than only that each layer works alone.
///
/// The scheduler deliberately does not touch providers: it records a fenced dispatch, which
/// becomes an outbox event, which a separate consumer turns into a preview and -- only when the
/// schedule is Safe automatic and the plan is provably safe -- an approved, executed run. Every
/// existing test of that chain substitutes a double for either the runner or the provider, so the
/// end-to-end path had never been exercised against a real server.
///
/// Everything here is the production type except the clock and the endpoint connector. The
/// connector is replaced only to skip vault-backed credential resolution, which is covered
/// elsewhere; the sessions it hands out are live SFTP and S3 sessions.
/// </summary>
public sealed class ScheduledSyncEndToEndTests : IAsyncLifetime
{
    private static readonly SemaphoreSlim FrameworkGate = new(1, 1);
    private static StorageLibrary? _library;

    private static readonly DateTimeOffset Start = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    private readonly List<RuntimeStorageConnection> _connections = [];
    private readonly ConnectionProfileId _sourceProfileId = ConnectionProfileId.New();
    private readonly ConnectionProfileId _destinationProfileId = ConnectionProfileId.New();
    private string _testRoot = string.Empty;
    private SingleWriterSqliteDatabase? _database;

    public async Task InitializeAsync()
    {
        if (!Required())
        {
            return;
        }

        _testRoot = Path.Combine(Path.GetTempPath(), $"storagehub-schedule-e2e-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(_testRoot);
        await EnsureFrameworkAsync();
    }

    [Fact]
    [Trait("Category", "SyncLabIntegration")]
    public async Task A_due_schedule_runs_the_sync_and_moves_bytes_to_the_destination()
    {
        if (!Required())
        {
            return;
        }

        var clock = new MutableTimeProvider(Start);
        var factory = new CodeLogicStorageSessionFactory(
            _library ?? throw new InvalidOperationException("Storage library unavailable."));
        var source = await RegisterSftpAsync(factory);
        var destination = await RegisterS3Async(factory);

        // Real content on the real SFTP server, under a root this run owns.
        var runId = Guid.NewGuid().ToString("N");
        var sourceRoot = $"scheduled-source/{runId}";
        var destinationRoot = $"scheduled-destination/{runId}";
        await CreateDirectoryTreeAsync(source, sourceRoot);
        await CreateDirectoryTreeAsync(destination, destinationRoot);
        var payload = Enumerable.Range(0, 24_571).Select(index => (byte)(index % 251)).ToArray();
        await WriteAsync(source, $"{sourceRoot}/scheduled.bin", payload);

        var options = new SqliteDatabaseOptions(
            Path.Combine(_testRoot, "storagehub.db"), pooling: false);
        var initialized = await new StorageHubDatabaseInitializer(options).InitializeAsync();
        Assert.True(initialized.IsReady, initialized.Message);
        _database = new SingleWriterSqliteDatabase(options);
        await SeedConnectionAsync(_sourceProfileId, "Lab SFTP");
        await SeedConnectionAsync(_destinationProfileId, "Lab S3");

        var syncProfileId = SyncProfileId.New();
        var profiles = new SqliteSyncProfileRepository(_database, clock);
        var created = await profiles.CreateAsync(new SyncProfile(
            syncProfileId,
            "Scheduled lab sync",
            _sourceProfileId,
            sourceRoot,
            _destinationProfileId,
            destinationRoot,
            SyncDirection.LeftToRight,
            // Update rather than Mirror: Safe automatic refuses to auto-run a plan containing a
            // delete, so a mirror profile would stop at approval by design.
            SyncDeletionMode.Disabled,
            SyncConflictPolicy.Block,
            new DeletionSafetyPolicy(maximumDeletionCount: 10, maximumDeletionPercentage: 10),
            new TransferExecutionOptions(Overwrite: true, BufferSize: 32 * 1024),
            enabled: true,
            revision: 1,
            createdAtUtc: Start,
            updatedAtUtc: Start));
        Assert.Equal(SyncProfileWriteStatus.Succeeded, created.Status);

        var schedules = new SqliteSyncScheduleManagementRepository(_database, clock);
        var scheduleId = ScheduledSyncJobId.New();
        var scheduled = await schedules.CreateAsync(scheduleId, new SyncScheduleManagementDraft(
            syncProfileId,
            // Every five minutes, which is the shape of "set one for five minutes from now".
            "*/5 * * * *",
            "UTC",
            TimeSpan.FromMinutes(30),
            QueueOneWhileRunning: true,
            Enabled: true,
            SyncScheduleExecutionMode.SafeAutomatic));
        Assert.Equal(SyncScheduleManagementMutationStatus.Applied, scheduled.Status);

        var connector = new LabSyncEndpointConnector(new Dictionary<ConnectionProfileId, IStorageEndpointSession>
        {
            [_sourceProfileId] = source.Session,
            [_destinationProfileId] = destination.Session
        });
        var orchestration = new SyncOrchestrationService(
            profiles,
            new SqliteSyncBaselineStore(_database),
            new SqliteSyncPlanStore(_database),
            new SqliteSyncRunStore(_database),
            new SqliteSyncConflictStore(_database),
            connector,
            timeProvider: clock);
        var outboxStore = new SqliteReliableOutboxStore(_database);
        var processor = new SyncOutboxEventProcessor(
            orchestration,
            profiles,
            new SqliteSyncPlanStore(_database),
            new SqliteSyncExecutionStore(_database, clock),
            connector,
            new SqliteAuditEventStore(_database),
            timeProvider: clock);
        await using var outbox = new SyncOutboxAgentSubsystem(outboxStore, processor);
        await using var scheduler = new SchedulerAgentSubsystem(
            new SqliteScheduledSyncJobStore(_database, clock),
            new DurableScheduledSyncJobRunner(new SqliteScheduledSyncDispatchStore(_database, clock)),
            timeProvider: clock);

        Assert.True((await scheduler.InitializeAsync(CancellationToken.None)).IsReady);
        Assert.True((await outbox.InitializeAsync(CancellationToken.None)).IsReady);

        // Nothing is due yet, so nothing may be dispatched.
        await scheduler.RunDueOnceAsync();
        Assert.False(await outbox.RunClaimOnceAsync());

        // Five minutes later the occurrence is due.
        clock.Advance(TimeSpan.FromMinutes(5));
        await scheduler.RunDueOnceAsync();

        // Drain the chain: preview -> safe-automatic approval -> apply.
        var drained = 0;
        while (await outbox.RunClaimOnceAsync())
        {
            drained++;
            Assert.True(drained < 10, "The outbox did not settle.");
        }

        Assert.True(drained >= 2, $"Expected a preview and an apply, drained {drained}.");
        Assert.Equal(0, outbox.DeadLetterCount);

        // The only claim that matters: the bytes are on the destination.
        Assert.Equal(payload, await ReadAsync(destination, $"{destinationRoot}/scheduled.bin"));
    }

    private sealed class LabSyncEndpointConnector(
        IReadOnlyDictionary<ConnectionProfileId, IStorageEndpointSession> sessions) : ISyncEndpointConnector
    {
        public ValueTask<StorageResult<ISyncEndpointConnection>> OpenAsync(
            ConnectionProfileId profileId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(sessions.TryGetValue(profileId, out var session)
                ? StorageResult<ISyncEndpointConnection>.Success(new LabConnection(session))
                : StorageResult<ISyncEndpointConnection>.Fail(new StorageFailure(
                    "sync.lab.unknown_profile",
                    StorageFailureKind.NotFound,
                    "The lab connector was not given a session for this profile.")));

        /// <summary>
        /// Non-owning: the orchestration opens and disposes a connection per phase, while these
        /// sessions belong to the test and must survive all of them.
        /// </summary>
        private sealed class LabConnection(IStorageEndpointSession session) : ISyncEndpointConnection
        {
            public IStorageEndpointSession Session => session;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }

    private async Task<RuntimeStorageConnection> RegisterSftpAsync(CodeLogicStorageSessionFactory factory)
    {
        var registration = await factory.RegisterAsync(
            _sourceProfileId,
            $"schedule-sftp-{Guid.NewGuid():N}",
            new SftpConnectionConfig
            {
                Enabled = true,
                Host = "127.0.0.1",
                Port = RequiredPort("STORAGEHUB_SYNCLAB_SFTP_PORT"),
                Root = RequiredValue("STORAGEHUB_SYNCLAB_SFTP_ROOT"),
                Username = RequiredValue("STORAGEHUB_SYNCLAB_SFTP_USERNAME"),
                AuthenticationMode = SftpAuthenticationMode.PrivateKey,
                PrivateKeyPath = RequiredValue("STORAGEHUB_SYNCLAB_SFTP_KEY_PATH"),
                PrivateKeyPassphrase = RequiredValue("STORAGEHUB_SYNCLAB_SFTP_KEY_PASSPHRASE"),
                HostKeyFingerprints = [RequiredValue("STORAGEHUB_SYNCLAB_SFTP_HOST_SHA256")],
                TimeoutSeconds = 30
            });
        Assert.True(registration.IsSuccess, Failure(registration.Error));
        _connections.Add(registration.Value);
        return registration.Value;
    }

    private async Task<RuntimeStorageConnection> RegisterS3Async(CodeLogicStorageSessionFactory factory)
    {
        var registration = await factory.RegisterAsync(
            _destinationProfileId,
            $"schedule-s3-{Guid.NewGuid():N}",
            new S3ConnectionConfig
            {
                Bucket = RequiredValue("STORAGEHUB_MINIO_BUCKET"),
                Prefix = $"schedule-lab/{Guid.NewGuid():N}",
                ServiceUrl = RequiredValue("STORAGEHUB_MINIO_ENDPOINT"),
                Region = "us-east-1",
                AuthenticationMode = S3AuthenticationMode.StaticCredentials,
                AccessKey = RequiredValue("STORAGEHUB_MINIO_ACCESS_KEY"),
                SecretKey = RequiredValue("STORAGEHUB_MINIO_SECRET_KEY"),
                ForcePathStyle = true,
                AllowInsecureHttp = true,
                TimeoutSeconds = 30,
                MaxRetries = 0,
                Enabled = true
            });
        Assert.True(registration.IsSuccess, Failure(registration.Error));
        _connections.Add(registration.Value);
        return registration.Value;
    }

    private async Task SeedConnectionAsync(ConnectionProfileId id, string name)
    {
        await using var writer = await Assert.IsType<SingleWriterSqliteDatabase>(_database)
            .AcquireWriterAsync();
        await using var command = writer.Connection.CreateCommand();
        command.CommandText = """
            INSERT INTO connection_profiles
            (profile_id, provider, display_name, tags_json, metadata_json, endpoint_json,
             authentication_json, operational_options_json, is_favorite, is_enabled, version,
             created_utc, updated_utc)
            VALUES ($id, 'local', $name, '[]', '{}', '{}', '{}', '{}', 0, 1, 1, $now, $now);
            """;
        _ = command.Parameters.AddWithValue("$id", id.ToString());
        _ = command.Parameters.AddWithValue("$name", name);
        _ = command.Parameters.AddWithValue(
            "$now", Start.ToString("O", CultureInfo.InvariantCulture));
        _ = await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateDirectoryTreeAsync(
        RuntimeStorageConnection connection,
        string relativePath)
    {
        var session = (CodeLogicStorageEndpointSession)connection.Session;
        var current = string.Empty;
        foreach (var segment in relativePath.Split('/'))
        {
            current = string.IsNullOrEmpty(current) ? segment : $"{current}/{segment}";
            var created = await session.CreateDirectoryAsync(StorageAddress.Create(
                session.ProfileId, session.RootIdentity, current).Value);
            Assert.True(created.IsSuccess, Failure(created.Error));
        }
    }

    private static async Task WriteAsync(
        RuntimeStorageConnection connection,
        string relativePath,
        byte[] payload)
    {
        var session = (CodeLogicStorageEndpointSession)connection.Session;
        var address = StorageAddress.Create(
            session.ProfileId, session.RootIdentity, relativePath).Value;
        var opened = await session.OpenWriteAsync(new StorageWriteRequest(
            address, StorageWriteMode.Overwrite, payload.LongLength));
        Assert.True(opened.IsSuccess, Failure(opened.Error));
        await using var handle = opened.Value;
        await handle.Content.WriteAsync(payload);
        var committed = await handle.CommitAsync();
        Assert.True(committed.IsSuccess, Failure(committed.Error));
    }

    private static async Task<byte[]> ReadAsync(
        RuntimeStorageConnection connection,
        string relativePath)
    {
        var session = (CodeLogicStorageEndpointSession)connection.Session;
        var address = StorageAddress.Create(
            session.ProfileId, session.RootIdentity, relativePath).Value;
        var opened = await session.OpenReadAsync(new StorageReadRequest(address));
        Assert.True(opened.IsSuccess, Failure(opened.Error));
        await using var source = opened.Value;
        using var destination = new MemoryStream();
        await source.CopyToAsync(destination);
        return destination.ToArray();
    }

    private static async Task EnsureFrameworkAsync()
    {
        await FrameworkGate.WaitAsync();
        try
        {
            if (_library is not null)
            {
                return;
            }

            var root = Path.Combine(Path.GetTempPath(), $"storagehub-schedule-cl-{Guid.NewGuid():N}");
            var initialization = await global::CodeLogic.CodeLogic.InitializeAsync(configure =>
            {
                configure.FrameworkRootPath = Path.Combine(root, "framework");
                configure.ApplicationRootPath = Path.Combine(root, "application");
                configure.AppVersion = "test";
                configure.HandleShutdownSignals = false;
            });
            Assert.True(initialization.Success);
            await Libraries.LoadAsync<StorageLibrary>();
            Libraries.OverrideConfig<StorageConfig>(
                "CL.Storage", "storage", configuration => configuration.Enabled = false);
            await global::CodeLogic.CodeLogic.ConfigureAsync();
            await global::CodeLogic.CodeLogic.StartAsync();

            // Never stopped: CodeLogic cannot be initialised twice in one process.
            _library = Libraries.Get<StorageLibrary>() ??
                throw new InvalidOperationException("CL.Storage was not registered by CodeLogic.");
        }
        finally
        {
            _ = FrameworkGate.Release();
        }
    }

    private static bool Required() => string.Equals(
        Environment.GetEnvironmentVariable("STORAGEHUB_REQUIRE_SYNC_LAB"), "1", StringComparison.Ordinal);

    private static string RequiredValue(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.Any(char.IsControl))
        {
            throw new InvalidOperationException($"{name} is missing or invalid.");
        }

        return value;
    }

    private static int RequiredPort(string name) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var port) && port is >= 1 and <= 65535
            ? port
            : throw new InvalidOperationException($"{name} is missing or invalid.");

    private static string Failure(StorageFailure? failure) => failure is null
        ? "The operation failed without a structured failure."
        : $"{failure.Code}: {failure.Message}";

    public async Task DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            await connection.DisposeAsync();
        }

        if (_testRoot.Length > 0 && Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }
}
