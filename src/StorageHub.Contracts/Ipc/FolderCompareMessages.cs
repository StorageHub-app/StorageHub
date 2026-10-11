using System.Text.Json.Serialization;

namespace StorageHub.Contracts.Ipc;

/// <summary>
/// The folder comparison contract: compare two folders in the agent, then read the result in pages.
/// </summary>
/// <remarks>
/// A comparison is started, polled and read rather than answered in one reply, for two reasons.
/// Listing two trees and checksumming what is in both can take far longer than one request is
/// allowed, and a result for twenty thousand paths is bigger than one frame should carry. The agent
/// keeps a finished result for a while after it was last read, and drops it when told to.
/// </remarks>
public static class FolderCompareIpcContract
{
    public const int CurrentVersion = 1;

    public static bool IsSupported(int version) => version == CurrentVersion;
}

public static class FolderCompareIpcLimits
{
    /// <summary>The most paths under either folder, subfolders included.</summary>
    public const int MaximumEntries = 20_000;

    /// <summary>The most entries one page carries.</summary>
    public const int MaximumPageSize = 500;

    /// <summary>The most comparisons the agent keeps at once, running or finished.</summary>
    public const int MaximumRetained = 8;

    public const int MaximumRelativePathLength = TransferQueueIpcLimits.MaximumRelativePathLength;
}

public static class FolderCompareIpcMessageTypes
{
    public const string StartRequest = "folder-compare.start.request";
    public const string StartResponse = "folder-compare.start.response";
    public const string StatusRequest = "folder-compare.status.request";
    public const string StatusResponse = "folder-compare.status.response";
    public const string ReleaseRequest = "folder-compare.release.request";
    public const string ReleaseResponse = "folder-compare.release.response";
}

/// <summary>
/// Compares the folder <paramref name="Left"/> names with the one <paramref name="Right"/> names.
/// </summary>
/// <param name="Left">
/// A saved connection's folder, or a folder on this PC encoded as <see cref="LocalTransferFolder"/>
/// encodes a queued transfer's, which is what a pane already hands the queue.
/// </param>
/// <param name="CompareContents">Whether files of the same size are also compared by SHA-256.</param>
public sealed record FolderCompareStartRequest(
    int ContractVersion,
    TransferQueueAddress Left,
    TransferQueueAddress Right,
    bool CompareContents = false)
{
    public bool HasValidBounds =>
        ContractVersion > 0 &&
        Left is not null && Left.HasValidBounds &&
        Right is not null && Right.HasValidBounds;
}

public sealed record FolderCompareStartResponse(
    int ContractVersion,
    Guid CompareId,
    StorageIpcFailure? Failure = null);

[JsonConverter(typeof(JsonStringEnumConverter<FolderCompareState>))]
public enum FolderCompareState
{
    Running,
    Completed,
    Failed
}

[JsonConverter(typeof(JsonStringEnumConverter<FolderCompareKind>))]
public enum FolderCompareKind
{
    OnlyLeft,
    OnlyRight,
    Different,
    Same
}

/// <summary>What told two items apart; the agent's <c>FolderComparisonReasons</c>, bit for bit.</summary>
[Flags]
public enum FolderCompareReasons
{
    None = 0,
    Size = 1,
    LeftNewer = 2,
    RightNewer = 4,
    Checksum = 8,
    Type = 16
}

/// <summary>Asks how a comparison is going and, once it has finished, for a page of what it found.</summary>
public sealed record FolderCompareStatusRequest(
    int ContractVersion,
    Guid CompareId,
    int Offset = 0,
    int PageSize = FolderCompareIpcLimits.MaximumPageSize)
{
    public bool HasValidBounds =>
        ContractVersion > 0 &&
        CompareId != Guid.Empty &&
        Offset is >= 0 and <= FolderCompareIpcLimits.MaximumEntries * 2 &&
        PageSize is >= 1 and <= FolderCompareIpcLimits.MaximumPageSize;
}

/// <summary>One side's item.</summary>
/// <param name="Sha256">Its SHA-256, when contents were compared and one could be had.</param>
/// <param name="NativeItemId">The provider's identities, so a copy over it is held to this version.</param>
public sealed record FolderCompareItem(
    StorageItemKind Kind,
    long? Size,
    DateTimeOffset? LastModifiedUtc,
    string? Sha256 = null,
    string? NativeItemId = null,
    string? VersionId = null,
    string? EntityTag = null)
{
    public bool HasValidBounds =>
        Enum.IsDefined(Kind) &&
        Size is null or >= 0 &&
        (Sha256 is null || Sha256.Length == 64 && Sha256.All(Uri.IsHexDigit)) &&
        IsBoundedOpaque(NativeItemId) &&
        IsBoundedOpaque(VersionId) &&
        IsBoundedOpaque(EntityTag);

    private static bool IsBoundedOpaque(string? value) => value is null ||
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= TransferQueueIpcLimits.MaximumOpaqueIdentityLength &&
        !value.Any(char.IsControl);
}

/// <summary>One path under either folder, relative to both, with <c>/</c> between its names.</summary>
/// <param name="RightRelativePath">
/// Where the item is, or would go, under the right folder when that is spelled differently: names
/// matched ignoring case keep each side's own spelling. Null when it is <paramref name="RelativePath"/>.
/// </param>
public sealed record FolderCompareEntry(
    string RelativePath,
    FolderCompareKind Kind,
    FolderCompareReasons Reasons,
    FolderCompareItem? Left,
    FolderCompareItem? Right,
    string? RightRelativePath = null)
{
    public bool HasValidBounds =>
        IsRelativePath(RelativePath) &&
        (RightRelativePath is null ||
            IsRelativePath(RightRelativePath) &&
            string.Equals(RightRelativePath, RelativePath, StringComparison.OrdinalIgnoreCase)) &&
        Enum.IsDefined(Kind) &&
        (Reasons & ~(FolderCompareReasons.Size | FolderCompareReasons.LeftNewer | FolderCompareReasons.RightNewer |
            FolderCompareReasons.Checksum | FolderCompareReasons.Type)) == 0 &&
        (Left is null || Left.HasValidBounds) &&
        (Right is null || Right.HasValidBounds) &&
        Kind switch
        {
            FolderCompareKind.OnlyLeft => Left is not null && Right is null,
            FolderCompareKind.OnlyRight => Left is null && Right is not null,
            FolderCompareKind.Different => Left is not null && Right is not null && Reasons != FolderCompareReasons.None,
            _ => Left is not null && Right is not null && Reasons == FolderCompareReasons.None
        };

    private static bool IsRelativePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        path.Length <= FolderCompareIpcLimits.MaximumRelativePathLength &&
        !path.Any(char.IsControl) &&
        !path.Contains('\\') &&
        !path.StartsWith('/') &&
        !path.EndsWith('/') &&
        !path.Split('/').Any(static name => name.Length == 0 || name is "." or "..");
}

/// <param name="TotalEntries">How many entries the finished comparison holds; zero while it runs.</param>
/// <param name="UncheckedPairs">Pairs whose contents were to be compared and could not be.</param>
/// <param name="CaseInsensitive">Whether names were matched ignoring case.</param>
public sealed record FolderCompareStatusResponse(
    int ContractVersion,
    Guid CompareId,
    FolderCompareState State,
    int TotalEntries,
    FolderCompareEntry[] Entries,
    int UncheckedPairs = 0,
    bool CaseInsensitive = false,
    StorageIpcFailure? Failure = null)
{
    public bool HasValidBounds =>
        ContractVersion > 0 &&
        CompareId != Guid.Empty &&
        Enum.IsDefined(State) &&
        TotalEntries is >= 0 and <= FolderCompareIpcLimits.MaximumEntries * 2 &&
        Entries is not null &&
        Entries.Length <= FolderCompareIpcLimits.MaximumPageSize &&
        Entries.All(static entry => entry is not null && entry.HasValidBounds) &&
        UncheckedPairs >= 0 &&
        (State == FolderCompareState.Failed) == (Failure is not null) &&
        (State == FolderCompareState.Completed || Entries.Length == 0 && TotalEntries == 0);
}

/// <summary>Lets the agent drop a comparison nobody will read again.</summary>
public sealed record FolderCompareReleaseRequest(int ContractVersion, Guid CompareId)
{
    public bool HasValidBounds => ContractVersion > 0 && CompareId != Guid.Empty;
}

public sealed record FolderCompareReleaseResponse(int ContractVersion, Guid CompareId, bool Released);
