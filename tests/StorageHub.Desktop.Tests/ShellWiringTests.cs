using Avalonia.Headless.XUnit;
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

    /// <summary>The status bar reports the pane, rather than its startup values.</summary>
    [AvaloniaFact]
    public void TheStatusBarFollowsTheActivePane()
    {
        var model = ShellPreview.CreateOnWorkspace();
        var workspace = model.Workspaces.Select(static tab => tab.Workspace).First(static w => w is not null)!;
        var pane = workspace.Panes[0];
        pane.IsActive = true;

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
}
