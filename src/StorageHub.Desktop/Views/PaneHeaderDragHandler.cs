using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace StorageHub.Desktop.Views;

/// <summary>
/// Dragging a pane by its header onto another pane, as 1.x allowed: the middle swaps the two, an
/// edge docks the dragged pane on that side.
/// </summary>
/// <remarks>
/// The same moves the pane's "Move or swap" menu offers, by hand. The row drag on the same pane
/// carries files; this carries a pane, in its own format, and leaves every other drag alone --
/// listening even to events the row drag has already handled, so the two never block each other.
/// </remarks>
internal static class PaneHeaderDragHandler
{
    private static readonly DataFormat<string> Format =
        DataFormat.CreateStringApplicationFormat("StorageHub.Pane.v1");

    /// <summary>The pane being dragged. One process, one pointer, so one at a time.</summary>
    private static BrowserPaneModel? _dragging;

    /// <summary>How far in from an edge still docks on that edge, as a share of the pane.</summary>
    private const double EdgeShare = 0.25;

    internal static void Attach(BrowserPaneView view, Control header, Func<BrowserPaneModel?> pane)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(pane);

        DragDrop.SetAllowDrop(view, true);

        header.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            // Not from the header's own button: Pane actions is a click, not a drag.
            if (e.Source is Visual source && source.FindAncestorOfType<Button>() is not null) return;
            if (!e.GetCurrentPoint(header).Properties.IsLeftButtonPressed || pane() is not { } moving) return;

            _dragging = moving;
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(Format, "pane"));
            _ = Drag(e, transfer);
        }, RoutingStrategies.Tunnel);

        view.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            if (!IsPaneDrag(e)) return;
            e.DragEffects = Target(view, pane) is null ? DragDropEffects.None : DragDropEffects.Move;
            e.Handled = true;
        }, RoutingStrategies.Bubble, handledEventsToo: true);

        view.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (!IsPaneDrag(e) || _dragging is not { } moving || Target(view, pane) is not { } target) return;
            if (view.FindAncestorOfType<WorkspaceView>()?.DataContext is not WorkspaceModel workspace) return;

            var at = e.GetPosition(view);
            workspace.Rearrange(moving, target, EdgeAt(at, view.Bounds.Size));
            e.Handled = true;
        }, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>
    /// Which edge a point is near enough to dock on, or null for the middle, which swaps.
    /// </summary>
    internal static WorkspaceDockEdge? EdgeAt(Point at, Size size)
    {
        if (size.Width <= 0 || size.Height <= 0) return null;
        var x = at.X / size.Width;
        var y = at.Y / size.Height;

        // Whichever edge the point is closest to, if it is within the band at all.
        var candidates = new (double Distance, WorkspaceDockEdge Edge)[]
        {
            (x, WorkspaceDockEdge.Left), (1 - x, WorkspaceDockEdge.Right),
            (y, WorkspaceDockEdge.Top), (1 - y, WorkspaceDockEdge.Bottom),
        };
        var nearest = candidates.MinBy(static candidate => candidate.Distance);
        return nearest.Distance < EdgeShare ? nearest.Edge : null;
    }

    private static async Task Drag(PointerPressedEventArgs e, DataTransfer transfer)
    {
        try
        {
            await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move).ConfigureAwait(true);
        }
        finally
        {
            _dragging = null;
        }
    }

    private static bool IsPaneDrag(DragEventArgs e) => e.DataTransfer?.TryGetValue(Format) is not null;

    /// <summary>The pane dropped on, if it is not the one being dragged.</summary>
    private static BrowserPaneModel? Target(BrowserPaneView view, Func<BrowserPaneModel?> pane) =>
        pane() is { } target && _dragging is { } moving && !ReferenceEquals(target, moving) ? target : null;
}
