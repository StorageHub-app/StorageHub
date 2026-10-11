namespace StorageHub.Desktop;

/// <summary>
/// The folders directly inside a folder on this computer, for the folder tree's scan.
/// </summary>
/// <remarks>
/// Its own small read rather than the pane's browser: the browser is a navigation, with history and
/// a current folder, and a scan must not be one. Links are left out, so a scan can neither follow a
/// loop nor wander off the drive it started on, and a folder nobody may read is simply empty.
/// </remarks>
internal static class LocalFolderLister
{
    internal static IReadOnlyList<PaneFolder>? List(string path, int maximum, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length == 0 || !Path.IsPathFullyQualified(path)) return null;

        try
        {
            var folders = new List<PaneFolder>();
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                AttributesToSkip = FileAttributes.ReparsePoint,
                ReturnSpecialDirectories = false,
            };
            foreach (var directory in new DirectoryInfo(path).EnumerateDirectories("*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hidden = (directory.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 ||
                    directory.Name.StartsWith('.');
                folders.Add(new PaneFolder(directory.Name, directory.FullName, hidden));
                if (folders.Count >= maximum) break;
            }

            return folders;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
