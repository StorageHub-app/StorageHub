using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The toolbar page in Settings.
/// </summary>
/// <remarks>
/// The rules are pinned in ToolbarEditorTests. What is checked here is the seam between the page
/// and them: that the two lists say what the editor holds, that the selection follows an edit
/// rather than being dropped, that the buttons dim when they would do nothing, and that what was
/// arranged reaches the preferences the window writes.
/// </remarks>
public sealed class ToolbarSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"storagehub-toolbar-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    /// <summary>The toolbar is one of the pages, and it is the one with no rows.</summary>
    [AvaloniaFact]
    public void TheToolbarIsAPageInTheNavigation()
    {
        var page = Toolbar(Settings());

        Assert.Equal(Ui.Settings.CategoryToolbar, page.Title);
        Assert.Empty(page.Rows);
    }

    [AvaloniaFact]
    public void BothListsSayWhatTheToolbarHolds()
    {
        var page = Toolbar(Settings());

        Assert.Equal(ToolbarLayout.Default.Count, page.Current.Count);
        Assert.NotEmpty(page.Available);

        // Nothing already on the bar is offered again.
        var onBar = ToolbarLayout.Default.ToHashSet(StringComparer.Ordinal);
        Assert.All(page.Available, choice => Assert.DoesNotContain(choice.Id, onBar));
    }

    /// <summary>Adding leaves the new entry selected, so it can be moved straight away.</summary>
    [AvaloniaFact]
    public void AddingSelectsWhatWasJustAdded()
    {
        var page = Toolbar(Settings());
        var added = page.Available[0];
        page.SelectedAvailable = 0;
        page.SelectedCurrent = 0;

        page.AddSelected();

        Assert.Equal(1, page.SelectedCurrent);
        Assert.Equal(ToolbarEditor.Describe(added.Id), page.Current[1]);
        Assert.DoesNotContain(added, page.Available);
    }

    /// <summary>With nothing selected it goes to the end, and the selection follows it there.</summary>
    [AvaloniaFact]
    public void AddingWithNothingSelectedAppends()
    {
        var page = Toolbar(Settings());
        page.SelectedAvailable = 0;
        page.SelectedCurrent = -1;
        var before = page.Current.Count;

        page.AddSelected();

        Assert.Equal(before + 1, page.Current.Count);
        Assert.Equal(before, page.SelectedCurrent);
    }

    /// <summary>
    /// Removing leaves the row that took its place selected, so removing several in a row does
    /// not mean re-selecting between each.
    /// </summary>
    [AvaloniaFact]
    public void RemovingKeepsTheSelectionWhereItWas()
    {
        var page = Toolbar(Settings());
        page.SelectedCurrent = 1;
        var following = page.Current[2];

        page.RemoveSelected();

        Assert.Equal(1, page.SelectedCurrent);
        Assert.Equal(following, page.Current[1]);
    }

    /// <summary>Removing the last row selects the new last one rather than nothing.</summary>
    [AvaloniaFact]
    public void RemovingTheLastRowSelectsTheNewLastRow()
    {
        var page = Toolbar(Settings());
        page.SelectedCurrent = page.Current.Count - 1;

        page.RemoveSelected();

        Assert.Equal(page.Current.Count - 1, page.SelectedCurrent);
    }

    /// <summary>A moved entry keeps the selection, which is what lets it be moved again.</summary>
    [AvaloniaFact]
    public void MovingCarriesTheSelectionWithTheEntry()
    {
        var page = Toolbar(Settings());
        page.SelectedCurrent = 0;
        var moved = page.Current[0];

        page.MoveSelected(1);

        Assert.Equal(1, page.SelectedCurrent);
        Assert.Equal(moved, page.Current[1]);
    }

    /// <summary>A button that would do nothing is dim, rather than doing nothing when pressed.</summary>
    [AvaloniaFact]
    public void TheButtonsDimWhenTheyWouldDoNothing()
    {
        var page = Toolbar(Settings());

        // Nothing is selected when the page opens, so Add has nothing to add.
        Assert.False(page.AddCommand.CanExecute(null));

        page.SelectedCurrent = -1;
        Assert.False(page.RemoveCommand.CanExecute(null));
        Assert.False(page.MoveUpCommand.CanExecute(null));
        Assert.False(page.MoveDownCommand.CanExecute(null));

        page.SelectedCurrent = 0;
        Assert.True(page.RemoveCommand.CanExecute(null));
        // Already at the top, so there is nowhere up to go.
        Assert.False(page.MoveUpCommand.CanExecute(null));
        Assert.True(page.MoveDownCommand.CanExecute(null));

        page.SelectedCurrent = page.Current.Count - 1;
        Assert.True(page.MoveUpCommand.CanExecute(null));
        Assert.False(page.MoveDownCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void ResettingReplacesTheListWithThePreset()
    {
        var page = Toolbar(Settings());

        page.Reset(ToolbarPreset.Expanded);

        Assert.Equal(ToolbarLayout.Preset(ToolbarPreset.Expanded), page.Items);
        Assert.Equal(ToolbarLayout.Preset(ToolbarPreset.Expanded).Count, page.Current.Count);
    }

    /// <summary>Arranging the toolbar marks the window unsaved, as every other setting does.</summary>
    [AvaloniaFact]
    public void ArrangingTheToolbarMarksTheWindowUnsaved()
    {
        var settings = Settings();
        var page = Toolbar(settings);
        Assert.False(settings.IsDirty);

        page.Reset(ToolbarPreset.Expanded);

        Assert.True(settings.IsDirty);
        Assert.Equal(ToolbarLayout.Preset(ToolbarPreset.Expanded), settings.Working.ToolbarItems);
    }

    /// <summary>The label style is a setting like any other, and reaches the same working copy.</summary>
    [AvaloniaFact]
    public void TheLabelStyleReachesThePreferences()
    {
        var settings = Settings();
        var page = Toolbar(settings);

        page.SelectedLabelStyle = (int)ToolbarLabelStyle.TextUnderIcon;

        Assert.True(settings.IsDirty);
        Assert.Equal(ToolbarLabelStyle.TextUnderIcon, settings.Working.ToolbarLabels);
    }

    /// <summary>
    /// What is stored is what the toolbar will render.
    /// </summary>
    /// <remarks>
    /// Sanitised on the way in, so a layout left with a trailing divider does not come back
    /// changed on the next load and look like the edit had not been saved.
    /// </remarks>
    [AvaloniaFact]
    public void WhatIsStoredIsWhatTheToolbarWillRender()
    {
        var settings = Settings();
        var page = Toolbar(settings);

        page.SelectedCurrent = page.Current.Count - 1;
        page.AddSeparator();
        settings.Apply();

        var stored = settings.Working.ToolbarItems!;
        Assert.Equal(stored, ToolbarLayout.Sanitise(stored));
        Assert.NotEqual(ToolbarLayout.Separator, stored[^1]);
    }

    /// <summary>What was applied is what a window opened afterwards starts from.</summary>
    [AvaloniaFact]
    public void AnAppliedLayoutComesBackOnTheNextOpen()
    {
        var settings = Settings();
        Toolbar(settings).Reset(ToolbarPreset.Expanded);
        settings.Apply();

        var reopened = Toolbar(Settings());

        Assert.Equal(ToolbarLayout.Preset(ToolbarPreset.Expanded), reopened.Items);
    }

    /// <summary>
    /// Choosing a category puts that page on screen.
    /// </summary>
    /// <remarks>
    /// It did not, until the toolbar page needed reaching: the window wired the list to the page
    /// from DataContextChanged, which runs before the visual tree exists, so the list it went
    /// looking for was never found and every category showed the first page. Nothing caught it
    /// because no test selected a second category and no screenshot was taken of one.
    /// </remarks>
    [AvaloniaFact]
    public void ChoosingACategoryShowsThatPage()
    {
        var settings = Settings();
        var window = new SettingsWindow { DataContext = settings };
        window.Show();
        window.UpdateLayout();

        var toolbar = Toolbar(settings);
        settings.SelectedPage = settings.Pages.IndexOf(toolbar);
        window.UpdateLayout();

        Assert.Same(toolbar, settings.SelectedPageModel);
        Assert.Same(toolbar, window.GetControl<ContentControl>("PART_Page").Content);
    }

    /// <summary>
    /// Photographs the toolbar page in both appearances, for a human to look at.
    /// </summary>
    /// <remarks>
    /// Two lists with a column of buttons between them, and a footer that has to stay on two rows
    /// in every language, is exactly the sort of screen that lays out wrong without failing
    /// anything. Set STORAGEHUB_SHOT_DIR to keep the files.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheToolbarPageCanBePhotographed(bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));

        var settings = Settings();
        var window = new SettingsWindow { DataContext = settings };
        window.Show();

        // The page the window shows follows the navigation list, so selecting it is what puts the
        // toolbar editor on screen rather than the first page.
        settings.SelectedPage = settings.Pages.IndexOf(Toolbar(settings));

        window.Measure(new Size(880, 620));
        window.Arrange(new Rect(0, 0, 880, 620));
        window.UpdateLayout();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;

        Directory.CreateDirectory(directory);
        using var stream = File.Create(
            Path.Combine(directory, $"settings-toolbar-{(dark ? "dark" : "light")}.png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }

    private static ToolbarPageModel Toolbar(SettingsModel settings) =>
        settings.Pages.OfType<ToolbarPageModel>().Single();

    /// <summary>A Settings window over a temporary settings file rather than the real one.</summary>
    private SettingsModel Settings()
    {
        Directory.CreateDirectory(_directory);
        var store = new Configuration.DesktopConfigStore(_directory);
        return new SettingsModel(store.Load, store.Save);
    }
}
