using Avalonia.Input;

namespace StorageHub.Desktop.Tests;

public sealed class UiCommandCatalogTests
{
    /// <summary>
    /// The English menu and label of every command as of settings schema 15.
    /// </summary>
    /// <remarks>
    /// Frozen on purpose. This table does not describe what the menus say — it describes what is
    /// already written into users' settings.json, because command ids used to be derived from
    /// these strings and a user's rebound shortcuts are stored against them. Translating a label
    /// must not move an id, so when a label changes, change it in <c>CommandStrings</c> and leave
    /// this table alone.
    /// </remarks>
    private static readonly (string Menu, string Label)[] LegacyCommands =
    [
        ("Workspace", "New Workspace..."),
        ("Workspace", "Open Workspace..."),
        ("Workspace", "Save Workspace"),
        ("Workspace", "Save Workspace As..."),
        ("Workspace", "Rename Workspace..."),
        ("Workspace", "Close Workspace"),
        ("Workspace", "Exit"),
        ("Edit", "New Folder"),
        ("Edit", "New Empty File..."),
        ("Edit", "Cut"),
        ("Edit", "Copy"),
        ("Edit", "Paste"),
        ("Edit", "Rename"),
        ("Edit", "Batch Rename..."),
        ("Edit", "Delete"),
        ("Edit", "Select All"),
        ("Edit", "Invert Selection"),
        ("Edit", "Properties"),
        ("View", "Refresh"),
        ("View", "Connections Panel"),
        ("View", "Move Connections Panel"),
        ("View", "Directory Tree"),
        ("View", "Transfer Queue"),
        ("View", "Session Log"),
        ("View", "Hidden Files"),
        ("View", "Theme"),
        ("Go", "Back"),
        ("Go", "Forward"),
        ("Go", "Up"),
        ("Go", "Focus Address"),
        ("Go", "Next Pane"),
        ("Go", "Home"),
        ("Go", "History"),
        ("Go", "Favorites"),
        ("Connections", "New Connection..."),
        ("Connections", "Key Store..."),
        ("Connections", "Quick Connect..."),
        ("Connections", "Reconnect"),
        ("Connections", "Disconnect"),
        ("Connections", "Test Connection"),
        ("Transfer", "Start Queue"),
        ("Transfer", "Pause All"),
        ("Transfer", "Resume All"),
        ("Transfer", "Cancel Selected"),
        ("Transfer", "Speed Limits..."),
        ("Sync", "Compare Panes"),
        ("Sync", "Review & Run..."),
        ("Sync", "Sync Profiles..."),
        ("Sync", "Schedules..."),
        ("Tools", "Search..."),
        ("Tools", "Background Agent..."),
        ("Tools", "Checksums..."),
        ("Tools", "Settings..."),
        ("Tools", "Export Settings..."),
        ("Tools", "Import Settings..."),
        ("Tools", "Logs..."),
        ("Tools", "Diagnostics..."),
        ("Help", "Check for Updates..."),
        ("Help", "Keyboard Shortcuts"),
        ("Help", "Documentation"),
        ("Help", "Report Issue"),
        ("Help", "About StorageHub")
    ];

    /// <summary>
    /// The one test that makes "no settings migration" true rather than hoped for.
    /// </summary>
    /// <remarks>
    /// Ids were previously computed from the English menu and label at startup. They are now
    /// declared constants, and every one of them must still equal what that computation produced,
    /// or every user's customised keyboard shortcuts silently revert to their defaults. The
    /// derivation is reproduced here exactly, quirks included: only a literal three-dot ellipsis
    /// is removed, it is removed before spaces become hyphens, and an ampersand survives.
    /// </remarks>
    [Fact]
    public void CommandIdsStillMatchTheIdsAlreadyWrittenIntoSettingsFiles()
    {
        var expected = LegacyCommands
            .Select(command => $"{command.Menu}.{command.Label}"
                .Replace("...", string.Empty, StringComparison.Ordinal)
                .Replace(' ', '-')
                .ToLowerInvariant())
            .ToArray();

        Assert.Equal(expected, UiCommandCatalog.Specs.Select(spec => spec.Id).ToArray());
    }

    [Fact]
    public void MenusMatchTheProductNavigationContract()
    {
        Assert.Equal(
            [
                UiMenuId.Workspace, UiMenuId.Edit, UiMenuId.View, UiMenuId.Go, UiMenuId.Connections,
                UiMenuId.Transfer, UiMenuId.Sync, UiMenuId.Tools, UiMenuId.Help
            ],
            UiCommandCatalog.Menus);
    }

    [Theory]
    [InlineData(UiMenuId.Connections, UiCommandIds.ConnectionsNewConnection)]
    [InlineData(UiMenuId.Transfer, UiCommandIds.TransferPauseAll)]
    [InlineData(UiMenuId.Sync, UiCommandIds.SyncReviewRun)]
    [InlineData(UiMenuId.Tools, UiCommandIds.ToolsDiagnostics)]
    internal void CriticalCommandsAreReachableFromTheirMenu(UiMenuId menu, string commandId)
    {
        Assert.Contains(UiCommandCatalog.ForMenu(menu), definition => definition.Id == commandId);
    }

    [Fact]
    public void EveryMenuCommandHasAUniquePresentationDefinition()
    {
        var definitions = UiCommandCatalog.Definitions;

        Assert.Equal(LegacyCommands.Length, definitions.Count);
        Assert.Equal(
            definitions.Count,
            definitions.Select(definition => definition.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(definitions, definition => Assert.False(string.IsNullOrWhiteSpace(definition.Label)));
        Assert.All(definitions, definition => Assert.False(string.IsNullOrWhiteSpace(definition.Description)));
    }

    [Theory]
    [InlineData(UiCommandIds.WorkspaceNewWorkspace, Key.T, KeyModifiers.Control)]
    [InlineData(UiCommandIds.WorkspaceSaveWorkspaceAs, Key.S, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData(UiCommandIds.SyncSchedules, Key.S, KeyModifiers.Control | KeyModifiers.Alt)]
    [InlineData(UiCommandIds.WorkspaceOpenWorkspace, Key.O, KeyModifiers.Control)]
    [InlineData(UiCommandIds.WorkspaceSaveWorkspace, Key.S, KeyModifiers.Control)]
    [InlineData(UiCommandIds.ViewRefresh, Key.F5, KeyModifiers.None)]
    [InlineData(UiCommandIds.EditNewFolder, Key.N, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData(UiCommandIds.EditRename, Key.F2, KeyModifiers.None)]
    [InlineData(UiCommandIds.EditDelete, Key.Delete, KeyModifiers.None)]
    [InlineData(UiCommandIds.EditInvertSelection, Key.I, KeyModifiers.Control)]
    [InlineData(UiCommandIds.ConnectionsQuickConnect, Key.K, KeyModifiers.Control)]
    [InlineData(UiCommandIds.SyncComparePanes, Key.D, KeyModifiers.Control)]
    public void CriticalCommandsExposeKeyboardShortcuts(string commandId, Key key, KeyModifiers modifiers)
    {
        Assert.Equal(
            new KeyGesture(key, modifiers),
            UiCommandCatalog.GetDefinition(commandId).Shortcut);
    }

    [Fact]
    public void KeyboardShortcutsDoNotInvokeCompetingCommands()
    {
        var conflicts = UiCommandCatalog.Definitions
            .Where(definition => definition.Shortcut is not null)
            .GroupBy(definition => definition.Shortcut!)
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(", ", group.Select(definition => definition.Label)));

        Assert.Empty(conflicts);
    }

    /// <summary>
    /// Dispatch, availability and shortcut persistence all key on the id, so an id that no
    /// command declares is a command that can never run.
    /// </summary>
    [Fact]
    public void EveryDeclaredIdConstantBelongsToACommand()
    {
        var declared = typeof(UiCommandIds)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                       System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.Equal(
            UiCommandCatalog.Specs.Select(spec => spec.Id).OrderBy(id => id, StringComparer.Ordinal),
            declared.OrderBy(id => id, StringComparer.Ordinal));
    }

    /// <summary>
    /// A menu where only some rows are illustrated reads as unfinished, and the icon column is
    /// what makes these menus scannable.
    /// </summary>
    /// <remarks>
    /// Kept from the theme suite, which was otherwise asserting on how WinForms rasterised and
    /// recoloured those glyphs. This part is about the catalog rather than the painting, so it
    /// outlives the shell that drew it.
    /// </remarks>
    [Fact]
    public void EveryMenuCommandCarriesAnIcon()
    {
        Assert.All(UiCommandCatalog.Definitions, definition => Assert.NotNull(definition.Glyph));
    }
}
