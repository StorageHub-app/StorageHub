using System.Globalization;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// Turns a listing entry from the agent into the row a pane shows.
/// </summary>
/// <remarks>
/// <para>
/// Lifted out of BrowserPaneControl, where it was a lambda inside PresentRemoteSnapshot. It reads
/// nothing from a control and writes nothing to one: it is the projection from what the agent
/// reports to the five columns a pane draws, which both shells need to agree on exactly. A file
/// listed as 1.2 MB in one shell and 1,258,291 bytes in the other is the sort of difference nobody
/// notices until they are comparing two panes side by side, which is the whole point of this app.
/// </para>
/// <para>
/// The row is deliberately thin. TableView virtualizes containers over a materialized list, so
/// building one must do no work beyond formatting two numbers.
/// </para>
/// </remarks>
internal static class BrowserRowFactory
{
    /// <summary>A remote listing entry, as a row.</summary>
    internal static BrowserListItem FromRemote(StorageListItem entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new BrowserListItem(
            entry.Name,
            entry.Size is null ? string.Empty : UiFormatting.FormatBytes(entry.Size.Value),
            DescribeRemoteType(entry),
            entry.LastModifiedUtc is null
                ? string.Empty
                : entry.LastModifiedUtc.Value.LocalDateTime.ToString("g", CultureInfo.CurrentCulture),
            string.Empty,
            entry.RelativePath,
            entry.IsContainer,
            entry.Kind,
            entry.Size,
            entry.NativeItemId,
            entry.VersionId,
            entry.EntityTag,
            entry.LastModifiedUtc);
    }

    /// <summary>
    /// A local listing entry, as a row.
    /// </summary>
    /// <remarks>
    /// The same five columns as a remote one, so the two panes of a workspace line up when one is
    /// a folder on this computer and the other is a bucket. The local browser has already worked
    /// out the type and the status -- a drive's free space, a folder nobody may read -- so this
    /// only formats the size and the timestamp.
    /// </remarks>
    internal static BrowserListItem FromLocal(LocalBrowserEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new BrowserListItem(
            entry.Name,
            entry.Length is null ? string.Empty : UiFormatting.FormatBytes(entry.Length.Value),
            entry.Type,
            LocalBrowserPresentation.FormatModified(entry.Modified),
            entry.Status,
            entry.FullPath,
            entry.IsContainer,
            entry.IsContainer ? StorageItemKind.Directory : StorageItemKind.File,
            entry.Length,
            ModifiedUtc: entry.Modified);
    }

    /// <summary>
    /// What the Type column says.
    /// </summary>
    /// <remarks>
    /// A file's content type when the provider reported one, because "image/png" from S3 is more
    /// use than guessing from ".png" -- and the extension when it did not, which is every file over
    /// SFTP. Prefix is distinct from Folder on purpose: an S3 prefix is not a directory and behaves
    /// differently when you delete what is under it.
    /// </remarks>
    internal static string DescribeRemoteType(StorageListItem entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Kind switch
        {
            StorageItemKind.Directory => Ui.Pane.Folder,
            StorageItemKind.Prefix => Ui.Pane.ColumnPrefix,
            StorageItemKind.SymbolicLink => Ui.Pane.SymbolicLink,
            StorageItemKind.File when !string.IsNullOrWhiteSpace(entry.ContentType) => entry.ContentType,
            StorageItemKind.File => LocalBrowserPresentation.DescribeFileType(Path.GetExtension(entry.Name)),
            _ => Ui.Pane.StorageItem
        };
    }
}
