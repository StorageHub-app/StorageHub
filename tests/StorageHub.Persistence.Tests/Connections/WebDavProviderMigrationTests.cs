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
/// Schema v17: the provider column's CHECK gains <c>webdav</c>, edited in place, and nothing that
/// refers to a connection loses a row on the way.
/// </summary>
public sealed class WebDavProviderMigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"storagehub-webdav-migration-{Guid.NewGuid():N}");

    [Fact]
    public async Task A_v16_database_accepts_WebDav_profiles_and_keeps_every_row_that_names_a_connection()
    {
        Directory.CreateDirectory(_directory);
        var options = new SqliteDatabaseOptions(Path.Combine(_directory, "storagehub.db"), pooling: false);
        var v16 = await new StorageHubDatabaseInitializer(options,
        [
            new InitialSchemaMigration(), new SchedulerSchemaMigration(), new TransferQueueSchemaMigration(),
            new SchedulerCompletionSchemaMigration(), new SyncDurabilitySchemaMigration(),
            new SyncOrchestrationSchemaMigration(), new SyncExecutionSchemaMigration(),
            new PortableChecksumEvidenceSchemaMigration(), new SymmetricSyncSchemaMigration(),
            new SyncDestinationExistenceSchemaMigration(), new NonAtomicSyncWritesSchemaMigration(),
            new LocalTransferEndpointsSchemaMigration(), new KeyStoreSchemaMigration(),
            new OptionalKeyPassphraseSchemaMigration(), new OptionalSshKeyPassphraseSchemaMigration(),
            new ConnectionGroupsSchemaMigration()
        ]).InitializeAsync();
        Assert.Equal(ConnectionGroupsSchemaMigration.SchemaVersion, v16.SchemaVersion);

        var existing = Guid.NewGuid();
        await InsertProfileAsync(options, existing, "local", "Existing");
        await InsertTrustRecordAsync(options, existing);
        // v16 refuses the provider outright, which is why the migration exists.
        var refused = await Assert.ThrowsAsync<SqliteException>(
            () => InsertProfileAsync(options, Guid.NewGuid(), "webdav", "Too early"));
        Assert.Equal(19, refused.SqliteErrorCode);

        var upgraded = await new StorageHubDatabaseInitializer(options).InitializeAsync();
        Assert.True(upgraded.IsReady, upgraded.Message);
        Assert.Equal(AzureBlobProviderSchemaMigration.SchemaVersion, upgraded.SchemaVersion);

        // The table was not rebuilt, so the cascading trust record is still there.
        Assert.Equal(1L, await ScalarAsync(options, "SELECT count(*) FROM trust_records;"));
        Assert.Equal(1L, await ScalarAsync(options, "SELECT count(*) FROM connection_profiles;"));
        Assert.Equal("ok", await ScalarAsync(options, "PRAGMA integrity_check;"));
        Assert.Equal(0L, await ScalarAsync(options, "SELECT count(*) FROM pragma_foreign_key_check;"));

        var repository = new SqliteConnectionProfileRepository(options);
        var webDav = ConnectionProfile.Create(
            ConnectionProfileId.New(),
            new ConnectionProfileMetadata("Lab WebDAV"),
            new WebDavEndpoint(new Uri("https://127.0.0.1:8443/"), TlsCertificatePolicy.Pinned),
            new UsernamePasswordAuthentication("storagehub", SecretReference.Create()),
            new ConnectionOperationalOptions(
                TimeSpan.FromSeconds(30),
                TimeSpan.FromMinutes(5),
                new ConnectionRetryPolicy(0, TimeSpan.Zero, TimeSpan.Zero),
                proxy: null,
                new ConnectionBandwidthLimits(null, null),
                "utf-8"),
            DateTimeOffset.UtcNow);
        Assert.Equal(ConnectionProfileWriteStatus.Succeeded, (await repository.CreateAsync(webDav)).Status);
        Assert.Equal(webDav.Endpoint, (await repository.GetAsync(webDav.Id))!.Endpoint);

        // Only the one name was added: anything else is still refused.
        var bogus = await Assert.ThrowsAsync<SqliteException>(
            () => InsertProfileAsync(options, Guid.NewGuid(), "gopher", "Bogus"));
        Assert.Equal(19, bogus.SqliteErrorCode);
    }

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
