using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// A selection staged in one pane, waiting to be pasted into another.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes a file manager work with more than two panes. With two, "copy" can mean
/// "to the other one", because there is only one other one. With three or four that phrase stops
/// naming anything, so the operation splits in half: a pane stages what is selected, and a later
/// paste says where it lands. The 1.x shell worked this way with two panes already, which is why
/// growing to four costs a layout and not a redesign.
/// </para>
/// <para>
/// It holds a <see cref="PaneSelectionSnapshot"/> rather than a live pane, so the source can be
/// navigated away, re-pointed at another connection or closed entirely between the copy and the
/// paste. What was staged is what was staged.
/// </para>
/// </remarks>
/// <param name="SourceName">
/// What the source pane was called when the staging happened, for the confirmation to name it.
/// Carried rather than looked up, for the same reason the selection is.
/// </param>
public sealed record PaneClipboard(
    PaneSelectionSnapshot Selection,
    TransferQueueOperation Operation,
    string SourceName)
{
    public bool IsMove => Operation == TransferQueueOperation.Move;

    public int Count => Selection.Items.Count;

    /// <summary>"3 items" or the single item's name, the way the review dialog says it.</summary>
    public string ItemSummary => Count == 1
        ? Selection.Items[0].Name
        : Ui.Format(Ui.Dialogs.SelectedItemsFormat, Count);

    /// <summary>"Copying 3 items from Studio Assets", for the pane to show while it is staged.</summary>
    public string Describe() => Ui.Format(
        IsMove ? Ui.Pane.StagedMoveFormat : Ui.Pane.StagedCopyFormat,
        ItemSummary,
        SourceName);
}

/// <summary>
/// Which of copy and move a selection is actually eligible for.
/// </summary>
/// <remarks>
/// Both shells ask this, and both got it from the agent refusing the request -- which is the
/// expensive way to find out, because by then the pane has already said it was working. Asking
/// here is the difference between a button that is dim and one that fails after it is pressed.
/// </remarks>
public static class PaneClipboardRules
{
    /// <summary>Anything that is not the way back out of the folder.</summary>
    public static IReadOnlyList<BrowserListItem> Transferable(IEnumerable<BrowserListItem> rows) =>
        [.. (rows ?? []).Where(static row => !row.IsParentNavigation)];

    /// <summary>Whether a copy could be staged from these rows.</summary>
    public static bool CanCopy(IEnumerable<BrowserListItem> rows) => Transferable(rows).Count > 0;

    /// <summary>
    /// Whether a move could be staged: files only, every one carrying a stable identity.
    /// </summary>
    /// <remarks>
    /// A move deletes the source, so the agent will only run one against an object it can prove is
    /// the one it listed -- a version id or an entity tag. Providers differ on which they supply,
    /// so the same selection is movable on one connection and not on another. Folders are refused
    /// outright: a half-moved tree is not something either end can recover from.
    /// </remarks>
    public static bool CanMove(IEnumerable<BrowserListItem> rows)
    {
        var chosen = Transferable(rows);
        return chosen.Count > 0 && chosen.All(static row =>
            !row.IsContainer && (row.VersionId is not null || row.EntityTag is not null));
    }

    /// <summary>Whether this operation can be staged from these rows at all.</summary>
    public static bool CanStage(IEnumerable<BrowserListItem> rows, TransferQueueOperation operation) =>
        operation == TransferQueueOperation.Move ? CanMove(rows) : CanCopy(rows);
}
