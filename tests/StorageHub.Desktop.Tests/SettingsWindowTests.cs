using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using StorageHub.Desktop.Localization;
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
        var model = (SettingsModel)window.DataContext!;

        Assert.Equal(model.Navigation.Count, list.ItemCount);
        Assert.Equal(SettingsPageCatalog.Pages.Count, model.Navigation.Count(entry => entry.Page is not null));
        Assert.Equal(SettingsPageCatalog.Pages[0].Title, ((SettingsNavigationEntry)list.Items[0]!).Text);
    }

    /// <summary>
    /// The window is 1.4's (ui-reference 02) in its pages, its grouping and the rules between rows.
    /// </summary>
    /// <remarks>
    /// One walk through the things 2.0 had lost: the rail's order with Storage and Clients under
    /// Connections &amp; trust; Transfers &amp; sync holding what had become Performance and
    /// Confirmations, under captions, with the unit in each field; "Start with" dimmed without
    /// adaptive concurrency and raising the maximums it may not exceed, which the settings file
    /// would otherwise throw back to its defaults, and a maximum's arrow stopping at it rather than
    /// showing a number that will not be saved; a refused path keeping its text while another row
    /// changes; a remote provider's
    /// operation timeout following its connection timeout; Left and Right folding the rail; a
    /// shortcut that is taken being refused rather than stolen; and a previewed scheme undone when
    /// the window is closed without OK.
    /// </remarks>
    [AvaloniaFact]
    public void ThePagesAndTheirRowsAreThoseOf14()
    {
        var previewed = new List<string?>();
        var model = Model(p => previewed.Add(p.ColorScheme));
        var window = new SettingsWindow { DataContext = model };
        window.Show();
        window.UpdateLayout();

        Assert.Equal(
            [
                Ui.Settings.CategoryTransfersAndSync, Ui.Settings.CategoryEditing, Ui.Settings.CategoryAppearance,
                Ui.Settings.CategoryWorkspace, Ui.Settings.CategoryShortcuts, Ui.Settings.CategoryConnectionsAndTrust,
                Ui.Settings.CategoryToolbar, Ui.Settings.CategoryAgent, Ui.Settings.CategoryUpdates
            ],
            model.Navigation.Where(entry => entry.IsTopLevel).Select(entry => entry.Text));
        var trust = model.Navigation.Select(entry => entry.Text)
            .SkipWhile(text => text != Ui.Settings.CategoryConnectionsAndTrust).ToList();
        Assert.Equal(Ui.Settings.CategoryStorage.ToUpper(CultureInfo.CurrentCulture), trust[1]);
        Assert.Equal(ConnectionProviderCatalog.Get(StorageProviderKind.Local).DisplayName, trust[2]);
        Assert.Contains(Ui.Settings.CategoryClients.ToUpper(CultureInfo.CurrentCulture), trust);

        var transfers = model.SelectedPageModel;
        Assert.Equal(
            [
                Ui.Settings.SectionConcurrency, Ui.Settings.SectionSpeedLimits, Ui.Settings.SectionSchedules,
                Ui.Settings.SectionConfirmations
            ],
            transfers.Groups.Select(group => group.Caption),
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(Ui.Settings.UnitJobs, Row(model, "maximum-transfers").Unit);

        // New schedules follow the system until a zone is chosen, and following again stores no zone.
        var zone = ScheduleTimeZones.All.First(static zone => zone.Id != TimeZoneInfo.Local.Id).Id;
        Assert.Equal(0, Row(model, "new-schedule-time-zone").SelectedChoice);
        Row(model, "new-schedule-time-zone").SelectedChoice = IndexOf(model, "new-schedule-time-zone", zone);
        Assert.Equal(zone, model.Working.NewScheduleTimeZone);
        Row(model, "new-schedule-time-zone").SelectedChoice = 0;
        Assert.Null(model.Working.NewScheduleTimeZone);

        var startWith = Row(model, "start-with");
        Row(model, "adaptive-concurrency").IsOn = false;
        Assert.False(startWith.IsEnabled);
        Row(model, "adaptive-concurrency").IsOn = true;
        startWith.NumberValue = 6;
        Assert.Equal(6, model.Working.MaximumSyncConcurrency);
        Assert.Equal("6", Row(model, "maximum-synchronizations").Value);
        var synchronizations = window.GetVisualDescendants().OfType<NumericUpDown>()
            .Single(field => ReferenceEquals(field.DataContext, Row(model, "maximum-synchronizations")));
        synchronizations.GetVisualDescendants().OfType<ButtonSpinner>().Single()
            .RaiseEvent(new SpinEventArgs(Spinner.SpinEvent, SpinDirection.Decrease));
        Assert.Equal(6, synchronizations.Value);
        Assert.Equal(6, model.Working.MaximumSyncConcurrency);

        var editor = Row(model, "external-editor");
        editor.Text = "code";
        Row(model, "warn-unsafe-edit").IsOn = false;
        Assert.Equal("code", editor.Text);
        Assert.True(editor.HasProblem);

        var connect = ConnectionDefaultSettings.Key(StorageProviderKind.Ftp, ConnectionDefaultSettings.ConnectTimeoutKey);
        var operation = ConnectionDefaultSettings.Key(StorageProviderKind.Ftp, ConnectionDefaultSettings.OperationTimeoutKey);
        Row(model, "default:" + connect).NumberValue = 90;
        Assert.False(Row(model, "default:" + operation).IsEnabled);
        Assert.Equal("90", Row(model, "default:" + operation).Value);
        Assert.Equal(90, ConnectionDefaultSettings.Get(StorageProviderKind.Ftp, model.Working.ConnectionDefaults).ConnectTimeoutSeconds);

        var ftp = ConnectionProviderCatalog.Get(StorageProviderKind.Ftp).DisplayName;
        model.SelectPage(SettingsPageCatalog.ProviderPageKey(StorageProviderKind.Ftp));
        Assert.True(model.Fold(open: false));
        Assert.Equal(SettingsPageCatalog.ConnectionsPageKey, SettingsPageCatalog.Pages[model.SelectedPage].Key);
        Assert.True(model.Fold(open: false));
        Assert.DoesNotContain(model.Navigation, entry => entry.Text == ftp);
        Assert.True(model.Fold(open: true));
        Assert.Contains(model.Navigation, entry => entry.Text == ftp);

        var shortcuts = model.Pages.OfType<ShortcutsPageModel>().Single();
        var bound = ShortcutSettings.Commands.First(command => command.Shortcut is not null);
        shortcuts.SelectedCommand = shortcuts.Commands.First(row => row.Id != bound.Id);
        shortcuts.Capture(bound.Shortcut!);
        shortcuts.AssignCommand.Execute(null);
        Assert.Null(model.Working.Shortcuts);

        var free = new KeyGesture(Key.F9, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift);
        shortcuts.Capture(free);
        shortcuts.AssignCommand.Execute(null);
        Assert.Equal(free, model.Working.Shortcuts![shortcuts.SelectedCommand!.Id]);

        Row(model, "color-scheme").SelectedChoice = IndexOf(model, "color-scheme", "dracula");
        window.Close();
        Assert.Equal(DesktopUpdatePreferences.Defaults.ColorScheme, previewed[^1]);
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

        // Move everything off its default, then save and reopen on the result. A dimmed row is
        // left alone: what it shows is decided by another row.
        foreach (var row in model.Pages.SelectMany(page => page.Rows).Where(row => row.IsEnabled))
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

    /// <summary>
    /// Photographs the pages that differ most, in both appearances, for a human to hold against
    /// ui-reference 02. Set STORAGEHUB_SHOT_DIR to keep the files.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheWindowCanBePhotographed(bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));
        var model = Model();
        var window = new SettingsWindow { DataContext = model };
        window.Show();

        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        foreach (var page in new[]
                 {
                     SettingsPageCatalog.PerformancePageKey,
                     "appearance",
                     "workspace",
                     SettingsPageCatalog.ConnectionsPageKey,
                     SettingsPageCatalog.ProviderPageKey(StorageProviderKind.Sftp),
                     SettingsPageCatalog.ProviderPageKey(StorageProviderKind.Ssh),
                     SettingsPageCatalog.ShortcutsPageKey,
                     SettingsPageCatalog.AgentPageKey
                 })
        {
            model.SelectPage(page);
            window.Measure(new Size(1160, 780));
            window.Arrange(new Rect(0, 0, 1160, 780));
            window.UpdateLayout();

            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            if (string.IsNullOrWhiteSpace(directory)) continue;

            Directory.CreateDirectory(directory);
            var name = page.Replace(':', '-');
            using var stream = File.Create(Path.Combine(directory, $"settings-{name}-{(dark ? "dark" : "light")}.png"));
            frame!.Save(stream, new PngBitmapEncoderOptions());
        }
    }

    /// <summary>
    /// The language is chosen on the Appearance page, and a restart is offered as 1.4 offered it.
    /// </summary>
    /// <remarks>
    /// The list names each language in itself, so somebody who has landed in one they cannot read
    /// still finds their own. A save asks only when the words on screen would change: declined,
    /// the choice is saved all the same and Apply leaves the window open; going back to the
    /// language already showing asks nothing; a save that fails says so, asks nothing and stays
    /// open, as 1.4's did; accepted, OK closes the window with the restart left for the shell.
    /// </remarks>
    [AvaloniaFact]
    public void ChoosingALanguageOffersARestartOnlyWhenTheWordsWouldChange()
    {
        Assert.SkipWhen(
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(DesktopCulture.LanguageEnvironmentVariable)),
            "STORAGEHUB_LANGUAGE pins the language, so no save changes it and nothing is offered.");
        var showing = Ui.Culture.Name;
        _stored = _stored with { Language = showing };
        var failing = false;
        var dialogs = new KeyStoreTests.RecordingDialogs();
        var model = new SettingsModel(
            () => _stored,
            p =>
            {
                if (failing) throw new IOException("The disk is full.");
                _stored = p;
                _saves++;
            },
            dialogs: dialogs);
        var closed = 0;
        model.Closed += (_, _) => closed++;
        var language = Row(model, "language");
        static string Prompt(string culture) =>
            Ui.Format(Ui.Dialogs.LanguageRestartPromptFormat, CultureInfo.GetCultureInfo(culture).NativeName);

        Assert.Contains(language, model.Pages.Single(page => page.Key == "appearance").Rows);
        Assert.Equal(
            [Ui.Settings.LanguageAutomatic, .. DesktopCulture.SupportedCultures.Select(culture => CultureInfo.GetCultureInfo(culture).NativeName)],
            language.Choices);
        var others = DesktopCulture.SupportedCultures.Where(culture => culture != showing).ToList();

        language.SelectedChoice = IndexOf(model, "language", others[0]);
        model.ApplyCommand.Execute(null);
        Assert.Equal(Prompt(others[0]), dialogs.LastRequest?.Message);
        Assert.Equal(others[0], _stored.Language);
        Assert.False(model.LanguageRestartRequested);
        Assert.Equal(0, closed);

        var asked = dialogs.LastRequest;
        language.SelectedChoice = IndexOf(model, "language", showing);
        model.ApplyCommand.Execute(null);
        Assert.Same(asked, dialogs.LastRequest);

        dialogs.Choice = Desktop.Shell.DialogChoice.Yes;
        language.SelectedChoice = IndexOf(model, "language", others[1]);
        failing = true;
        model.SaveCommand.Execute(null);
        Assert.Equal(Ui.Dialogs.SettingsSaveFailed, dialogs.LastRequest?.Message);
        Assert.Equal(showing, _stored.Language);
        Assert.Equal(0, closed);

        failing = false;
        model.SaveCommand.Execute(null);
        Assert.Equal(Prompt(others[1]), dialogs.LastRequest?.Message);
        Assert.True(model.LanguageRestartRequested);
        Assert.Equal(1, closed);
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
