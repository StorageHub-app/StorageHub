using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Lucide.Avalonia;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Text and icons drawn on a filled button take the fill's contrast colour, in both appearances.
/// </summary>
/// <remarks>
/// <para>
/// Written after the sidebar's New button was found drawing a near-black label on a blue fill in
/// light appearance. It looked right in dark only by accident: the page's text colour there is
/// near-white, so the same bug was invisible.
/// </para>
/// <para>
/// The cause is worth stating, because it will recur. A style setter beats inheritance, so a
/// blanket <c>Style Selector="TextBlock"</c> that sets Foreground overrides whatever the button
/// containing it set -- but only for content written out in markup. A button whose Content is a
/// bare string gets its TextBlock from the template, which that selector never matched, so the
/// two kinds of button disagreed. The default now lives on the Window and is inherited.
/// </para>
/// </remarks>
public class FilledButtonContrastTests
{
    [AvaloniaFact]
    public void TheSidebarsNewButtonIsReadableInBothAppearances()
    {
        try
        {
            foreach (var variant in (ThemeVariant[])[ThemeVariant.Light, ThemeVariant.Dark])
            {
                global::Avalonia.Application.Current!.RequestedThemeVariant = variant;

                var window = new MainWindow { DataContext = ShellPreview.Sample };
                window.Show();
                window.UpdateLayout();

                var button = window.GetVisualDescendants()
                    .OfType<Button>()
                    .First(candidate => candidate.Classes.Contains("primary"));

                // Everything the button draws, not just the label: the icon has its own muted
                // default, which is exactly what made the plus sign invisible too.
                // Whatever the active scheme says belongs on its accent, not a literal white:
                // half the shipped schemes have a pastel accent that only a dark label sits on.
                var expected = ColorSchemeApplier.Resolve(
                    global::Avalonia.Application.Current!,
                    ColorTokens.OnPrimary);

                foreach (var text in button.GetVisualDescendants().OfType<TextBlock>())
                {
                    Assert.Equal(expected, Solid(text.Foreground, variant));
                }

                foreach (var icon in button.GetVisualDescendants().OfType<LucideIcon>())
                {
                    Assert.Equal(expected, Solid(icon.Foreground, variant));
                }
            }
        }
        finally
        {
            global::Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    private static Color Solid(IBrush? brush, ThemeVariant variant)
    {
        Assert.True(brush is ISolidColorBrush, $"No solid brush in {variant}.");
        return ((ISolidColorBrush)brush!).Color;
    }
}
