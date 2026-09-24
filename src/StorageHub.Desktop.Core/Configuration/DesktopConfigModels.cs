using StorageHub.Desktop.Localization;
using CodeLogic.Core.Configuration;
using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop.Configuration;

/// <summary>
/// One connections-panel group as it is written down.
/// </summary>
/// <remarks>
/// Members are ids as text rather than Guids, because a settings file that somebody has edited by
/// hand should lose one bad entry rather than fail to load at all.
/// </remarks>
public sealed class DesktopConnectionGroup
{
    public string Name { get; set; } = string.Empty;

    public List<string>? Members { get; set; }
}

/// <summary>Schema versions written by this application, as opposed to the legacy ladder.</summary>
internal static class DesktopConfigSchema
{
    /// <summary>
    /// The version stamped into every file this application writes. It starts again at 1 because
    /// the fifteen versions before it describe a different file with a different shape; see
    /// <see cref="Legacy.LegacySettingsReader"/>.
    /// </summary>
    internal const int Current = 1;
}

/// <summary>
/// General desktop settings: <c>config.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Validate"/> is deliberately not implemented, and no DataAnnotations appear on any
/// property. CodeLogic turns a failed validation into an exception out of <c>ConfigureAsync</c>,
/// which would mean a hand-edited number stopping StorageHub from starting at all. Every bound
/// these values have is enforced by <see cref="DesktopConfigRepair"/> instead, which clamps rather
/// than throws. The bounds are not weaker for living there -- they are applied to imported files
/// too, which annotations never were.
/// </para>
/// </remarks>
internal sealed class DesktopConfig : ConfigModelBase
{
    public int SchemaVersion { get; set; } = DesktopConfigSchema.Current;

    public bool CheckAutomatically { get; set; } = true;

    public bool DownloadAutomatically { get; set; } = true;

    public bool RestartAutomatically { get; set; }

    public bool IncludePrereleases { get; set; } = true;

    public SshHostKeyDiscoveryMode SshHostKeyDiscovery { get; set; } = SshHostKeyDiscoveryMode.AskBeforeFetching;

    public string? ExternalEditorPath { get; set; }

    public int MaximumEditableFileBytes { get; set; } = EditableFileIpcContract.MaximumContentBytes;

    public bool AdaptiveConcurrency { get; set; } = true;

    public int MinimumConcurrency { get; set; } = 1;

    public int MaximumTransferConcurrency { get; set; } = 4;

    public int PerConnectionConcurrency { get; set; } = 2;

    public int MaximumSyncConcurrency { get; set; } = 2;

    public DesktopAppearance Appearance { get; set; } = DesktopAppearance.System;

    /// <summary>
    /// Which language the shell is presented in: <c>auto</c> to follow Windows, or one of the
    /// cultures in <see cref="DesktopCulture.SupportedCultures"/>.
    /// </summary>
    public string Language { get; set; } = DesktopCulture.AutomaticLanguage;

    public bool WarnBeforeUnsafeExternalEdit { get; set; } = true;

    public Dictionary<string, string>? ConnectionDefaults { get; set; }

    public WorkspaceLayout DefaultWorkspaceLayout { get; set; } = WorkspaceLayout.SideBySide;

    public DesktopSshTerminalConfig? SshTerminal { get; set; }

    public bool ReconnectRemotePanesAutomatically { get; set; } = true;

    public bool ConfirmBeforeClearingTransferHistory { get; set; } = true;

    public bool ConfirmBeforeDeletingItems { get; set; } = true;

    public int? DefaultWorkspacePaneCount { get; set; }

    public int ConnectionsPanelWidth { get; set; } = DesktopUpdatePreferences.DefaultConnectionsPanelWidth;

    public bool ConnectionsPanelVisible { get; set; } = true;

    public ConnectionsPanelSide ConnectionsPanelSide { get; set; } = ConnectionsPanelSide.Left;

    /// <summary>
    /// The main toolbar's buttons as command ids, with "|" for a divider. Absent means the
    /// default preset.
    /// </summary>
    public List<string>? ToolbarItems { get; set; }

    /// <summary>The connections panel's groups, in the order they are shown.</summary>
    public List<DesktopConnectionGroup>? ConnectionGroups { get; set; }

    public ToolbarLabelStyle ToolbarLabels { get; set; } = ToolbarLabelStyle.IconsOnly;

    /// <summary>
    /// Speed limits shared by every transfer, in bytes per second; absent is no limit. Read by the
    /// agent as well as the desktop, under these names.
    /// </summary>
    public long? TotalUploadBytesPerSecond { get; set; }

    public long? TotalDownloadBytesPerSecond { get; set; }
}

/// <summary>
/// The SSH terminal block of <c>config.json</c>. A plain class rather than the
/// <see cref="SshTerminalPreferences"/> record because CodeLogic deserializes config models with
/// property setters and no constructor binding.
/// </summary>
internal sealed class DesktopSshTerminalConfig
{
    public string TerminalName { get; set; } = SshTerminalPreferences.Defaults.TerminalName;

    public string? StartupCommand { get; set; }

    public int KeepAliveSeconds { get; set; } = SshTerminalPreferences.Defaults.KeepAliveSeconds;

    public string FontFamily { get; set; } = SshTerminalPreferences.Defaults.FontFamily;

    public float FontSize { get; set; } = SshTerminalPreferences.Defaults.FontSize;

    public int ScrollbackLines { get; set; } = SshTerminalPreferences.Defaults.ScrollbackLines;

    public int RefreshIntervalMilliseconds { get; set; } = SshTerminalPreferences.Defaults.RefreshIntervalMilliseconds;

    public bool RenderBoldText { get; set; } = SshTerminalPreferences.Defaults.RenderBoldText;

    internal SshTerminalPreferences ToPreferences() => SshTerminalPreferences.Resolve(
        new SshTerminalPreferences(
            TerminalName,
            StartupCommand,
            KeepAliveSeconds,
            FontFamily,
            FontSize,
            ScrollbackLines,
            RefreshIntervalMilliseconds,
            RenderBoldText));

    internal static DesktopSshTerminalConfig From(SshTerminalPreferences preferences)
    {
        var resolved = SshTerminalPreferences.Resolve(preferences);
        return new DesktopSshTerminalConfig
        {
            TerminalName = resolved.TerminalName,
            StartupCommand = resolved.StartupCommand,
            KeepAliveSeconds = resolved.KeepAliveSeconds,
            FontFamily = resolved.FontFamily,
            FontSize = resolved.FontSize,
            ScrollbackLines = resolved.ScrollbackLines,
            RefreshIntervalMilliseconds = resolved.RefreshIntervalMilliseconds,
            RenderBoldText = resolved.RenderBoldText
        };
    }
}

/// <summary>
/// Keyboard shortcut overrides: <c>config.shortcuts.json</c>.
/// </summary>
/// <remarks>
/// Split into its own file so that rebinding a key cannot put every other setting at risk, and so
/// a user who wants to reset their shortcuts can delete one file rather than hand-edit a large one.
/// The values are chord strings, never <see cref="Keys"/>: see <see cref="ShortcutChord"/>.
/// </remarks>
internal sealed class DesktopShortcutsConfig : ConfigModelBase
{
    public int SchemaVersion { get; set; } = DesktopConfigSchema.Current;

    public Dictionary<string, string>? Shortcuts { get; set; }
}

/// <summary>
/// Pinned and recent workspaces: <c>config.workspaces.json</c>.
/// </summary>
/// <remarks>
/// The only part of the desktop's settings that grows with use, which is why it is kept away from
/// the rest: it is the list most likely to reach a size limit, and losing it costs a user nothing
/// but convenience.
/// </remarks>
internal sealed class DesktopWorkspacesConfig : ConfigModelBase
{
    public int SchemaVersion { get; set; } = DesktopConfigSchema.Current;

    public List<DesktopWorkspaceEntry>? Pinned { get; set; }

    public List<DesktopWorkspaceEntry>? Recent { get; set; }
}

/// <summary>One remembered workspace file.</summary>
internal sealed class DesktopWorkspaceEntry
{
    public string Path { get; set; } = string.Empty;

    public string? Name { get; set; }

    public DateTimeOffset LastOpenedUtc { get; set; }

    internal WorkspaceShortcutEntry ToEntry() => new(Path, Name, LastOpenedUtc);

    internal static DesktopWorkspaceEntry From(WorkspaceShortcutEntry entry) => new()
    {
        Path = entry.Path,
        Name = entry.Name,
        LastOpenedUtc = entry.LastOpenedUtc
    };
}
