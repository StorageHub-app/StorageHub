using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace StorageHub.Desktop.Views;

/// <summary>Extends the queue's normal click selection with a left-button sweep over rows.</summary>
internal sealed class TransferRowDragSelection
{
    private readonly TransferQueueView _view;
    private readonly DispatcherTimer _scrollTimer;
    private TableView? _table;
    private TransferRow? _anchor;
    private TransferRow[] _original = [];
    private Point _start;
    private Point _position;
    private IPointer? _pointer;
    private bool _dragging;

    public TransferRowDragSelection(TransferQueueView view)
    {
        _view = view;
        _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _scrollTimer.Tick += (_, _) => UpdateSelection(scroll: true);
        view.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
        view.AddHandler(InputElement.PointerMovedEvent, Moved, RoutingStrategies.Tunnel);
        view.AddHandler(InputElement.PointerReleasedEvent, Released, RoutingStrategies.Tunnel);
        view.DetachedFromVisualTree += (_, _) => Reset();
    }

    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        Reset();
        if (!e.GetCurrentPoint(_view).Properties.IsLeftButtonPressed ||
            _view.DataContext is not TransferQueueModel model || e.Source is not Visual source ||
            source.FindAncestorOfType<TableViewColumnHeader>(includeSelf: true) is not null ||
            source.FindAncestorOfType<TableView>(includeSelf: true) is not
                { DataContext: TransferQueueTab { IsStateFilter: true } } table)
            return;

        var position = e.GetPosition(table);
        var rows = VisibleRows(table);
        // Headers and their resize grips keep their own pointer behavior.
        if (rows.Length == 0 || position.Y < rows.Min(row => RowTop(row, table))) return;
        if (table.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } scroller &&
            scroller.TranslatePoint(default, table) is { } viewport && position.Y < viewport.Y) return;
        _table = table;
        _anchor = NearestRow(table, position.Y);
        _original = e.KeyModifiers.HasFlag(KeyModifiers.Control) ? [.. model.SelectedRows] : [];
        _start = _position = e.GetPosition(_view);
        _pointer = e.Pointer;
        // Leave clicks, Ctrl-click, Shift-click, and keyboard selection to the table.
    }

    private void Moved(object? sender, PointerEventArgs e)
    {
        if (_table is null || !ReferenceEquals(e.Pointer, _pointer)) return;
        if (!e.GetCurrentPoint(_view).Properties.IsLeftButtonPressed)
        {
            Reset();
            return;
        }
        _position = e.GetPosition(_view);
        if (!_dragging && Math.Abs(_position.X - _start.X) < 4 && Math.Abs(_position.Y - _start.Y) < 4) return;
        if (!_dragging)
        {
            _dragging = true;
            _table.PointerCaptureLost += CaptureLost;
            e.Pointer.Capture(_table);
            _scrollTimer.Start();
        }
        UpdateSelection(scroll: false);
        e.Handled = true;
    }

    private void UpdateSelection(bool scroll)
    {
        if (_table is not { IsEffectivelyVisible: true } table ||
            _view.DataContext is not TransferQueueModel model || _anchor is null ||
            table.DataContext is not TransferQueueTab { IsStateFilter: true })
        {
            Reset();
            return;
        }
        var point = _view.TranslatePoint(_position, table);
        if (point is null) return;
        if (scroll && table.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } scroller &&
            _view.TranslatePoint(_position, scroller) is { } scrollPoint)
        {
            var delta = scrollPoint.Y < 0 ? -20 : scrollPoint.Y > scroller.Bounds.Height ? 20 : 0;
            if (delta != 0)
            {
                scroller.Offset = new Vector(scroller.Offset.X,
                    Math.Clamp(scroller.Offset.Y + delta, 0, Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height)));
                table.UpdateLayout();
            }
        }
        var end = NearestRow(table, point.Value.Y);
        var first = model.Rows.IndexOf(_anchor);
        var last = end is null ? -1 : model.Rows.IndexOf(end);
        if (first < 0 || last < 0)
        {
            Reset();
            return;
        }
        var chosen = model.Rows.Skip(Math.Min(first, last)).Take(Math.Abs(last - first) + 1)
            .Concat(_original.Where(model.Rows.Contains)).ToHashSet();
        for (var index = model.SelectedRows.Count - 1; index >= 0; index--)
            if (!chosen.Contains(model.SelectedRows[index])) model.SelectedRows.RemoveAt(index);
        foreach (var row in model.Rows.Where(chosen.Contains))
            if (!model.SelectedRows.Contains(row)) model.SelectedRows.Add(row);
    }

    private static TableViewRow[] VisibleRows(TableView table) => table.GetVisualDescendants()
        .OfType<TableViewRow>().Where(row => row.IsEffectivelyVisible && row.DataContext is TransferRow).ToArray();

    private static double RowTop(TableViewRow row, TableView table) =>
        row.TranslatePoint(default, table)?.Y ?? double.PositiveInfinity;

    private static TransferRow? NearestRow(TableView table, double y) => VisibleRows(table)
        .MinBy(row => Math.Max(0, Math.Max(RowTop(row, table) - y, y - RowTop(row, table) - row.Bounds.Height)))
        ?.DataContext as TransferRow;

    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _pointer) || e.InitialPressMouseButton != MouseButton.Left) return;
        var dragged = _dragging;
        Reset();
        if (dragged) e.Handled = true;
    }

    private void CaptureLost(object? sender, PointerCaptureLostEventArgs e) => Reset();

    private void Reset()
    {
        _scrollTimer.Stop();
        var table = _table;
        var pointer = _pointer;
        if (table is not null) table.PointerCaptureLost -= CaptureLost;
        _table = null;
        _pointer = null;
        _anchor = null;
        _original = [];
        _dragging = false;
        if (table is not null && ReferenceEquals(pointer?.Captured, table)) pointer.Capture(null);
    }
}
