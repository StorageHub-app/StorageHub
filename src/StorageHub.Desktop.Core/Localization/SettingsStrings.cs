using CodeLogic.Core.Localization;

namespace StorageHub.Desktop.Localization;

/// <summary>
/// The Settings window: its categories, pages, options and the prose explaining them.
/// </summary>
/// <remarks>
/// Flat string properties only, and no property may be named <c>Culture</c>. See
/// <see cref="CommandStrings"/> for why.
///
/// Page keys, control names and technical values such as a TERM name are deliberately absent:
/// they identify things rather than describe them, and translating them would break the lookup
/// they exist for.
/// </remarks>
[LocalizationSection("settings")]
internal sealed class SettingsStrings : LocalizationModelBase
{
    // -------------------------------------------------------------- agent host
    public string CategoryAgent { get; set; } = "Background agent";

    public string PageAgentDescription { get; set; } =
        "The agent does the transfers and scheduled syncs. Choose whether it runs only while you " +
        "are signed in, or as a Windows service that starts with the computer.";

    public string AgentModeSection { get; set; } = "Background agent";

    public string AgentModeLabel { get; set; } = "Run the agent";

    public string AgentModeUserSession { get; set; } = "When I sign in";

    public string AgentModeService { get; set; } = "As a Windows service";

    public string AgentModeAppSession { get; set; } = "Only while StorageHub is open";

    public string AgentModeAppSessionDetail { get; set; } =
        "Starts with StorageHub and stops when you close it. Nothing is registered to run at " +
        "sign-in and nothing is left running afterwards. Transfers and schedules only progress " +
        "while the window is open.";

    public string AgentModeHint { get; set; } =
        "Signing in runs the agent in your session, so it stops when you sign out. " +
        "A service runs from startup with nobody signed in, which is what scheduled syncs need.";

    public string AgentModeServiceWarning { get; set; } =
        "Switching to a service copies your connections and secrets to a machine-wide location " +
        "that administrators of this computer can read. Your existing data is copied, not moved.";

    public string AgentModeApply { get; set; } = "Apply change";

    /// <summary>{0} = the version the service is running, {1} = this application's version.</summary>
    public string AgentModeStaleServiceFormat { get; set; } =
        "The service is still running version {0}, but this application is {1}. " +
        "Apply the change again to update it.";

    public string AgentModeApplied { get; set; } = "The background agent mode was changed.";

    public string AgentModeAppliedWithSecretLoss { get; set; } =
        "The service is running, but some stored secrets could not be copied and must be entered again.";

    public string AgentModeNeedsAdministrator { get; set; } =
        "Changing how the agent runs needs administrator rights.";

    public string AgentModeChangeFailed { get; set; } = "The background agent mode could not be changed.";

    public string AgentModeChangeCancelled { get; set; } = "The change was cancelled.";

    public string AgentModeApplying { get; set; } = "Applying the background agent mode…";

    /// <summary>{0} = the mode actually in force.</summary>
    public string AgentModeMismatchFormat { get; set; } =
        "The agent is currently running {0}, which is not what is configured.";

    public string AgentModeStartupQuestionTitle { get; set; } = "How should StorageHub run?";

    public string AgentModeStartupQuestion { get; set; } =
        "StorageHub can now run its background agent as a Windows service, so transfers and " +
        "scheduled syncs continue when you are signed out. Would you like to switch?\r\n\r\n" +
        "You can change this later in Settings.";

    public string AgentModeStartupIntro { get; set; } =
        "Choose how StorageHub runs its background agent on this computer.";

    public string AgentModeUserSessionDetail { get; set; } =
        "Runs in your session and stops when you sign out. Your saved secrets stay readable " +
        "only by your account. Scheduled syncs run only while you are signed in.";

    public string AgentModeServiceDetail { get; set; } =
        "Starts with the computer and keeps running when nobody is signed in, so scheduled " +
        "syncs and transfers continue. Requires administrator approval once.";

    public string AgentModeChangeLaterHint { get; set; } =
        "You can change this at any time in Settings under Background agent.";

    public string AgentModeContinue { get; set; } = "Continue";

    public string AgentModeKeepUserSession { get; set; } = "Keep running at sign-in";

    public string AgentModeSwitchToService { get; set; } = "Run as a service";

    // ----------------------------------------------------------------- toolbar
    public string CategoryToolbar { get; set; } = "Toolbar";

    public string PageToolbarDescription { get; set; } =
        "Choose which commands appear on the toolbar and in what order. Every command in the " +
        "menus can be added.";

    public string ToolbarAccessibleName { get; set; } = "Toolbar layout";

    public string ToolbarAvailable { get; set; } = "Available commands";

    public string ToolbarAvailableAccessibleName { get; set; } = "Commands available for the toolbar";

    public string ToolbarCurrent { get; set; } = "On the toolbar";

    public string ToolbarCurrentAccessibleName { get; set; } = "Commands on the toolbar, in order";

    public string ToolbarAdd { get; set; } = "Add";

    public string ToolbarRemove { get; set; } = "Remove";

    public string ToolbarAddSeparator { get; set; } = "Add divider";

    public string ToolbarMoveUp { get; set; } = "Move up";

    public string ToolbarMoveDown { get; set; } = "Move down";

    public string ToolbarSeparator { get; set; } = "— divider —";

    public string ToolbarLabels { get; set; } = "Button labels";

    public string ToolbarLabelsAccessibleName { get; set; } = "Toolbar button labels";

    public string ToolbarLabelsIconsOnly { get; set; } = "Icons only";

    public string ToolbarLabelsIconsAndText { get; set; } = "Icons and text";

    public string ToolbarLabelsTextUnderIcon { get; set; } = "Text under icon";

    public string ToolbarResetEssential { get; set; } = "Reset to essentials";

    public string ToolbarResetExpanded { get; set; } = "Reset to expanded";

    // ------------------------------------------------------------------ window
    public string WindowTitle { get; set; } = "Settings — StorageHub";

    public string WindowAccessibleName { get; set; } = "StorageHub Settings";

    public string WindowAccessibleDescription { get; set; } =
        "Configure StorageHub behavior, connection trust, and updates.";

    public string NavigationTitle { get; set; } = "Settings";

    public string CategoriesAccessibleName { get; set; } = "Settings categories";

    public string ButtonBrowse { get; set; } = "Browse...";

    // -------------------------------------------------------------- categories
    public string CategoryTransfersAndSync { get; set; } = "Transfers & sync";

    public string CategoryPerformance { get; set; } = "Performance";

    public string CategoryEditing { get; set; } = "Editing";

    public string CategoryAppearance { get; set; } = "Appearance";

    public string CategoryWorkspace { get; set; } = "Workspace";

    public string CategoryShortcuts { get; set; } = "Shortcuts";

    public string CategoryConnectionsAndTrust { get; set; } = "Connections & trust";

    public string CategoryStorage { get; set; } = "Storage";

    public string CategoryClients { get; set; } = "Clients";

    public string CategoryUpdates { get; set; } = "Updates";

    // ------------------------------------------------------------ page headings
    public string PagePerformanceDescription { get; set; } =
        "Control how many transfers and synchronization jobs run at once.";

    public string PageEditingDescription { get; set; } =
        "Choose how remote files open in an external editor.";

    public string PageAppearanceDescription { get; set; } = "Choose the application color theme and the language StorageHub speaks.";

    public string PageWorkspaceDescription { get; set; } =
        "Choose how new workspaces are arranged, and whether StorageHub asks first.";

    public string PageUpdatesDescription { get; set; } =
        "Choose how StorageHub checks for and installs updates.";

    public string PageTrustDescription { get; set; } =
        "Choose how SSH host keys are discovered before you verify and trust them.";

    public string PageStorageDescription { get; set; } =
        "Providers available for browsing, transfers, and synchronization.";

    public string PageClientsDescription { get; set; } = "Interactive remote client providers.";

    public string PageShortcutsDescription { get; set; } =
        "Customize keyboard commands. File commands act on the active pane. Shortcuts do not intercept text fields or SSH terminal input. Clear an assignment to disable it, or restore the defaults.";

    // --------------------------------------------------------------- concurrency
    public string SectionConcurrency { get; set; } = "Concurrency";

    public string SectionConfirmations { get; set; } = "Confirmations";

    /// <summary>
    /// Units shown inside a number field, after the value. Short by design: the field is narrow
    /// and the row's title already says what is being counted.
    /// </summary>
    public string UnitJobs { get; set; } = "jobs";

    public string UnitKibibytes { get; set; } = "KiB";

    public string UnitSeconds { get; set; } = "s";

    public string UnitPoints { get; set; } = "pt";

    public string AdaptiveConcurrency { get; set; } =
        "Automatically tune concurrency from observed speed";

    public string MaximumTransfers { get; set; } = "Maximum transfers";

    public string MaximumTransfersAccessibleName { get; set; } = "Maximum concurrent transfers";

    public string MaximumTransfersHint { get; set; } =
        "Global ceiling shared by transfers across every supported provider.";

    public string PerConnection { get; set; } = "Per saved connection";

    public string PerConnectionAccessibleName { get; set; } = "Maximum transfers per connection";

    public string PerConnectionHint { get; set; } =
        "Prevents one server, bucket, or local connection from consuming every worker.";

    public string MaximumSynchronizations { get; set; } = "Maximum synchronizations";

    public string MaximumSynchronizationsAccessibleName { get; set; } =
        "Maximum concurrent synchronizations";

    public string MaximumSynchronizationsHint { get; set; } =
        "Separate ceiling for scheduled and manually approved synchronization runs.";

    public string MinimumConcurrencyHint { get; set; } =
        "The adaptive controller begins conservatively at this many jobs.";

    // ------------------------------------------------------------------ editing
    public string EditorExecutable { get; set; } = "Editor executable";

    public string EditorExecutableAccessibleName { get; set; } = "External editor executable";

    public string EditorPlaceholder { get; set; } =
        "Choose an editor executable, or leave blank for the Windows default";

    public string EditorHint { get; set; } = "Leave blank to use the Windows default app.";

    public string ChooseEditorTitle { get; set; } = "Choose external editor";

    /// <summary>A file-dialog filter. The descriptions translate; the patterns must not.</summary>
    public string ExecutableFilter { get; set; } = "Applications (*.exe)|*.exe|All files (*.*)|*.*";

    public string MaximumEditableSize { get; set; } = "Maximum editable file size (KiB)";

    public string MaximumEditableSizeAccessibleName { get; set; } =
        "Maximum externally editable file size in KiB";

    public string MaximumEditableSizeHint { get; set; } = "Maximum: 1,024 KiB (1 MiB).";

    public string WarnUnsafeEdit { get; set; } =
        "Warn before editing without remote change protection";

    public string WarnUnsafeEditHint { get; set; } =
        "When a provider cannot enforce version or ETag checks, ask before continuing with an edit that could overwrite newer remote changes.";

    // --------------------------------------------------------------- appearance
    public string Theme { get; set; } = "Theme";

    public string ThemeAccessibleName { get; set; } = "Application appearance";

    public string ThemeHint { get; set; } = "System follows Windows. Changes preview immediately.";

    // ---------------------------------------------------------------- workspace
    public string DefaultPaneLayout { get; set; } = "Default pane layout";

    public string DefaultPaneLayoutAccessibleName { get; set; } = "Default workspace layout";

    public string DefaultPaneLayoutHint { get; set; } =
        "Choose the orientation used by two- and three-pane presets.";

    public string NewWorkspaceLayout { get; set; } = "New workspace layout";

    public string NewWorkspaceLayoutAccessibleName { get; set; } = "Layout for a new workspace";

    public string LayoutSideBySide { get; set; } = "Side by side";

    public string LayoutTopAndBottom { get; set; } = "Top and bottom";

    public string LayoutTwoPanesTopAndBottom { get; set; } = "2 panes, top and bottom";

    public string LayoutAskEveryTime { get; set; } = "Ask every time";

    public string ReconnectRemotePanes { get; set; } =
        "Reconnect remote panes automatically when opening workspace files";

    public string ReconnectRemotePanesHint { get; set; } =
        "Uses saved profiles to create fresh storage and SSH sessions. Workspace files never contain credentials or terminal contents.";

    public string WarnClearingHistory { get; set; } = "Warn before clearing all transfer history";

    public string WarnClearingHistoryHint { get; set; } =
        "Shows a confirmation before permanently removing completed, cancelled, and failed transfer records.";

    public string WarnDeletingItems { get; set; } = "Warn before deleting files and folders";

    public string WarnDeletingItemsHint { get; set; } =
        "Shows a review prompt before sending local items to the Recycle Bin or permanently deleting remote items.";

    // ------------------------------------------------------------------ updates
    public string CheckAutomatically { get; set; } =
        "Check GitHub for updates when StorageHub starts";

    public string CheckAutomaticallyHint { get; set; } =
        "Uses the fixed official StorageHub repository. Manual checks remain available when disabled.";

    public string DownloadAutomatically { get; set; } = "Download available updates automatically";

    public string DownloadAutomaticallyHint { get; set; } =
        "Downloads the matching integrity-checked Velopack package silently after an automatic check.";

    public string RestartAutomatically { get; set; } = "Install silently and restart automatically";

    public string RestartAutomaticallyHint { get; set; } =
        "Closes StorageHub after download, applies the update, and reopens it. Disabled by default to avoid interrupting work.";

    public string IncludePrereleases { get; set; } = "Include release candidate builds";

    public string IncludePrereleasesHint { get; set; } =
        "Keep enabled while using StorageHub release candidates. Disable it later to receive stable releases only.";

    public string UpdateSourceAccessibleName { get; set; } = "Update source and installed version";

    /// <summary>{0} = the repository URL, {1} = the installed version.</summary>
    public string UpdateSourceFormat { get; set; } = "Update source: {0}\nInstalled version: {1}";

    // -------------------------------------------------------------------- trust
    public string HostKeyDiscovery { get; set; } = "SSH host-key discovery";

    public string HostKeyDiscoveryAccessibleName { get; set; } = "SSH host-key discovery";

    public string HostKeyDiscoveryDescriptionAccessibleName { get; set; } =
        "SSH host-key discovery description";

    public string HostKeyManual { get; set; } = "Manual — use Fetch from host";

    public string HostKeyAsk { get; set; } = "Ask before fetching";

    public string HostKeyAutomatic { get; set; } = "Fetch automatically";

    public string HostKeyManualHint { get; set; } =
        "StorageHub fetches a host key only when you press the button in the connection editor.";

    public string HostKeyAskHint { get; set; } =
        "When an SFTP endpoint is ready and has no fingerprint, StorageHub asks before contacting it.";

    public string HostKeyAutomaticHint { get; set; } =
        "When an SFTP endpoint is ready and has no fingerprint, StorageHub retrieves and displays its key automatically.";

    public string HostKeyCaveat { get; set; } =
        "Fetching discovers what the contacted host presents; it does not prove that the host is genuine. Automatic discovery never records trust or bypasses fingerprint verification.";

    // ----------------------------------------------------------- SSH and vault
    public string TerminalAndShell { get; set; } = "Terminal & shell";

    public string TerminalPreferencesAccessibleName { get; set; } =
        "SSH terminal and shell preferences";

    public string TerminalPreferencesHint { get; set; } =
        "Defaults for new SSH profiles and terminal sessions.";

    public string TerminalType { get; set; } = "Terminal type (TERM)";

    public string TerminalTypeAccessibleName { get; set; } = "SSH terminal type";

    public string TerminalTypeHint { get; set; } =
        "Advertised to the server, for example xterm-256color or vt220.";

    public string StartupShell { get; set; } = "Startup shell / command";

    public string StartupShellAccessibleName { get; set; } = "SSH startup shell or command";

    public string StartupShellPlaceholder { get; set; } =
        "Server default (examples: bash -l, zsh -l, pwsh -NoLogo)";

    public string KeepAliveInterval { get; set; } = "Keepalive interval (seconds)";

    public string KeepAliveIntervalAccessibleName { get; set; } = "SSH keepalive interval in seconds";

    public string FontFamily { get; set; } = "Font family";

    public string FontFamilyAccessibleName { get; set; } = "SSH terminal font family";

    public string FontFamilyHint { get; set; } = "A monospaced font is recommended.";

    public string FontSize { get; set; } = "Font size (points)";

    public string FontSizeAccessibleName { get; set; } = "SSH terminal font size";

    public string ScrollbackLines { get; set; } = "Scrollback lines";

    public string ScrollbackLinesAccessibleName { get; set; } = "SSH terminal scrollback lines";

    public string OutputRefresh { get; set; } = "Output poll interval (milliseconds)";

    public string OutputRefreshAccessibleName { get; set; } =
        "SSH terminal output refresh interval in milliseconds";

    /// <remarks>
    /// This is how often the agent is asked for new output, not how often the terminal repaints.
    /// Painting has its own cap, so lowering this shortens the delay before a keystroke echoes
    /// back rather than making the screen redraw more often.
    /// </remarks>
    public string OutputRefreshHint { get; set; } =
        "How often the agent is asked for new output. Lower feels more responsive; higher uses "
        + "fewer resources.";

    public string ShowFavoritesInFolders { get; set; } = "Show favourites in their folders too";

    public string ShowFavoritesInFoldersHint { get; set; } =
        "A favourite is listed under Favourites and again where it actually lives. Turn this off "
        + "to list it only under Favourites.";

    public string RenderBoldText { get; set; } = "Render ANSI bold text";

    public string RenderBoldTextHint { get; set; } =
        "Uses a bold terminal font for server output which requests the ANSI bold attribute.";

    public string DefaultPrivateKey { get; set; } = "Default private key";

    public string NoDefaultPrivateKey { get; set; } = "No default SSH private key";

    public string PrivateKeyAccessibleDescription { get; set; } =
        "Opaque encrypted-vault reference; the key file path and contents are not saved in settings.";

    public string ImportKey { get; set; } = "Import key…";

    public string ClearDefault { get; set; } = "Clear default";

    public string SelectPrivateKeyTitle { get; set; } =
        "Select an encrypted OpenSSH or PEM private key";

    /// <summary>A file-dialog filter. The descriptions translate; the patterns must not.</summary>
    public string PrivateKeyFilter { get; set; } =
        "SSH private keys (*.key;*.pem)|*.key;*.pem|All files (*.*)|*.*";

    public string PrivateKeyUnavailable { get; set; } =
        "The selected key is unavailable, redirected, empty, or too large.";

    public string StoredInVault { get; set; } = "Stored securely in the encrypted vault.";

    // -------------------------------------------------------- provider defaults
    public string BasicDefaults { get; set; } = "Basic defaults";

    public string AdvancedBehavior { get; set; } = "Advanced connection behavior";

    public string NoSettingsRequired { get; set; } = "No settings are required.";

    public string NoReusableDefaults { get; set; } =
        "This provider has no reusable basic defaults. Its root path remains specific to each connection.";

    public string UnknownControl { get; set; } = "Unknown connection-default control.";

    public string ConnectionTimeout { get; set; } = "Connection timeout (seconds)";

    public string OperationTimeout { get; set; } = "Operation timeout (seconds)";

    public string RetryAttempts { get; set; } = "Retry attempts";

    public string RetriesUnsupported { get; set; } =
        "Automatic retries are not supported for this provider.";

    public string OperationTimeoutHint { get; set; } = "Uses the connection timeout for this provider.";

    public string AuthenticationModeHint { get; set; } =
        "Selects the authentication fields shown for new profiles.";

    /// <summary>{0} = the provider's display name.</summary>
    public string ProviderDefaultsFormat { get; set; } = "{0} defaults";

    /// <summary>{0} = the provider's display name.</summary>
    public string ProviderNewConnectionDefaultsFormat { get; set; } = "{0} new-connection defaults";

    /// <summary>{0} = the provider's display name.</summary>
    public string ProviderDefaultsDescriptionFormat { get; set; } = "Defaults for new {0} profiles.";

    /// <summary>{0} = the provider's display name.</summary>
    public string ConfigureProviderFormat { get; set; } = "Configure {0} connection";

    /// <summary>{0} = the provider's display name.</summary>
    public string CreateProviderFormat { get; set; } = "Create a {0} connection...";

    /// <summary>{0} = the field's label in lower case.</summary>
    public string DefaultFieldFormat { get; set; } = "Default {0}";

    /// <summary>{0} = the card's title.</summary>
    public string SettingsCardFormat { get; set; } = "{0} settings card";

    /// <summary>{0} = the provider summary, {1} = its default port or a dash.</summary>
    public string ProviderSummaryFormat { get; set; } = "{0} Default port: {1}";

    public string PageExternalEditing { get; set; } = "External editing";

    public string StartWith { get; set; } = "Start with";

    public string StartingConcurrencyAccessibleName { get; set; } = "Starting concurrency";

    public string AdaptiveConcurrencyHint { get; set; } =
        "Starts at the minimum, increases after sustained healthy throughput, and backs off on slowdown or provider errors.";

    public string WorkspacePresetHint { get; set; } =
        "The same arrangements the New Workspace chooser offers. \"Ask every time\" shows that chooser, which is also where you can tell StorageHub to stop asking; pick an arrangement here to change or undo that choice.";

    public string NotApplicable { get; set; } = "Not applicable";
    public string ThemeLight { get; set; } = "Light";

    public string ThemeDark { get; set; } = "Dark";

    public string ThemeSystem { get; set; } = "Follow Windows";

    public string Language { get; set; } = "Language";

    public string LanguageAccessibleName { get; set; } = "Application language";

    public string LanguageHint { get; set; } =
        "StorageHub offers to restart when you apply this, because every window reads its text as it is built.";

    /// <summary>The "follow Windows" entry in the language list.</summary>
    public string LanguageAutomatic { get; set; } = "Same as Windows";

    // ------------------------------------------------------- shortcuts editor
    public string ShortcutsAccessibleName { get; set; } = "Keyboard shortcut assignments";

    public string ShortcutsGridAccessibleName { get; set; } = "Commands and shortcuts";

    public string ShortcutColumnCommand { get; set; } = "Command";

    public string ShortcutColumnShortcut { get; set; } = "Shortcut";

    public string ShortcutColumnDefault { get; set; } = "Default";

    /// <summary>{0} = the menu the command sits in, {1} = the command's label.</summary>
    public string ShortcutCommandLabelFormat { get; set; } = "{0}: {1}";

    public string ShortcutCaptureAccessibleName { get; set; } = "Press a new shortcut";

    public string ShortcutCapturePlaceholder { get; set; } = "Press shortcut keys";

    public string ShortcutAssign { get; set; } = "Assign";

    public string ShortcutClear { get; set; } = "Clear";

    public string ShortcutRestoreDefaults { get; set; } = "Restore defaults";

    public string ShortcutStatusAccessibleName { get; set; } = "Shortcut assignment status";

    public string ShortcutSelectCommandHint { get; set; } =
        "Select a command, press the new keys, then choose Assign.";

    public string ShortcutUpdatedHint { get; set; } = "Shortcut updated. Choose Apply or OK to save.";

    /// <summary>Shown in the shortcut column when a command has no keys bound to it.</summary>
    public string ShortcutUnassigned { get; set; } = "Unassigned";

    // ----------------------------------------------------------- import wizard
    public string ImportOpen { get; set; } = "Open";

    public string ImportBack { get; set; } = "Back";
}
