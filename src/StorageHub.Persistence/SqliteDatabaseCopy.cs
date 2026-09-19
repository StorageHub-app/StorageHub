using Microsoft.Data.Sqlite;

namespace StorageHub.Persistence;

/// <summary>
/// Copies a StorageHub database to another location, with its contents.
///
/// Copying the <c>.db</c> file is not enough, and fails in the worst possible way: it succeeds.
/// The database runs in write-ahead logging mode, so everything written since the last checkpoint
/// lives in the <c>-wal</c> sidecar rather than in the <c>.db</c> file. A plain file copy therefore
/// produces a database that opens cleanly, passes its integrity check, and is empty -- which is how
/// switching to the service presented an installation with no connections, no keys, no schedules
/// and no history, and reported that it had copied the database.
///
/// Copying all three files together only moves the problem: they have to be captured at a single
/// instant, and a sequence of file copies cannot promise that while anything holds the database
/// open. SQLite's online backup does both at once -- it reads through the write-ahead log and
/// writes one consistent, fully checkpointed file, even with a writer attached.
/// </summary>
public static class SqliteDatabaseCopy
{
    /// <summary>The write-ahead log and shared-memory files that belong to a database.</summary>
    public static IReadOnlyList<string> ResolveSidecarPaths(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var full = Path.GetFullPath(databasePath);
        return [full + "-wal", full + "-shm"];
    }

    /// <summary>
    /// Writes a consistent copy of <paramref name="sourcePath"/> at <paramref name="destinationPath"/>.
    ///
    /// The destination must not exist, sidecars included. Deciding what to do with a database that
    /// is already there -- keep it, archive it, refuse -- belongs to the caller, and silently
    /// writing over one is not a decision this should make on its own.
    /// </summary>
    public static async Task CopyAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        // Pooling off on both sides: a pooled connection can outlive its Dispose, and the caller's
        // next step is usually to move these files around.
        var source = new SqliteDatabaseOptions(sourcePath, pooling: false);
        var destination = new SqliteDatabaseOptions(destinationPath, pooling: false);
        if (!File.Exists(source.DatabasePath))
        {
            throw new FileNotFoundException("The database to copy was not found.", source.DatabasePath);
        }

        foreach (var occupied in ResolveSidecarPaths(destination.DatabasePath).Prepend(destination.DatabasePath))
        {
            if (File.Exists(occupied))
            {
                throw new IOException($"'{occupied}' already exists; the destination must be empty.");
            }
        }

        _ = Directory.CreateDirectory(Path.GetDirectoryName(destination.DatabasePath)!);

        // Read-write rather than read-only: recovering a write-ahead log needs to write the
        // shared-memory file, and a read-only open of a database with an unrecovered log fails
        // rather than reading through it -- which is exactly the state a copy has to handle.
        await using var sourceConnection = new SqliteConnection(
            SqliteConnectionConfiguration.BuildConnectionString(source, SqliteOpenMode.ReadWrite));
        await sourceConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqliteConnectionConfiguration
            .ApplyPerConnectionSettingsAsync(sourceConnection, source, cancellationToken)
            .ConfigureAwait(false);

        await using var destinationConnection = new SqliteConnection(
            SqliteConnectionConfiguration.BuildConnectionString(destination, SqliteOpenMode.ReadWriteCreate));
        await destinationConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        sourceConnection.BackupDatabase(destinationConnection);
    }
}
