using Microsoft.Data.Sqlite;

namespace StorageHub.Persistence;

/// <summary>
/// Lets a connection profile's provider column say <c>azureblob</c>, widening the CHECK that
/// <see cref="WebDavProviderSchemaMigration"/> left in the same way (<see cref="ProviderCheckWidening"/>),
/// so that an Azure Blob connection is stored as itself and found by its provider.
/// </summary>
public sealed class AzureBlobProviderSchemaMigration : IDatabaseMigration
{
    public const int SchemaVersion = 18;
    public int Version => SchemaVersion;
    public string Name => "azure-blob-provider";

    internal const string PreviousProviderCheck = WebDavProviderSchemaMigration.ProviderCheck;
    internal const string ProviderCheck = "provider IN ('local', 's3', 'ftp', 'ftps', 'sftp', 'webdav', 'azureblob')";

    public ValueTask ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken = default) =>
        ProviderCheckWidening.ApplyAsync(connection, transaction, PreviousProviderCheck, ProviderCheck, cancellationToken);
}
