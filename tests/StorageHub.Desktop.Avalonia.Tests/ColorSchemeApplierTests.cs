using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Avalonia.Tests;

/// <summary>
/// That choosing a scheme repaints what is already on screen, without rebuilding it.
/// </summary>
/// <remarks>
/// The WinForms shell needed a 250-line recursive walker to do this, because a colour there was a
/// value copied onto every control the moment it was created. Here a brush points at a colour by
/// name, so replacing the colours is the whole operation -- and this suite is what says so rather
/// than the comment claiming it.
/// </remarks>
public sealed class ColorSchemeApplierTests : IDisposable
{
    /// <summary>A scheme is application-wide state, so a test that sets one has to put it back.</summary>
    public void Dispose() => ColorSchemeApplier.Reset(global::Avalonia.Application.Current!);

    [AvaloniaFact]
    public void ApplyingASchemeChangesWhatTheTokensResolveTo()
    {
        var application = global::Avalonia.Application.Current!;
        var dracula = ColorSchemeCatalog.Resolve("dracula", preferDark: true);

        ColorSchemeApplier.Apply(application, dracula);

        Assert.Equal(dracula.Tokens[ColorTokens.Canvas], ColorSchemeApplier.Resolve(application, ColorTokens.Canvas));
        Assert.Equal(dracula.Tokens[ColorTokens.Primary], ColorSchemeApplier.Resolve(application, ColorTokens.Primary));
        Assert.Equal(dracula, ColorSchemeApplier.Current);
    }

    [AvaloniaFact]
    public void AWindowAlreadyOpenTakesTheNewColoursWithoutBeingRebuilt()
    {
        var application = global::Avalonia.Application.Current!;
        var window = new MainWindow { DataContext = ShellPreview.Sample };
        window.Show();
        window.UpdateLayout();

        var before = Background(window);
        ColorSchemeApplier.Apply(application, ColorSchemeCatalog.Resolve("gruvbox-light", preferDark: false));
        window.UpdateLayout();

        var after = Background(window);

        Assert.NotEqual(before, after);
        Assert.Equal(
            ColorSchemeCatalog.Resolve("gruvbox-light", preferDark: false).Tokens[ColorTokens.Canvas],
            after);
    }

    [AvaloniaFact]
    public void SwitchingSchemesLeavesOneDictionaryBehindRatherThanTwenty()
    {
        var application = global::Avalonia.Application.Current!;
        var before = application.Resources.MergedDictionaries.Count;

        foreach (var scheme in ColorSchemeCatalog.All)
        {
            ColorSchemeApplier.Apply(application, scheme);
        }

        Assert.Equal(before + 1, application.Resources.MergedDictionaries.Count);
    }

    /// <summary>A dark scheme must not leave a light scrollbar behind, or the reverse.</summary>
    [AvaloniaFact]
    public void TheVariantFollowsTheScheme()
    {
        var application = global::Avalonia.Application.Current!;

        ColorSchemeApplier.Apply(application, ColorSchemeCatalog.Resolve("solarized-light", preferDark: false));
        Assert.Equal(ThemeVariant.Light, application.RequestedThemeVariant);

        ColorSchemeApplier.Apply(application, ColorSchemeCatalog.Resolve("solarized-dark", preferDark: true));
        Assert.Equal(ThemeVariant.Dark, application.RequestedThemeVariant);
    }

    [AvaloniaFact]
    public void ResettingPutsTheAuthoredTokensBack()
    {
        var application = global::Avalonia.Application.Current!;
        var authored = ColorSchemeApplier.Resolve(application, ColorTokens.Canvas);

        ColorSchemeApplier.Apply(application, ColorSchemeCatalog.Resolve("monokai", preferDark: true));
        ColorSchemeApplier.Reset(application);

        Assert.Equal(authored, ColorSchemeApplier.Resolve(application, ColorTokens.Canvas));
        Assert.Null(ColorSchemeApplier.Current);
    }

    /// <summary>
    /// Renders the shell in every scheme, for a human to look at.
    /// </summary>
    /// <remarks>
    /// Twenty-two is past the number anybody checks by hand, which is the argument for the contrast
    /// rules in ColorSchemeTests -- but a contrast ratio cannot tell you a scheme looks like itself.
    /// Set STORAGEHUB_SHOT_DIR to keep the files.
    /// </remarks>
    [AvaloniaFact]
    public void EverySchemeCanBePhotographed()
    {
        var application = global::Avalonia.Application.Current!;
        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");

        foreach (var scheme in ColorSchemeCatalog.All)
        {
            ColorSchemeApplier.Apply(application, scheme);

            var window = new MainWindow { DataContext = ShellPreview.Sample };
            window.Show();
            window.Measure(new Size(1500, 920));
            window.Arrange(new Rect(0, 0, 1500, 920));

            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);

            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            Directory.CreateDirectory(directory);
            using var stream = File.Create(Path.Combine(directory, $"scheme-{scheme.Id}.png"));
            frame!.Save(stream, new PngBitmapEncoderOptions());
        }
    }

    /// <summary>The window's own background, which is the canvas token every scheme moves.</summary>
    private static Color Background(Window window)
    {
        Assert.True(window.Background is ISolidColorBrush, "The window should paint the canvas.");
        return ((ISolidColorBrush)window.Background!).Color;
    }
}
