using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.VisualTree;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

public class MenuSizingTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PopupRowsShareCompactMetricsAcrossMenuHosts(bool light)
    {
        var window = new MainWindow
        {
            DataContext = ShellPreview.CreateOnWorkspace(),
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        window.Show();
        try
        {
            var button = window.GetVisualDescendants().OfType<Button>()
                .First(control => AutomationProperties.GetName(control) == BrowserPaneModel.PaneActionsLabel);
            var flyout = (MenuFlyout)button.Flyout!;
            flyout.ShowAt(button);
            window.UpdateLayout();
            var flyoutRow = flyout.Items.OfType<MenuItem>().First();

            var contextRow = new MenuItem { Header = "Same action", InputGesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.C) };
            var contextMenu = new ContextMenu { ItemsSource = new[] { contextRow } };
            contextMenu.Open(button);
            window.UpdateLayout();

            var menu = window.GetVisualDescendants().OfType<Menu>().Single();
            var root = menu.GetLogicalChildren().OfType<MenuItem>().First();
            root.IsSubMenuOpen = true;
            window.UpdateLayout();
            var dropdownRow = root.GetLogicalChildren().OfType<MenuItem>().First(item => !item.Classes.Contains("heading"));

            foreach (var row in flyout.Items.OfType<MenuItem>().Concat(new[] { contextRow, dropdownRow }))
            {
                Assert.Equal(12, row.FontSize);
                Assert.Equal(new Thickness(8, 3), row.Padding);
                Assert.Equal(26, row.MinHeight);
                Assert.InRange(row.Bounds.Height, 26, 30);
            }
            root.IsSubMenuOpen = false;
            contextMenu.Close();
            flyout.Hide();
        }
        finally
        {
            window.Close();
        }
    }
}
