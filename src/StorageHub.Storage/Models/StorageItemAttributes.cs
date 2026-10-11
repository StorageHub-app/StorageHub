using StorageHub.Domain.Storage;

namespace StorageHub.Storage.Models;

/// <summary>
/// What a provider reports about one item beyond its listing: permissions, ownership, where a
/// link points and its times.
/// </summary>
/// <remarks>
/// Every value is optional because every provider reports a different subset: SFTP gives the mode
/// and numeric owner and group but no creation time or link target, FTP gives owner and group
/// names and the link target from its listing, a local folder gives the times and, outside
/// Windows, the mode. A value the provider did not report is null rather than a guess.
/// </remarks>
public sealed record StorageItemAttributes(
    StorageEntry Entry,
    int? UnixMode = null,
    string? Owner = null,
    string? Group = null,
    long? OwnerId = null,
    long? GroupId = null,
    string? LinkTarget = null,
    DateTimeOffset? CreatedUtc = null,
    DateTimeOffset? LastAccessedUtc = null);
