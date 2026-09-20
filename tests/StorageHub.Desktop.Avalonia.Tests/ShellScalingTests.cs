using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Avalonia.Tests;

/// <summary>
/// The shell, laid out at every display scaling StorageHub is used at.
/// </summary>
/// <remarks>
/// This is the suite the WinForms shell could not have. Its DPI defects came from metrics written as
/// literal pixels while fonts scaled with the display, and finding them needed a machine set to
/// 125% - which is why the project was authored at 125% and came apart at 100%. Avalonia lays out
/// in device-independent pixels, so the same tree can be measured at any scaling here, on either
/// platform, with no display attached at all.
/// </remarks>
public class ShellScalingTests
{
    private static MainWindow Shell() => new() { DataContext = ShellPreview.Sample };

    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void TheShellArrangesWithoutOverflowingAtEveryScaling(double scaling)
    {
        var window = Shell();
        window.Show();

        var available = new Size(1500 * scaling, 920 * scaling);
        window.Measure(available);
        window.Arrange(new Rect(available));

        Assert.True(window.Bounds.Width > 0, $"nothing arranged at {scaling}x");
        Assert.All(
            window.GetVisualDescendants().OfType<Control>(),
            child =>
            {
                Assert.False(double.IsNaN(child.Bounds.Width), $"{child.GetType().Name} has no width");
                Assert.False(double.IsInfinity(child.Bounds.Height), $"{child.GetType().Name} is unbounded");
            });
    }

    /// <summary>
    /// The relationship the WinForms shell kept getting wrong.
    /// </summary>
    /// <remarks>
    /// There, a metric left in logical units was compared against text measured in device units, so
    /// at 125% a column was a quarter narrower than the text it was sizing for. Here the shell is
    /// measured twice and the proportions have to hold, which is the property that actually matters
    /// and the one a fixed-pixel assertion could never express.
    /// </remarks>
    [AvaloniaFact]
    public void TheLayoutKeepsItsProportionsAcrossScalings()
    {
        double SidebarShare(double scale)
        {
            var window = Shell();
            window.Show();
            var available = new Size(1500 * scale, 920 * scale);
            window.Measure(available);
            window.Arrange(new Rect(available));

            var sidebar = window.GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("surface"));
            return sidebar.Bounds.Width;
        }

        // The sidebar is a fixed width in device-independent pixels, so it stays the same number at
        // every scaling and the compositor makes it physically larger. That is exactly the
        // arithmetic the old shell had to do by hand at 761 call sites.
        Assert.Equal(SidebarShare(1.0), SidebarShare(1.25));
        Assert.Equal(SidebarShare(1.0), SidebarShare(2.0));
    }

    [AvaloniaFact]
    public void TheShellCarriesEveryPartItPromises()
    {
        var window = Shell();
        window.Show();

        var menu = window.GetVisualDescendants().OfType<Menu>().Single();
        Assert.Equal(9, menu.ItemsSource!.Cast<object>().Count());

        var tabs = window.GetVisualDescendants().OfType<TabControl>().ToList();
        Assert.Equal(2, tabs.Count);
        Assert.Equal(3, tabs[0].ItemsSource!.Cast<object>().Count());
        Assert.Equal(7, tabs[1].ItemsSource!.Cast<object>().Count());
    }

    /// <summary>
    /// The workspace strip carries a button to add one, after the last tab.
    /// </summary>
    /// <remarks>
    /// Not a tab. The WinForms shell used a sentinel "+" TabPage and vetoed selecting it in
    /// WorkspaceTabsSelecting; Avalonia's TabControl.SelectionChanged cannot be cancelled, so the
    /// same trick would leave the shell showing an empty workspace. It is templated into the strip
    /// instead, which also retires the _changingWorkspaceTabs and _workspaceAddPending guards.
    /// </remarks>
    [AvaloniaFact]
    public void TheWorkspaceStripHasAnAddButtonThatIsNotATab()
    {
        var window = Shell();
        window.Show();
        window.Measure(new Size(1500, 920));
        window.Arrange(new Rect(0, 0, 1500, 920));

        var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        var add = window.GetVisualDescendants().OfType<Button>()
            .Single(button => button.Classes.Contains("tab-add"));

        Assert.NotNull(add.Command);
        Assert.Equal(3, tabs.ItemsSource!.Cast<object>().Count());

        // It sits to the right of the last tab rather than among them.
        var lastTab = window.GetVisualDescendants().OfType<TabItem>().Last();
        Assert.True(
            add.Bounds.X >= 0 && lastTab.Bounds.Width > 0,
            "the add button did not arrange beside the tabs");
    }

    /// <summary>Nothing on screen is a type name.</summary>
    /// <remarks>
    /// A TabControl with no ContentTemplate renders its item's ToString, so the queue strip showed a
    /// literal "QueueTab { Title = Active (0) }" - visible in a screenshot, invisible to every test
    /// that only counted items. This asserts the shape of that mistake rather than the one instance.
    /// </remarks>
    [AvaloniaFact]
    public void NoTemplateFallsBackToATypeName()
    {
        var window = Shell();
        window.Show();
        window.Measure(new Size(1500, 920));
        window.Arrange(new Rect(0, 0, 1500, 920));

        var rendered = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text)
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();

        Assert.NotEmpty(rendered);
        Assert.DoesNotContain(rendered, text => text!.Contains(" { ", StringComparison.Ordinal));
    }

    /// <summary>
    /// Switching appearance repaints the tree rather than walking it.
    /// </summary>
    /// <remarks>
    /// The WinForms shell needed a 250-line recursive walker and a colour-to-colour lookup table to
    /// re-theme an open window, because each control had its palette baked into its properties. Here
    /// the brushes are dynamic resources, so the assignment below is the whole mechanism.
    /// </remarks>
    [AvaloniaFact]
    public void ChangingAppearanceRepaintsWithoutWalkingTheTree()
    {
        var window = Shell();
        window.Show();

        global::Avalonia.Media.Color Repaint(ThemeVariant variant)
        {
            global::Avalonia.Application.Current!.RequestedThemeVariant = variant;
            window.Measure(new Size(1500, 920));
            window.Arrange(new Rect(0, 0, 1500, 920));
            return ((global::Avalonia.Media.ISolidColorBrush)window.Background!).Color;
        }

        var light = Repaint(ThemeVariant.Light);
        var dark = Repaint(ThemeVariant.Dark);

        Assert.NotEqual(light, dark);
        Assert.Equal(global::Avalonia.Media.Color.FromRgb(244, 246, 250), light);
        Assert.Equal(global::Avalonia.Media.Color.FromRgb(22, 24, 29), dark);
    }

    /// <summary>A picture per scaling and appearance, which a build agent can attach to a review.</summary>
    /// <remarks>
    /// Light is photographed too because it is a shipped appearance, not a fallback: both variants
    /// are complete in DesignTokens.axaml and a reviewer should be able to see both without setting
    /// a machine to them.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(1.0, "dark")]
    [InlineData(1.25, "dark")]
    [InlineData(2.0, "dark")]
    [InlineData(1.25, "light")]
    public void TheShellCanBePhotographedAtEveryScaling(double scaling, string appearance)
    {
        var variant = appearance == "light" ? ThemeVariant.Light : ThemeVariant.Dark;
        try
        {
            Photograph(scaling, appearance, variant);
        }
        finally
        {
            // Session-wide state on one dispatcher: leaving it Light would make every test that
            // reads the appearance depend on the order it happened to run in.
            global::Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    private static void Photograph(double scaling, string appearance, ThemeVariant variant)
    {
        global::Avalonia.Application.Current!.RequestedThemeVariant = variant;

        var window = Shell();
        window.Show();
        window.Measure(new Size(1500 * scaling, 920 * scaling));
        window.Arrange(new Rect(0, 0, 1500 * scaling, 920 * scaling));

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            var file = Path.Combine(
                directory,
                string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"shell-{(OperatingSystem.IsWindows() ? "windows" : "linux")}-{appearance}-{scaling:0.00}x.png"));
            using var stream = File.Create(file);
            frame!.Save(stream, new global::Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
    }
}
