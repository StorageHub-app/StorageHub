using Microsoft.Data.Sqlite;

namespace StorageHub.Persistence;

/// <summary>
/// Lets a connection profile's provider column say <c>gcs</c>, widening the CHECK that
/// <see cref="AzureBlobProviderSchemaMigration"/> left in the same way (<see cref="ProviderCheckWidening"/>),
/// so that a Google Cloud Storage connection is stored as itself and found by its provider.
/// </summary>
public sealed class GoogleCloudStorageProviderSchemaMigration : IDatabaseMigration
{
    public const int SchemaVersion = 19;
    public int Version => SchemaVersion;
    public string Name => "google-cloud-storage-provider";

    internal const string PreviousProviderCheck = AzureBlobProviderSchemaMigration.ProviderCheck;
    internal const string ProviderCheck =
        "provider IN ('local', 's3', 'ftp', 'ftps', 'sftp', 'webdav', 'azureblob', 'gcs')";

    public ValueTask ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken = default) =>
        ProviderCheckWidening.ApplyAsync(connection, transaction, PreviousProviderCheck, ProviderCheck, cancellationToken);
}
