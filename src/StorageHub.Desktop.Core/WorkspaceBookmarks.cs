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

    /// <summary>The pinned workspaces, in the order they were pinned.</summary>
    internal IReadOnlyList<WorkspaceShortcutEntry> Pinned =>
        WorkspaceShortcutSettings.Resolve(Read()?.PinnedWorkspaces, WorkspaceShortcutSettings.MaximumPinned);

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

    /// <summary>Drops a workspace from both lists, for a file that has gone.</summary>
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
