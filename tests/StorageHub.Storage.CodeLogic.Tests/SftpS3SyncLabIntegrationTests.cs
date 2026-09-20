using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using CL.Storage;
using CL.Storage.Configuration;
using CodeLogic;
using StorageHub.Contracts.Results;
using StorageHub.Domain.Identifiers;
using StorageHub.Domain.Storage;
using StorageHub.Storage.Abstractions;
using StorageHub.Storage.Models;
using StorageHub.Sync;
using StorageHub.Sync.Persistence;
using StorageHub.Transfers;

namespace StorageHub.Storage.CodeLogic.Tests;

/// <summary>
/// Drives the real sync engine between a live SFTP server and a live S3-compatible server.
///
/// <see cref="VmCrossProviderIntegrationTests"/> already covers this pairing, but it demands a
/// whole VM: an FTPS server as well, and SFTP reachable with a password. Neither is available on
/// a workstation running only sshd and MinIO, and password authentication is a shape StorageHub
/// itself does not offer for SFTP -- the connection editor only accepts an encrypted private key.
/// This class therefore exercises the same engine over the authentication StorageHub actually
/// ships, so the sync path can be verified against real servers locally.
///
/// Its SFTP settings carry a <c>SYNCLAB</c> prefix rather than reusing the <c>STORAGEHUB_SFTP_*</c>
/// names. Those belong to <see cref="SftpProviderIntegrationTests"/>, whose fixture treats any one
/// of them being set as "configured" and then throws over the rest -- so sharing the names would
/// make configuring this lab break that suite.
/// </summary>
[Collection(ProviderIntegrationFixtureGroup.Name)]
public sealed class SftpS3SyncLabIntegrationTests : IAsyncLifetime
{
    private readonly List<RuntimeStorageConnection> _connections = [];
    private string? _testRoot;

    public async Task InitializeAsync()
    {
        if (!Required())
        {
            return;
        }

        _testRoot = Path.Combine(Path.GetTempPath(), $"storagehub-sync-lab-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(_testRoot);
        _ = await CodeLogicTestFramework.EnsureStartedAsync();
    }

    [Fact]
    [Trait("Category", "SyncLabIntegration")]
    public async Task SyncEngineMirrorsSftpIntoS3AndPropagatesLaterChanges()
    {
        if (!Required())
        {
            return;
        }

        var endpoint = RequiredUri("STORAGEHUB_MINIO_ENDPOINT");
        var accessKey = RequiredValue("STORAGEHUB_MINIO_ACCESS_KEY");
        var secretKey = RequiredValue("STORAGEHUB_MINIO_SECRET_KEY");
        var bucket = RequiredValue("STORAGEHUB_MINIO_BUCKET");
        using (var administrator = new AmazonS3Client(
                   new BasicAWSCredentials(accessKey, secretKey),
                   new AmazonS3Config
                   {
                       ServiceURL = endpoint.AbsoluteUri,
                       AuthenticationRegion = "us-east-1",
                       ForcePathStyle = true,
                       MaxErrorRetry = 0
                   }))
        {
            try
            {
                await administrator.PutBucketAsync(new PutBucketRequest { BucketName = bucket });
            }
            catch (BucketAlreadyOwnedByYouException)
            {
                // The lab bucket outlives any single test process.
            }
        }

        var factory = new CodeLogicStorageSessionFactory(
            Libraries.Get<StorageLibrary>() ??
            throw new InvalidOperationException("Storage library unavailable."));
        var source = await RegisterSftpAsync(factory);
        var destination = await RegisterS3Async(factory, endpoint, accessKey, secretKey, bucket);

        var sourceHealth = await source.Connection.Session.CheckHealthAsync();
        Assert.True(sourceHealth.IsSuccess, Failure(sourceHealth.Error));
        var destinationHealth = await destination.Connection.Session.CheckHealthAsync();
        Assert.True(destinationHealth.IsSuccess, Failure(destinationHealth.Error));

        var runId = Guid.NewGuid().ToString("N");
        var sourceRoot = Address(source, $"sync-source/{runId}");
        var destinationRoot = Address(destination, $"sync-destination/{runId}");
        await CreateDirectoryTreeAsync(source.Connection.Session, sourceRoot);
        await CreateDirectoryTreeAsync(destination.Connection.Session, destinationRoot);

        var alpha = Payload(81_337, 241);
        var beta = Payload(19_777, 197);
        var gamma = Payload(41_123, 181);
        await CreateDirectoryTreeAsync(source.Connection.Session, Child(sourceRoot, "nested/deeper"));
        await CreateDirectoryTreeAsync(source.Connection.Session, Child(sourceRoot, "empty"));
        await WriteAsync(source.Connection.Session, Child(sourceRoot, "alpha.bin"), alpha);
        await WriteAsync(source.Connection.Session, Child(sourceRoot, "beta.bin"), beta);
        await WriteAsync(source.Connection.Session, Child(sourceRoot, "nested/deeper/gamma.bin"), gamma);

        var scanOptions = new SyncSnapshotScanOptions(
            pageSize: 2,
            portableHashMode: SyncPortableHashMode.AllFiles,
            maximumConcurrentHashes: 2);
        var leftScan = await SyncSnapshotScanner.ScanAsync(
            source.Connection.Session, sourceRoot, scanOptions);
        var rightScan = await SyncSnapshotScanner.ScanAsync(
            destination.Connection.Session, destinationRoot, scanOptions);
        Assert.True(leftScan.IsSuccess, Failure(leftScan.Error));
        Assert.True(rightScan.IsSuccess, Failure(rightScan.Error));

        var profileId = SyncProfileId.New();
        var now = DateTimeOffset.UtcNow;
        var profile = new SyncProfile(
            profileId,
            "Lab SFTP to S3",
            source.ProfileId,
            sourceRoot.CanonicalRelativePath,
            destination.ProfileId,
            destinationRoot.CanonicalRelativePath,
            SyncDirection.LeftToRight,
            SyncDeletionMode.Mirror,
            SyncConflictPolicy.Block,
            new DeletionSafetyPolicy(maximumDeletionCount: 100, maximumDeletionPercentage: 50),
            new TransferExecutionOptions(Overwrite: true, BufferSize: 32 * 1024),
            enabled: true,
            revision: 1,
            createdAtUtc: now,
            updatedAtUtc: now);

        var built = SyncPlanBuilder.Build(new SyncPlanBuildRequest(
            OperationPlanId.New(),
            profileId,
            baselineGeneration: 0,
            sourceRoot,
            destinationRoot,
            leftScan.Value,
            rightScan.Value,
            new Dictionary<string, SyncBaselineObservation>(),
            SyncDirection.LeftToRight,
            SyncDeletionMode.Mirror,
            now));
        Assert.True(built.IsSuccess, Failure(built.Error));
        Assert.Equal(3, built.Value.Plan.Operations.Count(operation =>
            operation.Kind == SyncPlanOperationKind.Copy));

        IReadOnlyDictionary<ConnectionProfileId, IStorageEndpointSession> sessions =
            new Dictionary<ConnectionProfileId, IStorageEndpointSession>
            {
                [source.ProfileId] = source.Connection.Session,
                [destination.ProfileId] = destination.Connection.Session
            };

        // Preview must touch nothing: the point of a dry run is that a wrong plan is survivable.
        var preview = await ExecutePlanAsync(
            built.Value, profile, sessions, SyncPlanExecutionMode.Preview);
        Assert.True(preview.IsSuccess, Failure(preview.Error));
        Assert.Equal(0, preview.Value.ExecutedOperations);

        var executed = await ExecutePlanAsync(
            built.Value, profile, sessions, SyncPlanExecutionMode.Execute);
        Assert.True(executed.IsSuccess, Failure(executed.Error));
        Assert.Equal(built.Value.Plan.Operations.Length, executed.Value.ExecutedOperations);
        Assert.Equal(
            alpha.LongLength + beta.LongLength + gamma.LongLength,
            executed.Value.BytesTransferred);
        Assert.Equal(alpha, await ReadAsync(
            destination.Connection.Session, Child(destinationRoot, "alpha.bin")));
        Assert.Equal(beta, await ReadAsync(
            destination.Connection.Session, Child(destinationRoot, "beta.bin")));
        Assert.Equal(gamma, await ReadAsync(
            destination.Connection.Session, Child(destinationRoot, "nested/deeper/gamma.bin")));

        var freshLeft = await SyncSnapshotScanner.ScanAsync(
            source.Connection.Session, sourceRoot, scanOptions);
        var freshRight = await SyncSnapshotScanner.ScanAsync(
            destination.Connection.Session, destinationRoot, scanOptions);
        Assert.True(freshLeft.IsSuccess, Failure(freshLeft.Error));
        Assert.True(freshRight.IsSuccess, Failure(freshRight.Error));
        var baseline = VerifiedSyncBaselineBuilder.Build(
            profile,
            built.Value.Plan,
            new SyncBaselineSnapshot(
                profileId,
                Generation: 0,
                Revision: 0,
                new Dictionary<string, SyncBaselineObservation>(),
                Sha256Digest: string.Empty,
                UpdatedAtUtc: now),
            freshLeft.Value,
            freshRight.Value);
        Assert.True(baseline.IsSuccess, Failure(baseline.Error));

        // A second run over an unchanged tree must plan nothing, or every scheduled sync would
        // re-upload the whole tree.
        var idle = SyncPlanBuilder.Build(new SyncPlanBuildRequest(
            OperationPlanId.New(),
            profileId,
            baselineGeneration: 1,
            sourceRoot,
            destinationRoot,
            freshLeft.Value,
            freshRight.Value,
            baseline.Value,
            SyncDirection.LeftToRight,
            SyncDeletionMode.Mirror,
            DateTimeOffset.UtcNow));
        Assert.True(idle.IsSuccess, Failure(idle.Error));
        Assert.DoesNotContain(idle.Value.Plan.Operations, operation =>
            operation.Kind is SyncPlanOperationKind.Copy or SyncPlanOperationKind.Delete);

        var updated = Payload(97_531, 233, descending: true);
        var added = Payload(27_777, 173);
        await WriteAsync(source.Connection.Session, Child(sourceRoot, "alpha.bin"), updated);
        var removed = await source.Connection.Session.DeleteAsync(new StorageDeleteRequest(
            Child(sourceRoot, "beta.bin")));
        Assert.True(removed.IsSuccess, Failure(removed.Error));
        await WriteAsync(source.Connection.Session, Child(sourceRoot, "delta.bin"), added);

        var changedLeft = await SyncSnapshotScanner.ScanAsync(
            source.Connection.Session, sourceRoot, scanOptions);
        var unchangedRight = await SyncSnapshotScanner.ScanAsync(
            destination.Connection.Session, destinationRoot, scanOptions);
        Assert.True(changedLeft.IsSuccess, Failure(changedLeft.Error));
        Assert.True(unchangedRight.IsSuccess, Failure(unchangedRight.Error));
        var changedPlan = SyncPlanBuilder.Build(new SyncPlanBuildRequest(
            OperationPlanId.New(),
            profileId,
            baselineGeneration: 1,
            sourceRoot,
            destinationRoot,
            changedLeft.Value,
            unchangedRight.Value,
            baseline.Value,
            SyncDirection.LeftToRight,
            SyncDeletionMode.Mirror,
            DateTimeOffset.UtcNow));
        Assert.True(changedPlan.IsSuccess, Failure(changedPlan.Error));
        Assert.Contains(changedPlan.Value.Plan.Operations, operation =>
            operation.Kind == SyncPlanOperationKind.Copy &&
            operation.Destination?.CanonicalRelativePath.EndsWith("/alpha.bin", StringComparison.Ordinal) == true);
        Assert.Contains(changedPlan.Value.Plan.Operations, operation =>
            operation.Kind == SyncPlanOperationKind.Copy &&
            operation.Destination?.CanonicalRelativePath.EndsWith("/delta.bin", StringComparison.Ordinal) == true);
        Assert.Contains(changedPlan.Value.Plan.Operations, operation =>
            operation.Kind == SyncPlanOperationKind.Delete &&
            operation.SourceOrTarget.CanonicalRelativePath.EndsWith("/beta.bin", StringComparison.Ordinal));

        var changedExecution = await ExecutePlanAsync(
            changedPlan.Value, profile, sessions, SyncPlanExecutionMode.Execute);
        Assert.True(changedExecution.IsSuccess, Failure(changedExecution.Error));
        Assert.Equal(updated, await ReadAsync(
            destination.Connection.Session, Child(destinationRoot, "alpha.bin")));
        Assert.Equal(added, await ReadAsync(
            destination.Connection.Session, Child(destinationRoot, "delta.bin")));
        var deleted = await destination.Connection.Session.GetEntryAsync(
            Child(destinationRoot, "beta.bin"));
        Assert.True(deleted.IsFailure);
        Assert.Equal(StorageFailureKind.NotFound, deleted.Error.Kind);
    }

    /// <summary>
    /// Every direction pair, not just SFTP to S3. Providers differ in what they can do to a
    /// destination -- object stores have no rename, SFTP has no conditional create -- so a mirror
    /// that works one way proves nothing about the other way.
    /// </summary>
    [Fact]
    [Trait("Category", "SyncLabIntegration")]
    public async Task SyncEngineMirrorsAcrossEveryDirectionPair()
    {
        if (!Required())
        {
            return;
        }

        var endpoint = RequiredUri("STORAGEHUB_MINIO_ENDPOINT");
        var accessKey = RequiredValue("STORAGEHUB_MINIO_ACCESS_KEY");
        var secretKey = RequiredValue("STORAGEHUB_MINIO_SECRET_KEY");
        var bucket = RequiredValue("STORAGEHUB_MINIO_BUCKET");
        var factory = new CodeLogicStorageSessionFactory(
            Libraries.Get<StorageLibrary>() ??
            throw new InvalidOperationException("Storage library unavailable."));

        var sftp = await RegisterSftpAsync(factory);
        var s3 = await RegisterS3Async(factory, endpoint, accessKey, secretKey, bucket);
        var local = await RegisterLocalAsync(factory);

        // Local and S3 destinations can be written safely, so they need no opt-in.
        await AssertMirrorAsync(sftp, local, "sftp-to-local");
        await AssertMirrorAsync(s3, local, "s3-to-local");
        await AssertMirrorAsync(local, s3, "local-to-s3");

        // An SFTP destination cannot: SFTP has no conditional create, so the executor refuses to
        // create the destination rather than race whatever might already be at that path. Syncing
        // onto an SFTP server is therefore opt-in, via the editor's "Allow non-atomic destination
        // writes" checkbox. Both halves are asserted so a regression either way is caught.
        await AssertMirrorRefusedWithoutOptInAsync(local, sftp, "local-to-sftp");
        await AssertMirrorAsync(local, sftp, "local-to-sftp-opt-in", allowNonAtomicDestinationWrites: true);
        await AssertMirrorAsync(s3, sftp, "s3-to-sftp-opt-in", allowNonAtomicDestinationWrites: true);
    }

    private static async Task AssertMirrorRefusedWithoutOptInAsync(
        RegisteredLabEndpoint source,
        RegisteredLabEndpoint destination,
        string label)
    {
        var failure = await RunMirrorAsync(source, destination, label, false);
        Assert.True(failure.IsFailure, $"{label} was expected to fail closed.");

        // The sync executor's preflight, not the transfer executor's mid-operation check. Both
        // refuse; the difference is when. Through the agent the transfer-layer refusal arrived
        // after the run had been approved and dispatched, so it surfaced as "provider state is
        // uncertain, reconciliation required" -- alarming, and untrue, because nothing had been
        // written. It is answered at plan time now, so the preview refuses and says what to do.
        Assert.Equal("sync.create.conditional_unsupported", failure.Error.Code);
    }

    private static async Task AssertMirrorAsync(
        RegisteredLabEndpoint source,
        RegisteredLabEndpoint destination,
        string label,
        bool allowNonAtomicDestinationWrites = false)
    {
        var executed = await RunMirrorAsync(source, destination, label, allowNonAtomicDestinationWrites);
        Assert.True(executed.IsSuccess, $"{label} execute: {Failure(executed.Error)}");
    }

    private static async Task<StorageResult<SyncPlanExecutionReport>> RunMirrorAsync(
        RegisteredLabEndpoint source,
        RegisteredLabEndpoint destination,
        string label,
        bool allowNonAtomicDestinationWrites)
    {
        var runId = Guid.NewGuid().ToString("N");
        var sourceRoot = Address(source, $"matrix-source/{label}/{runId}");
        var destinationRoot = Address(destination, $"matrix-destination/{label}/{runId}");
        await CreateDirectoryTreeAsync(source.Connection.Session, sourceRoot);
        await CreateDirectoryTreeAsync(destination.Connection.Session, destinationRoot);

        var first = Payload(11_311, 251);
        var nested = Payload(7_919, 149);
        await CreateDirectoryTreeAsync(source.Connection.Session, Child(sourceRoot, "inner"));
        await WriteAsync(source.Connection.Session, Child(sourceRoot, "one.bin"), first);
        await WriteAsync(source.Connection.Session, Child(sourceRoot, "inner/two.bin"), nested);

        var scanOptions = new SyncSnapshotScanOptions(
            pageSize: 4,
            portableHashMode: SyncPortableHashMode.AllFiles,
            maximumConcurrentHashes: 2);
        var leftScan = await SyncSnapshotScanner.ScanAsync(
            source.Connection.Session, sourceRoot, scanOptions);
        var rightScan = await SyncSnapshotScanner.ScanAsync(
            destination.Connection.Session, destinationRoot, scanOptions);
        Assert.True(leftScan.IsSuccess, $"{label} source scan: {Failure(leftScan.Error)}");
        Assert.True(rightScan.IsSuccess, $"{label} destination scan: {Failure(rightScan.Error)}");

        var profileId = SyncProfileId.New();
        var now = DateTimeOffset.UtcNow;
        var profile = new SyncProfile(
            profileId,
            $"Lab {label}",
            source.ProfileId,
            sourceRoot.CanonicalRelativePath,
            destination.ProfileId,
            destinationRoot.CanonicalRelativePath,
            SyncDirection.LeftToRight,
            SyncDeletionMode.Mirror,
            SyncConflictPolicy.Block,
            new DeletionSafetyPolicy(maximumDeletionCount: 100, maximumDeletionPercentage: 50),
            new TransferExecutionOptions(
                Overwrite: true,
                BufferSize: 32 * 1024,
                AllowNonAtomicDestinationWrites: allowNonAtomicDestinationWrites),
            enabled: true,
            revision: 1,
            createdAtUtc: now,
            updatedAtUtc: now);
        var built = SyncPlanBuilder.Build(new SyncPlanBuildRequest(
            OperationPlanId.New(),
            profileId,
            baselineGeneration: 0,
            sourceRoot,
            destinationRoot,
            leftScan.Value,
            rightScan.Value,
            new Dictionary<string, SyncBaselineObservation>(),
            SyncDirection.LeftToRight,
            SyncDeletionMode.Mirror,
            now));
        Assert.True(built.IsSuccess, $"{label} plan: {Failure(built.Error)}");
        Assert.Equal(2, built.Value.Plan.Operations.Count(operation =>
            operation.Kind == SyncPlanOperationKind.Copy));

        IReadOnlyDictionary<ConnectionProfileId, IStorageEndpointSession> sessions =
            new Dictionary<ConnectionProfileId, IStorageEndpointSession>
            {
                [source.ProfileId] = source.Connection.Session,
                [destination.ProfileId] = destination.Connection.Session
            };
        var executed = await ExecutePlanAsync(
            built.Value, profile, sessions, SyncPlanExecutionMode.Execute);
        if (executed.IsFailure)
        {
            return executed;
        }

        Assert.Equal(first.LongLength + nested.LongLength, executed.Value.BytesTransferred);
        Assert.Equal(first, await ReadAsync(
            destination.Connection.Session, Child(destinationRoot, "one.bin")));
        Assert.Equal(nested, await ReadAsync(
            destination.Connection.Session, Child(destinationRoot, "inner/two.bin")));
        return executed;
    }

    private async Task<RegisteredLabEndpoint> RegisterLocalAsync(CodeLogicStorageSessionFactory factory)
    {
        var profileId = ConnectionProfileId.New();
        var rootIdentity = $"lab-local-{Guid.NewGuid():N}";
        var root = Path.Combine(Assert.IsType<string>(_testRoot), $"local-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(root);
        var registration = await factory.RegisterLocalAsync(
            profileId,
            rootIdentity,
            new LocalConnectionConfig { RootPath = root, Enabled = true });
        Assert.True(registration.IsSuccess, Failure(registration.Error));
        _connections.Add(registration.Value);
        return new RegisteredLabEndpoint(profileId, rootIdentity, registration.Value);
    }

    private async Task<RegisteredLabEndpoint> RegisterSftpAsync(CodeLogicStorageSessionFactory factory)
    {
        var profileId = ConnectionProfileId.New();
        var rootIdentity = $"lab-sftp-{Guid.NewGuid():N}";
        var registration = await factory.RegisterAsync(profileId, rootIdentity, new SftpConnectionConfig
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
        return new RegisteredLabEndpoint(profileId, rootIdentity, registration.Value);
    }

    private async Task<RegisteredLabEndpoint> RegisterS3Async(
        CodeLogicStorageSessionFactory factory,
        Uri endpoint,
        string accessKey,
        string secretKey,
        string bucket)
    {
        var profileId = ConnectionProfileId.New();
        var rootIdentity = $"lab-s3-{Guid.NewGuid():N}";
        var registration = await factory.RegisterAsync(profileId, rootIdentity, new S3ConnectionConfig
        {
            Bucket = bucket,
            Prefix = $"sync-lab/{Guid.NewGuid():N}",
            ServiceUrl = endpoint.AbsoluteUri,
            Region = "us-east-1",
            AuthenticationMode = S3AuthenticationMode.StaticCredentials,
            AccessKey = accessKey,
            SecretKey = secretKey,
            ForcePathStyle = true,
            AllowInsecureHttp = true,
            TimeoutSeconds = 30,
            MaxRetries = 0,
            Enabled = true
        });
        Assert.True(registration.IsSuccess, Failure(registration.Error));
        _connections.Add(registration.Value);
        return new RegisteredLabEndpoint(profileId, rootIdentity, registration.Value);
    }

    private static async Task<StorageResult<SyncPlanExecutionReport>> ExecutePlanAsync(
        SyncPlanBuildResult plan,
        SyncProfile profile,
        IReadOnlyDictionary<ConnectionProfileId, IStorageEndpointSession> sessions,
        SyncPlanExecutionMode mode)
    {
        var approval = SyncExecutionApproval.Create(
            plan.Plan,
            sessions,
            plan.Snapshots,
            mode,
            profile.DeletionSafetyPolicy,
            profile.TransferOptions,
            profile.PolicySha256);
        return await SyncPlanExecutor.ExecuteAsync(new SyncPlanExecutionRequest(
            plan.Plan,
            approval,
            sessions,
            plan.Snapshots,
            mode,
            profile.DeletionSafetyPolicy,
            profile.TransferOptions,
            profile.PolicySha256));
    }

    private static byte[] Payload(int length, int modulus, bool descending = false) =>
        Enumerable.Range(0, length)
            .Select(index => descending ? (byte)(255 - index % modulus) : (byte)(index % modulus))
            .ToArray();

    private static async Task CreateDirectoryTreeAsync(
        CodeLogicStorageEndpointSession session,
        StorageAddress directory)
    {
        var current = string.Empty;
        foreach (var segment in directory.CanonicalRelativePath.Split('/'))
        {
            current = string.IsNullOrEmpty(current) ? segment : $"{current}/{segment}";
            var created = await session.CreateDirectoryAsync(StorageAddress.Create(
                session.ProfileId, session.RootIdentity, current).Value);
            Assert.True(created.IsSuccess, Failure(created.Error));
        }
    }

    private static async Task WriteAsync(
        CodeLogicStorageEndpointSession session,
        StorageAddress address,
        byte[] payload)
    {
        var opened = await session.OpenWriteAsync(new StorageWriteRequest(
            address, StorageWriteMode.Overwrite, payload.LongLength));
        Assert.True(opened.IsSuccess, Failure(opened.Error));
        await using var handle = opened.Value;
        await handle.Content.WriteAsync(payload);
        var committed = await handle.CommitAsync();
        Assert.True(committed.IsSuccess, Failure(committed.Error));
    }

    private static async Task<byte[]> ReadAsync(
        CodeLogicStorageEndpointSession session,
        StorageAddress address)
    {
        var opened = await session.OpenReadAsync(new StorageReadRequest(address));
        Assert.True(opened.IsSuccess, Failure(opened.Error));
        await using var source = opened.Value;
        using var destination = new MemoryStream();
        await source.CopyToAsync(destination);
        return destination.ToArray();
    }

    private static StorageAddress Address(RegisteredLabEndpoint endpoint, string path)
    {
        var address = StorageAddress.Create(endpoint.ProfileId, endpoint.RootIdentity, path);
        Assert.True(address.IsSuccess, Failure(address.Error));
        return address.Value;
    }

    private static StorageAddress Child(StorageAddress root, string relativePath) =>
        StorageAddress.Create(
            root.ProfileId,
            root.RootIdentity,
            $"{root.CanonicalRelativePath}/{relativePath}").Value;

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

    private static Uri RequiredUri(string name) =>
        Uri.TryCreate(RequiredValue(name), UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttp &&
        uri.IsLoopback
            ? uri
            : throw new InvalidOperationException($"{name} must be an HTTP loopback origin.");

    private static string Failure(StorageFailure? failure) => failure is null
        ? "The operation failed without a structured failure."
        : $"{failure.Code}: {failure.Message}";

    public async Task DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            await connection.DisposeAsync();
        }

        if (_testRoot is not null && Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    private sealed record RegisteredLabEndpoint(
        ConnectionProfileId ProfileId,
        string RootIdentity,
        RuntimeStorageConnection Connection);
}
