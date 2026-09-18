using System.Text;

namespace StorageHub.Desktop;

/// <summary>
/// Modes the remote program has switched on that change what a key press means on the wire.
/// </summary>
internal readonly record struct VtKeyModes(bool ApplicationCursorKeys = false, bool BracketedPaste = false);

/// <summary>
/// Turns a key press into the bytes a VT-style host expects. Pure and static on purpose: this is
/// the layer where an off-by-one is invisible on screen and only shows up as an arrow key doing
/// nothing inside vim, so it is the piece most worth pinning down in tests.
/// </summary>
internal static class VtKeyEncoder
{
    private const byte Escape = 0x1B;

    /// <summary>xterm's modifier parameter: 1 + Shift + 2*Alt + 4*Ctrl.</summary>
    private static int ModifierParameter(bool shift, bool alt, bool control) =>
        1 + (shift ? 1 : 0) + (alt ? 2 : 0) + (control ? 4 : 0);

    /// <summary>
    /// Encodes a non-character key. Returns null when the key carries no meaning of its own and
    /// should be left to the ordinary character path.
    /// </summary>
    internal static byte[]? Encode(Keys key, bool shift, bool alt, bool control, VtKeyModes modes)
    {
        var modifier = ModifierParameter(shift, alt, control);
        var bytes = EncodeCore(key, shift, alt, control, modifier, modes);
        if (bytes is null)
        {
            return null;
        }

        // Alt is the ESC prefix, but only for keys that did not already fold it into an xterm
        // modifier parameter -- those are the ones whose encoding does not start with ESC.
        return alt && bytes.Length > 0 && bytes[0] != Escape
            ? [Escape, .. bytes]
            : bytes;
    }

    private static byte[]? EncodeCore(Keys key, bool shift, bool alt, bool control, int modifier, VtKeyModes modes)
    {
        switch (key)
        {
            case Keys.Enter:
                return control ? [10] : [13];
            case Keys.Back:
                // DEL is what a modern remote expects; Ctrl+Backspace is the one that sends BS.
                return control ? [8] : [127];
            case Keys.Tab:
                return shift ? Ascii("[Z") : [9];
            case Keys.Escape:
                return [Escape];
            case Keys.Space when control:
                return [0];

            case Keys.Up:
                return Cursor('A', modifier, modes);
            case Keys.Down:
                return Cursor('B', modifier, modes);
            case Keys.Right:
                return Cursor('C', modifier, modes);
            case Keys.Left:
                return Cursor('D', modifier, modes);
            case Keys.Home:
                return Cursor('H', modifier, modes);
            case Keys.End:
                return Cursor('F', modifier, modes);

            case Keys.Insert:
                return Tilde(2, modifier);
            case Keys.Delete:
                return Tilde(3, modifier);
            case Keys.PageUp:
                return Tilde(5, modifier);
            case Keys.PageDown:
                return Tilde(6, modifier);

            // F1-F4 are SS3 in their unmodified form and CSI once a modifier joins in.
            case Keys.F1:
                return Function('P', modifier);
            case Keys.F2:
                return Function('Q', modifier);
            case Keys.F3:
                return Function('R', modifier);
            case Keys.F4:
                return Function('S', modifier);
            case Keys.F5:
                return Tilde(15, modifier);
            case Keys.F6:
                return Tilde(17, modifier);
            case Keys.F7:
                return Tilde(18, modifier);
            case Keys.F8:
                return Tilde(19, modifier);
            case Keys.F9:
                return Tilde(20, modifier);
            case Keys.F10:
                return Tilde(21, modifier);
            case Keys.F11:
                return Tilde(23, modifier);
            case Keys.F12:
                return Tilde(24, modifier);

            case >= Keys.A and <= Keys.Z when control:
                return [(byte)(key - Keys.A + 1)];

            default:
                return null;
        }
    }

    private static byte[] Cursor(char final, int modifier, VtKeyModes modes)
    {
        if (modifier != 1)
        {
            return Ascii($"[1;{modifier}{final}");
        }

        // DECCKM: inside vim, less and readline the arrows are SS3, and a host that asked for
        // application cursor keys does not recognise the CSI form.
        return modes.ApplicationCursorKeys
            ? Ascii($"O{final}")
            : Ascii($"[{final}");
    }

    private static byte[] Tilde(int number, int modifier) =>
        Ascii(modifier == 1
            ? $"[{number}~"
            : $"[{number};{modifier}~");

    private static byte[] Function(char final, int modifier) =>
        Ascii(modifier == 1
            ? $"O{final}"
            : $"[1;{modifier}{final}");

    /// <summary>
    /// Prepares clipboard text for the wire: one carriage return per line, no NULs, and under
    /// bracketed paste wrapped in the markers a host uses to tell a paste from typing.
    /// </summary>
    internal static byte[] EncodePaste(string text, VtKeyModes modes)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = text
            .Replace("\r\n", "\r", StringComparison.Ordinal)
            .Replace('\n', '\r')
            .Replace("\0", string.Empty, StringComparison.Ordinal);
        if (!modes.BracketedPaste)
        {
            return Encoding.UTF8.GetBytes(normalized);
        }

        // A crafted clipboard containing the end marker would otherwise close the bracket early
        // and have everything after it run as typed input.
        normalized = normalized.Replace("[201~", string.Empty, StringComparison.Ordinal);
        return Encoding.UTF8.GetBytes($"[200~{normalized}[201~");
    }

    /// <summary>What a mouse event is: which button, and what happened to it.</summary>
    internal enum VtMouseEvent
    {
        Press,
        Release,
        Move,
        WheelUp,
        WheelDown
    }

    /// <summary>
    /// Encodes a mouse event in SGR (1006) form. The older X10 encoding cannot express a column
    /// or row past 223, which a maximised window easily exceeds, so only the SGR form is sent and
    /// a program that did not ask for it gets nothing.
    /// </summary>
    internal static byte[]? EncodeMouse(
        VtMouseEvent mouseEvent,
        MouseButtons button,
        int column,
        int row,
        bool shift,
        bool alt,
        bool control,
        bool sgrEncoding)
    {
        if (!sgrEncoding)
        {
            return null;
        }

        var code = mouseEvent switch
        {
            VtMouseEvent.WheelUp => 64,
            VtMouseEvent.WheelDown => 65,
            _ => button switch
            {
                MouseButtons.Left => 0,
                MouseButtons.Middle => 1,
                MouseButtons.Right => 2,
                _ => 3
            }
        };

        if (mouseEvent == VtMouseEvent.Move)
        {
            code += 32;
        }

        code += (shift ? 4 : 0) + (alt ? 8 : 0) + (control ? 16 : 0);

        // Coordinates are one-based on the wire.
        var final = mouseEvent == VtMouseEvent.Release ? 'm' : 'M';
        return Ascii($"[<{code};{Math.Max(1, column + 1)};{Math.Max(1, row + 1)}{final}");
    }

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);
}
