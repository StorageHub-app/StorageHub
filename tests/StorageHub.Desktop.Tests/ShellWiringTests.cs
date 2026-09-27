using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucide.Avalonia;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The menus, the status bar and the search box, now that they reach something.
/// </summary>
/// <remarks>
/// Each of these was drawn and inert. The Edit and Go menus showed entries whose work the panes had
/// been doing by button for weeks; three of the status bar's five cells held their startup values
/// for the life of the process; and the search box had no <c>Text</c> binding at all, so it
/// accepted typing and nothing read it.
/// </remarks>
public class ShellWiringTests
{
    /// <summary>
    /// Every pane operation the menu offers has somewhere to go.
    /// </summary>
    /// <remarks>
    /// Listed by hand rather than derived, so adding a command to the catalog does not quietly
    /// satisfy this: the point is that these particular ones work.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(UiCommandIds.EditNewFolder)]
    [InlineData(UiCommandIds.EditNewEmptyFile)]
    [InlineData(UiCommandIds.EditRename)]
    [InlineData(UiCommandIds.EditDelete)]
    [InlineData(UiCommandIds.EditCopy)]
    [InlineData(UiCommandIds.EditCut)]
    [InlineData(UiCommandIds.EditPaste)]
    [InlineData(UiCommandIds.EditSelectAll)]
    [InlineData(UiCommandIds.EditInvertSelection)]
    [InlineData(UiCommandIds.GoBack)]
    [InlineData(UiCommandIds.GoForward)]
    [InlineData(UiCommandIds.GoUp)]
    [InlineData(UiCommandIds.GoNextPane)]
    [InlineData(UiCommandIds.ViewRefresh)]
    public void ThePaneCommandsAreHandled(string id)
    {
        var model = ShellPreview.CreateOnWorkspace();

        Assert.True(model.Router.IsHandled(id), $"{id} has no handler.");
    }

    /// <summary>
    /// Go next pane moves the active one along, and wraps.
    /// </summary>
    /// <remarks>
    /// In the layout's order rather than the order the panes were made, so tabbing follows the
    /// order they are read in.
    /// </remarks>
    [AvaloniaFact]
    public void GoNextPaneMovesAlongAndWraps()
    {
        var model = ShellPreview.CreateOnWorkspace();
        var workspace = model.Workspaces.Select(static tab => tab.Workspace).First(static w => w is not null)!;
        workspace.Panes[0].IsActive = true;

        model.Router.For(UiCommandIds.GoNextPane).Execute(null);
        Assert.Same(workspace.Panes[1], workspace.Active);

        model.Router.For(UiCommandIds.GoNextPane).Execute(null);
        Assert.Same(workspace.Panes[0], workspace.Active);
    }

    /// <summary>
    /// A command whose pane cannot run it does nothing, rather than failing.
    /// </summary>
    /// <remarks>
    /// Routed through the pane's command rather than its method precisely so the menu entry
    /// declines in the same cases the button does -- a rename with nothing selected being the
    /// obvious one.
    /// </remarks>
    [AvaloniaFact]
    public void AMenuEntryDeclinesWhereItsButtonWould()
    {
        var model = ShellPreview.CreateOnWorkspace();
        var pane = model.Workspaces
            .Select(static tab => tab.Workspace)
            .First(static w => w is not null)!
            .Panes[0];

        Assert.False(pane.RenameCommand.CanExecute(null));

        // Nothing selected and no connection open: the entry runs and the pane simply declines.
        model.Router.For(UiCommandIds.EditRename).Execute(null);

        Assert.Empty(pane.SelectedRows);
    }

    /// <summary>
    /// The status bar reports the pane, rather than its startup values, and a pane with nothing
    /// chosen as "No connection" rather than the "/" its address bar holds.
    /// </summary>
    [AvaloniaFact]
    public async Task TheStatusBarFollowsTheActivePane()
    {
        var model = ShellPreview.CreateOnWorkspace();
        var workspace = model.Workspaces.Select(static tab => tab.Workspace).First(static w => w is not null)!;
        var pane = workspace.Panes[0];
        pane.IsActive = true;
        Assert.Equal(Ui.Shell.StatusNoConnection, model.ShellStatus.Location);

        await pane.OpenAsync(
            new PaneConnection(null, Ui.Pane.ConnectionsHome, LucideIconKind.House, PaneContentKind.ConnectionsHome),
            TestContext.Current.CancellationToken);

        Assert.Equal(pane.Path, model.ShellStatus.Location);
    }

    /// <summary>
    /// And the selection, which said "No selection" however much was picked.
    /// </summary>
    /// <remarks>
    /// Folders contribute no bytes, because nobody has counted what is inside one -- the same rule
    /// the pane's own summary uses, so the two cannot disagree.
    /// </remarks>
    [AvaloniaFact]
    public void TheStatusBarCountsWhatIsSelected()
    {
        var model = ShellPreview.CreateOnWorkspace();
        var pane = model.Workspaces
            .Select(static tab => tab.Workspace)
            .First(static w => w is not null)!
            .Panes[0];
        pane.IsActive = true;

        pane.Rows.Add(new BrowserListItem("a.bin", "1 KB", "File", "now", string.Empty,
            Location: "a.bin", Length: 1024));
        pane.Rows.Add(new BrowserListItem("folder", string.Empty, "Folder", "now", string.Empty,
            Location: "folder", IsContainer: true));
        pane.SelectedRows.Add(pane.Rows[0]);
        pane.SelectedRows.Add(pane.Rows[1]);

        Assert.Equal(2, model.ShellStatus.SelectedItems);
        Assert.Equal(1024, model.ShellStatus.SelectedBytes);
    }

    /// <summary>The parent row is not part of a selection, here as everywhere else.</summary>
    [AvaloniaFact]
    public void TheStatusBarIgnoresTheParentRow()
    {
        var model = ShellPreview.CreateOnWorkspace();
        var pane = model.Workspaces
            .Select(static tab => tab.Workspace)
            .First(static w => w is not null)!
            .Panes[0];
        pane.IsActive = true;

        pane.Rows.Add(BrowserParentNavigation.Item);
        pane.SelectedRows.Add(pane.Rows[0]);

        Assert.Equal(0, model.ShellStatus.SelectedItems);
    }

    /// <summary>
    /// The connections panel opens the way it was left, and shows, hides and moves as 1.x's did:
    /// View > Connections Panel ticked while it shows, Ctrl+B, Move Connections Panel keeping its
    /// width, and the splitter, each saved as it happens.
    /// </summary>
    /// <remarks>
    /// The entry is clicked with the mouse: on a real click Avalonia ticks a toggle item itself as
    /// well as running its command, which a raised Click event does not, and bound the wrong way
    /// the two would undo each other.
    /// </remarks>
    [AvaloniaFact]
    public void TheConnectionsPanelShowsHidesMovesAndIsRemembered()
    {
        var saved = DesktopUpdatePreferences.Defaults with
        {
            ConnectionsPanelSide = ConnectionsPanelSide.Right,
            ConnectionsPanelWidth = 360,
        };
        var model = ShellPreview.CreateOnWorkspace();
        model.ConnectionsPanel.Restore(saved);
        model.ConnectionsPanel.Persist = change => saved = change(saved);
        var window = new MainWindow { DataContext = model, Width = 1500, Height = 920 };
        window.Show();
        var panel = window.GetControl<Border>("PART_ConnectionsPanel");
        Settle(window);

        // On the right, at the width it was left at.
        Assert.Equal(360, panel.Bounds.Width, 1);
        Assert.Equal(1500 - 360, panel.Bounds.X, 1);

        // Ticked in the View menu; clicking the entry hides the panel and takes the tick away.
        var view = window.GetControl<Menu>("PART_Menu").GetVisualDescendants().OfType<MenuItem>()
            .Single(static item => item.DataContext is MenuSection { Menu: UiMenuId.View });
        view.Open();
        Dispatcher.UIThread.RunJobs();
        var entry = view.Items.OfType<CommandEntry>().Single(static e => e.Id == UiCommandIds.ViewConnectionsPanel);
        var item = Assert.IsType<MenuItem>(view.ContainerFromItem(entry));
        Assert.True(item.IsChecked);
        Click(item);
        Settle(window);

        Assert.False(panel.IsVisible);
        Assert.False(item.IsChecked);
        Assert.False(saved.ConnectionsPanelVisible);

        // Moved while hidden, then Ctrl+B: back on the left, the same width, and ticked again.
        model.Router.For(UiCommandIds.ViewMoveConnectionsPanel).Execute(null);
        window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.Control);
        Settle(window);

        Assert.True(panel.IsVisible);
        Assert.True(item.IsChecked);
        Assert.Equal(0, panel.Bounds.X, 1);
        Assert.Equal(360, panel.Bounds.Width, 1);
        Assert.Equal((ConnectionsPanelSide.Left, true), (saved.ConnectionsPanelSide, saved.ConnectionsPanelVisible));

        // A drag of the splitter is saved once it is let go, and stops at 1.x's widest; so is a
        // move on the arrow keys.
        var splitter = window.GetControl<GridSplitter>("PART_ConnectionsSplitter");
        Drag(40);
        Assert.Equal(400, saved.ConnectionsPanelWidth);
        Drag(400);
        Assert.Equal(DesktopUpdatePreferences.MaximumConnectionsPanelWidth, saved.ConnectionsPanelWidth);
        splitter.Focus();
        window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        Settle(window);
        Assert.Equal(DesktopUpdatePreferences.MaximumConnectionsPanelWidth - 10, saved.ConnectionsPanelWidth);

        void Drag(double by)
        {
            splitter.RaiseEvent(new VectorEventArgs { RoutedEvent = Thumb.DragStartedEvent });
            splitter.RaiseEvent(new VectorEventArgs { RoutedEvent = Thumb.DragDeltaEvent, Vector = new Vector(by, 0) });
            splitter.RaiseEvent(new VectorEventArgs { RoutedEvent = Thumb.DragCompletedEvent });
            Settle(window);
        }

        static void Settle(Window window)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }

        static void Click(MenuItem item)
        {
            var root = TopLevel.GetTopLevel(item)!;
            var centre = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), root)!.Value;
            root.MouseDown(centre, MouseButton.Left);
            root.MouseUp(centre, MouseButton.Left);
        }
    }
}
