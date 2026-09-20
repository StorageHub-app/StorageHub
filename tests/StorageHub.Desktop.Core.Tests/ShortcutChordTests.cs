using Avalonia.Input;
using StorageHub.Desktop.Configuration;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// That a keyboard shortcut survives being written to a file and read back.
/// </summary>
/// <remarks>
/// Chords are stored as text rather than as an enum, because CodeLogic's camel-case string enum
/// converter would render a flags combination as something like <c>"control, shiftKey, s"</c>.
/// That makes the codec here the thing standing between a user's rebindings and silent loss, so
/// it is tested on its own rather than only through the store.
///
/// The one thing this file cannot check is that the text matches what the WinForms build wrote,
/// because that needs the enum that build used. ShortcutChordWinFormsFidelityTests does it, over
/// in the project that still references the shell, and dies with it.
/// </remarks>
public sealed class ShortcutChordTests
{
    public static TheoryData<Key, KeyModifiers, string> Chords => new()
    {
        { Key.S, KeyModifiers.Control, "Ctrl+S" },
        { Key.S, KeyModifiers.Control | KeyModifiers.Shift, "Ctrl+Shift+S" },
        { Key.Delete, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift, "Ctrl+Alt+Shift+Delete" },
        { Key.Left, KeyModifiers.Alt, "Alt+Left" },
        { Key.F5, KeyModifiers.None, "F5" },
        { Key.Delete, KeyModifiers.None, "Delete" },
        { Key.D1, KeyModifiers.Control, "Ctrl+D1" },
        { Key.OemPlus, KeyModifiers.Control, "Ctrl+Oemplus" }
    };

    [Theory]
    [MemberData(nameof(Chords))]
    public void AChordFormatsToItsStoredText(Key key, KeyModifiers modifiers, string expected) =>
        Assert.Equal(expected, ShortcutChord.Format(new KeyGesture(key, modifiers)));

    [Theory]
    [MemberData(nameof(Chords))]
    public void AStoredChordParsesBackToTheSameChord(Key key, KeyModifiers modifiers, string text)
    {
        Assert.True(ShortcutChord.TryParse(text, out var gesture));
        Assert.Equal(new KeyGesture(key, modifiers), gesture);
    }

    /// <summary>Nothing bound writes and reads as the same word.</summary>
    [Fact]
    public void AnUnassignedChordRoundTrips()
    {
        Assert.Equal("Unassigned", ShortcutChord.Format((KeyGesture?)null));
        Assert.True(ShortcutChord.TryParse("Unassigned", out var gesture));
        Assert.Null(gesture);
    }

    /// <summary>Every shortcut the shell ships with survives a trip through the file.</summary>
    [Fact]
    public void EveryDefaultBindingRoundTrips()
    {
        var defaults = ShortcutSettings.Resolve(null);

        var restored = ShortcutChord.Parse(ShortcutChord.Format(defaults));

        Assert.Equal(
            defaults.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            restored.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    /// <summary>
    /// The modifier order is fixed so that the same binding always produces the same bytes, and a
    /// diff of the file shows only what the user actually changed.
    /// </summary>
    [Fact]
    public void ModifiersAreWrittenInAFixedOrder() => Assert.Equal(
        "Ctrl+Alt+Shift+F1",
        ShortcutChord.Format(new KeyGesture(
            Key.F1,
            KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Control)));
}
