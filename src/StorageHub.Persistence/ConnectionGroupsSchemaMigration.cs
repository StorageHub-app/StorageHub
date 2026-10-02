using Microsoft.Data.Sqlite;

namespace StorageHub.Persistence;

/// <summary>
/// Keeps the connections panel's groups in the database, with each connection naming its own.
///
/// Until now the groups were the desktop's: an arrangement in its settings file, with a typed
/// folder path deciding where a new connection landed. A group is now a row with a unique name,
/// a place in the order, and an optional icon and colour, and a connection refers to one by id or,
/// with none, is Ungrouped. Removing a group sends its connections back to Ungrouped; the
/// repository does that itself so each one moves to a new version, and the foreign key's
/// <c>ON DELETE SET NULL</c> is only the backstop.
///
/// Nothing is filed by this migration. The desktop brings its old arrangement across once, through
/// the agent, because only it knows how somebody had dragged their connections around; the folder
/// path alone would undo all of that.
/// </summary>
public sealed class ConnectionGroupsSchemaMigration : IDatabaseMigration
{
    public const int SchemaVersion = 16;
    public int Version => SchemaVersion;
    public string Name => "connection-groups";

    /// <summary>The <c>application_settings</c> key recording that an old arrangement was brought in.</summary>
    public const string ImportedSettingKey = "connection-groups.legacy-arrangement-imported";

    public async ValueTask ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = SchemaSql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private const string SchemaSql = """
        CREATE TABLE connection_groups
        (
            group_id TEXT NOT NULL PRIMARY KEY,
            display_name TEXT NOT NULL COLLATE NOCASE,
            sort_order INTEGER NOT NULL,
            icon_key TEXT NULL,
            color_key TEXT NULL
                CHECK (color_key IS NULL OR (length(color_key) = 7 AND substr(color_key, 1, 1) = '#')),
            version INTEGER NOT NULL CHECK (version > 0),
            created_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL
        );

        CREATE UNIQUE INDEX ux_connection_groups_display_name
            ON connection_groups(display_name COLLATE NOCASE);
        CREATE INDEX ix_connection_groups_sort_order
            ON connection_groups(sort_order, display_name COLLATE NOCASE);

        ALTER TABLE connection_profiles
            ADD COLUMN group_id TEXT NULL REFERENCES connection_groups(group_id) ON DELETE SET NULL;
        CREATE INDEX ix_connection_profiles_group
            ON connection_profiles(group_id);
        """;
}
