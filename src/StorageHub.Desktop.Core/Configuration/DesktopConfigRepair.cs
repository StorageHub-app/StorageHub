using StorageHub.Desktop.Localization;
using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop.Configuration;

/// <summary>
/// The bounds every desktop setting has to satisfy, and the repair that brings an out-of-range one
/// back inside them.
/// </summary>
/// <remarks>
/// <para>
/// This is where validation lives now that the config models cannot carry DataAnnotations -- see
/// <see cref="DesktopConfig"/> for why. <see cref="Repair"/> clamps and never throws, because it
/// runs on a file a user may have hand-edited and a bad number there must not stop the
/// application starting. <see cref="Validate"/> rejects and is what the Settings window and the
/// settings importer call, because at those two points there is someone to tell.
/// </para>
/// <para>
/// Both read the same constants, so a value the dialog refuses to save is never one the loader
/// would have quietly accepted.
/// </para>
/// </remarks>
internal static class DesktopConfigRepair
{
    /// <summary>
    /// Whether <see cref="Repair"/> actually changed anything.
    /// </summary>
    /// <remarks>
    /// Not <c>repaired != loaded</c>. <see cref="DesktopUpdatePreferences"/> is a record holding
    /// four collections, and record equality compares members with
    /// <c>EqualityComparer&lt;T&gt;.Default</c> — reference equality for a dictionary or a list.
    /// Repair rebuilds all four unconditionally, so any settings file with shortcuts, connection
    /// defaults or a recent workspace reported itself repaired on every launch with no value
    /// having changed, and the startup warning became something to ignore.
    ///
    /// The scalars are still compared by record equality, with the collections held aside so they
    /// cannot drag the result to "different" on identity alone.
    /// </remarks>
    internal static bool ChangedAnything(
        DesktopUpdatePreferences loaded,
        DesktopUpdatePreferences repaired)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(repaired);

        if (WithoutCollections(loaded) != WithoutCollections(repaired))
        {
            return true;
        }

        return !SameDictionary(loaded.ConnectionDefaults, repaired.ConnectionDefaults) ||
            !SameDictionary(loaded.Shortcuts, repaired.Shortcuts) ||
            !SameList(loaded.PinnedWorkspaces, repaired.PinnedWorkspaces) ||
            !SameList(loaded.RecentWorkspaces, repaired.RecentWorkspaces);
    }

    private static DesktopUpdatePreferences WithoutCollections(DesktopUpdatePreferences preferences) =>
        preferences with
        {
            ConnectionDefaults = null,
            Shortcuts = null,
            PinnedWorkspaces = null,
            RecentWorkspaces = null
        };

    private static bool SameDictionary<TValue>(
        IReadOnlyDictionary<string, TValue>? left,
        IReadOnlyDictionary<string, TValue>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var value) ||
                !EqualityComparer<TValue>.Default.Equals(pair.Value, value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameList<T>(IReadOnlyList<T>? left, IReadOnlyList<T>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return left is not null && right is not null && left.SequenceEqual(right);
    }

    /// <summary>
    /// Brings every value inside its bounds, replacing anything unusable with its default.
    /// </summary>
    internal static DesktopUpdatePreferences Repair(DesktopUpdatePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var defaults = DesktopUpdatePreferences.Defaults;
        var concurrencyIsValid =
            preferences.MinimumConcurrency is >= 1 and <= 8 &&
            preferences.MaximumTransferConcurrency is >= 1 and <= 32 &&
            preferences.MinimumConcurrency <= preferences.MaximumTransferConcurrency &&
            preferences.PerConnectionConcurrency is >= 1 and <= 16 &&
            preferences.MaximumSyncConcurrency is >= 1 and <= 8 &&
            preferences.MinimumConcurrency <= preferences.MaximumSyncConcurrency;

        return preferences with
        {
            SshHostKeyDiscovery = Enum.IsDefined(preferences.SshHostKeyDiscovery)
                ? preferences.SshHostKeyDiscovery
                : defaults.SshHostKeyDiscovery,
            ExternalEditorPath = IsValidEditorPath(preferences.ExternalEditorPath)
                ? preferences.ExternalEditorPath
                : null,
            MaximumEditableFileBytes =
                preferences.MaximumEditableFileBytes is >= 1 and <= EditableFileIpcContract.MaximumContentBytes
                    ? preferences.MaximumEditableFileBytes
                    : EditableFileIpcContract.MaximumContentBytes,
            AdaptiveConcurrency = concurrencyIsValid ? preferences.AdaptiveConcurrency : defaults.AdaptiveConcurrency,
            MinimumConcurrency = concurrencyIsValid ? preferences.MinimumConcurrency : defaults.MinimumConcurrency,
            MaximumTransferConcurrency = concurrencyIsValid
                ? preferences.MaximumTransferConcurrency
                : defaults.MaximumTransferConcurrency,
            PerConnectionConcurrency = concurrencyIsValid
                ? preferences.PerConnectionConcurrency
                : defaults.PerConnectionConcurrency,
            MaximumSyncConcurrency = concurrencyIsValid
                ? preferences.MaximumSyncConcurrency
                : defaults.MaximumSyncConcurrency,
            TotalUploadBytesPerSecond = DesktopUpdatePreferences.ValidSpeedLimit(preferences.TotalUploadBytesPerSecond),
            TotalDownloadBytesPerSecond = DesktopUpdatePreferences.ValidSpeedLimit(preferences.TotalDownloadBytesPerSecond),
            Appearance = Enum.IsDefined(preferences.Appearance) ? preferences.Appearance : DesktopAppearance.System,
            ConnectionDefaults = preferences.ConnectionDefaults is null
                ? null
                : ConnectionDefaultSettings.Normalize(preferences.ConnectionDefaults),
            DefaultWorkspaceLayout = Enum.IsDefined(preferences.DefaultWorkspaceLayout)
                ? preferences.DefaultWorkspaceLayout
                : WorkspaceLayout.SideBySide,
            SshTerminal = preferences.SshTerminal is null
                ? null
                : SshTerminalPreferences.Resolve(preferences.SshTerminal),
            Shortcuts = preferences.Shortcuts is null ? null : ShortcutBindings.Resolve(preferences.Shortcuts),
            PinnedWorkspaces = preferences.PinnedWorkspaces is null
                ? null
                : WorkspaceShortcutSettings.Resolve(preferences.PinnedWorkspaces, WorkspaceShortcutSettings.MaximumPinned),
            RecentWorkspaces = preferences.RecentWorkspaces is null
                ? null
                : WorkspaceShortcutSettings.Resolve(preferences.RecentWorkspaces, WorkspaceShortcutSettings.MaximumRecent),
            DefaultWorkspacePaneCount =
                preferences.DefaultWorkspacePaneCount is >= 1 and <= WorkspaceLayoutModel.MaximumPanes
                    ? preferences.DefaultWorkspacePaneCount
                    : null,
            ConnectionsPanelWidth = preferences.ConnectionsPanelWidth is
                >= DesktopUpdatePreferences.MinimumConnectionsPanelWidth and
                <= DesktopUpdatePreferences.MaximumConnectionsPanelWidth
                    ? preferences.ConnectionsPanelWidth
                    : DesktopUpdatePreferences.DefaultConnectionsPanelWidth,
            ConnectionsPanelSide = Enum.IsDefined(preferences.ConnectionsPanelSide)
                ? preferences.ConnectionsPanelSide
                : ConnectionsPanelSide.Left,
            Language = DesktopCulture.IsSupportedSetting(preferences.Language)
                ? preferences.Language
                : DesktopCulture.AutomaticLanguage
        };
    }

    /// <summary>
    /// Reports why these preferences cannot be persisted, or null when they are acceptable.
    ///
    /// Separate from <see cref="Save"/> so that settings arriving from outside the app — an
    /// imported export file — are judged by exactly the same rules as settings the dialog writes.
    /// Two copies of these bounds would eventually disagree, and the import is the one that would
    /// be wrong.
    /// </summary>
    internal static string? Validate(DesktopUpdatePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (preferences.Shortcuts is not null && ShortcutBindings.Validate(preferences.Shortcuts) is { } shortcutError)
            return shortcutError;
        if (WorkspaceShortcutSettings.Validate(
                preferences.PinnedWorkspaces, WorkspaceShortcutSettings.MaximumPinned) is { } pinnedError)
            return pinnedError;
        if (WorkspaceShortcutSettings.Validate(
                preferences.RecentWorkspaces, WorkspaceShortcutSettings.MaximumRecent) is { } recentError)
            return recentError;
        if (preferences.DefaultWorkspacePaneCount is { } paneCount &&
            paneCount is < 1 or > WorkspaceLayoutModel.MaximumPanes)
        {
            return $"A workspace can have between 1 and {WorkspaceLayoutModel.MaximumPanes} panes.";
        }

        if (preferences.ConnectionsPanelWidth is
            < DesktopUpdatePreferences.MinimumConnectionsPanelWidth or
            > DesktopUpdatePreferences.MaximumConnectionsPanelWidth)
        {
            return "The connections panel must be between " +
                $"{DesktopUpdatePreferences.MinimumConnectionsPanelWidth} and " +
                $"{DesktopUpdatePreferences.MaximumConnectionsPanelWidth} pixels wide.";
        }

        if (!Enum.IsDefined(preferences.ConnectionsPanelSide))
        {
            return "The connections panel must be docked to the left or the right.";
        }

        return !IsValidEditorPath(preferences.ExternalEditorPath) ||
            preferences.MaximumEditableFileBytes is < 1 or > EditableFileIpcContract.MaximumContentBytes ||
            preferences.MinimumConcurrency is < 1 or > 8 ||
            preferences.MaximumTransferConcurrency is < 1 or > 32 ||
            preferences.MinimumConcurrency > preferences.MaximumTransferConcurrency ||
            preferences.PerConnectionConcurrency is < 1 or > 16 ||
            preferences.MaximumSyncConcurrency is < 1 or > 8 ||
            preferences.MinimumConcurrency > preferences.MaximumSyncConcurrency ||
            DesktopUpdatePreferences.ValidSpeedLimit(preferences.TotalUploadBytesPerSecond) != preferences.TotalUploadBytesPerSecond ||
            DesktopUpdatePreferences.ValidSpeedLimit(preferences.TotalDownloadBytesPerSecond) != preferences.TotalDownloadBytesPerSecond ||
            !Enum.IsDefined(preferences.Appearance)
                ? "External editor preferences exceed the permitted bounds."
                : null;
    }

    /// <summary>
    /// Whether an external editor path is storable. Internal so the settings importer applies the
    /// same rule rather than a lookalike of its own.
    /// </summary>
    internal static bool IsValidEditorPath(string? value) => value is null ||
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 2_048 &&
        !value.Any(char.IsControl) &&
        Path.IsPathFullyQualified(value);
}
