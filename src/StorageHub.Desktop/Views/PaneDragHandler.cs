using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
/// press-and-drag on the selection carries it, or from a press on a folder in the tree, which
/// carries that folder. It lands as the same transfer a paste would -- same snapshots, same
/// confirmation, same queue -- through the receiver the workspace gives each pane. Copy unless
/// Shift is held and the rows can be moved, as in 1.x.
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

    /// <summary>The class that draws a list, a tree or a folder in either as the place a drop would land.</summary>
    internal const string DropTargetClass = "drop-target";

    /// <summary>A folder the pointer is over: what to light, and where a drop there goes.</summary>
    internal sealed record DropFolder(Control Element, string Target);

    internal static void Attach(Control view, Func<BrowserPaneModel?> pane)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(pane);

        DragDrop.SetAllowDrop(view, true);

        // The press is remembered and the drag begins once the pointer has moved, not on the
        // press itself: a drag started on the press would swallow the second click of a
        // double-click on a selected row, which is how a folder is opened.
        PointerPressedEventArgs? pending = null;
        BrowserListItem? pressedRow = null;
        BrowserListItem[]? pressedSelection = null;
        PaneTreeNode? pressedNode = null;
        Point origin = default;

        view.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            pending = null;
            pressedRow = null;
            pressedSelection = null;
            pressedNode = null;
            if (pane() is not { IsTerminal: false } model) return;
            if (e.ClickCount != 1 || !e.GetCurrentPoint(view).Properties.IsLeftButtonPressed) return;

            // A folder in the tree is dragged on its own, as 1.x's tree let it be: the tree has no
            // selection to carry beyond the folder under the pointer.
            if (NodeAt(view, e.Source) is { } node)
            {
                pending = e;
                pressedNode = node;
                origin = e.GetPosition(view);
                return;
            }

            if (RowAt(e.Source) is not { IsParentNavigation: false } row) return;

            // Not required to be selected yet. This runs before the list has handled the press, so a
            // row pressed for the first time is not selected here, and a drag that began on it never
            // started: it took a second try, once the first press had selected it. Whether it is
            // selected is asked when the pointer has moved, which is after the press has selected it.
            pending = e;
            pressedRow = row;
            origin = e.GetPosition(view);

            // The list answers a plain press on one of several selected rows by making that row the
            // whole selection, before the pointer has moved. A drag that followed carried that one
            // row, so three files dragged out arrived as one. The selection as it stood is kept here,
            // ahead of the list, and a drag puts it back; a click without a drag is left to the list.
            // Not with a modifier held: Ctrl or Shift is changing the selection, not dragging it.
            if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Meta)) == 0 &&
                model.SelectedRows.Contains(row))
            {
                pressedSelection = [.. model.SelectedRows];
            }
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
            if (pressedNode is { } node)
            {
                if (Payload(model, node) is { } folder) _ = DragAsync(view, press, model, folder);
                return;
            }

            // A press that left its row unselected, such as a Ctrl-click that took it out of the
            // selection, is not the start of a drag.
            if (pressedRow is null || !model.SelectedRows.Contains(pressedRow)) return;
            if (pressedSelection is { Length: > 1 } kept)
            {
                foreach (var row in kept)
                {
                    if (!model.SelectedRows.Contains(row)) model.SelectedRows.Add(row);
                }
            }

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
            var folder = FolderUnder(view, e.Source);
            var effect = EffectFor(e, pane(), folder?.Target);
            e.DragEffects = effect;
            e.Handled = true;

            // The folder under the pointer when there is one, so it is plain which it will go into;
            // otherwise the list or the tree, which is the folder the pane is in.
            Light(effect == DragDropEffects.None ? null : folder?.Element ?? TargetFor(view, e.Source));
        }

        view.AddHandler(DragDrop.DragEnterEvent, (_, e) => Over(e));
        view.AddHandler(DragDrop.DragOverEvent, (_, e) => Over(e));
        view.AddHandler(DragDrop.DragLeaveEvent, (_, _) => Light(null));

        view.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            Light(null);
            if (pane() is not { } model) return;
            var folder = FolderUnder(view, e.Source);
            var effect = EffectFor(e, model, folder?.Target);
            if (effect == DragDropEffects.None) return;

            e.DragEffects = effect;
            e.Handled = true;

            // Into the folder that was lit, whichever it was: a drop of files on a folder row in
            // the list used to land in the pane's folder, and one on the root of the tree at the
            // connection's root, while the row or the whole tree was lit as the place it would go.
            _ = ReceiveAsync(e, model, effect, folder?.Target);
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
    /// A folder in the pane's tree, as a drag would carry it, or nothing: a drive, the root, and
    /// a tree that is not a folder's (Connections Home) are not dragged. Copy only, as a folder
    /// from the list is.
    /// </summary>
    internal static PaneDragPayload? Payload(BrowserPaneModel model, PaneTreeNode node)
    {
        if (node.IsRoot) return null;
        var selection = PaneTransferSnapshots.FolderFor(model.Source, node.Name, node.Target);
        if (selection.IsFailure) return null;

        return new PaneDragPayload(model, selection.Value, model.Title, CanMove: false);
    }

    /// <summary>
    /// What a drop here would do.
    /// </summary>
    /// <remarks>
    /// Nothing for a pane's own rows: dropping a selection where it came from is a paste into its
    /// own folder, which the queue would answer with a collision for every item. Nothing, too, for
    /// a pane with no folder open or a terminal.
    /// </remarks>
    /// <param name="folder">
    /// The folder the pointer is over, where there is one. Over a folder, even a pane's own
    /// selection can be dropped, unless the folder is one of the things dragged or inside one.
    /// </param>
    internal static DragDropEffects EffectFor(DragEventArgs e, BrowserPaneModel? model, string? folder = null)
    {
        if (model is null || !model.CanReceiveDrop) return DragDropEffects.None;

        if (PaneDragPayloads.Find(e.DataTransfer?.TryGetValue(Format)) is { } payload)
        {
            if (ReferenceEquals(payload.Source, model) &&
                (folder is null || IsInsideSelection(payload, folder)))
            {
                return DragDropEffects.None;
            }
            return payload.CanMove && e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                ? DragDropEffects.Move
                : DragDropEffects.Copy;
        }

        return e.DataTransfer?.Contains(DataFormat.File) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    /// <summary>
    /// Whether a folder is one of the things being dragged, or inside one. Dropped there, a folder
    /// would be copied into itself without end, or a move would be refused after it had begun.
    /// </summary>
    internal static bool IsInsideSelection(PaneDragPayload payload, string folder)
    {
        static string Norm(string path) => path.Replace('\\', '/').Trim('/');

        var target = Norm(folder);
        foreach (var item in payload.Selection.Items)
        {
            var dragged = Norm(item.RelativePath);
            if (dragged.Length == 0) continue;
            if (string.Equals(target, dragged, StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith(dragged + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
            // Real paths, a plain file drop, as 1.x's was. This PC's items are their own full paths.
            foreach (var item in payload.Selection.Items)
            {
                if (await LocalItemAsync(storage, item.RelativePath, item.IsContainer).ConfigureAwait(true) is { } local)
                {
                    outside.Add(local);
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
    /// <param name="folder">The folder under the pointer, in the tree or the list; null for the pane's own.</param>
    private static async Task ReceiveAsync(
        DragEventArgs e, BrowserPaneModel model, DragDropEffects effect, string? folder)
    {
        if (PaneDragPayloads.Find(e.DataTransfer?.TryGetValue(Format)) is { } payload)
        {
            payload.Landed = true;
            var operation = effect == DragDropEffects.Move
                ? TransferQueueOperation.Move
                : TransferQueueOperation.Copy;
            await model.ReceiveDropAsync(
                new PaneClipboard(payload.Selection, operation, payload.SourceName), folder)
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

    /// <summary>
    /// The folder the pointer is over, in the pane's tree or in its list, with the element to light.
    /// Not the way back out, not a connection on Connections Home, and not the root of the tree,
    /// which is the folder the pane is already in.
    /// </summary>
    private static DropFolder? FolderUnder(Control view, object? source)
    {
        for (Visual? step = source as Visual; step is not null && !ReferenceEquals(step, view); step = step.GetVisualParent())
        {
            if (step is TreeViewItem { DataContext: PaneTreeNode { IsRoot: false } node } item)
            {
                return new DropFolder(item, node.Target);
            }

            if (step is TableViewRow
                {
                    DataContext: BrowserListItem { IsContainer: true, IsParentNavigation: false, Location.Length: > 0 } row
                } line &&
                ConnectionsHomeSource.ConnectionOf(row) is null)
            {
                return new DropFolder(line, row.Location!);
            }
        }

        return null;
    }

    /// <summary>
    /// The folder in the tree a press landed on, as a drag would carry it: never the root, which
    /// is This PC or the connection itself, and never the expander, which opens the folder.
    /// </summary>
    private static PaneTreeNode? NodeAt(Control view, object? source)
    {
        for (Visual? step = source as Visual; step is not null && !ReferenceEquals(step, view); step = step.GetVisualParent())
        {
            if (step is ToggleButton) return null;
            if (step is TreeViewItem { DataContext: PaneTreeNode { IsRoot: false } node }) return node;
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
