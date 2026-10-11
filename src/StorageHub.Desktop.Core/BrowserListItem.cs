using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop;

/// <summary>
/// A row in a browser pane, and the column it is sorted by.
/// </summary>
/// <remarks>
/// Both were declared at the bottom of BrowserPaneControl, which is a 3,600-line WinForms control -
/// so PagedListingIndex and WorkspaceModel, which are pure logic over these, could not leave the
/// shell either. They are plain data and belong with the rest of the presentation models.
///
/// The row stays a thin record on purpose: TableView virtualizes containers over a materialized
/// list, so constructing one must do no work. An icon is resolved lazily by the view and cached by
/// extension, never per row while scrolling.
/// </remarks>
public enum BrowserSortColumn
{
    Name,
    Size,
    Type,
    Modified,
    Status
}

public sealed record BrowserListItem(
    string Name,
    string Size,
    string Type,
    string Modified,
    string Status,
    string? Location = null,
    bool IsContainer = false,
    StorageItemKind Kind = StorageItemKind.Other,
    long? Length = null,
    string? NativeItemId = null,
    string? VersionId = null,
    string? EntityTag = null,
    DateTimeOffset? ModifiedUtc = null,
    bool IsParentNavigation = false,
    // What View > Hidden Files leaves out when it is off: a name starting with a dot, which is how
    // every server StorageHub speaks to marks one, or a file this computer marks hidden.
    bool IsHidden = false);
