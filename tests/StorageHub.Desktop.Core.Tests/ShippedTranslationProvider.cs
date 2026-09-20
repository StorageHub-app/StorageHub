using System.Globalization;
using System.Reflection;
using System.Text.Json;
using CodeLogic.Core.Localization;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Reads the shipped translation files directly, so a test can render the shell in Danish or
/// German without starting the framework.
/// </summary>
/// <remarks>
/// This deliberately reproduces the merge the framework performs rather than calling it: a key the
/// translation does not carry keeps its English text. That is the behaviour the UI actually has,
/// and it is what makes a partially translated file safe.
/// </remarks>
public sealed class ShippedTranslationProvider : IDesktopStringProvider
{
    private readonly string _culture;
    private readonly Dictionary<Type, LocalizationModelBase> _cache = [];

    internal ShippedTranslationProvider(string culture) => _culture = culture;

    /// <summary>The languages a layout is worth checking against, English included.</summary>
    public static IEnumerable<object[]> Cultures =>
        [["en-US"], ["da-DK"], ["de-DE"]];

    public T Get<T>() where T : LocalizationModelBase, new()
    {
        if (_cache.TryGetValue(typeof(T), out var cached))
        {
            return (T)cached;
        }

        var model = new T();
        var section = typeof(T).GetCustomAttribute<LocalizationSectionAttribute>()?.SectionName;
        var path = section is null
            ? null
            : Path.Combine(AppContext.BaseDirectory, "localization", $"{section}.{_culture}.json");

        if (path is not null && File.Exists(path))
        {
            var translated = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            if (translated is not null)
            {
                foreach (var property in typeof(T).GetProperties(
                             BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (!property.CanWrite || property.PropertyType != typeof(string))
                    {
                        continue;
                    }

                    var key = string.Concat(
                        property.Name[..1].ToLower(CultureInfo.InvariantCulture),
                        property.Name[1..]);
                    if (translated.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                    {
                        property.SetValue(model, value);
                    }
                }
            }
        }

        _cache[typeof(T)] = model;
        return model;
    }

    /// <summary>Renders <paramref name="action"/> in one language, then restores English.</summary>
    internal static void InCulture(string culture, Action action)
    {
        Ui.UseProvider(new ShippedTranslationProvider(culture));

        // The words come from the file above, but anything the shell asks .NET for — a weekday, a
        // month — reads Ui.Culture, so a test that only swapped the provider would still render
        // those in English and prove nothing.
        Ui.UseCulture(culture);
        try
        {
            action();
        }
        finally
        {
            Ui.UseFallback();
        }
    }
}
