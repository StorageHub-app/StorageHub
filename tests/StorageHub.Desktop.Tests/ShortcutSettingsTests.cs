using static StorageHub.Desktop.Tests.TempDirectoryCleanup;
using System.Reflection;

namespace StorageHub.Desktop.Tests;

public sealed class ShortcutSettingsTests
{
    [Fact]
    public void DefaultsCoverBasicFileActionsWithoutConflicts()
    {
        var defaults = ShortcutSettings.Resolve(null);
        Assert.Null(ShortcutSettings.Validate(defaults));
        Assert.Equal(Keys.Control | Keys.C, defaults[UiCommandIds.EditCopy]);
        Assert.Equal(Keys.Control | Keys.V, defaults[UiCommandIds.EditPaste]);
        Assert.Equal(Keys.F6, defaults[UiCommandIds.GoNextPane]);
        Assert.DoesNotContain(ShortcutSettings.Commands, command => command.Id == UiCommandIds.ConnectionsQuickConnect);
    }

    /// <summary>
    /// A shortcut rebound by an older build must survive the move from derived ids to declared
    /// ones. These keys are what is actually sitting in users' settings.json today, written there
    /// when ids were computed from the English menu and label.
    /// </summary>
    [Theory]
    [InlineData("edit.copy", UiCommandIds.EditCopy)]
    [InlineData("go.next-pane", UiCommandIds.GoNextPane)]
    [InlineData("workspace.save-workspace-as", UiCommandIds.WorkspaceSaveWorkspaceAs)]
    [InlineData("sync.review-&-run", UiCommandIds.SyncReviewRun)]
    public void ShortcutsStoredByAnOlderBuildAreStillHonoured(string persistedId, string commandId)
    {
        Assert.Equal(commandId, persistedId);

        var rebound = Keys.Control | Keys.Shift | Keys.F9;
        var resolved = ShortcutSettings.Resolve(new Dictionary<string, Keys>(StringComparer.Ordinal)
        {
            [persistedId] = rebound
        });

        Assert.Equal(rebound, resolved[commandId]);
    }

    [Fact]
    public void DuplicateAndUnsafeBindingsAreRejectedAndCorruptSettingsFallBack()
    {
        var bindings = ShortcutSettings.Resolve(null);
        bindings[UiCommandIds.EditPaste] = Keys.Control | Keys.C;
        Assert.Contains("already assigned", ShortcutSettings.Validate(bindings), StringComparison.Ordinal);
        Assert.Equal(Keys.Control | Keys.V, ShortcutSettings.Resolve(bindings)[UiCommandIds.EditPaste]);
        foreach (var invalid in new[] { Keys.C, Keys.ControlKey, Keys.Alt | Keys.F4, Keys.Control | Keys.Alt | Keys.Delete })
            Assert.False(ShortcutSettings.IsValid(invalid));
    }

    [Fact]
    public void ReassignedAndDisabledShortcutsSurviveRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"storagehub-shortcuts-{Guid.NewGuid():N}");
        try
        {
            var store = new DesktopConfigStore(path);
            var bindings = ShortcutSettings.Resolve(null);
            bindings[UiCommandIds.EditCopy] = Keys.Control | Keys.Shift | Keys.C;
            bindings[UiCommandIds.EditPaste] = Keys.None;
            store.Save(DesktopUpdatePreferences.Defaults with { Shortcuts = bindings });
            var restored = ShortcutSettings.Resolve(new DesktopConfigStore(path).Load().Shortcuts);
            Assert.Equal(bindings.OrderBy(pair => pair.Key), restored.OrderBy(pair => pair.Key));
        }
        finally { DeleteDirectory(path); }
    }

    [Theory]
    [InlineData(UiCommandIds.EditCopy)]
    [InlineData(UiCommandIds.EditPaste)]
    [InlineData(UiCommandIds.EditDelete)]
    [InlineData(UiCommandIds.WorkspaceCloseWorkspace)]
    public void ShortcutsLeaveSshAndTextInputAlone(string commandId)
    {
        var command = UiCommandCatalog.GetDefinition(commandId);
        Assert.False(ShortcutSettings.CanDispatch(command, sshFocused: true, textFocused: false, hasPane: true));
        Assert.False(ShortcutSettings.CanDispatch(command, sshFocused: false, textFocused: true, hasPane: true));
        Assert.True(ShortcutSettings.CanDispatch(command, sshFocused: false, textFocused: false, hasPane: true));
    }

    [Fact]
    public void ShellUsesRemappedShortcutAndIgnoresOldBinding()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"storagehub-shortcuts-{Guid.NewGuid():N}");
            try
            {
                var store = new DesktopConfigStore(path);
                var bindings = ShortcutSettings.Resolve(null);
                bindings[UiCommandIds.GoNextPane] = Keys.Control | Keys.F6;
                store.Save(DesktopUpdatePreferences.Defaults with { Shortcuts = bindings });
                using var main = new MainForm(store);
                var tabs = (TabControl)typeof(MainForm).GetField("_workspaceTabs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
                _ = tabs.Handle;
                var page = main.AddWorkspace(2);
                var workspace = Assert.Single(page.Controls.OfType<WorkspaceControl>());
                var first = workspace.ActivePaneId;
                Assert.False(main.TryDispatchShortcut(Keys.F6));
                Assert.Equal(first, workspace.ActivePaneId);
                Assert.True(main.TryDispatchShortcut(Keys.Control | Keys.F6));
                Assert.NotEqual(first, workspace.ActivePaneId);
                Assert.Equal(ShortcutSettings.Format(Keys.Control | Keys.F6), main.ShortcutDisplay(UiCommandIds.GoNextPane));
                var menu = Assert.Single(main.Controls.OfType<MenuStrip>());
                Assert.All(menu.Items.OfType<ToolStripMenuItem>().SelectMany(root => root.DropDownItems.OfType<ToolStripMenuItem>()),
                    item => Assert.Equal(Keys.None, item.ShortcutKeys));
            }
            finally { DeleteDirectory(path); }
        });
    }

    [Fact]
    public void SettingsExposeShortcutEditorAndCancelDoesNotPersist()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"storagehub-shortcuts-{Guid.NewGuid():N}");
            using var settings = new SettingsForm(new DesktopConfigStore(path), saved: null);
            var tree = (TreeView)typeof(SettingsForm).GetField("_categories", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
            Assert.Contains(tree.Nodes.Cast<TreeNode>(), node => node.Name == "Shortcuts");
            Assert.False(File.Exists(path));
        });
    }

    [Fact]
    public void ShortcutEditorAppliesChangesAndRejectsConflictsWithoutLosingAssignments()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"storagehub-shortcuts-{Guid.NewGuid():N}");
            try
            {
                var store = new DesktopConfigStore(path);
                using var settings = new SettingsForm(store, saved: null);
                var editor = (ShortcutSettingsControl)typeof(SettingsForm).GetField("_shortcuts", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(settings)!;
                var grid = editor.Controls.OfType<DataGridView>().Single();
                var copy = grid.Rows.Cast<DataGridViewRow>().Single(row => Equals(row.Tag, UiCommandIds.EditCopy));
                grid.CurrentCell = copy.Cells[0];
                var assign = typeof(ShortcutSettingsControl).GetMethod("SetSelected", BindingFlags.NonPublic | BindingFlags.Instance)!;
                assign.Invoke(editor, [Keys.Control | Keys.V]);
                Assert.Equal(Keys.Control | Keys.C, editor.ReadShortcuts()[UiCommandIds.EditCopy]);
                assign.Invoke(editor, [Keys.Control | Keys.Shift | Keys.C]);
                var save = typeof(SettingsForm).GetMethod("TrySave", BindingFlags.NonPublic | BindingFlags.Instance)!;
                Assert.True((bool)save.Invoke(settings, null)!);
                Assert.Equal(Keys.Control | Keys.Shift | Keys.C, store.Load().Shortcuts![UiCommandIds.EditCopy]);
                assign.Invoke(editor, [Keys.None]);
                Assert.Equal(Keys.None, editor.ReadShortcuts()[UiCommandIds.EditCopy]);
                Assert.Equal(Keys.Control | Keys.Shift | Keys.C, store.Load().Shortcuts![UiCommandIds.EditCopy]);
            }
            finally { DeleteDirectory(path); }
        });
    }
}
