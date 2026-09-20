using Avalonia;
using Avalonia.Media;

namespace StorageHub.Desktop.Themes;

/// <summary>
/// The design tokens, reached from code.
/// </summary>
/// <remarks>
/// Views take their tokens from DesignTokens.axaml through StaticResource. Code cannot, so it comes
/// here instead of writing a number - which is the rule that keeps the shell's appearance in one
/// file rather than in one file plus a hundred literals. Nothing in this type scales anything: the
/// framework lays out in device-independent pixels, so a value written once is right at every
/// display scaling on every platform.
/// </remarks>
public static class DesignTokens
{
    /// <summary>The families the shell draws with, chosen for the platform it is running on.</summary>
    /// <remarks>
    /// Segoe UI does not exist on Linux, and naming it there does not fail - it silently falls back
    /// to whatever the system considers a default sans, at normal weight, which is how a shell ends
    /// up looking subtly wrong rather than obviously broken. The fallback chains name real families
    /// per platform and end in a generic, so there is always something to draw with.
    /// </remarks>
    public static FontFamily UiFont { get; } = new(
        OperatingSystem.IsWindows()
            ? "Segoe UI, Arial, sans-serif"
            : "Inter, Ubuntu, Cantarell, DejaVu Sans, Liberation Sans, sans-serif");

    public static FontFamily MonospaceFont { get; } = new(
        OperatingSystem.IsWindows()
            ? "Cascadia Mono, Consolas, Courier New, monospace"
            : "JetBrains Mono, DejaVu Sans Mono, Liberation Mono, Ubuntu Mono, monospace");

    /// <summary>Publishes the values that cannot be written in XAML into the application's resources.</summary>
    public static void Apply(global::Avalonia.Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.Resources["AppFontUi"] = UiFont;
        application.Resources["AppFontMono"] = MonospaceFont;
    }

    /// <summary>Reads a token, failing loudly rather than falling back to an invented value.</summary>
    /// <remarks>
    /// A missing token is a typo, and a typo that silently returns zero produces a layout nobody can
    /// explain. This is the one place that turns it back into an exception with the key in it.
    /// </remarks>
    public static T Get<T>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var application = global::Avalonia.Application.Current
            ?? throw new InvalidOperationException("No application is running.");

        if (application.Resources.TryGetResource(key, application.ActualThemeVariant, out var value) &&
            value is T typed)
        {
            return typed;
        }

        throw new KeyNotFoundException($"The design token '{key}' is not defined.");
    }
}
