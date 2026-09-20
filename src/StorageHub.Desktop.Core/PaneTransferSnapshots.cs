using StorageHub.Contracts.Results;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// Turns what a pane is showing into what <see cref="ManualTransferController"/> needs.
/// </summary>
/// <remarks>
/// <para>
/// One implementation for both shells. BrowserPaneControl had its own copy - CaptureTransferContext,
/// CaptureSelectionSnapshot, CaptureDestinationSnapshot and MapTransferItem, all private to a
/// WinForms control - which meant the Avalonia pane either grew a second copy or could not queue a
/// transfer at all. The two copies would then have had to agree about which rows are transferable,
/// what a destination is, and when a listing is complete enough to be one, forever.
/// </para>
/// <para>
/// None of it draws. A pane is a snapshot from the browser plus whichever rows are selected; that
/// is the whole input, and it is the same input on both platforms.
/// </para>
/// </remarks>
internal static class PaneTransferSnapshots
{
    /// <summary>Where a pane is, as a transfer endpoint.</summary>
    /// <remarks>
    /// The source answers, not this: whether the context says ThisPc or SavedConnection is a
    /// property of what the pane is pointed at, and asking it here is what lets a local-to-remote
    /// transfer work without anything above knowing it is one.
    /// </remarks>
    internal static StorageResult<PaneTransferContext> ContextFor(IPaneSource? source)
    {
        if (source is null)
        {
            return StorageResult<PaneTransferContext>.Fail(new StorageFailure(
                "manual_transfer.pane.not_connected",
                StorageFailureKind.Validation,
                Ui.Pane.SelectProfileToConnect));
        }

        return source.TransferContext();
    }

    /// <summary>
    /// The rows somebody chose, as the things to move.
    /// </summary>
    /// <remarks>
    /// The parent row is dropped rather than refused. It is part of the listing, so a select-all
    /// includes it, and failing the whole transfer because of a row that is not a file would be a
    /// surprising way to find that out.
    /// </remarks>
    internal static StorageResult<PaneSelectionSnapshot> SelectionFor(
        IPaneSource? source,
        IEnumerable<BrowserListItem> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);

        var context = ContextFor(source);
        if (context.IsFailure)
        {
            return StorageResult<PaneSelectionSnapshot>.Fail(context.Error);
        }

        var items = new List<PaneTransferItem>();
        foreach (var row in selected.Where(static row => !row.IsParentNavigation))
        {
            var mapped = ItemFor(row);
            if (mapped.IsFailure)
            {
                return StorageResult<PaneSelectionSnapshot>.Fail(mapped.Error);
            }

            items.Add(mapped.Value);
        }

        return PaneSelectionSnapshot.Create(context.Value, items);
    }

    /// <summary>
    /// Where the rows are going, and what is already there.
    /// </summary>
    /// <remarks>
    /// A destination has to know its own contents, because that is how a collision is detected
    /// before anything is queued. A listing that has not finished paging therefore cannot be one:
    /// a file just past the last page read would look absent and be silently overwritten.
    /// </remarks>
    internal static StorageResult<PaneDestinationSnapshot> DestinationFor(
        IPaneSource? source,
        IEnumerable<BrowserListItem> rows,
        bool hasMorePages)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var context = ContextFor(source);
        if (context.IsFailure)
        {
            return StorageResult<PaneDestinationSnapshot>.Fail(context.Error);
        }

        if (hasMorePages)
        {
            return StorageResult<PaneDestinationSnapshot>.Fail(new StorageFailure(
                "manual_transfer.destination.index_incomplete",
                StorageFailureKind.Conflict,
                Ui.Pane.FinishIndexingFirst));
        }

        var items = new List<PaneTransferItem>();
        foreach (var row in rows.Where(static row => !row.IsParentNavigation))
        {
            var mapped = ItemFor(row);
            if (mapped.IsFailure)
            {
                return StorageResult<PaneDestinationSnapshot>.Fail(mapped.Error);
            }

            items.Add(mapped.Value);
        }

        return PaneDestinationSnapshot.Create(context.Value, items);
    }

    /// <summary>
    /// One row, as something a transfer can name.
    /// </summary>
    /// <remarks>
    /// A row with no location is a message rather than a file -- "Loading connections", "No
    /// transfers in this view" -- and those are drawn as rows because a listing is the only surface
    /// a pane has. Refusing them here is what stops one being queued.
    /// </remarks>
    internal static StorageResult<PaneTransferItem> ItemFor(BrowserListItem row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (string.IsNullOrWhiteSpace(row.Location))
        {
            return StorageResult<PaneTransferItem>.Fail(new StorageFailure(
                "manual_transfer.pane.item_unavailable",
                StorageFailureKind.Validation,
                Ui.Pane.NotTransferable));
        }

        return PaneTransferItem.Create(
            row.Name,
            row.Location,
            row.Kind,
            row.Length,
            row.NativeItemId,
            row.VersionId,
            row.EntityTag);
    }
}
