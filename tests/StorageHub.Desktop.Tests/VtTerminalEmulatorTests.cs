using System.Text;

namespace StorageHub.Desktop.Tests;

public sealed class VtTerminalEmulatorTests
{
    [Fact]
    public void Printing_past_the_last_column_wraps_and_records_the_wrap()
    {
        var emulator = new VtTerminalEmulator(10, 4);

        emulator.Feed("0123456789A");

        Assert.Equal("0123456789", Row(emulator, 0));
        Assert.Equal("A", Row(emulator, 1).TrimEnd());

        // The wrap has to be recorded, because it is the only thing reflow can join on later.
        Assert.True(emulator.Document.FindLine(emulator.Document.ScreenTopLineNumber)!.WrappedToNext);
    }

    [Fact]
    public void A_line_that_exactly_fills_the_width_does_not_scroll_until_more_arrives()
    {
        var emulator = new VtTerminalEmulator(10, 3);

        emulator.Feed("0123456789");

        // A progress bar filling the row must redraw in place, not push the screen up.
        Assert.Equal(0, emulator.Document.Screen.CursorRow);

        emulator.Feed("\r100%");
        Assert.Equal("100%456789", Row(emulator, 0).TrimEnd());
    }

    [Fact]
    public void Autowrap_off_overwrites_the_last_column_instead_of_wrapping()
    {
        var emulator = new VtTerminalEmulator(5, 3);

        // less and vim turn DECAWM off while drawing their last column.
        emulator.Feed("[?7l");
        emulator.Feed("ABCDEFG");

        Assert.Equal("ABCDG", Row(emulator, 0));
        Assert.Equal(string.Empty, Row(emulator, 1).TrimEnd());
    }

    [Fact]
    public void The_alternate_screen_leaves_the_primary_untouched()
    {
        var emulator = new VtTerminalEmulator(20, 5);
        emulator.Feed("shell line one\r\nshell line two\r\n");
        var before = emulator.Document.GetText(
            emulator.Document.FirstLineNumber, emulator.Document.TotalLineCount, joinWrapped: false);

        // Enter, scribble all over it, and leave -- what vim does.
        emulator.Feed("[?1049h");
        Assert.True(emulator.Document.UsingAlternateScreen);
        emulator.Feed("[2J[Hvim is drawing here\r\nand here too");
        emulator.Feed("[?1049l");

        Assert.False(emulator.Document.UsingAlternateScreen);
        var after = emulator.Document.GetText(
            emulator.Document.FirstLineNumber, emulator.Document.TotalLineCount, joinWrapped: false);
        Assert.Equal(before, after);
    }

    [Fact]
    public void The_alternate_screen_never_adds_to_scrollback()
    {
        var emulator = new VtTerminalEmulator(10, 3);
        emulator.Feed("one\r\ntwo\r\n");
        var scrollbackBefore = emulator.Document.ScrollbackCount;

        emulator.Feed("[?1049h");
        for (var line = 0; line < 50; line++)
        {
            emulator.Feed($"alt {line}\r\n");
        }

        emulator.Feed("[?1049l");

        // Fifty lines of vim output must not have pushed the shell's history away.
        Assert.Equal(scrollbackBefore, emulator.Document.ScrollbackCount);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(1047)]
    [InlineData(1049)]
    public void The_legacy_alternate_screen_modes_all_switch(int mode)
    {
        var emulator = new VtTerminalEmulator(10, 3);

        emulator.Feed($"[?{mode}h");
        Assert.True(emulator.Document.UsingAlternateScreen);

        emulator.Feed($"[?{mode}l");
        Assert.False(emulator.Document.UsingAlternateScreen);
    }

    [Fact]
    public void Designating_a_character_set_consumes_its_selector()
    {
        var emulator = new VtTerminalEmulator(10, 2);

        // Almost every login prints this. The old parser let the 'B' fall through and print.
        emulator.Feed("(B$ ");

        Assert.Equal("$", Row(emulator, 0).TrimEnd());
    }

    [Fact]
    public void The_dec_graphics_set_draws_box_characters()
    {
        var emulator = new VtTerminalEmulator(10, 2);

        emulator.Feed("(0qx(Bq");

        // 'q' and 'x' are the horizontal and vertical line; after the switch back 'q' is a letter.
        Assert.Equal("─│q", Row(emulator, 0).TrimEnd());
    }

    [Fact]
    public void Reverse_index_scrolls_down_at_the_top_of_the_region()
    {
        var emulator = new VtTerminalEmulator(10, 3);
        emulator.Feed("one\r\ntwo\r\nthree");

        emulator.Feed("[H");
        emulator.Feed("M");

        // nano and less scroll backwards with ESC M; the top line moves down instead of away.
        Assert.Equal(string.Empty, Row(emulator, 0).TrimEnd());
        Assert.Equal("one", Row(emulator, 1).TrimEnd());
        Assert.Equal("two", Row(emulator, 2).TrimEnd());
    }

    [Fact]
    public void Erasing_paints_the_current_background_and_drops_the_attributes()
    {
        var emulator = new VtTerminalEmulator(8, 2);

        // Bold red on blue, then erase the line. The erased cells keep the blue and lose the rest.
        emulator.Feed("[1;31;44mXXXX[2K");

        var line = emulator.Document.GetLine(emulator.Document.ScreenTopLineNumber);
        var cell = line[0];
        Assert.Equal(' ', cell.Codepoint);
        Assert.Equal(VtColorKind.Indexed, cell.Background.Kind);
        Assert.Equal(4, cell.Background.Index);
        Assert.Equal(VtCellFlags.None, cell.Flags);
        Assert.Equal(VtColorKind.Default, cell.Foreground.Kind);
    }

    [Fact]
    public void Inverse_video_is_recorded_and_cleared()
    {
        var emulator = new VtTerminalEmulator(8, 2);

        emulator.Feed("[7mA[27mB");

        var line = emulator.Document.GetLine(emulator.Document.ScreenTopLineNumber);
        Assert.True(line[0].Flags.HasFlag(VtCellFlags.Inverse));
        Assert.False(line[1].Flags.HasFlag(VtCellFlags.Inverse));
    }

    [Theory]
    [InlineData("[38;5;120mA", true, 120)]
    [InlineData("[38;2;10;20;30mA", false, 0)]
    [InlineData("[91mA", true, 9)]
    public void Extended_colours_keep_their_unresolved_form(string input, bool indexed, int index)
    {
        var emulator = new VtTerminalEmulator(8, 2);

        emulator.Feed(input);

        var cell = emulator.Document.GetLine(emulator.Document.ScreenTopLineNumber)[0];
        Assert.Equal(indexed ? VtColorKind.Indexed : VtColorKind.Rgb, cell.Foreground.Kind);
        if (indexed)
        {
            Assert.Equal(index, cell.Foreground.Index);
        }
        else
        {
            Assert.Equal(10, cell.Foreground.Red);
            Assert.Equal(20, cell.Foreground.Green);
            Assert.Equal(30, cell.Foreground.Blue);
        }
    }

    [Fact]
    public void A_cursor_position_report_answers_with_the_exact_bytes()
    {
        var emulator = new VtTerminalEmulator(80, 24);
        byte[]? response = null;
        emulator.ResponseRequested += (_, args) => response = args.Response;

        emulator.Feed("[5;9H[6n");

        // TUIs block on this reply, so a wrong or missing answer is a hang, not a glitch.
        Assert.Equal("[5;9R", Encoding.ASCII.GetString(response!));
    }

    [Fact]
    public void Device_attributes_answer_as_a_colour_capable_vt220()
    {
        var emulator = new VtTerminalEmulator(80, 24);
        byte[]? response = null;
        emulator.ResponseRequested += (_, args) => response = args.Response;

        emulator.Feed("[c");

        Assert.Equal("[?62;1;6;22c", Encoding.ASCII.GetString(response!));
    }

    [Fact]
    public void Private_modes_are_dispatched_rather_than_having_their_marker_trimmed_away()
    {
        var emulator = new VtTerminalEmulator(10, 3);

        emulator.Feed("[?1h[?25l[?2004h[?7l");

        Assert.True(emulator.ApplicationCursorKeys);
        Assert.False(emulator.CursorVisible);
        Assert.True(emulator.BracketedPaste);
        Assert.False(emulator.AutoWrap);

        emulator.Feed("[?1l[?25h[?2004l[?7h");

        Assert.False(emulator.ApplicationCursorKeys);
        Assert.True(emulator.CursorVisible);
        Assert.False(emulator.BracketedPaste);
        Assert.True(emulator.AutoWrap);
    }

    [Fact]
    public void An_osc_title_is_raised_and_never_printed()
    {
        var emulator = new VtTerminalEmulator(20, 3);
        string? title = null;
        emulator.TitleChanged += (_, args) => title = args.Title;

        emulator.Feed("]0;user@host: ~ready");

        Assert.Equal("user@host: ~", title);
        Assert.Equal("ready", Row(emulator, 0).TrimEnd());
    }

    [Fact]
    public void A_clipboard_write_from_the_remote_host_is_swallowed()
    {
        var emulator = new VtTerminalEmulator(20, 3);

        // OSC 52 is an exfiltration and injection route; it must do nothing and print nothing.
        emulator.Feed("]52;c;aGVsbG8=ok");

        Assert.Equal("ok", Row(emulator, 0).TrimEnd());
    }

    [Theory]
    [InlineData("Psomething long\\done")]
    [InlineData("^private message\\done")]
    [InlineData("_application program\\done")]
    public void String_payloads_are_swallowed_whole(string input)
    {
        var emulator = new VtTerminalEmulator(20, 3);

        emulator.Feed(input);

        Assert.Equal("done", Row(emulator, 0).TrimEnd());
    }

    [Fact]
    public void Tab_stops_can_be_moved_and_cleared()
    {
        var emulator = new VtTerminalEmulator(40, 3);

        // Default stops every eight columns.
        emulator.Feed("a\tb");
        Assert.Equal("a       b", Row(emulator, 0).TrimEnd());

        // Clear them all, set one at column 3, and the tab goes there instead of to 8.
        emulator.Feed("\r[2K[3g[4GH[1Gx\ty");
        Assert.Equal("x  y", Row(emulator, 0).TrimEnd());
    }

    [Fact]
    public void Scroll_regions_keep_the_rest_of_the_screen_still()
    {
        var emulator = new VtTerminalEmulator(10, 5);
        emulator.Feed("one\r\ntwo\r\nthree\r\nfour\r\nfive");

        // Region covering rows 2-4, then scroll inside it.
        emulator.Feed("[2;4r[4;1H\n");

        Assert.Equal("one", Row(emulator, 0).TrimEnd());
        Assert.Equal("three", Row(emulator, 1).TrimEnd());
        Assert.Equal("four", Row(emulator, 2).TrimEnd());
        Assert.Equal(string.Empty, Row(emulator, 3).TrimEnd());
        Assert.Equal("five", Row(emulator, 4).TrimEnd());
    }

    [Fact]
    public void One_printable_character_damages_exactly_one_row()
    {
        var emulator = new VtTerminalEmulator(20, 10);
        emulator.Feed("settle");
        _ = emulator.Document.TakeDamage();

        emulator.Feed("x");

        var damage = emulator.Document.TakeDamage();
        Assert.False(damage.FullRepaint);
        Assert.True(damage.HasRows);
        Assert.Equal(damage.FirstLine, damage.LastLine);
    }

    [Fact]
    public void Taking_damage_clears_it()
    {
        var emulator = new VtTerminalEmulator(20, 10);
        emulator.Feed("x");

        Assert.False(emulator.Document.TakeDamage().IsEmpty);
        Assert.True(emulator.Document.TakeDamage().IsEmpty);
    }

    [Fact]
    public void A_soft_reset_restores_the_defaults_without_clearing_the_screen()
    {
        var emulator = new VtTerminalEmulator(10, 4);
        emulator.Feed("kept\r\n[?7l[?25l[2;3r");

        emulator.Feed("[!p");

        Assert.True(emulator.AutoWrap);
        Assert.True(emulator.CursorVisible);
        Assert.Equal(0, emulator.Document.Screen.ScrollTop);
        Assert.Equal(3, emulator.Document.Screen.ScrollBottom);
        Assert.Equal("kept", Row(emulator, 0).TrimEnd());
    }

    [Fact]
    public void Astral_plane_text_survives_as_one_cell()
    {
        var emulator = new VtTerminalEmulator(10, 2);

        emulator.Feed("\U0001F600");

        var cell = emulator.Document.GetLine(emulator.Document.ScreenTopLineNumber)[0];
        Assert.Equal(0x1F600, cell.Codepoint);
    }

    [Fact]
    public void Shifting_out_before_any_designation_still_prints_letters()
    {
        var emulator = new VtTerminalEmulator(10, 2);

        // An undesignated G1 is ASCII. Defaulting it to the graphics set would turn ordinary
        // letters into box-drawing characters for any program that shifts out without designating.
        emulator.Feed("ABC");

        Assert.Equal("ABC", Row(emulator, 0).TrimEnd());
    }

    private static string Row(VtTerminalEmulator emulator, int row)
    {
        var line = emulator.Document.FindLine(emulator.Document.ScreenTopLineNumber + row);
        if (line is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        line.AppendTextTo(builder, 0, line.Length);
        return builder.ToString().TrimEnd();
    }
}
