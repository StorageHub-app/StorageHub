using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodeLogic.Core.Localization;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Rules that hold for every localization model, enforced by reflection so a model added later is
/// covered without anyone remembering to extend this file.
/// </summary>
public sealed partial class LocalizationModelTests
{
    /// <summary>
    /// Every localization model the shell declares, discovered rather than listed.
    /// </summary>
    /// <remarks>
    /// A hand-kept list silently stops covering a model the moment someone adds one, which is
    /// exactly when these rules matter most: a registered model with no shipped translation, or
    /// one whose placeholders drifted, would pass simply by not being mentioned here.
    /// </remarks>
    private static readonly Type[] Models =
        [.. typeof(ShellStrings).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(LocalizationModelBase).IsAssignableFrom(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal)];

    /// <summary>
    /// A composite-format placeholder, with the optional alignment and format specifier that
    /// <c>{0:N0}</c> carries. Matching only <c>{0}</c> would quietly treat a formatted placeholder
    /// as literal text.
    /// </summary>
    [GeneratedRegex(@"\{(\d+)(?:,-?\d+)?(?::[^}]*)?\}")]
    private static partial Regex Placeholder { get; }

    /// <summary>
    /// The merge that provides per-key fallback to English is top-level only, so a nested value
    /// would replace a whole subtree instead of falling back key by key.
    /// </summary>
    [Fact]
    public void ModelsExposeOnlyFlatStringProperties()
    {
        foreach (var model in Models)
        {
            Assert.All(Properties(model), property =>
                Assert.True(
                    property.PropertyType == typeof(string),
                    $"{model.Name}.{property.Name} is {property.PropertyType.Name}; only string is supported."));
        }
    }

    /// <summary>
    /// <see cref="LocalizationModelBase"/> already defines Culture, and it is written into every
    /// generated file. A model redefining it would collide with that.
    /// </summary>
    [Fact]
    public void NoModelRedefinesCulture()
    {
        foreach (var model in Models)
        {
            Assert.DoesNotContain(
                Properties(model),
                property => property.Name == nameof(LocalizationModelBase.Culture));
        }
    }

    [Fact]
    public void EveryStringHasEnglishTextToFallBackOn()
    {
        foreach (var model in Models)
        {
            var instance = (LocalizationModelBase)Activator.CreateInstance(model)!;
            Assert.All(Properties(model), property =>
                Assert.False(
                    string.IsNullOrWhiteSpace((string?)property.GetValue(instance)),
                    $"{model.Name}.{property.Name} has no English default."));
        }
    }

    /// <summary>
    /// A format string has to be recognisable as one from its name alone, because the call site is
    /// what has to pass arguments to it.
    /// </summary>
    [Fact]
    public void PropertiesTakingArgumentsAreNamedFormat()
    {
        foreach (var model in Models)
        {
            var instance = (LocalizationModelBase)Activator.CreateInstance(model)!;
            foreach (var property in Properties(model))
            {
                var value = (string?)property.GetValue(instance) ?? string.Empty;
                var takesArguments = Placeholder.IsMatch(value);
                var namedFormat = property.Name.EndsWith("Format", StringComparison.Ordinal);

                Assert.True(
                    takesArguments == namedFormat,
                    takesArguments
                        ? $"{model.Name}.{property.Name} contains a placeholder but is not named ...Format."
                        : $"{model.Name}.{property.Name} is named ...Format but contains no placeholder.");
            }
        }
    }

    /// <summary>
    /// A translated format string that renumbers its placeholders throws at the moment the message
    /// is shown, which is typically a rare error path. Catch it here instead.
    /// </summary>
    [Theory]
    [InlineData("da-DK")]
    [InlineData("de-DE")]
    public void ShippedTranslationsUseTheSamePlaceholdersAsEnglish(string culture)
    {
        foreach (var model in Models)
        {
            var english = (LocalizationModelBase)Activator.CreateInstance(model)!;
            var translated = Load(model, culture);

            foreach (var property in Properties(model))
            {
                var key = Camel(property.Name);
                if (!translated.TryGetValue(key, out var value))
                {
                    // A missing key falls back to English, so it is not a placeholder problem.
                    // That it is missing at all is what
                    // ShippedTranslationsCoverEveryString reports.
                    continue;
                }

                var expected = Indices((string?)property.GetValue(english));
                Assert.Equal(expected, Indices(value));
            }
        }
    }

    /// <summary>
    /// An empty string wins over the English default during the merge, so it is worse than an
    /// absent key: it produces a blank menu entry rather than an English one.
    /// </summary>
    [Theory]
    [InlineData("da-DK")]
    [InlineData("de-DE")]
    public void ShippedTranslationsContainNoEmptyValues(string culture)
    {
        foreach (var model in Models)
        {
            foreach (var pair in Load(model, culture))
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(pair.Value),
                    $"{Section(model)}.{culture}.json has an empty value for '{pair.Key}'.");
            }
        }
    }

    [Theory]
    [InlineData("da-DK")]
    [InlineData("de-DE")]
    public void ShippedTranslationsDeclareTheirOwnCulture(string culture)
    {
        foreach (var model in Models)
        {
            Assert.Equal(culture, Load(model, culture)["culture"]);
        }
    }

    /// <summary>
    /// Every string a model declares must be translated in every shipped language.
    /// </summary>
    /// <remarks>
    /// A missing key is not broken — the merge falls back to English, so the window still renders.
    /// It is invisible, which is the problem this catches. The shell section once carried 40 of its
    /// 123 strings while every other section was complete, because the file was written when the
    /// model was smaller and nothing failed as the model grew. A translation is either shipped or
    /// it is not; "mostly translated" is a state no one can see from the outside.
    ///
    /// The failure message names the keys, so the fix is to add them to the file it names.
    /// </remarks>
    [Theory]
    [InlineData("da-DK")]
    [InlineData("de-DE")]
    public void ShippedTranslationsCoverEveryString(string culture)
    {
        foreach (var model in Models)
        {
            var translated = Load(model, culture);
            var missing = Properties(model)
                .Select(property => Camel(property.Name))
                .Where(key => !translated.ContainsKey(key))
                .ToArray();

            Assert.True(
                missing.Length == 0,
                $"{Section(model)}.{culture}.json is missing {missing.Length} of " +
                $"{Properties(model).Length} strings: {string.Join(", ", missing)}");
        }
    }

    /// <summary>Every key in a shipped file must still exist on the model it translates.</summary>
    [Theory]
    [InlineData("da-DK")]
    [InlineData("de-DE")]
    public void ShippedTranslationsHaveNoKeysTheModelDropped(string culture)
    {
        foreach (var model in Models)
        {
            var known = Properties(model).Select(property => Camel(property.Name)).ToHashSet(StringComparer.Ordinal);
            known.Add("culture");

            foreach (var key in Load(model, culture).Keys)
            {
                Assert.True(known.Contains(key), $"{Section(model)}.{culture}.json translates unknown key '{key}'.");
            }
        }
    }

    private static PropertyInfo[] Properties(Type model) =>
        [.. model.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.CanRead && property.CanWrite)];

    private static string Section(Type model) =>
        model.GetCustomAttribute<LocalizationSectionAttribute>()!.SectionName;

    private static Dictionary<string, string> Load(Type model, string culture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "localization", $"{Section(model)}.{culture}.json");
        Assert.True(File.Exists(path), $"Shipped translation is missing: {path}");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
    }

    private static IEnumerable<string> Indices(string? value) =>
        Placeholder.Matches(value ?? string.Empty)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(index => index, StringComparer.Ordinal);

    private static string Camel(string name) =>
        string.Concat(name[..1].ToLower(CultureInfo.InvariantCulture), name[1..]);
}
