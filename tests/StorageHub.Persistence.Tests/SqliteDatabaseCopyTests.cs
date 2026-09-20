using Microsoft.Data.Sqlite;
using StorageHub.Application.Connections;
using StorageHub.Domain.Identifiers;
using StorageHub.Persistence.Connections;
using Xunit;
using StorageHub.Testing;

namespace StorageHub.Persistence.Tests;

/// <summary>
/// Moving an installation between host modes copies this database, and copying it wrongly is
/// indistinguishable from copying it right until somebody opens the result and finds it empty.
/// </summary>
public sealed class SqliteDatabaseCopyTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"storagehub-db-copy-{Guid.NewGuid():N}");

    /// <summary>
    /// Connections held open for the life of a test, standing in for the running agent. SQLite
    /// checkpoints the write-ahead log and deletes it when the *last* connection closes, so a
    /// fixture that tidies up after itself quietly destroys the condition these tests are about.
    /// </summary>
    private readonly List<SqliteConnection> _attached = [];

    /// <summary>
    /// The regression, stated plainly. The database runs in write-ahead logging mode, so rows
    /// written while the agent holds it open live in the <c>-wal</c> sidecar rather than in the
    /// <c>.db</c> file. Switching to the Windows service copied the <c>.db</c> file alone, which is
    /// why the service came up with a valid, fully migrated and completely empty installation and
    /// reported that it had copied the database.
    /// </summary>
    [Fact]
    public async Task A_copy_carries_rows_that_are_still_only_in_the_write_ahead_log()
    {
        var source = Path.Combine(_directory, "source", "storagehub.db");
        var saved = await SeedProfileAsync(source);

        // Proves the fixture reproduces the shape of the bug rather than merely passing: the row is
        // outside the .db file at this point, so a copy of that file alone would not carry it.
        Assert.True(new FileInfo(source + "-wal").Length > 0);

        var destination = Path.Combine(_directory, "destination", "storagehub.db");
        await SqliteDatabaseCopy.CopyAsync(source, destination);

        var copied = await new SqliteConnectionProfileRepository(
            new SqliteDatabaseOptions(destination, pooling: false)).GetAsync(saved.Id);
        Assert.NotNull(copied);
        Assert.Equal(saved.Id, copied.Id);
    }

    /// <summary>
    /// The naive copy, against the same fixture, to keep the assertion above honest: if this ever
    /// stops failing, the write-ahead log is being checkpointed and the test above proves nothing.
    /// </summary>
    [Fact]
    public async Task Copying_only_the_database_file_loses_everything()
    {
        var source = Path.Combine(_directory, "source", "storagehub.db");
        var saved = await SeedProfileAsync(source);

        var destination = Path.Combine(_directory, "naive", "storagehub.db");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination);

        var copied = await new SqliteConnectionProfileRepository(
            new SqliteDatabaseOptions(destination, pooling: false)).GetAsync(saved.Id);
        Assert.Null(copied);
    }

    /// <summary>
    /// The copy is a plain, checkpointed file with nothing left in a sidecar, so the caller may
    /// move it, archive it, or hand it to a service that has never opened it.
    /// </summary>
    [Fact]
    public async Task A_copy_leaves_no_sidecars_behind()
    {
        var source = Path.Combine(_directory, "source", "storagehub.db");
        _ = await SeedProfileAsync(source);
        var destination = Path.Combine(_directory, "destination", "storagehub.db");

        await SqliteDatabaseCopy.CopyAsync(source, destination);

        foreach (var sidecar in SqliteDatabaseCopy.ResolveSidecarPaths(destination))
        {
            Assert.False(File.Exists(sidecar), sidecar);
        }
    }

    /// <summary>
    /// Whether an installation already at the destination is kept, archived or discarded is the
    /// caller's decision. Writing over one silently is the failure this area already had once.
    /// </summary>
    [Fact]
    public async Task An_occupied_destination_is_refused_rather_than_overwritten()
    {
        var source = Path.Combine(_directory, "source", "storagehub.db");
        _ = await SeedProfileAsync(source);
        var destination = Path.Combine(_directory, "destination", "storagehub.db");
        await SqliteDatabaseCopy.CopyAsync(source, destination);

        _ = await Assert.ThrowsAsync<IOException>(
            () => SqliteDatabaseCopy.CopyAsync(source, destination));
    }

    [Fact]
    public async Task A_missing_source_is_reported_rather_than_creating_an_empty_copy()
    {
        var destination = Path.Combine(_directory, "destination", "storagehub.db");

        _ = await Assert.ThrowsAsync<FileNotFoundException>(
            () => SqliteDatabaseCopy.CopyAsync(
                Path.Combine(_directory, "absent", "storagehub.db"),
                destination));

        Assert.False(File.Exists(destination));
    }

    /// <summary>
    /// Creates a real StorageHub database holding one saved connection, and leaves a connection
    /// attached so the row stays in the write-ahead log -- the state the database is always in
    /// while the agent is running, and therefore the state a migration has to cope with.
    /// </summary>
    private async Task<ConnectionProfile> SeedProfileAsync(string databasePath)
    {
        var options = new SqliteDatabaseOptions(databasePath, pooling: false);
        var initialization = await new StorageHubDatabaseInitializer(options).InitializeAsync();
        Assert.True(initialization.IsReady, initialization.Message);

        // Attached before the row is written, so the schema is already checkpointed into the .db
        // file and only the row that follows is left in the log.
        await AttachAsync(options);

        var profile = ConnectionProfile.Create(
            ConnectionProfileId.New(),
            new ConnectionProfileMetadata("Migration fixture"),
            new LocalEndpoint(TestPaths.LocalRoot),
            new NoAuthentication(),
            new ConnectionOperationalOptions(
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(60),
                new ConnectionRetryPolicy(3, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(5)),
                proxy: null,
                new ConnectionBandwidthLimits(null, null),
                "utf-8"),
            DateTimeOffset.UtcNow);
        var write = await new SqliteConnectionProfileRepository(options).CreateAsync(profile);
        Assert.Equal(ConnectionProfileWriteStatus.Succeeded, write.Status);
        return profile;
    }

    private async Task AttachAsync(SqliteDatabaseOptions options)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = options.DatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        _attached.Add(connection);

        // Opening is not attaching. SQLite defers reading the database header until the first
        // statement, so a connection that has never run one does not hold the write-ahead log open
        // and the next close still checkpoints it away.
        await using var touch = connection.CreateCommand();
        touch.CommandText = "SELECT count(*) FROM sqlite_schema;";
        _ = await touch.ExecuteScalarAsync();
    }

    public void Dispose()
    {
        foreach (var connection in _attached)
        {
            connection.Dispose();
        }

        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
