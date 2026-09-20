using System.Text;
using Avalonia.Input;

namespace StorageHub.Desktop;

/// <summary>
/// Modes the remote program has switched on that change what a key press means on the wire.
/// </summary>
internal readonly record struct VtKeyModes(bool ApplicationCursorKeys = false, bool BracketedPaste = false);

/// <summary>
/// Turns a key press into the bytes a VT-style host expects. Pure and static on purpose: this is
/// the layer where an off-by-one is invisible on screen and only shows up as an arrow key doing
/// nothing inside vim, so it is the piece most worth pinning down in tests.
///
/// It named System.Windows.Forms.Keys and MouseButtons until the port, which is the only reason it
/// sat in the shell rather than beside the emulator it feeds. Every key it encodes is spelled the
/// same by both enums, so the move is a rename; ShortcutChord holds the table for the twenty-one
/// that are not, and none of them appear here.
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
    internal static byte[]? Encode(Key key, bool shift, bool alt, bool control, VtKeyModes modes)
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

    private static byte[]? EncodeCore(Key key, bool shift, bool alt, bool control, int modifier, VtKeyModes modes)
    {
        switch (key)
        {
            case Key.Enter:
                return control ? [10] : [13];
            case Key.Back:
                // DEL is what a modern remote expects; Ctrl+Backspace is the one that sends BS.
                return control ? [8] : [127];
            case Key.Tab:
                return shift ? Ascii("[Z") : [9];
            case Key.Escape:
                return [Escape];
            case Key.Space when control:
                return [0];

            case Key.Up:
                return Cursor('A', modifier, modes);
            case Key.Down:
                return Cursor('B', modifier, modes);
            case Key.Right:
                return Cursor('C', modifier, modes);
            case Key.Left:
                return Cursor('D', modifier, modes);
            case Key.Home:
                return Cursor('H', modifier, modes);
            case Key.End:
                return Cursor('F', modifier, modes);

            case Key.Insert:
                return Tilde(2, modifier);
            case Key.Delete:
                return Tilde(3, modifier);
            case Key.PageUp:
                return Tilde(5, modifier);
            case Key.PageDown:
                return Tilde(6, modifier);

            // F1-F4 are SS3 in their unmodified form and CSI once a modifier joins in.
            case Key.F1:
                return Function('P', modifier);
            case Key.F2:
                return Function('Q', modifier);
            case Key.F3:
                return Function('R', modifier);
            case Key.F4:
                return Function('S', modifier);
            case Key.F5:
                return Tilde(15, modifier);
            case Key.F6:
                return Tilde(17, modifier);
            case Key.F7:
                return Tilde(18, modifier);
            case Key.F8:
                return Tilde(19, modifier);
            case Key.F9:
                return Tilde(20, modifier);
            case Key.F10:
                return Tilde(21, modifier);
            case Key.F11:
                return Tilde(23, modifier);
            case Key.F12:
                return Tilde(24, modifier);

            case >= Key.A and <= Key.Z when control:
                return [(byte)(key - Key.A + 1)];

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
        MouseButton button,
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
                MouseButton.Left => 0,
                MouseButton.Middle => 1,
                MouseButton.Right => 2,
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
