using StorageHub.Application.Connections;
using StorageHub.Domain.Identifiers;
using StorageHub.Infrastructure.Windows;
using StorageHub.Persistence;
using StorageHub.Persistence.Connections;
using StorageHub.Security;
using StorageHub.Testing;

namespace StorageHub.Agent.Windows.Tests;

/// <summary>
/// Switching how the agent is hosted changes where it looks for everything it owns, so the switch
/// has to bring the installation with it -- in both directions. It used to copy one way only, and
/// to copy the database in a way that carried none of its contents.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class AgentModeMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"storagehub-mode-migration-{Guid.NewGuid():N}");

    /// <summary>
    /// Connections held open for the life of a test, standing in for the running agent. SQLite
    /// checkpoints the write-ahead log and deletes it when the last connection closes, so a fixture
    /// that tidies up after itself hides the very state a migration has to cope with -- and would
    /// pass just as happily against the file copy that shipped.
    /// </summary>
    private readonly List<Microsoft.Data.Sqlite.SqliteConnection> _attached = [];

    /// <summary>
    /// The whole point: install the service, change your mind, and find your connections and their
    /// credentials where you left them. Anything written under the service used to be stranded in
    /// ProgramData the moment somebody switched back.
    /// </summary>
    [WindowsOnlyFact]
    public async Task An_installation_survives_a_round_trip_through_the_machine_location()
    {
        var user = Location("user", DpapiProtectionScope.CurrentUser);
        var machine = Location("machine", DpapiProtectionScope.LocalMachine);
        var profile = await SeedDatabaseAsync(user);
        var secret = await SeedSecretAsync(user, "correct horse battery staple"u8.ToArray());

        var outward = await AgentModeMigration.MigrateAsync(user, machine);
        Assert.True(outward.Succeeded, outward.Summary);
        Assert.True(outward.DatabaseCopied);
        Assert.Equal(1, outward.SecretsCopied);
        await AssertProfilePresentAsync(machine, profile.Id);
        Assert.Equal(
            "correct horse battery staple"u8.ToArray(),
            await ReadSecretAsync(machine, secret));

        // Something done while the service owned the installation, which the way home has to carry.
        var addedUnderService = await AddProfileAsync(machine, "Added under the service");

        // The service is stopped before the switch back, and the session agent has been retired
        // since the service took over, so neither side is open by the time the copy happens.
        DetachAll();
        var homeward = await AgentModeMigration.MigrateAsync(machine, user);
        Assert.True(homeward.Succeeded, homeward.Summary);
        await AssertProfilePresentAsync(user, profile.Id);
        await AssertProfilePresentAsync(user, addedUnderService.Id);
        Assert.Equal(
            "correct horse battery staple"u8.ToArray(),
            await ReadSecretAsync(user, secret));
    }

    /// <summary>
    /// The source is only ever read. Switching is reversible precisely because nothing is moved,
    /// so a failure halfway leaves the mode you were in completely intact.
    /// </summary>
    [WindowsOnlyFact]
    public async Task The_source_installation_is_left_exactly_as_it_was()
    {
        var user = Location("user", DpapiProtectionScope.CurrentUser);
        var machine = Location("machine", DpapiProtectionScope.LocalMachine);
        var profile = await SeedDatabaseAsync(user);
        var secret = await SeedSecretAsync(user, "kept"u8.ToArray());

        _ = await AgentModeMigration.MigrateAsync(user, machine);

        await AssertProfilePresentAsync(user, profile.Id);
        Assert.Equal("kept"u8.ToArray(), await ReadSecretAsync(user, secret));
    }

    /// <summary>
    /// A stale installation at the destination is set aside, not written over and not deleted. It
    /// is normally a copy from an earlier switch, and "stale" is a guess about somebody's data.
    /// </summary>
    [WindowsOnlyFact]
    public async Task What_the_destination_already_held_is_archived_rather_than_overwritten()
    {
        var user = Location("user", DpapiProtectionScope.CurrentUser);
        var machine = Location("machine", DpapiProtectionScope.LocalMachine);
        _ = await SeedDatabaseAsync(user);
        var stale = await SeedDatabaseAsync(machine, attach: false);

        var report = await AgentModeMigration.MigrateAsync(user, machine);

        Assert.NotNull(report.ArchivedDirectory);
        Assert.True(Directory.Exists(report.ArchivedDirectory));
        var archivedDatabase = Path.Combine(report.ArchivedDirectory!, "storagehub.db");
        Assert.True(File.Exists(archivedDatabase), archivedDatabase);
        var archived = await new SqliteConnectionProfileRepository(
            new SqliteDatabaseOptions(archivedDatabase, pooling: false)).GetAsync(stale.Id);
        Assert.NotNull(archived);
    }

    [WindowsOnlyFact]
    public async Task Migrating_a_location_onto_itself_changes_nothing()
    {
        var user = Location("user", DpapiProtectionScope.CurrentUser);
        var profile = await SeedDatabaseAsync(user);

        var report = await AgentModeMigration.MigrateAsync(user, user);

        Assert.True(report.Succeeded);
        Assert.False(report.DatabaseCopied);
        Assert.Null(report.ArchivedDirectory);
        await AssertProfilePresentAsync(user, profile.Id);
    }

    [WindowsOnlyFact]
    public async Task An_empty_source_is_reported_rather_than_failing()
    {
        var report = await AgentModeMigration.MigrateAsync(
            Location("absent", DpapiProtectionScope.CurrentUser),
            Location("machine", DpapiProtectionScope.LocalMachine));

        Assert.True(report.Succeeded);
        Assert.False(report.DatabaseCopied);
        Assert.Equal(0, report.SecretsCopied);
    }

    private AgentDataLocation Location(string name, DpapiProtectionScope scope) =>
        new(Path.Combine(_root, name), scope);

    private static string AgentDirectory(AgentDataLocation location) =>
        Path.Combine(location.DataRoot, "Agent");

    private static SqliteDatabaseOptions DatabaseOptions(AgentDataLocation location) =>
        new(Path.Combine(AgentDirectory(location), "storagehub.db"), pooling: false);

    private static VersionedFileSecretVault OpenVault(AgentDataLocation location) =>
        new(
            Path.Combine(AgentDirectory(location), "vault"),
            new WindowsDpapiProtector(location.Scope));

    /// <summary>
    /// Seeds a location with one saved connection. <paramref name="attach"/> models whether an
    /// agent is running against it: true for the location being migrated away from, false for the
    /// one being migrated into, which in production is never open -- its agent is stopped before
    /// the switch precisely so this is true.
    /// </summary>
    private async Task<ConnectionProfile> SeedDatabaseAsync(AgentDataLocation location, bool attach = true)
    {
        var options = DatabaseOptions(location);
        var initialization = await new StorageHubDatabaseInitializer(options).InitializeAsync();
        Assert.True(initialization.IsReady, initialization.Message);

        if (attach)
        {
            // Attached before the row is written, so the schema is already in the .db file and the
            // row that follows stays in the write-ahead log, as it does under a running agent.
            await AttachAsync(options);
        }

        return await AddProfileAsync(location, "Seeded " + Path.GetFileName(location.DataRoot));
    }

    /// <summary>
    /// Models the agent being stopped. The switch does this before migrating, which is what lets
    /// the destination's previous contents be moved aside at all.
    /// </summary>
    private void DetachAll()
    {
        foreach (var connection in _attached)
        {
            connection.Dispose();
        }

        _attached.Clear();
    }

    private async Task AttachAsync(SqliteDatabaseOptions options)
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = options.DatabasePath,
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString());
        await connection.OpenAsync();
        _attached.Add(connection);

        // Opening is not attaching: SQLite defers reading the header until the first statement, and
        // a connection that has run none does not hold the log open.
        await using var touch = connection.CreateCommand();
        touch.CommandText = "SELECT count(*) FROM sqlite_schema;";
        _ = await touch.ExecuteScalarAsync();
    }

    private static async Task<ConnectionProfile> AddProfileAsync(AgentDataLocation location, string name)
    {
        var profile = ConnectionProfile.Create(
            ConnectionProfileId.New(),
            new ConnectionProfileMetadata(name),
            new LocalEndpoint("C:\\Data"),
            new NoAuthentication(),
            new ConnectionOperationalOptions(
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(60),
                new ConnectionRetryPolicy(3, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(5)),
                proxy: null,
                new ConnectionBandwidthLimits(null, null),
                "utf-8"),
            DateTimeOffset.UtcNow);
        var write = await new SqliteConnectionProfileRepository(DatabaseOptions(location))
            .CreateAsync(profile);
        Assert.Equal(ConnectionProfileWriteStatus.Succeeded, write.Status);
        return profile;
    }

    private static async Task AssertProfilePresentAsync(
        AgentDataLocation location,
        ConnectionProfileId id)
    {
        var found = await new SqliteConnectionProfileRepository(DatabaseOptions(location)).GetAsync(id);
        Assert.NotNull(found);
    }

    private static async Task<SecretReference> SeedSecretAsync(
        AgentDataLocation location,
        byte[] secret)
    {
        using var vault = OpenVault(location);
        return (await vault.CreateAsync(secret)).Reference;
    }

    /// <summary>
    /// Reads through the destination's own protector, which is the half a file copy cannot do: a
    /// secret sealed with the user's key is unreadable to a service, and the other way round.
    /// </summary>
    private static async Task<byte[]> ReadSecretAsync(
        AgentDataLocation location,
        SecretReference reference)
    {
        using var vault = OpenVault(location);
        await using var lease = await vault.OpenAsync(reference);
        return lease.Memory.ToArray();
    }

    public void Dispose()
    {
        foreach (var connection in _attached)
        {
            connection.Dispose();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
