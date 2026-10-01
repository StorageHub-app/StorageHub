using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop.Views;

/// <summary>
/// What a pane drag carries, kept in the process rather than on the platform's clipboard.
/// </summary>
/// <remarks>
/// A drag's data crosses a platform boundary as strings and files, and a selection snapshot is
/// neither. The drag carries a token; this holds what the token names, and forgets it when the
/// drag ends. What another application can use travels beside the token: This PC's own paths,
/// or the drop broker's marker for a connection's rows on Windows.
/// </remarks>
internal static class PaneDragPayloads
{
    private static readonly ConcurrentDictionary<string, PaneDragPayload> Payloads = new(StringComparer.Ordinal);

    internal static string Register(PaneDragPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var token = Guid.NewGuid().ToString("N");
        Payloads[token] = payload;
        return token;
    }

    internal static PaneDragPayload? Find(string? token) =>
        token is not null && Payloads.TryGetValue(token, out var payload) ? payload : null;

    internal static void Release(string token) => Payloads.TryRemove(token, out _);
}

/// <summary>One pane's selection, on its way to another.</summary>
/// <param name="CanMove">
/// Whether the rows could be moved rather than copied, decided when the drag starts from the
/// same rules the Move button uses, because the rows may no longer be selected by the time it
/// lands.
/// </param>
internal sealed record PaneDragPayload(
    BrowserPaneModel Source,
    PaneSelectionSnapshot Selection,
    string SourceName,
    bool CanMove)
{
    /// <summary>
    /// Whether a StorageHub pane took it, which is how a drag out to Explorer that landed in
    /// StorageHub instead is told from one that went to Explorer, as 1.x's
    /// <c>InternalDropHandled</c> told them.
    /// </summary>
    internal bool Landed { get; set; }
}

/// <summary>
/// Dragging rows from one pane to another or out to the desktop, and files from the desktop into
/// a pane.
/// </summary>
/// <remarks>
/// <para>
/// Attached to the pane view rather than to the table, for the reason the connections panel's
/// handler is attached to the panel: a handler on the container survives the table rebuilding its
/// rows, and can see both the row the drag started on and the pane it landed in.
/// </para>
/// <para>
/// A drag starts from a press on a row that is already selected, so a click still selects and a
/// press-and-drag on the selection carries it. It lands as the same transfer a paste would --
/// same snapshots, same confirmation, same queue -- through the receiver the workspace gives each
/// pane. Copy unless Shift is held and the rows can be moved, as in 1.x.
/// </para>
/// <para>
/// Out of StorageHub, as 1.x's did: This PC's rows go as their own paths, so Explorer, Nautilus
/// or Dolphin copy them as they would any file. A connection's rows go to Explorer through the
/// drop broker (<see cref="ExplorerDragOut"/>), copy only, since a move would move the broker's
/// marker folder for real. Nothing else can take them: on Linux there is no broker, and a file
/// manager takes nothing that could stand in for a file not yet downloaded.
/// </para>
/// <para>
/// The list or the tree being dragged over is drawn in the selection colour while it would take
/// the drop, as 1.x drew it.
/// </para>
/// </remarks>
internal static class PaneDragHandler
{
    /// <summary>Our own format, scoped to this application.</summary>
    private static readonly DataFormat<string> Format =
        DataFormat.CreateStringApplicationFormat("StorageHub.PaneSelection.v1");

    /// <summary>How far the pointer moves before a press on a selected row becomes a drag.</summary>
    private const double DragThreshold = 4;

    /// <summary>The class that draws a list or tree as the place a drop would land.</summary>
    internal const string DropTargetClass = "drop-target";

    internal static void Attach(Control view, Func<BrowserPaneModel?> pane)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(pane);

        DragDrop.SetAllowDrop(view, true);

        // The press is remembered and the drag begins once the pointer has moved, not on the
        // press itself: a drag started on the press would swallow the second click of a
        // double-click on a selected row, which is how a folder is opened.
        PointerPressedEventArgs? pending = null;
        Point origin = default;

        view.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            pending = null;
            if (pane() is not { IsTerminal: false } model) return;
            if (e.ClickCount != 1 || !e.GetCurrentPoint(view).Properties.IsLeftButtonPressed) return;
            if (RowAt(e.Source) is not { IsParentNavigation: false } row) return;
            if (!model.SelectedRows.Contains(row)) return;

            pending = e;
            origin = e.GetPosition(view);
        }, RoutingStrategies.Tunnel);

        view.AddHandler(InputElement.PointerMovedEvent, (_, e) =>
        {
            if (pending is not { } press || pane() is not { } model) return;
            if (!e.GetCurrentPoint(view).Properties.IsLeftButtonPressed)
            {
                pending = null;
                return;
            }

            var moved = e.GetPosition(view) - origin;
            if (Math.Abs(moved.X) < DragThreshold && Math.Abs(moved.Y) < DragThreshold) return;

            pending = null;
            if (Payload(model) is not { } payload) return;

            _ = DragAsync(view, press, model, payload);
        }, RoutingStrategies.Tunnel);

        view.AddHandler(InputElement.PointerReleasedEvent, (_, _) => pending = null, RoutingStrategies.Tunnel);

        // Entering a list or tree lights it, leaving it puts it out. Moving from one to the other
        // is a leave and then an enter, in that order, so the light moves with the pointer.
        Control? lit = null;
        void Light(Control? target)
        {
            if (ReferenceEquals(lit, target)) return;
            lit?.Classes.Set(DropTargetClass, false);
            lit = target;
            lit?.Classes.Set(DropTargetClass, true);
        }

        void Over(DragEventArgs e)
        {
            var effect = EffectFor(e, pane());
            e.DragEffects = effect;
            e.Handled = true;
            Light(effect == DragDropEffects.None ? null : TargetFor(view, e.Source));
        }

        view.AddHandler(DragDrop.DragEnterEvent, (_, e) => Over(e));
        view.AddHandler(DragDrop.DragOverEvent, (_, e) => Over(e));
        view.AddHandler(DragDrop.DragLeaveEvent, (_, _) => Light(null));

        view.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            Light(null);
            if (pane() is not { } model) return;
            var effect = EffectFor(e, model);
            if (effect == DragDropEffects.None) return;

            e.DragEffects = effect;
            e.Handled = true;
            _ = ReceiveAsync(e, model, effect, FolderAt(view, e.Source));
        });
    }

    /// <summary>The selection under the pointer, as a drag would carry it, or nothing.</summary>
    internal static PaneDragPayload? Payload(BrowserPaneModel model)
    {
        var selection = PaneTransferSnapshots.SelectionFor(model.Source, model.SelectedRows);
        if (selection.IsFailure) return null;

        return new PaneDragPayload(
            model,
            selection.Value,
            model.Title,
            PaneClipboardRules.CanMove(model.SelectedRows));
    }

    /// <summary>
    /// What a drop here would do.
    /// </summary>
    /// <remarks>
    /// Nothing for a pane's own rows: dropping a selection where it came from is a paste into its
    /// own folder, which the queue would answer with a collision for every item. Nothing, too, for
    /// a pane with no folder open or a terminal.
    /// </remarks>
    internal static DragDropEffects EffectFor(DragEventArgs e, BrowserPaneModel? model)
    {
        if (model is null || !model.CanReceiveDrop) return DragDropEffects.None;

        if (PaneDragPayloads.Find(e.DataTransfer?.TryGetValue(Format)) is { } payload)
        {
            if (ReferenceEquals(payload.Source, model)) return DragDropEffects.None;
            return payload.CanMove && e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                ? DragDropEffects.Move
                : DragDropEffects.Copy;
        }

        return e.DataTransfer?.Contains(DataFormat.File) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    /// <summary>
    /// Carries the selection: to another pane by its token, and out of StorageHub as the files
    /// that stand for it, then settles what a drag out came to.
    /// </summary>
    private static async Task DragAsync(
        Control view, PointerPressedEventArgs press, BrowserPaneModel model, PaneDragPayload payload)
    {
        var storage = TopLevel.GetTopLevel(view)?.StorageProvider;
        var outside = new List<IStorageItem>();
        var drag = ExplorerDrag.Nothing;
        if (payload.Selection.Context.Kind == PaneTransferContextKind.ThisPc)
        {
            // Real paths, a plain file drop, as 1.x's was.
            foreach (var row in model.SelectedRows.Where(static row => !row.IsParentNavigation).ToArray())
            {
                if (await LocalItemAsync(storage, row.Location, row.IsContainer).ConfigureAwait(true) is { } item)
                {
                    outside.Add(item);
                }
            }
        }
        else
        {
            drag = model.StartDragOut(payload.Selection);
            if (drag.Refusal is { } refusal)
            {
                model.ReportDragOut(refusal);
                return;
            }

            if (await LocalItemAsync(storage, drag.MarkerPath, container: true).ConfigureAwait(true) is { } marker)
            {
                // Copy only, as 1.x's was: Explorer moving the marker would move it for real,
                // since the broker only stands in the way of a copy.
                outside.Add(marker);
                payload = payload with { CanMove = false };
            }
        }

        var token = PaneDragPayloads.Register(payload);
        try
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(Format, token));
            foreach (var item in outside) transfer.Add(DataTransferItem.CreateFile(item));
            var effects = payload.CanMove ? DragDropEffects.Copy | DragDropEffects.Move : DragDropEffects.Copy;

            try
            {
                _ = await DragDrop.DoDragDropAsync(press, transfer, effects).ConfigureAwait(true);
            }
            catch (Exception error) when (error is InvalidOperationException or NotSupportedException or
                System.Runtime.InteropServices.ExternalException)
            {
                // A platform with no drag support, or a drag begun without a pointer. Only a drag
                // out through the broker has anything to put right, and says so, as 1.x said it.
                if (drag.MarkerPath is not null) model.ReportDragOut(await drag.AbandonAsync(error.Message).ConfigureAwait(true));
                return;
            }

            model.ReportDragOut(await drag.FinishAsync(payload.Landed).ConfigureAwait(true));
        }
        finally
        {
            PaneDragPayloads.Release(token);
        }
    }

    /// <summary>A file or folder on this computer, as the platform's drag wants it, or nothing.</summary>
    private static async Task<IStorageItem?> LocalItemAsync(IStorageProvider? storage, string? path, bool container)
    {
        if (storage is null || string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return null;
        try
        {
            return container
                ? await storage.TryGetFolderFromPathAsync(path).ConfigureAwait(true)
                : await storage.TryGetFileFromPathAsync(path).ConfigureAwait(true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>Hands what landed to the pane: another pane's rows, or the desktop's files.</summary>
    private static async Task ReceiveAsync(
        DragEventArgs e, BrowserPaneModel model, DragDropEffects effect, string? folder)
    {
        if (PaneDragPayloads.Find(e.DataTransfer?.TryGetValue(Format)) is { } payload)
        {
            payload.Landed = true;
            var operation = effect == DragDropEffects.Move
                ? TransferQueueOperation.Move
                : TransferQueueOperation.Copy;
            await model.ReceiveDropAsync(new PaneClipboard(payload.Selection, operation, payload.SourceName))
                .ConfigureAwait(true);
            return;
        }

        var paths = (e.DataTransfer?.TryGetFiles() ?? [])
            .Select(static item => item.TryGetLocalPath())
            .OfType<string>()
            .ToArray();
        if (paths.Length == 0) return;

        await model.ReceiveFilesAsync(paths, folder).ConfigureAwait(true);
    }

    /// <summary>
    /// What a drop over this element lights: the tree when it is over the tree, the list for
    /// anywhere else in the pane, since that is where it lands.
    /// </summary>
    private static Control? TargetFor(Control view, object? source)
    {
        for (Visual? step = source as Visual; step is not null && !ReferenceEquals(step, view); step = step.GetVisualParent())
        {
            if (step is TreeView tree) return tree;
        }

        return view.FindControl<Control>("PART_Rows");
    }

    /// <summary>The folder in the tree a drop is over, as 1.x took it for a drop from Explorer.</summary>
    private static string? FolderAt(Control view, object? source)
    {
        for (Visual? step = source as Visual; step is not null && !ReferenceEquals(step, view); step = step.GetVisualParent())
        {
            if (step is StyledElement { DataContext: PaneTreeNode node }) return node.Target;
            if (step is TreeView) return null;
        }

        return null;
    }

    private static BrowserListItem? RowAt(object? source)
    {
        for (Visual? step = source as Visual; step is not null; step = step.GetVisualParent())
        {
            if (step is StyledElement { DataContext: BrowserListItem row }) return row;
        }

        return null;
    }
}
