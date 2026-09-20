using Avalonia.Input;
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
        Assert.Equal(expected, ShortcutChord.Format(ShortcutKeys.ToGesture(keys)));

    [Theory]
    [MemberData(nameof(Chords))]
    public void AStoredChordParsesBackToTheSameKeys(Keys keys, string text)
    {
        Assert.True(ShortcutChord.TryParse(text, out var gesture));
        Assert.Equal(keys, ShortcutKeys.ToKeys(gesture));
    }

    /// <summary>
    /// The codec writes exactly what the WinForms build wrote, for every key.
    /// </summary>
    /// <remarks>
    /// This is the test that matters for the port. The chord is stored as the key's name, and the
    /// two enums disagree about seventeen of them - PageDown was written as "Next", CapsLock as
    /// "Capital", OemQuestion as "Oem2", Return as "Enter". Emitting Avalonia's spelling instead
    /// would rewrite every affected line of an existing config.shortcuts.json, and an older build
    /// reading it back would drop those bindings as unrecognised rather than fail loudly.
    ///
    /// Comparing against System.Windows.Forms.Keys directly is the point: it is the only source of
    /// truth for what is already on disk, and it is available here because this project still
    /// references the WinForms shell.
    /// </remarks>
    [Fact]
    public void EveryKeyStillWritesTheTextTheOldBuildWrote()
    {
        var wrong = new List<string>();

        foreach (var key in Enum.GetValues<Key>().Select(value => (int)value).Distinct().Select(value => (Key)value))
        {
            var gesture = new KeyGesture(key);
            var keys = ShortcutKeys.ToKeys(gesture);
            if (keys == Keys.None) continue;

            var expected = (keys & Keys.KeyCode).ToString();
            var actual = ShortcutChord.Format(gesture);
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                wrong.Add($"{key}: wrote '{actual}', file holds '{expected}'");
            }
        }

        Assert.Empty(wrong);
    }

    /// <summary>Whatever the old build wrote, this one reads back to the same key.</summary>
    [Fact]
    public void EveryStoredNameStillParses()
    {
        var unreadable = new List<string>();

        foreach (var keys in Enum.GetValues<Keys>().Distinct())
        {
            if (keys == Keys.None || (keys & ~Keys.KeyCode) != 0) continue;
            if (ShortcutKeys.ToGesture(keys) is null) continue;

            var stored = keys.ToString();
            if (!ShortcutChord.TryParse(stored, out var parsed) || parsed is null ||
                ShortcutKeys.ToKeys(parsed) != keys)
            {
                unreadable.Add(stored);
            }
        }

        Assert.Empty(unreadable);
    }

    /// <summary>
    /// The modifier order is fixed so that the same binding always produces the same bytes, and a
    /// diff of the file shows only what the user actually changed.
    /// </summary>
    [Fact]
    public void ModifiersAreWrittenInAFixedOrder() => Assert.Equal(
        "Ctrl+Alt+Shift+F1",
        ShortcutChord.Format(ShortcutKeys.ToGesture(Keys.Shift | Keys.Alt | Keys.Control | Keys.F1)));

    [Fact]
    public void EveryDefaultBindingRoundTrips()
    {
        var defaults = ShortcutKeys.ToGestures(ShortcutSettings.Resolve(null));

        var restored = ShortcutChord.Parse(ShortcutChord.Format(defaults));

        Assert.Equal(
            defaults.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            restored.OrderBy(pair => pair.Key, StringComparer.Ordinal));
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
        Assert.False(ShortcutChord.TryParse(text, out _));

    /// <summary>
    /// A long value costs a lookup rather than any real work, so a hand-edited file cannot turn
    /// reading settings into a slow operation.
    /// </summary>
    [Fact]
    public void AnAbsurdlyLongChordIsRefusedWithoutBeingParsed() =>
        Assert.False(ShortcutChord.TryParse(new string('A', 4096), out _));

    /// <summary>
    /// The modifier alone is not a binding, and accepting it would let a file describe a shortcut
    /// that can never fire.
    /// </summary>
    [Fact]
    public void ABareModifierIsNotAChord() => Assert.False(ShortcutChord.TryParse("Ctrl+Control", out _));

    [Fact]
    public void ParsingATableDropsOnlyTheEntriesItCannotRead()
    {
        var parsed = ShortcutChord.Parse(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [UiCommandIds.EditCopy] = "Ctrl+Shift+C",
            [UiCommandIds.EditPaste] = "Hyper+Q",
            [UiCommandIds.ViewRefresh] = "F9"
        });

        Assert.Equal(Keys.Control | Keys.Shift | Keys.C, ShortcutKeys.ToKeys(parsed[UiCommandIds.EditCopy]));
        Assert.Equal(Keys.F9, ShortcutKeys.ToKeys(parsed[UiCommandIds.ViewRefresh]));
        Assert.False(parsed.ContainsKey(UiCommandIds.EditPaste));
    }

    [Fact]
    public void ParsingNothingYieldsNoBindingsRatherThanThrowing() =>
        Assert.Empty(ShortcutChord.Parse(null));
}
