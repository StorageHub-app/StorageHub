using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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

        var table = window.GetVisualDescendants().OfType<TableView>().First(static t => t.Name == "PART_Rows");
        table.RaiseEvent(new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent, Source = table });

        var headers = (table.ContextMenu?.ItemsSource as IEnumerable<Control> ?? [])
            .OfType<MenuItem>()
            .Select(static item => item.Header as string)
            .ToArray();

        Assert.Equal(Ui.Pane.Open, headers[0]);
        Assert.Equal(Ui.Pane.EditInExternalEditor, headers[1]);
        Assert.Contains(Ui.Commands.EditNewFolder, headers);
        Assert.Contains(Ui.Commands.EditCopy, headers);
        Assert.Contains(Ui.Commands.EditPaste, headers);
        Assert.Contains(Ui.Commands.EditDelete, headers);
        Assert.Equal(Ui.Commands.EditProperties, headers[^1]);

        // Each shell entry carries the shortcut the menu bar shows for it.
        var copy = table.ContextMenu!.ItemsSource!.OfType<MenuItem>().Single(item => (string?)item.Header == Ui.Commands.EditCopy);
        Assert.NotNull(copy.InputGesture);
    }
}
