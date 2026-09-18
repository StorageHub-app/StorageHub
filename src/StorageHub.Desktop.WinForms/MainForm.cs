using System.Globalization;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

public sealed class MainForm : Form
{
    private readonly AgentStatusMonitor _agentMonitor = new();
    private readonly ManualTransferController _manualTransfers = new();
    private readonly NamedPipeTransferQueueAgentClient _shellTransfers = new();
    private readonly RecursiveTransferController _recursiveTransfers;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ToolStripStatusLabel _locationStatus = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _selectionStatus = new();
    private readonly ToolStripStatusLabel _speedStatus = new();
    private readonly ToolStripStatusLabel _queueStatus = new();
    private readonly ToolStripStatusLabel _agentStatus = new() { AccessibleName = Ui.Shell.AgentStatus };
    private readonly ToolStripStatusLabel _updateStatus = new()
    {
        AccessibleName = Ui.Shell.UpdateStatus,
        IsLink = true,
        ToolTipText = Ui.Shell.CheckForUpdatesTooltip
    };
    private readonly DesktopConfigStore _updatePreferencesStore;
    private readonly MenuStrip _menu;
    private readonly DesktopUpdater _updater;
    private readonly PackagedDesktopLifecycle? _packagedLifecycle;

    /// <summary>The most recent agent report, so the agent dialog opens with real state.</summary>
    private AgentMonitorStatus? _lastAgentStatus;
    private readonly bool _explorerDropBrokerAvailable;
    private readonly TabControl _workspaceTabs;
    private readonly ConnectionsPanelControl _connectionsPanel;
    private ToolStripButton? _connectionsPanelButton;
    private ToolStrip? _toolbar;
    private IReadOnlyList<string>? _toolbarItems;
    private ToolbarLabelStyle _toolbarLabels = ToolbarLabelStyle.IconsOnly;
    private readonly SplitContainer _shellSplit;
    private readonly SplitContainer _workspaceSplit;
    private ConnectionsPanelSide _connectionsPanelSide = ConnectionsPanelSide.Left;
    private bool _restoringPanelLayout;
    private readonly TransferQueueControl _transferQueue;
    private readonly OverviewDashboardControl _overview;
    private readonly SyncTasksOverviewControl _syncTasks;
    private readonly ExternalEditorController _externalEditor;
    private readonly Icon? _windowIcon;
    private ShellStatusSnapshot _status = ShellStatusSnapshot.Initial;

    /// <summary>
    /// Drags that have left a pane but have no destination yet. Desktop-local and never durable,
    /// shared so the queue and the activity log show the same pending gestures.
    /// </summary>
    private readonly PendingDropRegistry _pendingDrops = new();
    private BrowserPaneControl? _activePane;
    private PaneClipboardSnapshot? _paneClipboard;
    private bool _changingWorkspaceTabs;
    private bool _workspaceAddPending;
    private bool _monitorStarted;
    private bool _updaterStarted;
    private bool _agentHostModeChecked;
    private bool _agentRestartPending;
    private bool _agentRecoveryInProgress;
    private bool _restartAfterClose;
    private int _nextWorkspaceNumber = 1;

    public MainForm()
        : this(DesktopConfigStore.CreateDefault())
    {
    }

    internal MainForm(
        DesktopConfigStore updatePreferencesStore,
        IDesktopUpdateEngineFactory? updateEngineFactory = null,
        PackagedDesktopLifecycle? packagedLifecycle = null,
        bool explorerDropBrokerAvailable = true)
    {
        _updatePreferencesStore = updatePreferencesStore;
        _packagedLifecycle = packagedLifecycle;

        // Lets any terminal restart a stale agent and retry, wherever it was opened from.
        SshTerminalForm.DefaultAgentLifecycleProvider = () =>
            _packagedLifecycle is { } agentLifecycle
                ? new PackagedAgentLifecycleController(agentLifecycle)
                : null;
        _explorerDropBrokerAvailable = explorerDropBrokerAvailable;
        _updater = new DesktopUpdater(updatePreferencesStore, updateEngineFactory);
        _recursiveTransfers = new RecursiveTransferController(_manualTransfers);
        _externalEditor = new ExternalEditorController(updatePreferencesStore);
        _externalEditor.FileUploaded += ExternalEditorFileUploaded;
        Text = "StorageHub";
        _windowIcon = LoadWindowIcon();
        if (_windowIcon is not null)
        {
            Icon = _windowIcon;
        }
        AccessibleName = Ui.Shell.ShellAccessibleName;
        AccessibleDescription = Ui.Shell.ShellAccessibleDescription;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1120, 720);
        Size = new Size(1500, 920);
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = StorageHubTheme.Canvas;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        StorageHubTheme.Register(this);

        _menu = BuildMenu();
        MainMenuStrip = _menu;
        var toolbar = BuildToolbar();
        _toolbar = toolbar;

        _workspaceTabs = new ThemedTabControl
        {
            Dock = DockStyle.Fill,
            AccessibleName = Ui.Shell.WorkspaceTabs,
            AccessibleDescription = Ui.Shell.NamedWorkspacesHint,
            HotTrack = true,
            ShowToolTips = true,

            // WorkspaceTabsDrawItem draws these headers, including the folder icon and the close
            // cross, so the shared renderer must not also paint them.
            OwnsItemRendering = true
        };
        StorageHubTheme.ConfigureTabs(_workspaceTabs);
        _workspaceTabs.DrawItem += WorkspaceTabsDrawItem;
        _workspaceTabs.MouseDown += WorkspaceTabsMouseDown;
        _overview = new OverviewDashboardControl();
        _overview.NewWorkspaceRequested += (_, _) => ChooseAndAddWorkspace();
        _overview.ConnectionsRequested += (_, _) => ShowConnectionManager();
        _overview.SyncTasksRequested += (_, _) => _workspaceTabs.SelectedIndex = 1;
        _overview.WorkspaceOpenRequested += async (_, args) =>
            _ = await OpenWorkspacePathAsync(args.Shortcut.Entry.Path).ConfigureAwait(true);
        _overview.WorkspacePinToggleRequested += (_, args) =>
            ToggleWorkspacePin(args.Shortcut.Entry.Path, args.Shortcut.Entry.Name);
        _overview.WorkspaceRemoveRequested += (_, args) => ForgetWorkspace(args.Shortcut.Entry.Path);
        _syncTasks = new SyncTasksOverviewControl();
        _syncTasks.NewProfileRequested += (_, _) => ShowSyncProfileEditor();
        _syncTasks.SchedulesRequested += (_, _) => ShowSchedules();
        _workspaceTabs.TabPages.Add(CreateFixedTab(Ui.Shell.TabWelcome, UiGlyph.Home, _overview));
        _workspaceTabs.TabPages.Add(CreateFixedTab(Ui.Shell.TabSyncTasks, UiGlyph.Compare, _syncTasks));
        _workspaceTabs.TabPages.Add(new TabPage("+")
        {
            ToolTipText = Ui.Shell.NewWorkspaceTab,
            AccessibleName = Ui.Shell.NewWorkspaceTab
        });
        _workspaceTabs.Selecting += WorkspaceTabsSelecting;
        _workspaceTabs.SelectedIndexChanged += (_, _) => UpdateWorkspaceCommandState();
        PublishWorkspaceShortcuts(LoadPreferences());

        var mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            Size = new Size(1400, 760),
            SplitterDistance = 615,
            Panel2MinSize = 145,
            BackColor = StorageHubTheme.Border,
            AccessibleName = Ui.Shell.WorkspaceAndJobQueue
        };
        mainSplit.Panel1.BackColor = StorageHubTheme.Canvas;
        // A hair of canvas above the tab strip, to match the gap beneath it: docked straight
        // under the toolbar the headers sat flush against it and read as part of the toolbar.
        mainSplit.Panel1.Padding = new Padding(0, LogicalToDeviceUnits(4), 0, 0);
        mainSplit.Panel2.BackColor = StorageHubTheme.Surface;
        mainSplit.Panel1.Controls.Add(_workspaceTabs);
        _transferQueue = new TransferQueueControl(_updatePreferencesStore) { PendingDrops = _pendingDrops };
        _transferQueue.QueueCountsChanged += TransferQueueCountsChanged;
        _manualTransfers.TransfersEnqueued += ManualTransfersEnqueued;
        mainSplit.Panel2.Controls.Add(_transferQueue);

        _workspaceSplit = mainSplit;
        _connectionsPanel = new ConnectionsPanelControl
        {
            ShowFavoritesInTheirFolders = _updatePreferencesStore.Load().ShowFavoritesInTheirFolders,
            FolderIcons = _updatePreferencesStore.Load().FolderIcons
                ?? new Dictionary<string, string>(StringComparer.Ordinal)
        };
        _connectionsPanel.FolderIconsChanged += (_, icons) =>
            _updatePreferencesStore.Save(_updatePreferencesStore.Load() with { FolderIcons = icons });
        _connectionsPanel.ConnectionActivated += async (_, args) =>
            await OpenConnectionInWorkspaceAsync(args.Connection, args.InNewPane).ConfigureAwait(true);
        _connectionsPanel.EditRequested += (_, request) => ShowConnectionManager(request.ConnectionId, request.Tab);
        _connectionsPanel.ConnectionsChanged += (_, _) => _ = _overview.RefreshAsync(_lifetime.Token);
        _connectionsPanel.MoveSideRequested += (_, _) => ToggleConnectionsPanelSide();
        _connectionsPanel.HideRequested += (_, _) => SetConnectionsPanelVisible(false);

        _shellSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            Size = new Size(1500, 760),
            BackColor = StorageHubTheme.Border,
            AccessibleName = Ui.Shell.ConnectionsAndWorkspaces
        };
        ApplyConnectionsPanelSide(
            ConnectionsPanelSide.Left,
            DesktopUpdatePreferences.DefaultConnectionsPanelWidth,
            visible: true);
        _shellSplit.SplitterMoved += ShellSplitterMoved;

        var statusStrip = BuildStatusStrip();
        Controls.Add(_shellSplit);
        Controls.Add(statusStrip);
        Controls.Add(toolbar);
        Controls.Add(_menu);

        ApplyStatus(_status);
        _agentMonitor.StatusChanged += AgentMonitorStatusChanged;
        _updater.StatusChanged += UpdaterStatusChanged;
        _updater.RestartRequested += UpdaterRestartRequested;
        _updateStatus.Click += UpdateStatusClicked;
        ApplyUpdateStatus(_updater.Snapshot);
        UpdateWorkspaceCommandState();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _menu.Renderer = DesktopAppearanceService.MenuRenderer;
        RestoreConnectionsPanelLayout();
        _ = _connectionsPanel.RefreshAsync(_lifetime.Token);
        if (!_monitorStarted)
        {
            _monitorStarted = true;
            _agentMonitor.Start();
        }

        if (!_updaterStarted)
        {
            _updaterStarted = true;
            _ = RunAutomaticUpdaterAsync();
        }

        _ = _overview.RefreshAsync(_lifetime.Token);
        if (!_agentHostModeChecked)
        {
            _agentHostModeChecked = true;
            _ = OfferAgentHostModeAsync();
        }
    }

    /// <summary>
    /// Asks the agent to stop as the shell exits, with a bounded wait.
    ///
    /// A request rather than a kill: the agent flushes its database and releases its pipes on a
    /// graceful stop, and a transfer interrupted mid-write is worse than an agent that lingers a
    /// few seconds. If it declines, it exits with the session anyway.
    /// </summary>
    private void StopSessionAgentOnExit()
    {
        try
        {
            // Absent when the shell is hosted without a packaged lifecycle, as the tests do; there
            // is then no agent of ours to retire.
            if (_packagedLifecycle is not { } lifecycle)
            {
                return;
            }

            _ = lifecycle
                .TryStopAgentAsync(AgentShutdownReason.Restart)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Never block or fail an exit over this.
            _ = error;
        }
    }

    /// <summary>
    /// Offers the Windows service once, the first time a build that supports it starts.
    ///
    /// Asked here rather than during startup so the shell is already usable behind it, and the
    /// answer is recorded before the question is shown, so a crash or a forced close cannot turn a
    /// one-time prompt into one that returns on every launch.
    /// </summary>
    private async Task OfferAgentHostModeAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            if (AgentHostModePrompt.AskOnce(
                    this,
                    StorageHub.Desktop.Framework.DesktopFrameworkPaths.Resolve().ApplicationRoot)
                is not { } chosen)
            {
                return;
            }

            var controller = new AgentHostModeController(
                PackagedDesktopLifecycle.CreateDefault().AgentExecutablePath);
            var result = await controller
                .ApplyAsync(chosen, _lifetime.Token)
                .ConfigureAwait(true);
            _ = MessageBox.Show(
                this,
                result.Summary,
                Ui.Settings.AgentModeStartupQuestionTitle,
                MessageBoxButtons.OK,
                result.Succeeded ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // A failed offer must never stop the shell from opening.
            _ = error;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!e.Cancel && Visible)
        {
            foreach (var page in _workspaceTabs.TabPages.Cast<TabPage>().ToArray())
            {
                if (page.Controls.OfType<WorkspaceControl>().SingleOrDefault() is not { IsDirty: true } workspace) continue;
                var choice = MessageBox.Show(
                    this,
                    Ui.Format(Ui.Dialogs.SaveWorkspaceChangesPromptFormat, workspace.WorkspaceName),
                    Ui.Dialogs.ExitCaption,
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);
                if (choice == DialogResult.Cancel || choice == DialogResult.Yes && !SaveWorkspace(workspace, page))
                {
                    e.Cancel = true;
                    break;
                }
            }
        }
        if (_restartAfterClose)
        {
            // Only once the close has survived the prompts above: a cancelled exit has to leave
            // the shell exactly as it was, with no restart queued behind it.
            _restartAfterClose = false;
            if (!e.Cancel)
            {
                DesktopRestart.Request();
            }
        }

        // "Only while StorageHub is open" means exactly that, so the agent is retired with the
        // window. Skipped on a restart, where the next process expects to find it still there,
        // and after a cancelled close, which must change nothing.
        if (!e.Cancel && !_restartAfterClose && OperatingSystem.IsWindows() &&
            DesktopAgentHost.DesktopStopsAgent)
        {
            StopSessionAgentOnExit();
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lifetime.Cancel();
            _manualTransfers.TransfersEnqueued -= ManualTransfersEnqueued;
            _externalEditor.FileUploaded -= ExternalEditorFileUploaded;
            _workspaceTabs.Selecting -= WorkspaceTabsSelecting;
            _workspaceTabs.DrawItem -= WorkspaceTabsDrawItem;
            _workspaceTabs.MouseDown -= WorkspaceTabsMouseDown;
            _agentMonitor.StatusChanged -= AgentMonitorStatusChanged;
            _updater.StatusChanged -= UpdaterStatusChanged;
            _updater.RestartRequested -= UpdaterRestartRequested;
            _updateStatus.Click -= UpdateStatusClicked;
            _updater.Dispose();
            _agentMonitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _recursiveTransfers.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _manualTransfers.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _shellTransfers.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _externalEditor.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _lifetime.Dispose();
            _windowIcon?.Dispose();
        }

        base.Dispose(disposing);
    }

    internal string ShortcutDisplay(string commandId)
    {
        var keys = ShortcutSettings.Resolve(_updater.Preferences.Shortcuts).GetValueOrDefault(commandId);
        return keys == Keys.None ? string.Empty : ShortcutSettings.Format(keys);
    }

    private void RefreshShortcutPresentation()
    {
        foreach (var root in _menu.Items.OfType<ToolStripMenuItem>())
            foreach (var item in root.DropDownItems.OfType<ToolStripMenuItem>())
                if (item.Tag is string id) item.ShortcutKeyDisplayString = ShortcutDisplay(id);
        foreach (var pane in FindControls<BrowserPaneControl>(_workspaceTabs)) pane.RefreshCommandState();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (TryDispatchShortcut(keyData)) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    internal bool TryDispatchShortcut(Keys keyData)
    {
        var bindings = ShortcutSettings.Resolve(_updater.Preferences.Shortcuts);
        var command = ShortcutSettings.Commands.FirstOrDefault(candidate => bindings[candidate.Id] == keyData && keyData != Keys.None);
        if (command is null) return false;
        var focused = (Control)this;
        while (focused.Controls.Cast<Control>().FirstOrDefault(child => child.ContainsFocus) is { } child) focused = child;
        var sshFocused = false;
        var textFocused = false;
        for (Control? control = focused; control is not null; control = control.Parent)
        {
            sshFocused |= control is SshTerminalForm;
            // TerminalView is not a TextBoxBase, so without naming it here every app shortcut
            // would start firing in the middle of typing at a remote shell.
            // The app's own inputs host a text box, so a focused editor still matches above --
            // but a drop-down in list mode takes focus itself and is none of these, which is
            // what would let a plain letter run a menu command while a list was open.
            textFocused |= control is TextBoxBase or ComboBox or UpDownBase or TerminalView
                or StorageHubChoiceField or StorageHubNumberField or StorageHubTextField;
        }
        var pane = GetActivePane();
        if (ShortcutSettings.IsPaneCommand(command) && pane?.IsSshClient == true) return false;
        if (!ShortcutSettings.CanDispatch(command, sshFocused, textFocused, pane is not null)) return false;
        var item = _menu.Items.OfType<ToolStripMenuItem>()
            .SelectMany(root => root.DropDownItems.OfType<ToolStripMenuItem>())
            .FirstOrDefault(candidate => Equals(candidate.Tag, command.Id));
        if (item?.Enabled != true) return false;
        item.PerformClick();
        return true;
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip
        {
            AccessibleName = "Main menu",
            BackColor = StorageHubTheme.Surface,
            ForeColor = StorageHubTheme.Text,
            Renderer = DesktopAppearanceService.MenuRenderer,
            ShowItemToolTips = true,
            Padding = new Padding(
                LogicalToDeviceUnits(8),
                LogicalToDeviceUnits(3),
                LogicalToDeviceUnits(8),
                LogicalToDeviceUnits(3))
        };
        foreach (var menuId in UiCommandCatalog.Menus)
        {
            var menuName = MenuTitle(menuId);
            var root = new ToolStripMenuItem(menuName)
            {
                AccessibleName = $"{menuName} menu",
                // Room enough that each title reads as its own target: the gap between two of them
                // is the sum of both margins and both paddings, so half of this shows up twice.
                Margin = new Padding(LogicalToDeviceUnits(2), 0, LogicalToDeviceUnits(2), 0),
                Padding = new Padding(
                    LogicalToDeviceUnits(7),
                    LogicalToDeviceUnits(3),
                    LogicalToDeviceUnits(7),
                    LogicalToDeviceUnits(3))
            };
            foreach (var definition in UiCommandCatalog.ForMenu(menuId))
            {
                if (!IsAvailableCommand(definition.Id))
                {
                    continue;
                }

                var item = new ToolStripMenuItem(definition.Label)
                {
                    Tag = definition.Id,
                    ShortcutKeyDisplayString = ShortcutDisplay(definition.Id),
                    ToolTipText = definition.Description,
                    AccessibleName = definition.Label,
                    AccessibleDescription = definition.Description
                };
                if (definition.Glyph is { } glyph)
                {
                    _ = StorageHubTheme.TrackIcon(item, glyph, 16, definition.Tone, DeviceDpi / 96F);
                }
                WireCommand(item, definition.Id);
                root.DropDownItems.Add(item);
            }

            AttachDynamicSection(root, menuId);
            if (root.DropDownItems.Count > 0)
            {
                menu.Items.Add(root);
            }
            else
            {
                root.Dispose();
            }
        }

        return menu;
    }

    /// <summary>
    /// Tags an item the menu rebuilds on every open. Deliberately not a string: the three loops
    /// that walk the menus by <see cref="ToolStripItem.Tag"/> — shortcut display, shortcut
    /// dispatch, and workspace enable state — all compare against a command id, so a non-string
    /// tag makes them structurally incapable of touching a dynamic entry.
    /// </summary>
    private sealed record DynamicMenuItem(string Section, object? Payload);

    /// <summary>
    /// Subscribes the menus whose contents depend on state that changes while the app runs.
    ///
    /// These entries are built here rather than declared in <see cref="UiCommandCatalog"/>: that
    /// catalog is a static, uniquely-keyed contract which also drives the rebindable-shortcut
    /// editor, and a variable number of entries — two of which can share a workspace name — would
    /// both break its uniqueness invariant and inject unbindable rows into that editor.
    /// </summary>
    private void AttachDynamicSection(ToolStripMenuItem root, UiMenuId menu)
    {
        switch (menu)
        {
            case UiMenuId.Workspace:
                root.DropDownOpening += (_, _) => RebuildWorkspaceSection(root);
                break;
            case UiMenuId.Go:
                root.DropDownOpening += (_, _) => RebuildFavoritesSection(root);
                break;
            default:
                break;
        }
    }

    /// <summary>The menu's title in the current language.</summary>
    private static string MenuTitle(UiMenuId menu)
    {
        var shell = Ui.Shell;
        return menu switch
        {
            UiMenuId.Workspace => shell.MenuWorkspace,
            UiMenuId.Edit => shell.MenuEdit,
            UiMenuId.View => shell.MenuView,
            UiMenuId.Go => shell.MenuGo,
            UiMenuId.Connections => shell.MenuConnections,
            UiMenuId.Transfer => shell.MenuTransfer,
            UiMenuId.Sync => shell.MenuSync,
            UiMenuId.Tools => shell.MenuTools,
            UiMenuId.Help => shell.MenuHelp,
            _ => menu.ToString()
        };
    }

    private static void ClearDynamicSection(ToolStripMenuItem root, string section)
    {
        for (var index = root.DropDownItems.Count - 1; index >= 0; index--)
        {
            if (root.DropDownItems[index].Tag is not DynamicMenuItem tag || tag.Section != section) continue;
            var stale = root.DropDownItems[index];
            root.DropDownItems.RemoveAt(index);
            stale.Dispose();
        }
    }

    private void RebuildWorkspaceSection(ToolStripMenuItem root)
    {
        const string Section = "workspace";
        ClearDynamicSection(root, Section);

        // Ahead of Exit, which is the last static entry in this menu.
        var insertAt = root.DropDownItems.Cast<ToolStripItem>().ToList().FindIndex(item => item.Text == "Exit");
        if (insertAt < 0) insertAt = root.DropDownItems.Count;

        var workspace = GetActiveWorkspace();
        var pinned = workspace?.FilePath is { } path &&
            WorkspaceShortcutSettings.Contains(LoadPreferences().PinnedWorkspaces, path);
        var toggle = new ToolStripMenuItem(pinned ? Ui.Shell.UnpinWorkspace : Ui.Shell.PinWorkspace)
        {
            Tag = new DynamicMenuItem(Section, null),
            Enabled = workspace is not null,
            ToolTipText = pinned
                ? Ui.Shell.RemoveFromPinned
                : Ui.Shell.PinWorkspaceHint,
            AccessibleName = pinned ? Ui.Shell.UnpinWorkspaceAccessibleName : "Pin workspace"
        };
        toggle.Click += (_, _) => ToggleWorkspacePin(path: null, name: null);

        var items = new List<ToolStripItem>
        {
            new ToolStripSeparator { Tag = new DynamicMenuItem(Section, null) },
            toggle
        };

        var shortcuts = ComposeWorkspaceShortcuts(LoadPreferences());
        AppendWorkspaceGroup(items, Section, "Pinned", shortcuts.Where(view => view.IsPinned));
        AppendWorkspaceGroup(items, Section, "Recent", shortcuts.Where(view => !view.IsPinned));
        for (var index = 0; index < items.Count; index++)
        {
            root.DropDownItems.Insert(insertAt + index, items[index]);
        }
    }

    private void AppendWorkspaceGroup(
        List<ToolStripItem> items,
        string section,
        string heading,
        IEnumerable<WorkspaceShortcutView> shortcuts)
    {
        var group = shortcuts.ToArray();
        if (group.Length == 0) return;

        items.Add(new ToolStripSeparator { Tag = new DynamicMenuItem(section, null) });
        items.Add(new ToolStripMenuItem(heading)
        {
            Tag = new DynamicMenuItem(section, null),
            Enabled = false,
            Font = new Font(_menu.Font, FontStyle.Bold)
        });
        foreach (var view in group)
        {
            var entry = view.Entry;
            var label = entry.DisplayName.Replace("&", "&&", StringComparison.Ordinal) +
                (view.LooksPresent ? string.Empty : " (missing)");
            var item = new ToolStripMenuItem(label)
            {
                Tag = new DynamicMenuItem(section, entry),
                ToolTipText = entry.Path,
                AccessibleName = entry.DisplayName,
                AccessibleDescription = view.LooksPresent
                    ? entry.Path
                    : $"{entry.Path}. This file is missing.",
                // Dimmed rather than disabled: a disabled item cannot be clicked, and clicking is
                // how a missing entry offers to remove itself.
                ForeColor = view.LooksPresent ? StorageHubTheme.Text : StorageHubTheme.TextMuted
            };
            item.Click += async (_, _) => _ = await OpenWorkspacePathAsync(entry.Path).ConfigureAwait(true);
            items.Add(item);
        }
    }

    private void RebuildFavoritesSection(ToolStripMenuItem root)
    {
        const string Section = "favorites";
        ClearDynamicSection(root, Section);

        var favorites = OverviewDashboardControl.SelectFavoriteConnections(_overview.SavedConnections);
        root.DropDownItems.Add(new ToolStripSeparator { Tag = new DynamicMenuItem(Section, null) });
        root.DropDownItems.Add(new ToolStripMenuItem(Ui.Shell.Favorites)
        {
            Tag = new DynamicMenuItem(Section, null),
            Enabled = false,
            Font = new Font(_menu.Font, FontStyle.Bold)
        });

        if (favorites.Count == 0)
        {
            root.DropDownItems.Add(new ToolStripMenuItem(Ui.Shell.NoFavoriteConnections)
            {
                Tag = new DynamicMenuItem(Section, null),
                Enabled = false,
                ToolTipText = Ui.Shell.FavoritesEmptyHint
            });

            // The overview is built after the menu, and its cache fills on the first refresh, so a
            // cold menu kicks one off rather than claiming there are no favourites.
            _ = _overview.RefreshAsync(_lifetime.Token);
            return;
        }

        foreach (var connection in favorites)
        {
            var item = new ToolStripMenuItem(
                connection.DisplayName.Replace("&", "&&", StringComparison.Ordinal))
            {
                Tag = new DynamicMenuItem(Section, connection),
                ToolTipText = connection.FolderPath ?? connection.Provider.ToString(),
                AccessibleName = connection.DisplayName,
                AccessibleDescription = Ui.Format(Ui.Shell.OpenConnectionInPaneFormat, connection.DisplayName)
            };
            item.Click += async (_, _) => await OpenFavoriteConnectionAsync(connection).ConfigureAwait(true);
            root.DropDownItems.Add(item);
        }
    }

    /// <summary>
    /// Navigates the active pane to a favourite. Go is the pane-navigation menu, so a favourite
    /// replaces what the current pane is showing rather than opening a tab of its own.
    /// </summary>
    private Task OpenFavoriteConnectionAsync(ConnectionSummary connection) =>
        OpenConnectionInWorkspaceAsync(connection, inNewPane: false);

    /// <summary>
    /// Navigates a pane to a saved connection. Welcome and Sync tasks are not workspaces, so
    /// opening from one of those creates a workspace to open into.
    /// </summary>
    private async Task OpenConnectionInWorkspaceAsync(ConnectionSummary connection, bool inNewPane)
    {
        if (GetActiveWorkspace() is null)
        {
            AddWorkspace(2);
        }

        // SplitPane makes the new pane active, so the connection lands in the new one.
        if (inNewPane && GetActiveWorkspace() is { } workspace)
        {
            _ = workspace.SplitPane(workspace.ActivePaneId, WorkspaceDockEdge.Right);
        }

        var pane = GetActivePane();
        if (pane is null) return;
        await pane.RestoreStateAsync(
            StateFor(connection),
            reconnectRemote: true,
            _lifetime.Token).ConfigureAwait(true);
    }

    /// <summary>
    /// The one place that decides what kind of pane a saved connection opens into, so the panel
    /// and the favourites menu cannot drift apart on it.
    /// </summary>
    internal static BrowserPaneState StateFor(ConnectionSummary connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return new BrowserPaneState(
            connection.Type == ConnectionProfileType.Client
                ? PaneContentKind.SshClient
                : PaneContentKind.SavedStorage,
            connection.ConnectionId,
            connection.DisplayName);
    }

    private ToolStrip BuildToolbar()
    {
        var toolbar = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            ImageScalingSize = new Size(20, 20),
            AccessibleName = Ui.Shell.MainToolbar,
            AccessibleDescription = Ui.Shell.WorkspaceCommands,
            BackColor = StorageHubTheme.Surface,
            ForeColor = StorageHubTheme.Text,
            Padding = new Padding(
                LogicalToDeviceUnits(10),
                LogicalToDeviceUnits(7),
                LogicalToDeviceUnits(10),
                LogicalToDeviceUnits(7)),
            AutoSize = true
        };
        PopulateToolbar(toolbar);
        return toolbar;
    }

    /// <summary>
    /// Fills the toolbar from the saved layout.
    ///
    /// Every button still forwards to the menu entry that owns the command, so the toolbar
    /// inherits that entry's enabled state rather than maintaining a second, drifting copy. The
    /// icon comes from the command catalog rather than the call site, which is what stops a
    /// toolbar button and its menu entry from showing different pictures of the same command.
    /// </summary>
    private void PopulateToolbar(ToolStrip toolbar)
    {
        toolbar.Items.Clear();
        var style = _toolbarLabels switch
        {
            ToolbarLabelStyle.IconsAndText => ToolStripItemDisplayStyle.ImageAndText,
            ToolbarLabelStyle.TextUnderIcon => ToolStripItemDisplayStyle.ImageAndText,
            _ => ToolStripItemDisplayStyle.Image
        };
        toolbar.TextDirection = ToolStripTextDirection.Horizontal;
        foreach (var entry in ToolbarLayout.Resolve(_toolbarItems))
        {
            if (string.Equals(entry, ToolbarLayout.Separator, StringComparison.Ordinal))
            {
                toolbar.Items.Add(new ToolStripSeparator());
                continue;
            }

            var spec = UiCommandCatalog.Specs.FirstOrDefault(item => item.Id == entry);
            // A command the shell does not implement has no menu entry to forward to, so it is
            // left out rather than drawn as a button that does nothing.
            if (spec is null || FindCommandMenuItem(entry) is null)
            {
                continue;
            }

            if (CreateCommandButton(spec.Glyph ?? UiGlyph.More, entry, spec.Tone) is ToolStripButton button)
            {
                button.DisplayStyle = style;
                if (_toolbarLabels == ToolbarLabelStyle.TextUnderIcon)
                {
                    button.TextImageRelation = TextImageRelation.ImageAboveText;
                }

                toolbar.Items.Add(button);
            }
        }

        TrimSeparators(toolbar);
    }

    /// <summary>
    /// Removes separators that ended up leading, trailing or doubled once unavailable commands
    /// were left out, so a missing command cannot show as a gap between two dividers.
    /// </summary>
    private static void TrimSeparators(ToolStrip toolbar)
    {
        for (var index = toolbar.Items.Count - 1; index >= 0; index--)
        {
            if (toolbar.Items[index] is not ToolStripSeparator)
            {
                continue;
            }

            var leading = index == 0;
            var trailing = index == toolbar.Items.Count - 1;
            var doubled = index > 0 && toolbar.Items[index - 1] is ToolStripSeparator;
            if (leading || trailing || doubled)
            {
                toolbar.Items.RemoveAt(index);
            }
        }
    }

    /// <summary>The live menu entry that owns a command, or null when the shell has none.</summary>
    private ToolStripMenuItem? FindCommandMenuItem(string commandId) => _menu.Items
        .OfType<ToolStripMenuItem>()
        .SelectMany(root => root.DropDownItems.OfType<ToolStripMenuItem>())
        .FirstOrDefault(item => Equals(item.Tag, commandId));

    /// <summary>
    /// Rebuilds the toolbar after its layout or label style changed in Settings, so the change is
    /// visible without restarting.
    /// </summary>
    internal void ApplyToolbarPreferences(IReadOnlyList<string>? items, ToolbarLabelStyle labels)
    {
        _toolbarItems = items;
        _toolbarLabels = labels;
        if (_toolbar is { } toolbar)
        {
            PopulateToolbar(toolbar);
        }
    }

    /// <summary>
    /// A toolbar button backed by a menu command. It mirrors the menu item's label, tooltip, and
    /// enabled state, and clicking it runs exactly the same handler.
    /// </summary>
    private ToolStripItem CreateCommandButton(UiGlyph glyph, string commandId, UiIconTone tone = UiIconTone.Text)
    {
        var source = _menu.Items.OfType<ToolStripMenuItem>()
            .SelectMany(root => root.DropDownItems.OfType<ToolStripMenuItem>())
            .FirstOrDefault(item => Equals(item.Tag, commandId));
        if (source is null)
        {
            return new ToolStripSeparator { Visible = false };
        }

        var button = new ToolStripButton
        {
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            // Text is always set even when the style hides it, so switching to labels needs only
            // the display style changed rather than every button rebuilt from the menu again.
            Text = StripMnemonics(source.Text),
            ToolTipText = source.ToolTipText,
            AccessibleName = source.Text,
            AccessibleDescription = source.ToolTipText,
            AutoToolTip = true,
            Enabled = source.Enabled,
            // A toolbar button's own margin is what separates one glyph from the next; the stock
            // one leaves them all but touching.
            Margin = new Padding(LogicalToDeviceUnits(3), LogicalToDeviceUnits(1), LogicalToDeviceUnits(3), LogicalToDeviceUnits(1)),
            Padding = new Padding(LogicalToDeviceUnits(3), LogicalToDeviceUnits(2), LogicalToDeviceUnits(3), LogicalToDeviceUnits(2)),
            // Lets the toolbar be searched by command, which is how the connections-panel toggle
            // finds the button whose checked state it mirrors.
            Tag = commandId
        };
        _ = StorageHubTheme.TrackIcon(button, glyph, 20, tone, DeviceDpi / 96F);
        button.Click += (_, _) => source.PerformClick();
        source.EnabledChanged += (_, _) => button.Enabled = source.Enabled;
        source.TextChanged += (_, _) =>
        {
            button.AccessibleName = source.Text;
            button.Text = StripMnemonics(source.Text);
        };
        return button;
    }

    private StatusStrip BuildStatusStrip()
    {
        var status = new StatusStrip
        {
            AccessibleName = Ui.Shell.ApplicationStatus,
            BackColor = StorageHubTheme.Surface,
            ForeColor = StorageHubTheme.TextMuted,
            SizingGrip = true
        };
        _locationStatus.AccessibleName = Ui.Shell.CurrentLocation;
        _selectionStatus.AccessibleName = Ui.Shell.SelectionSummary;
        _speedStatus.AccessibleName = Ui.Shell.TransferSpeed;
        _queueStatus.AccessibleName = Ui.Shell.QueueSummary;
        status.Items.Add(_locationStatus);
        status.Items.Add(_selectionStatus);
        status.Items.Add(new ToolStripSeparator());
        status.Items.Add(_speedStatus);
        status.Items.Add(new ToolStripSeparator());
        status.Items.Add(_queueStatus);
        status.Items.Add(new ToolStripSeparator());
        // The agent indicator is where a user looks when something has stopped working, so it is
        // also the fastest way into the controls that fix it.
        _agentStatus.IsLink = false;
        _agentStatus.Click += (_, _) => ShowAgentControl();
        _agentStatus.ToolTipText = Ui.Shell.AgentControlsTooltip;
        status.Items.Add(_agentStatus);
        status.Items.Add(new ToolStripSeparator());
        status.Items.Add(_updateStatus);
        return status;
    }

    private TabPage CreateWorkspace(string title)
    {
        var page = new TabPage(CreateTabLabel(title))
        {
            AccessibleName = $"{title} workspace",
            ToolTipText = title,
            Tag = CreateTabMetadata(UiGlyph.Folder, closable: true)
        };
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            Size = new Size(1300, 600),
            SplitterDistance = 700,
            Panel1MinSize = 360,
            Panel2MinSize = 360,
            BackColor = StorageHubTheme.Border,
            AccessibleName = Ui.Shell.SourceAndDestinationPanes
        };
        split.Panel1.Padding = new Padding(0, 0, 3, 0);
        split.Panel2.Padding = new Padding(3, 0, 0, 0);
        var source = new BrowserPaneControl("Source", showLocalDefault: true);
        var destination = new BrowserPaneControl("Destination", showLocalDefault: false);
        source.Enter += ActivePaneEntered;
        destination.Enter += ActivePaneEntered;
        source.TransferRequested += (_, args) =>
            _ = EnqueueManualTransferAsync(source, destination, args.Operation);
        destination.TransferRequested += (_, args) =>
            _ = EnqueueManualTransferAsync(destination, source, args.Operation);
        source.TransferDropRequested += (_, args) => EnqueuePaneDrop(page, source, args);
        destination.TransferDropRequested += (_, args) => EnqueuePaneDrop(page, destination, args);
        source.ShellImportDropRequested += (_, args) => _ = ReviewShellImportAsync(args);
        destination.ShellImportDropRequested += (_, args) => _ = ReviewShellImportAsync(args);
        if (_explorerDropBrokerAvailable)
        {
            source.BeginExplorerDropAsync = BeginExplorerDropAsync;
            destination.BeginExplorerDropAsync = BeginExplorerDropAsync;
            source.CommitExplorerDropAsync = CommitExplorerDropAsync;
            destination.CommitExplorerDropAsync = CommitExplorerDropAsync;
            source.PendingDrops = _pendingDrops;
            destination.PendingDrops = _pendingDrops;
        }
        else
        {
            var unavailableReason = Ui.Shell.ExplorerIntegrationUnavailable;
            source.ExplorerDropUnavailableReason = unavailableReason;
            destination.ExplorerDropUnavailableReason = unavailableReason;
        }
        source.SelectionStaged += (_, args) => StagePaneSelection(source, args);
        destination.SelectionStaged += (_, args) => StagePaneSelection(destination, args);
        source.CanPaste = () => _paneClipboard is not null;
        destination.CanPaste = () => _paneClipboard is not null;
        source.PasteRequested += (_, _) => PasteIntoPane(source);
        destination.PasteRequested += (_, _) => PasteIntoPane(destination);
        source.DeleteRequested += (_, _) => _ = ReviewDeleteAsync(source);
        destination.DeleteRequested += (_, _) => _ = ReviewDeleteAsync(destination);
        source.NewFolderRequested += (_, _) => _ = CreatePaneItemAsync(source, createFolder: true);
        destination.NewFolderRequested += (_, _) => _ = CreatePaneItemAsync(destination, createFolder: true);
        source.NewFileRequested += (_, _) => _ = CreatePaneItemAsync(source, createFolder: false);
        destination.NewFileRequested += (_, _) => _ = CreatePaneItemAsync(destination, createFolder: false);
        source.RenameRequested += (_, _) => _ = RenamePaneSelectionAsync(source);
        destination.RenameRequested += (_, _) => _ = RenamePaneSelectionAsync(destination);
        source.BatchRenameRequested += (_, _) => _ = BatchRenamePaneSelectionAsync(source);
        destination.BatchRenameRequested += (_, _) => _ = BatchRenamePaneSelectionAsync(destination);
        source.EditRequested += (_, _) => EditSelectedFile(source);
        destination.EditRequested += (_, _) => EditSelectedFile(destination);
        source.ObjectInspectionRequested += (_, _) => ShowObjectInspector(source);
        destination.ObjectInspectionRequested += (_, _) => ShowObjectInspector(destination);
        source.ConnectionOpened += (_, args) => _overview.RecordRecentConnection(args.Connection);
        destination.ConnectionOpened += (_, args) => _overview.RecordRecentConnection(args.Connection);
        split.Panel1.Controls.Add(source);
        split.Panel2.Controls.Add(destination);

        var layoutToolbar = new ToolStrip
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 40,
            GripStyle = ToolStripGripStyle.Hidden,
            BackColor = StorageHubTheme.SurfaceMuted,
            ForeColor = StorageHubTheme.Text,
            ImageScalingSize = new Size(20, 20),
            Padding = new Padding(6, 5, 6, 5),
            AccessibleName = $"{title} workspace layout"
        };
        var layoutMenu = new ToolStripDropDownButton(Ui.Shell.LayoutSideBySide)
        {
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            AccessibleName = Ui.Shell.WorkspacePaneLayout,
            ToolTipText = Ui.Shell.ArrangePanesTooltip
        };
        _ = StorageHubTheme.TrackIcon(layoutMenu, UiGlyph.Compare, 18, UiIconTone.Text, DeviceDpi / 96F);
        var sideBySide = new ToolStripMenuItem("Side by side") { Checked = true };
        _ = StorageHubTheme.TrackIcon(sideBySide, UiGlyph.Compare, 16, UiIconTone.Text, DeviceDpi / 96F);
        var topAndBottom = new ToolStripMenuItem("Top and bottom");
        _ = StorageHubTheme.TrackIcon(topAndBottom, UiGlyph.Layers, 16, UiIconTone.Text, DeviceDpi / 96F);
        sideBySide.Click += (_, _) => SetWorkspaceOrientation(
            split,
            layoutMenu,
            sideBySide,
            topAndBottom,
            stacked: false);
        topAndBottom.Click += (_, _) => SetWorkspaceOrientation(
            split,
            layoutMenu,
            sideBySide,
            topAndBottom,
            stacked: true);
        layoutMenu.DropDownItems.Add(sideBySide);
        layoutMenu.DropDownItems.Add(topAndBottom);
        layoutToolbar.Items.Add(layoutMenu);
        layoutToolbar.Items.Add(new ToolStripSeparator());
        var clipboardLabel = new ToolStripLabel(Ui.Shell.ClipboardLabel)
        {
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            ForeColor = StorageHubTheme.TextMuted,
            ToolTipText = Ui.Shell.ClipboardTooltip
        };
        _ = StorageHubTheme.TrackIcon(clipboardLabel, UiGlyph.Copy, 18, UiIconTone.Muted, DeviceDpi / 96F);
        layoutToolbar.Items.Add(clipboardLabel);
        var clipboardStatus = new ToolStripLabel(Ui.Shell.ClipboardEmpty)
        {
            Name = "WorkspaceClipboardStatus",
            ForeColor = StorageHubTheme.Text,
            ToolTipText = Ui.Shell.ClipboardStageTooltip
        };
        var pasteClipboard = new ToolStripButton(Ui.Shell.PasteToActivePane)
        {
            Name = "WorkspaceClipboardPaste",
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            Enabled = false,
            ToolTipText = Ui.Shell.ClipboardPasteTooltip
        };
        _ = StorageHubTheme.TrackIcon(pasteClipboard, UiGlyph.Paste, 18, UiIconTone.Text, DeviceDpi / 96F);
        pasteClipboard.Click += (_, _) =>
        {
            var target = destination.ContainsFocus ? destination : source.ContainsFocus ? source : destination;
            PasteIntoPane(target);
        };
        var clearClipboard = new ToolStripButton(Ui.Shell.ClipboardClear)
        {
            Name = "WorkspaceClipboardClear",
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            Enabled = false,
            ToolTipText = Ui.Shell.ClipboardClearTooltip
        };
        _ = StorageHubTheme.TrackIcon(clearClipboard, UiGlyph.Close, 18, UiIconTone.Text, DeviceDpi / 96F);
        clearClipboard.Click += (_, _) => ClearPaneClipboard();
        layoutToolbar.Items.Add(clipboardStatus);
        layoutToolbar.Items.Add(pasteClipboard);
        layoutToolbar.Items.Add(clearClipboard);

        page.Controls.Add(split);
        page.Controls.Add(layoutToolbar);
        if (_updatePreferencesStore.Load().DefaultWorkspaceLayout == WorkspaceLayout.TopAndBottom)
        {
            SetWorkspaceOrientation(
                split,
                layoutMenu,
                sideBySide,
                topAndBottom,
                stacked: true);
        }
        RefreshPaneClipboardPresentation();
        return page;
    }

    private Task<ExplorerDropBeginResponse> BeginExplorerDropAsync(
        PaneSelectionSnapshot selection,
        string dropToken,
        CancellationToken cancellationToken)
    {
        if (selection.Context.ConnectionId is not { } connectionId ||
            string.IsNullOrWhiteSpace(selection.Context.RootIdentity))
        {
            return Task.FromResult(new ExplorerDropBeginResponse(
                ShellTransferIpcContract.CurrentVersion,
                null,
                null,
                new StorageIpcFailure(
                    "shell-transfer.export.invalid",
                    StorageIpcFailureCategory.Validation,
                    Ui.Shell.ExplorerExportRequiresConnection,
                    false)));
        }

        var sources = selection.Items.Select(item => new ShellExportSource(
            new TransferQueueAddress(
                connectionId,
                selection.Context.RootIdentity,
                item.RelativePath,
                item.NativeItemId,
                item.VersionId,
                item.EntityTag),
            item.IsContainer,
            item.Name)).ToArray();
        return _shellTransfers.BeginExplorerDropAsync(new ExplorerDropBeginRequest(
            ShellTransferIpcContract.CurrentVersion,
            sources,
            dropToken), cancellationToken);
    }

    private Task<ExplorerDropCommitResponse> CommitExplorerDropAsync(
        string token,
        CancellationToken cancellationToken) =>
        _shellTransfers.CommitExplorerDropAsync(new ExplorerDropCommitRequest(
            ShellTransferIpcContract.CurrentVersion,
            token), cancellationToken);

    private static void SetWorkspaceOrientation(
        SplitContainer split,
        ToolStripDropDownButton layoutMenu,
        ToolStripMenuItem sideBySide,
        ToolStripMenuItem topAndBottom,
        bool stacked)
    {
        split.Panel1MinSize = 0;
        split.Panel2MinSize = 0;
        split.Orientation = stacked ? Orientation.Horizontal : Orientation.Vertical;
        var available = stacked ? split.ClientSize.Height : split.ClientSize.Width;
        if (available > split.SplitterWidth)
        {
            var half = (available - split.SplitterWidth) / 2;
            split.SplitterDistance = half;
            var desiredMinimum = stacked ? 150 : 300;
            var effectiveMinimum = Math.Min(desiredMinimum, half);
            split.Panel1MinSize = effectiveMinimum;
            split.Panel2MinSize = effectiveMinimum;
        }

        sideBySide.Checked = !stacked;
        topAndBottom.Checked = stacked;
        layoutMenu.Text = stacked ? Ui.Shell.LayoutTopAndBottom : Ui.Shell.LayoutSideBySide;
        split.AccessibleDescription = stacked
            ? "SSH or storage panes arranged from top to bottom."
            : "SSH or storage panes arranged from left to right.";
    }

    private TabPage CreateFixedTab(string title, UiGlyph glyph, Control content)
    {
        var page = new TabPage(CreateTabLabel(title))
        {
            AccessibleName = title,
            ToolTipText = title,
            Tag = CreateTabMetadata(glyph, closable: false)
        };
        page.Controls.Add(content);
        return page;
    }

    private WorkspaceTabMetadata CreateTabMetadata(UiGlyph glyph, bool closable)
    {
        var metadata = new WorkspaceTabMetadata(closable);
        // The strip is hand-drawn, so the icon is not owned by a control property. Tracking it
        // against the tab control keeps it legible after an appearance change.
        _ = StorageHubTheme.TrackIcon(
            _workspaceTabs,
            image => metadata.Icon = image,
            glyph,
            16,
            UiIconTone.Muted,
            DeviceDpi / 96F);
        return metadata;
    }

    private static string CreateTabLabel(string title) => title;

    /// <summary>
    /// Menu labels carry an ampersand for their keyboard mnemonic, which a toolbar button would
    /// render as an underline on a bar that has no mnemonics.
    /// </summary>
    private static string StripMnemonics(string? label) =>
        label?.Replace("&", string.Empty, StringComparison.Ordinal) ?? string.Empty;

    private ToolStripButton CreateToolbarButton(
        UiGlyph glyph,
        string toolTip,
        EventHandler click)
    {
        var button = new ToolStripButton
        {
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            ToolTipText = toolTip,
            AccessibleName = toolTip,
            AccessibleDescription = toolTip,
            AutoToolTip = true
        };
        _ = StorageHubTheme.TrackIcon(button, glyph, 20, UiIconTone.Text, DeviceDpi / 96F);
        button.Click += click;

        return button;
    }

    private void WireCommand(ToolStripMenuItem item, string commandId)
    {
        item.Click += async (_, _) =>
        {
            switch (commandId)
            {
                case UiCommandIds.WorkspaceNewWorkspace:
                    ChooseAndAddWorkspace();
                    break;
                case UiCommandIds.WorkspaceOpenWorkspace:
                    await OpenWorkspaceAsync();
                    break;
                case UiCommandIds.WorkspaceSaveWorkspace:
                    SaveActiveWorkspace(saveAs: false);
                    break;
                case UiCommandIds.WorkspaceSaveWorkspaceAs:
                    SaveActiveWorkspace(saveAs: true);
                    break;
                case UiCommandIds.WorkspaceRenameWorkspace:
                    RenameActiveWorkspace();
                    break;
                case UiCommandIds.WorkspaceCloseWorkspace:
                    CloseActiveWorkspace();
                    break;
                case UiCommandIds.ConnectionsNewConnection:
                    ShowConnectionManager();
                    break;
                case UiCommandIds.SyncSyncProfiles:
                case UiCommandIds.SyncReviewRun:
                    ShowSyncProfileEditor();
                    break;
                case UiCommandIds.EditCopy:
                    StageFocusedPane(TransferQueueOperation.Copy);
                    break;
                case UiCommandIds.EditCut:
                    StageFocusedPane(TransferQueueOperation.Move);
                    break;
                case UiCommandIds.EditPaste:
                    if (GetActivePane() is { } pasteDestination)
                    {
                        PasteIntoPane(pasteDestination);
                    }
                    break;
                case UiCommandIds.EditNewFolder:
                    if (GetActivePane() is { } folderPane) await CreatePaneItemAsync(folderPane, createFolder: true);
                    break;
                case UiCommandIds.EditNewEmptyFile:
                    if (GetActivePane() is { } filePane) await CreatePaneItemAsync(filePane, createFolder: false);
                    break;
                case UiCommandIds.EditRename:
                    if (GetActivePane() is { } renamePane) await RenamePaneSelectionAsync(renamePane);
                    break;
                case UiCommandIds.EditBatchRename:
                    if (GetActivePane() is { } batchPane) await BatchRenamePaneSelectionAsync(batchPane);
                    break;
                case UiCommandIds.EditDelete:
                    if (GetActivePane() is { } deletePane) await ReviewDeleteAsync(deletePane);
                    break;
                case UiCommandIds.EditInvertSelection:
                    GetActivePane()?.InvertVisibleSelection();
                    break;
                case UiCommandIds.EditProperties:
                    ShowObjectInspectorFromFocusedPane();
                    break;
                case UiCommandIds.EditSelectAll:
                    GetActivePane()?.SelectAllVisibleItems();
                    break;
                case UiCommandIds.ViewRefresh:
                    NavigateActivePane(PaneNavigation.Refresh);
                    break;
                case UiCommandIds.ViewConnectionsPanel:
                    ToggleConnectionsPanel();
                    break;
                case UiCommandIds.ViewMoveConnectionsPanel:
                    ToggleConnectionsPanelSide();
                    break;
                case UiCommandIds.GoBack:
                    NavigateActivePane(PaneNavigation.Back);
                    break;
                case UiCommandIds.GoForward:
                    NavigateActivePane(PaneNavigation.Forward);
                    break;
                case UiCommandIds.GoUp:
                    NavigateActivePane(PaneNavigation.Up);
                    break;
                case UiCommandIds.GoFocusAddress:
                    GetActivePane()?.FocusAddress();
                    break;
                case UiCommandIds.GoNextPane:
                    GetActiveWorkspace()?.FocusNextPane();
                    break;
                case UiCommandIds.SyncSchedules:
                    ShowSchedules();
                    break;
                case UiCommandIds.ConnectionsKeyStore:
                    ShowKeyStore();
                    break;
                case UiCommandIds.ToolsBackgroundAgent:
                    ShowAgentControl();
                    break;
                case UiCommandIds.ToolsSettings:
                    var preferencesBefore = _updatePreferencesStore.Load();
                    var languageRestartRequested = false;
                    using (var dialog = new SettingsForm(
                               _updatePreferencesStore,
                               preferences => { _updater.SavePreferences(preferences); RefreshShortcutPresentation(); }))
                    {
                        _ = dialog.ShowDialog(this);
                        languageRestartRequested = dialog.LanguageRestartRequested;
                    }

                    if (languageRestartRequested)
                    {
                        // Closing the shell runs the usual save prompts, so a restart for the sake
                        // of a menu's language still cannot discard an unsaved workspace. The
                        // restart is armed in OnFormClosing rather than here, so a close the user
                        // then cancels does not leave one waiting for the next ordinary exit.
                        _restartAfterClose = true;
                        Close();
                        break;
                    }

                    var preferencesAfter = _updatePreferencesStore.Load();
                    if (!ConcurrencyEquals(preferencesBefore, preferencesAfter))
                    {
                        await ApplyConcurrencySettingsAsync();
                    }

                    ApplyToolbarPreferences(preferencesAfter.ToolbarItems, preferencesAfter.ToolbarLabels);

                    // Settings can open the connection editor from its provider list.
                    RefreshConnectionSurfaces();
                    break;
                case UiCommandIds.ToolsExportSettings:
                    ShowSettingsExport();
                    break;
                case UiCommandIds.ToolsImportSettings:
                    await ShowSettingsImportAsync();

                    // Connections are one of the importable sections.
                    RefreshConnectionSurfaces();
                    break;
                case UiCommandIds.HelpCheckForUpdates:
                    ShowUpdateChecker();
                    break;
                case UiCommandIds.HelpAboutStorageHub:
                    _ = MessageBox.Show(
                        this,
                        Ui.Format(Ui.Dialogs.AboutBodyFormat, DesktopApplicationVersion.Current),
                        Ui.Dialogs.AboutCaption,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    break;
                case UiCommandIds.WorkspaceExit:
                    Close();
                    break;
            }
        };
    }

    private void WorkspaceTabsSelecting(object? sender, TabControlCancelEventArgs e)
    {
        if (!_changingWorkspaceTabs && e.TabPage == _workspaceTabs.TabPages[^1])
        {
            e.Cancel = true;
            if (_workspaceAddPending)
            {
                return;
            }

            _workspaceAddPending = true;
            _workspaceTabs.BeginInvoke(() =>
            {
                _workspaceAddPending = false;
                if (!_workspaceTabs.IsDisposed)
                {
                    ChooseAndAddWorkspace();
                }
            });
        }
    }

    private void WorkspaceTabsDrawItem(object? sender, DrawItemEventArgs e)
    {
        var page = _workspaceTabs.TabPages[e.Index];
        var bounds = _workspaceTabs.GetTabRect(e.Index);
        var selected = e.Index == _workspaceTabs.SelectedIndex;
        var hovered = e.State.HasFlag(DrawItemState.HotLight);
        var isAddTab = page == _workspaceTabs.TabPages[^1];

        var previousMode = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var fill = selected
            ? StorageHubTheme.Surface
            : hovered ? StorageHubTheme.Elevated : StorageHubTheme.SurfaceMuted;
        // The strip and the page below it are different colours, so the tab is drawn taller than
        // its own rectangle and the overflow is clipped away by the page: the selected tab then
        // reads as one continuous surface with its content.
        // Inset by a pixel each side so neighbouring tabs do not touch, and outlined against the
        // strip rather than in the tab's own fill colour: an outline matching the fill left no
        // visible edge at all, so a row of tabs ran together into one band with words in it.
        using (var shape = UiShapes.RoundedRectangle(
                   new RectangleF(bounds.Left + 1, bounds.Top, bounds.Width - 3, bounds.Height + 6),
                   6F))
        using (var brush = new SolidBrush(fill))
        using (var outline = new Pen(selected ? StorageHubTheme.Border : StorageHubTheme.Canvas))
        {
            e.Graphics.FillPath(brush, shape);
            e.Graphics.DrawPath(outline, shape);
        }

        if (selected)
        {
            using var accent = new SolidBrush(StorageHubTheme.Primary);
            e.Graphics.FillRectangle(accent, bounds.Left + 4, bounds.Top + 1, bounds.Width - 9, 2);
        }

        e.Graphics.SmoothingMode = previousMode;
        var foreground = selected ? StorageHubTheme.Text : StorageHubTheme.TextMuted;

        if (isAddTab)
        {
            using var plus = new Pen(hovered ? StorageHubTheme.Primary : StorageHubTheme.TextMuted, Math.Max(1.4F, DeviceDpi / 96F * 1.5F));
            var center = new Point(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2));
            e.Graphics.DrawLine(plus, center.X - 5, center.Y, center.X + 5, center.Y);
            e.Graphics.DrawLine(plus, center.X, center.Y - 5, center.X, center.Y + 5);
            return;
        }

        var metadata = page.Tag as WorkspaceTabMetadata;
        var iconSize = ScaleForDpi(16);
        var iconBounds = new Rectangle(
            bounds.Left + ScaleForDpi(9),
            bounds.Top + Math.Max(0, (bounds.Height - iconSize) / 2),
            iconSize,
            iconSize);
        if (metadata?.Icon is { } icon)
        {
            e.Graphics.DrawImage(icon, iconBounds);
        }

        var closeBounds = GetWorkspaceCloseBounds(bounds);
        var textRight = metadata?.Closable == true
            ? closeBounds.Left - ScaleForDpi(5)
            : bounds.Right - ScaleForDpi(7);
        TextRenderer.DrawText(e.Graphics, page.Text, Font,
            Rectangle.FromLTRB(iconBounds.Right + ScaleForDpi(6), bounds.Top, textRight, bounds.Bottom),
            foreground,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (metadata?.Closable == true)
        {
            using var pen = new Pen(
                selected || hovered ? StorageHubTheme.Text : StorageHubTheme.TextMuted,
                Math.Max(1F, DeviceDpi / 96F * 1.4F));

            // The inset scales with the box, or the cross keeps its 96-DPI size inside a box that
            // grew around it and the glyph drifts out of proportion as the display scales.
            var inset = ScaleForDpi(4);
            e.Graphics.DrawLine(pen, closeBounds.Left + inset, closeBounds.Top + inset, closeBounds.Right - inset, closeBounds.Bottom - inset);
            e.Graphics.DrawLine(pen, closeBounds.Right - inset, closeBounds.Top + inset, closeBounds.Left + inset, closeBounds.Bottom - inset);
        }
    }

    private void WorkspaceTabsMouseDown(object? sender, MouseEventArgs e)
    {
        for (var index = 0; index < _workspaceTabs.TabPages.Count - 1; index++)
        {
            if (e.Button == MouseButtons.Left &&
                _workspaceTabs.TabPages[index].Tag is WorkspaceTabMetadata { Closable: true } &&
                GetWorkspaceCloseBounds(_workspaceTabs.GetTabRect(index)).Contains(e.Location))
            {
                CloseWorkspaceAt(index);
                return;
            }
        }
    }

    /// <summary>
    /// Where the close cross is drawn, and where a click on it counts.
    /// </summary>
    /// <remarks>
    /// Scaled, because everything around it is. The tab's height follows the font, the stroke
    /// follows <see cref="Control.DeviceDpi"/> and every icon is rendered through
    /// <c>TrackIcon</c> at the same scale — but this box was a flat 16 pixels, so at 125% it was
    /// drawn a fifth smaller than the chrome it sits in, and smaller still further up. A close
    /// affordance that shrinks as the display scales is one people stop finding.
    /// </remarks>
    private Rectangle GetWorkspaceCloseBounds(Rectangle tabBounds)
    {
        var closeSize = ScaleForDpi(16);
        return new Rectangle(
            tabBounds.Right - closeSize - ScaleForDpi(6),
            tabBounds.Top + Math.Max(0, (tabBounds.Height - closeSize) / 2),
            closeSize,
            closeSize);
    }

    private int ScaleForDpi(int value) => (int)Math.Round(value * (DeviceDpi / 96F));

    private void ChooseAndAddWorkspace()
    {
        var preferences = LoadPreferences();
        if (preferences.DefaultWorkspacePaneCount is { } remembered)
        {
            // The user asked not to be asked. Settings ▸ Workspace is the way back.
            AddWorkspace(remembered);
            return;
        }

        using var chooser = new NewWorkspaceForm(preferences.DefaultWorkspaceLayout);
        if (chooser.ShowDialog(this) != DialogResult.OK) return;

        if (chooser.RememberChoice)
        {
            var (panes, chosenLayout) = (chooser.PaneCount, chooser.PaneLayout);
            MutatePreferences(current => current with
            {
                DefaultWorkspacePaneCount = panes,
                DefaultWorkspaceLayout = chosenLayout
            });
        }

        AddWorkspace(chooser.PaneCount, chooser.PaneLayout);
    }

    internal TabPage AddWorkspace(int paneCount = 2, WorkspaceLayout? layout = null)
    {
        var insertAt = _workspaceTabs.TabPages.Count - 1;
        var workspaceNumber = _nextWorkspaceNumber++;
        while (_workspaceTabs.TabPages.Cast<TabPage>()
            .SelectMany(page => page.Controls.OfType<WorkspaceControl>())
            .Any(workspace => string.Equals(
                     workspace.WorkspaceName,
                     Ui.Format(Ui.Shell.WorkspaceNumberFormat, workspaceNumber),
                     StringComparison.OrdinalIgnoreCase)))
        {
            workspaceNumber = _nextWorkspaceNumber++;
        }
        var name = Ui.Format(Ui.Shell.WorkspaceNumberFormat, workspaceNumber);
        var page = CreateCustomWorkspace(
            name,
            WorkspaceLayoutModel.CreatePreset(paneCount, layout ?? LoadPreferences().DefaultWorkspaceLayout));
        _workspaceTabs.TabPages.Insert(insertAt, page);
        _workspaceTabs.SelectedTab = page;
        return page;
    }

    private TabPage CreateCustomWorkspace(
        string name,
        WorkspaceLayoutModel layout,
        IReadOnlyDictionary<Guid, BrowserPaneState>? states = null)
    {
        var page = new TabPage(name)
        {
            AccessibleName = $"{name} workspace",
            ToolTipText = name,
            Tag = CreateTabMetadata(UiGlyph.Folder, closable: true)
        };
        WorkspaceControl? workspace = null;
        workspace = new WorkspaceControl(name, layout, pane => ConfigureWorkspacePane(page, pane), states);
        workspace.WorkspaceChanged += (_, _) => UpdateWorkspaceTab(page, workspace);
        workspace.ActivePaneChanged += (_, _) => _activePane = workspace.ActivePane;
        var toolbar = workspace.Controls.OfType<ToolStrip>().Single();
        if (toolbar.Items["WorkspaceClipboardPaste"] is ToolStripButton paste)
            paste.Click += (_, _) => { if (workspace.ActivePane is { } pane) PasteIntoPane(pane); };
        if (toolbar.Items["WorkspaceClipboardClear"] is ToolStripButton clear)
            clear.Click += (_, _) => ClearPaneClipboard();
        page.Controls.Add(workspace);
        UpdateWorkspaceTab(page, workspace);
        RefreshPaneClipboardPresentation();
        return page;
    }

    private void ConfigureWorkspacePane(TabPage page, BrowserPaneControl pane)
    {
        pane.Enter += ActivePaneEntered;
        pane.TransferDropRequested += (_, args) => EnqueuePaneDrop(page, pane, args);
        pane.ShellImportDropRequested += (_, args) => _ = ReviewShellImportAsync(args);
        if (_explorerDropBrokerAvailable)
        {
            pane.BeginExplorerDropAsync = BeginExplorerDropAsync;
            pane.CommitExplorerDropAsync = CommitExplorerDropAsync;
            pane.PendingDrops = _pendingDrops;
        }
        else
        {
            pane.ExplorerDropUnavailableReason =
                Ui.Shell.ExplorerIntegrationUnavailable;
        }
        pane.SelectionStaged += (_, args) => StagePaneSelection(pane, args);
        pane.CanPaste = () => _paneClipboard is not null;
        pane.PasteRequested += (_, _) => PasteIntoPane(pane);
        pane.DeleteRequested += (_, _) => _ = ReviewDeleteAsync(pane);
        pane.NewFolderRequested += (_, _) => _ = CreatePaneItemAsync(pane, createFolder: true);
        pane.NewFileRequested += (_, _) => _ = CreatePaneItemAsync(pane, createFolder: false);
        pane.RenameRequested += (_, _) => _ = RenamePaneSelectionAsync(pane);
        pane.BatchRenameRequested += (_, _) => _ = BatchRenamePaneSelectionAsync(pane);
        pane.EditRequested += (_, _) => EditSelectedFile(pane);
        pane.ObjectInspectionRequested += (_, _) => ShowObjectInspector(pane);
        pane.ConnectionOpened += (_, args) => _overview.RecordRecentConnection(args.Connection);
    }

    private static void UpdateWorkspaceTab(TabPage page, WorkspaceControl workspace)
    {
        page.Text = workspace.WorkspaceName + (workspace.IsDirty ? " *" : string.Empty);
        page.ToolTipText = workspace.FilePath ?? workspace.WorkspaceName;
        page.AccessibleName = Ui.Format(Ui.Shell.WorkspaceAccessibleNameFormat, workspace.WorkspaceName);
    }

    private WorkspaceControl? GetActiveWorkspace() =>
        _workspaceTabs.SelectedTab?.Controls.OfType<WorkspaceControl>().SingleOrDefault();

    private async Task OpenWorkspaceAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Open Workspace",
            Filter = WorkspaceFileStore.Filter,
            DefaultExt = "shw",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _ = await OpenWorkspacePathAsync(dialog.FileName).ConfigureAwait(true);
    }

    /// <summary>
    /// Opens a workspace file, whatever route asked for it: the Open dialog, a pinned or recent
    /// entry, or the Welcome tab. Owning de-duplication, most-recently-used recording and the
    /// missing-file prompt in one place is what keeps those three routes consistent.
    /// </summary>
    /// <returns>Whether a workspace tab for the file is now open and selected.</returns>
    private async Task<bool> OpenWorkspacePathAsync(string path)
    {
        string normalized;
        try
        {
            normalized = Path.GetFullPath(path);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or
            PathTooLongException)
        {
            PromptToForgetWorkspace(path);
            return false;
        }

        var existing = _workspaceTabs.TabPages.Cast<TabPage>()
            .FirstOrDefault(page => page.Controls.OfType<WorkspaceControl>().SingleOrDefault() is { FilePath: { } open } &&
                string.Equals(Path.GetFullPath(open), normalized, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            // Re-activating an already-open tab is an open as far as the recent list is concerned.
            _workspaceTabs.SelectedTab = existing;
            RecordRecentWorkspace(
                normalized, existing.Controls.OfType<WorkspaceControl>().Single().WorkspaceName);
            return true;
        }

        // Re-checked here rather than trusted from the menu: the file can vanish between the
        // dropdown opening and the click, and entries on unreachable shares are never probed.
        if (!File.Exists(normalized))
        {
            PromptToForgetWorkspace(normalized);
            return false;
        }

        try
        {
            var document = WorkspaceFileStore.Load(normalized);
            var layout = new WorkspaceLayoutModel(WorkspaceFileStore.ToLayout(document.Layout));
            var page = CreateCustomWorkspace(document.Name, layout, document.Panes);
            _workspaceTabs.TabPages.Insert(_workspaceTabs.TabPages.Count - 1, page);
            _workspaceTabs.SelectedTab = page;
            var workspace = page.Controls.OfType<WorkspaceControl>().Single();
            await workspace.HydrateAsync(
                document.Panes,
                document.ActivePaneId,
                _updatePreferencesStore.Load().ReconnectRemotePanesAutomatically,
                _lifetime.Token).ConfigureAwait(true);
            workspace.MarkNameAsExplicit();
            workspace.AssociateFile(normalized);
            RecordRecentWorkspace(normalized, workspace.WorkspaceName);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            // The file is there but will not load: corrupt, oversized, or a newer schema. That is
            // a recoverable problem, so the bookmark is left alone rather than offered for removal.
            _ = MessageBox.Show(
                this,
                Ui.Format(Ui.Dialogs.OpenWorkspaceFailedFormat, error.Message),
                Ui.Dialogs.OpenWorkspaceCaption,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }
    }

    /// <summary>
    /// Offers to drop a remembered workspace whose file is gone. Only an explicit yes removes it —
    /// a disconnected drive or a file that is about to come back should not silently lose a pin.
    /// </summary>
    private void PromptToForgetWorkspace(string path)
    {
        var answer = MessageBox.Show(
            this,
            Ui.Format(Ui.Dialogs.WorkspaceMissingPromptFormat, path),
            Ui.Dialogs.OpenWorkspaceCaption,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        MutatePreferences(preferences => preferences with
        {
            PinnedWorkspaces = WorkspaceShortcutSettings.Remove(
                preferences.PinnedWorkspaces, path, WorkspaceShortcutSettings.MaximumPinned),
            RecentWorkspaces = WorkspaceShortcutSettings.Remove(
                preferences.RecentWorkspaces, path, WorkspaceShortcutSettings.MaximumRecent)
        });
    }

    /// <summary>
    /// Moves a workspace to the front of the recent list, refreshing the name stored beside it so
    /// a renamed workspace does not keep an old label. The pinned list is left alone: a path can
    /// be in both, and unpinning should not erase the recent entry.
    /// </summary>
    private void RecordRecentWorkspace(string path, string name)
    {
        var current = LoadPreferences().RecentWorkspaces;
        if (current is { Count: > 0 } &&
            string.Equals(current[0].Path, path, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(current[0].Name, name, StringComparison.Ordinal))
        {
            // Already at the front with the same label, so Ctrl+S does not rewrite settings.
            return;
        }

        MutatePreferences(preferences => preferences with
        {
            RecentWorkspaces = WorkspaceShortcutSettings.Promote(
                preferences.RecentWorkspaces,
                new WorkspaceShortcutEntry(path, name, DateTimeOffset.UtcNow),
                WorkspaceShortcutSettings.MaximumRecent)
        });
    }

    /// <summary>
    /// Applies a change to the settings the shell writes behind the Settings dialog's back, and
    /// republishes the workspace card. The file is re-read immediately before the change rather
    /// than cached, because the dialog writes the same file. Failures are swallowed: losing a
    /// bookmark or a remembered pane count must never break the action that caused it.
    /// </summary>
    private void MutatePreferences(Func<DesktopUpdatePreferences, DesktopUpdatePreferences> change)
    {
        try
        {
            var updated = change(LoadPreferences());
            _updatePreferencesStore.Save(updated);
            PublishWorkspaceShortcuts(updated);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Save validates and throws, and the settings file may be locked or read-only.
        }
    }

    private DesktopUpdatePreferences LoadPreferences() => _updatePreferencesStore.Load();

    internal const string ConnectionsPanelCommandId = "view.connections-panel";
    private const int ConnectionsPanelMinimum = 220;
    private const int WorkspaceMinimum = 560;

    internal bool IsConnectionsPanelVisible => _connectionsPanelSide == ConnectionsPanelSide.Left
        ? !_shellSplit.Panel1Collapsed
        : !_shellSplit.Panel2Collapsed;

    /// <summary>
    /// Docks the connections panel to one side and the workspace area to the other.
    ///
    /// Every property that depends on the side is set here together. A SplitContainer's
    /// SplitterDistance is always Panel1's width, so which panel holds what, which one is fixed
    /// while the window resizes, which minimum applies to which side, and which one the hide
    /// toggle collapses all have to flip as one; splitting them across methods is how they drift.
    /// </summary>
    private void ApplyConnectionsPanelSide(ConnectionsPanelSide side, int width, bool visible)
    {
        _restoringPanelLayout = true;
        _shellSplit.SuspendLayout();
        try
        {
            _shellSplit.Panel1Collapsed = false;
            _shellSplit.Panel2Collapsed = false;
            _shellSplit.Panel1.Controls.Clear();
            _shellSplit.Panel2.Controls.Clear();

            var left = side == ConnectionsPanelSide.Left;
            _shellSplit.Panel1.Controls.Add(left ? _connectionsPanel : _workspaceSplit);
            _shellSplit.Panel2.Controls.Add(left ? _workspaceSplit : _connectionsPanel);
            _shellSplit.Panel1MinSize = left ? ConnectionsPanelMinimum : WorkspaceMinimum;
            _shellSplit.Panel2MinSize = left ? WorkspaceMinimum : ConnectionsPanelMinimum;
            _shellSplit.FixedPanel = left ? FixedPanel.Panel1 : FixedPanel.Panel2;
            _shellSplit.Panel1.BackColor = left ? StorageHubTheme.Surface : StorageHubTheme.Canvas;
            _shellSplit.Panel2.BackColor = left ? StorageHubTheme.Canvas : StorageHubTheme.Surface;
            _connectionsPanelSide = side;

            SetConnectionsPanelWidth(width);
            if (left)
            {
                _shellSplit.Panel1Collapsed = !visible;
            }
            else
            {
                _shellSplit.Panel2Collapsed = !visible;
            }
        }
        finally
        {
            _shellSplit.ResumeLayout(true);
            _restoringPanelLayout = false;
        }
    }

    /// <summary>
    /// Sets the panel's width in pixels whichever side it is on. SplitterDistance throws when it
    /// would violate either minimum, so every write goes through here: clamped, and given up on
    /// rather than crashing the shell when the window is too narrow to honour it at all.
    /// </summary>
    private void SetConnectionsPanelWidth(int width)
    {
        var available = _shellSplit.Width - _shellSplit.SplitterWidth;
        if (available <= _shellSplit.Panel1MinSize + _shellSplit.Panel2MinSize)
        {
            return;
        }

        var distance = _connectionsPanelSide == ConnectionsPanelSide.Left ? width : available - width;
        distance = Math.Clamp(
            distance,
            _shellSplit.Panel1MinSize,
            available - _shellSplit.Panel2MinSize);
        try
        {
            _shellSplit.SplitterDistance = distance;
        }
        catch (InvalidOperationException)
        {
            // The window is too small to honour the stored width; the default split stands.
        }
    }

    internal int CurrentConnectionsPanelWidth() => _connectionsPanelSide == ConnectionsPanelSide.Left
        ? _shellSplit.SplitterDistance
        : _shellSplit.Width - _shellSplit.SplitterWidth - _shellSplit.SplitterDistance;

    private void RestoreConnectionsPanelLayout()
    {
        var preferences = LoadPreferences();
        ApplyConnectionsPanelSide(
            preferences.ConnectionsPanelSide,
            preferences.ConnectionsPanelWidth,
            preferences.ConnectionsPanelVisible);
        SyncConnectionsPanelCommandState();
    }

    private void ShellSplitterMoved(object? sender, SplitterEventArgs e)
    {
        if (!IsHandleCreated || _restoringPanelLayout || !IsConnectionsPanelVisible)
        {
            return;
        }

        var width = CurrentConnectionsPanelWidth();
        if (width is < ConnectionsPanelMinimum or > DesktopUpdatePreferences.MaximumConnectionsPanelWidth)
        {
            return;
        }

        MutatePreferences(current => current with { ConnectionsPanelWidth = width });
    }

    internal void ToggleConnectionsPanelSide()
    {
        var side = _connectionsPanelSide == ConnectionsPanelSide.Left
            ? ConnectionsPanelSide.Right
            : ConnectionsPanelSide.Left;

        // Carried across as a width rather than a splitter position, so the panel is the same size
        // after the move instead of jumping to the mirror of where the splitter happened to be.
        var width = IsConnectionsPanelVisible
            ? CurrentConnectionsPanelWidth()
            : LoadPreferences().ConnectionsPanelWidth;
        ApplyConnectionsPanelSide(side, width, IsConnectionsPanelVisible);
        MutatePreferences(current => current with { ConnectionsPanelSide = side });
    }

    internal void SetConnectionsPanelVisible(bool visible)
    {
        if (_connectionsPanelSide == ConnectionsPanelSide.Left)
        {
            _shellSplit.Panel1Collapsed = !visible;
        }
        else
        {
            _shellSplit.Panel2Collapsed = !visible;
        }

        SyncConnectionsPanelCommandState();
        MutatePreferences(current => current with { ConnectionsPanelVisible = visible });
        if (visible)
        {
            _ = _connectionsPanel.RefreshAsync(_lifetime.Token);
        }
    }

    private void ToggleConnectionsPanel() => SetConnectionsPanelVisible(!IsConnectionsPanelVisible);

    /// <summary>Mirrors the panel's visibility onto the two places that offer to change it.</summary>
    private void SyncConnectionsPanelCommandState()
    {
        var visible = IsConnectionsPanelVisible;
        _connectionsPanelButton = _toolbar?.Items.OfType<ToolStripButton>()
            .FirstOrDefault(button => Equals(button.Tag, UiCommandIds.ViewConnectionsPanel));
        if (_connectionsPanelButton is not null)
        {
            _connectionsPanelButton.Checked = visible;
        }

        foreach (var item in _menu.Items.OfType<ToolStripMenuItem>()
            .SelectMany(root => root.DropDownItems.OfType<ToolStripMenuItem>())
            .Where(item => item.Tag is string id && string.Equals(id, ConnectionsPanelCommandId, StringComparison.Ordinal)))
        {
            item.Checked = visible;
        }
    }

    /// <summary>
    /// Flattens the two stored lists into the order they are drawn in: pinned first, then recent
    /// with anything already pinned skipped. A path deliberately lives in both lists so that
    /// unpinning does not also erase the recent entry, and this is where that overlap collapses.
    /// </summary>
    private static List<WorkspaceShortcutView> ComposeWorkspaceShortcuts(
        DesktopUpdatePreferences preferences)
    {
        var pinned = WorkspaceShortcutSettings.Resolve(
            preferences.PinnedWorkspaces, WorkspaceShortcutSettings.MaximumPinned);
        var views = new List<WorkspaceShortcutView>(pinned.Count);
        views.AddRange(pinned.Select(entry => new WorkspaceShortcutView(
            entry, IsPinned: true, WorkspaceShortcutSettings.LooksPresent(entry.Path))));
        foreach (var entry in WorkspaceShortcutSettings.Resolve(
            preferences.RecentWorkspaces, WorkspaceShortcutSettings.MaximumRecent))
        {
            if (WorkspaceShortcutSettings.Contains(pinned, entry.Path)) continue;
            views.Add(new WorkspaceShortcutView(
                entry, IsPinned: false, WorkspaceShortcutSettings.LooksPresent(entry.Path)));
        }

        return views;
    }

    private void PublishWorkspaceShortcuts(DesktopUpdatePreferences preferences) =>
        _overview.ShowWorkspaceShortcuts(ComposeWorkspaceShortcuts(preferences));

    /// <summary>
    /// Pins or unpins a workspace. Pinning one that has never been saved runs Save As first, so a
    /// pin always refers to a real file.
    /// </summary>
    private void ToggleWorkspacePin(string? path, string? name)
    {
        if (path is null)
        {
            var workspace = GetActiveWorkspace();
            if (workspace is null) return;
            if (workspace.FilePath is null && !SaveActiveWorkspace(saveAs: true)) return;
            if (workspace.FilePath is not { } saved) return;
            path = saved;
            name = workspace.WorkspaceName;
        }

        var pinnedAlready = WorkspaceShortcutSettings.Contains(LoadPreferences().PinnedWorkspaces, path);
        var target = path;
        MutatePreferences(preferences => preferences with
        {
            PinnedWorkspaces = pinnedAlready
                ? WorkspaceShortcutSettings.Remove(
                    preferences.PinnedWorkspaces, target, WorkspaceShortcutSettings.MaximumPinned)
                : WorkspaceShortcutSettings.Promote(
                    preferences.PinnedWorkspaces,
                    new WorkspaceShortcutEntry(target, name, DateTimeOffset.UtcNow),
                    WorkspaceShortcutSettings.MaximumPinned)
        });
    }

    private void ForgetWorkspace(string path) => MutatePreferences(preferences => preferences with
    {
        PinnedWorkspaces = WorkspaceShortcutSettings.Remove(
            preferences.PinnedWorkspaces, path, WorkspaceShortcutSettings.MaximumPinned),
        RecentWorkspaces = WorkspaceShortcutSettings.Remove(
            preferences.RecentWorkspaces, path, WorkspaceShortcutSettings.MaximumRecent)
    });

    private bool SaveActiveWorkspace(bool saveAs)
    {
        var workspace = GetActiveWorkspace();
        if (workspace is null) return false;
        var path = saveAs ? null : workspace.FilePath;
        if (path is null)
        {
            using var dialog = new SaveFileDialog
            {
                Title = "Save Workspace",
                Filter = WorkspaceFileStore.Filter,
                DefaultExt = "shw",
                AddExtension = true,
                FileName = SanitizeWorkspaceFileName(workspace.WorkspaceName) + ".shw"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return false;
            path = dialog.FileName;
        }
        try
        {
            workspace.Save(path);
            if (_workspaceTabs.SelectedTab is { } page) UpdateWorkspaceTab(page, workspace);
            // Save normalises the path and forces the .shw extension, so the workspace's own
            // FilePath is the one to remember, not what the dialog handed back.
            if (workspace.FilePath is { } saved) RecordRecentWorkspace(saved, workspace.WorkspaceName);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            _ = MessageBox.Show(
                this,
                Ui.Format(Ui.Dialogs.SaveWorkspaceFailedFormat, error.Message),
                Ui.Dialogs.SaveWorkspaceCaption,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }
    }

    private void RenameActiveWorkspace()
    {
        var workspace = GetActiveWorkspace();
        if (workspace is null) return;
        using var dialog = new Form
        {
            Text = Ui.Shell.RenameWorkspaceTitle,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ClientSize = new Size(420, 120),
            MinimizeBox = false,
            MaximizeBox = false
        };
        var name = new StorageHubTextField { Text = workspace.WorkspaceName, Left = 14, Top = 16, Width = 390, MaxLength = 128 };
        var accept = new StorageHubButton { Text = Ui.Shell.RenameWorkspaceAccept, DialogResult = DialogResult.OK, Left = 230, Top = 62, Width = 82 };
        var cancel = new StorageHubButton { Text = Ui.Dialogs.ButtonCancel, DialogResult = DialogResult.Cancel, Left = 322, Top = 62, Width = 82 };
        dialog.Controls.AddRange([name, accept, cancel]);
        dialog.AcceptButton = accept; dialog.CancelButton = cancel;
        if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(name.Text))
            workspace.WorkspaceName = name.Text;
    }

    private static string SanitizeWorkspaceFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var value = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(value) ? Ui.Shell.MenuWorkspace : value;
    }

    private void UpdateWorkspaceCommandState()
    {
        var active = GetActiveWorkspace() is not null;
        var root = _menu.Items.OfType<ToolStripMenuItem>().FirstOrDefault(item => item.Text == Ui.Shell.MenuWorkspace);
        if (root is null) return;
        foreach (ToolStripItem item in root.DropDownItems)
        {
            // Only the static catalog commands are governed here; they alone carry a string Tag.
            // The pinned and recent entries are always clickable — at launch, with no workspace
            // open, they are the most useful thing in the menu — and a missing one is dimmed by
            // colour rather than disabled, because clicking it is how it gets pruned.
            if (item.Tag is not string commandId) continue;

            // Keyed on the command id, not on the label. Comparing the text to the English
            // wording meant that in any other language none of the three matched, so opening
            // the app in Danish or German left New Workspace, Open Workspace and Exit disabled
            // until a workspace existed -- including their toolbar buttons, which mirror this.
            item.Enabled = commandId is UiCommandIds.WorkspaceNewWorkspace
                or UiCommandIds.WorkspaceOpenWorkspace
                or UiCommandIds.WorkspaceExit || active;
        }
    }

    private void CloseActiveWorkspace()
    {
        var selected = _workspaceTabs.SelectedTab;
        if (selected?.Tag is not WorkspaceTabMetadata { Closable: true })
        {
            return;
        }

        CloseWorkspaceAt(_workspaceTabs.TabPages.IndexOf(selected));
    }

    private void CloseWorkspaceAt(int index)
    {
        if ((uint)index >= (uint)(_workspaceTabs.TabPages.Count - 1) ||
            _workspaceTabs.TabPages[index].Tag is not WorkspaceTabMetadata { Closable: true })
        {
            return;
        }

        var page = _workspaceTabs.TabPages[index];
        if (page.Controls.OfType<WorkspaceControl>().SingleOrDefault() is { IsDirty: true } workspace && Visible)
        {
            var choice = MessageBox.Show(
                this,
                Ui.Format(Ui.Dialogs.SaveWorkspaceChangesPromptFormat, workspace.WorkspaceName),
                Ui.Dialogs.CloseWorkspaceCaption,
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);
            if (choice == DialogResult.Cancel || choice == DialogResult.Yes && !SaveWorkspace(workspace, page)) return;
        }
        _changingWorkspaceTabs = true;
        try
        {
            _workspaceTabs.TabPages.RemoveAt(index);
            if (_workspaceTabs.TabPages.Count == 1)
            {
                _workspaceTabs.SelectedIndex = -1;
            }
        }
        finally
        {
            _changingWorkspaceTabs = false;
        }

        if (_activePane is not null && page.Contains(_activePane))
        {
            _activePane = null;
        }

        page.Dispose();
    }

    private bool SaveWorkspace(WorkspaceControl workspace, TabPage page)
    {
        var previous = _workspaceTabs.SelectedTab;
        _workspaceTabs.SelectedTab = page;
        var saved = SaveActiveWorkspace(saveAs: false);
        if (previous is not null && _workspaceTabs.TabPages.Contains(previous)) _workspaceTabs.SelectedTab = previous;
        return saved;
    }

    private void ShowConnectionManager(
        Guid? connectionId = null,
        ConnectionEditorTab tab = ConnectionEditorTab.General)
    {
        using var dialog = new ConnectionManagerForm(connectionId, initialTab: tab);

        // Subscribed rather than only refreshing on close, so saving in the editor updates the
        // panel behind it while the dialog is still open.
        void ProfilesChanged(object? sender, EventArgs args) => RefreshConnectionSurfaces();
        dialog.ProfilesChanged += ProfilesChanged;
        try
        {
            _ = dialog.ShowDialog(this);
        }
        finally
        {
            dialog.ProfilesChanged -= ProfilesChanged;
        }

        RefreshConnectionSurfaces();
    }

    /// <summary>
    /// Re-lists the two surfaces that show saved connections. There is no change bus: the routes
    /// that can alter a connection call this instead, which keeps the subscription lifetime tied
    /// to this window rather than to a static event that would outlive it.
    /// </summary>
    private void RefreshConnectionSurfaces()
    {
        var connectionPreferences = _updatePreferencesStore.Load();
        _connectionsPanel.ShowFavoritesInTheirFolders = connectionPreferences.ShowFavoritesInTheirFolders;
        _connectionsPanel.FolderIcons = connectionPreferences.FolderIcons
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
        _ = _connectionsPanel.RefreshAsync(_lifetime.Token);
        _ = _overview.RefreshAsync(_lifetime.Token);
    }

    private void ShowSyncProfileEditor()
    {
        using var dialog = new SyncProfileEditorForm();
        _ = dialog.ShowDialog(this);
        if (dialog.LastGeneratedRun is { } run)
        {
            _syncTasks.RecordRun(run);
        }

        _ = _syncTasks.RefreshAsync(_lifetime.Token);
    }

    private void ShowSchedules()
    {
        using var dialog = new ScheduleManagerForm();
        _ = dialog.ShowDialog(this);
    }

    /// <summary>
    /// Opens the four agent clients a settings transfer needs. Connections, sync tasks and
    /// schedules each live behind their own contract, and all of them are non-secret: credentials
    /// travel on a separate write-only pipe that is deliberately not opened here.
    /// </summary>
    private static SettingsAgentClients CreateSettingsAgentClients() => new(
        new NamedPipeRemoteStorageAgentClient(),
        new NamedPipeRemoteConnectionProfileClient(),
        new NamedPipeSyncManagementAgentClient(),
        new NamedPipeScheduleManagementAgentClient());

    private static void DisposeSettingsAgentClients(SettingsAgentClients clients)
    {
        _ = clients.Storage.DisposeAsync().AsTask();
        _ = clients.Profiles.DisposeAsync().AsTask();
        _ = clients.Sync.DisposeAsync().AsTask();
        _ = clients.Schedules.DisposeAsync().AsTask();
    }

    private void ShowSettingsExport()
    {
        var clients = CreateSettingsAgentClients();
        try
        {
            using var dialog = new SettingsExportForm(
                new SettingsExportService(_updatePreferencesStore, clients));
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.ExportedPath is { } path)
            {
                _locationStatus.Text = Ui.Format(Ui.Shell.SettingsExportedFormat, Path.GetFileName(path));
            }
        }
        finally
        {
            DisposeSettingsAgentClients(clients);
        }
    }

    private async Task ShowSettingsImportAsync()
    {
        var clients = CreateSettingsAgentClients();
        SettingsImportReport? report;
        try
        {
            var exporter = new SettingsExportService(_updatePreferencesStore, clients);
            var importer = new SettingsImportService(
                _updatePreferencesStore,
                exporter,
                SettingsImportService.DefaultBackupDirectory(_updatePreferencesStore.FilePath),
                clock: null,
                agent: clients);

            using var dialog = new SettingsImportForm(importer);
            _ = dialog.ShowDialog(this);
            report = dialog.Report;
        }
        finally
        {
            DisposeSettingsAgentClients(clients);
        }

        if (report is null || !report.Succeeded || report.Applied.Count == 0) return;

        // Imported settings are live everywhere the shell reads them, so refresh what is already
        // on screen rather than waiting for the next restart.
        var preferences = _updatePreferencesStore.Load();
        DesktopAppearanceService.SetAppearance(preferences.Appearance);
        _updater.SavePreferences(preferences);
        RefreshShortcutPresentation();
        PublishWorkspaceShortcuts(preferences);
        UpdateWorkspaceCommandState();
        _locationStatus.Text = Ui.Shell.StatusSettingsImported;

        if (report.ConcurrencyChanged)
        {
            await ApplyConcurrencySettingsAsync();
        }
    }

    private void ShowAgentControl()
    {
        using var dialog = new AgentControlForm(
            () => _lastAgentStatus,
            _packagedLifecycle is { } lifecycle ? new PackagedAgentLifecycleController(lifecycle) : null);
        _ = dialog.ShowDialog(this);
    }

    private void ShowKeyStore()
    {
        // Two clients: metadata travels on the ordinary pipe, and material is enrolled only on the
        // dedicated secret pipe, which never reads anything back.
        var client = new NamedPipeKeyStoreAgentClient();
        var secrets = new NamedPipeRemoteSecretVaultClient();
        try
        {
            using var dialog = new KeyStoreForm(client, secrets);
            _ = dialog.ShowDialog(this);
        }
        finally
        {
            _ = client.DisposeAsync().AsTask();
            _ = secrets.DisposeAsync().AsTask();
        }
    }

    private void ActivePaneEntered(object? sender, EventArgs e)
    {
        _activePane = sender as BrowserPaneControl;
    }

    private BrowserPaneControl? GetActivePane()
    {
        var workspace = GetActiveWorkspace();
        if (workspace is null) return null;
        return _activePane is not null && workspace.Contains(_activePane)
            ? _activePane
            : workspace.ActivePane;
    }

    private void NavigateActivePane(PaneNavigation navigation)
    {
        var pane = GetActivePane();
        switch (navigation)
        {
            case PaneNavigation.Back:
                pane?.NavigateBack();
                break;
            case PaneNavigation.Forward:
                pane?.NavigateForward();
                break;
            case PaneNavigation.Up:
                pane?.NavigateUp();
                break;
            case PaneNavigation.Refresh:
                pane?.Reload();
                break;
        }
    }

    /// <summary>
    /// Whether a declared command is actually wired up yet.
    /// </summary>
    /// <remarks>
    /// Keyed on the id rather than the label, so the set does not change with the language,
    /// and so a command that is renamed keeps its place here.
    /// </remarks>
    internal static bool IsAvailableCommand(string commandId) => commandId is
        UiCommandIds.WorkspaceNewWorkspace or
        UiCommandIds.WorkspaceOpenWorkspace or
        UiCommandIds.WorkspaceSaveWorkspace or
        UiCommandIds.WorkspaceSaveWorkspaceAs or
        UiCommandIds.WorkspaceRenameWorkspace or
        UiCommandIds.WorkspaceCloseWorkspace or
        UiCommandIds.WorkspaceExit or
        UiCommandIds.EditCut or
        UiCommandIds.EditCopy or
        UiCommandIds.EditPaste or
        UiCommandIds.EditNewFolder or
        UiCommandIds.EditNewEmptyFile or
        UiCommandIds.EditRename or
        UiCommandIds.EditBatchRename or
        UiCommandIds.EditDelete or
        UiCommandIds.EditSelectAll or
        UiCommandIds.EditInvertSelection or
        UiCommandIds.EditProperties or
        UiCommandIds.ViewRefresh or
        UiCommandIds.ViewConnectionsPanel or
        UiCommandIds.ViewMoveConnectionsPanel or
        UiCommandIds.GoBack or
        UiCommandIds.GoForward or
        UiCommandIds.GoUp or
        UiCommandIds.GoFocusAddress or
        UiCommandIds.GoNextPane or
        UiCommandIds.ConnectionsNewConnection or
        UiCommandIds.ConnectionsKeyStore or
        UiCommandIds.ToolsBackgroundAgent or
        UiCommandIds.SyncReviewRun or
        UiCommandIds.SyncSyncProfiles or
        UiCommandIds.SyncSchedules or
        UiCommandIds.ToolsSettings or
        UiCommandIds.ToolsExportSettings or
        UiCommandIds.ToolsImportSettings or
        UiCommandIds.HelpCheckForUpdates or
        UiCommandIds.HelpAboutStorageHub;

    private enum PaneNavigation
    {
        Back,
        Forward,
        Up,
        Refresh
    }

    private void EnqueueFromFocusedPane(TransferQueueOperation operation)
    {
        if (!TryGetActiveWorkspacePanes(out var source, out var destination))
        {
            return;
        }

        if (destination.ContainsFocus)
        {
            (source, destination) = (destination, source);
        }

        _ = EnqueueManualTransferAsync(source, destination, operation);
    }

    private void StageFocusedPane(TransferQueueOperation operation)
    {
        var pane = GetActivePane();
        if (pane is null)
        {
            return;
        }

        var selection = pane.CaptureSelectionSnapshot();
        if (selection.IsFailure)
        {
            ShowManualTransferFailure(selection.Error.Message);
            return;
        }

        StagePaneSelection(pane, new PaneSelectionStagedEventArgs(selection.Value, operation));
    }

    private void ShowObjectInspectorFromFocusedPane()
    {
        if (GetActivePane() is { } pane) ShowObjectInspector(pane);
    }

    private void ShowObjectInspector(BrowserPaneControl pane)
    {
        var selected = pane.CaptureSelectionSnapshot();
        if (selected.IsFailure)
        {
            ShowObjectInspectorFailure(selected.Error.Message);
            return;
        }

        var snapshot = selected.Value;
        if (snapshot.Items.Count != 1 || snapshot.Items[0].Kind != StorageItemKind.File)
        {
            ShowObjectInspectorFailure(Ui.Shell.SelectOneToInspect);
            return;
        }

        var context = snapshot.Context;
        if (context.Kind != PaneTransferContextKind.SavedConnection ||
            context.ConnectionId is not { } connectionId ||
            string.IsNullOrWhiteSpace(context.RootIdentity))
        {
            ShowObjectInspectorFailure(
                Ui.Shell.InspectionRequiresConnection);
            return;
        }

        var item = snapshot.Items[0];
        var address = new ObjectInspectorAddress(
            connectionId,
            context.RootIdentity,
            item.RelativePath,
            item.NativeItemId,
            item.VersionId,
            item.EntityTag);
        if (!address.HasValidBounds)
        {
            ShowObjectInspectorFailure(Ui.Shell.FileHasNoObjectIdentity);
            return;
        }

        using var dialog = new ObjectInspectorForm(address);
        _ = dialog.ShowDialog(this);
    }

    private bool TryGetActiveWorkspacePanes(
        out BrowserPaneControl source,
        out BrowserPaneControl destination)
    {
        source = null!;
        destination = null!;
        var page = _workspaceTabs.SelectedTab;
        if (page?.Tag is not WorkspaceTabMetadata { Closable: true })
        {
            return false;
        }

        var split = page.Controls.OfType<SplitContainer>().SingleOrDefault();
        source = split?.Panel1.Controls.OfType<BrowserPaneControl>().SingleOrDefault()!;
        destination = split?.Panel2.Controls.OfType<BrowserPaneControl>().SingleOrDefault()!;
        return source is not null && destination is not null;
    }

    private async Task EnqueueManualTransferAsync(
        BrowserPaneControl source,
        BrowserPaneControl destination,
        TransferQueueOperation operation,
        PaneSelectionSnapshot? capturedSelection = null)
    {
        if (_lifetime.IsCancellationRequested)
        {
            return;
        }

        PaneSelectionSnapshot selection;
        if (capturedSelection is null)
        {
            var captured = source.CaptureSelectionSnapshot();
            if (captured.IsFailure)
            {
                ShowManualTransferFailure(captured.Error.Message);
                return;
            }

            selection = captured.Value;
        }
        else
        {
            selection = capturedSelection;
        }

        _locationStatus.Text = Ui.Shell.StatusIndexingDestination;
        if (!await destination.EnsureListingCompleteAsync(_lifetime.Token).ConfigureAwait(true))
        {
            ShowManualTransferFailure(Ui.Shell.CouldNotFinishIndexing);
            return;
        }

        var destinationSnapshot = destination.CaptureDestinationSnapshot(
            selection.Items.Select(static item => item.Name).ToArray());
        if (destinationSnapshot.IsFailure)
        {
            ShowManualTransferFailure(destinationSnapshot.Error.Message);
            return;
        }

        try
        {
            var result = selection.Items.Any(static item => item.IsContainer)
                ? await _recursiveTransfers.EnqueueAsync(
                    selection,
                    destinationSnapshot.Value,
                    operation,
                    _lifetime.Token).ConfigureAwait(true)
                : await _manualTransfers.EnqueueAsync(
                    selection,
                    destinationSnapshot.Value,
                    operation,
                    cancellationToken: _lifetime.Token).ConfigureAwait(true);
            if (result.HasAmbiguity)
            {
                ShowManualTransferFailure(DescribeAmbiguousEnqueue(
                    selection.Items.Count,
                    result.Accepted.Count,
                    result.AmbiguousTransferIds));
                return;
            }

            if (result.Failure is null)
            {
                if (selection.Items.Any(static item => item.IsContainer))
                {
                    _locationStatus.Text = result.Accepted.Count == 0
                        ? Ui.Shell.EmptyDestinationCreated
                        : Ui.Format(Ui.Shell.QueuedFromManifestFormat, result.Accepted.Count);
                    if (result.Accepted.Count == 0)
                    {
                        destination.Reload();
                    }
                }

                return;
            }

            var message = result.IsPartial
                ? $"{result.Accepted.Count} transfer(s) were durably accepted before the next request failed. {result.Failure.Message}"
                : result.Failure.Message;
            ShowManualTransferFailure(message);
        }
        catch (ManualTransferEnqueueAmbiguousException error)
        {
            if (!_lifetime.IsCancellationRequested)
            {
                ShowManualTransferFailure(DescribeAmbiguousEnqueue(
                    selection.Items.Count,
                    error.AcceptedTransferIds.Count,
                    error.AmbiguousTransferIds));
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The window is closing. Any already accepted jobs remain in the durable queue.
        }
        catch (Exception error) when (
            error is IOException or InvalidDataException or InvalidOperationException or TimeoutException)
        {
            ShowManualTransferFailure(Ui.Shell.AgentCannotEnqueue);
        }
    }

    private void EnqueuePaneDrop(
        TabPage workspace,
        BrowserPaneControl destination,
        PaneTransferDropRequestedEventArgs args)
    {
        if (ReferenceEquals(args.SourcePane, destination) ||
            !workspace.Contains(args.SourcePane) ||
            !workspace.Contains(destination))
        {
            return;
        }

        _ = EnqueueManualTransferAsync(
            args.SourcePane,
            destination,
            args.Operation,
            args.Selection);
    }

    private void StagePaneSelection(BrowserPaneControl source, PaneSelectionStagedEventArgs args)
    {
        _paneClipboard = new PaneClipboardSnapshot(source, args.Selection, args.Operation);
        var verb = args.Operation == TransferQueueOperation.Move ? Ui.Shell.StagedCut : Ui.Shell.StagedCopied;
        _locationStatus.Text = Ui.Format(Ui.Shell.StagedSelectionFormat, verb, args.Selection.Items.Count);
        RefreshPaneClipboardPresentation();
    }

    private void PasteIntoPane(BrowserPaneControl destination)
    {
        if (_paneClipboard is not { } clipboard)
        {
            return;
        }

        var moving = clipboard.Operation == TransferQueueOperation.Move;
        var operation = moving ? Ui.Dialogs.TransferOperationMove : Ui.Dialogs.TransferOperationCopy;
        var itemSummary = clipboard.Selection.Items.Count == 1
            ? clipboard.Selection.Items[0].Name
            : Ui.Format(Ui.Dialogs.SelectedItemsFormat, clipboard.Selection.Items.Count);
        var decision = MessageBox.Show(
            this,
            Ui.Format(
                Ui.Dialogs.ReviewTransferBodyFormat,
                operation,
                itemSummary,
                DescribePaneContext(clipboard.Selection.Context),
                destination.PaneDisplayName,
                moving ? Ui.Dialogs.TransferOriginalsRemoved : Ui.Dialogs.TransferOriginalsRemain),
            Ui.Format(Ui.Dialogs.ReviewTransferCaptionFormat, operation.ToLower(CultureInfo.CurrentCulture)),
            MessageBoxButtons.OKCancel,
            clipboard.Operation == TransferQueueOperation.Move
                ? MessageBoxIcon.Warning
                : MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button2);
        if (decision != DialogResult.OK)
        {
            return;
        }

        _ = EnqueueManualTransferAsync(
            clipboard.SourcePane,
            destination,
            clipboard.Operation,
            clipboard.Selection);
    }

    private void ClearPaneClipboard()
    {
        _paneClipboard = null;
        _locationStatus.Text = Ui.Shell.StatusClipboardCleared;
        RefreshPaneClipboardPresentation();
    }

    private void RefreshPaneClipboardPresentation()
    {
        var description = _paneClipboard is { } clipboard
            ? Ui.Format(
                Ui.Shell.ClipboardSummaryFormat,
                UiEnumNames.Describe(clipboard.Operation),
                clipboard.Selection.Items.Count,
                DescribePaneContext(clipboard.Selection.Context))
            : Ui.Shell.ClipboardEmpty;
        foreach (var toolbar in FindControls<ToolStrip>(_workspaceTabs))
        {
            if (toolbar.Items["WorkspaceClipboardStatus"] is ToolStripLabel status)
            {
                status.Text = description;
            }

            if (toolbar.Items["WorkspaceClipboardPaste"] is ToolStripButton paste)
            {
                paste.Enabled = _paneClipboard is not null;
            }

            if (toolbar.Items["WorkspaceClipboardClear"] is ToolStripButton clear)
            {
                clear.Enabled = _paneClipboard is not null;
            }
        }

        foreach (var pane in FindControls<BrowserPaneControl>(_workspaceTabs))
        {
            pane.RefreshCommandState();
        }
    }

    private static IEnumerable<T> FindControls<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindControls<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static string DescribePaneContext(PaneTransferContext context) => context.Kind switch
    {
        PaneTransferContextKind.SavedConnection => string.IsNullOrEmpty(context.RelativePath)
            ? "saved connection root"
            : context.RelativePath,
        PaneTransferContextKind.ThisPc => string.IsNullOrEmpty(context.RelativePath)
            ? "This PC"
            : context.RelativePath,
        _ => string.IsNullOrEmpty(context.RelativePath) ? "storage pane" : context.RelativePath
    };

    private async Task CreatePaneItemAsync(BrowserPaneControl pane, bool createFolder)
    {
        var captured = pane.CaptureCurrentLocation();
        if (captured.IsFailure || captured.Value.Kind is not (PaneTransferContextKind.ThisPc or PaneTransferContextKind.SavedConnection))
        {
            ShowManualTransferFailure(captured.IsFailure
                ? captured.Error.Message
                : Ui.Shell.OpenFolderBeforeCreating);
            return;
        }

        using var dialog = new PaneItemNameDialog(
            createFolder ? Ui.Shell.NewFolderTitle : Ui.Shell.NewEmptyFile,
            createFolder ? Ui.Shell.FolderName : Ui.Shell.FileName,
            createFolder ? Ui.Shell.NewFolderTitle : Ui.Shell.DefaultFileName,
            Ui.Shell.Create);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var context = captured.Value;
        try
        {
            if (context.Kind == PaneTransferContextKind.ThisPc)
            {
                var target = CombineLocalChild(context.RelativePath, dialog.ItemName);
                if (createFolder)
                {
                    if (Directory.Exists(target) || File.Exists(target))
                        throw new IOException(Ui.Shell.NameAlreadyExists);
                    Directory.CreateDirectory(target);
                }
                else
                {
                    await using var stream = new FileStream(
                        target, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous);
                    await stream.FlushAsync(_lifetime.Token).ConfigureAwait(true);
                }
            }
            else
            {
                var address = CreateRemoteChildAddress(context, dialog.ItemName);
                await using var client = new NamedPipeObjectInspectorAgentClient();
                StorageIpcFailure? failure;
                if (createFolder)
                {
                    var response = await client.CreateDirectoryAsync(
                        new StorageDirectoryCreateRequest(EditableFileIpcContract.CurrentVersion, address),
                        _lifetime.Token).ConfigureAwait(true);
                    failure = response.Failure;
                }
                else
                {
                    var response = await client.CreateFileAsync(
                        new StorageFileCreateRequest(EditableFileIpcContract.CurrentVersion, address),
                        _lifetime.Token).ConfigureAwait(true);
                    failure = response.Failure;
                }
                if (failure is not null) throw new InvalidOperationException(failure.Message);
            }

            pane.Reload();
            _locationStatus.Text = Ui.Format(
                createFolder ? Ui.Shell.CreatedFolderFormat : Ui.Shell.CreatedFileFormat,
                dialog.ItemName);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) when (IsPaneMutationFailure(error))
        {
            ShowManualTransferFailure(Ui.Format(Ui.Shell.ItemCreateFailedFormat, error.Message));
        }
    }

    private async Task RenamePaneSelectionAsync(BrowserPaneControl pane)
    {
        var captured = pane.CaptureSelectionSnapshot();
        if (captured.IsFailure || captured.Value.Items.Count != 1)
        {
            ShowManualTransferFailure(captured.IsFailure ? captured.Error.Message : Ui.Shell.SelectOneToRename);
            return;
        }
        var selection = captured.Value;
        var item = selection.Items[0];
        using var dialog = new PaneItemNameDialog(Ui.Shell.RenameItem, "New name", item.Name, Ui.Shell.RenameWorkspaceAccept);
        if (dialog.ShowDialog(this) != DialogResult.OK || string.Equals(dialog.ItemName, item.Name, StringComparison.Ordinal)) return;

        var result = await RenameItemAsync(selection.Context, item, dialog.ItemName, client: null).ConfigureAwait(true);
        if (result is not null)
        {
            ShowManualTransferFailure(Ui.Format(Ui.Shell.ItemRenameFailedFormat, result));
            return;
        }
        pane.Reload();
        _locationStatus.Text = Ui.Format(Ui.Shell.RenamedOneFormat, item.Name, dialog.ItemName);
    }

    private async Task BatchRenamePaneSelectionAsync(BrowserPaneControl pane)
    {
        var captured = pane.CaptureSelectionSnapshot();
        if (captured.IsFailure || captured.Value.Items.Count < 2)
        {
            ShowManualTransferFailure(captured.IsFailure ? captured.Error.Message : Ui.Shell.SelectTwoToBatchRename);
            return;
        }
        if (!await pane.EnsureListingCompleteAsync(_lifetime.Token).ConfigureAwait(true)) return;
        var destination = pane.CaptureDestinationSnapshot();
        if (destination.IsFailure)
        {
            ShowManualTransferFailure(destination.Error.Message);
            return;
        }

        var selection = captured.Value;
        using var dialog = new BatchRenameDialog(
            selection.Items.Select(static item => item.Name).ToArray(),
            destination.Value.Entries.Select(static item => item.Name));
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        NamedPipeObjectInspectorAgentClient? client = selection.Context.Kind == PaneTransferContextKind.SavedConnection
            ? new NamedPipeObjectInspectorAgentClient()
            : null;
        var renamed = 0;
        try
        {
            foreach (var item in selection.Items.Where(item => dialog.RenameMap.ContainsKey(item.Name)))
            {
                var error = await RenameItemAsync(selection.Context, item, dialog.RenameMap[item.Name], client)
                    .ConfigureAwait(true);
                if (error is not null)
                {
                    ShowManualTransferFailure(Ui.Format(Ui.Shell.RenamedThenStoppedFormat, renamed, item.Name, error));
                    pane.Reload();
                    return;
                }
                renamed++;
            }
        }
        finally
        {
            if (client is not null) await client.DisposeAsync().ConfigureAwait(true);
        }
        pane.Reload();
        _locationStatus.Text = Ui.Format(Ui.Shell.RenamedItemsFormat, renamed);
    }

    private async Task<string?> RenameItemAsync(
        PaneTransferContext context,
        PaneTransferItem item,
        string newName,
        NamedPipeObjectInspectorAgentClient? client)
    {
        try
        {
            if (context.Kind == PaneTransferContextKind.ThisPc)
            {
                var source = Path.GetFullPath(item.RelativePath);
                var parent = Path.GetDirectoryName(source) ?? throw new IOException(Ui.Shell.ParentFolderUnavailable);
                var destination = CombineLocalChild(parent, newName);
                var caseOnlyRename = string.Equals(source, destination, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(source, destination, StringComparison.Ordinal);
                if (!caseOnlyRename && (File.Exists(destination) || Directory.Exists(destination)))
                    return Ui.Shell.NameAlreadyExists;
                if (caseOnlyRename)
                {
                    var temporary = CombineLocalChild(parent, $".storagehub-rename-{Guid.NewGuid():N}.tmp");
                    if (item.IsContainer) Directory.Move(source, temporary);
                    else File.Move(source, temporary, overwrite: false);
                    try
                    {
                        if (item.IsContainer) Directory.Move(temporary, destination);
                        else File.Move(temporary, destination, overwrite: false);
                    }
                    catch
                    {
                        if (item.IsContainer) Directory.Move(temporary, source);
                        else File.Move(temporary, source, overwrite: false);
                        throw;
                    }
                }
                else if (item.IsContainer) Directory.Move(source, destination);
                else File.Move(source, destination, overwrite: false);
                return null;
            }
            if (context.Kind != PaneTransferContextKind.SavedConnection ||
                context.ConnectionId is not { } connectionId || string.IsNullOrWhiteSpace(context.RootIdentity))
                return Ui.Shell.OpenFolderBeforeRenaming;

            var sourceAddress = new ObjectInspectorAddress(
                connectionId, context.RootIdentity, item.RelativePath,
                item.NativeItemId, item.VersionId, item.EntityTag);
            var destinationAddress = CreateRemoteChildAddress(context, newName);
            var ownsClient = client is null;
            client ??= new NamedPipeObjectInspectorAgentClient();
            try
            {
                var response = await client.RenameItemAsync(
                    new StorageItemRenameRequest(
                        EditableFileIpcContract.CurrentVersion, sourceAddress, destinationAddress),
                    _lifetime.Token).ConfigureAwait(true);
                return response.Failure?.Message;
            }
            finally
            {
                if (ownsClient) await client.DisposeAsync().ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return Ui.Shell.OperationCancelled; }
        catch (Exception error) when (IsPaneMutationFailure(error)) { return error.Message; }
    }

    private static string CombineLocalChild(string parent, string name)
    {
        var fullParent = Path.GetFullPath(parent);
        var target = Path.GetFullPath(Path.Combine(fullParent, name));
        if (!string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(target) ?? string.Empty),
                Path.TrimEndingDirectorySeparator(fullParent),
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(Ui.Shell.NameMustBeDirectChild);
        return target;
    }

    private static ObjectInspectorAddress CreateRemoteChildAddress(PaneTransferContext context, string name)
    {
        if (context.ConnectionId is not { } connectionId || string.IsNullOrWhiteSpace(context.RootIdentity))
            throw new ArgumentException(Ui.Shell.PaneHasNoVerifiedIdentity);
        var path = string.IsNullOrEmpty(context.RelativePath) ? name : $"{context.RelativePath}/{name}";
        return new ObjectInspectorAddress(connectionId, context.RootIdentity, path);
    }

    private static bool IsPaneMutationFailure(Exception error) => error is
        IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or
        InvalidOperationException or TimeoutException or NotSupportedException;

    private async Task ReviewDeleteAsync(BrowserPaneControl pane)
    {
        var captured = pane.CaptureSelectionSnapshot();
        if (captured.IsFailure)
        {
            ShowManualTransferFailure(captured.Error.Message);
            return;
        }

        var selection = captured.Value;
        var local = selection.Context.Kind == PaneTransferContextKind.ThisPc;
        var preferences = _updatePreferencesStore.Load();
        if (preferences.ConfirmBeforeDeletingItems)
        {
            using var confirmation = new DeleteItemsConfirmationForm(selection.Items, local);
            if (confirmation.ShowDialog(this) != DialogResult.OK) return;
            if (confirmation.DoNotShowAgain)
            {
                try { _updatePreferencesStore.Save(preferences with { ConfirmBeforeDeletingItems = false }); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
                {
                    ShowManualTransferFailure(Ui.Format(Ui.Shell.DeleteWarningPreferenceFailedFormat, error.Message));
                    return;
                }
            }
        }

        if (!local)
        {
            await DeleteRemoteSelectionAsync(pane, selection).ConfigureAwait(true);
            return;
        }

        try
        {
            foreach (var item in selection.Items)
            {
                _lifetime.Token.ThrowIfCancellationRequested();
                var fullPath = Path.GetFullPath(item.RelativePath);
                if (item.IsContainer)
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                        fullPath,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
                else
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                        fullPath,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
            }

            pane.Reload();
            _locationStatus.Text = Ui.Format(Ui.Shell.SentToRecycleBinFormat, selection.Items.Count);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShowManualTransferFailure(Ui.Format(Ui.Shell.ItemsDeleteFailedFormat, error.Message));
        }

    }

    private async Task DeleteRemoteSelectionAsync(
        BrowserPaneControl pane,
        PaneSelectionSnapshot selection)
    {
        if (selection.Context.Kind != PaneTransferContextKind.SavedConnection ||
            selection.Context.ConnectionId is not { } connectionId ||
            string.IsNullOrWhiteSpace(selection.Context.RootIdentity))
        {
            ShowManualTransferFailure(Ui.Shell.RemoteDeleteRequiresRoot);
            return;
        }

        await using var client = new NamedPipeObjectInspectorAgentClient();
        var deleted = 0;
        foreach (var item in selection.Items)
        {
            try
            {
                var response = await client.DeleteItemAsync(
                    new StorageItemDeleteRequest(
                        EditableFileIpcContract.CurrentVersion,
                        new ObjectInspectorAddress(
                            connectionId,
                            selection.Context.RootIdentity,
                            item.RelativePath,
                            item.NativeItemId,
                            item.VersionId,
                            item.EntityTag),
                        Recursive: item.IsContainer),
                    _lifetime.Token).ConfigureAwait(true);
                if (response.Failure is not null)
                {
                    ShowManualTransferFailure(
                        deleted == 0
                            ? response.Failure.Message
                            : Ui.Format(Ui.Shell.DeletedThenStoppedFormat, deleted, response.Failure.Message));
                    pane.Reload();
                    return;
                }

                deleted++;
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error) when (
                error is IOException or InvalidDataException or InvalidOperationException or TimeoutException)
            {
                ShowManualTransferFailure(
                    deleted == 0
                        ? Ui.Shell.AgentCannotDelete
                        : Ui.Format(Ui.Shell.DeletedThenAgentLostFormat, deleted));
                pane.Reload();
                return;
            }
        }

        pane.Reload();
        _locationStatus.Text = Ui.Format(Ui.Shell.DeletedRemoteItemsFormat, deleted);
    }

    private static Icon? LoadWindowIcon()
    {
        try
        {
            var executable = Environment.ProcessPath;
            return string.IsNullOrWhiteSpace(executable)
                ? null
                : Icon.ExtractAssociatedIcon(executable);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private async void EditSelectedFile(BrowserPaneControl pane)
    {
        var selected = pane.CaptureSelectionSnapshot();
        if (selected.IsFailure)
        {
            ShowManualTransferFailure(selected.Error.Message);
            return;
        }

        var snapshot = selected.Value;
        if (snapshot.Context.Kind != PaneTransferContextKind.SavedConnection ||
            snapshot.Context.ConnectionId is not { } connectionId ||
            string.IsNullOrWhiteSpace(snapshot.Context.RootIdentity) ||
            snapshot.Items.Count != 1 ||
            snapshot.Items[0] is not { Kind: StorageItemKind.File } item)
        {
            ShowManualTransferFailure(Ui.Shell.ExternalEditRequiresOneFile);
            return;
        }

        var address = new ObjectInspectorAddress(
            connectionId,
            snapshot.Context.RootIdentity,
            item.RelativePath,
            item.NativeItemId,
            item.VersionId,
            item.EntityTag);
        if (!address.HasValidBounds)
        {
            ShowManualTransferFailure(Ui.Shell.FileHasNoObjectIdentity);
            return;
        }

        try
        {
            await _externalEditor.OpenAsync(
                this,
                address,
                item.Name,
                item.Length,
                _lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or TimeoutException)
        {
            _ = MessageBox.Show(
                this,
                Ui.Format(Ui.Dialogs.ExternalEditorOpenFailedFormat, error.Message),
                Ui.Dialogs.ExternalEditorCaption,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void ExternalEditorFileUploaded(object? sender, EventArgs e)
    {
        GetActivePane()?.Reload();
        _locationStatus.Text = Ui.Shell.StatusEditedFileUploaded;
    }

    private void ManualTransfersEnqueued(object? sender, ManualTransfersEnqueuedEventArgs e)
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed || Disposing)
                {
                    return;
                }

                var queuedJobs = (int)Math.Min(
                    int.MaxValue,
                    (long)_status.QueuedJobs + e.AcceptedTransferIds.Count);
                ApplyStatus(_status with { QueuedJobs = queuedJobs });
                _ = _transferQueue.RefreshQueueAsync(_lifetime.Token);
            }));
        }
        catch (InvalidOperationException)
        {
            // The window handle can disappear between the guard and BeginInvoke during shutdown.
        }
    }

    private static string FormatTransferIds(IEnumerable<Guid> transferIds) =>
        string.Join(", ", transferIds.Select(static transferId => transferId.ToString("D")));

    private static string DescribeAmbiguousEnqueue(
        int selectedCount,
        int acceptedCount,
        IReadOnlyCollection<Guid> ambiguousTransferIds)
    {
        var unsubmittedCount = Math.Max(0, selectedCount - acceptedCount - ambiguousTransferIds.Count);
        var unsubmitted = unsubmittedCount == 0
            ? string.Empty
            : $" {unsubmittedCount} later selected file(s) were not submitted.";
        return $"{acceptedCount} transfer(s) were durably acknowledged. " +
            Ui.Format(Ui.Shell.AmbiguousTransfersFormat, FormatTransferIds(ambiguousTransferIds)) +
            unsubmitted +
            " Check the queue for those exact IDs before submitting replacement jobs.";
    }

    private void ShowManualTransferFailure(string message)
    {
        if (IsDisposed || Disposing || _lifetime.IsCancellationRequested)
        {
            return;
        }

        _ = MessageBox.Show(
            this,
            message,
            Ui.Dialogs.TransferQueueCaption,
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private void ShowObjectInspectorFailure(string message)
    {
        if (IsDisposed || Disposing || _lifetime.IsCancellationRequested)
        {
            return;
        }

        _ = MessageBox.Show(
            this,
            message,
            Ui.Dialogs.ObjectInspectorCaption,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void AgentMonitorStatusChanged(object? sender, AgentMonitorStatusEventArgs e)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed)
                {
                    return;
                }

                _lastAgentStatus = e.Status;
                ApplyStatus(_status with
                {
                    AgentState = e.Status.State,
                    ActiveJobs = e.Status.ActiveTransfers + e.Status.ActiveSyncRuns
                });
                _agentStatus.ToolTipText = e.Status.Detail;
                if (e.Status.State == AgentConnectionState.Disconnected &&
                    _packagedLifecycle is not null &&
                    !_agentRecoveryInProgress &&
                    !_lifetime.IsCancellationRequested)
                {
                    _ = RecoverAgentAsync();
                }
                if (_agentRestartPending && e.Status.ActiveTransfers + e.Status.ActiveSyncRuns == 0)
                {
                    _ = RestartAgentForConcurrencyAsync();
                }
            }));
        }
        catch (InvalidOperationException)
        {
            // The window handle can disappear between the guard and BeginInvoke during shutdown.
        }
    }

    private async Task ReviewShellImportAsync(ShellImportDropRequestedEventArgs args)
    {
        try
        {
            var plan = await _shellTransfers.PlanShellImportAsync(new ShellImportPlanRequest(
                ShellTransferIpcContract.CurrentVersion, args.SourcePaths, args.Destination), _lifetime.Token).ConfigureAwait(true);
            if (plan.Failure is not null || string.IsNullOrWhiteSpace(plan.ReviewToken))
            {
                ShowManualTransferFailure(plan.Failure?.Message ?? Ui.Shell.CouldNotReviewDrop);
                return;
            }

            var conflicts = plan.Items.Count(item => item.DestinationConflict);
            var choice = conflicts == 0
                ? MessageBox.Show(this, Ui.Format(Ui.Dialogs.ImportFromExplorerPromptFormat, plan.Items.Length), Ui.Dialogs.ImportFromExplorerCaption, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK
                    ? ShellImportDisposition.ReplaceFiles : ShellImportDisposition.Cancel
                : MessageBox.Show(this, Ui.Format(Ui.Dialogs.ImportConflictsPromptFormat, conflicts), Ui.Dialogs.ImportConflictsCaption, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) switch
                {
                    DialogResult.Yes => ShellImportDisposition.ReplaceFiles,
                    DialogResult.No => ShellImportDisposition.SkipConflictingFiles,
                    _ => ShellImportDisposition.Cancel
                };
            var committed = await _shellTransfers.CommitShellImportAsync(new ShellImportCommitRequest(
                ShellTransferIpcContract.CurrentVersion, plan.ReviewToken, choice), _lifetime.Token).ConfigureAwait(true);
            if (committed.Failure is not null) ShowManualTransferFailure(committed.Failure.Message);
            else if (committed.Accepted) _locationStatus.Text = Ui.Format(Ui.Shell.QueuedExplorerImportFormat, committed.TransferIds.Length);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or
            InvalidOperationException or TimeoutException or System.Text.Json.JsonException)
        {
            ShowManualTransferFailure(Ui.Shell.AgentCannotReviewDrop);
        }
    }

    private async Task RecoverAgentAsync()
    {
        if (_packagedLifecycle is null || _agentRecoveryInProgress || _lifetime.IsCancellationRequested)
        {
            return;
        }

        _agentRecoveryInProgress = true;
        ApplyStatus(_status with { AgentState = AgentConnectionState.Starting });
        _agentStatus.ToolTipText = Ui.Shell.AgentReconnectingTooltip;
        try
        {
            var result = await _packagedLifecycle.EnsureAgentAsync(_lifetime.Token);
            if (result.IsReady && !_lifetime.IsCancellationRequested)
            {
                ApplyStatus(_status with { AgentState = AgentConnectionState.Connected });
                _agentStatus.ToolTipText = result.Status == AgentEnsureStatus.Started
                    ? Ui.Shell.AgentRestarted
                    : Ui.Shell.AgentReconnected;
                _ = _overview.RefreshAsync(_lifetime.Token);
                _ = _syncTasks.RefreshAsync(_lifetime.Token);
                _ = _transferQueue.RefreshQueueAsync(_lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            _agentRecoveryInProgress = false;
        }
    }

    private async Task ApplyConcurrencySettingsAsync()
    {
        if (_packagedLifecycle is null)
        {
            _locationStatus.Text = Ui.Shell.StatusConcurrencyRestartRequired;
            return;
        }

        if (_status.ActiveJobs > 0)
        {
            _agentRestartPending = true;
            _locationStatus.Text = Ui.Shell.StatusConcurrencyPendingIdle;
            return;
        }

        await RestartAgentForConcurrencyAsync();
    }

    private async Task RestartAgentForConcurrencyAsync()
    {
        if (_packagedLifecycle is null)
        {
            return;
        }

        if (_status.ActiveJobs > 0)
        {
            _agentRestartPending = true;
            return;
        }

        _agentRestartPending = false;
        _locationStatus.Text = Ui.Shell.StatusApplyingConcurrency;
        var stopped = await _packagedLifecycle.TryStopAgentAsync(AgentShutdownReason.Restart, _lifetime.Token);
        var started = stopped
            ? await _packagedLifecycle.EnsureAgentAsync(_lifetime.Token)
            : new AgentEnsureResult(AgentEnsureStatus.LaunchFailed);
        _locationStatus.Text = started.IsReady
            ? Ui.Shell.AdaptiveConcurrencyActive
            : Ui.Shell.ConcurrencyAgentRestartFailed;
    }

    private static bool ConcurrencyEquals(
        DesktopUpdatePreferences left,
        DesktopUpdatePreferences right) =>
        left.AdaptiveConcurrency == right.AdaptiveConcurrency &&
        left.MinimumConcurrency == right.MinimumConcurrency &&
        left.MaximumTransferConcurrency == right.MaximumTransferConcurrency &&
        left.PerConnectionConcurrency == right.PerConnectionConcurrency &&
        left.MaximumSyncConcurrency == right.MaximumSyncConcurrency;

    private async Task RunAutomaticUpdaterAsync()
    {
        try
        {
            await _updater.RunAutomaticAsync(_lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            // Window shutdown or an update-settings opt-out cancels background update work.
        }
    }

    private void UpdateStatusClicked(object? sender, EventArgs e) => ShowUpdateChecker();

    private void ShowUpdateChecker()
    {
        using var dialog = new UpdateCheckerForm(_updater);
        _ = dialog.ShowDialog(this);
    }

    private void UpdaterStatusChanged(object? sender, DesktopUpdateSnapshot snapshot)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (!InvokeRequired)
        {
            ApplyUpdateStatus(snapshot);
            return;
        }

        try
        {
            BeginInvoke(new Action(() =>
            {
                if (!IsDisposed && !Disposing)
                {
                    ApplyUpdateStatus(snapshot);
                }
            }));
        }
        catch (InvalidOperationException)
        {
            // The window handle can disappear between the guard and BeginInvoke during shutdown.
        }
    }

    private void UpdaterRestartRequested(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(Close));
            }
            catch (InvalidOperationException)
            {
                // The updater has already been staged; normal process shutdown will release it.
            }

            return;
        }

        Close();
    }

    private void ApplyUpdateStatus(DesktopUpdateSnapshot snapshot)
    {
        _updateStatus.Text = snapshot.Message;
        _updateStatus.AccessibleDescription = snapshot.Message;
        _updateStatus.ForeColor = snapshot.State switch
        {
            DesktopUpdateState.ReadyToRestart => StorageHubTheme.Success,
            DesktopUpdateState.Installing => StorageHubTheme.Success,
            DesktopUpdateState.UpdateAvailable => StorageHubTheme.Warning,
            DesktopUpdateState.Failed => StorageHubTheme.Danger,
            _ => StorageHubTheme.TextMuted
        };
    }

    /// <summary>
    /// Keeps the shell status bar's queue counters in step with the agent, so a running transfer
    /// is visible from any workspace tab rather than only inside the queue panel.
    /// </summary>
    private void TransferQueueCountsChanged(object? sender, TransferQueueCountsEventArgs e)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (_status.QueuedJobs != e.QueuedJobs || _status.ActiveJobs != e.ActiveJobs)
        {
            ApplyStatus(_status with { QueuedJobs = e.QueuedJobs, ActiveJobs = e.ActiveJobs });
        }
    }

    private void ApplyStatus(ShellStatusSnapshot status)
    {
        _status = status;
        _locationStatus.Text = status.Location;
        _selectionStatus.Text = status.SelectionText;
        _speedStatus.Text = status.TransferRateText;
        _queueStatus.Text = status.QueueText;
        _agentStatus.Text = status.AgentText;
        _agentStatus.ForeColor = status.AgentState switch
        {
            AgentConnectionState.Connected => StorageHubTheme.Success,
            AgentConnectionState.RecoveryOnly => StorageHubTheme.Warning,
            AgentConnectionState.Disconnected => StorageHubTheme.Danger,
            _ => StorageHubTheme.TextMuted
        };
        _overview.UpdateAgentStatus(status);
    }

    private sealed class WorkspaceTabMetadata(bool Closable)
    {
        public bool Closable { get; } = Closable;

        /// <summary>Reassigned by the theme when the appearance changes.</summary>
        public Image? Icon { get; set; }
    }

    private sealed record PaneClipboardSnapshot(
        BrowserPaneControl SourcePane,
        PaneSelectionSnapshot Selection,
        TransferQueueOperation Operation);
}
