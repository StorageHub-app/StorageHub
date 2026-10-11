using System.Text.Json.Serialization;

namespace StorageHub.Contracts.Ipc;

/// <summary>
/// The leftover cleanup contract: find the working files transfers left beside their destinations,
/// then remove the ones somebody chose.
/// </summary>
/// <remarks>
/// Two requests rather than one, so nothing is removed that was not first shown. The removal does
/// not trust the scan it follows: each file is looked at again, and one a transfer has since taken
/// up is left where it is.
/// </remarks>
public static class StagingCleanupIpcContract
{
    public const int CurrentVersion = 1;

    public static bool IsSupported(int version) => version == CurrentVersion;
}

public static class StagingCleanupIpcLimits
{
    /// <summary>The most leftovers one scan reports, and one removal names.</summary>
    public const int MaximumItems = 1_000;

    /// <summary>The most destination folders one scan looks in.</summary>
    public const int MaximumFolders = 64;

    public const int MaximumNameLength = StorageIpcLimits.MaximumItemNameLength;

    public const int MaximumFolderLength = TransferQueueIpcLimits.MaximumRelativePathLength;
}

public static class StagingCleanupIpcMessageTypes
{
    public const string ScanRequest = "staging-cleanup.scan.request";
    public const string ScanResponse = "staging-cleanup.scan.response";
    public const string RemoveRequest = "staging-cleanup.remove.request";
    public const string RemoveResponse = "staging-cleanup.remove.response";
}

/// <summary>Which working file a leftover is.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StagingLeftoverKind>))]
public enum StagingLeftoverKind
{
    /// <summary><c>.storagehub-&lt;key&gt;.partial</c>: what an interrupted copy had written, kept to resume it.</summary>
    ResumePartial,

    /// <summary><c>.storagehub-&lt;job&gt;.staging</c>: a whole copy waiting to be renamed into place.</summary>
    TransferStaging,

    /// <summary><c>.storagehub-write-*.tmp</c>: the empty file that tested whether a folder was writable.</summary>
    WriteProbe,

    /// <summary><c>.storagehub-rename-*.tmp</c>: a name held for a moment while a file was renamed.</summary>
    RenameTemporary
}

/// <summary>Looks for leftovers in the folders transfers have written to.</summary>
/// <param name="IncludeRemote">Whether saved connections are opened and looked in too, which is slower.</param>
public sealed record StagingCleanupScanRequest(int ContractVersion, bool IncludeRemote = true)
{
    public bool HasValidBounds => ContractVersion > 0;
}

/// <summary>One leftover file.</summary>
/// <param name="Address">
/// Where it is: a saved connection's root and path, or a folder on this PC encoded as a queued
/// transfer's is, with the file's name as the path. What a removal names it by.
/// </param>
/// <param name="Folder">The folder it is in, as somebody reads it: a full local path, or the path under the connection.</param>
/// <param name="InUse">
/// Whether a transfer may still want it: one not yet finished writes to that folder, or a sync is
/// running. Such a file is shown and never removed.
/// </param>
public sealed record StagingLeftover(
    TransferQueueAddress Address,
    StagingLeftoverKind Kind,
    string Name,
    string Folder,
    long? Size,
    DateTimeOffset? LastModifiedUtc,
    bool InUse)
{
    public bool HasValidBounds =>
        Address is not null && Address.HasValidBounds &&
        Enum.IsDefined(Kind) &&
        !string.IsNullOrWhiteSpace(Name) &&
        Name.Length <= StagingCleanupIpcLimits.MaximumNameLength &&
        !Name.Any(char.IsControl) &&
        !Name.Contains('/') && !Name.Contains('\\') &&
        Folder is not null &&
        Folder.Length <= StagingCleanupIpcLimits.MaximumFolderLength &&
        !Folder.Any(char.IsControl) &&
        Size is null or >= 0;
}

/// <param name="FoldersSearched">How many folders were looked in.</param>
/// <param name="FoldersUnreachable">How many could not be: a connection that would not open, a folder since removed.</param>
/// <param name="Truncated">Whether there were more leftovers, or more folders, than one scan reports.</param>
/// <param name="SyncRunning">Whether a sync was running, which leaves every leftover in use.</param>
public sealed record StagingCleanupScanResponse(
    int ContractVersion,
    StagingLeftover[] Items,
    int FoldersSearched,
    int FoldersUnreachable,
    bool Truncated,
    bool SyncRunning)
{
    public bool HasValidBounds =>
        ContractVersion > 0 &&
        Items is not null &&
        Items.Length <= StagingCleanupIpcLimits.MaximumItems &&
        Items.All(static item => item is not null && item.HasValidBounds) &&
        FoldersSearched is >= 0 and <= StagingCleanupIpcLimits.MaximumFolders &&
        FoldersUnreachable is >= 0 and <= StagingCleanupIpcLimits.MaximumFolders;
}

/// <summary>Removes the leftovers named, each only if it is still a leftover nobody is using.</summary>
public sealed record StagingCleanupRemoveRequest(int ContractVersion, TransferQueueAddress[] Items)
{
    public bool HasValidBounds =>
        ContractVersion > 0 &&
        Items is not null &&
        Items.Length is >= 1 and <= StagingCleanupIpcLimits.MaximumItems &&
        Items.All(static item => item is not null && item.HasValidBounds && item.RelativePath.Length > 0);
}

/// <param name="Removed">Whether it is gone; false with a failure saying why it was left.</param>
public sealed record StagingCleanupRemoveResult(
    TransferQueueAddress Address,
    bool Removed,
    long? Size = null,
    StorageIpcFailure? Failure = null);

public sealed record StagingCleanupRemoveResponse(int ContractVersion, StagingCleanupRemoveResult[] Results)
{
    public bool HasValidBounds =>
        ContractVersion > 0 &&
        Results is not null &&
        Results.Length <= StagingCleanupIpcLimits.MaximumItems &&
        Results.All(static result => result is not null && result.Address is not null &&
            result.Removed == (result.Failure is null));
}
