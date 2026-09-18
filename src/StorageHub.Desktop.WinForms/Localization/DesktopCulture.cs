using System.Globalization;

namespace StorageHub.Desktop.Localization;

/// <summary>
/// Which language the shell speaks.
/// </summary>
internal static class DesktopCulture
{
    /// <summary>The language the strings are authored in, and the fallback for every other one.</summary>
    internal const string DefaultCulture = "en-US";

    /// <summary>The setting value meaning "follow Windows".</summary>
    internal const string AutomaticLanguage = "auto";

    /// <summary>
    /// Overrides the shell's language for one launch.
    /// </summary>
    /// <remarks>
    /// Matches the existing STORAGEHUB_DATA_ROOT convention. It exists so a translation can be
    /// checked, and a language-specific report reproduced, without changing the Windows display
    /// language and signing out.
    /// </remarks>
    internal const string LanguageEnvironmentVariable = "STORAGEHUB_LANGUAGE";

    /// <summary>The language for this launch, from the environment or the configured preference.</summary>
    internal static string ResolveCurrent(string? configured) =>
        Resolve(
            Environment.GetEnvironmentVariable(LanguageEnvironmentVariable) is { Length: > 0 } environment
                ? environment
                : configured,
            CultureInfo.CurrentUICulture);

    /// <summary>
    /// The languages shipped with StorageHub.
    /// </summary>
    /// <remarks>
    /// Also what gets written into the framework's supportedCultures, because CodeLogic scaffolds
    /// that list with en-US alone and only generates and loads the cultures named in it.
    /// </remarks>
    internal static IReadOnlyList<string> SupportedCultures { get; } = [DefaultCulture, "da-DK", "de-DE"];

    /// <summary>
    /// Resolves the language to use from a configured preference and the operating system.
    /// </summary>
    /// <param name="configured">
    /// A specific culture name, or <see cref="AutomaticLanguage"/>/<see langword="null"/> to follow
    /// Windows. A configured language that is not shipped is ignored rather than honoured, so a
    /// stale setting cannot leave the shell without words.
    /// </param>
    /// <param name="uiCulture">The operating system's UI culture.</param>
    internal static string Resolve(string? configured, CultureInfo uiCulture)
    {
        ArgumentNullException.ThrowIfNull(uiCulture);

        if (!string.IsNullOrWhiteSpace(configured) &&
            !string.Equals(configured, AutomaticLanguage, StringComparison.OrdinalIgnoreCase) &&
            Match(configured) is { } chosen)
        {
            return chosen;
        }

        return Match(uiCulture.Name)
               ?? Match(uiCulture.TwoLetterISOLanguageName)
               ?? DefaultCulture;
    }

    /// <summary>
    /// Whether a stored <c>Language</c> setting is one this build can honour: a shipped culture,
    /// or <see cref="AutomaticLanguage"/>. Anything else is repaired back to automatic rather than
    /// kept, so a setting written by a build that shipped more languages does not survive a
    /// downgrade as a value nothing understands.
    /// </summary>
    internal static bool IsSupportedSetting(string? value) =>
        string.Equals(value, AutomaticLanguage, StringComparison.OrdinalIgnoreCase) ||
        SupportedCultures.Any(culture => string.Equals(culture, value, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Finds the shipped culture for a name, accepting a bare language so that a Danish user on
    /// "da" or "da-GL" still gets Danish rather than English.
    /// </summary>
    private static string? Match(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var exact = SupportedCultures.FirstOrDefault(culture =>
            string.Equals(culture, name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var language = name.Split('-')[0];
        return SupportedCultures.FirstOrDefault(culture =>
            culture.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase));
    }
}
