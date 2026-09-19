using System.Globalization;
using System.Security.Cryptography;
using StorageHub.Agent;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

public sealed class SettingsForm : Form
{
    /// <summary>
    /// How wide a page's content runs. Capped rather than filled: a row puts its control against
    /// the trailing edge, and across a maximised window that edge ends up so far from the setting
    /// it belongs to that the two read as two unrelated columns.
    /// </summary>
    private const int ContentWidth = 720;
    /// <summary>
    /// Wide enough for the deepest rail entry — a provider under Storage under Connections — to
    /// show its full name beside its icon rather than an ellipsis.
    /// </summary>
    private const int NavigationWidth = 288;

    /// <summary>
    /// The two column widths at the size they are drawn. They are read from layout code that also
    /// measures text, and text is measured in device pixels, so a column left in logical units is
    /// compared against a width a quarter larger than itself on a 125% display -- which is how a
    /// description that fits on one line at 100% came to wrap onto two.
    /// </summary>
    private int ScaledContentWidth => LogicalToDeviceUnits(ContentWidth);

    private int ScaledNavigationWidth => LogicalToDeviceUnits(NavigationWidth);

    private readonly DesktopConfigStore _store;
    private readonly Action<DesktopUpdatePreferences>? _saved;
    private readonly IRemoteSecretVaultClient _secretClient;
    private readonly bool _ownsSecretClient;
    private readonly ImageList _categoryIcons;
    private readonly TreeView _categories;
    private readonly Font _categoryItemFont;
    private TreeNode? _lastCategoryNode;
    private readonly Dictionary<string, Control> _pages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Control> _connectionDefaultControls = new(StringComparer.Ordinal);
    private readonly StorageHubToggle _checkAutomatically;
    private readonly StorageHubToggle _downloadAutomatically;
    private readonly StorageHubToggle _restartAutomatically;
    private readonly StorageHubToggle _includePrereleases;
    private readonly StorageHubChoiceField _sshDiscovery;
    private readonly StorageHubChoiceField _agentMode = new();
    private readonly Label _agentModeWarning = new();
    private readonly Label _agentModeStatus = new();
    private readonly Button _agentModeApply = new StorageHubButton();
    private string? _agentModeStaleService;
    /// <summary>
    /// The row the host-key discovery mode sits in. Its description changes with the selection,
    /// which is what the separate paragraph under the old drop-down used to do.
    /// </summary>
    private SettingsRow? _sshDiscoveryRow;
    private readonly StorageHubTextField _externalEditor;
    private readonly StorageHubNumberField _maximumEditableKilobytes;
    private readonly StorageHubToggle _warnBeforeUnsafeExternalEdit;
    private readonly StorageHubToggle _adaptiveConcurrency;
    private readonly StorageHubToggle _confirmBeforeClearingTransferHistory;
    private readonly StorageHubToggle _confirmBeforeDeletingItems;
    private readonly StorageHubToggle _showFavoritesInTheirFolders;
    private readonly StorageHubNumberField _minimumConcurrency;
    private readonly StorageHubNumberField _maximumTransferConcurrency;
    private readonly StorageHubNumberField _perConnectionConcurrency;
    private readonly StorageHubNumberField _maximumSyncConcurrency;
    private readonly StorageHubChoiceField _appearance;
    private readonly StorageHubChoiceField _language;
    /// <summary>
    /// The "no preset" entry in the workspace-preset list, and the sentinel stored as that item.
    /// A property rather than a const, because the caption follows the current language.
    /// </summary>
    private static string AskEveryTime => Ui.Settings.LayoutAskEveryTime;

    private readonly StorageHubChoiceField _defaultWorkspaceLayout;
    private readonly StorageHubChoiceField _defaultWorkspacePreset;
    private bool _syncingWorkspaceControls;
    private readonly DesktopUpdatePreferences _preferences;
    private readonly StorageHubToggle _reconnectRemotePanes;
    private readonly StorageHubChoiceField _sshTerminalName;
    private readonly StorageHubTextField _sshStartupCommand;
    private readonly StorageHubNumberField _sshKeepAliveSeconds;
    private readonly StorageHubChoiceField _sshFontFamily;
    private readonly StorageHubNumberField _sshFontSize;
    private readonly StorageHubNumberField _sshScrollbackLines;
    private readonly StorageHubNumberField _sshRefreshInterval;
    private readonly StorageHubToggle _sshRenderBoldText;
    private readonly StorageHubButton _apply;
    private readonly ShortcutSettingsControl _shortcuts;
    private readonly ToolbarSettingsControl _toolbar;
    private DesktopAppearance _appliedAppearance;
    private string _appliedLanguage;

    public SettingsForm()
        : this(DesktopConfigStore.CreateDefault(), saved: null, secretClient: null)
    {
    }

    internal SettingsForm(
        DesktopConfigStore store,
        Action<DesktopUpdatePreferences>? saved,
        IRemoteSecretVaultClient? secretClient = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _saved = saved;
        _ownsSecretClient = secretClient is null;
        _secretClient = secretClient ?? new NamedPipeRemoteSecretVaultClient();

        Text = Ui.Settings.WindowTitle;
        AccessibleName = Ui.Settings.WindowAccessibleName;
        AccessibleDescription = Ui.Settings.WindowAccessibleDescription;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = this.LogicalWindowSize(new Size(1080, 720));
        Size = this.LogicalWindowSize(new Size(1160, 780));
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = StorageHubTheme.Canvas;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        StorageHubTheme.Register(this);

        // Built here rather than in a field initializer: the glyphs are rasterised for this
        // window's scaling, and a field initializer cannot see the window.
        _categoryIcons = CreateCategoryIcons(this);

        var preferences = _store.Load();
        // Held so settings this dialog does not present -- the pinned and recent workspace
        // lists, which the shell writes -- survive a save from here.
        _preferences = preferences;
        _shortcuts = new ShortcutSettingsControl(preferences.Shortcuts);
        _toolbar = new ToolbarSettingsControl(preferences.ToolbarItems, preferences.ToolbarLabels);
        _appliedAppearance = preferences.Appearance;
        _appliedLanguage = preferences.Language;
        // The caption that used to sit inside each check box now belongs to the row carrying the
        // switch, so these hold state and nothing else.
        _checkAutomatically = CreateToggle(preferences.CheckAutomatically);
        _downloadAutomatically = CreateToggle(preferences.DownloadAutomatically);
        _restartAutomatically = CreateToggle(preferences.RestartAutomatically);
        _includePrereleases = CreateToggle(preferences.IncludePrereleases);
        _externalEditor = new StorageHubTextField
        {
            Text = preferences.ExternalEditorPath ?? string.Empty,
            Width = LogicalToDeviceUnits(240),
            PlaceholderText = Ui.Settings.EditorPlaceholder,
            AccessibleName = Ui.Settings.EditorExecutableAccessibleName
        };
        _maximumEditableKilobytes = new StorageHubNumberField
        {
            Minimum = 1,
            Maximum = EditableFileIpcContract.MaximumContentBytes / 1024,
            Value = Math.Clamp(preferences.MaximumEditableFileBytes / 1024, 1, EditableFileIpcContract.MaximumContentBytes / 1024),
            Width = LogicalToDeviceUnits(150),
            Unit = Ui.Settings.UnitKibibytes,
            ThousandsSeparator = true,
            AccessibleName = Ui.Settings.MaximumEditableSizeAccessibleName
        };
        _warnBeforeUnsafeExternalEdit = CreateToggle(preferences.WarnBeforeUnsafeExternalEdit);
        _adaptiveConcurrency = CreateToggle(preferences.AdaptiveConcurrency);
        _confirmBeforeClearingTransferHistory = CreateToggle(preferences.ConfirmBeforeClearingTransferHistory);
        _confirmBeforeDeletingItems = CreateToggle(preferences.ConfirmBeforeDeletingItems);
        _showFavoritesInTheirFolders = CreateToggle(preferences.ShowFavoritesInTheirFolders);
        _minimumConcurrency = CreateConcurrencyInput(1, 8, preferences.MinimumConcurrency, Ui.Settings.StartingConcurrencyAccessibleName);
        _maximumTransferConcurrency = CreateConcurrencyInput(1, 32, preferences.MaximumTransferConcurrency, Ui.Settings.MaximumTransfersAccessibleName);
        _perConnectionConcurrency = CreateConcurrencyInput(1, 16, preferences.PerConnectionConcurrency, Ui.Settings.PerConnectionAccessibleName);
        _maximumSyncConcurrency = CreateConcurrencyInput(1, 8, preferences.MaximumSyncConcurrency, Ui.Settings.MaximumSynchronizationsAccessibleName);
        _appearance = new StorageHubChoiceField
        {
            Width = LogicalToDeviceUnits(240),
            AccessibleName = Ui.Settings.ThemeAccessibleName,
            DisplayText = static item => item switch
            {
                DesktopAppearance.Light => Ui.Settings.ThemeLight,
                DesktopAppearance.Dark => Ui.Settings.ThemeDark,
                _ => Ui.Settings.ThemeSystem
            }
        };
        _appearance.Items.Add(DesktopAppearance.Light);
        _appearance.Items.Add(DesktopAppearance.Dark);
        _appearance.Items.Add(DesktopAppearance.System);
        _appearance.SelectedItem = preferences.Appearance;
        _language = new StorageHubChoiceField
        {
            // Wider than the controls around it because a language names itself in full, and
            // "Nederlands (Nederland)" has to fit without the reader having to open the list.
            Width = LogicalToDeviceUnits(280),
            AccessibleName = Ui.Settings.LanguageAccessibleName,
            DisplayText = static item => DescribeLanguage((string)item)
        };
        _language.Items.Add(DesktopCulture.AutomaticLanguage);
        foreach (var culture in DesktopCulture.SupportedCultures)
        {
            _language.Items.Add(culture);
        }

        _language.SelectedItem = _language.Items
            .Cast<string>()
            .FirstOrDefault(item => string.Equals(item, preferences.Language, StringComparison.OrdinalIgnoreCase))
            ?? DesktopCulture.AutomaticLanguage;
        _defaultWorkspaceLayout = new StorageHubChoiceField
        {
            Width = LogicalToDeviceUnits(240),
            AccessibleName = Ui.Settings.DefaultPaneLayoutAccessibleName,
            DisplayText = static item => item switch
            {
                WorkspaceLayout.TopAndBottom => Ui.Settings.LayoutTopAndBottom,
                _ => Ui.Settings.LayoutSideBySide
            }
        };
        _defaultWorkspaceLayout.Items.Add(WorkspaceLayout.SideBySide);
        _defaultWorkspaceLayout.Items.Add(WorkspaceLayout.TopAndBottom);
        _defaultWorkspaceLayout.SelectedItem = preferences.DefaultWorkspaceLayout;
        _defaultWorkspacePreset = new StorageHubChoiceField
        {
            Width = LogicalToDeviceUnits(280),
            AccessibleName = Ui.Settings.NewWorkspaceLayoutAccessibleName,
            DisplayText = static item => item is WorkspacePreset preset ? preset.Label : AskEveryTime
        };
        // The same six arrangements the chooser offers, so neither place has options the other
        // lacks. The leading entry is "ask", which no preset can represent.
        _defaultWorkspacePreset.Items.Add(AskEveryTime);
        foreach (var workspacePreset in WorkspacePreset.All)
        {
            _defaultWorkspacePreset.Items.Add(workspacePreset);
        }

        _defaultWorkspacePreset.SelectedItem = preferences.DefaultWorkspacePaneCount is { } panes
            ? WorkspacePreset.Find(panes, preferences.DefaultWorkspaceLayout) ?? (object)AskEveryTime
            : AskEveryTime;
        // Kept in step so the two controls can never disagree about the same workspace.
        _defaultWorkspacePreset.SelectedIndexChanged += (_, _) => SyncWorkspaceControls(fromPreset: true);
        _defaultWorkspaceLayout.SelectedIndexChanged += (_, _) => SyncWorkspaceControls(fromPreset: false);
        _reconnectRemotePanes = CreateToggle(preferences.ReconnectRemotePanesAutomatically);

        var terminalPreferences = SshTerminalPreferences.Resolve(preferences.SshTerminal);
        _sshTerminalName = new StorageHubChoiceField
        {
            Editable = true,
            Width = LogicalToDeviceUnits(280),
            MaxLength = SshTerminalIpcContract.MaximumTerminalNameLength,
            Text = terminalPreferences.TerminalName,
            AccessibleName = Ui.Settings.TerminalTypeAccessibleName
        };
        foreach (var terminal in new[]
                 {
                     "xterm-256color", "xterm", "screen-256color", "tmux-256color", "linux", "vt220", "vt100"
                 })
        {
            _sshTerminalName.Items.Add(terminal);
        }

        _sshStartupCommand = new StorageHubTextField
        {
            Width = LogicalToDeviceUnits(280),
            MaxLength = SshTerminalPreferences.MaximumStartupCommandLength,
            Text = terminalPreferences.StartupCommand ?? string.Empty,
            PlaceholderText = Ui.Settings.StartupShellPlaceholder,
            AccessibleName = Ui.Settings.StartupShellAccessibleName
        };
        _sshKeepAliveSeconds = CreateProviderNumberDefault(
            terminalPreferences.KeepAliveSeconds,
            0,
            3_600);
        _sshKeepAliveSeconds.AccessibleName = Ui.Settings.KeepAliveIntervalAccessibleName;
        _sshFontFamily = new StorageHubChoiceField
        {
            Editable = true,
            Width = LogicalToDeviceUnits(280),
            MaxLength = 128,
            Text = terminalPreferences.FontFamily,
            AccessibleName = Ui.Settings.FontFamilyAccessibleName
        };
        foreach (var family in FontFamily.Families
            .Select(family => family.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            _sshFontFamily.Items.Add(family);
        }

        _sshFontSize = new StorageHubNumberField
        {
            Minimum = 6,
            Maximum = 32,
            DecimalPlaces = 1,
            Increment = 0.5M,
            Value = (decimal)terminalPreferences.FontSize,
            Width = LogicalToDeviceUnits(150),
            Unit = Ui.Settings.UnitPoints,
            AccessibleName = Ui.Settings.FontSizeAccessibleName
        };
        _sshScrollbackLines = CreateProviderNumberDefault(
            terminalPreferences.ScrollbackLines,
            100,
            20_000);
        _sshScrollbackLines.AccessibleName = Ui.Settings.ScrollbackLinesAccessibleName;
        _sshRefreshInterval = CreateProviderNumberDefault(
            terminalPreferences.RefreshIntervalMilliseconds,
            16,
            500);
        _sshRefreshInterval.AccessibleName = Ui.Settings.OutputRefreshAccessibleName;
        _sshRenderBoldText = CreateToggle(terminalPreferences.RenderBoldText);

        _sshDiscovery = new StorageHubChoiceField
        {
            Width = LogicalToDeviceUnits(280),
            AccessibleName = Ui.Settings.HostKeyDiscoveryAccessibleName,
            DisplayText = static item => ((DiscoveryChoice)item).Label
        };
        _sshDiscovery.Items.Add(new DiscoveryChoice(
            SshHostKeyDiscoveryMode.Manual,
            Ui.Settings.HostKeyManual,
            Ui.Settings.HostKeyManualHint));
        _sshDiscovery.Items.Add(new DiscoveryChoice(
            SshHostKeyDiscoveryMode.AskBeforeFetching,
            Ui.Settings.HostKeyAsk,
            Ui.Settings.HostKeyAskHint));
        _sshDiscovery.Items.Add(new DiscoveryChoice(
            SshHostKeyDiscoveryMode.Automatic,
            Ui.Settings.HostKeyAutomatic,
            Ui.Settings.HostKeyAutomaticHint));
        _sshDiscovery.SelectedItem = _sshDiscovery.Items
            .Cast<DiscoveryChoice>()
            .Single(choice => choice.Mode == preferences.SshHostKeyDiscovery);

        _categories = new TreeView
        {
            Dock = DockStyle.Fill,
            ItemHeight = this.TextBoxHeight(10),
            Indent = 18,
            FullRowSelect = true,
            HideSelection = false,
            // The rail is drawn end to end here. The dialog opens with focus on the page, and an
            // unfocused native selection is a faint outline that leaves no clear indication of
            // which category you are looking at.
            DrawMode = TreeViewDrawMode.OwnerDrawAll,
            ShowLines = false,
            ShowPlusMinus = true,
            ShowRootLines = false,
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point),
            BackColor = StorageHubTheme.SurfaceMuted,
            ImageList = _categoryIcons,
            AccessibleName = Ui.Settings.CategoriesAccessibleName
        };
        var work = CategoryNode(Ui.Settings.CategoryTransfersAndSync, "Performance", UiGlyph.Speed);
        _categories.Nodes.Add(work);
        _categories.Nodes.Add(CategoryNode(Ui.Settings.CategoryEditing, "Editing", UiGlyph.Rename));
        _categories.Nodes.Add(CategoryNode(Ui.Settings.CategoryAppearance, "Appearance", UiGlyph.Theme));
        _categories.Nodes.Add(CategoryNode(Ui.Settings.CategoryWorkspace, "Workspace", UiGlyph.Layers));
        _categories.Nodes.Add(CategoryNode(Ui.Settings.CategoryShortcuts, "Shortcuts", UiGlyph.Keyboard));
        var connections = CategoryNode(Ui.Settings.CategoryConnectionsAndTrust, "Connections & trust", UiGlyph.Shield);
        foreach (var type in new[] { ConnectionProfileType.Storage, ConnectionProfileType.Client })
        {
            var typeName = type == ConnectionProfileType.Storage ? "Storage" : "Clients";
            var typeNode = CategoryNode(
                typeName,
                ConnectionTypePageKey(type),
                type == ConnectionProfileType.Storage ? UiGlyph.Server : UiGlyph.Terminal);
            foreach (var provider in ConnectionProviderCatalog.All.Where(provider => provider.Type == type))
            {
                typeNode.Nodes.Add(CategoryNode(
                    provider.DisplayName,
                    ProviderPageKey(provider.Kind),
                    ProviderGlyph(provider.Kind)));
            }
            typeNode.Expand();
            connections.Nodes.Add(typeNode);
        }
        connections.Expand();
        _categories.Nodes.Add(connections);
        _categories.Nodes.Add(CategoryNode(
            Ui.Settings.CategoryToolbar, "Toolbar", UiGlyph.Layers));
        _categories.Nodes.Add(CategoryNode(
            Ui.Settings.CategoryAgent, "Background agent", UiGlyph.Server));
        _categories.Nodes.Add(CategoryNode(Ui.Settings.CategoryUpdates, "Updates", UiGlyph.Download));
        _categoryItemFont = new Font(_categories.Font, FontStyle.Regular);
        foreach (TreeNode rootNode in _categories.Nodes)
        {
            ApplyCategoryItemFont(rootNode.Nodes, _categoryItemFont);
        }

        var pageHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = StorageHubTheme.Surface,
            Padding = this.LogicalToDeviceUnits(new Padding(32, 26, 32, 20))
        };
        AddPage(pageHost, "Performance", BuildPerformancePage());
        AddPage(pageHost, "Editing", BuildEditingPage());
        AddPage(pageHost, "Appearance", BuildAppearancePage());
        AddPage(pageHost, "Workspace", BuildWorkspacePage());
        var shortcutPage = CreatePage(Ui.Settings.CategoryShortcuts, Ui.Settings.PageShortcutsDescription);
        shortcutPage.Controls.Add(_shortcuts);
        AddPage(pageHost, "Shortcuts", shortcutPage);
        _shortcuts.Changed += MarkDirty;
        AddPage(pageHost, "Connections & trust", BuildConnectionsPage());
        // Storage and Clients are captions in the rail rather than destinations, so the
        // pages that used to restate each provider's one-line summary are gone: the rows
        // under the caption are the list, and each provider's own page holds the detail.
        foreach (var provider in ConnectionProviderCatalog.All)
        {
            AddPage(pageHost, ProviderPageKey(provider.Kind), BuildProviderSettingsPage(provider, preferences));
        }
        AddPage(pageHost, "Toolbar", BuildToolbarPage());
        AddPage(pageHost, "Background agent", BuildAgentPage());
        AddPage(pageHost, "Updates", BuildUpdatesPage());

        var navigation = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = StorageHubTheme.SurfaceMuted,
            Padding = this.LogicalToDeviceUnits(new Padding(16, 22, 12, 14))
        };
        var navigationTitle = UiControlFactory.CreateSectionTitle(Ui.Settings.NavigationTitle);
        navigationTitle.Dock = DockStyle.Top;
        navigationTitle.Height = this.TextBoxHeight(navigationTitle.Font, 16);
        navigation.Controls.Add(_categories);
        navigation.Controls.Add(navigationTitle);

        var navigationWidth = MeasureNavigationWidth();

        // The split and the window both have to make room for the measured navigation, or
        // Panel2MinSize simply clamps the splitter back and the widest label truncates anyway.
        // Both grow by exactly what the navigation gained, so English is unchanged.
        var extraNavigation = navigationWidth - ScaledNavigationWidth;
        MinimumSize = new Size(MinimumSize.Width + extraNavigation, MinimumSize.Height);
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Size = new Size(LogicalToDeviceUnits(1060) + extraNavigation, LogicalToDeviceUnits(650)),
            FixedPanel = FixedPanel.Panel1,
            SplitterDistance = navigationWidth,
            Panel1MinSize = navigationWidth,
            Panel2MinSize = ScaledContentWidth + LogicalToDeviceUnits(64),
            IsSplitterFixed = true,
            BackColor = StorageHubTheme.Border
        };
        split.Panel1.Controls.Add(navigation);
        split.Panel2.Controls.Add(pageHost);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = this.TextBoxHeight(35),
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = this.LogicalToDeviceUnits(new Padding(16, 12, 16, 10)),
            BackColor = StorageHubTheme.Surface
        };
        var ok = new StorageHubButton { Text = Ui.Dialogs.ButtonOk };
        ok.Variant = StorageHubButtonVariant.Primary;
        ok.Click += SaveAndClose;
        var cancel = new StorageHubButton { Text = Ui.Dialogs.ButtonCancel, DialogResult = DialogResult.Cancel };
        cancel.Variant = StorageHubButtonVariant.Secondary;
        _apply = new StorageHubButton { Text = Ui.Dialogs.ButtonApply, Enabled = false };
        _apply.Variant = StorageHubButtonVariant.Secondary;
        _apply.Click += SaveWithoutClosing;
        footer.Controls.Add(ok);
        footer.Controls.Add(cancel);
        footer.Controls.Add(_apply);

        Controls.Add(split);
        Controls.Add(footer);
        AcceptButton = ok;
        CancelButton = cancel;

        _categories.AfterSelect += CategorySelected;
        _categories.DrawNode += DrawCategoryNode;
        _checkAutomatically.CheckedChanged += UpdateDependencies;
        _downloadAutomatically.CheckedChanged += UpdateDependencies;
        _sshDiscovery.SelectedIndexChanged += DiscoverySelectionChanged;
        _externalEditor.TextChanged += MarkDirty;
        _maximumEditableKilobytes.ValueChanged += MarkDirty;
        _warnBeforeUnsafeExternalEdit.CheckedChanged += MarkDirty;
        _adaptiveConcurrency.CheckedChanged += ConcurrencyChanged;
        _confirmBeforeClearingTransferHistory.CheckedChanged += MarkDirty;
        _confirmBeforeDeletingItems.CheckedChanged += MarkDirty;
        _showFavoritesInTheirFolders.CheckedChanged += MarkDirty;
        _minimumConcurrency.ValueChanged += ConcurrencyChanged;
        _maximumTransferConcurrency.ValueChanged += ConcurrencyChanged;
        _perConnectionConcurrency.ValueChanged += ConcurrencyChanged;
        _maximumSyncConcurrency.ValueChanged += ConcurrencyChanged;
        _appearance.SelectedIndexChanged += AppearanceSelectionChanged;
        _language.SelectedIndexChanged += MarkDirty;
        _defaultWorkspaceLayout.SelectedIndexChanged += MarkDirty;
        _defaultWorkspacePreset.SelectedIndexChanged += MarkDirty;
        _reconnectRemotePanes.CheckedChanged += MarkDirty;
        _sshTerminalName.TextChanged += MarkDirty;
        _sshStartupCommand.TextChanged += MarkDirty;
        _sshFontFamily.TextChanged += MarkDirty;
        _sshFontSize.ValueChanged += MarkDirty;
        _sshRenderBoldText.CheckedChanged += MarkDirty;
        foreach (var option in UpdateOptions())
        {
            option.CheckedChanged += MarkDirty;
        }

        _categories.SelectedNode = work;
        UpdateDependencies(this, EventArgs.Empty);
        StorageHubTheme.Apply(this);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (DialogResult != DialogResult.OK)
        {
            DesktopAppearanceService.SetAppearance(_appliedAppearance);
        }

        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _categories.AfterSelect -= CategorySelected;
            _categories.DrawNode -= DrawCategoryNode;
            _checkAutomatically.CheckedChanged -= UpdateDependencies;
            _downloadAutomatically.CheckedChanged -= UpdateDependencies;
            _sshDiscovery.SelectedIndexChanged -= DiscoverySelectionChanged;
            _externalEditor.TextChanged -= MarkDirty;
            _maximumEditableKilobytes.ValueChanged -= MarkDirty;
            _warnBeforeUnsafeExternalEdit.CheckedChanged -= MarkDirty;
            _adaptiveConcurrency.CheckedChanged -= ConcurrencyChanged;
            _confirmBeforeClearingTransferHistory.CheckedChanged -= MarkDirty;
            _confirmBeforeDeletingItems.CheckedChanged -= MarkDirty;
            _showFavoritesInTheirFolders.CheckedChanged -= MarkDirty;
            _minimumConcurrency.ValueChanged -= ConcurrencyChanged;
            _maximumTransferConcurrency.ValueChanged -= ConcurrencyChanged;
            _perConnectionConcurrency.ValueChanged -= ConcurrencyChanged;
            _maximumSyncConcurrency.ValueChanged -= ConcurrencyChanged;
            _appearance.SelectedIndexChanged -= AppearanceSelectionChanged;
            _defaultWorkspaceLayout.SelectedIndexChanged -= MarkDirty;
            _defaultWorkspacePreset.SelectedIndexChanged -= MarkDirty;
            _reconnectRemotePanes.CheckedChanged -= MarkDirty;
            _sshTerminalName.TextChanged -= MarkDirty;
            _sshStartupCommand.TextChanged -= MarkDirty;
            _sshFontFamily.TextChanged -= MarkDirty;
            _sshFontSize.ValueChanged -= MarkDirty;
            _sshRenderBoldText.CheckedChanged -= MarkDirty;
            foreach (var option in UpdateOptions())
            {
                option.CheckedChanged -= MarkDirty;
            }

            if (_ownsSecretClient)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _secretClient.DisposeAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                });
            }

            _categories.Font.Dispose();
            _categoryIcons.Dispose();
            _categoryItemFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private SettingsPagePanel BuildPerformancePage()
    {
        var page = CreatePage(
            Ui.Settings.CategoryTransfersAndSync,
            Ui.Settings.PagePerformanceDescription);

        var concurrency = new SettingsCard();
        concurrency.Add(Ui.Settings.AdaptiveConcurrency, Ui.Settings.AdaptiveConcurrencyHint, _adaptiveConcurrency);
        concurrency.Add(Ui.Settings.StartWith, Ui.Settings.MinimumConcurrencyHint, _minimumConcurrency);
        concurrency.Add(Ui.Settings.MaximumTransfers, Ui.Settings.MaximumTransfersHint, _maximumTransferConcurrency);
        concurrency.Add(Ui.Settings.PerConnection, Ui.Settings.PerConnectionHint, _perConnectionConcurrency);
        concurrency.Add(
            Ui.Settings.MaximumSynchronizations,
            Ui.Settings.MaximumSynchronizationsHint,
            _maximumSyncConcurrency);
        AddSection(page, Ui.Settings.SectionConcurrency, concurrency);

        var confirmations = new SettingsCard();
        confirmations.Add(
            Ui.Settings.WarnClearingHistory,
            Ui.Settings.WarnClearingHistoryHint,
            _confirmBeforeClearingTransferHistory);
        confirmations.Add(
            Ui.Settings.WarnDeletingItems,
            Ui.Settings.WarnDeletingItemsHint,
            _confirmBeforeDeletingItems);
        AddSection(page, Ui.Settings.SectionConfirmations, confirmations);
        return page;
    }

    /// <summary>
    /// Puts a card on a page under its caption, at the page's content width.
    /// </summary>
    /// <remarks>
    /// The height is assigned rather than left to auto-sizing: a card measures itself from rows
    /// whose own height depends on how their text wraps at this width, which auto-sizing resolves
    /// one layout pass too late and leaves the last row clipped.
    /// </remarks>
    private void AddSection(FlowLayoutPanel page, string? caption, Control card)
    {
        if (caption is not null)
        {
            page.Controls.Add(new SettingsCaption(caption)
            {
                Width = ScaledContentWidth,
                Margin = new Padding(0, page.Controls.Count > 2 ? 18 : 4, 0, 6)
            });
        }

        card.Width = ScaledContentWidth;
        card.MinimumSize = new Size(0, 0);
        card.Margin = this.LogicalToDeviceUnits(new Padding(0, 0, 0, 2));
        card.Height = card.GetPreferredSize(new Size(ScaledContentWidth, 0)).Height;
        page.Controls.Add(card);
    }

    private SettingsPagePanel BuildConnectionsPage()
    {
        var page = CreatePage(
            Ui.Settings.CategoryConnectionsAndTrust,
            Ui.Settings.PageTrustDescription);
        var card = new SettingsCard();
        _sshDiscoveryRow = card.Add(Ui.Settings.HostKeyDiscovery, null, _sshDiscovery);
        UpdateDiscoveryDescription();
        AddSection(page, Ui.Settings.HostKeyDiscovery, card);
        page.Controls.Add(CreateSecurityNotice());
        return page;
    }

    private SettingsPagePanel BuildEditingPage()
    {
        var page = CreatePage(
            Ui.Settings.PageExternalEditing,
            Ui.Settings.PageEditingDescription);
        var browse = new StorageHubButton
        {
            Text = Ui.Settings.ButtonBrowse,
            Variant = StorageHubButtonVariant.Secondary
        };
        browse.Click += BrowseEditorClicked;

        var card = new SettingsCard();
        card.Add(Ui.Settings.EditorExecutable, Ui.Settings.EditorHint, _externalEditor, browse);
        card.Add(
            Ui.Settings.MaximumEditableSize,
            Ui.Settings.MaximumEditableSizeHint,
            _maximumEditableKilobytes).DescriptionIsWarning = true;
        card.Add(Ui.Settings.WarnUnsafeEdit, Ui.Settings.WarnUnsafeEditHint, _warnBeforeUnsafeExternalEdit);
        AddSection(page, Ui.Settings.PageExternalEditing, card);
        return page;
    }

    private void BrowseEditorClicked(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = Ui.Settings.ChooseEditorTitle,
            Filter = Ui.Settings.ExecutableFilter,
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _externalEditor.Text = dialog.FileName;
        }
    }

    /// <summary>
    /// The toolbar page. Unlike the agent page below it, this is an ordinary preference: it applies
    /// with the dialog's Apply, and the shell rebuilds its toolbar from it.
    /// </summary>
    private SettingsPagePanel BuildToolbarPage()
    {
        var page = CreatePage(Ui.Settings.CategoryToolbar, Ui.Settings.PageToolbarDescription);
        _toolbar.Width = ScaledContentWidth;
        _toolbar.Margin = this.LogicalToDeviceUnits(new Padding(0, 6, 0, 0));
        _toolbar.Changed += MarkDirty;
        page.Controls.Add(_toolbar);
        return page;
    }

    /// <summary>
    /// The background agent page. It applies on its own button rather than through the dialog's
    /// Apply, because switching modes needs an elevated helper and copies secrets: that is a
    /// deliberate action with its own confirmation, not a preference saved alongside the rest.
    /// </summary>
    private SettingsPagePanel BuildAgentPage()
    {
        var page = CreatePage(Ui.Settings.CategoryAgent, Ui.Settings.PageAgentDescription);
        _agentMode.Name = "AgentHostMode";
        _agentMode.Width = LogicalToDeviceUnits(280);
        _agentMode.AccessibleName = Ui.Settings.AgentModeLabel;
        // Order matches AgentModeChoices below; index is the only thing binding them.
        _agentMode.Items.Add(Ui.Settings.AgentModeUserSession);
        _agentMode.Items.Add(Ui.Settings.AgentModeAppSession);
        _agentMode.Items.Add(Ui.Settings.AgentModeService);
        _agentMode.SelectedIndexChanged += (_, _) => UpdateAgentModeControls();

        var card = new SettingsCard();
        card.Add(Ui.Settings.AgentModeLabel, Ui.Settings.AgentModeHint, _agentMode);
        AddSection(page, Ui.Settings.CategoryAgent, card);

        _agentModeWarning.AutoSize = true;
        _agentModeWarning.MaximumSize = new Size(ScaledContentWidth, 0);
        _agentModeWarning.ForeColor = StorageHubTheme.Warning;
        _agentModeWarning.Text = Ui.Settings.AgentModeServiceWarning;
        _agentModeWarning.Margin = this.LogicalToDeviceUnits(new Padding(0, 12, 0, 0));
        page.Controls.Add(_agentModeWarning);

        _agentModeApply.Name = "ApplyAgentHostMode";
        _agentModeApply.Text = Ui.Settings.AgentModeApply;
        _agentModeApply.Margin = this.LogicalToDeviceUnits(new Padding(0, 12, 0, 0));
        _agentModeApply.Click += async (_, _) => await ApplyAgentModeAsync().ConfigureAwait(true);
        page.Controls.Add(_agentModeApply);

        _agentModeStatus.AutoSize = true;
        _agentModeStatus.MaximumSize = new Size(ScaledContentWidth, 0);
        _agentModeStatus.ForeColor = StorageHubTheme.TextMuted;
        _agentModeStatus.Margin = this.LogicalToDeviceUnits(new Padding(0, 10, 0, 0));
        page.Controls.Add(_agentModeStatus);

        RefreshAgentMode();
        return page;
    }

    /// <summary>Reads the live service state, so the page always reflects reality.</summary>
    private void RefreshAgentMode()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var status = AgentHostModeController.Describe(DesktopAgentHost.Mode);
        _agentMode.SelectedIndex = Array.IndexOf(AgentModeChoices, DesktopAgentHost.Mode);
        _agentModeStatus.Text = status.ServiceInstalled && !status.ServiceRunning
            ? Ui.Format(Ui.Settings.AgentModeMismatchFormat, Ui.Settings.AgentModeService)
            : string.Empty;
        _agentModeStaleService = AgentHostModeController.DescribeStaleService();
        if (_agentModeStaleService is { } stale)
        {
            // Updating the app leaves the service on the previous build: re-staging needs
            // elevation, so it cannot happen silently behind an update hook.
            _agentModeStatus.Text = stale;
            _agentModeStatus.ForeColor = StorageHubTheme.Warning;
        }

        UpdateAgentModeControls();
    }

    private static readonly AgentHostMode[] AgentModeChoices =
    [
        AgentHostMode.UserSession,
        AgentHostMode.AppSession,
        AgentHostMode.WindowsService
    ];

    private AgentHostMode SelectedAgentMode => _agentMode.SelectedIndex >= 0
        ? AgentModeChoices[_agentMode.SelectedIndex]
        : AgentHostMode.UserSession;

    private void UpdateAgentModeControls()
    {
        var wantsService = SelectedAgentMode == AgentHostMode.WindowsService;
        _agentModeWarning.Visible = wantsService;
        if (!OperatingSystem.IsWindows())
        {
            _agentModeApply.Enabled = false;
            return;
        }

        // Also offer Apply when the mode already matches but the service is running an older
        // build, which is the only way to refresh it.
        _agentModeApply.Enabled = SelectedAgentMode != DesktopAgentHost.Mode ||
            (wantsService && _agentModeStaleService is not null);
    }

    private async Task ApplyAgentModeAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var desired = SelectedAgentMode;
        _agentModeApply.Enabled = false;
        _agentModeStatus.ForeColor = StorageHubTheme.TextMuted;
        _agentModeStatus.Text = Ui.Settings.AgentModeApplying;
        try
        {
            var controller = new AgentHostModeController(PackagedDesktopLifecycle.CreateDefault().AgentExecutablePath);
            var result = await controller.ApplyAsync(desired).ConfigureAwait(true);
            _agentModeStatus.Text = result.Summary;
            _agentModeStatus.ForeColor = result.Succeeded
                ? StorageHubTheme.Success
                : StorageHubTheme.Danger;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _agentModeStatus.Text = error.Message;
            _agentModeStatus.ForeColor = StorageHubTheme.Danger;
        }
        finally
        {
            RefreshAgentMode();
        }
    }

    private SettingsPagePanel BuildUpdatesPage()
    {
        var page = CreatePage(
            Ui.Settings.CategoryUpdates,
            Ui.Settings.PageUpdatesDescription);
        var card = new SettingsCard();
        card.Add(Ui.Settings.CheckAutomatically, Ui.Settings.CheckAutomaticallyHint, _checkAutomatically);
        card.Add(Ui.Settings.DownloadAutomatically, Ui.Settings.DownloadAutomaticallyHint, _downloadAutomatically);
        card.Add(Ui.Settings.RestartAutomatically, Ui.Settings.RestartAutomaticallyHint, _restartAutomatically);
        card.Add(Ui.Settings.IncludePrereleases, Ui.Settings.IncludePrereleasesHint, _includePrereleases);
        AddSection(page, Ui.Settings.CategoryUpdates, card);
        page.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(ScaledContentWidth, 0),
            Text = Ui.Format(
                Ui.Settings.UpdateSourceFormat,
                VelopackDesktopUpdateEngineFactory.TrustedRepositoryUrl,
                DesktopApplicationVersion.Current),
            ForeColor = StorageHubTheme.TextMuted,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 14, 0, 0)),
            AccessibleName = Ui.Settings.UpdateSourceAccessibleName
        });
        return page;
    }

    private SettingsPagePanel CreatePage(string title, string description)
    {
        var page = new SettingsPagePanel
        {
            Dock = DockStyle.Fill,
            BackColor = StorageHubTheme.Surface
        };
        var heading = UiControlFactory.CreateSectionTitle(title);
        // A page title is a sentence, not a mnemonic: left as WinForms defaults, "Transfers &
        // sync" is painted as "Transfers  sync" with the ampersand swallowed and no underline to
        // show for it.
        heading.UseMnemonic = false;
        heading.Width = ScaledContentWidth;
        heading.MinimumSize = new Size(ScaledContentWidth, 0);
        heading.Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold, GraphicsUnit.Point);
        heading.Height = this.TextBoxHeight(heading.Font, 4);
        var summary = UiControlFactory.CreateDescription(description);
        summary.UseMnemonic = false;
        summary.Width = ScaledContentWidth;
        summary.MinimumSize = new Size(ScaledContentWidth, 0);
        summary.MaximumSize = new Size(ScaledContentWidth, 0);
        summary.Padding = this.LogicalToDeviceUnits(new Padding(0, 0, 0, 14));
        page.Controls.Add(heading);
        page.Controls.Add(summary);
        page.ClientSizeChanged += FitSettingsPageContent;
        return page;
    }

    private void FitSettingsPageContent(object? sender, EventArgs e)
    {
        if (sender is FlowLayoutPanel page)
        {
            FitSettingsPageContent(page);
        }
    }

    private void FitSettingsPageContent(FlowLayoutPanel page)
    {
        if (page.Tag is true || !page.Visible || !page.IsHandleCreated ||
            page.ClientSize.Width <= SystemInformation.VerticalScrollBarWidth + 1)
        {
            return;
        }

        page.Tag = true;
        page.SuspendLayout();
        try
        {
            // The scrollbar's width is always reserved, never measured. Measuring it produced a
            // page that fitted exactly until its content grew tall enough to need the scrollbar,
            // at which point the width it took away pushed the content into a horizontal scrollbar
            // as well.
            var availableWidth = Math.Max(
                1,
                page.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 1);
            foreach (Control control in page.Controls)
            {
                if (control is Button or CheckBox or StorageHubToggle)
                {
                    continue;
                }

                if (control is Label label)
                {
                    var maximum = Math.Min(availableWidth, ScaledContentWidth);
                    if (label.MaximumSize.Width > 0 && label.MaximumSize.Width != maximum)
                    {
                        label.MaximumSize = new Size(maximum, label.MaximumSize.Height);
                    }

                    // An auto-sizing label still honours its minimum, so a heading authored at the
                    // 700px content width keeps the page scrolling sideways on a narrower display
                    // however the maximum above is clamped. Labels are laid out by their maximum
                    // and their text, so dropping the minimum costs nothing -- unlike the panels
                    // below, which use it to fill a page wider than their content.
                    if (label.MinimumSize.Width > availableWidth)
                    {
                        label.MinimumSize = new Size(0, label.MinimumSize.Height);
                    }

                    if (!label.AutoSize && label.Width != availableWidth)
                    {
                        label.MinimumSize = new Size(0, label.MinimumSize.Height);
                        label.Width = availableWidth;
                    }
                    continue;
                }

                // Capped at the content width rather than stretched to fill: a row puts its
                // control against its trailing edge, and on a wide window that edge would end up
                // an inch of empty space away from the setting it belongs to.
                var width = Math.Min(availableWidth, ScaledContentWidth);
                if (!control.AutoSize && control.Width != width)
                {
                    control.MinimumSize = new Size(0, control.MinimumSize.Height);
                    control.Width = width;
                }
            }
        }
        finally
        {
            page.ResumeLayout(false);
            page.Tag = null;
        }

        if (page is SettingsPagePanel settings)
        {
            settings.ResetScrollState();
        }
    }

    /// <summary>
    /// Builds the image list the category tree indexes into. Tree nodes address images by key, so
    /// each glyph is rasterised once here in the muted text colour that suits a navigation rail.
    /// </summary>
    private static ImageList CreateCategoryIcons(Control owner)
    {
        var images = new ImageList { ImageSize = owner.LogicalToDeviceUnits(new Size(18, 18)), ColorDepth = ColorDepth.Depth32Bit };
        foreach (var glyph in new[]
                 {
                     UiGlyph.Speed, UiGlyph.Rename, UiGlyph.Theme, UiGlyph.Layers, UiGlyph.Keyboard,
                     UiGlyph.Shield, UiGlyph.Server, UiGlyph.Terminal, UiGlyph.Download,
                     UiGlyph.Folder, UiGlyph.Cloud, UiGlyph.Link, UiGlyph.Lock, UiGlyph.Key
                 })
        {
            images.Images.Add(
                glyph.ToString(),
                UiIconFactory.Create(glyph, StorageHubTheme.TextMuted, 18, owner.DeviceDpi / 96F));
        }

        return images;
    }

    /// <summary>
    /// The width the category list actually needs, measured from the widest row.
    /// </summary>
    /// <remarks>
    /// The fixed 288 pixels this replaces was sized against the English labels, so
    /// "Verbindungen &amp; Vertrauen" arrived truncated in German. The metrics here mirror
    /// <see cref="DrawCategoryNode"/> exactly -- chevron slot, icon, gap -- because that is what
    /// decides where the text actually starts. The old constant survives as the floor, so no
    /// language makes the list narrower than it has always been.
    /// </remarks>
    private int MeasureNavigationWidth()
    {
        const int MaximumNavigationWidth = 420;
        var widest = 0;
        foreach (var node in AllCategoryNodes(_categories.Nodes))
        {
            // Every row's text starts at the same place now, so the widest row is simply the
            // longest name.
            var indent = LogicalToDeviceUnits(IsCategoryCaption(node) ? CaptionIndent : TextIndent);

            var text = TextRenderer.MeasureText(
                node.Text,
                node.NodeFont ?? _categories.Font).Width;
            widest = Math.Max(widest, indent + text);
        }

        // Room for the list's own padding, its scrollbar, and the panel's right inset.
        widest += LogicalToDeviceUnits(16) + SystemInformation.VerticalScrollBarWidth + LogicalToDeviceUnits(12);
        return Math.Clamp(widest, ScaledNavigationWidth, LogicalToDeviceUnits(MaximumNavigationWidth));
    }

    private static IEnumerable<TreeNode> AllCategoryNodes(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            yield return node;
            foreach (var child in AllCategoryNodes(node.Nodes))
            {
                yield return child;
            }
        }
    }

    private static TreeNode CategoryNode(string text, string key, UiGlyph glyph) => new(text)
    {
        Name = key,
        ImageKey = glyph.ToString(),
        SelectedImageKey = glyph.ToString()
    };

    private static UiGlyph ProviderGlyph(StorageProviderKind provider) => provider switch
    {
        StorageProviderKind.Local => UiGlyph.Folder,
        StorageProviderKind.S3 => UiGlyph.Cloud,
        StorageProviderKind.Ftp => UiGlyph.Link,
        StorageProviderKind.Ftps => UiGlyph.Lock,
        StorageProviderKind.Sftp => UiGlyph.Lock,
        StorageProviderKind.Ssh => UiGlyph.Key,
        _ => UiGlyph.Server
    };

    private void DrawCategoryNode(object? sender, DrawTreeNodeEventArgs e)
    {
        if (e.Node is not { } node || e.Bounds.Height <= 0)
        {
            return;
        }

        e.DrawDefault = false;
        var graphics = e.Graphics;
        // Every inset this method draws from is a logical unit. The rail is painted rather than
        // laid out, so nothing here is scaled for it by the framework.
        var captionIndent = LogicalToDeviceUnits(CaptionIndent);
        var textIndent = LogicalToDeviceUnits(TextIndent);
        var trailingInset = LogicalToDeviceUnits(6);
        var row = new Rectangle(0, e.Bounds.Top, _categories.ClientSize.Width, e.Bounds.Height);
        using (var background = new SolidBrush(_categories.BackColor))
        {
            graphics.FillRectangle(background, row);
        }

        if (IsCategoryCaption(node))
        {
            using var captionFont = new Font("Segoe UI Semibold", 8F, FontStyle.Bold, GraphicsUnit.Point);
            TextRenderer.DrawText(
                graphics,
                node.Text.ToUpperInvariant(),
                captionFont,
                Rectangle.FromLTRB(captionIndent, row.Top, row.Right - trailingInset, row.Bottom),
                StorageHubTheme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
            return;
        }

        var selected = ReferenceEquals(node, _categories.SelectedNode);
        if (selected)
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var pill = new Rectangle(
                row.Left + LogicalToDeviceUnits(4),
                row.Top + LogicalToDeviceUnits(1),
                Math.Max(1, row.Width - LogicalToDeviceUnits(10)),
                row.Height - LogicalToDeviceUnits(3));
            using (var fill = new SolidBrush(StorageHubTheme.Selection))
            using (var shape = UiShapes.RoundedRectangle(pill, LogicalToDeviceUnits(5)))
            {
                graphics.FillPath(fill, shape);
            }

            using var accent = new SolidBrush(StorageHubTheme.Primary);
            graphics.FillRectangle(
                accent,
                pill.Left,
                pill.Top + LogicalToDeviceUnits(4),
                LogicalToDeviceUnits(3),
                pill.Height - LogicalToDeviceUnits(8));
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
        }

        if (node.Parent is not null)
        {
            // A guide line instead of an icon. The leaves under a group are all of one kind, so
            // an icon each said nothing the group had not already said, while pushing their names
            // into a column of their own.
            using var guide = new Pen(StorageHubTheme.Border);
            graphics.DrawLine(guide, captionIndent, row.Top, captionIndent, row.Bottom);
        }
        else if (node.Nodes.Count > 0)
        {
            DrawCategoryChevron(
                graphics,
                new Rectangle(
                    LogicalToDeviceUnits(ChevronIndent), row.Top, LogicalToDeviceUnits(ChevronWidth), row.Height),
                node.IsExpanded);
        }

        if (node.Parent is null && _categoryIcons.Images.IndexOfKey(node.ImageKey) is >= 0 and var index)
        {
            var image = _categoryIcons.Images[index];
            graphics.DrawImage(
                image,
                LogicalToDeviceUnits(IconIndent),
                row.Top + ((row.Height - image.Height) / 2),
                image.Width,
                image.Height);
        }

        TextRenderer.DrawText(
            graphics,
            node.Text,
            node.NodeFont ?? _categories.Font,
            Rectangle.FromLTRB(textIndent, row.Top, row.Right - trailingInset, row.Bottom),
            selected ? StorageHubTheme.Text : StorageHubTheme.TextMuted,
            // NoPrefix: category names such as "Transfers & sync" are labels, not mnemonics.
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);
    }

    /// <summary>
    /// Where a rail row's parts sit, measured from the left edge.
    /// </summary>
    /// <remarks>
    /// One icon column, not four. The chevron used to share the icon's slot and each level shifted
    /// everything right, so the rail ended up with an icon column per depth and no vertical line
    /// for the eye to follow. The chevron now has a gutter of its own that every row reserves,
    /// which is what lets icons, captions and leaf names each keep a single column.
    /// </remarks>
    private const int ChevronIndent = 6;
    private const int ChevronWidth = 14;
    private const int IconIndent = 26;
    private const int CaptionIndent = 46;
    private const int TextIndent = 58;

    /// <summary>
    /// A group that exists only to hold the rows under it -- Storage, Clients. It is drawn as a
    /// caption rather than as a row, because it names a part of the list rather than a page.
    /// </summary>
    private static bool IsCategoryCaption(TreeNode node) =>
        node.Parent is not null && node.Nodes.Count > 0;

    private static void DrawCategoryChevron(Graphics graphics, Rectangle bounds, bool expanded)
    {
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Pen(StorageHubTheme.TextMuted, 1.4F);
        var centerX = bounds.Left + (bounds.Width / 2F);
        var centerY = bounds.Top + (bounds.Height / 2F);
        if (expanded)
        {
            graphics.DrawLines(pen,
            [
                new PointF(centerX - 3.5F, centerY - 1.5F),
                new PointF(centerX, centerY + 2F),
                new PointF(centerX + 3.5F, centerY - 1.5F)
            ]);
        }
        else
        {
            graphics.DrawLines(pen,
            [
                new PointF(centerX - 1.5F, centerY - 3.5F),
                new PointF(centerX + 2F, centerY),
                new PointF(centerX - 1.5F, centerY + 3.5F)
            ]);
        }

        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
    }

    private static void ApplyCategoryItemFont(TreeNodeCollection nodes, Font itemFont)
    {
        foreach (TreeNode node in nodes)
        {
            node.NodeFont = itemFont;
            ApplyCategoryItemFont(node.Nodes, itemFont);
        }
    }

    /// <summary>
    /// A card that states something rather than changing it: one heading and a paragraph, on the
    /// same surface as the cards that carry settings.
    /// </summary>
    private SettingsCard CreateInformationCard(string title, string text)
    {
        var card = new SettingsCard
        {
            Name = "InformationCard",
            Width = ScaledContentWidth,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 4, 0, 12)),
            AccessibleName = Ui.Format(Ui.Settings.SettingsCardFormat, title)
        };
        card.Add(title, text, null);
        card.Height = card.GetPreferredSize(new Size(ScaledContentWidth, 0)).Height;
        return card;
    }

    /// <summary>
    /// The standing caveat about host keys: a card of one titleless row, in the warning colour.
    /// A card rather than a tinted panel so the page can size it like everything else on it.
    /// </summary>
    private SettingsCard CreateSecurityNotice()
    {
        var notice = new SettingsCard
        {
            Name = "SecurityNotice",
            Width = ScaledContentWidth,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 14, 0, 0)),
            AccessibleName = Ui.Settings.HostKeyCaveat
        };
        notice.Add(string.Empty, Ui.Settings.HostKeyCaveat, null).DescriptionIsWarning = true;
        notice.Height = notice.GetPreferredSize(new Size(ScaledContentWidth, 0)).Height;
        return notice;
    }

    private void AddPage(Control host, string name, Control page)
    {
        page.Visible = false;
        _pages.Add(name, page);
        host.Controls.Add(page);
    }

    private IEnumerable<StorageHubToggle> UpdateOptions()
    {
        yield return _checkAutomatically;
        yield return _downloadAutomatically;
        yield return _restartAutomatically;
        yield return _includePrereleases;
    }

    private static StorageHubToggle CreateToggle(bool isChecked) => new() { Checked = isChecked };

    private StorageHubNumberField CreateConcurrencyInput(
        int minimum,
        int maximum,
        int value,
        string accessibleName) =>
        new()
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = Math.Clamp(value, minimum, maximum),
            Width = LogicalToDeviceUnits(132),
            Unit = Ui.Settings.UnitJobs,
            AccessibleName = accessibleName
        };

    private SettingsPagePanel BuildAppearancePage()
    {
        var page = CreatePage(Ui.Settings.CategoryAppearance, Ui.Settings.PageAppearanceDescription);
        var theme = new SettingsCard();
        theme.Add(Ui.Settings.Theme, Ui.Settings.ThemeHint, _appearance);
        theme.Add(
            Ui.Settings.ShowFavoritesInFolders,
            Ui.Settings.ShowFavoritesInFoldersHint,
            _showFavoritesInTheirFolders);
        AddSection(page, Ui.Settings.Theme, theme);

        var language = new SettingsCard();
        language.Add(Ui.Settings.Language, Ui.Settings.LanguageHint, _language);
        AddSection(page, Ui.Settings.Language, language);
        return page;
    }

    /// <summary>
    /// How one language is named in the list: in itself, never in the language now on screen.
    /// </summary>
    /// <remarks>
    /// This is what lets someone who has ended up in a language they cannot read find their own
    /// again, and it is what every other application does for the same reason.
    /// </remarks>
    private static string DescribeLanguage(string culture)
    {
        if (string.Equals(culture, DesktopCulture.AutomaticLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return Ui.Settings.LanguageAutomatic;
        }

        return CultureInfo.GetCultureInfo(culture).NativeName;
    }

    private WorkspaceLayout ReadWorkspaceLayout() =>
        _defaultWorkspaceLayout.SelectedItem is WorkspaceLayout layout ? layout : WorkspaceLayout.SideBySide;

    /// <summary>The chosen preset's pane count, or null when "ask every time" is selected.</summary>
    private int? ReadWorkspacePaneCount() =>
        _defaultWorkspacePreset.SelectedItem is WorkspacePreset preset ? preset.PaneCount : null;

    /// <summary>
    /// Keeps the preset and the orientation agreeing. Without this the two controls can describe
    /// different workspaces -- "2 panes, top and bottom" beside an orientation of "Side by side" --
    /// and nothing on screen says which wins.
    /// </summary>
    private void SyncWorkspaceControls(bool fromPreset)
    {
        if (_syncingWorkspaceControls) return;
        _syncingWorkspaceControls = true;
        try
        {
            if (_defaultWorkspacePreset.SelectedItem is not WorkspacePreset preset ||
                !preset.OrientationMatters)
            {
                // Ask every time, one pane, or a grid: the orientation stands on its own as the
                // default the chooser opens with.
                return;
            }

            if (fromPreset)
            {
                _defaultWorkspaceLayout.SelectedItem = preset.Layout;
            }
            else if (WorkspacePreset.Find(preset.PaneCount, ReadWorkspaceLayout()) is { } matching)
            {
                _defaultWorkspacePreset.SelectedItem = matching;
            }
        }
        finally
        {
            _syncingWorkspaceControls = false;
        }
    }

    private SettingsPagePanel BuildWorkspacePage()
    {
        var page = CreatePage(
            "Workspace",
            Ui.Settings.PageWorkspaceDescription);
        var card = new SettingsCard();
        card.Add(Ui.Settings.DefaultPaneLayout, Ui.Settings.DefaultPaneLayoutHint, _defaultWorkspaceLayout);
        card.Add(Ui.Settings.NewWorkspaceLayout, Ui.Settings.WorkspacePresetHint, _defaultWorkspacePreset);
        card.Add(
            Ui.Settings.ReconnectRemotePanes,
            Ui.Settings.ReconnectRemotePanesHint,
            _reconnectRemotePanes);
        AddSection(page, Ui.Settings.CategoryWorkspace, card);
        return page;
    }

    private SettingsPagePanel BuildProviderSettingsPage(
        ConnectionProviderDescriptor provider,
        DesktopUpdatePreferences preferences)
    {
        var page = CreatePage(
            Ui.Format(Ui.Settings.ProviderDefaultsFormat, provider.DisplayName),
            provider.Kind == StorageProviderKind.Ssh
                ? Ui.Settings.TerminalPreferencesHint
                : Ui.Format(Ui.Settings.ProviderDefaultsDescriptionFormat, provider.DisplayName));
        var defaults = ConnectionDefaultSettings.Get(provider.Kind, preferences.ConnectionDefaults);
        var basics = new SettingsCard
        {
            Name = $"ProviderSettings:{provider.Kind}",
            AccessibleName = Ui.Format(Ui.Settings.ProviderNewConnectionDefaultsFormat, provider.DisplayName)
        };
        var editableFields = ConnectionDefaultSettings.EditableFields(provider);
        if (editableFields.Count == 0)
        {
            basics.Add(Ui.Settings.NoReusableDefaults, null, null);
        }

        foreach (var field in editableFields)
        {
            var (control, accessory) = CreateProviderFieldDefault(field, defaults.FieldValues[field.Key]);
            AddProviderDefaultRow(
                basics,
                provider.Kind,
                field.Key,
                ProviderDefaultLabel(field),
                control,
                ProviderFieldDescription(field),
                accessory);
        }

        AddSection(page, Ui.Settings.BasicDefaults, basics);

        var advanced = new SettingsCard();
        var connectionTimeout = CreateProviderNumberDefault(defaults.ConnectTimeoutSeconds, 1, 600);
        connectionTimeout.Unit = Ui.Settings.UnitSeconds;
        AddProviderDefaultRow(
            advanced,
            provider.Kind,
            ConnectionDefaultSettings.ConnectTimeoutKey,
            Ui.Settings.ConnectionTimeout,
            connectionTimeout,
            description: null);
        var operationTimeout = CreateProviderNumberDefault(defaults.OperationTimeoutSeconds, 1, 86_400);
        operationTimeout.Unit = Ui.Settings.UnitSeconds;
        operationTimeout.Enabled = provider.Kind == StorageProviderKind.Local;
        if (provider.Kind != StorageProviderKind.Local)
        {
            connectionTimeout.ValueChanged += (_, _) => operationTimeout.Value = connectionTimeout.Value;
        }

        AddProviderDefaultRow(
            advanced,
            provider.Kind,
            ConnectionDefaultSettings.OperationTimeoutKey,
            Ui.Settings.OperationTimeout,
            operationTimeout,
            provider.Kind == StorageProviderKind.Local
                ? null
                : Ui.Settings.OperationTimeoutHint);
        var retries = CreateProviderNumberDefault(defaults.MaximumRetryAttempts, 0, 20);
        var retriesSupported = ConnectionDefaultSettings.SupportsConfigurableRetries(provider.Kind);
        retries.Enabled = retriesSupported;
        AddProviderDefaultRow(
            advanced,
            provider.Kind,
            ConnectionDefaultSettings.RetryAttemptsKey,
            Ui.Settings.RetryAttempts,
            retries,
            retriesSupported
                ? null
                : Ui.Settings.RetriesUnsupported);
        AddSection(page, Ui.Settings.AdvancedBehavior, advanced);

        if (provider.Kind == StorageProviderKind.Ssh)
        {
            AddSection(page, Ui.Settings.TerminalAndShell, BuildSshTerminalSettingsEditor());
        }
        var open = new StorageHubButton
        {
            Text = Ui.Format(Ui.Settings.CreateProviderFormat, provider.DisplayName),
            AutoSize = true,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 16, 0, 8)),
            AccessibleName = Ui.Format(Ui.Settings.ConfigureProviderFormat, provider.DisplayName)
        };
        open.Variant = StorageHubButtonVariant.Primary;
        open.Click += (_, _) =>
        {
            using var manager = new ConnectionManagerForm(initialProvider: provider.Kind);
            _ = manager.ShowDialog(this);
        };
        page.Controls.Add(open);
        return page;
    }

    private SettingsCard BuildSshTerminalSettingsEditor()
    {
        var card = new SettingsCard
        {
            Name = "SshTerminalSettings",
            AccessibleName = Ui.Settings.TerminalPreferencesAccessibleName
        };
        card.Add(Ui.Settings.TerminalType, Ui.Settings.TerminalTypeHint, _sshTerminalName);
        card.Add(Ui.Settings.StartupShell, null, _sshStartupCommand);
        card.Add(Ui.Settings.KeepAliveInterval, null, _sshKeepAliveSeconds);
        card.Add(Ui.Settings.FontFamily, Ui.Settings.FontFamilyHint, _sshFontFamily);
        card.Add(Ui.Settings.FontSize, null, _sshFontSize);
        card.Add(Ui.Settings.ScrollbackLines, null, _sshScrollbackLines);
        card.Add(Ui.Settings.OutputRefresh, Ui.Settings.OutputRefreshHint, _sshRefreshInterval);
        card.Add(Ui.Settings.RenderBoldText, Ui.Settings.RenderBoldTextHint, _sshRenderBoldText);
        return card;
    }

    private StorageHubNumberField CreateProviderNumberDefault(int value, int minimum, int maximum)
    {
        var control = new StorageHubNumberField
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = Math.Clamp(value, minimum, maximum),
            Width = LogicalToDeviceUnits(150),
            ThousandsSeparator = true
        };
        control.ValueChanged += MarkDirty;
        return control;
    }

    /// <summary>
    /// The control that edits one provider default, and the buttons that belong beside it when
    /// the value is not typed but picked.
    /// </summary>
    private (Control Control, Control? Accessory) CreateProviderFieldDefault(
        ConnectionFieldDescriptor field,
        string value)
    {
        if (field.Kind == ConnectionFieldKind.SecretReference &&
            string.Equals(field.Key, "privateKeyReference", StringComparison.Ordinal))
        {
            return CreateDefaultSshPrivateKeyPicker(value);
        }

        if (field.Kind == ConnectionFieldKind.Number)
        {
            _ = int.TryParse(value, out var number);
            return (CreateProviderNumberDefault(number, 1, 65_535), null);
        }

        if (field.Kind == ConnectionFieldKind.Choice)
        {
            var choice = new StorageHubChoiceField { Width = 280 };
            foreach (var option in field.Choices ?? [])
            {
                choice.Items.Add(option);
            }

            choice.SelectedItem = choice.Items.Cast<object>()
                .FirstOrDefault(item => string.Equals(item.ToString(), value, StringComparison.Ordinal));
            if (choice.SelectedIndex < 0 && choice.Items.Count > 0)
            {
                choice.SelectedIndex = 0;
            }

            choice.SelectedIndexChanged += MarkDirty;
            return (choice, null);
        }

        var text = new StorageHubTextField
        {
            Text = value,
            Width = LogicalToDeviceUnits(280),
            MaxLength = 2_048
        };
        text.TextChanged += MarkDirty;
        return (text, null);
    }

    private (Control Control, Control? Accessory) CreateDefaultSshPrivateKeyPicker(string reference)
    {
        var value = new StorageHubTextField
        {
            ReadOnly = true,
            Text = reference,
            Width = LogicalToDeviceUnits(200),
            PlaceholderText = Ui.Settings.NoDefaultPrivateKey,
            AccessibleDescription = Ui.Settings.PrivateKeyAccessibleDescription
        };
        value.TextChanged += MarkDirty;
        var import = new StorageHubButton
        {
            Text = Ui.Settings.ImportKey,
            Variant = StorageHubButtonVariant.Secondary
        };
        var clear = new StorageHubButton
        {
            Text = Ui.Settings.ClearDefault,
            Variant = StorageHubButtonVariant.Secondary
        };
        import.Click += async (_, _) => await ImportDefaultSshPrivateKeyAsync(value);
        clear.Click += (_, _) => value.Text = string.Empty;
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        import.Margin = this.LogicalToDeviceUnits(new Padding(0, 0, 6, 0));
        clear.Margin = Padding.Empty;
        buttons.Controls.Add(import);
        buttons.Controls.Add(clear);
        // Measured rather than auto-sized: the row asks the accessory how wide it is while laying
        // out, which is before a flow panel would have sized itself from its children.
        buttons.Size = new Size(
            import.GetPreferredSize(Size.Empty).Width
            + LogicalToDeviceUnits(6)
            + clear.GetPreferredSize(Size.Empty).Width,
            Math.Max(import.GetPreferredSize(Size.Empty).Height, clear.GetPreferredSize(Size.Empty).Height));
        return (value, buttons);
    }

    private async Task ImportDefaultSshPrivateKeyAsync(StorageHubTextField referenceBox)
    {
        using var picker = new OpenFileDialog
        {
            Title = Ui.Settings.SelectPrivateKeyTitle,
            Filter = Ui.Settings.PrivateKeyFilter,
            CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        byte[]? material = null;
        try
        {
            var file = new FileInfo(picker.FileName);
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0 ||
                file.Length is <= 0 or > SecretVaultIpcContract.MaximumSecretBytes)
            {
                throw new IOException(Ui.Settings.PrivateKeyUnavailable);
            }

            material = await File.ReadAllBytesAsync(picker.FileName);
            var response = await _secretClient.EnrollAsync(
                SecretMaterialPurpose.SshPrivateKey,
                material);
            if (!response.Succeeded || string.IsNullOrWhiteSpace(response.Reference))
            {
                _ = MessageBox.Show(
                    this,
                    response.Failure?.Message ?? Ui.Dialogs.DefaultSshPrivateKeyImportFailed,
                    Ui.Dialogs.DefaultSshPrivateKeyCaption,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            referenceBox.Text = response.Reference;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or
            InvalidOperationException or TimeoutException or System.Text.Json.JsonException)
        {
            _ = MessageBox.Show(
                this,
                Ui.Dialogs.DefaultSshPrivateKeyVaultFailed,
                Ui.Dialogs.DefaultSshPrivateKeyCaption,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            if (material is not null)
            {
                CryptographicOperations.ZeroMemory(material);
            }
        }
    }

    private void AddProviderDefaultRow(
        SettingsCard card,
        StorageProviderKind provider,
        string setting,
        string label,
        Control control,
        string? description = null,
        Control? accessory = null)
    {
        var key = ConnectionDefaultSettings.Key(provider, setting);
        control.AccessibleName = label;
        _connectionDefaultControls.Add(key, control);
        _ = card.Add(label, description, control, accessory);
    }

    private static string? ProviderFieldDescription(ConnectionFieldDescriptor field)
    {
        return field.Key switch
        {
            "authenticationMode" => Ui.Settings.AuthenticationModeHint,
            "privateKeyReference" => Ui.Settings.StoredInVault,
            "tlsMode" or "trustMode" => field.HelpText,
            _ => null
        };
    }

    private static string ProviderDefaultLabel(ConnectionFieldDescriptor field) => field.Key switch
    {
        "privateKeyReference" => Ui.Settings.DefaultPrivateKey,
        _ => Ui.Format(Ui.Settings.DefaultFieldFormat, field.Label.ToLower(CultureInfo.CurrentCulture))
    };

    private void AddProviderFieldGroup(
        FlowLayoutPanel page,
        string title,
        IReadOnlyList<ConnectionFieldDescriptor> fields)
    {
        var details = fields.Count == 0
            ? Ui.Settings.NoSettingsRequired
            : string.Join(Environment.NewLine, fields.Select(field =>
                $"• {field.Label}{(field.Required ? " (required)" : string.Empty)}" +
                (string.IsNullOrWhiteSpace(field.DefaultValue) ? string.Empty : $" — default: {field.DefaultValue}") +
                (string.IsNullOrWhiteSpace(field.HelpText) ? string.Empty : $" — {field.HelpText}")));
        page.Controls.Add(CreateInformationCard(title, details));
    }

    private static string ConnectionTypePageKey(ConnectionProfileType type) => $"ConnectionType:{type}";

    private static string ProviderPageKey(StorageProviderKind provider) => $"Provider:{provider}";

    private void CategorySelected(object? sender, EventArgs e)
    {
        if (_categories.SelectedNode is { } chosen && IsCategoryCaption(chosen))
        {
            // A caption has no page of its own, so the selection passes through it rather
            // than stopping there. Which way it passes depends on where it came from:
            // always dropping onto the first child would trap the arrow keys, because
            // moving up off that child lands on the caption and would bounce straight back.
            var arrivingFromBelow = _lastCategoryNode is { } previous &&
                previous.Parent == chosen;
            _categories.SelectedNode = arrivingFromBelow
                ? chosen.PrevVisibleNode ?? chosen.Nodes[0]
                : chosen.Nodes[0];
            return;
        }

        _lastCategoryNode = _categories.SelectedNode;
        _categories.Invalidate();
        var selected = _categories.SelectedNode?.Name;
        foreach (var page in _pages)
        {
            // Reading Visible back reports false whenever an ancestor is hidden, so on a form
            // that has not been shown yet no page was ever raised or laid out. Keep the decision
            // in a local so the selected page is prepared before the form first appears.
            var isSelected = string.Equals(page.Key, selected, StringComparison.Ordinal);
            page.Value.Visible = isSelected;
            if (isSelected)
            {
                page.Value.BringToFront();
                if (page.Value is SettingsPagePanel settingsPage)
                {
                    settingsPage.PerformLayout();
                    FitSettingsPageContent(settingsPage);
                    // The page has only just been docked to its real width, so this is the
                    // first moment its scrollbars can be worked out correctly.
                    settingsPage.ResetScrollState();
                }
            }
        }
    }

    private void UpdateDependencies(object? sender, EventArgs e)
    {
        _downloadAutomatically.Enabled = _checkAutomatically.Checked;
        _restartAutomatically.Enabled = _checkAutomatically.Checked && _downloadAutomatically.Checked;
        _minimumConcurrency.Enabled = _adaptiveConcurrency.Checked;
    }

    private void ConcurrencyChanged(object? sender, EventArgs e)
    {
        var minimum = (int)_minimumConcurrency.Value;
        if (_maximumTransferConcurrency.Value < minimum)
        {
            _maximumTransferConcurrency.Value = minimum;
        }

        if (_maximumSyncConcurrency.Value < minimum)
        {
            _maximumSyncConcurrency.Value = minimum;
        }

        UpdateDependencies(sender, e);
        MarkDirty(sender, e);
    }

    private void DiscoverySelectionChanged(object? sender, EventArgs e)
    {
        UpdateDiscoveryDescription();
        MarkDirty(sender, e);
    }

    private void AppearanceSelectionChanged(object? sender, EventArgs e)
    {
        if (_appearance.SelectedItem is DesktopAppearance appearance)
        {
            var previous = DesktopAppearanceService.EffectiveAppearance;
            DesktopAppearanceService.SetAppearance(appearance);
            StorageHubTheme.Apply(this, previous);
        }

        MarkDirty(sender, e);
    }

    /// <summary>
    /// The chosen mode explains itself in its own row, rather than in a paragraph underneath that
    /// had no visible tie to the control it described.
    /// </summary>
    private void UpdateDiscoveryDescription()
    {
        if (_sshDiscoveryRow is { } row)
        {
            row.Description = _sshDiscovery.SelectedItem is DiscoveryChoice choice
                ? choice.Description
                : null;
        }
    }

    private void MarkDirty(object? sender, EventArgs e) => _apply.Enabled = true;

    private void SaveAndClose(object? sender, EventArgs e)
    {
        if (TrySave())
        {
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private void SaveWithoutClosing(object? sender, EventArgs e) => _ = TrySave();

    /// <summary>
    /// Whether the user asked to restart so the new language takes effect. The shell acts on this
    /// once the dialog has closed; restarting from inside a modal dialog would tear down the
    /// window that is running the code.
    /// </summary>
    internal bool LanguageRestartRequested { get; private set; }

    /// <summary>
    /// Asks the user whether to restart now, given the language named in itself.
    /// </summary>
    /// <remarks>
    /// A seam only so a test can answer it. The rule worth testing is not the message box but
    /// which changes are worth interrupting someone for, and a modal prompt cannot be exercised
    /// from a test run. Left null in the product, where it is the message box.
    /// </remarks>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<string, bool>? AskAboutLanguageRestart { get; set; }

    private bool PromptForLanguageRestart(string language) =>
        MessageBox.Show(
            this,
            Ui.Format(Ui.Dialogs.LanguageRestartPromptFormat, language),
            Ui.Dialogs.LanguageRestartCaption,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question) == DialogResult.Yes;

    /// <summary>
    /// Asks about restarting, but only when the saved setting actually changes the language on
    /// screen.
    /// </summary>
    /// <remarks>
    /// The comparison is between resolved cultures, not between the stored values: on a Danish
    /// Windows, moving from "Same as Windows" to "Dansk" writes a different setting and changes
    /// nothing visible, and prompting there would be noise. Resolving both sides through
    /// <see cref="DesktopCulture.ResolveCurrent"/> also honours STORAGEHUB_LANGUAGE, so a launch
    /// pinned by the environment never offers a restart that would not change anything either.
    /// </remarks>
    private void OfferLanguageRestart(string language)
    {
        var current = DesktopCulture.ResolveCurrent(_appliedLanguage);
        var next = DesktopCulture.ResolveCurrent(language);
        _appliedLanguage = language;
        if (string.Equals(current, next, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var ask = AskAboutLanguageRestart ?? PromptForLanguageRestart;
        if (!ask(DescribeLanguage(next)))
        {
            // Declining is not a failure: the setting is saved either way and takes effect at the
            // next launch, which is what happened before there was a prompt at all.
            return;
        }

        LanguageRestartRequested = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    private Dictionary<string, string> ReadConnectionDefaults()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in _connectionDefaultControls)
        {
            values[pair.Key] = pair.Value switch
            {
                StorageHubNumberField number => decimal.ToInt32(number.Value)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture),
                StorageHubChoiceField choice => choice.SelectedItem?.ToString() ?? string.Empty,
                StorageHubTextField text => text.Text.Trim(),
                _ => throw new InvalidOperationException(Ui.Settings.UnknownControl)
            };
        }

        return ConnectionDefaultSettings.Normalize(values);
    }

    private SshTerminalPreferences ReadSshTerminalPreferences() =>
        SshTerminalPreferences.Resolve(new SshTerminalPreferences(
            _sshTerminalName.Text,
            _sshStartupCommand.Text,
            decimal.ToInt32(_sshKeepAliveSeconds.Value),
            _sshFontFamily.Text,
            decimal.ToSingle(_sshFontSize.Value),
            decimal.ToInt32(_sshScrollbackLines.Value),
            decimal.ToInt32(_sshRefreshInterval.Value),
            _sshRenderBoldText.Checked));

    private bool TrySave()
    {
        try
        {
            var discovery = (_sshDiscovery.SelectedItem as DiscoveryChoice)?.Mode
                ?? SshHostKeyDiscoveryMode.AskBeforeFetching;
            var editorPath = string.IsNullOrWhiteSpace(_externalEditor.Text)
                ? null
                : Path.GetFullPath(_externalEditor.Text.Trim());
            if (editorPath is not null && (!File.Exists(editorPath) ||
                (File.GetAttributes(editorPath) & FileAttributes.ReparsePoint) != 0))
            {
                _ = MessageBox.Show(
                    this,
                    Ui.Dialogs.ExternalEditorChooseExecutable,
                    Ui.Dialogs.ExternalEditorCaption,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }

            var preferences = new DesktopUpdatePreferences(
                _checkAutomatically.Checked,
                _downloadAutomatically.Checked,
                _restartAutomatically.Checked,
                _includePrereleases.Checked,
                discovery,
                editorPath,
                checked((int)_maximumEditableKilobytes.Value * 1024),
                _adaptiveConcurrency.Checked,
                (int)_minimumConcurrency.Value,
                (int)_maximumTransferConcurrency.Value,
                (int)_perConnectionConcurrency.Value,
                (int)_maximumSyncConcurrency.Value,
                _appearance.SelectedItem is DesktopAppearance appearance ? appearance : DesktopAppearance.System,
                _warnBeforeUnsafeExternalEdit.Checked,
                ReadConnectionDefaults(),
                ReadWorkspaceLayout(),
                ReadSshTerminalPreferences(),
                _reconnectRemotePanes.Checked,
                _confirmBeforeClearingTransferHistory.Checked,
                _confirmBeforeDeletingItems.Checked,
                _shortcuts.ReadShortcuts(),
                // The pinned and recent lists are written by the shell, not this dialog, and are
                // carried through untouched so saving settings never drops them.
                _preferences.PinnedWorkspaces,
                _preferences.RecentWorkspaces,
                ReadWorkspacePaneCount(),
                // Panel geometry belongs to the shell, like the workspace lists above it: this
                // dialog does not show it, so it carries it through rather than defaulting it.
                _preferences.ConnectionsPanelWidth,
                _preferences.ConnectionsPanelVisible,
                _preferences.ConnectionsPanelSide,
                (string)(_language.SelectedItem ?? DesktopCulture.AutomaticLanguage),
                _showFavoritesInTheirFolders.Checked,
                ToolbarItems: _toolbar.ReadItems(),
                ToolbarLabels: _toolbar.ReadLabels());
            if (_saved is null)
            {
                _store.Save(preferences);
            }
            else
            {
                _saved(preferences);
            }

            DesktopAppearanceService.SetAppearance(preferences.Appearance);
            _appliedAppearance = preferences.Appearance;

            _apply.Enabled = false;
            OfferLanguageRestart(preferences.Language);
            return true;
        }
        catch (Exception error) when (error is
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException)
        {
            _ = MessageBox.Show(
                this,
                Ui.Dialogs.SettingsSaveFailed,
                Ui.Dialogs.SettingsCaption,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }
    }

    private sealed record DiscoveryChoice(
        SshHostKeyDiscoveryMode Mode,
        string Label,
        string Description)
    {
        public override string ToString() => Label;
    }
}
