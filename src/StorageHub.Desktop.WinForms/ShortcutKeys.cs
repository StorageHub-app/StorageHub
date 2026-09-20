using Avalonia.Input;
using StorageHub.Desktop.Configuration;

namespace StorageHub.Desktop;

/// <summary>
/// Translates between the catalog's <see cref="KeyGesture"/> and WinForms' <see cref="Keys"/>.
/// </summary>
/// <remarks>
/// Transitional, and deliberately one-way in spirit: the command catalog moved to Desktop.Core and
/// now names shortcuts in the framework-neutral vocabulary the Avalonia shell uses, while this shell
/// still needs a Keys value for ProcessCmdKey and ShortcutKeyDisplayString. It dies with the WinForms
/// project.
///
/// The member names do not line up as well as they look: twenty-one keys are spelled differently by
/// the two enums. ShortcutChord owns that table, because it is the same knowledge the on-disk format
/// needs, and this type borrows it rather than keeping a second copy.
/// </remarks>
internal static class ShortcutKeys
{
    internal static Keys ToKeys(KeyGesture? gesture)
    {
        if (gesture is null) return Keys.None;

        // The same spelling config.shortcuts.json uses, which is this enum's - twenty-one keys are
        // named differently by the two, and a case-sensitive parse of Avalonia's name silently
        // yielded Keys.None for every one of them.
        if (!Enum.TryParse<Keys>(ShortcutChord.StoredName(gesture.Key), ignoreCase: false, out var keys))
        {
            return Keys.None;
        }

        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control)) keys |= Keys.Control;
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Shift)) keys |= Keys.Shift;
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Alt)) keys |= Keys.Alt;
        return keys;
    }

    /// <summary>A whole override table, as this shell wants it.</summary>
    internal static Dictionary<string, Keys> ToKeys(IReadOnlyDictionary<string, KeyGesture?>? gestures)
    {
        var result = new Dictionary<string, Keys>(StringComparer.Ordinal);
        if (gestures is null) return result;

        foreach (var entry in gestures) result[entry.Key] = ToKeys(entry.Value);
        return result;
    }

    /// <summary>A whole override table, as configuration stores it.</summary>
    internal static Dictionary<string, KeyGesture?> ToGestures(IReadOnlyDictionary<string, Keys>? keys)
    {
        var result = new Dictionary<string, KeyGesture?>(StringComparer.Ordinal);
        if (keys is null) return result;

        foreach (var entry in keys) result[entry.Key] = ToGesture(entry.Value);
        return result;
    }

    internal static KeyGesture? ToGesture(Keys keys)
    {
        if (keys == Keys.None) return null;

        var code = keys & Keys.KeyCode;
        if (!ShortcutChord.TryParseKey(code.ToString(), out var key)) return null;

        var modifiers = KeyModifiers.None;
        if (keys.HasFlag(Keys.Control)) modifiers |= KeyModifiers.Control;
        if (keys.HasFlag(Keys.Shift)) modifiers |= KeyModifiers.Shift;
        if (keys.HasFlag(Keys.Alt)) modifiers |= KeyModifiers.Alt;
        return new KeyGesture(key, modifiers);
    }
}
