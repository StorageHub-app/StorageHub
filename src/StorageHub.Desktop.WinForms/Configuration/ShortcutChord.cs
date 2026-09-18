namespace StorageHub.Desktop.Configuration;

/// <summary>
/// Converts a <see cref="Keys"/> value to and from the canonical text stored in
/// <c>config.shortcuts.json</c>, for example <c>Ctrl+Shift+S</c>.
/// </summary>
/// <remarks>
/// <para>
/// A chord is deliberately not persisted as its enum. CodeLogic installs a
/// <c>JsonStringEnumConverter</c> with a camel-case policy, and <see cref="Keys"/> is a
/// <c>[Flags]</c> enum carrying dozens of aliases -- <c>Ctrl+Shift+S</c> would be written as
/// <c>"control, shiftKey, s"</c>, which is neither readable nor reliably round-trippable.
/// </para>
/// <para>
/// The modifier order is fixed at Ctrl, Alt, Shift so that the same binding always produces the
/// same bytes, and a diff of the file shows only what a user actually changed.
/// </para>
/// </remarks>
internal static class ShortcutChord
{
    private const string None = "Unassigned";

    /// <summary>The longest text this will parse, so a hostile file cannot make it work hard.</summary>
    private const int MaximumLength = 64;

    internal static string Format(Keys keys)
    {
        if (keys == Keys.None) return None;
        var code = keys & Keys.KeyCode;
        var chord = new List<string>(4);
        if ((keys & Keys.Control) != 0) chord.Add("Ctrl");
        if ((keys & Keys.Alt) != 0) chord.Add("Alt");
        if ((keys & Keys.Shift) != 0) chord.Add("Shift");
        chord.Add(code.ToString());
        return string.Join('+', chord);
    }

    /// <summary>
    /// Reads a chord back, or returns null when the text is not one. Null rather than an exception
    /// because the caller is always reading a file a user may have edited, and one bad line should
    /// cost that one binding rather than the whole file.
    /// </summary>
    internal static Keys? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumLength) return null;
        var text = value.Trim();
        if (string.Equals(text, None, StringComparison.OrdinalIgnoreCase)) return Keys.None;

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 4) return null;

        var keys = Keys.None;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            var modifier = parts[index] switch
            {
                "Ctrl" or "Control" => Keys.Control,
                "Alt" => Keys.Alt,
                "Shift" => Keys.Shift,
                _ => Keys.None
            };
            // An unrecognised or repeated modifier means the text was not written by Format, and
            // guessing at what was meant is how a user ends up with a binding they did not choose.
            if (modifier == Keys.None || (keys & modifier) != 0) return null;
            keys |= modifier;
        }

        if (!Enum.TryParse<Keys>(parts[^1], ignoreCase: false, out var code) ||
            (code & ~Keys.KeyCode) != 0 ||
            !Enum.IsDefined(code))
        {
            return null;
        }

        return keys | code;
    }

    /// <summary>
    /// The stored form of a whole override table, dropping any binding the current build no longer
    /// recognises rather than carrying it forward invisibly.
    /// </summary>
    internal static Dictionary<string, string> Format(IReadOnlyDictionary<string, Keys> shortcuts)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        return shortcuts.ToDictionary(
            entry => entry.Key,
            entry => Format(entry.Value),
            StringComparer.Ordinal);
    }

    internal static Dictionary<string, Keys> Parse(IReadOnlyDictionary<string, string>? shortcuts)
    {
        var result = new Dictionary<string, Keys>(StringComparer.Ordinal);
        if (shortcuts is null) return result;
        foreach (var entry in shortcuts)
        {
            if (!string.IsNullOrWhiteSpace(entry.Key) && TryParse(entry.Value) is { } keys)
            {
                result[entry.Key] = keys;
            }
        }

        return result;
    }
}
