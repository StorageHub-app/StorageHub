using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using StorageHub.Desktop.Settings;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The Settings window, driven the way a person drives it.
/// </summary>
/// <remarks>
/// The model takes its load and save as delegates, so the whole window runs against a dictionary
/// in memory rather than the real settings file. That is the difference between testing this and
/// testing DesktopConfigStore, which has its own suite.
/// </remarks>
public sealed class SettingsWindowTests : IDisposable
{
    private DesktopUpdatePreferences _stored = DesktopUpdatePreferences.Defaults;
    private int _saves;

    public void Dispose() => ColorSchemeApplier.Reset(global::Avalonia.Application.Current!);

    private SettingsModel Model(Action<DesktopUpdatePreferences>? preview = null) =>
        new(() => _stored, p => { _stored = p; _saves++; }, preview);

    [AvaloniaFact]
    public void TheWindowShowsEveryPageAndTheirRows()
    {
        var window = new SettingsWindow { DataContext = Model() };
        window.Show();
        window.UpdateLayout();

        var list = window.GetVisualDescendants().OfType<ListBox>().First();

        Assert.Equal(SettingsPageCatalog.Pages.Count, list.ItemCount);
        Assert.Equal(SettingsPageCatalog.Pages[0].Title, ((SettingsPageModel)list.Items[0]!).Title);
    }

    [AvaloniaFact]
    public void AnEditIsNotSavedUntilApply()
    {
        var model = Model();
        var row = Row(model, "check-automatically");

        row.IsOn = false;

        Assert.True(model.IsDirty);
        Assert.True(_stored.CheckAutomatically);
        Assert.Equal(0, _saves);

        model.Apply();

        Assert.False(model.IsDirty);
        Assert.False(_stored.CheckAutomatically);
        Assert.Equal(1, _saves);
    }

    [AvaloniaFact]
    public void ApplyingTwiceWithNothingChangedWritesOnce()
    {
        var model = Model();
        Row(model, "include-prereleases").IsOn = false;

        model.Apply();
        model.Apply();

        Assert.Equal(1, _saves);
    }

    /// <summary>Cancelling puts back what the live preview changed.</summary>
    /// <remarks>
    /// The colour scheme is the one setting that takes effect before Apply, because twenty-two of
    /// them cannot be told apart by name. That makes Cancel responsible for undoing it -- and this
    /// is the case that would otherwise leave somebody in a scheme they rejected.
    /// </remarks>
    [AvaloniaFact]
    public void CancellingUndoesALivePreview()
    {
        var previewed = new List<string?>();
        var model = Model(p => previewed.Add(p.ColorScheme));

        Row(model, "color-scheme").SelectedChoice = IndexOf(model, "color-scheme", "dracula");
        Assert.Equal("dracula", previewed[^1]);

        model.CancelCommand.Execute(null);

        Assert.Equal(DesktopUpdatePreferences.Defaults.ColorScheme, previewed[^1]);
        Assert.Equal(0, _saves);
    }

    [AvaloniaFact]
    public void ChoosingASchemePreviewsItButOnlySavingKeepsIt()
    {
        var model = Model(SettingsWindow.ApplyScheme);

        Row(model, "color-scheme").SelectedChoice = IndexOf(model, "color-scheme", "nord");

        Assert.Equal("nord", ColorSchemeApplier.Current?.Id);
        Assert.Null(_stored.ColorScheme);

        model.Apply();

        Assert.Equal("nord", _stored.ColorScheme);
    }

    /// <summary>
    /// "Follow the system" picks the other half of a pair rather than abandoning the scheme.
    /// </summary>
    [AvaloniaFact]
    public void TheAppearanceChoosesWhichHalfOfAPairIsShown()
    {
        var model = Model(SettingsWindow.ApplyScheme);
        Row(model, "color-scheme").SelectedChoice = IndexOf(model, "color-scheme", "solarized-dark");
        var appearance = Row(model, "appearance");

        appearance.SelectedChoice = IndexOf(model, "appearance", nameof(DesktopAppearance.Light));
        Assert.Equal("solarized-light", ColorSchemeApplier.Current?.Id);

        appearance.SelectedChoice = IndexOf(model, "appearance", nameof(DesktopAppearance.Dark));
        Assert.Equal("solarized-dark", ColorSchemeApplier.Current?.Id);
    }

    [AvaloniaFact]
    public void EverySettingSurvivesTheWholeTripThroughTheWindow()
    {
        var model = Model();

        // Move everything off its default, then save and reopen on the result.
        foreach (var row in model.Pages.SelectMany(page => page.Rows))
        {
            switch (row.Definition.Kind)
            {
                case SettingsControlKind.Toggle:
                    row.IsOn = !row.IsOn;
                    break;
                case SettingsControlKind.Choice:
                    row.SelectedChoice = row.Choices.Count - 1;
                    break;
                case SettingsControlKind.Number:
                    row.NumberValue = row.Maximum;
                    break;
            }
        }

        model.Apply();
        var reopened = Model();

        foreach (var row in reopened.Pages.SelectMany(page => page.Rows))
        {
            var original = model.Pages.SelectMany(page => page.Rows).Single(r => r.Key == row.Key);
            Assert.Equal(original.Value, row.Value);
        }
    }

    [AvaloniaFact]
    public void TheWindowCanBePhotographed()
    {
        var window = new SettingsWindow { DataContext = Model() };
        window.Show();
        window.Measure(new Size(880, 620));
        window.Arrange(new Rect(0, 0, 880, 620));

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        using (var stream = File.Create(Path.Combine(directory, "settings.png")))
        {
            frame!.Save(stream, new PngBitmapEncoderOptions());
        }

        // The Performance page too, where the total speed limits sit under the concurrency rows.
        ((SettingsModel)window.DataContext!).SelectPage(SettingsPageCatalog.PerformancePageKey);
        window.UpdateLayout();
        using var performance = File.Create(Path.Combine(directory, "settings-performance.png"));
        window.CaptureRenderedFrame()!.Save(performance, new PngBitmapEncoderOptions());
    }

    /// <summary>Speed Limits opens Settings on the page that holds them.</summary>
    [AvaloniaFact]
    public void TheWindowCanOpenOnAChosenPage()
    {
        var model = Model();

        model.SelectPage(SettingsPageCatalog.PerformancePageKey);
        Assert.Equal(SettingsPageCatalog.PerformancePageKey, SettingsPageCatalog.Pages[model.SelectedPage].Key);
        Assert.Contains(model.SelectedPageModel.Rows, row => row.Key == "total-upload-limit");

        model.SelectPage("no-such-page");
        Assert.Equal(SettingsPageCatalog.PerformancePageKey, SettingsPageCatalog.Pages[model.SelectedPage].Key);
    }

    private static SettingsRowModel Row(SettingsModel model, string key) =>
        model.Pages.SelectMany(page => page.Rows).Single(row => row.Key == key);

    private static int IndexOf(SettingsModel model, string key, string value)
    {
        var definition = Row(model, key).Definition;
        for (var index = 0; index < definition.Choices.Count; index++)
        {
            if (string.Equals(definition.Choices[index].Value, value, StringComparison.Ordinal))
            {
                return index;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(value), value, $"{key} does not offer it.");
    }
}
