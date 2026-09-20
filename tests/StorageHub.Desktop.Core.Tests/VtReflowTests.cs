using System.Text;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Resize is where the old buffer lost output outright: it allocated a fresh grid and copied what
/// fitted, so narrowing the window truncated every line and widening it never put anything back.
/// These pin down that text survives a resize and comes back the way it went in.
/// </summary>
public sealed class VtReflowTests
{
    [Fact]
    public void Narrowing_then_widening_returns_the_original_line()
    {
        var emulator = new VtTerminalEmulator(80, 10);
        var sentence = new string('x', 80);
        emulator.Feed(sentence);

        emulator.Resize(40, 10);
        emulator.Resize(80, 10);

        Assert.Equal(sentence, AllText(emulator).TrimEnd());
    }

    [Fact]
    public void Every_line_survives_a_narrowing_resize()
    {
        var emulator = new VtTerminalEmulator(60, 8);
        for (var index = 0; index < 40; index++)
        {
            emulator.Feed($"line-{index:D2}\r\n");
        }

        emulator.Resize(20, 8);

        // Boundedness is not enough: the old test only asserted the buffer stayed small, which a
        // resize that threw the text away also satisfies.
        var text = AllText(emulator);
        for (var index = 0; index < 40; index++)
        {
            Assert.Contains($"line-{index:D2}", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Widening_pulls_scrollback_back_onto_the_screen()
    {
        var emulator = new VtTerminalEmulator(20, 4);
        for (var index = 0; index < 10; index++)
        {
            emulator.Feed($"row{index}\r\n");
        }

        var before = emulator.Document.TotalLineCount;
        emulator.Resize(20, 10);

        // The extra rows come from history rather than being invented blank.
        Assert.Equal(before, emulator.Document.TotalLineCount);
        Assert.Contains("row5", ScreenText(emulator), StringComparison.Ordinal);
    }

    [Fact]
    public void A_wrapped_line_rejoins_and_splits_at_the_new_width()
    {
        var emulator = new VtTerminalEmulator(10, 6);

        // Twenty characters at ten columns is two physical rows of one logical line.
        emulator.Feed("abcdefghijklmnopqrst");

        emulator.Resize(20, 6);

        // At twenty columns it is one row again, and no longer marked as wrapping.
        var top = FirstNonEmptyLine(emulator);
        Assert.Equal("abcdefghijklmnopqrst", RowText(emulator, top).TrimEnd());
        Assert.False(emulator.Document.FindLine(top)!.WrappedToNext);
    }

    [Fact]
    public void A_deliberate_line_break_is_never_joined_away()
    {
        var emulator = new VtTerminalEmulator(10, 6);
        emulator.Feed("one\r\ntwo\r\n");

        emulator.Resize(40, 6);

        // Widening must not run them together: the break was the program's, not the width's, so
        // the two words stay on separate lines however wide the window gets.
        var lines = AllText(emulator)
            .Split(Environment.NewLine)
            .Where(line => line.Length > 0)
            .ToArray();

        Assert.Equal(["one", "two"], lines);
    }

    [Fact]
    public void The_cursor_lands_on_the_same_character_after_a_resize()
    {
        var emulator = new VtTerminalEmulator(20, 6);
        emulator.Feed("prompt$ typed text here");

        var before = CharacterUnderCursor(emulator);
        emulator.Resize(12, 6);
        var after = CharacterUnderCursor(emulator);

        Assert.Equal(before, after);
    }

    [Fact]
    public void A_coloured_background_row_is_not_trimmed_away_by_reflow()
    {
        var emulator = new VtTerminalEmulator(20, 4);

        // A row of spaces on blue: htop's meter bars and vim's status line look exactly like this,
        // and treating a coloured space as blank strips the colour off the right of every one.
        emulator.Feed("[44m" + new string(' ', 20) + "[0m");

        emulator.Resize(40, 4);

        var line = emulator.Document.FindLine(FirstNonEmptyLine(emulator))!;
        Assert.Equal(20, line.TrimmedLength());
        Assert.Equal(VtColorKind.Indexed, line[0].Background.Kind);
        Assert.Equal(4, line[0].Background.Index);
    }

    [Fact]
    public void Resizing_while_the_alternate_screen_is_showing_still_reflows_the_primary()
    {
        var emulator = new VtTerminalEmulator(80, 6);
        var sentence = new string('y', 80);
        emulator.Feed(sentence);

        emulator.Feed("[?1049h");
        emulator.Resize(40, 6);
        emulator.Resize(80, 6);
        emulator.Feed("[?1049l");

        // The stashed primary content is reflowed even though it was not on screen, so quitting
        // vim after a resize hands back correctly wrapped scrollback rather than shredded lines.
        Assert.Equal(sentence, AllText(emulator).TrimEnd());
    }

    [Fact]
    public void Resizing_to_the_same_size_changes_nothing()
    {
        var emulator = new VtTerminalEmulator(40, 8);
        emulator.Feed("stable\r\n");
        var before = AllText(emulator);
        var revision = emulator.Document.Revision;

        emulator.Resize(40, 8);

        Assert.Equal(before, AllText(emulator));
        Assert.Equal(revision, emulator.Document.Revision);
    }

    [Fact]
    public void Scrollback_trimming_moves_the_numbering_instead_of_renumbering()
    {
        var emulator = new VtTerminalEmulator(20, 4, maximumScrollbackLines: 100);
        for (var index = 0; index < 400; index++)
        {
            emulator.Feed($"flood {index}\r\n");
        }

        // The oldest line still held is not line zero any more, and everything that remains kept
        // the number it had -- which is what stops a selection sliding while output pours in.
        Assert.True(emulator.Document.FirstLineNumber > 0);
        Assert.Equal(
            emulator.Document.FirstLineNumber + emulator.Document.TotalLineCount - 1,
            emulator.Document.LastLineNumber);
        Assert.Contains("flood 399", ScreenText(emulator), StringComparison.Ordinal);
    }

    private static long FirstNonEmptyLine(VtTerminalDocument document)
    {
        for (var number = document.FirstLineNumber; number <= document.LastLineNumber; number++)
        {
            var line = document.FindLine(number);
            if (line is not null && line.TrimmedLength() > 0)
            {
                return number;
            }
        }

        return document.FirstLineNumber;
    }

    private static long FirstNonEmptyLine(VtTerminalEmulator emulator) => FirstNonEmptyLine(emulator.Document);

    private static string RowText(VtTerminalEmulator emulator, long lineNumber)
    {
        var line = emulator.Document.FindLine(lineNumber);
        if (line is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        line.AppendTextTo(builder, 0, line.Length);
        return builder.ToString();
    }

    /// <summary>
    /// A space at the wrap column survives being rejoined.
    /// </summary>
    /// <remarks>
    /// Found by a terminal session test, and it is the reading of a terminal that it breaks. A
    /// trailing space is padding on a line that ended and content on a line that wrapped; trimming
    /// it either way deletes the one word-break unlucky enough to land in the last column, so
    /// copied text comes back with two words fused -- occasionally, and only ever at whatever width
    /// the window happened to be. The reflow above has always drawn this distinction; reading the
    /// text did not.
    /// </remarks>
    [Fact]
    public void RejoiningAWrappedLineKeepsTheSpaceAtTheSeam()
    {
        var emulator = new VtTerminalEmulator(20, 5);

        // Nineteen characters, then the space that lands in the twentieth column, then the word
        // that wrapped onto the line below it.
        emulator.Feed("nineteen chars here wrapped");

        Assert.Contains("here wrapped", AllText(emulator));
    }

    /// <summary>
    /// A shorter window drops the blank rows below the cursor, not the output above it.
    /// </summary>
    /// <remarks>
    /// Found in a screenshot of a terminal pane, whose first line was missing. Every session opens
    /// at a default grid and is resized to the pane's real one as soon as it has been laid out, so
    /// almost every terminal shrinks by a row or two while its screen is nearly empty. Trimming
    /// from the top there scrolled the first line of every session into the scrollback before it
    /// had been read once.
    /// </remarks>
    [Fact]
    public void ShrinkingAMostlyEmptyScreenKeepsTheOutputOnIt()
    {
        var emulator = new VtTerminalEmulator(40, 24);
        emulator.Feed("first line\r\nsecond line\r\n");

        emulator.Resize(40, 20);

        Assert.Equal(0, emulator.Document.ScrollbackCount);
        Assert.Contains("first line", ScreenText(emulator));
    }

    /// <summary>
    /// And so does one that changed width at the same time, which is the usual case.
    /// </summary>
    /// <remarks>
    /// A pane is almost never exactly as wide as the grid a session opened at, so the first resize
    /// changes both -- and that takes the reflow path rather than the row-trimming one. The same
    /// defect lived in both, and this is the one the screenshot caught: a terminal that had said
    /// four lines showed three.
    /// </remarks>
    [Fact]
    public void ShrinkingBothDimensionsKeepsTheOutputOnScreen()
    {
        var emulator = new VtTerminalEmulator(80, 24);
        emulator.Feed("first line\r\nsecond line\r\nthird line\r\n$ ");

        emulator.Resize(77, 23);

        Assert.Equal(0, emulator.Document.ScrollbackCount);
        Assert.Contains("first line", ScreenText(emulator));
    }

    /// <summary>
    /// And once the output itself does not fit, the top goes to the scrollback rather than away.
    /// </summary>
    [Fact]
    public void ShrinkingPastTheOutputPushesItIntoScrollback()
    {
        var emulator = new VtTerminalEmulator(40, 6);
        for (var line = 0; line < 6; line++)
        {
            emulator.Feed($"line {line}\r\n");
        }

        emulator.Resize(40, 5);

        Assert.True(emulator.Document.ScrollbackCount > 0);
        Assert.Contains("line 0", AllText(emulator));
    }

    private static char CharacterUnderCursor(VtTerminalEmulator emulator)
    {
        var line = emulator.Document.FindLine(emulator.Document.CursorLineNumber)!;
        return (char)line[Math.Max(0, emulator.Document.Screen.CursorColumn - 1)].Codepoint;
    }

    private static string AllText(VtTerminalEmulator emulator) =>
        emulator.Document.GetText(
            emulator.Document.FirstLineNumber,
            emulator.Document.TotalLineCount,
            joinWrapped: true);

    private static string ScreenText(VtTerminalEmulator emulator) =>
        emulator.Document.GetText(
            emulator.Document.ScreenTopLineNumber,
            emulator.Document.Rows,
            joinWrapped: true);
}
