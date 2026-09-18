using StorageHub.Desktop.Configuration;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The rules a saved toolbar layout has to keep: a stale or hostile file still renders, every
/// command the menus offer can be put on it, and a layout survives a save and reload unchanged.
/// </summary>
public sealed class ToolbarLayoutTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"storagehub-toolbar-{Guid.NewGuid():N}");

    [Fact]
    public void NothingStoredResolvesToTheEssentialPreset()
    {
        Assert.Equal(ToolbarLayout.Preset(ToolbarPreset.Essential), ToolbarLayout.Resolve(null));
        Assert.Equal(ToolbarLayout.Preset(ToolbarPreset.Essential), ToolbarLayout.Resolve([]));
        Assert.True(ToolbarLayout.Matches(ToolbarLayout.Default, ToolbarPreset.Essential));
    }

    [Fact]
    public void EmptyingTheToolbarGivesTheDefaultBackRatherThanAnUnusableBar()
    {
        // Otherwise removing the last button would remove the only way to reach Settings and
        // put one back.
        Assert.Equal(ToolbarLayout.Default, ToolbarLayout.Resolve([ToolbarLayout.Separator, "  ", ""]));
    }

    [Fact]
    public void BothPresetsNameOnlyKnownCommandsAndNeverRepeatOne()
    {
        var known = UiCommandCatalog.Specs.Select(static spec => spec.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var preset in Enum.GetValues<ToolbarPreset>())
        {
            var items = ToolbarLayout.Preset(preset);
            var commands = items.Where(entry => entry != ToolbarLayout.Separator).ToArray();

            Assert.All(commands, command => Assert.Contains(command, known));
            Assert.Equal(commands.Length, commands.Distinct(StringComparer.Ordinal).Count());

            // A preset is what Reset writes, so it has to survive sanitising unchanged or the
            // toolbar would come back different from the one the button promised.
            Assert.Equal(items, ToolbarLayout.Sanitise(items));
            Assert.True(ToolbarLayout.Matches(items, preset));
        }

        Assert.NotEqual(
            ToolbarLayout.Preset(ToolbarPreset.Essential),
            ToolbarLayout.Preset(ToolbarPreset.Expanded));
    }

    [Fact]
    public void EveryCommandInTheCatalogCanBePutOnTheToolbar()
    {
        var all = UiCommandCatalog.Specs.Select(static spec => spec.Id).ToArray();

        Assert.Equal(all, ToolbarLayout.Sanitise(all));
    }

    [Fact]
    public void UnknownAndDuplicateEntriesAreDroppedAndTheRestSurvives()
    {
        var stored = new[]
        {
            "tools.that.was.removed.in.an.older.release",
            UiCommandIds.ViewRefresh,
            UiCommandIds.ViewRefresh,
            UiCommandIds.ToolsSettings
        };

        Assert.Equal([UiCommandIds.ViewRefresh, UiCommandIds.ToolsSettings], ToolbarLayout.Sanitise(stored));
    }

    [Fact]
    public void SeparatorsNeverRenderAsLeadingTrailingOrDoubledDividers()
    {
        var stored = new[]
        {
            ToolbarLayout.Separator,
            ToolbarLayout.Separator,
            UiCommandIds.GoBack,
            ToolbarLayout.Separator,
            ToolbarLayout.Separator,
            UiCommandIds.GoForward,
            ToolbarLayout.Separator
        };

        Assert.Equal(
            [UiCommandIds.GoBack, ToolbarLayout.Separator, UiCommandIds.GoForward],
            ToolbarLayout.Sanitise(stored));
    }

    [Fact]
    public void AHostileFileCannotGrowTheToolbarWithoutBound()
    {
        var stored = Enumerable.Repeat(ToolbarLayout.Separator, ToolbarLayout.MaximumItems * 4)
            .Append(UiCommandIds.ToolsSettings)
            .ToArray();

        // Everything past the cap is never looked at, so the one real command beyond it is gone
        // and the separators collapse to nothing.
        Assert.Empty(ToolbarLayout.Sanitise(stored));
    }

    [Fact]
    public void ACustomLayoutAndLabelStyleRoundTripThroughTheSettingsFiles()
    {
        var layout = new[]
        {
            UiCommandIds.ToolsSettings,
            ToolbarLayout.Separator,
            UiCommandIds.GoBack,
            UiCommandIds.GoForward
        };
        var saved = DesktopUpdatePreferences.Defaults with
        {
            ToolbarItems = layout,
            ToolbarLabels = ToolbarLabelStyle.TextUnderIcon
        };

        new DesktopConfigStore(_directory).Save(saved);
        var loaded = new DesktopConfigStore(_directory).Load();

        Assert.Equal(layout, loaded.ToolbarItems);
        Assert.Equal(ToolbarLabelStyle.TextUnderIcon, loaded.ToolbarLabels);
    }

    [Fact]
    public void AStoredLayoutIsSanitisedOnItsWayIntoTheFile()
    {
        var saved = DesktopUpdatePreferences.Defaults with
        {
            ToolbarItems = [ToolbarLayout.Separator, "not.a.command", UiCommandIds.ViewRefresh]
        };

        new DesktopConfigStore(_directory).Save(saved);

        Assert.Equal([UiCommandIds.ViewRefresh], new DesktopConfigStore(_directory).Load().ToolbarItems);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
