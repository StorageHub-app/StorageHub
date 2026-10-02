using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using StorageHub.Application.Connections;
using StorageHub.Domain.Identifiers;
using StorageHub.Persistence.Connections;
using StorageHub.Testing;
using Xunit;

namespace StorageHub.Persistence.Tests.Connections;

/// <summary>
/// Schema v16: the connections panel's groups move into the database, and no connection is lost on
/// the way in or at any point after.
/// </summary>
public sealed class ConnectionGroupsMigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"storagehub-groups-migration-{Guid.NewGuid():N}");

    /// <summary>
    /// A v15 database with connections in it upgrades with every connection Ungrouped; the old
    /// arrangement is brought in once; and making, renaming, recolouring, moving, filing and
    /// removing groups never deletes a connection, moving each one it touches to a new version.
    /// </summary>
    [Fact]
    public async Task A_v15_database_gains_groups_without_losing_a_connection()
    {
        var options = new SqliteDatabaseOptions(Path.Combine(_directory, "storagehub.db"), pooling: false);
        var v15 = await new StorageHubDatabaseInitializer(options,
        [
            new InitialSchemaMigration(), new SchedulerSchemaMigration(), new TransferQueueSchemaMigration(),
            new SchedulerCompletionSchemaMigration(), new SyncDurabilitySchemaMigration(),
            new SyncOrchestrationSchemaMigration(), new SyncExecutionSchemaMigration(),
            new PortableChecksumEvidenceSchemaMigration(), new SymmetricSyncSchemaMigration(),
            new SyncDestinationExistenceSchemaMigration(), new NonAtomicSyncWritesSchemaMigration(),
            new LocalTransferEndpointsSchemaMigration(), new KeyStoreSchemaMigration(),
            new OptionalKeyPassphraseSchemaMigration(), new OptionalSshKeyPassphraseSchemaMigration()
        ]).InitializeAsync();
        Assert.Equal(OptionalSshKeyPassphraseSchemaMigration.SchemaVersion, v15.SchemaVersion);
        var studio = Guid.NewGuid();
        var scratch = Guid.NewGuid();
        await InsertV15ProfileAsync(options, studio, "Studio", "Team");
        await InsertV15ProfileAsync(options, scratch, "Scratch", null);

        var upgraded = await new StorageHubDatabaseInitializer(options).InitializeAsync();
        Assert.True(upgraded.IsReady, upgraded.Message);
        Assert.Equal(ConnectionGroupsSchemaMigration.SchemaVersion, upgraded.SchemaVersion);

        var profiles = new SqliteConnectionProfileRepository(options);
        var groups = new SqliteConnectionGroupRepository(options);
        Assert.All(
            await profiles.SearchAsync(new ConnectionProfileSearch(IncludeDisabled: true)),
            static profile => Assert.Null(profile.Metadata.GroupId));
        Assert.Empty(await groups.ListAsync());

        // The desktop's old arrangement, once: Team with Studio in it, and an empty group.
        Assert.True(await groups.ImportOnceAsync(
        [
            new ConnectionGroupImport("Team", "layers", null, [studio, Guid.NewGuid()]),
            new ConnectionGroupImport("Empty", null, null, [])
        ]));
        var team = (await groups.ListAsync())[0];
        Assert.Equal(["Team", "Empty"], (await groups.ListAsync()).Select(static group => group.Name));
        Assert.Equal("layers", team.IconKey);
        var filed = await profiles.GetAsync(new ConnectionProfileId(studio));
        Assert.Equal(team.Id, filed!.Metadata.GroupId);
        Assert.Equal(2, filed.Version);

        // A second arrangement, from this desktop again or another, changes nothing.
        Assert.False(await groups.ImportOnceAsync([new ConnectionGroupImport("Other", null, null, [scratch])]));
        Assert.Equal(2, (await groups.ListAsync()).Count);
        Assert.Null((await profiles.GetAsync(new ConnectionProfileId(scratch)))!.Metadata.GroupId);

        // Names are unique without regard to case; a group is renamed and recoloured in place.
        var archive = (await groups.CreateAsync("Archive")).Group!;
        Assert.Equal(2, archive.SortOrder);
        Assert.Equal(ConnectionGroupWriteStatus.NameConflict, (await groups.CreateAsync("team")).Status);
        var empty = (await groups.ListAsync()).Single(static group => group.Name == "Empty");
        var spare = (await groups.UpdateAsync(empty.Id, "Spare", "box", "#16a34a")).Group!;
        Assert.Equal(("Spare", "#16A34A", 2L), (spare.Name, spare.ColorKey, spare.Version));
        Assert.Equal(ConnectionGroupWriteStatus.NameConflict, (await groups.UpdateAsync(spare.Id, "ARCHIVE", null, null)).Status);

        // Moving renumbers the rest; filing moves a connection to a new version.
        await groups.MoveAsync(archive.Id, 0);
        Assert.Equal(["Archive", "Team", "Spare"], (await groups.ListAsync()).Select(static group => group.Name));
        Assert.Equal(2, await groups.AssignAsync(scratch, archive.Id));
        Assert.Null(await groups.AssignAsync(scratch, Guid.NewGuid()));

        // Removing a group keeps its connections, Ungrouped, each at a new version.
        Assert.Equal(ConnectionGroupWriteStatus.Succeeded, (await groups.DeleteAsync(archive.Id)).Status);
        var released = await profiles.GetAsync(new ConnectionProfileId(scratch));
        Assert.Null(released!.Metadata.GroupId);
        Assert.Equal(3, released.Version);

        // A save naming a group removed in the meantime files the connection as Ungrouped rather
        // than failing on the foreign key.
        var stale = released with { Metadata = released.Metadata with { GroupId = archive.Id } };
        var saved = await profiles.UpdateAsync(stale, released.Version);
        Assert.Equal(ConnectionProfileWriteStatus.Succeeded, saved.Status);
        Assert.Null((await profiles.GetAsync(new ConnectionProfileId(scratch)))!.Metadata.GroupId);
        Assert.Equal(2, (await profiles.SearchAsync(new ConnectionProfileSearch(IncludeDisabled: true))).Count);
    }

    /// <summary>A profile row as v15 wrote it, before profiles had a group column.</summary>
    private static async Task InsertV15ProfileAsync(SqliteDatabaseOptions options, Guid id, string name, string? folder)
    {
        await using var connection = new SqliteConnection($"Data Source={options.DatabasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO connection_profiles
            (profile_id, provider, display_name, folder_path, tags_json, metadata_json, endpoint_json,
             authentication_json, operational_options_json, is_favorite, is_enabled, version,
             created_utc, updated_utc, deleted_utc)
            VALUES ($id, 'local', $name, $folder, '[]', $metadata, $endpoint, '{"kind":"none"}', $options,
                    0, 1, 1, $now, $now, NULL);
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$folder", (object?)folder ?? DBNull.Value);
        command.Parameters.AddWithValue("$metadata", JsonSerializer.Serialize(new
        {
            displayName = name,
            folderPath = folder,
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
        command.Parameters.AddWithValue(
            "$now",
            new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero).ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
