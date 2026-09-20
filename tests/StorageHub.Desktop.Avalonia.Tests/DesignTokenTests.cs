using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using StorageHub.Desktop.Themes;
using Xunit;

namespace StorageHub.Desktop.Avalonia.Tests;

/// <summary>
/// The tokens, and the promise that they are the only place appearance is decided.
/// </summary>
/// <remarks>
/// Asserting the palette here is what turns "the Avalonia shell looks like the WinForms one" from an
/// intention into a build-time guarantee. The values are transcribed from UiTheme.cs, so a drift in
/// either direction fails rather than being noticed later in a screenshot.
/// </remarks>
public class DesignTokenTests
{
    /// <summary>Every semantic slot UiTheme declared, in both appearances.</summary>
    public static TheoryData<string> Slots() =>
    [
        "CanvasColor", "SurfaceColor", "SurfaceMutedColor", "ElevatedColor", "BorderColor",
        "TextColor", "TextMutedColor", "PrimaryColor", "PrimaryHoverColor", "PrimaryPressedColor",
        "SelectionColor", "SelectionPressedColor", "InputColor", "DisabledTextColor",
        "SuccessColor", "WarningColor", "DangerColor", "SuccessTintColor", "WarningTintColor",
        "DangerTintColor",
    ];

    [AvaloniaTheory]
    [MemberData(nameof(Slots))]
    public void EverySlotIsDefinedInBothAppearances(string slot)
    {
        var resources = global::Avalonia.Application.Current!.Resources;

        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            Assert.True(
                resources.TryGetResource(slot, variant, out var value) && value is Color,
                $"{slot} is not defined for {variant}");
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Slots))]
    public void EverySlotHasABrushOfItsOwn(string slot)
    {
        // A view names a brush and never constructs one, which is what lets a variant change repaint
        // the live tree instead of needing a walk over it.
        var brushKey = slot.Replace("Color", "Brush", StringComparison.Ordinal);

        Assert.True(
            global::Avalonia.Application.Current!.Resources.TryGetResource(
                brushKey, ThemeVariant.Dark, out var value) && value is ISolidColorBrush,
            $"{brushKey} is missing");
    }

    [AvaloniaFact]
    public void TheDarkPaletteIsTheOneUiThemeDeclares()
    {
        Assert.Equal(Color.FromRgb(22, 24, 29), Token<Color>("CanvasColor", ThemeVariant.Dark));
        Assert.Equal(Color.FromRgb(30, 33, 39), Token<Color>("SurfaceColor", ThemeVariant.Dark));
        Assert.Equal(Color.FromRgb(76, 139, 245), Token<Color>("PrimaryColor", ThemeVariant.Dark));
        Assert.Equal(Color.FromRgb(232, 234, 240), Token<Color>("TextColor", ThemeVariant.Dark));
        Assert.Equal(Color.FromRgb(244, 105, 111), Token<Color>("DangerColor", ThemeVariant.Dark));
    }

    [AvaloniaFact]
    public void TheLightPaletteIsTheOneUiThemeDeclares()
    {
        Assert.Equal(Color.FromRgb(244, 246, 250), Token<Color>("CanvasColor", ThemeVariant.Light));
        Assert.Equal(Color.FromRgb(255, 255, 255), Token<Color>("SurfaceColor", ThemeVariant.Light));
        Assert.Equal(Color.FromRgb(24, 103, 192), Token<Color>("PrimaryColor", ThemeVariant.Light));
        Assert.Equal(Color.FromRgb(30, 36, 45), Token<Color>("TextColor", ThemeVariant.Light));
    }

    /// <summary>The stock Fluent accent does not survive into the shell.</summary>
    /// <remarks>
    /// FluentTheme paints the selected-tab pipe, focus adorners, checkbox fills and scrollbar thumbs
    /// from SystemAccentColor rather than from anything this project names. Overriding the ramp per
    /// variant is what keeps "the palette lives in one file" true for the controls the shell did not
    /// author, and this is the test that notices when a new Fluent version reaches for another step.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData("SystemAccentColor")]
    [InlineData("SystemAccentColorLight1")]
    [InlineData("SystemAccentColorLight2")]
    [InlineData("SystemAccentColorLight3")]
    [InlineData("SystemAccentColorDark1")]
    [InlineData("SystemAccentColorDark2")]
    [InlineData("SystemAccentColorDark3")]
    public void TheFluentAccentRampIsOurs(string step)
    {
        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            Assert.True(
                global::Avalonia.Application.Current!.Resources.TryGetResource(
                    step, variant, out var value) && value is Color,
                $"{step} is not overridden for {variant}");
        }

        Assert.Equal(Token<Color>("PrimaryColor", ThemeVariant.Dark), Token<Color>("SystemAccentColor", ThemeVariant.Dark));
        Assert.Equal(Token<Color>("PrimaryHoverColor", ThemeVariant.Dark), Token<Color>("SystemAccentColorLight1", ThemeVariant.Dark));
        Assert.Equal(Token<Color>("PrimaryPressedColor", ThemeVariant.Dark), Token<Color>("SystemAccentColorDark1", ThemeVariant.Dark));
    }

    /// <summary>StorageHub opens dark, the way the WinForms shell did.</summary>
    /// <remarks>
    /// The variant was Default first, which asks the platform - and a headless platform, a fresh
    /// container or a Linux desktop with no portal all answer Light. The shell rendered white, which
    /// a screenshot caught and no palette test could have.
    /// </remarks>
    [AvaloniaFact]
    public void TheShellOpensDark()
    {
        Assert.Equal(ThemeVariant.Dark, global::Avalonia.Application.Current!.ActualThemeVariant);
    }

    [AvaloniaFact]
    public void TheMetricsTheShellLaysOutWithAreAllDeclared()
    {
        // If any of these were a literal in a view instead, changing the shell's density would mean
        // finding it. This is the list that keeps that from happening again.
        foreach (var key in new[]
                 {
                     "SpaceXs", "SpaceSm", "SpaceMd", "SpaceLg", "SpaceXl", "Space2Xl",
                     "ToolbarHeight", "StatusBarHeight", "ListRowHeight", "TreeRowHeight",
                     "SidebarMinWidth", "SidebarMaxWidth", "SplitterThickness",
                     "TrailingZoneWidth", "DenseTrailingZoneWidth",
                     "FontSizeCaption", "FontSizeBody", "FontSizeSubtitle", "FontSizeTitle",
                     "FontSizeHeading",
                     "IconSizeSm", "IconSizeMd", "IconSizeLg", "IconSizeXl",
                 })
        {
            Assert.True(DesignTokens.Get<double>(key) > 0, $"{key} is missing or not positive");
        }
    }

    [AvaloniaFact]
    public void TheFieldMetricsAreTheOnesFieldChromeDeclared()
    {
        // StorageHubFieldChrome: 10 horizontal, 8 vertical, radius 5, card radius 7, dense 8/3/4.
        Assert.Equal(new Thickness(10, 8), DesignTokens.Get<Thickness>("FieldPadding"));
        Assert.Equal(new Thickness(8, 3), DesignTokens.Get<Thickness>("DenseFieldPadding"));
        Assert.Equal(new CornerRadius(5), DesignTokens.Get<CornerRadius>("FieldRadius"));
        Assert.Equal(new CornerRadius(7), DesignTokens.Get<CornerRadius>("CardRadius"));
        Assert.Equal(24d, DesignTokens.Get<double>("TrailingZoneWidth"));
    }

    [AvaloniaFact]
    public void AMissingTokenSaysWhichOne()
    {
        var error = Assert.Throws<KeyNotFoundException>(
            () => DesignTokens.Get<double>("SpaceEnormous"));

        Assert.Contains("SpaceEnormous", error.Message, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void TheFontsNameFamiliesThisPlatformActuallyHas()
    {
        // Segoe UI does not exist on Linux, and naming it there does not fail - it quietly falls
        // back, which is how a shell ends up subtly wrong rather than obviously broken.
        var ui = DesignTokens.UiFont.Name;

        Assert.Contains(OperatingSystem.IsWindows() ? "Segoe UI" : "Inter", ui, StringComparison.Ordinal);
        Assert.NotEqual(DesignTokens.UiFont.Name, DesignTokens.MonospaceFont.Name);
    }

    private static T Token<T>(string key, ThemeVariant variant) =>
        global::Avalonia.Application.Current!.Resources.TryGetResource(key, variant, out var value) &&
        value is T typed
            ? typed
            : throw new KeyNotFoundException(key);
}
