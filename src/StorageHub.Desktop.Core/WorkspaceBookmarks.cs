using StorageHub.Desktop.Configuration;
using StorageHub.Desktop.Framework;

namespace StorageHub.Desktop;

/// <summary>
/// The pinned and recent workspace files, as the desktop settings hold them.
/// </summary>
/// <remarks>
/// <para>
/// What Welcome's workspace list and the Workspace menu read, and what saving and opening a
/// workspace write. The rules for the lists themselves are <see cref="WorkspaceShortcutSettings"/>'s;
/// this is only where they are kept, so every route that opens a workspace records it the same way.
/// </para>
/// <para>
/// The settings are read again before every change rather than cached, because the Settings dialog
/// writes the same files, and a list held here would put back whatever it had changed.
/// </para>
/// </remarks>
internal sealed class WorkspaceBookmarks(
    Func<DesktopUpdatePreferences> load,
    Action<DesktopUpdatePreferences> save)
{
    private readonly Func<DesktopUpdatePreferences> _load = load ?? throw new ArgumentNullException(nameof(load));
    private readonly Action<DesktopUpdatePreferences> _save = save ?? throw new ArgumentNullException(nameof(save));

    /// <summary>The settings file of whoever is running the shell.</summary>
    internal static WorkspaceBookmarks ForCurrentUser() => new(
        static () => Store().Load(),
        static preferences => Store().Save(preferences));

    /// <summary>Raised after either list changed, so whatever draws them can draw them again.</summary>
    internal event EventHandler? Changed;

    /// <summary>The recent workspaces, most recently opened first.</summary>
    internal IReadOnlyList<WorkspaceShortcutEntry> Recent =>
        WorkspaceShortcutSettings.Resolve(Read()?.RecentWorkspaces, WorkspaceShortcutSettings.MaximumRecent);

    /// <summary>The pinned workspaces, the most recently pinned first.</summary>
    internal IReadOnlyList<WorkspaceShortcutEntry> Pinned =>
        WorkspaceShortcutSettings.Resolve(Read()?.PinnedWorkspaces, WorkspaceShortcutSettings.MaximumPinned);

    /// <summary>
    /// Both lists in the order Welcome and the Workspace menu draw them: pinned first, then the
    /// recent ones that are not also pinned.
    /// </summary>
    /// <remarks>
    /// A path is deliberately kept in both lists, so that unpinning does not also forget it was
    /// opened, and this is where the overlap collapses, as 1.x's MainForm did. Whether each file is
    /// still there is decided here, when the lists are drawn, and again when one is opened.
    /// </remarks>
    internal IReadOnlyList<WorkspaceShortcutView> Shortcuts
    {
        get
        {
            var preferences = Read();
            var pinned = WorkspaceShortcutSettings.Resolve(
                preferences?.PinnedWorkspaces, WorkspaceShortcutSettings.MaximumPinned);
            var views = new List<WorkspaceShortcutView>(pinned.Count);
            views.AddRange(pinned.Select(static entry => new WorkspaceShortcutView(
                entry, IsPinned: true, WorkspaceShortcutSettings.LooksPresent(entry.Path))));
            foreach (var entry in WorkspaceShortcutSettings.Resolve(
                preferences?.RecentWorkspaces, WorkspaceShortcutSettings.MaximumRecent))
            {
                if (WorkspaceShortcutSettings.Contains(pinned, entry.Path)) continue;
                views.Add(new WorkspaceShortcutView(
                    entry, IsPinned: false, WorkspaceShortcutSettings.LooksPresent(entry.Path)));
            }

            return views;
        }
    }

    /// <summary>Whether a workspace file is on the pinned list.</summary>
    internal bool IsPinned(string? path) => WorkspaceShortcutSettings.Contains(Read()?.PinnedWorkspaces, path);

    /// <summary>
    /// Pins a workspace, or unpins it when it already is.
    /// </summary>
    /// <remarks>
    /// Its recent entry is left alone either way: a path can be in both lists, and unpinning
    /// should not erase that it was opened.
    /// </remarks>
    internal void TogglePin(string path, string? name)
    {
        var pinned = IsPinned(path);
        Change(preferences => preferences with
        {
            PinnedWorkspaces = pinned
                ? WorkspaceShortcutSettings.Remove(
                    preferences.PinnedWorkspaces, path, WorkspaceShortcutSettings.MaximumPinned)
                : WorkspaceShortcutSettings.Promote(
                    preferences.PinnedWorkspaces,
                    new WorkspaceShortcutEntry(path, name, DateTimeOffset.UtcNow),
                    WorkspaceShortcutSettings.MaximumPinned)
        });
    }

    /// <summary>
    /// Moves a workspace to the front of the recent list, with the name it has now.
    /// </summary>
    /// <remarks>
    /// The name is refreshed so a renamed workspace does not keep its old label. The pinned list
    /// is left alone: a path can be in both, and unpinning should not erase the recent entry.
    /// </remarks>
    internal void RecordOpened(string path, string name)
    {
        var current = Read()?.RecentWorkspaces;
        if (current is { Count: > 0 } &&
            string.Equals(current[0].Path, path, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(current[0].Name, name, StringComparison.Ordinal))
        {
            // Already at the front with the same label, so Ctrl+S does not rewrite the settings.
            return;
        }

        Change(preferences => preferences with
        {
            RecentWorkspaces = WorkspaceShortcutSettings.Promote(
                preferences.RecentWorkspaces,
                new WorkspaceShortcutEntry(path, name, DateTimeOffset.UtcNow),
                WorkspaceShortcutSettings.MaximumRecent)
        });
    }

    /// <summary>
    /// Drops a workspace from both lists: for a file that has gone, and for Welcome's "Remove from
    /// list", which leaves the file itself where it is.
    /// </summary>
    internal void Forget(string path) => Change(preferences => preferences with
    {
        PinnedWorkspaces = WorkspaceShortcutSettings.Remove(
            preferences.PinnedWorkspaces, path, WorkspaceShortcutSettings.MaximumPinned),
        RecentWorkspaces = WorkspaceShortcutSettings.Remove(
            preferences.RecentWorkspaces, path, WorkspaceShortcutSettings.MaximumRecent)
    });

    /// <summary>
    /// Applies a change to the settings, or quietly does not.
    /// </summary>
    /// <remarks>
    /// Losing a bookmark must never break the save or the open that caused it: the settings file
    /// can be locked or read-only, and saving validates and throws.
    /// </remarks>
    private void Change(Func<DesktopUpdatePreferences, DesktopUpdatePreferences> change)
    {
        try
        {
            _save(change(_load()));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidDataException or ArgumentException)
        {
            return;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private DesktopUpdatePreferences? Read()
    {
        try
        {
            return _load();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    private static DesktopConfigStore Store() =>
        new(DesktopFrameworkPaths.Resolve().ApplicationRoot);
}
