using StorageHub.Desktop.Configuration;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// That a keyboard shortcut survives being written to a file and read back.
/// </summary>
/// <remarks>
/// Chords are stored as text rather than as the <see cref="Keys"/> enum, because CodeLogic's
/// camel-case string enum converter would render a flags combination as something like
/// <c>"control, shiftKey, s"</c>. That makes the codec here the thing standing between a user's
/// rebindings and silent loss, so it is tested on its own rather than only through the store.
/// </remarks>
public sealed class ShortcutChordTests
{
    public static TheoryData<Keys, string> Chords => new()
    {
        { Keys.None, "Unassigned" },
        { Keys.Control | Keys.S, "Ctrl+S" },
        { Keys.Control | Keys.Shift | Keys.S, "Ctrl+Shift+S" },
        { Keys.Control | Keys.Alt | Keys.Shift | Keys.Delete, "Ctrl+Alt+Shift+Delete" },
        { Keys.Alt | Keys.Left, "Alt+Left" },
        { Keys.F5, "F5" },
        { Keys.Delete, "Delete" },
        { Keys.Control | Keys.D1, "Ctrl+D1" },
        { Keys.Control | Keys.Oemplus, "Ctrl+Oemplus" }
    };

    [Theory]
    [MemberData(nameof(Chords))]
    public void AChordFormatsToItsStoredText(Keys keys, string expected) =>
        Assert.Equal(expected, ShortcutChord.Format(keys));

    [Theory]
    [MemberData(nameof(Chords))]
    public void AStoredChordParsesBackToTheSameKeys(Keys keys, string text) =>
        Assert.Equal(keys, ShortcutChord.TryParse(text));

    /// <summary>
    /// The modifier order is fixed so that the same binding always produces the same bytes, and a
    /// diff of the file shows only what the user actually changed.
    /// </summary>
    [Fact]
    public void ModifiersAreWrittenInAFixedOrder() => Assert.Equal(
        "Ctrl+Alt+Shift+F1",
        ShortcutChord.Format(Keys.Shift | Keys.Alt | Keys.Control | Keys.F1));

    [Fact]
    public void EveryDefaultBindingRoundTrips()
    {
        var defaults = ShortcutSettings.Resolve(null);

        var restored = ShortcutChord.Parse(ShortcutChord.Format(defaults));

        Assert.Equal(defaults.OrderBy(pair => pair.Key, StringComparer.Ordinal), restored.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+")]
    [InlineData("Ctrl+")]
    [InlineData("Hyper+Q")]
    [InlineData("Ctrl+Ctrl+S")]
    [InlineData("Ctrl+NotAKey")]
    [InlineData("Ctrl+Alt+Shift+Meta+S")]
    [InlineData("control, shiftKey, s")]
    public void TextThatWasNotWrittenByFormatIsRefused(string? text) =>
        Assert.Null(ShortcutChord.TryParse(text));

    /// <summary>
    /// A long value costs a lookup rather than any real work, so a hand-edited file cannot turn
    /// reading settings into a slow operation.
    /// </summary>
    [Fact]
    public void AnAbsurdlyLongChordIsRefusedWithoutBeingParsed() =>
        Assert.Null(ShortcutChord.TryParse(new string('A', 4096)));

    /// <summary>
    /// The modifier alone is not a binding, and accepting it would let a file describe a shortcut
    /// that can never fire.
    /// </summary>
    [Fact]
    public void ABareModifierIsNotAChord() => Assert.Null(ShortcutChord.TryParse("Ctrl+Control"));

    [Fact]
    public void ParsingATableDropsOnlyTheEntriesItCannotRead()
    {
        var parsed = ShortcutChord.Parse(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [UiCommandIds.EditCopy] = "Ctrl+Shift+C",
            [UiCommandIds.EditPaste] = "Hyper+Q",
            [UiCommandIds.ViewRefresh] = "F9"
        });

        Assert.Equal(Keys.Control | Keys.Shift | Keys.C, parsed[UiCommandIds.EditCopy]);
        Assert.Equal(Keys.F9, parsed[UiCommandIds.ViewRefresh]);
        Assert.False(parsed.ContainsKey(UiCommandIds.EditPaste));
    }

    [Fact]
    public void ParsingNothingYieldsNoBindingsRatherThanThrowing() =>
        Assert.Empty(ShortcutChord.Parse(null));
}
