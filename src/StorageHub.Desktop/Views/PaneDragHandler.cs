using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>
/// What a pane drag carries, kept in the process rather than on the platform's clipboard.
/// </summary>
/// <remarks>
/// A drag's data crosses a platform boundary as strings and files, and a selection snapshot is
/// neither. The drag carries a token; this holds what the token names, and forgets it when the
/// drag ends. Dropping a StorageHub selection on another application therefore does nothing,
/// which is right: nothing else can act on a saved connection's listing.
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
    bool CanMove);

/// <summary>
/// Dragging rows from one pane to another, and files from the desktop into a pane.
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
/// Files from the operating system's file manager land the same way, as This PC selections. What
/// is not here is the other direction: dragging a remote file out to Explorer needs the broker
/// that stages it, which has not been ported.
/// </para>
/// </remarks>
internal static class PaneDragHandler
{
    /// <summary>Our own format, scoped to this application.</summary>
    private static readonly DataFormat<string> Format =
        DataFormat.CreateStringApplicationFormat("StorageHub.PaneSelection.v1");

    /// <summary>How far the pointer moves before a press on a selected row becomes a drag.</summary>
    private const double DragThreshold = 4;

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

            var token = PaneDragPayloads.Register(payload);
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(Format, token));
            var effects = payload.CanMove ? DragDropEffects.Copy | DragDropEffects.Move : DragDropEffects.Copy;
            _ = DragAsync(press, transfer, effects, token);
        }, RoutingStrategies.Tunnel);

        view.AddHandler(InputElement.PointerReleasedEvent, (_, _) => pending = null, RoutingStrategies.Tunnel);

        view.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = EffectFor(e, pane());
            e.Handled = true;
        });

        view.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (pane() is not { } model) return;
            var effect = EffectFor(e, model);
            if (effect == DragDropEffects.None) return;

            e.DragEffects = effect;
            e.Handled = true;
            _ = ReceiveAsync(e, model, effect);
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

    private static async Task DragAsync(
        PointerPressedEventArgs e, DataTransfer transfer, DragDropEffects effects, string token)
    {
        try
        {
            _ = await DragDrop.DoDragDropAsync(e, transfer, effects).ConfigureAwait(true);
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException)
        {
            // A platform with no drag support, or a drag begun without a pointer: nothing to do.
        }
        finally
        {
            PaneDragPayloads.Release(token);
        }
    }

    /// <summary>Hands what landed to the pane, as one transfer per source folder.</summary>
    private static async Task ReceiveAsync(DragEventArgs e, BrowserPaneModel model, DragDropEffects effect)
    {
        if (PaneDragPayloads.Find(e.DataTransfer?.TryGetValue(Format)) is { } payload)
        {
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

        var selections = LocalDrops.From(paths);
        if (selections.IsFailure)
        {
            model.Status = selections.Error.Message;
            return;
        }

        foreach (var selection in selections.Value)
        {
            await model.ReceiveDropAsync(new PaneClipboard(selection, TransferQueueOperation.Copy, Ui.Pane.ThisPc))
                .ConfigureAwait(true);
        }
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
