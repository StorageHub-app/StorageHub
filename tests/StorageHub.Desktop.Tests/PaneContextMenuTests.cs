using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// A pane's right-click menu, which 1.x had and 2.0 did not.
/// </summary>
public class PaneContextMenuTests
{
    [AvaloniaFact]
    public void RightClickingTheListOffersOpenThenTheShellsOwnFileCommands()
    {
        var model = ShellPreview.CreateOnWorkspace();
        var window = new MainWindow { DataContext = model };
        window.Show();
        window.Measure(new Size(1500, 920));
        window.Arrange(new Rect(0, 0, 1500, 920));
        window.UpdateLayout();

        // A real right-click, on a row: it becomes the selection, and the menu opens the first
        // time, not only from the second.
        var table = window.GetVisualDescendants().OfType<TableView>().First(static t => t.Name == "PART_Rows");
        var pane = (BrowserPaneModel)table.DataContext!;
        var clicked = new BrowserListItem("render.exr", "", "", "", "", Location: "bucket/render.exr");
        pane.Rows.Add(clicked);
        window.UpdateLayout();
        var cell = table.GetVisualDescendants().OfType<TextBlock>()
            .First(each => ReferenceEquals(each.DataContext, clicked) && each.IsEffectivelyVisible);
        var point = cell.TranslatePoint(new Point(4, 4), window)!.Value;
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(clicked, Assert.Single(pane.SelectedRows));
        Assert.True(table.ContextMenu!.IsOpen);
        var headers = table.ContextMenu.ItemsSource!
            .OfType<MenuItem>()
            .Select(static item => item.Header as string)
            .ToArray();

        // Everything the FILES row and its "..." offer, so that hiding the row loses nothing.
        Assert.Equal(Ui.Pane.Open, headers[0]);
        Assert.Equal(Ui.Pane.EditInExternalEditor, headers[1]);
        Assert.Subset(
            new HashSet<string?>(headers),
            new HashSet<string?>
            {
                Ui.Commands.EditNewFolder, Ui.Commands.EditNewEmptyFile, Ui.Commands.EditRename,
                Ui.Commands.EditBatchRename, Ui.Commands.EditCopy, Ui.Commands.EditCut,
                Ui.Commands.EditPaste, Ui.Commands.EditDelete, Ui.Commands.EditSelectAll,
                Ui.Commands.EditInvertSelection,
            });
        Assert.Equal(Ui.Commands.EditProperties, headers[^1]);

        // Each shell entry carries the shortcut the menu bar shows for it.
        var copy = table.ContextMenu.ItemsSource!.OfType<MenuItem>().Single(item => (string?)item.Header == Ui.Commands.EditCopy);
        Assert.NotNull(copy.InputGesture);
        table.ContextMenu.Close();

        // One on the empty space below the rows clears the selection, as 1.x's did, so the menu
        // it opens has nothing to act on rather than the row clicked before.
        var below = table.TranslatePoint(new Point(20, table.Bounds.Height - 20), window)!.Value;
        window.MouseDown(below, MouseButton.Right);
        window.MouseUp(below, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(pane.SelectedRows);
        Assert.Null(pane.Selected);
        Assert.True(table.ContextMenu.IsOpen);
        table.ContextMenu.Close();

        // With the FILES row hidden and the folder empty, the menu is still there to make the
        // first folder in it: the list fills the pane and still takes the right-click.
        pane.ShowFilesBar = false;
        pane.Rows.Clear();
        window.UpdateLayout();
        var row = window.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Classes.Contains("pane-commands") && ReferenceEquals(border.DataContext, pane));
        Assert.False(row.IsVisible);
        var middle = table.TranslatePoint(new Point(table.Bounds.Width / 2, table.Bounds.Height / 2), window)!.Value;
        window.MouseDown(middle, MouseButton.Right);
        window.MouseUp(middle, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();

        // Each entry is lit where the row's button for it would be, as 1.x's Opening had it: with
        // nothing there, nothing to copy, delete or select, while Refresh still has somewhere to go.
        Assert.True(table.ContextMenu.IsOpen);
        var entries = table.ContextMenu.ItemsSource!.OfType<MenuItem>().ToDictionary(static item => (string)item.Header!);
        Assert.Equal(pane.NewFolderCommand.CanExecute(null), entries[Ui.Commands.EditNewFolder].IsEffectivelyEnabled);
        Assert.All(
            new[]
            {
                Ui.Commands.EditRename, Ui.Commands.EditCopy, Ui.Commands.EditCut, Ui.Commands.EditDelete,
                Ui.Commands.EditSelectAll, Ui.Commands.EditInvertSelection, Ui.Commands.EditProperties,
            },
            header => Assert.False(entries[header].IsEffectivelyEnabled, header));
        Assert.True(entries[Ui.Commands.ViewRefresh].IsEffectivelyEnabled);
        table.ContextMenu.Close();
    }
}
