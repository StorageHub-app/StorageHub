namespace StorageHub.Desktop;

/// <summary>How toolbar buttons are labelled.</summary>
public enum ToolbarLabelStyle
{
    /// <summary>Icon only, with the label as a tooltip. How the toolbar has always looked.</summary>
    IconsOnly = 0,

    /// <summary>Icon with its label beside it. Fewer buttons fit, but none need a hover to read.</summary>
    IconsAndText = 1,

    /// <summary>Icon with its label underneath, which keeps buttons square and still readable.</summary>
    TextUnderIcon = 2,
}

/// <summary>The built-in layouts a toolbar can be reset to.</summary>
public enum ToolbarPreset
{
    /// <summary>
    /// The toolbar as it shipped: the commands used constantly, and nothing else. Chosen as the
    /// default because a toolbar holding every command is a worse toolbar, not a better one.
    /// </summary>
    Essential = 0,

    /// <summary>
    /// Essential plus the commands people reach for often enough to resent a menu trip: creating
    /// folders, the tree and queue panels, hidden files, connecting and disconnecting, pausing
    /// transfers, sync profiles and checksums.
    /// </summary>
    Expanded = 1,
}

/// <summary>
/// The toolbar's contents: an ordered list of command ids, with <see cref="Separator"/> standing
/// for a divider.
///
/// Stored as command ids rather than as indices or labels because ids are the one thing that is
/// already stable across releases and languages -- the same reason the shortcut editor persists
/// them. An id that no longer exists is dropped on load, so removing a command in a later version
/// cannot leave somebody with a toolbar that refuses to build.
/// </summary>
internal static class ToolbarLayout
{
    /// <summary>The entry that renders as a divider rather than a button.</summary>
    internal const string Separator = "|";

    /// <summary>Most toolbars hold a dozen or two; this is only here to bound a hostile file.</summary>
    internal const int MaximumItems = 100;

    private static readonly IReadOnlyList<string> EssentialItems =
    [
        UiCommandIds.WorkspaceNewWorkspace,
        UiCommandIds.WorkspaceOpenWorkspace,
        UiCommandIds.WorkspaceSaveWorkspace,
        Separator,
        UiCommandIds.GoBack,
        UiCommandIds.GoForward,
        UiCommandIds.GoUp,
        UiCommandIds.ViewRefresh,
        Separator,
        UiCommandIds.EditCopy,
        UiCommandIds.EditCut,
        UiCommandIds.EditPaste,
        UiCommandIds.EditRename,
        UiCommandIds.EditDelete,
        Separator,
        UiCommandIds.ViewConnectionsPanel,
        UiCommandIds.ConnectionsNewConnection,
        UiCommandIds.ConnectionsKeyStore,
        Separator,
        UiCommandIds.SyncComparePanes,
        UiCommandIds.SyncSchedules,
        Separator,
        UiCommandIds.ToolsSearch,
        UiCommandIds.ToolsSettings
    ];

    private static readonly IReadOnlyList<string> ExpandedItems =
    [
        UiCommandIds.WorkspaceNewWorkspace,
        UiCommandIds.WorkspaceOpenWorkspace,
        UiCommandIds.WorkspaceSaveWorkspace,
        Separator,
        UiCommandIds.GoBack,
        UiCommandIds.GoForward,
        UiCommandIds.GoUp,
        UiCommandIds.GoHome,
        UiCommandIds.ViewRefresh,
        Separator,
        UiCommandIds.EditNewFolder,
        UiCommandIds.EditNewEmptyFile,
        UiCommandIds.EditCopy,
        UiCommandIds.EditCut,
        UiCommandIds.EditPaste,
        UiCommandIds.EditRename,
        UiCommandIds.EditDelete,
        Separator,
        UiCommandIds.ViewConnectionsPanel,
        UiCommandIds.ViewDirectoryTree,
        UiCommandIds.ViewTransferQueue,
        UiCommandIds.ViewHiddenFiles,
        Separator,
        UiCommandIds.ConnectionsNewConnection,
        UiCommandIds.ConnectionsQuickConnect,
        UiCommandIds.ConnectionsDisconnect,
        UiCommandIds.ConnectionsKeyStore,
        Separator,
        UiCommandIds.TransferPauseAll,
        UiCommandIds.TransferResumeAll,
        Separator,
        UiCommandIds.SyncComparePanes,
        UiCommandIds.SyncSyncProfiles,
        UiCommandIds.SyncSchedules,
        Separator,
        UiCommandIds.ToolsSearch,
        UiCommandIds.ToolsChecksums,
        UiCommandIds.ToolsSettings
    ];

    /// <summary>The contents of a built-in preset.</summary>
    internal static IReadOnlyList<string> Preset(ToolbarPreset preset) => preset switch
    {
        ToolbarPreset.Expanded => ExpandedItems,
        _ => EssentialItems
    };

    /// <summary>What a toolbar shows when nothing has been customised.</summary>
    internal static IReadOnlyList<string> Default => EssentialItems;

    /// <summary>
    /// The layout to render, given what was stored. Null or empty means nothing was customised,
    /// which is not the same as an empty toolbar: somebody who removes every button gets the
    /// default back rather than a bar they cannot use to reach Settings and undo it.
    /// </summary>
    internal static IReadOnlyList<string> Resolve(IReadOnlyList<string>? stored)
    {
        var sanitised = Sanitise(stored);
        return sanitised.Count == 0 ? Default : sanitised;
    }

    /// <summary>
    /// Drops anything that is not a separator or a known command, collapses runs of separators,
    /// and trims separators from the ends, so a stored layout can never render as a leading
    /// divider or a gap where a removed command used to be.
    /// </summary>
    internal static IReadOnlyList<string> Sanitise(IReadOnlyList<string>? stored)
    {
        if (stored is null || stored.Count == 0)
        {
            return [];
        }

        var known = UiCommandCatalog.Specs.Select(static spec => spec.Id).ToHashSet(StringComparer.Ordinal);
        var cleaned = new List<string>(Math.Min(stored.Count, MaximumItems));
        foreach (var entry in stored.Take(MaximumItems))
        {
            var candidate = entry?.Trim();
            if (string.IsNullOrEmpty(candidate))
            {
                continue;
            }

            if (string.Equals(candidate, Separator, StringComparison.Ordinal))
            {
                if (cleaned.Count > 0 && cleaned[^1] != Separator)
                {
                    cleaned.Add(Separator);
                }

                continue;
            }

            // A command may legitimately disappear between releases; a saved layout naming it is
            // stale, not corrupt, so the entry goes and the rest survives.
            if (known.Contains(candidate) && !cleaned.Contains(candidate, StringComparer.Ordinal))
            {
                cleaned.Add(candidate);
            }
        }

        while (cleaned.Count > 0 && cleaned[^1] == Separator)
        {
            cleaned.RemoveAt(cleaned.Count - 1);
        }

        return cleaned;
    }

    /// <summary>True when the stored layout is one of the presets rather than a custom order.</summary>
    internal static bool Matches(IReadOnlyList<string> layout, ToolbarPreset preset) =>
        layout.SequenceEqual(Preset(preset), StringComparer.Ordinal);
}
