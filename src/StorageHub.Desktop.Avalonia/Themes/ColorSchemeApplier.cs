using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace StorageHub.Desktop.Themes;

/// <summary>
/// Puts a <see cref="ColorScheme"/> in front of the design tokens, and repaints the live tree.
/// </summary>
/// <remarks>
/// <para>
/// Merged on top rather than replacing DesignTokens.axaml. That file keeps the two house schemes
/// in ThemeDictionaries, which is what the shell looks like before any scheme has been chosen and
/// what a headless test measures without arranging anything; this dictionary shadows those colours
/// while a scheme is active.
/// </para>
/// <para>
/// Only <c>Color</c> entries are written. Every brush in DesignTokens.axaml points at a colour by
/// name through DynamicResource, so replacing the colours repaints every brush -- which is the
/// whole reason the tokens were split into a colour and a brush in the first place, and what
/// deletes the 250-line recursive theme walker the WinForms shell needed to do the same thing.
/// </para>
/// </remarks>
internal static class ColorSchemeApplier
{
    private static ResourceDictionary? _active;

    /// <summary>The scheme currently applied, or null while the shell is on its defaults.</summary>
    internal static ColorScheme? Current { get; private set; }

    internal static void Apply(global::Avalonia.Application application, ColorScheme scheme)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(scheme);

        var dictionary = new ResourceDictionary();
        foreach (var token in ColorTokens.All)
        {
            // Indexed rather than TryGetValue: a scheme missing a token is a bug in the catalog,
            // and the alternative is a shell that paints one surface in the previous scheme's
            // colour and gives nobody a reason why.
            dictionary[token] = scheme.Tokens[token];
        }

        var resources = application.Resources.MergedDictionaries;
        if (_active is not null)
        {
            _ = resources.Remove(_active);
        }

        resources.Add(dictionary);
        _active = dictionary;
        Current = scheme;

        // Fluent still draws a handful of things from the variant rather than from a token - the
        // default scrollbar and the caret among them - so the variant has to agree with the scheme
        // or a dark scheme gets a light scrollbar.
        application.RequestedThemeVariant = scheme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    /// <summary>Drops back to the tokens as authored. Used by tests, so one cannot leak into another.</summary>
    internal static void Reset(global::Avalonia.Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (_active is not null)
        {
            _ = application.Resources.MergedDictionaries.Remove(_active);
            _active = null;
        }

        Current = null;
        application.RequestedThemeVariant = ThemeVariant.Dark;
    }

    /// <summary>
    /// The colour a token resolves to right now, for a test or a preview swatch.
    /// </summary>
    internal static Color Resolve(global::Avalonia.Application application, string token)
    {
        ArgumentNullException.ThrowIfNull(application);
        return application.Resources.TryGetResource(token, application.ActualThemeVariant, out var value)
            && value is Color colour
                ? colour
                : throw new KeyNotFoundException($"The colour token '{token}' is not defined.");
    }
}
