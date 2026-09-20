using Avalonia.Media;
using StorageHub.Desktop.Themes;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// That every scheme is complete, readable, and internally consistent.
/// </summary>
/// <remarks>
/// Twenty-two schemes is more than anyone will look at before a release, so the properties that
/// make one usable are asserted rather than eyeballed. Contrast is the one that matters: a palette
/// that looks good as six swatches on a website can still put muted text on a surface at a ratio
/// nobody can read.
/// </remarks>
public class ColorSchemeTests
{
    [Fact]
    public void EverySchemeDefinesEveryToken()
    {
        var incomplete = new List<string>();
        foreach (var scheme in ColorSchemeCatalog.All)
        {
            foreach (var token in ColorTokens.All)
            {
                if (!scheme.Tokens.ContainsKey(token))
                {
                    incomplete.Add($"{scheme.Id} is missing {token}");
                }
            }

            foreach (var token in scheme.Tokens.Keys)
            {
                if (!ColorTokens.All.Contains(token))
                {
                    incomplete.Add($"{scheme.Id} defines {token}, which nothing reads");
                }
            }
        }

        Assert.Empty(incomplete);
    }

    [Fact]
    public void IdsAreStableLowercaseAndUnique()
    {
        Assert.Equal(
            ColorSchemeCatalog.All.Count,
            ColorSchemeCatalog.All.Select(scheme => scheme.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        Assert.All(ColorSchemeCatalog.All, scheme =>
        {
            Assert.Equal(scheme.Id, scheme.Id.ToLowerInvariant());
            Assert.DoesNotContain(' ', scheme.Id);
            Assert.False(string.IsNullOrWhiteSpace(scheme.Name));
        });
    }

    /// <summary>A pair has to agree that it is a pair, or "follow the system" picks the wrong one.</summary>
    [Fact]
    public void EveryCounterpartExistsPointsBackAndIsTheOtherSide()
    {
        var broken = new List<string>();
        foreach (var scheme in ColorSchemeCatalog.All.Where(s => s.CounterpartId is not null))
        {
            var other = ColorSchemeCatalog.All.SingleOrDefault(s =>
                string.Equals(s.Id, scheme.CounterpartId, StringComparison.Ordinal));
            if (other is null)
            {
                broken.Add($"{scheme.Id} names {scheme.CounterpartId}, which does not exist");
                continue;
            }

            if (other.CounterpartId != scheme.Id) broken.Add($"{scheme.Id} and {other.Id} disagree");
            if (other.IsDark == scheme.IsDark) broken.Add($"{scheme.Id} and {other.Id} are the same side");
        }

        Assert.Empty(broken);
    }

    [Fact]
    public void BothHouseSchemesShipAndTheyArePaired()
    {
        var dark = ColorSchemeCatalog.Resolve(ColorSchemeCatalog.DefaultDarkId, preferDark: true);
        var light = ColorSchemeCatalog.Resolve(ColorSchemeCatalog.DefaultLightId, preferDark: false);

        Assert.True(dark.IsDark);
        Assert.False(light.IsDark);
        Assert.Equal(light.Id, dark.CounterpartId);
        Assert.Equal(dark.Id, light.CounterpartId);
    }

    /// <summary>
    /// The house schemes are exactly what DesignTokens.axaml has always said.
    /// </summary>
    /// <remarks>
    /// Pinned, not derived. These two are transcriptions of the WinForms palette, and the reference
    /// captures this port is compared against were taken against them: a derivation that came
    /// within two units per channel would still be a change to the one appearance the port exists
    /// to reproduce.
    /// </remarks>
    [Fact]
    public void TheHouseSchemesAreTheOriginalPalette()
    {
        var dark = ColorSchemeCatalog.Resolve(ColorSchemeCatalog.DefaultDarkId, preferDark: true);

        Assert.Equal(Color.Parse("#FF16181D"), dark.Tokens[ColorTokens.Canvas]);
        Assert.Equal(Color.Parse("#FFE8EAF0"), dark.Tokens[ColorTokens.Text]);
        Assert.Equal(Color.Parse("#FF4C8BF5"), dark.Tokens[ColorTokens.Primary]);
        Assert.Equal(Color.Parse("#FF3A3F4A"), dark.Tokens[ColorTokens.Border]);
        Assert.Equal(Color.Parse("#FFF4696F"), dark.Tokens[ColorTokens.Danger]);

        var light = ColorSchemeCatalog.Resolve(ColorSchemeCatalog.DefaultLightId, preferDark: false);

        Assert.Equal(Color.Parse("#FFF4F6FA"), light.Tokens[ColorTokens.Canvas]);
        Assert.Equal(Color.Parse("#FF1E242D"), light.Tokens[ColorTokens.Text]);
        Assert.Equal(Color.Parse("#FF1867C0"), light.Tokens[ColorTokens.Primary]);
        Assert.Equal(Color.Parse("#FFD3DAE4"), light.Tokens[ColorTokens.Border]);
        Assert.Equal(Color.Parse("#FFBE2D37"), light.Tokens[ColorTokens.Danger]);
    }

    /// <summary>
    /// Body text is readable on every surface it is drawn on, in every scheme.
    /// </summary>
    /// <remarks>
    /// 4.5:1 is the WCAG AA threshold for body text. Asserted rather than trusted because a palette
    /// published as a set of swatches says nothing about the pairs StorageHub actually draws -- and
    /// because a scheme added later will be checked by this rather than by whoever reviews it.
    /// </remarks>
    [Fact]
    public void BodyTextIsReadableOnEverySurface()
    {
        var failures = new List<string>();
        foreach (var scheme in ColorSchemeCatalog.All)
        {
            foreach (var behind in (string[])
                [ColorTokens.Canvas, ColorTokens.Surface, ColorTokens.SurfaceMuted, ColorTokens.Elevated, ColorTokens.Input])
            {
                var ratio = Contrast(scheme.Tokens[ColorTokens.Text], scheme.Tokens[behind]);
                if (ratio < 4.5)
                {
                    failures.Add($"{scheme.Id}: text on {behind} is {ratio:0.00}:1");
                }
            }
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// Muted text, and the label on a filled accent button, clear the large-text threshold.
    /// </summary>
    /// <remarks>
    /// 3:1 rather than 4.5:1 on purpose. Muted text is a deliberate step back from body text -- a
    /// caption, a hint, a column that is not the point of the row -- and holding it to the body
    /// threshold would mean it could not be muted at all.
    /// </remarks>
    [Fact]
    public void MutedTextAndAccentLabelsClearTheLargeTextThreshold()
    {
        var failures = new List<string>();
        foreach (var scheme in ColorSchemeCatalog.All)
        {
            var muted = Contrast(scheme.Tokens[ColorTokens.TextMuted], scheme.Tokens[ColorTokens.Canvas]);
            if (muted < 3.0) failures.Add($"{scheme.Id}: muted text is {muted:0.00}:1");

            // Whatever the scheme says goes on the accent, against the accent. Asserting White
            // here instead is how this test first ran, and it failed for nine schemes: half of
            // these palettes have a pastel accent that only a dark label can sit on.
            var onAccent = Contrast(
                scheme.Tokens[ColorTokens.OnPrimary],
                scheme.Tokens[ColorTokens.Primary]);
            if (onAccent < 3.0) failures.Add($"{scheme.Id}: its accent label is {onAccent:0.00}:1");
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// A dark scheme's surfaces are darker than its text, and a light scheme's are lighter.
    /// </summary>
    /// <remarks>
    /// Catches the one mistake a derived scheme can make that contrast does not: a palette entered
    /// with IsDark the wrong way round derives its whole ramp backwards, and a shell whose panels
    /// step the wrong way still passes every contrast check.
    /// </remarks>
    [Fact]
    public void EverySchemeStepsInTheDirectionItClaims()
    {
        var wrong = new List<string>();
        foreach (var scheme in ColorSchemeCatalog.All)
        {
            var canvas = Luminance(scheme.Tokens[ColorTokens.Canvas]);
            var text = Luminance(scheme.Tokens[ColorTokens.Text]);
            if (scheme.IsDark != canvas < text)
            {
                wrong.Add($"{scheme.Id} says IsDark={scheme.IsDark} but its canvas is {(canvas < text ? "darker" : "lighter")} than its text");
            }
        }

        Assert.Empty(wrong);
    }

    [Fact]
    public void AnUnknownSchemeFallsBackToTheHouseOneRatherThanThrowing()
    {
        // Arrives from a settings file written by a newer build, or edited by hand.
        Assert.Equal(ColorSchemeCatalog.DefaultDarkId, ColorSchemeCatalog.Resolve("nope", preferDark: true).Id);
        Assert.Equal(ColorSchemeCatalog.DefaultLightId, ColorSchemeCatalog.Resolve(null, preferDark: false).Id);
        Assert.Equal(ColorSchemeCatalog.DefaultLightId, ColorSchemeCatalog.Resolve("   ", preferDark: false).Id);
    }

    [Fact]
    public void FollowingTheSystemPicksTheOtherHalfOfAPairAndLeavesALonerAlone()
    {
        var mocha = ColorSchemeCatalog.Resolve("catppuccin-mocha", preferDark: true);

        Assert.Equal("catppuccin-latte", ColorSchemeCatalog.ForAppearance(mocha, preferDark: false).Id);
        Assert.Equal("catppuccin-mocha", ColorSchemeCatalog.ForAppearance(mocha, preferDark: true).Id);

        // Dracula has no light half, and inventing one would be worse than staying dark.
        var dracula = ColorSchemeCatalog.Resolve("dracula", preferDark: true);
        Assert.Equal("dracula", ColorSchemeCatalog.ForAppearance(dracula, preferDark: false).Id);
    }

    [Fact]
    public void TheCatalogIsAboutTwentySchemesWithBothSidesRepresented()
    {
        Assert.InRange(ColorSchemeCatalog.All.Count, 18, 26);
        Assert.True(ColorSchemeCatalog.All.Count(scheme => scheme.IsDark) >= 6);
        Assert.True(ColorSchemeCatalog.All.Count(scheme => !scheme.IsDark) >= 6);
    }

    /// <summary>Lightening keeps the hue, which is why it is not a mix towards white.</summary>
    [Fact]
    public void LighteningAnAccentKeepsItsHue()
    {
        var accent = Color.Parse("#FF1867C0");

        var lighter = ColorScheme.Lighten(accent, 0.24);
        var washed = ColorScheme.Mix(accent, Colors.White, 0.4);

        var (accentHue, accentSaturation, _) = ColorScheme.ToHsl(accent);
        var (lighterHue, lighterSaturation, _) = ColorScheme.ToHsl(lighter);
        var (_, washedSaturation, _) = ColorScheme.ToHsl(washed);

        Assert.Equal(accentHue, lighterHue, 2);
        Assert.Equal(accentSaturation, lighterSaturation, 2);
        Assert.True(washedSaturation < accentSaturation - 0.1, "Mixing towards white should wash the hue out.");
    }

    /// <summary>WCAG relative luminance, and the ratio built from it.</summary>
    private static double Contrast(Color first, Color second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(Color color) =>
        (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));

    private static double Linear(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
