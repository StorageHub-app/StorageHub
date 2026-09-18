namespace StorageHub.Desktop.Tests;

/// <summary>
/// The combo boxes that list enum values show words, not C# identifiers.
/// </summary>
/// <remarks>
/// <see cref="UiEnumNameTests"/> proves the words exist. This proves they reach the control, which
/// is a separate question and the one that actually went wrong: <c>ListControl</c> raises its
/// <c>Format</c> event only when <c>FormattingEnabled</c> is set, so a handler written to name an
/// item silently does nothing without it and the list falls back to <c>ToString()</c>. Two of these
/// dialogs shipped with exactly that — a correct handler that had never once run.
///
/// <c>GetItemText</c> is what the control itself calls to paint a row, so asking it is asking what
/// the user sees rather than what the code intended.
/// </remarks>
public sealed class ComboDisplayTextTests
{
    [Theory]
    [MemberData(nameof(ShippedTranslationProvider.Cultures), MemberType = typeof(ShippedTranslationProvider))]
    public void EnumCombosShowWordsRatherThanIdentifiers(string culture)
    {
        ShippedTranslationProvider.InCulture(culture, () =>
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var schedules = new ScheduleManagerForm();
            using var sync = new SyncProfileEditorForm();

            foreach (var form in new Form[] { schedules, sync })
            {
                foreach (var combo in Combos(form))
                {
                    AssertShowsWords(culture, combo);
                }
            }
        }));
    }

    private static void AssertShowsWords(string culture, ComboBox combo)
    {
        foreach (var item in combo.Items)
        {
            if (item is not Enum value)
            {
                continue;
            }

            // Weekdays are the one enum whose English words are its identifiers -- "Sunday" is
            // both a correct day name and what a dead handler would leave behind, so this check
            // cannot tell them apart. UiEnumNameTests pins the day per culture instead.
            if (value is DayOfWeek)
            {
                continue;
            }

            var shown = combo.GetItemText(item);
            Assert.False(
                string.Equals(shown, value.ToString(), StringComparison.Ordinal),
                $"{culture}: '{combo.AccessibleName ?? combo.Name}' shows {value.GetType().Name}." +
                $"{value} as its identifier. Either it has no Format handler, or the control is " +
                "missing FormattingEnabled and the handler never runs.");
        }
    }

    private static IEnumerable<ComboBox> Combos(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is ComboBox combo)
            {
                yield return combo;
            }

            foreach (var nested in Combos(child))
            {
                yield return nested;
            }
        }
    }
}
