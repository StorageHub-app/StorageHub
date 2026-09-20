using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The tab strip, and the difference between hovering a tab and being on it.
/// </summary>
/// <remarks>
/// Fluent draws its accent pipe on hover as well as on selection, a little dimmer. On a dark strip
/// the two are indistinguishable: hovering a tab made it look like the one you were on, while the
/// one you were actually on stopped standing out. Owning the template fixes it; this says so.
/// </remarks>
public class TabStripTests
{
    private static (Window Window, TabControl Tabs) Shell()
    {
        var window = new MainWindow { DataContext = ShellPreview.Sample };
        window.Show();
        window.Measure(new Size(1500, 920));
        window.Arrange(new Rect(0, 0, 1500, 920));

        var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        return (window, tabs);
    }

    private static Border Accent(TabItem item) =>
        item.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_Accent");

    [AvaloniaFact]
    public void OnlyTheSelectedTabDrawsTheAccent()
    {
        var (window, tabs) = Shell();
        var items = tabs.GetVisualDescendants().OfType<TabItem>().ToList();

        Assert.Equal(3, items.Count);
        Assert.Single(items, item => item.IsSelected);

        foreach (var item in items)
        {
            var painted = Accent(item).Background is ISolidColorBrush { Color.A: > 0 };
            Assert.Equal(item.IsSelected, painted);
        }

        GC.KeepAlive(window);
    }

    /// <summary>Hovering an unselected tab must not make it look selected.</summary>
    [AvaloniaFact]
    public void HoveringATabDoesNotDrawTheAccent()
    {
        var (window, tabs) = Shell();
        var unselected = tabs.GetVisualDescendants().OfType<TabItem>().First(item => !item.IsSelected);

        var centre = unselected.TranslatePoint(
            new Point(unselected.Bounds.Width / 2, unselected.Bounds.Height / 2), window);
        Assert.NotNull(centre);

        window.MouseMove(centre!.Value, RawInputModifiers.None);
        window.UpdateLayout();

        Assert.True(unselected.IsPointerOver, "the tab was never hovered, so this proves nothing");
        Assert.False(unselected.IsSelected);
        Assert.False(
            Accent(unselected).Background is ISolidColorBrush { Color.A: > 0 },
            "a hovered tab is drawing the selection accent");
    }
}
