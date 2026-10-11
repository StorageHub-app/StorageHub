using StorageHub.Contracts.Results;
using StorageHub.Domain.Storage;

namespace StorageHub.Storage.Abstractions;

/// <summary>
/// How much room the volume or quota behind a folder has. Any figure can be unknown: an SFTP
/// server reports what statvfs says, an FTP server only what its own extension does.
/// </summary>
/// <remarks>
/// Built through <see cref="Create"/>, which drops a figure that cannot be true -- a negative
/// count, or more free or used than there is in all -- rather than refusing the lot, because a
/// server that gets one figure wrong usually has the others right and the free space is the one
/// that matters.
/// </remarks>
public sealed record StorageSpace
{
    private StorageSpace(long? totalBytes, long? availableBytes, long? usedBytes)
    {
        TotalBytes = totalBytes;
        AvailableBytes = availableBytes;
        UsedBytes = usedBytes;
    }

    public long? TotalBytes { get; }

    /// <summary>What this connection can still write.</summary>
    public long? AvailableBytes { get; }

    public long? UsedBytes { get; }

    /// <summary>Whether anything at all is known.</summary>
    public bool IsKnown => TotalBytes is not null || AvailableBytes is not null || UsedBytes is not null;

    public static StorageSpace Create(long? totalBytes, long? availableBytes, long? usedBytes)
    {
        var total = totalBytes is >= 0 ? totalBytes : null;
        var available = availableBytes is >= 0 && (total is null || availableBytes <= total) ? availableBytes : null;
        var used = usedBytes is >= 0 && (total is null || usedBytes <= total) ? usedBytes : null;
        return new StorageSpace(total, available, used);
    }
}

/// <summary>
/// Optional endpoint capability for reading free space. Its presence says only that the session
/// can ask; a provider that has no answer fails with an unsupported failure.
/// </summary>
public interface IStorageSpaceSession
{
    ValueTask<StorageResult<StorageSpace>> GetSpaceAsync(
        StorageAddress address,
        CancellationToken cancellationToken = default);
}
