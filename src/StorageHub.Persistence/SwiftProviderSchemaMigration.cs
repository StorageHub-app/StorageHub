using Microsoft.Data.Sqlite;

namespace StorageHub.Persistence;

/// <summary>
/// Lets a connection profile's provider column say <c>swift</c>, widening the CHECK that
/// <see cref="GoogleCloudStorageProviderSchemaMigration"/> left in the same way (<see cref="ProviderCheckWidening"/>),
/// so that an OpenStack Swift connection is stored as itself and found by its provider.
/// </summary>
public sealed class SwiftProviderSchemaMigration : IDatabaseMigration
{
    public const int SchemaVersion = 20;
    public int Version => SchemaVersion;
    public string Name => "openstack-swift-provider";

    internal const string PreviousProviderCheck = GoogleCloudStorageProviderSchemaMigration.ProviderCheck;
    internal const string ProviderCheck =
        "provider IN ('local', 's3', 'ftp', 'ftps', 'sftp', 'webdav', 'azureblob', 'gcs', 'swift')";

    public ValueTask ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken = default) =>
        ProviderCheckWidening.ApplyAsync(connection, transaction, PreviousProviderCheck, ProviderCheck, cancellationToken);
}
