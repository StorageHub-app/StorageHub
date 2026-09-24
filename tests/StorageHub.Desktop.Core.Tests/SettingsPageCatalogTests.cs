using StorageHub.Desktop.Settings;
using StorageHub.Desktop.Themes;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// That every settings row reads and writes the preference it claims to.
/// </summary>
/// <remarks>
/// The rows carry their value as text so one view template can draw all of them, which is what
/// keeps this page from being SettingsForm's 1,827 lines again. The cost is that a typo in a Read
/// or a Write is not a compile error, so it is a test instead -- and one that covers every row
/// rather than the ones somebody remembered.
/// </remarks>
public class SettingsPageCatalogTests
{
    [Fact]
    public void EveryRowIsUniqueAndLabelled()
    {
        var rows = SettingsPageCatalog.AllRows.ToList();

        Assert.Equal(rows.Count, rows.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.All(rows, row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Key));
            Assert.False(string.IsNullOrWhiteSpace(row.Label));
        });

        Assert.All(SettingsPageCatalog.Pages, page =>
        {
            Assert.False(string.IsNullOrWhiteSpace(page.Title));
            Assert.False(string.IsNullOrWhiteSpace(page.Description));

            // Every page is a list of rows except the ones whose editor is a screen of its own.
            // Those still appear here, so the navigation list stays one list, but they carry no
            // rows and are drawn by their own template.
            if (!string.Equals(page.Key, SettingsPageCatalog.ToolbarPageKey, StringComparison.Ordinal))
            {
                Assert.NotEmpty(page.Rows);
            }
        });
    }

    /// <summary>
    /// Writing a value and reading it back gives the same value, for every row and every value it
    /// can hold.
    /// </summary>
    /// <remarks>
    /// The one property that catches a Read and a Write pointing at different preferences, which is
    /// the mistake this shape invites and the one that produces a setting that looks saved and is
    /// not.
    /// </remarks>
    [Fact]
    public void EveryRowRoundTripsEveryValueItCanHold()
    {
        var broken = new List<string>();
        foreach (var row in SettingsPageCatalog.AllRows)
        {
            foreach (var value in ValuesFor(row))
            {
                var written = row.Write(DesktopUpdatePreferences.Defaults, value);
                var read = row.Read(written);
                if (!string.Equals(read, value, StringComparison.Ordinal))
                {
                    broken.Add($"{row.Key}: wrote '{value}', read back '{read}'");
                }
            }
        }

        Assert.Empty(broken);
    }

    /// <summary>A row must change exactly the preference it owns, and no other.</summary>
    [Fact]
    public void WritingOneRowLeavesEveryOtherRowAlone()
    {
        var rows = SettingsPageCatalog.AllRows.ToList();
        var interference = new List<string>();

        foreach (var row in rows)
        {
            var before = DesktopUpdatePreferences.Defaults;
            var values = ValuesFor(row);
            var after = row.Write(before, values[^1]);

            foreach (var other in rows.Where(candidate => candidate.Key != row.Key))
            {
                if (!string.Equals(other.Read(before), other.Read(after), StringComparison.Ordinal))
                {
                    interference.Add($"{row.Key} moved {other.Key}");
                }
            }
        }

        Assert.Empty(interference);
    }

    /// <summary>
    /// A choice row only ever offers values it can store.
    /// </summary>
    /// <remarks>
    /// The scheme row's options come from the catalog, so this is also what says the picker cannot
    /// offer a scheme that does not exist.
    /// </remarks>
    [Fact]
    public void EveryChoiceOffersOnlyValuesItAccepts()
    {
        var rejected = new List<string>();
        foreach (var row in SettingsPageCatalog.AllRows.Where(r => r.Kind == SettingsControlKind.Choice))
        {
            Assert.NotEmpty(row.Choices);
            foreach (var choice in row.Choices)
            {
                Assert.False(string.IsNullOrWhiteSpace(choice.Label), $"{row.Key} has an unlabelled choice");
                var read = row.Read(row.Write(DesktopUpdatePreferences.Defaults, choice.Value));
                if (!string.Equals(read, choice.Value, StringComparison.Ordinal))
                {
                    rejected.Add($"{row.Key} offers '{choice.Value}' but stores '{read}'");
                }
            }
        }

        Assert.Empty(rejected);
    }

    [Fact]
    public void TheSchemePickerOffersEveryShippedScheme()
    {
        var row = SettingsPageCatalog.AllRows.Single(r => r.Key == "color-scheme");

        Assert.Equal(
            ColorSchemeCatalog.All.Select(scheme => scheme.Id).Order(StringComparer.Ordinal),
            row.Choices.Select(choice => choice.Value).Order(StringComparer.Ordinal));
    }

    /// <summary>A number out of range is clamped, not stored, and not thrown over.</summary>
    [Fact]
    public void ANumberOutsideItsRangeIsClamped()
    {
        var transfers = SettingsPageCatalog.AllRows.Single(r => r.Key == "maximum-transfers");

        Assert.Equal("16", transfers.Read(transfers.Write(DesktopUpdatePreferences.Defaults, "9999")));
        Assert.Equal("1", transfers.Read(transfers.Write(DesktopUpdatePreferences.Defaults, "-4")));
    }

    /// <summary>
    /// Nonsense leaves the setting where it was.
    /// </summary>
    /// <remarks>
    /// These values do not come only from controls. Settings import reads them from a file somebody
    /// else's build wrote, and a row that throws on a value it does not recognise would fail the
    /// whole import over one setting.
    /// </remarks>
    [Fact]
    public void AnUnparseableValueLeavesThePreferenceUnchanged()
    {
        var defaults = DesktopUpdatePreferences.Defaults;

        foreach (var row in SettingsPageCatalog.AllRows.Where(r => r.Kind != SettingsControlKind.Toggle))
        {
            var after = row.Write(defaults, "not-a-value");
            Assert.Equal(row.Read(defaults), row.Read(after));
        }
    }

    /// <summary>
    /// A path that is not a full one is refused with a sentence, and not written.
    /// </summary>
    /// <remarks>
    /// The settings file drops an editor path that is not fully qualified when it is loaded, so
    /// saving one would look like it had worked and then quietly revert.
    /// </remarks>
    [Fact]
    public void AnEditorPathMustBeAFullOne()
    {
        var editor = SettingsPageCatalog.AllRows.Single(r => r.Key == "external-editor");
        var before = DesktopUpdatePreferences.Defaults with { ExternalEditorPath = TestPaths.Rooted("tools/old") };

        Assert.Equal(Localization.Ui.Settings.EditorPathMustBeFull, editor.Validate!("code"));
        Assert.Equal(before, editor.Write(before, "code"));
        Assert.Null(editor.Validate!(""));
        Assert.Null(editor.Write(before, "  ").ExternalEditorPath);
    }

    /// <summary>The size is shown in KiB and stored in bytes, as 1.x did.</summary>
    [Fact]
    public void TheEditingLimitIsKilobytesOnScreen()
    {
        var limit = SettingsPageCatalog.AllRows.Single(r => r.Key == "maximum-editable-kib");

        Assert.Equal(512 * 1024, limit.Write(DesktopUpdatePreferences.Defaults, "512").MaximumEditableFileBytes);
        Assert.Equal("1024", limit.Read(DesktopUpdatePreferences.Defaults));
    }

    /// <summary>Total speed limits are KiB/s on screen and bytes per second stored; 0 is no limit.</summary>
    [Fact]
    public void TotalSpeedLimitsAreKibibytesPerSecondOnScreenAndZeroIsNone()
    {
        var upload = SettingsPageCatalog.AllRows.Single(r => r.Key == "total-upload-limit");
        var download = SettingsPageCatalog.AllRows.Single(r => r.Key == "total-download-limit");

        var limited = upload.Write(DesktopUpdatePreferences.Defaults, "300");
        var cleared = upload.Write(limited, "0");

        Assert.Equal(300 * 1024, limited.TotalUploadBytesPerSecond);
        Assert.Null(limited.TotalDownloadBytesPerSecond);
        Assert.Equal("300", upload.Read(limited));
        Assert.Null(cleared.TotalUploadBytesPerSecond);
        Assert.Equal("0", download.Read(DesktopUpdatePreferences.Defaults));
        Assert.Contains(
            SettingsPageCatalog.Pages.Single(p => p.Key == SettingsPageCatalog.PerformancePageKey).Rows,
            row => row.Key == "total-download-limit");
    }

    /// <summary>
    /// The agent reads concurrency and the total speed limits when it starts, so changing one of
    /// them is what calls for a restart, and changing anything else does not.
    /// </summary>
    [Fact]
    public void OnlyWhatTheAgentReadsCallsForARestart()
    {
        var current = DesktopUpdatePreferences.Defaults;

        Assert.True(current.ChangesWhatTheAgentReads(current with { TotalDownloadBytesPerSecond = 1024 }));
        Assert.True(current.ChangesWhatTheAgentReads(current with { TotalUploadBytesPerSecond = 1024 }));
        Assert.True(current.ChangesWhatTheAgentReads(current with { PerConnectionConcurrency = 5 }));
        Assert.False(current.ChangesWhatTheAgentReads(current with { Appearance = DesktopAppearance.Dark }));
        Assert.False(current.ChangesWhatTheAgentReads(current));
    }

    /// <summary>
    /// The unsafe-edit warning is restored where it says it is.
    /// </summary>
    /// <remarks>
    /// Its checkbox hint reads "restore this warning later in Settings under Editing". The toggle
    /// sat under Confirmations after the port, with no Editing page for the hint to point at.
    /// </remarks>
    [Fact]
    public void TheUnsafeEditWarningIsUnderEditing()
    {
        var editing = SettingsPageCatalog.Pages.Single(page => page.Key == "editing");

        Assert.Equal(Localization.Ui.Settings.CategoryEditing, editing.Title);
        Assert.Contains(editing.Rows, row => row.Key == "warn-unsafe-edit");
        Assert.Single(SettingsPageCatalog.AllRows, row => row.Key == "warn-unsafe-edit");
    }

    private static IReadOnlyList<string> ValuesFor(SettingsRowDefinition row) => row.Kind switch
    {
        SettingsControlKind.Toggle => ["true", "false"],
        SettingsControlKind.Choice => [.. row.Choices.Select(choice => choice.Value)],
        SettingsControlKind.Number =>
            [row.Minimum.ToString(System.Globalization.CultureInfo.InvariantCulture),
             row.Maximum.ToString(System.Globalization.CultureInfo.InvariantCulture)],
        // Blank is a value too: it is what hands the file to the system's own app.
        SettingsControlKind.Path => ["", TestPaths.Rooted("tools/editor")],
        _ => []
    };
}
