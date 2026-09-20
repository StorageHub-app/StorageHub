using Avalonia.Input;

namespace StorageHub.Desktop;

/// <summary>
/// Translates between the catalog's <see cref="KeyGesture"/> and WinForms' <see cref="Keys"/>.
/// </summary>
/// <remarks>
/// Transitional, and deliberately one-way in spirit: the command catalog moved to Desktop.Core and
/// now names shortcuts in the framework-neutral vocabulary the Avalonia shell uses, while this shell
/// still needs a Keys value for ProcessCmdKey and ShortcutKeyDisplayString. It dies with the WinForms
/// project. The member names line up almost exactly - the two exceptions are spelled out below.
/// </remarks>
internal static class ShortcutKeys
{
    internal static Keys ToKeys(KeyGesture? gesture)
    {
        if (gesture is null) return Keys.None;

        var name = gesture.Key switch
        {
            Key.OemComma => nameof(Keys.Oemcomma),
            Key.Return => nameof(Keys.Enter),
            _ => gesture.Key.ToString(),
        };

        if (!Enum.TryParse<Keys>(name, ignoreCase: false, out var keys)) return Keys.None;

        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control)) keys |= Keys.Control;
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Shift)) keys |= Keys.Shift;
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Alt)) keys |= Keys.Alt;
        return keys;
    }

    internal static KeyGesture? ToGesture(Keys keys)
    {
        if (keys == Keys.None) return null;

        var code = keys & Keys.KeyCode;
        var name = code switch
        {
            Keys.Oemcomma => nameof(Key.OemComma),
            Keys.Enter => nameof(Key.Return),
            _ => code.ToString(),
        };

        if (!Enum.TryParse<Key>(name, ignoreCase: false, out var key)) return null;

        var modifiers = KeyModifiers.None;
        if (keys.HasFlag(Keys.Control)) modifiers |= KeyModifiers.Control;
        if (keys.HasFlag(Keys.Shift)) modifiers |= KeyModifiers.Shift;
        if (keys.HasFlag(Keys.Alt)) modifiers |= KeyModifiers.Alt;
        return new KeyGesture(key, modifiers);
    }
}
