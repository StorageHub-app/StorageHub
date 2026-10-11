using System.Globalization;
using Microsoft.Data.Sqlite;

namespace StorageHub.Persistence;

/// <summary>
/// Lets a connection profile's provider column say <c>webdav</c>.
///
/// The column has carried a CHECK naming the five providers of schema v1 since the start (SSH
/// clients are stored as <c>sftp</c> and told apart by their endpoint JSON). WebDAV is stored as
/// itself, so that a search by provider finds it, and so the CHECK has to allow it.
///
/// SQLite cannot alter a CHECK, and the usual way round -- copy the table, drop it, rename the
/// copy -- is not open to this one: a dozen tables refer to it, several with ON DELETE CASCADE, and
/// dropping it with foreign keys on, which they are on every connection, would delete their rows.
/// Widening a CHECK changes nothing on disk, though, so the table's stored definition is edited in
/// place, which is the procedure SQLite's own ALTER TABLE documentation gives for exactly this
/// kind of change: inside the migration's transaction, with the schema version bumped so every
/// other connection reloads the definition.
/// </summary>
public sealed class WebDavProviderSchemaMigration : IDatabaseMigration
{
    public const int SchemaVersion = 17;
    public int Version => SchemaVersion;
    public string Name => "webdav-provider";

    internal const string PreviousProviderCheck = "provider IN ('local', 's3', 'ftp', 'ftps', 'sftp')";
    internal const string ProviderCheck = "provider IN ('local', 's3', 'ftp', 'ftps', 'sftp', 'webdav')";

    public async ValueTask ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        var schemaVersion = Convert.ToInt64(
            await ScalarAsync(connection, transaction, "PRAGMA schema_version;", cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);

        await ExecuteAsync(connection, transaction, "PRAGMA writable_schema = ON;", cancellationToken).ConfigureAwait(false);
        try
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE sqlite_schema
                SET sql = replace(sql, $previous, $widened)
                WHERE type = 'table' AND name = 'connection_profiles';
                """;
            update.Parameters.AddWithValue("$previous", PreviousProviderCheck);
            update.Parameters.AddWithValue("$widened", ProviderCheck);
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await ExecuteAsync(
                connection,
                transaction,
                string.Create(CultureInfo.InvariantCulture, $"PRAGMA schema_version = {schemaVersion + 1};"),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await ExecuteAsync(connection, transaction, "PRAGMA writable_schema = OFF;", cancellationToken)
                .ConfigureAwait(false);
        }

        // A definition that did not read as expected is left alone by replace(), and the journal
        // would then record a migration that changed nothing. Saying so rolls the transaction back.
        await using var verify = connection.CreateCommand();
        verify.Transaction = transaction;
        verify.CommandText = """
            SELECT instr(sql, $widened) > 0
            FROM sqlite_schema
            WHERE type = 'table' AND name = 'connection_profiles';
            """;
        verify.Parameters.AddWithValue("$widened", ProviderCheck);
        if (Convert.ToInt64(await verify.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L,
                CultureInfo.InvariantCulture) != 1)
        {
            throw new InvalidOperationException(
                "The connection_profiles table does not have the provider check this migration widens.");
        }
    }

    private static async ValueTask<object?> ScalarAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
