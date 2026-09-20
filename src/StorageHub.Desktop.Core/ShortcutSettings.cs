using Avalonia.Input;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// Which commands can be rebound, what they are bound to, and whether a proposed binding is one a
/// shell can honour.
/// </summary>
/// <remarks>
/// This spoke <c>System.Windows.Forms.Keys</c> until the port, which is why it lived in the shell
/// rather than beside the catalog whose bindings it resolves. Nothing about it was ever about
/// WinForms: every rule here is about what a user may reasonably press, and the file it feeds
/// already stores a chord as text. It now names a <see cref="KeyGesture"/>, the same vocabulary
/// <see cref="UiCommandCatalog"/> and <c>config.shortcuts.json</c> use, so the conversion at the
/// edges disappears along with the shell that needed it.
/// </remarks>
internal static class ShortcutSettings
{
    /// <summary>
    /// The rebindable commands, resolved for the current language.
    /// </summary>
    /// <remarks>
    /// Deliberately a property rather than a cached array: the labels follow the current language,
    /// and caching them at type-initialization time would pin the menus to whatever language
    /// happened to be active when this type was first touched. The filter runs on the id, which
    /// does not move.
    /// </remarks>
    internal static IReadOnlyList<UiCommandDefinition> Commands =>
        [.. UiCommandCatalog.Definitions.Where(command => UiCommandCatalog.IsAvailable(command.Id))];

    /// <summary>
    /// Every rebindable command's binding: the catalog's default, with the user's overrides
    /// applied on top, or the defaults alone if that combination is not one this will accept.
    /// </summary>
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
            if (overrides.TryGetValue(command.Id, out var gesture)) result[command.Id] = gesture;
        return Validate(result) is null ? result : Resolve(null);
    }

    /// <summary>The chord as a menu shows it, in the current language when it shows nothing.</summary>
    /// <remarks>
    /// The text is <see cref="Configuration.ShortcutChord"/>'s, which is what the settings file
    /// holds. Using one spelling for both means a user reading their own settings.json sees the
    /// same <c>Ctrl+Shift+S</c> the menu showed them.
    /// </remarks>
    internal static string Format(KeyGesture? gesture) => gesture is null
        ? Ui.Settings.ShortcutUnassigned
        : Configuration.ShortcutChord.Format(gesture);

    /// <summary>
    /// Whether a chord is one a shell can actually deliver to a command.
    /// </summary>
    /// <remarks>
    /// Unbound is valid - that is how a user clears a shortcut. Beyond that the rules are: a real
    /// key rather than a modifier held on its own; no modifier the shell does not route, which on
    /// a Mac keyboard means Meta; not a chord the operating system takes first; and enough of a
    /// chord that it will not fire while someone is typing a filename, which means Ctrl or Alt
    /// unless it is a function key or Delete.
    /// </remarks>
    internal static bool IsValid(KeyGesture? gesture)
    {
        if (gesture is null) return true;

        var key = gesture.Key;
        var modifiers = gesture.KeyModifiers;
        if (!Enum.IsDefined(key) || key is Key.None or Key.LeftCtrl or Key.RightCtrl or
            Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return false;
        if ((modifiers & ~(KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt)) != 0)
            return false;
        // Alt+F4 closes the window and Ctrl+Alt+Delete never reaches an application at all, so a
        // command bound to either is a command with no shortcut and no way to tell.
        if (key == Key.F4 && modifiers == KeyModifiers.Alt) return false;
        if (key == Key.Delete && modifiers == (KeyModifiers.Control | KeyModifiers.Alt)) return false;
        return (modifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0 ||
            key is >= Key.F1 and <= Key.F24 ||
            (key == Key.Delete && modifiers == KeyModifiers.None);
    }

    /// <summary>Why this set of bindings cannot be used, or null when it can.</summary>
    internal static string? Validate(IReadOnlyDictionary<string, KeyGesture?> shortcuts)
    {
        var assigned = new Dictionary<KeyGesture, string>();
        foreach (var command in Commands)
        {
            var gesture = shortcuts.GetValueOrDefault(command.Id, command.Shortcut);
            if (!IsValid(gesture)) return Ui.Format(Ui.Validation.ChooseCtrlAltWithAKeyFormat, command.Label);
            if (gesture is null) continue;
            if (assigned.TryGetValue(gesture, out var other))
                return Ui.Format(Ui.Validation.IsAlreadyAssignedToClearThatFormat, Format(gesture), other);
            assigned.Add(gesture, command.Label);
        }
        return null;
    }
}
