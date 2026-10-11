using Microsoft.Data.Sqlite;

namespace StorageHub.Persistence;

/// <summary>
/// Lets a connection profile's provider column say <c>webdav</c>.
///
/// The column has carried a CHECK naming the five providers of schema v1 since the start (SSH
/// clients are stored as <c>sftp</c> and told apart by their endpoint JSON). WebDAV is stored as
/// itself, so that a search by provider finds it, and so the CHECK has to allow it. How the CHECK
/// is widened, and why it cannot be done by copying the table, is in
/// <see cref="ProviderCheckWidening"/>.
/// </summary>
public sealed class WebDavProviderSchemaMigration : IDatabaseMigration
{
    public const int SchemaVersion = 17;
    public int Version => SchemaVersion;
    public string Name => "webdav-provider";

    internal const string PreviousProviderCheck = "provider IN ('local', 's3', 'ftp', 'ftps', 'sftp')";
    internal const string ProviderCheck = "provider IN ('local', 's3', 'ftp', 'ftps', 'sftp', 'webdav')";

    public ValueTask ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken = default) =>
        ProviderCheckWidening.ApplyAsync(connection, transaction, PreviousProviderCheck, ProviderCheck, cancellationToken);
}
