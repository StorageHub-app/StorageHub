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
