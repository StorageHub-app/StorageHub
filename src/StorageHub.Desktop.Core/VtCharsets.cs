namespace StorageHub.Desktop;

/// <summary>
/// Character set designation. Only two matter in practice: US ASCII, and the DEC special graphics
/// set that draws the box characters ncurses programs frame themselves with. Designating a set
/// must also consume its selector -- not doing so is why a stray "B" used to print itself.
/// </summary>
internal static class VtCharsets
{
    internal const int Ascii = 'B';
    internal const int DecSpecialGraphics = '0';

    // The 0x5F-0x7E range of the DEC special graphics set, in order.
    private static readonly char[] SpecialGraphics =
    [
        ' ', '◆', '▒', '␉', '␌', '␍', '␊', '°',
        '±', '␤', '␋', '┘', '┐', '┌', '└', '┼',
        '⎺', '⎻', '─', '⎼', '⎽', '├', '┤', '┴',
        '┬', '│', '≤', '≥', 'π', '≠', '£', '·'
    ];

    /// <summary>Maps a printable codepoint through the active set.</summary>
    internal static int Map(int codepoint, int charset)
    {
        if (charset != DecSpecialGraphics || codepoint is < 0x5F or > 0x7E)
        {
            return codepoint;
        }

        return SpecialGraphics[codepoint - 0x5F];
    }

    /// <summary>Whether the byte after ESC ( or ESC ) designates a set this terminal knows.</summary>
    internal static bool IsKnownDesignator(int designator) =>
        designator is Ascii or DecSpecialGraphics or 'A' or '0' or '1' or '2';
}
