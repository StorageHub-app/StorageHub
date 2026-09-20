using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Views;
using Xunit;
using static StorageHub.Desktop.Tests.WorkspaceFakes;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// A pane's own header: which pane it is, and what its menu can do to the arrangement.
/// </summary>
/// <remarks>
/// <para>
/// These replaced a strip of six layout chips along the toolbar. The chips worked but answered the
/// wrong question: an arrangement is chosen once, when a workspace is made, and the rest of the
/// time six permanent controls competed with the panes for width. Splitting, closing and swapping
/// belong to a pane, which is where 1.x put them.
/// </para>
/// <para>
/// Everything here is positional, which is why it is worth testing: a pane's number is its place
/// in the tree, so closing one renumbers the rest and every Move or swap menu has to be rebuilt.
/// </para>
/// </remarks>
public class PaneActionsTests
{
    [AvaloniaFact]
    public async Task EachPaneIsNumberedAndOnlyOneSaysItIsActive()
    {
        await using var workspace = Create();

        Assert.Equal(Ui.Format(Ui.Shell.PaneActiveFormat, 1), workspace.Panes[0].PaneHeading);
        Assert.Equal(Ui.Format(Ui.Shell.PaneNumberFormat, 2), workspace.Panes[1].PaneHeading);

        workspace.Panes[1].IsActive = true;

        Assert.Equal(Ui.Format(Ui.Shell.PaneNumberFormat, 1), workspace.Panes[0].PaneHeading);
        Assert.Equal(Ui.Format(Ui.Shell.PaneActiveFormat, 2), workspace.Panes[1].PaneHeading);
    }

    [AvaloniaFact]
    public async Task SplittingAPaneAddsOneBesideIt()
    {
        await using var workspace = Create();

        workspace.Panes[0].SplitRightCommand!.Execute(null);

        Assert.Equal(3, workspace.Panes.Count);
        Assert.Equal(3, workspace.Layout.PaneCount);
    }

    /// <summary>Four is the most, and the menu says so by being unavailable rather than failing.</summary>
    [AvaloniaFact]
    public async Task APaneCannotBeSplitPastFour()
    {
        await using var workspace = Create();
        workspace.Preset = WorkspaceModel.Presets[5];

        Assert.Equal(WorkspaceLayoutModel.MaximumPanes, workspace.Panes.Count);
        Assert.All(workspace.Panes, pane =>
        {
            Assert.False(pane.SplitRightCommand!.CanExecute(null));
            Assert.False(pane.SplitBelowCommand!.CanExecute(null));
        });
    }

    /// <summary>
    /// Closing a pane renumbers the ones left.
    /// </summary>
    /// <remarks>
    /// A header that kept its first number would leave every Move or swap menu naming panes that
    /// are not where it says they are, which is the sort of thing that only goes wrong once
    /// somebody has four panes and is relying on the numbers to tell them apart.
    /// </remarks>
    [AvaloniaFact]
    public async Task ClosingAPaneRenumbersTheRest()
    {
        await using var workspace = Create();
        workspace.Preset = WorkspaceModel.Presets[3];
        var third = workspace.Panes[2];

        workspace.Panes[1].ClosePaneCommand!.Execute(null);

        Assert.Equal(2, workspace.Panes.Count);
        Assert.Same(third, workspace.Panes[1]);
        Assert.Equal(2, third.PaneNumber);
    }

    [AvaloniaFact]
    public async Task TheLastPaneCannotBeClosed()
    {
        await using var workspace = Create();
        workspace.Preset = WorkspaceModel.Presets[0];

        Assert.False(workspace.Panes[0].ClosePaneCommand!.CanExecute(null));
    }

    /// <summary>
    /// A pane's Move or swap menu offers every other pane, and never itself.
    /// </summary>
    /// <remarks>
    /// Swapping a pane with itself is the one entry that could do nothing at all, so it is left
    /// out rather than disabled.
    /// </remarks>
    [AvaloniaFact]
    public async Task TheMoveMenuOffersTheOtherPanesOnly()
    {
        await using var workspace = Create();
        workspace.Preset = WorkspaceModel.Presets[5];

        for (var index = 0; index < workspace.Panes.Count; index++)
        {
            var pane = workspace.Panes[index];
            Assert.True(pane.HasMoveTargets);
            Assert.Equal(3, pane.MoveTargets.Count);
            Assert.DoesNotContain(
                pane.MoveTargets,
                target => target.Title == Ui.Format(Ui.Shell.PaneNumberFormat, index + 1));

            // Swap, and the four edges it can dock on.
            Assert.All(pane.MoveTargets, target => Assert.Equal(5, target.Options.Count));
        }
    }

    /// <summary>A single pane has nowhere to move to, so the menu entry is dim.</summary>
    [AvaloniaFact]
    public async Task ASinglePaneHasNoMoveTargets()
    {
        await using var workspace = Create();
        workspace.Preset = WorkspaceModel.Presets[0];

        Assert.False(workspace.Panes[0].HasMoveTargets);
        Assert.Empty(workspace.Panes[0].MoveTargets);
    }

    [AvaloniaFact]
    public async Task SwappingTwoPanesExchangesTheirPlaces()
    {
        await using var workspace = Create();
        var first = workspace.Panes[0];
        var second = workspace.Panes[1];

        first.MoveTargets[0].Options[0].Command.Execute(null);

        Assert.Same(second, workspace.Panes[0]);
        Assert.Same(first, workspace.Panes[1]);
        Assert.Equal(1, second.PaneNumber);
        Assert.Equal(2, first.PaneNumber);
    }

    /// <summary>The connection bar can be put away once a pane is pointed where it belongs.</summary>
    [AvaloniaFact]
    public async Task TheConnectionBarCanBeHidden()
    {
        await using var workspace = Create();
        var pane = workspace.Panes[0];

        Assert.True(pane.ShowConnectionBar);

        pane.ShowConnectionBar = false;

        Assert.False(pane.ShowConnectionBar);
        Assert.True(workspace.Panes[1].ShowConnectionBar);
    }

    /// <summary>
    /// The Move or swap submenu really builds, two levels down.
    /// </summary>
    /// <remarks>
    /// Worth a test because the entries come from the view model through container styles rather
    /// than an ItemTemplate -- an ItemTemplate sets what a menu item shows, not what its children
    /// are, so a submenu of a submenu has to be bound on the generated MenuItem itself. A selector
    /// that did not match would leave a menu entry that opens onto nothing.
    /// </remarks>
    [AvaloniaFact]
    public void TheMoveMenuBuildsItsSubmenus()
    {
        var preview = ShellPreview.CreateOnWorkspace();
        var window = new MainWindow { DataContext = preview, Width = 1500, Height = 920 };
        window.Show();
        window.Measure(new global::Avalonia.Size(1500, 920));
        window.Arrange(new global::Avalonia.Rect(0, 0, 1500, 920));
        window.UpdateLayout();

        // The flyout's items only exist once it has been shown, and it opens into a popup of its
        // own rather than into the window, so it is reached through the button that owns it.
        var button = window.GetVisualDescendants().OfType<BrowserPaneView>().First()
            .GetVisualDescendants().OfType<Button>()
            .First(candidate => candidate.Flyout is MenuFlyout);
        var flyout = (MenuFlyout)button.Flyout!;
        flyout.ShowAt(button);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var menu = flyout.Items.OfType<MenuItem>()
            .First(item => Equals(item.Header, Ui.Shell.MoveOrSwapPane));
        menu.Open();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var targets = menu.ItemsSource!.OfType<PaneMoveTarget>().ToArray();
        Assert.NotEmpty(targets);

        // A menu item bound to data carries the item as its Header and renders it with the
        // template, so what is checked is the data reaching the container, not the text.
        var container = Assert.IsType<MenuItem>(menu.ContainerFromIndex(0));
        Assert.Same(targets[0], container.Header);
        Assert.Same(targets[0].Options, container.ItemsSource);

        // And the level below it: swap, and the four edges, each with a command behind it.
        container.Open();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var option = Assert.IsType<MenuItem>(container.ContainerFromIndex(0));

        Assert.Equal(targets[0].Options[0].Title, option.Header);
        Assert.Same(targets[0].Options[0].Command, option.Command);
    }

    private static WorkspaceModel Create() => new(
        static () => new BrowserPaneModel(),
        static () => new FakeTransferQueue(),
        static () => new FakeBrowsingAgent([]),
        static () => new FakeInspector());
}
