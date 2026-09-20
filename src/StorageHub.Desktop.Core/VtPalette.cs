namespace StorageHub.Desktop;

/// <summary>
/// What colour a terminal cell actually is, once its flags have had their say.
/// </summary>
/// <remarks>
/// <para>
/// A cell names a colour three ways -- a palette index, a direct RGB triple, or "whatever the
/// default is" -- and then four flags argue about the result: bold brightens an indexed colour, dim
/// halves it, inverse swaps the pair, and a selection swaps them again. Getting that order wrong is
/// not a crash, it is a terminal that looks subtly unlike every other terminal, which is the kind
/// of thing nobody reports and everybody notices.
/// </para>
/// <para>
/// It lives here rather than in the painter because it is arithmetic on a cell, not drawing: it can
/// be asserted, and the WinForms painter it came from could only be looked at. The painter's job is
/// reduced to turning the answer into a brush.
/// </para>
/// </remarks>
internal static class VtPalette
{
    /// <summary>
    /// The sixteen ANSI colours, as StorageHub draws them.
    /// </summary>
    /// <remarks>
    /// Not the VGA originals. These are the shell's own palette, chosen to sit against its
    /// surfaces rather than against a 1987 monitor, and the first eight have brighter twins at
    /// index + 8 which is what bold selects.
    /// </remarks>
    internal static readonly VtRgb[] Ansi =
    [
        new(15, 23, 42), new(220, 38, 38),
        new(22, 163, 74), new(202, 138, 4),
        new(37, 99, 235), new(147, 51, 234),
        new(8, 145, 178), new(203, 213, 225),
        new(100, 116, 139), new(248, 113, 113),
        new(74, 222, 128), new(250, 204, 21),
        new(96, 165, 250), new(216, 180, 254),
        new(34, 211, 238), new(248, 250, 252)
    ];

    /// <summary>
    /// The pair a cell is painted with, flags and selection applied in the order they interact.
    /// </summary>
    /// <param name="defaultForeground">What "default" means here, which the scheme decides.</param>
    /// <param name="selected">Whether this cell is inside the local selection.</param>
    internal static (VtRgb Foreground, VtRgb Background) Resolve(
        VtCell cell,
        VtRgb defaultForeground,
        VtRgb defaultBackground,
        bool selected)
    {
        var foreground = Resolve(cell.Foreground, defaultForeground);
        var background = Resolve(cell.Background, defaultBackground);

        // Bold on one of the first eight indexed colours means its brighter twin. It does not
        // brighten an RGB colour or a default one: there is nothing defined to brighten them to.
        if (cell.Flags.HasFlag(VtCellFlags.Bold) &&
            cell.Foreground.Kind == VtColorKind.Indexed &&
            cell.Foreground.Index < 8)
        {
            foreground = Ansi[cell.Foreground.Index + 8];
        }

        if (cell.Flags.HasFlag(VtCellFlags.Dim))
        {
            foreground = new VtRgb(foreground.Red / 2, foreground.Green / 2, foreground.Blue / 2);
        }

        // Inverse and selection are each a swap, so a selected cell inside inverse text swaps
        // twice and comes back to itself -- which is what makes a selection visible there at all.
        if (cell.Flags.HasFlag(VtCellFlags.Inverse))
        {
            (foreground, background) = (background, foreground);
        }

        if (selected)
        {
            (foreground, background) = (background, foreground);
        }

        return (foreground, background);
    }

    /// <summary>One colour, without the flags.</summary>
    internal static VtRgb Resolve(VtColor color, VtRgb fallback) => color.Kind switch
    {
        VtColorKind.Indexed => color.Index < 16 ? Ansi[color.Index] : Xterm(color.Index),
        VtColorKind.Rgb => new VtRgb(color.Red, color.Green, color.Blue),
        _ => fallback
    };

    /// <summary>
    /// The 240 colours above the ANSI sixteen: a 6x6x6 cube, then 24 greys.
    /// </summary>
    /// <remarks>
    /// The component steps are xterm's and are not linear -- 0, then 95, then 40 apart. Anything
    /// else produces a cube that is recognisably the wrong shade of everything.
    /// </remarks>
    internal static VtRgb Xterm(int index)
    {
        if (index >= 232)
        {
            var shade = 8 + ((index - 232) * 10);
            return new VtRgb(shade, shade, shade);
        }

        var cube = index - 16;
        static int Component(int value) => value == 0 ? 0 : 55 + (value * 40);
        return new VtRgb(Component(cube / 36), Component((cube / 6) % 6), Component(cube % 6));
    }
}

/// <summary>
/// A colour, as three bytes.
/// </summary>
/// <remarks>
/// Not Avalonia's Color, so this and its tests stay free of a UI toolkit; the painter converts once
/// at the edge. Desktop.Core does reference Avalonia.Base for a few types, but colour arithmetic is
/// the wrong place to spend that.
/// </remarks>
internal readonly record struct VtRgb(int Red, int Green, int Blue);
