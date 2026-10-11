using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// How much room the drive or server behind a pane's folder has, for the pane's status line.
/// </summary>
/// <remarks>
/// Only the free space is worth a line: a total with nothing free beside it says nothing about
/// whether a copy will fit, so <see cref="Describe"/> has nothing to say without one, and the
/// pane shows nothing rather than a guess.
/// </remarks>
public sealed record FolderSpace(long? TotalBytes, long? AvailableBytes)
{
    /// <summary>The line the pane shows, or null when the free space is not known.</summary>
    public static string? Describe(FolderSpace? space) => space switch
    {
        { AvailableBytes: { } free, TotalBytes: { } total } when free <= total =>
            Ui.Format(
                Ui.Pane.FreeSpaceOfTotalFormat,
                UiFormatting.FormatBytes(free),
                UiFormatting.FormatBytes(total)),
        { AvailableBytes: { } free } => Ui.Format(Ui.Pane.FreeSpaceFormat, UiFormatting.FormatBytes(free)),
        _ => null
    };

    /// <summary>
    /// What the agent reported, or null for a figure the pane cannot use. The agent has already
    /// checked these against the contract's bounds; this only drops what is no use here.
    /// </summary>
    public static FolderSpace? From(StorageSpaceDocument? document) =>
        document is { AvailableBytes: >= 0 }
            ? new FolderSpace(document.TotalBytes is >= 0 ? document.TotalBytes : null, document.AvailableBytes)
            : null;

    /// <summary>
    /// The space on the volume a local folder is on, or null when it cannot be read.
    /// </summary>
    /// <remarks>
    /// The volume is the mount point the folder is deepest under, not just its path root: on
    /// Linux and macOS everything is under "/", and a folder on a second disk or a network share
    /// mounted below it is on that volume, not the root's. On Windows the two are the same.
    /// Blocking -- a network drive can take seconds to answer -- so callers keep it off the UI
    /// thread.
    /// </remarks>
    public static FolderSpace? ForLocalFolder(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath)) return null;
        try
        {
            var full = Path.GetFullPath(directoryPath);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            DriveInfo? best = null;
            foreach (var drive in DriveInfo.GetDrives())
            {
                var root = drive.RootDirectory.FullName;
                if (!IsUnder(full, root, comparison)) continue;
                if (best is null || root.Length > best.RootDirectory.FullName.Length) best = drive;
            }

            return best is { IsReady: true }
                ? new FolderSpace(best.TotalSize, best.AvailableFreeSpace)
                : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static bool IsUnder(string path, string root, StringComparison comparison)
    {
        if (!path.StartsWith(root, comparison)) return false;
        if (path.Length == root.Length) return true;
        return Path.EndsInDirectorySeparator(root) ||
            path[root.Length] == Path.DirectorySeparatorChar ||
            path[root.Length] == Path.AltDirectorySeparatorChar;
    }
}
