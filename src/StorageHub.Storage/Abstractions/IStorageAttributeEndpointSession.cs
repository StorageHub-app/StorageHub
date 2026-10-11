using StorageHub.Contracts.Results;
using StorageHub.Domain.Storage;
using StorageHub.Storage.Models;

namespace StorageHub.Storage.Abstractions;

/// <summary>
/// Optional item attributes: reading permissions, ownership, link targets and times, and changing
/// the permission bits and the modified time.
/// </summary>
/// <remarks>
/// Reading is offered wherever the interface is; changing is gated on
/// <see cref="StorageHub.Domain.Capabilities.StorageFeature.Permissions"/> and
/// <see cref="StorageHub.Domain.Capabilities.StorageFeature.SetModifiedTime"/>, which callers must
/// test first. A provider that advertises a change can still refuse it for one item (an FTP
/// server without SITE CHMOD, an account that does not own the file), and says so in its failure.
/// </remarks>
public interface IStorageAttributeEndpointSession
{
    ValueTask<StorageResult<StorageItemAttributes>> GetAttributesAsync(
        StorageAddress address,
        CancellationToken cancellationToken = default);

    /// <summary>Sets the Unix permission bits, special bits included (at most octal 7777).</summary>
    ValueTask<StorageResult> SetPermissionsAsync(
        StorageAddress address,
        int unixMode,
        CancellationToken cancellationToken = default);

    /// <summary>Sets the modified time, leaving the access time as it is.</summary>
    ValueTask<StorageResult> SetModifiedTimeAsync(
        StorageAddress address,
        DateTimeOffset modifiedUtc,
        CancellationToken cancellationToken = default);
}
