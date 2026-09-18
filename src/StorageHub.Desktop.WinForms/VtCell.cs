namespace StorageHub.Desktop;

/// <summary>How a cell's colour is expressed, before any palette is applied.</summary>
internal enum VtColorKind : byte
{
    /// <summary>The terminal's own default, whatever the theme says that is.</summary>
    Default = 0,
    Indexed,
    Rgb
}

/// <summary>
/// A colour left unresolved until paint time. Storing the index rather than a <see cref="Color"/>
/// is what lets inverse video, a palette change and a theme change all work: a cell that says
/// "colour 2" has to keep saying that, or swapping the theme would leave old output painted in
/// the old theme's greens.
/// </summary>
internal readonly record struct VtColor(VtColorKind Kind, byte Index, int Rgb)
{
    internal static readonly VtColor Default = new(VtColorKind.Default, 0, 0);

    internal static VtColor FromIndex(int index) => new(VtColorKind.Indexed, (byte)Math.Clamp(index, 0, 255), 0);

    internal static VtColor FromRgb(int red, int green, int blue) =>
        new(VtColorKind.Rgb, 0, (Math.Clamp(red, 0, 255) << 16) | (Math.Clamp(green, 0, 255) << 8) | Math.Clamp(blue, 0, 255));

    internal int Red => (Rgb >> 16) & 0xFF;

    internal int Green => (Rgb >> 8) & 0xFF;

    internal int Blue => Rgb & 0xFF;
}

[Flags]
internal enum VtCellFlags : ushort
{
    None = 0,
    Bold = 1 << 0,
    Dim = 1 << 1,
    Italic = 1 << 2,
    Underline = 1 << 3,
    Blink = 1 << 4,
    Inverse = 1 << 5,
    Hidden = 1 << 6,
    Strike = 1 << 7,

    /// <summary>The left half of a double-width character.</summary>
    WideLeading = 1 << 8,

    /// <summary>The placeholder right half of a double-width character; never painted on its own.</summary>
    WideTrailing = 1 << 9
}

/// <summary>
/// One character cell. The codepoint is an <see cref="int"/> rather than a <see cref="char"/> so
/// astral-plane text is not permanently mangled; that costs nothing here and cannot be retrofitted
/// later without touching every consumer.
/// </summary>
internal readonly record struct VtCell(int Codepoint, VtColor Foreground, VtColor Background, VtCellFlags Flags)
{
    internal static readonly VtCell Blank = new(' ', VtColor.Default, VtColor.Default, VtCellFlags.None);

    internal bool SameStyle(VtCell other) =>
        Foreground == other.Foreground && Background == other.Background && Flags == other.Flags;

    /// <summary>
    /// Whether this cell may be trimmed as trailing whitespace. A space with a non-default
    /// background is not blank -- it is part of htop's meter bar or vim's status line, and
    /// trimming it away would strip the colour off the right of every such row.
    /// </summary>
    internal bool IsTrimmableBlank =>
        Codepoint == ' '
        && Background.Kind == VtColorKind.Default
        && Flags == VtCellFlags.None;
}

/// <summary>Bytes the terminal produced that must be sent to the remote.</summary>
internal sealed class VtInputEventArgs(byte[] input) : EventArgs
{
    internal byte[] Input { get; } = input;
}
