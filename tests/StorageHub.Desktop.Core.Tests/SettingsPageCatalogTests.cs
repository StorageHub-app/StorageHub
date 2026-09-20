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
            Assert.NotEmpty(page.Rows);
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

    private static IReadOnlyList<string> ValuesFor(SettingsRowDefinition row) => row.Kind switch
    {
        SettingsControlKind.Toggle => ["true", "false"],
        SettingsControlKind.Choice => [.. row.Choices.Select(choice => choice.Value)],
        SettingsControlKind.Number =>
            [row.Minimum.ToString(System.Globalization.CultureInfo.InvariantCulture),
             row.Maximum.ToString(System.Globalization.CultureInfo.InvariantCulture)],
        _ => []
    };
}
