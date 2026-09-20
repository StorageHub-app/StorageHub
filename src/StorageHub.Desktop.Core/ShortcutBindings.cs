using Avalonia.Input;
using StorageHub.Desktop.Configuration;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// Which shortcut runs which command, and whether a proposed binding is allowed.
/// </summary>
/// <remarks>
/// Policy rather than presentation, and shared: config repair, the settings page and dispatch all
/// have to agree about what a valid binding is and what the effective table looks like. It lived in
/// the WinForms shell as ShortcutSettings and spoke that framework's key enum, so config repair had
/// to round-trip every table through it to validate one.
/// </remarks>
internal static class ShortcutBindings
{
    /// <summary>
    /// The rebindable commands, resolved for the current language.
    /// </summary>
    /// <remarks>
    /// Deliberately a property rather than a cached array: the labels follow the current language,
    /// and caching them at type-initialization time would pin them to whatever language happened to
    /// be active when this type was first touched. The filter runs on the id, which does not move.
    /// </remarks>
    internal static IReadOnlyList<UiCommandDefinition> Commands =>
        [.. UiCommandCatalog.Definitions.Where(command => UiCommandCatalog.IsAvailable(command.Id))];

    /// <summary>
    /// The effective table: the catalog's defaults with the user's overrides applied.
    /// </summary>
    /// <remarks>
    /// An invalid table is discarded whole rather than partly, because a half-applied set of
    /// rebindings is a state the user never chose and cannot reason about.
    /// </remarks>
    internal static Dictionary<string, KeyGesture?> Resolve(
        IReadOnlyDictionary<string, KeyGesture?>? overrides)
    {
        var commands = Commands;
        var result = commands.ToDictionary(
            command => command.Id,
            command => command.Shortcut,
            StringComparer.Ordinal);

        if (overrides is null) return result;

        foreach (var command in commands)
        {
            if (overrides.TryGetValue(command.Id, out var gesture)) result[command.Id] = gesture;
        }

        return Validate(result) is null ? result : Resolve(null);
    }

    /// <summary>
    /// Whether a chord can be bound at all.
    /// </summary>
    /// <remarks>
    /// A bare letter would fire while typing, a modifier alone can never fire, and Alt+F4 and
    /// Ctrl+Alt+Delete belong to the system. What is left is a chord carrying Ctrl or Alt, a
    /// function key, or Delete.
    /// </remarks>
    internal static bool IsValid(KeyGesture? gesture)
    {
        if (gesture is null) return true;

        var key = gesture.Key;
        if (!Enum.IsDefined(key) || key is Key.None
            or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin)
        {
            return false;
        }

        // Meta is not offered: it is the system's on every platform StorageHub runs on.
        if ((gesture.KeyModifiers & ~(KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt)) != 0)
        {
            return false;
        }

        if (gesture.KeyModifiers == KeyModifiers.Alt && key == Key.F4) return false;
        if (gesture.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Alt) && key == Key.Delete) return false;

        return (gesture.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0
            || key is >= Key.F1 and <= Key.F24
            || key == Key.Delete;
    }

    /// <summary>Why a table cannot be used, or null when it can.</summary>
    internal static string? Validate(IReadOnlyDictionary<string, KeyGesture?> shortcuts)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        var assigned = new Dictionary<KeyGesture, string>();

        foreach (var command in Commands)
        {
            var gesture = shortcuts.GetValueOrDefault(command.Id, command.Shortcut);
            if (!IsValid(gesture))
            {
                return Ui.Format(Ui.Validation.ChooseCtrlAltWithAKeyFormat, command.Label);
            }

            if (gesture is null) continue;

            if (assigned.TryGetValue(gesture, out var other))
            {
                return Ui.Format(
                    Ui.Validation.IsAlreadyAssignedToClearThatFormat,
                    ShortcutChord.Format(gesture),
                    other);
            }

            assigned.Add(gesture, command.Label);
        }

        return null;
    }

    /// <summary>
    /// Whether a command acts on the active browser pane.
    /// </summary>
    /// <remarks>
    /// Pane commands are declined when there is no pane, and yield to a remote shell that owns the
    /// keystroke. Both shells apply the same rule through ShellCommandRouter.
    /// </remarks>
    internal static bool IsPaneCommand(UiCommandDefinition command) =>
        UiCommandCatalog.IsPaneCommand(command);
}
