using System.Text;

namespace StorageHub.Desktop.Tests;

public sealed class VtKeyEncoderTests
{
    [Theory]
    // Ordinary cursor keys.
    [InlineData(Keys.Up, false, false, false, "[A")]
    [InlineData(Keys.Down, false, false, false, "[B")]
    [InlineData(Keys.Right, false, false, false, "[C")]
    [InlineData(Keys.Left, false, false, false, "[D")]
    [InlineData(Keys.Home, false, false, false, "[H")]
    [InlineData(Keys.End, false, false, false, "[F")]
    // Editing and paging keys, none of which the old ad-hoc switch sent at all.
    [InlineData(Keys.Insert, false, false, false, "[2~")]
    [InlineData(Keys.Delete, false, false, false, "[3~")]
    [InlineData(Keys.PageUp, false, false, false, "[5~")]
    [InlineData(Keys.PageDown, false, false, false, "[6~")]
    // F1-F4 are SS3, F5 upwards are CSI with a number.
    [InlineData(Keys.F1, false, false, false, "OP")]
    [InlineData(Keys.F4, false, false, false, "OS")]
    [InlineData(Keys.F5, false, false, false, "[15~")]
    [InlineData(Keys.F12, false, false, false, "[24~")]
    // Modifiers use xterm's 1 + Shift + 2*Alt + 4*Ctrl.
    [InlineData(Keys.Up, true, false, false, "[1;2A")]
    [InlineData(Keys.Right, false, false, true, "[1;5C")]
    [InlineData(Keys.Left, true, false, true, "[1;6D")]
    [InlineData(Keys.Delete, false, false, true, "[3;5~")]
    [InlineData(Keys.F1, false, false, true, "[1;5P")]
    // Shift+Tab is back-tab, not a modified tab.
    [InlineData(Keys.Tab, true, false, false, "[Z")]
    public void Keys_encode_to_the_bytes_the_host_expects(
        Keys key, bool shift, bool alt, bool control, string expected)
    {
        var bytes = VtKeyEncoder.Encode(key, shift, alt, control, default);

        Assert.Equal(Encoding.ASCII.GetBytes(expected), bytes);
    }

    [Theory]
    [InlineData(Keys.Enter, new byte[] { 13 })]
    [InlineData(Keys.Tab, new byte[] { 9 })]
    [InlineData(Keys.Escape, new byte[] { 0x1B })]
    // DEL, not BS: a modern remote erases backwards on 0x7F.
    [InlineData(Keys.Back, new byte[] { 127 })]
    public void Unmodified_control_keys_send_their_single_byte(Keys key, byte[] expected) =>
        Assert.Equal(expected, VtKeyEncoder.Encode(key, false, false, false, default)!);

    [Theory]
    [InlineData(Keys.C, 3)]
    [InlineData(Keys.D, 4)]
    [InlineData(Keys.L, 12)]
    [InlineData(Keys.Z, 26)]
    [InlineData(Keys.A, 1)]
    public void Control_letters_map_onto_one_through_twenty_six(Keys key, byte expected) =>
        Assert.Equal([expected], VtKeyEncoder.Encode(key, false, false, true, default)!);

    [Fact]
    public void Control_space_sends_nul_and_control_backspace_sends_backspace()
    {
        Assert.Equal([0], VtKeyEncoder.Encode(Keys.Space, false, false, true, default)!);
        Assert.Equal([8], VtKeyEncoder.Encode(Keys.Back, false, false, true, default)!);
    }

    [Fact]
    public void Alt_prefixes_an_escape()
    {
        // Alt+b is how readline moves back a word, and it is ESC followed by the letter.
        Assert.Equal([0x1B, 2], VtKeyEncoder.Encode(Keys.B, false, alt: true, control: true, default)!);
    }

    [Fact]
    public void Application_cursor_keys_switch_the_arrows_to_ss3()
    {
        var modes = new VtKeyModes(ApplicationCursorKeys: true);

        // vim and readline turn DECCKM on; under it the CSI form is simply not recognised, which
        // is what made the arrows dead inside a full-screen program.
        Assert.Equal(Encoding.ASCII.GetBytes("OA"), VtKeyEncoder.Encode(Keys.Up, false, false, false, modes));
        Assert.Equal(Encoding.ASCII.GetBytes("OH"), VtKeyEncoder.Encode(Keys.Home, false, false, false, modes));

        // A modifier still forces the CSI form, mode or no mode.
        Assert.Equal(Encoding.ASCII.GetBytes("[1;2A"), VtKeyEncoder.Encode(Keys.Up, true, false, false, modes));
    }

    [Theory]
    [InlineData(Keys.F13)]
    [InlineData(Keys.CapsLock)]
    [InlineData(Keys.LWin)]
    [InlineData(Keys.A)]
    public void Keys_with_no_meaning_of_their_own_are_left_to_the_character_path(Keys key) =>
        Assert.Null(VtKeyEncoder.Encode(key, false, false, false, default));

    [Fact]
    public void Pasted_text_is_normalised_to_carriage_returns_without_nuls()
    {
        var bytes = VtKeyEncoder.EncodePaste("one\r\ntwo\nthree\0", default);

        Assert.Equal("one\rtwo\rthree", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Bracketed_paste_wraps_the_text_in_its_markers()
    {
        var bytes = VtKeyEncoder.EncodePaste("ls -la", new VtKeyModes(BracketedPaste: true));

        Assert.Equal("[200~ls -la[201~", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void A_crafted_clipboard_cannot_close_the_paste_bracket_early()
    {
        // Without stripping the end marker, everything after it would leave the paste and run as
        // if it had been typed -- clipboard contents becoming commands.
        var bytes = VtKeyEncoder.EncodePaste(
            "safe[201~rm -rf /",
            new VtKeyModes(BracketedPaste: true));

        var text = Encoding.UTF8.GetString(bytes);
        Assert.Equal("[200~saferm -rf /[201~", text);
        Assert.Equal(1, CountOccurrences(text, "[201~"));
    }

    [Theory]
    [InlineData(0, MouseButtons.Left, "[<0;6;3M")]
    [InlineData(0, MouseButtons.Middle, "[<1;6;3M")]
    [InlineData(0, MouseButtons.Right, "[<2;6;3M")]
    [InlineData(1, MouseButtons.Left, "[<0;6;3m")]
    [InlineData(2, MouseButtons.Left, "[<32;6;3M")]
    [InlineData(3, MouseButtons.None, "[<64;6;3M")]
    [InlineData(4, MouseButtons.None, "[<65;6;3M")]
    public void Mouse_events_encode_in_the_sgr_form(
        int mouseEvent, MouseButtons button, string expected)
    {
        // Column 5 and row 2 zero-based become 6 and 3 on the wire.
        var bytes = VtKeyEncoder.EncodeMouse(
            (VtKeyEncoder.VtMouseEvent)mouseEvent, button, column: 5, row: 2,
            shift: false, alt: false, control: false, sgrEncoding: true);

        Assert.Equal("" + expected, Encoding.ASCII.GetString(bytes!));
    }

    [Fact]
    public void A_program_that_did_not_ask_for_sgr_mouse_reporting_gets_nothing()
    {
        // The older X10 encoding cannot express a column past 223, which a maximised window
        // exceeds easily, so sending it would be worse than sending nothing.
        Assert.Null(VtKeyEncoder.EncodeMouse(
            VtKeyEncoder.VtMouseEvent.Press, MouseButtons.Left, 5, 2,
            shift: false, alt: false, control: false, sgrEncoding: false));
    }

    [Fact]
    public void Mouse_modifiers_are_folded_into_the_button_code()
    {
        var bytes = VtKeyEncoder.EncodeMouse(
            VtKeyEncoder.VtMouseEvent.Press, MouseButtons.Left, 0, 0,
            shift: true, alt: true, control: true, sgrEncoding: true);

        // 4 + 8 + 16 on top of button 0.
        Assert.Equal("[<28;1;1M", Encoding.ASCII.GetString(bytes!));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var index = haystack.IndexOf(needle, StringComparison.Ordinal);
            index >= 0;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
