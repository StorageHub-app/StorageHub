using System.Reflection;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// When applying a language offers a restart, and when it stays quiet.
/// </summary>
/// <remarks>
/// Every window reads its text as it is built, so a language change only reaches the screen by
/// rebuilding them — which the shell does by relaunching itself. The rule worth pinning down is
/// which changes are worth interrupting someone for: the setting and the language on screen are
/// not the same thing, and a change to the setting that resolves to the same culture must not
/// prompt.
/// </remarks>
public sealed class LanguageRestartTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "storagehub-language-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        DesktopRestart.Reset();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A temp directory that will not delete is not a test failure.
        }
    }

    [Fact]
    public void ApplyingADifferentLanguageOffersARestart()
    {
        var asked = 0;
        Assert.True(Apply("de-DE", answer: true, onAsk: _ => asked++, out var form));
        Assert.Equal(1, asked);
        Assert.True(form);
    }

    [Fact]
    public void DecliningTheRestartStillSavesTheSetting()
    {
        Assert.True(Apply("de-DE", answer: false, onAsk: null, out var restart));
        Assert.False(restart);

        // The point of declining: the language is still what the next launch will use.
        var store = new DesktopConfigStore(_root);
        Assert.Equal("de-DE", store.Load().Language);
    }

    /// <summary>
    /// The language named in the prompt is the one being switched to, in itself. Someone who has
    /// landed in a language they cannot read has to be able to recognize the way out.
    /// </summary>
    [Fact]
    public void ThePromptNamesTheLanguageInItsOwnWords()
    {
        string? named = null;
        _ = Apply("da-DK", answer: false, onAsk: language => named = language, out _);
        Assert.Equal("dansk (Danmark)", named, ignoreCase: true);
    }

    /// <summary>
    /// The case the naive check gets wrong: on a Danish Windows, moving from "Same as Windows" to
    /// "Dansk" writes a different setting and changes nothing on screen.
    /// </summary>
    [Fact]
    public void AChangeThatResolvesToTheSameLanguageDoesNotPrompt()
    {
        var current = DesktopCulture.ResolveCurrent(DesktopCulture.AutomaticLanguage);
        var asked = 0;
        Assert.True(Apply(current, answer: true, onAsk: _ => asked++, out var restart, from: DesktopCulture.AutomaticLanguage));
        Assert.Equal(0, asked);
        Assert.False(restart);
    }

    /// <summary>Runs one Apply, and reports whether it asked for a restart.</summary>
    private bool Apply(
        string language,
        bool answer,
        Action<string>? onAsk,
        out bool restartRequested,
        string from = "en-US")
    {
        var requested = false;
        var saved = false;
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            var store = new DesktopConfigStore(_root);
            var initial = store.Load() with { Language = from };
            store.Save(initial);

            using var settings = new SettingsForm(store, saved: null)
            {
                AskAboutLanguageRestart = named =>
                {
                    onAsk?.Invoke(named);
                    return answer;
                }
            };

            SelectLanguage(settings, language);
            saved = Invoke<bool>(settings, "TrySave");
            requested = settings.LanguageRestartRequested;
        });

        restartRequested = requested;
        return saved;
    }

    private static void SelectLanguage(SettingsForm settings, string language)
    {
        var field = typeof(SettingsForm).GetField("_language", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var combo = Assert.IsType<StorageHubChoiceField>(field.GetValue(settings));
        combo.SelectedItem = combo.Items
            .Cast<object>()
            .OfType<string>()
            .FirstOrDefault(item => string.Equals(item, language, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(combo.SelectedItem);
    }

    private static T Invoke<T>(SettingsForm settings, string name)
    {
        var method = typeof(SettingsForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<T>(method.Invoke(settings, null));
    }
}
