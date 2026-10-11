using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using StorageHub.Application.Connections;
using StorageHub.Domain.Identifiers;
using StorageHub.Persistence.Connections;
using StorageHub.Security;
using StorageHub.Testing;
using Xunit;

namespace StorageHub.Persistence.Tests.Connections;

/// <summary>
/// The schema versions after v17 that each add a cloud provider to the provider column's CHECK, in
/// place as WebDAV's did (<see cref="WebDavProviderMigrationTests"/>): the database one version
/// before refuses the name, the upgraded one stores the provider's profiles, nothing that refers to
/// a connection loses a row, and nothing else is let in.
/// </summary>
public sealed class CloudProviderMigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"storagehub-cloud-migration-{Guid.NewGuid():N}");

    public static TheoryData<string> Providers() => ["azureblob"];

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task The_version_before_refuses_the_provider_and_the_upgrade_stores_it_keeping_every_row(string provider)
    {
        Directory.CreateDirectory(_directory);
        var options = new SqliteDatabaseOptions(Path.Combine(_directory, "storagehub.db"), pooling: false);
        var (previous, profile) = Case(provider);
        var before = await new StorageHubDatabaseInitializer(options, previous).InitializeAsync();
        Assert.True(before.IsReady, before.Message);

        var existing = Guid.NewGuid();
        await InsertProfileAsync(options, existing, "local", "Existing");
        await InsertTrustRecordAsync(options, existing);
        var refused = await Assert.ThrowsAsync<SqliteException>(
            () => InsertProfileAsync(options, Guid.NewGuid(), provider, "Too early"));
        Assert.Equal(19, refused.SqliteErrorCode);

        var upgraded = await new StorageHubDatabaseInitializer(options).InitializeAsync();
        Assert.True(upgraded.IsReady, upgraded.Message);
        Assert.Equal(1L, await ScalarAsync(options, "SELECT count(*) FROM trust_records;"));
        Assert.Equal(1L, await ScalarAsync(options, "SELECT count(*) FROM connection_profiles;"));
        Assert.Equal("ok", await ScalarAsync(options, "PRAGMA integrity_check;"));
        Assert.Equal(0L, await ScalarAsync(options, "SELECT count(*) FROM pragma_foreign_key_check;"));

        var repository = new SqliteConnectionProfileRepository(options);
        Assert.Equal(ConnectionProfileWriteStatus.Succeeded, (await repository.CreateAsync(profile)).Status);
        var read = await repository.GetAsync(profile.Id);
        Assert.Equal(profile.Endpoint, read!.Endpoint);
        Assert.Equal(profile.Authentication, read.Authentication);
        Assert.Equal(provider, await ScalarAsync(options, "SELECT provider FROM connection_profiles WHERE display_name = 'Cloud';"));

        var bogus = await Assert.ThrowsAsync<SqliteException>(
            () => InsertProfileAsync(options, Guid.NewGuid(), "gopher", "Bogus"));
        Assert.Equal(19, bogus.SqliteErrorCode);
    }

    /// <summary>The migrations up to the one before the provider's, and a profile of that provider.</summary>
    private static (IDatabaseMigration[] Previous, ConnectionProfile Profile) Case(string provider) => provider switch
    {
        "azureblob" => (
            UpToWebDav(),
            Profile(
                new AzureBlobEndpoint("archive", allowInsecureHttp: true, rootPrefix: "team"),
                new AzureConnectionStringAuthentication(SecretReference.Create()))),
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    private static IDatabaseMigration[] UpToWebDav() =>
    [
        new InitialSchemaMigration(), new SchedulerSchemaMigration(), new TransferQueueSchemaMigration(),
        new SchedulerCompletionSchemaMigration(), new SyncDurabilitySchemaMigration(),
        new SyncOrchestrationSchemaMigration(), new SyncExecutionSchemaMigration(),
        new PortableChecksumEvidenceSchemaMigration(), new SymmetricSyncSchemaMigration(),
        new SyncDestinationExistenceSchemaMigration(), new NonAtomicSyncWritesSchemaMigration(),
        new LocalTransferEndpointsSchemaMigration(), new KeyStoreSchemaMigration(),
        new OptionalKeyPassphraseSchemaMigration(), new OptionalSshKeyPassphraseSchemaMigration(),
        new ConnectionGroupsSchemaMigration(), new WebDavProviderSchemaMigration()
    ];

    private static ConnectionProfile Profile(ConnectionEndpoint endpoint, ConnectionAuthentication authentication) =>
        ConnectionProfile.Create(
            ConnectionProfileId.New(),
            new ConnectionProfileMetadata("Cloud"),
            endpoint,
            authentication,
            new ConnectionOperationalOptions(
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30),
                new ConnectionRetryPolicy(3, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(5)),
                proxy: null,
                new ConnectionBandwidthLimits(null, null),
                "utf-8"),
            DateTimeOffset.UtcNow);

    private static async Task InsertProfileAsync(SqliteDatabaseOptions options, Guid id, string provider, string name)
    {
        await using var connection = await OpenAsync(options);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO connection_profiles
            (profile_id, provider, display_name, folder_path, tags_json, metadata_json, endpoint_json,
             authentication_json, operational_options_json, is_favorite, is_enabled, version,
             created_utc, updated_utc, deleted_utc)
            VALUES ($id, $provider, $name, NULL, '[]', $metadata, $endpoint, '{"kind":"none"}', $options,
                    0, 1, 1, $now, $now, NULL);
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$provider", provider);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$metadata", JsonSerializer.Serialize(new
        {
            displayName = name,
            tags = Array.Empty<string>(),
            isFavorite = false
        }));
        command.Parameters.AddWithValue("$endpoint", JsonSerializer.Serialize(new { kind = "local", rootPath = TestPaths.LocalRoot }));
        command.Parameters.AddWithValue("$options", JsonSerializer.Serialize(new
        {
            connectTimeoutTicks = TimeSpan.FromSeconds(10).Ticks,
            operationTimeoutTicks = TimeSpan.FromMinutes(2).Ticks,
            maximumAttempts = 3,
            initialRetryDelayTicks = TimeSpan.FromMilliseconds(250).Ticks,
            maximumRetryDelayTicks = TimeSpan.FromSeconds(5).Ticks,
            encodingName = "utf-8"
        }));
        command.Parameters.AddWithValue("$now", Now);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertTrustRecordAsync(SqliteDatabaseOptions options, Guid profileId)
    {
        await using var connection = await OpenAsync(options);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO trust_records
            (trust_id, profile_id, artifact_kind, canonical_host, port, algorithm, sha256_fingerprint,
             decision, decision_source, first_seen_utc, last_seen_utc)
            VALUES ($trust, $profile, 'tls-certificate', 'example.test', 443, 'x509',
                    $fingerprint, 'trusted', 'user', $now, $now);
            """;
        command.Parameters.AddWithValue("$trust", Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$profile", profileId.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$fingerprint", new string('A', 64));
        command.Parameters.AddWithValue("$now", Now);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(SqliteDatabaseOptions options, string sql)
    {
        await using var connection = await OpenAsync(options);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private static async Task<SqliteConnection> OpenAsync(SqliteDatabaseOptions options)
    {
        var connection = new SqliteConnection($"Data Source={options.DatabasePath};Pooling=False;Foreign Keys=True");
        await connection.OpenAsync();
        return connection;
    }

    private static string Now =>
        new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero).ToString("O", CultureInfo.InvariantCulture);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
