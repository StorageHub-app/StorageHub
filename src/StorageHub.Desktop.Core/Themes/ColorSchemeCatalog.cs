using Avalonia.Media;

namespace StorageHub.Desktop.Themes;

/// <summary>
/// Every colour scheme StorageHub ships.
/// </summary>
/// <remarks>
/// <para>
/// Twenty-two, of which twenty are derived from their published palette by
/// <see cref="ColorScheme.FromPalette"/> and two are the house schemes, transcribed. A palette
/// supplies eight colours; the other nineteen tokens come from rules that hold for all of them,
/// which is what keeps a scheme somebody adds next year consistent with these.
/// </para>
/// <para>
/// The names are the palettes' own. They are widely used and widely recognised -- somebody who
/// runs Gruvbox in their editor is looking for the word "Gruvbox" -- and a scheme list is where a
/// person matches a tool to the rest of their desktop. The colours are facts about a published
/// palette; only the derivation around them is StorageHub's.
/// </para>
/// <para>
/// Ids are stable and lowercase. They go into settings.json, so renaming one silently resets a
/// user's choice to the default.
/// </para>
/// </remarks>
internal static class ColorSchemeCatalog
{
    internal const string DefaultDarkId = "storagehub-dark";
    internal const string DefaultLightId = "storagehub-light";

    private static Color C(string hex) => Color.Parse(hex);

    /// <summary>In the order the picker shows them: the house pair, then alphabetical.</summary>
    internal static IReadOnlyList<ColorScheme> All { get; } =
    [
        StorageHubDark(),
        StorageHubLight(),

        ColorScheme.FromPalette(
            "catppuccin-latte", "Catppuccin Latte", isDark: false,
            canvas: C("#EFF1F5"), surface: C("#FFFFFF"), text: C("#4C4F69"), primary: C("#1E66F5"),
            success: C("#40A02B"), warning: C("#DF8E1D"), danger: C("#D20F39"),
            counterpartId: "catppuccin-mocha"),

        ColorScheme.FromPalette(
            "catppuccin-mocha", "Catppuccin Mocha", isDark: true,
            canvas: C("#1E1E2E"), surface: C("#313244"), text: C("#CDD6F4"), primary: C("#89B4FA"),
            success: C("#A6E3A1"), warning: C("#F9E2AF"), danger: C("#F38BA8"),
            counterpartId: "catppuccin-latte"),

        ColorScheme.FromPalette(
            "dracula", "Dracula", isDark: true,
            canvas: C("#282A36"), surface: C("#343746"), text: C("#F8F8F2"), primary: C("#BD93F9"),
            success: C("#50FA7B"), warning: C("#FFB86C"), danger: C("#FF5555")),

        ColorScheme.FromPalette(
            "everforest-dark", "Everforest Dark", isDark: true,
            canvas: C("#2D353B"), surface: C("#343F44"), text: C("#D3C6AA"), primary: C("#7FBBB3"),
            success: C("#A7C080"), warning: C("#DBBC7F"), danger: C("#E67E80")),

        ColorScheme.FromPalette(
            "github-dark", "GitHub Dark", isDark: true,
            canvas: C("#0D1117"), surface: C("#161B22"), text: C("#C9D1D9"), primary: C("#58A6FF"),
            success: C("#3FB950"), warning: C("#D29922"), danger: C("#F85149"),
            counterpartId: "github-light"),

        ColorScheme.FromPalette(
            "github-light", "GitHub Light", isDark: false,
            canvas: C("#FFFFFF"), surface: C("#F6F8FA"), text: C("#24292F"), primary: C("#0969DA"),
            success: C("#1A7F37"), warning: C("#9A6700"), danger: C("#CF222E"),
            counterpartId: "github-dark"),

        ColorScheme.FromPalette(
            "gruvbox-dark", "Gruvbox Dark", isDark: true,
            canvas: C("#282828"), surface: C("#32302F"), text: C("#EBDBB2"), primary: C("#83A598"),
            success: C("#B8BB26"), warning: C("#FABD2F"), danger: C("#FB4934"),
            counterpartId: "gruvbox-light"),

        ColorScheme.FromPalette(
            "gruvbox-light", "Gruvbox Light", isDark: false,
            canvas: C("#FBF1C7"), surface: C("#F2E5BC"), text: C("#3C3836"), primary: C("#076678"),
            success: C("#79740E"), warning: C("#B57614"), danger: C("#9D0006"),
            counterpartId: "gruvbox-dark"),

        // Not a palette so much as a floor: the maximum separation the tokens allow, for anyone who
        // needs it to read the screen at all.
        ColorScheme.FromPalette(
            "high-contrast-dark", "High Contrast Dark", isDark: true,
            canvas: C("#000000"), surface: C("#0A0A0A"), text: C("#FFFFFF"), primary: C("#3FA9FF"),
            success: C("#00E676"), warning: C("#FFD400"), danger: C("#FF5252"),
            counterpartId: "high-contrast-light"),

        ColorScheme.FromPalette(
            "high-contrast-light", "High Contrast Light", isDark: false,
            canvas: C("#FFFFFF"), surface: C("#FFFFFF"), text: C("#000000"), primary: C("#0B57D0"),
            success: C("#0B6B2E"), warning: C("#8A5A00"), danger: C("#B3261E"),
            counterpartId: "high-contrast-dark"),

        ColorScheme.FromPalette(
            "monokai", "Monokai", isDark: true,
            canvas: C("#272822"), surface: C("#2F3129"), text: C("#F8F8F2"), primary: C("#66D9EF"),
            success: C("#A6E22E"), warning: C("#E6DB74"), danger: C("#F92672")),

        ColorScheme.FromPalette(
            "nord", "Nord", isDark: true,
            canvas: C("#2E3440"), surface: C("#3B4252"), text: C("#ECEFF4"), primary: C("#88C0D0"),
            success: C("#A3BE8C"), warning: C("#EBCB8B"), danger: C("#BF616A"),
            counterpartId: "nord-light"),

        // Nord's Snow Storm, which the palette publishes as its light end. The semantic three are
        // Aurora darkened: Aurora at its published lightness cannot carry a white background.
        ColorScheme.FromPalette(
            "nord-light", "Nord Light", isDark: false,
            canvas: C("#ECEFF4"), surface: C("#FFFFFF"), text: C("#2E3440"), primary: C("#5E81AC"),
            success: C("#4F7A3C"), warning: C("#96700F"), danger: C("#A54A52"),
            counterpartId: "nord"),

        ColorScheme.FromPalette(
            "one-dark", "One Dark", isDark: true,
            canvas: C("#282C34"), surface: C("#21252B"), text: C("#ABB2BF"), primary: C("#61AFEF"),
            success: C("#98C379"), warning: C("#E5C07B"), danger: C("#E06C75"),
            counterpartId: "one-light"),

        ColorScheme.FromPalette(
            "one-light", "One Light", isDark: false,
            canvas: C("#FAFAFA"), surface: C("#FFFFFF"), text: C("#383A42"), primary: C("#4078F2"),
            success: C("#50A14F"), warning: C("#C18401"), danger: C("#E45649"),
            counterpartId: "one-dark"),

        ColorScheme.FromPalette(
            "rose-pine", "Rosé Pine", isDark: true,
            canvas: C("#191724"), surface: C("#1F1D2E"), text: C("#E0DEF4"), primary: C("#C4A7E7"),
            success: C("#9CCFD8"), warning: C("#F6C177"), danger: C("#EB6F92"),
            counterpartId: "rose-pine-dawn"),

        ColorScheme.FromPalette(
            "rose-pine-dawn", "Rosé Pine Dawn", isDark: false,
            canvas: C("#FAF4ED"), surface: C("#FFFAF3"), text: C("#575279"), primary: C("#907AA9"),
            success: C("#286983"), warning: C("#B07B23"), danger: C("#B4637A"),
            counterpartId: "rose-pine"),

        ColorScheme.FromPalette(
            "solarized-dark", "Solarized Dark", isDark: true,
            canvas: C("#002B36"), surface: C("#073642"), text: C("#93A1A1"), primary: C("#268BD2"),
            success: C("#859900"), warning: C("#B58900"), danger: C("#DC322F"),
            counterpartId: "solarized-light"),

        ColorScheme.FromPalette(
            "solarized-light", "Solarized Light", isDark: false,
            // base01 at 4.4:1 on base2 misses the 4.5:1 body-text rule by a hair, so the text is
            // one step darker than the published palette. A readability floor the whole catalog is
            // held to is worth more than the last unit of fidelity to one of them.
            canvas: C("#FDF6E3"), surface: C("#EEE8D5"), text: C("#4E6165"), primary: C("#268BD2"),
            success: C("#6C7A00"), warning: C("#96700F"), danger: C("#CB2A27"),
            counterpartId: "solarized-dark"),

        ColorScheme.FromPalette(
            "tokyo-night", "Tokyo Night", isDark: true,
            canvas: C("#1A1B26"), surface: C("#24283B"), text: C("#C0CAF5"), primary: C("#7AA2F7"),
            success: C("#9ECE6A"), warning: C("#E0AF68"), danger: C("#F7768E"))
    ];

    private static readonly Dictionary<string, ColorScheme> ById =
        All.ToDictionary(scheme => scheme.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The scheme with this id, or the house scheme for <paramref name="preferDark"/>.
    /// </summary>
    /// <remarks>
    /// Falling back rather than throwing is deliberate: an id can arrive from a settings file
    /// written by a newer build, or by a hand edit. A missing scheme is a shell that opens looking
    /// like StorageHub, not one that refuses to open.
    /// </remarks>
    internal static ColorScheme Resolve(string? id, bool preferDark)
    {
        if (!string.IsNullOrWhiteSpace(id) && ById.TryGetValue(id.Trim(), out var found))
        {
            return found;
        }

        return ById[preferDark ? DefaultDarkId : DefaultLightId];
    }

    /// <summary>
    /// The member of this scheme's pair that matches <paramref name="preferDark"/>, or the scheme
    /// itself when it has no counterpart.
    /// </summary>
    internal static ColorScheme ForAppearance(ColorScheme scheme, bool preferDark)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        if (scheme.IsDark == preferDark)
        {
            return scheme;
        }

        return scheme.CounterpartId is { } counterpart && ById.TryGetValue(counterpart, out var other)
            ? other
            : scheme;
    }

    /// <summary>
    /// The house dark scheme, exactly as the WinForms shell painted it.
    /// </summary>
    /// <remarks>
    /// Transcribed from UiTheme.cs DarkPalette and pinned by a test, because this is the appearance
    /// the reference captures in docs/ui-reference are compared against.
    /// </remarks>
    private static ColorScheme StorageHubDark() => ColorScheme.Transcribed(
        DefaultDarkId, "StorageHub Dark", isDark: true, counterpartId: DefaultLightId,
        new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            [ColorTokens.Canvas] = C("#16181D"),
            [ColorTokens.Surface] = C("#1E2127"),
            [ColorTokens.SurfaceMuted] = C("#272B33"),
            [ColorTokens.Elevated] = C("#2E323B"),
            [ColorTokens.Border] = C("#3A3F4A"),
            [ColorTokens.Text] = C("#E8EAF0"),
            [ColorTokens.TextMuted] = C("#A0A7B4"),
            [ColorTokens.Primary] = C("#4C8BF5"),
            [ColorTokens.PrimaryHover] = C("#6BA0F8"),
            [ColorTokens.PrimaryPressed] = C("#3872D6"),
            // White on #4C8BF5 is 3.3:1 - above the 3:1 floor for a control label, below the 4.5:1
            // one for body text. Kept because it is what the product has always drawn, and because
            // the reference captures were taken against it; the derivation would have chosen ink.
            [ColorTokens.OnPrimary] = C("#FFFFFF"),
            [ColorTokens.SystemAccent] = C("#4C8BF5"),
            [ColorTokens.SystemAccentLight1] = C("#6BA0F8"),
            [ColorTokens.SystemAccentLight2] = C("#8AB5FA"),
            [ColorTokens.SystemAccentLight3] = C("#A9CAFC"),
            [ColorTokens.SystemAccentDark1] = C("#3872D6"),
            [ColorTokens.SystemAccentDark2] = C("#2C5CAD"),
            [ColorTokens.SystemAccentDark3] = C("#204584"),
            [ColorTokens.Selection] = C("#2B4A7A"),
            [ColorTokens.SelectionPressed] = C("#23395C"),
            [ColorTokens.Input] = C("#14161A"),
            [ColorTokens.DisabledText] = C("#6B7280"),
            [ColorTokens.Success] = C("#4ABE84"),
            [ColorTokens.Warning] = C("#F0B045"),
            [ColorTokens.Danger] = C("#F4696F"),
            [ColorTokens.SuccessTint] = C("#1B2F27"),
            [ColorTokens.WarningTint] = C("#33291A"),
            [ColorTokens.DangerTint] = C("#33201F")
        });

    /// <inheritdoc cref="StorageHubDark"/>
    private static ColorScheme StorageHubLight() => ColorScheme.Transcribed(
        DefaultLightId, "StorageHub Light", isDark: false, counterpartId: DefaultDarkId,
        new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            [ColorTokens.Canvas] = C("#F4F6FA"),
            [ColorTokens.Surface] = C("#FFFFFF"),
            [ColorTokens.SurfaceMuted] = C("#EDF1F7"),
            [ColorTokens.Elevated] = C("#F9FBFD"),
            [ColorTokens.Border] = C("#D3DAE4"),
            [ColorTokens.Text] = C("#1E242D"),
            [ColorTokens.TextMuted] = C("#5C6674"),
            [ColorTokens.Primary] = C("#1867C0"),
            [ColorTokens.PrimaryHover] = C("#297BD6"),
            [ColorTokens.PrimaryPressed] = C("#12529A"),
            [ColorTokens.OnPrimary] = C("#FFFFFF"),
            [ColorTokens.SystemAccent] = C("#1867C0"),
            [ColorTokens.SystemAccentLight1] = C("#297BD6"),
            [ColorTokens.SystemAccentLight2] = C("#4A93E0"),
            [ColorTokens.SystemAccentLight3] = C("#6FAAE9"),
            [ColorTokens.SystemAccentDark1] = C("#12529A"),
            [ColorTokens.SystemAccentDark2] = C("#0E3E75"),
            [ColorTokens.SystemAccentDark3] = C("#092A4F"),
            [ColorTokens.Selection] = C("#DAE8FA"),
            [ColorTokens.SelectionPressed] = C("#C3DAF7"),
            [ColorTokens.Input] = C("#FDFEFF"),
            [ColorTokens.DisabledText] = C("#929AA5"),
            [ColorTokens.Success] = C("#118756"),
            [ColorTokens.Warning] = C("#B05E00"),
            [ColorTokens.Danger] = C("#BE2D37"),
            [ColorTokens.SuccessTint] = C("#E6F6EE"),
            [ColorTokens.WarningTint] = C("#FFF4E0"),
            [ColorTokens.DangerTint] = C("#FDECEC")
        });
}
