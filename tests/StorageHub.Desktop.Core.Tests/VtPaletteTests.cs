using StorageHub.Desktop;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// What colour a terminal cell ends up, once its flags have argued about it.
/// </summary>
/// <remarks>
/// Four flags interact and the order matters: bold brightens an indexed colour, dim halves it,
/// inverse swaps the pair, and a selection swaps them again. Getting that wrong is not a crash --
/// it is a terminal that looks subtly unlike every other terminal, which nobody reports and
/// everybody notices. In the WinForms painter this arithmetic could only be looked at; here it can
/// be asserted.
/// </remarks>
public class VtPaletteTests
{
    private static readonly VtRgb Foreground = new(200, 200, 200);
    private static readonly VtRgb Background = new(10, 10, 10);

    [Fact]
    public void ACellWithNoColoursTakesTheDefaults()
    {
        var (foreground, background) = Resolve(VtCell.Blank);

        Assert.Equal(Foreground, foreground);
        Assert.Equal(Background, background);
    }

    /// <summary>Bold on one of the first eight indexed colours means its brighter twin.</summary>
    [Fact]
    public void BoldBrightensAnIndexedColour()
    {
        var plain = Cell(VtColor.FromIndex(1));
        var bold = Cell(VtColor.FromIndex(1), flags: VtCellFlags.Bold);

        Assert.Equal(VtPalette.Ansi[1], Resolve(plain).Foreground);
        Assert.Equal(VtPalette.Ansi[9], Resolve(bold).Foreground);
    }

    /// <summary>
    /// And leaves anything else alone, because there is nothing defined to brighten it to.
    /// </summary>
    [Fact]
    public void BoldDoesNotBrightenAnRgbOrDefaultColour()
    {
        var rgb = Cell(VtColor.FromRgb(1, 2, 3), flags: VtCellFlags.Bold);
        var fallback = Cell(VtColor.Default, flags: VtCellFlags.Bold);

        Assert.Equal(new VtRgb(1, 2, 3), Resolve(rgb).Foreground);
        Assert.Equal(Foreground, Resolve(fallback).Foreground);
    }

    /// <summary>Nor one of the bright eight, which have no brighter twin above them.</summary>
    [Fact]
    public void BoldDoesNotBrightenAnAlreadyBrightColour()
    {
        var cell = Cell(VtColor.FromIndex(9), flags: VtCellFlags.Bold);

        Assert.Equal(VtPalette.Ansi[9], Resolve(cell).Foreground);
    }

    [Fact]
    public void DimHalvesTheForeground()
    {
        var cell = Cell(VtColor.FromRgb(200, 100, 50), flags: VtCellFlags.Dim);

        Assert.Equal(new VtRgb(100, 50, 25), Resolve(cell).Foreground);
    }

    [Fact]
    public void InverseSwapsThePair()
    {
        var cell = Cell(VtColor.FromRgb(1, 2, 3), VtColor.FromRgb(4, 5, 6), VtCellFlags.Inverse);

        var (foreground, background) = Resolve(cell);

        Assert.Equal(new VtRgb(4, 5, 6), foreground);
        Assert.Equal(new VtRgb(1, 2, 3), background);
    }

    /// <summary>
    /// A selection inside inverse text swaps twice and comes back.
    /// </summary>
    /// <remarks>
    /// Which is what makes a selection visible there at all: if selection simply forced one pair,
    /// dragging across inverse text would leave it looking unselected.
    /// </remarks>
    [Fact]
    public void SelectingInverseTextSwapsBackToItself()
    {
        var cell = Cell(VtColor.FromRgb(1, 2, 3), VtColor.FromRgb(4, 5, 6), VtCellFlags.Inverse);

        var plain = VtPalette.Resolve(cell, Foreground, Background, selected: false);
        var selected = VtPalette.Resolve(cell, Foreground, Background, selected: true);

        Assert.Equal(new VtRgb(4, 5, 6), plain.Foreground);
        Assert.Equal(new VtRgb(1, 2, 3), selected.Foreground);
    }

    /// <summary>Bold picks the brighter twin first, and dim then halves that.</summary>
    [Fact]
    public void BoldAndDimCompose()
    {
        var cell = Cell(VtColor.FromIndex(1), flags: VtCellFlags.Bold | VtCellFlags.Dim);
        var bright = VtPalette.Ansi[9];

        Assert.Equal(
            new VtRgb(bright.Red / 2, bright.Green / 2, bright.Blue / 2),
            Resolve(cell).Foreground);
    }

    /// <summary>
    /// The 240 colours above the ANSI sixteen: a 6x6x6 cube, then 24 greys.
    /// </summary>
    /// <remarks>
    /// The component steps are xterm's and are deliberately not linear -- 0, then 95, then 40
    /// apart. Anything else is a cube that is recognisably the wrong shade of everything.
    /// </remarks>
    [Theory]
    [InlineData(16, 0, 0, 0)]
    [InlineData(21, 0, 0, 255)]
    [InlineData(196, 255, 0, 0)]
    [InlineData(231, 255, 255, 255)]
    public void TheXtermCubeUsesXtermSteps(int index, int red, int green, int blue)
    {
        Assert.Equal(new VtRgb(red, green, blue), VtPalette.Xterm(index));
    }

    [Theory]
    [InlineData(232, 8)]
    [InlineData(243, 118)]
    [InlineData(255, 238)]
    public void TheGreyRampClimbsByTen(int index, int shade)
    {
        Assert.Equal(new VtRgb(shade, shade, shade), VtPalette.Xterm(index));
    }

    /// <summary>Every index a cell can name resolves to something, without a gap.</summary>
    [Fact]
    public void EveryIndexResolves()
    {
        for (var index = 0; index < 256; index++)
        {
            var colour = VtPalette.Resolve(VtColor.FromIndex(index), Foreground);

            Assert.InRange(colour.Red, 0, 255);
            Assert.InRange(colour.Green, 0, 255);
            Assert.InRange(colour.Blue, 0, 255);
        }
    }

    private static (VtRgb Foreground, VtRgb Background) Resolve(VtCell cell) =>
        VtPalette.Resolve(cell, Foreground, Background, selected: false);

    private static VtCell Cell(
        VtColor foreground,
        VtColor? background = null,
        VtCellFlags flags = VtCellFlags.None) =>
        new(' ', foreground, background ?? VtColor.Default, flags);
}
