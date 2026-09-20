using Avalonia.Media;

namespace StorageHub.Desktop.Themes;

/// <summary>
/// The colours a scheme has to supply. Every one of them, every time.
/// </summary>
/// <remarks>
/// Named rather than an enum so a scheme is a dictionary keyed by the same strings the XAML uses,
/// which is what lets a scheme be applied by merging a resource dictionary instead of by a switch
/// with twenty-seven arms.
/// </remarks>
internal static class ColorTokens
{
    internal const string Canvas = "CanvasColor";
    internal const string Surface = "SurfaceColor";
    internal const string SurfaceMuted = "SurfaceMutedColor";
    internal const string Elevated = "ElevatedColor";
    internal const string Border = "BorderColor";
    internal const string Text = "TextColor";
    internal const string TextMuted = "TextMutedColor";
    internal const string Primary = "PrimaryColor";
    internal const string PrimaryHover = "PrimaryHoverColor";
    internal const string PrimaryPressed = "PrimaryPressedColor";

    /// <summary>
    /// What is drawn on top of <see cref="Primary"/>: a button label, its icon.
    /// </summary>
    /// <remarks>
    /// A token rather than the literal White the shell used, because half of these accents are
    /// pastels. White on Dracula's lilac is 2.4:1 and on Monokai's cyan 1.7:1 -- unreadable, and
    /// exactly the defect already found once on StorageHub's own blue in light appearance.
    /// </remarks>
    internal const string OnPrimary = "OnPrimaryColor";
    internal const string SystemAccent = "SystemAccentColor";
    internal const string SystemAccentLight1 = "SystemAccentColorLight1";
    internal const string SystemAccentLight2 = "SystemAccentColorLight2";
    internal const string SystemAccentLight3 = "SystemAccentColorLight3";
    internal const string SystemAccentDark1 = "SystemAccentColorDark1";
    internal const string SystemAccentDark2 = "SystemAccentColorDark2";
    internal const string SystemAccentDark3 = "SystemAccentColorDark3";
    internal const string Selection = "SelectionColor";
    internal const string SelectionPressed = "SelectionPressedColor";
    internal const string Input = "InputColor";
    internal const string DisabledText = "DisabledTextColor";
    internal const string Success = "SuccessColor";
    internal const string Warning = "WarningColor";
    internal const string Danger = "DangerColor";
    internal const string SuccessTint = "SuccessTintColor";
    internal const string WarningTint = "WarningTintColor";
    internal const string DangerTint = "DangerTintColor";

    /// <summary>All of them, so a test can insist a scheme is complete.</summary>
    internal static readonly IReadOnlyList<string> All =
    [
        Canvas, Surface, SurfaceMuted, Elevated, Border, Text, TextMuted,
        Primary, PrimaryHover, PrimaryPressed, OnPrimary,
        SystemAccent, SystemAccentLight1, SystemAccentLight2, SystemAccentLight3,
        SystemAccentDark1, SystemAccentDark2, SystemAccentDark3,
        Selection, SelectionPressed, Input, DisabledText,
        Success, Warning, Danger, SuccessTint, WarningTint, DangerTint
    ];
}

/// <summary>
/// One complete set of colours, with a stable id and a name to show.
/// </summary>
/// <remarks>
/// <para>
/// A scheme is StorageHub's, not the desktop's. The system accent is deliberately overridden
/// rather than followed -- FluentTheme derives its selected-tab pipe, focus adorners, checkbox
/// fills and scrollbar thumbs from it, and left alone that is how a themed application still ends
/// up with one control in the wrong blue, in a different wrong blue on each machine.
/// </para>
/// <para>
/// <see cref="FromPalette"/> is how a scheme should be written: eight anchors, and the other
/// nineteen tokens derived from them by rules that hold for every scheme. That is what "the same
/// on both platforms, standardized" has to mean in practice -- not twenty tables that each looked
/// right on the day somebody typed them, but one set of relationships applied twenty times.
/// </para>
/// </remarks>
internal sealed record ColorScheme
{
    /// <summary>Stable, lowercase, written to settings.json. Never changes once shipped.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Whether the text is light on dark. Decides the ThemeVariant the shell asks for.</summary>
    public required bool IsDark { get; init; }

    /// <summary>
    /// The same scheme on the other side, when there is one.
    /// </summary>
    /// <remarks>
    /// Solarized, Gruvbox, Catppuccin and the rest ship a pair; Dracula, Nord's polar night and
    /// Monokai do not. A pair is what lets "follow the system" mean something for a scheme that is
    /// not the house one -- the OS says light or dark, and the scheme stays the scheme.
    /// </remarks>
    public string? CounterpartId { get; init; }

    public required IReadOnlyDictionary<string, Color> Tokens { get; init; }

    /// <summary>
    /// Derives a complete scheme from the handful of colours a palette actually publishes.
    /// </summary>
    /// <param name="canvas">The page behind everything.</param>
    /// <param name="surface">A panel or card. Given rather than derived: some palettes raise it and some recess it.</param>
    /// <param name="text">The body text colour.</param>
    /// <param name="primary">The accent, which also becomes the whole Fluent chrome ramp.</param>
    internal static ColorScheme FromPalette(
        string id,
        string name,
        bool isDark,
        Color canvas,
        Color surface,
        Color text,
        Color primary,
        Color success,
        Color warning,
        Color danger,
        string? counterpartId = null)
    {
        // Distances from the canvas towards the text colour. A negative step moves the other way,
        // which is how a light scheme raises a surface to white while a dark one lifts it out of
        // the page. The two sets are read off the house palettes, so every other scheme inherits
        // relationships that were tuned once against a real design rather than guessed per palette.
        var muted = isDark ? 0.081 : 0.033;
        var elevated = isDark ? 0.114 : -0.023;
        var border = isDark ? 0.171 : 0.154;
        var input = isDark ? -0.010 : -0.042;
        var textMuted = isDark ? 0.657 : 0.710;
        var disabled = isDark ? 0.405 : 0.458;

        var tokens = new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            [ColorTokens.Canvas] = canvas,
            [ColorTokens.Surface] = surface,
            [ColorTokens.SurfaceMuted] = Mix(canvas, text, muted),
            [ColorTokens.Elevated] = Mix(canvas, text, elevated),
            [ColorTokens.Border] = Mix(canvas, text, border),
            [ColorTokens.Text] = text,
            [ColorTokens.TextMuted] = Mix(canvas, text, textMuted),
            [ColorTokens.Input] = Mix(canvas, text, input),
            [ColorTokens.DisabledText] = Mix(canvas, text, disabled),

            [ColorTokens.Primary] = primary,
            [ColorTokens.PrimaryHover] = Lighten(primary, 0.08),
            [ColorTokens.PrimaryPressed] = Lighten(primary, -0.08),
            [ColorTokens.OnPrimary] = OnTopOf(primary),

            // One ramp, six steps, so Fluent's own chrome lands on the scheme's accent rather than
            // on the operating system's.
            [ColorTokens.SystemAccent] = primary,
            [ColorTokens.SystemAccentLight1] = Lighten(primary, 0.08),
            [ColorTokens.SystemAccentLight2] = Lighten(primary, 0.16),
            [ColorTokens.SystemAccentLight3] = Lighten(primary, 0.24),
            [ColorTokens.SystemAccentDark1] = Lighten(primary, -0.08),
            [ColorTokens.SystemAccentDark2] = Lighten(primary, -0.16),
            [ColorTokens.SystemAccentDark3] = Lighten(primary, -0.24),

            // Selection is the accent pulled most of the way back to the page, so selected text
            // stays readable in the page's text colour instead of needing its own.
            [ColorTokens.Selection] = Mix(primary, canvas, isDark ? 0.55 : 0.85),
            [ColorTokens.SelectionPressed] = Mix(primary, canvas, isDark ? 0.68 : 0.76),

            [ColorTokens.Success] = success,
            [ColorTokens.Warning] = warning,
            [ColorTokens.Danger] = danger,
            [ColorTokens.SuccessTint] = Mix(success, canvas, isDark ? 0.88 : 0.86),
            [ColorTokens.WarningTint] = Mix(warning, canvas, isDark ? 0.88 : 0.86),
            [ColorTokens.DangerTint] = Mix(danger, canvas, isDark ? 0.88 : 0.86)
        };

        return new ColorScheme
        {
            Id = id,
            Name = name,
            IsDark = isDark,
            CounterpartId = counterpartId,
            Tokens = tokens
        };
    }

    /// <summary>
    /// A scheme whose every colour is written out.
    /// </summary>
    /// <remarks>
    /// For the two house schemes only. They are transcriptions of the WinForms shell's hand-tuned
    /// palette, which the reference captures in docs/ui-reference are compared against, so they are
    /// pinned rather than regenerated: a derivation that came within two units per channel would
    /// still be a change to the one appearance this port is supposed to reproduce exactly.
    /// </remarks>
    internal static ColorScheme Transcribed(
        string id,
        string name,
        bool isDark,
        string? counterpartId,
        IReadOnlyDictionary<string, Color> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        return new ColorScheme
        {
            Id = id,
            Name = name,
            IsDark = isDark,
            CounterpartId = counterpartId,
            Tokens = new Dictionary<string, Color>(tokens, StringComparer.Ordinal)
        };
    }

    /// <summary>The ink, black or white, that reads best on <paramref name="fill"/>.</summary>
    /// <remarks>
    /// Not near-black and near-white: a button label is the one place a scheme cannot afford to be
    /// subtle, and every one of these accents clears 4.6:1 against whichever end wins.
    /// </remarks>
    internal static Color OnTopOf(Color fill) =>
        Contrast(Colors.White, fill) >= Contrast(Ink, fill) ? Colors.White : Ink;

    /// <summary>Not pure black: it reads as a hole punched in a saturated fill.</summary>
    private static readonly Color Ink = Color.FromArgb(255, 0x10, 0x10, 0x14);

    /// <summary>The WCAG contrast ratio, which is what decides the answer above.</summary>
    internal static double Contrast(Color first, Color second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    internal static double Luminance(Color color) =>
        (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));

    private static double Linear(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    /// <summary>
    /// <paramref name="amount"/> of the way from <paramref name="from"/> to <paramref name="to"/>,
    /// extrapolating past either end when asked.
    /// </summary>
    /// <remarks>
    /// The extrapolation is the point: a light scheme's raised surface sits on the far side of the
    /// canvas from its text, so the step is negative and the result is brighter than the page.
    /// </remarks>
    internal static Color Mix(Color from, Color to, double amount) => Color.FromArgb(
        255,
        Channel(from.R, to.R, amount),
        Channel(from.G, to.G, amount),
        Channel(from.B, to.B, amount));

    private static byte Channel(byte from, byte to, double amount) =>
        (byte)Math.Clamp(Math.Round(from + ((to - from) * amount)), 0, 255);

    /// <summary>
    /// Moves a colour along its own lightness, keeping hue and saturation.
    /// </summary>
    /// <remarks>
    /// Mixing towards white would do neither: it washes the hue out as it brightens, which turns a
    /// six-step accent ramp into a gradient from the accent to grey. The Fluent chrome uses the
    /// outer steps for focus adorners and scrollbar thumbs, where a desaturated step reads as a
    /// different colour rather than a lighter one.
    /// </remarks>
    internal static Color Lighten(Color color, double delta)
    {
        var (hue, saturation, lightness) = ToHsl(color);
        return FromHsl(hue, saturation, Math.Clamp(lightness + delta, 0, 1));
    }

    internal static (double Hue, double Saturation, double Lightness) ToHsl(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2;
        if (max - min < 1e-9)
        {
            return (0, 0, lightness);
        }

        var span = max - min;
        var saturation = lightness > 0.5 ? span / (2 - max - min) : span / (max + min);
        double hue;
        if (max == r) hue = ((g - b) / span) + (g < b ? 6 : 0);
        else if (max == g) hue = ((b - r) / span) + 2;
        else hue = ((r - g) / span) + 4;
        return (hue / 6, saturation, lightness);
    }

    internal static Color FromHsl(double hue, double saturation, double lightness)
    {
        if (saturation < 1e-9)
        {
            var grey = (byte)Math.Round(lightness * 255);
            return Color.FromArgb(255, grey, grey, grey);
        }

        var q = lightness < 0.5 ? lightness * (1 + saturation) : lightness + saturation - (lightness * saturation);
        var p = (2 * lightness) - q;
        return Color.FromArgb(
            255,
            Component(p, q, hue + (1.0 / 3)),
            Component(p, q, hue),
            Component(p, q, hue - (1.0 / 3)));
    }

    private static byte Component(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        var value = t < 1.0 / 6 ? p + ((q - p) * 6 * t)
            : t < 1.0 / 2 ? q
            : t < 2.0 / 3 ? p + ((q - p) * ((2.0 / 3) - t) * 6)
            : p;
        return (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
    }
}
