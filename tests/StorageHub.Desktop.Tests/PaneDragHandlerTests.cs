using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// What a press on the list does to the selection a drag is about to carry.
/// </summary>
public class PaneDragHandlerTests
{
    private static readonly string[] Names = ["a.txt", "b.txt", "c.txt"];

    /// <summary>
    /// Several selected rows, pressed on one of them and dragged, go together.
    /// </summary>
    /// <remarks>
    /// The list makes the pressed row the whole selection on the press, before the pointer has
    /// moved, so three files dragged to Explorer or to another pane arrived as one. The drag
    /// puts the selection back as it was when the press began. A click that goes nowhere is
    /// still the list's: it leaves the one row selected, as it always did.
    /// </remarks>
    [AvaloniaFact]
    public void APressOnOneOfSeveralSelectedRowsDragsAllOfThem()
    {
        var model = ShellPreview.CreateOnWorkspace();
        var window = new MainWindow { DataContext = model };
        window.Show();
        window.Measure(new Size(1500, 920));
        window.Arrange(new Rect(0, 0, 1500, 920));
        window.UpdateLayout();

        var table = window.GetVisualDescendants().OfType<TableView>().First(static t => t.Name == "PART_Rows");
        var pane = (BrowserPaneModel)table.DataContext!;
        var rows = Names
            .Select(name => new BrowserListItem(name, "", "", "", "", Location: "bucket/" + name))
            .ToArray();
        foreach (var row in rows) pane.Rows.Add(row);
        window.UpdateLayout();
        foreach (var row in rows) pane.SelectedRows.Add(row);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, pane.SelectedRows.Count);

        var cell = table.GetVisualDescendants().OfType<TextBlock>()
            .First(each => ReferenceEquals(each.DataContext, rows[1]) && each.IsEffectivelyVisible);
        var point = cell.TranslatePoint(new Point(4, 4), window)!.Value;

        // A click: the list keeps only the row that was clicked.
        window.MouseMove(point, RawInputModifiers.None);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(rows[1], Assert.Single(pane.SelectedRows));

        // A press on another of the three (another, so it is not the second click of a
        // double-click), moved far enough to be a drag: all three again.
        pane.SelectedRows.Add(rows[0]);
        pane.SelectedRows.Add(rows[2]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, table.SelectedItems!.Count);
        var other = table.GetVisualDescendants().OfType<TextBlock>()
            .First(each => ReferenceEquals(each.DataContext, rows[0]) && each.IsEffectivelyVisible)
            .TranslatePoint(new Point(4, 4), window)!.Value;
        window.MouseMove(other, RawInputModifiers.None);
        window.MouseDown(other, MouseButton.Left);
        Assert.Same(rows[0], Assert.Single(pane.SelectedRows));
        window.MouseMove(other + new Vector(12, 12), RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(rows, pane.SelectedRows.OrderBy(static row => row.Name, StringComparer.Ordinal));
        Assert.Equal(3, table.SelectedItems.Count);
        window.MouseUp(other + new Vector(12, 12), MouseButton.Left);
    }

    /// <summary>
    /// Files from Explorer dropped on a folder row in the list go into that folder, which is the
    /// one lit while they were over it. They used to go into the folder the pane was showing.
    /// </summary>
    [AvaloniaFact]
    public async Task FilesDroppedOnAFolderRowGoIntoThatFolder()
    {
        var folder = Directory.CreateTempSubdirectory("storagehub-drop-");
        var reports = Directory.CreateDirectory(Path.Combine(folder.FullName, "reports"));
        var file = Path.Combine(folder.FullName, "dropped.txt");
        await File.WriteAllTextAsync(file, "x", TestContext.Current.CancellationToken);
        try
        {
            await using var pane = new BrowserPaneModel(new WorkspaceFakes.FakeBrowsingAgent([]));
            await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
            await pane.OpenAsync(pane.Connections.Single(static c => c.Kind == PaneContentKind.ThisPc), TestContext.Current.CancellationToken);
            await pane.NavigateAsync(folder.FullName, TestContext.Current.CancellationToken);
            string? landed = null;
            pane.DropReceiver = static _ => Task.CompletedTask;
            pane.FilesDropReceiver = (_, into) =>
            {
                landed = into;
                return Task.CompletedTask;
            };

            var view = new BrowserPaneView { DataContext = pane };
            var window = new Window { Content = view, Width = 900, Height = 500 };
            window.Show();
            window.UpdateLayout();

            var storage = await window.StorageProvider.TryGetFileFromPathAsync(new Uri(file));
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateFile(storage!));
            var row = view.GetVisualDescendants().OfType<TableViewRow>()
                .First(static each => each.DataContext is BrowserListItem { Name: "reports" });
            row.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, data, row, new Point(4, 4), KeyModifiers.None));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(reports.FullName, landed, ignoreCase: true);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
