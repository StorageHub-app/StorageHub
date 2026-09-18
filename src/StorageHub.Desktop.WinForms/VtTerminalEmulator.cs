using System.Globalization;
using System.Text;

namespace StorageHub.Desktop;

/// <summary>Bytes the emulator owes the host in reply to a query such as DSR or DA.</summary>
internal sealed class VtResponseEventArgs(byte[] response) : EventArgs
{
    internal byte[] Response { get; } = response;
}

internal sealed class VtTitleEventArgs(string title) : EventArgs
{
    internal string Title { get; } = title;
}

/// <summary>
/// The VT parser and executor.
///
/// The parser follows the DEC ANSI state table rather than an ad-hoc switch. That is less code
/// once private markers, intermediate bytes and sub-parameters are all in play, and it is the
/// difference between handling <c>CSI ? 1049 h</c> and silently dropping it -- the previous parser
/// trimmed the '?' away before dispatch, which is precisely why the alternate screen, application
/// cursor keys and bracketed paste could never work.
/// </summary>
internal sealed class VtTerminalEmulator
{
    private const int MaximumParameters = 32;
    private const int MaximumStringLength = 4096;

    private readonly int[] _parameters = new int[MaximumParameters];
    private readonly StringBuilder _stringBuffer = new();
    private readonly StringBuilder _intermediates = new();
    private State _state = State.Ground;
    private int _parameterCount;
    private bool _parameterPending;
    private bool _privateMarker;
    private int _highSurrogate;

    private VtColor _foreground = VtColor.Default;
    private VtColor _background = VtColor.Default;
    private VtCellFlags _flags = VtCellFlags.None;
    private int _charsetG0 = VtCharsets.Ascii;
    private int _charsetG1 = VtCharsets.Ascii;
    private bool _shiftOut;

    internal VtTerminalEmulator(int columns, int rows, int maximumScrollbackLines = 2_000)
    {
        Document = new VtTerminalDocument(columns, rows, maximumScrollbackLines);
    }

    internal VtTerminalDocument Document { get; }

    internal int Columns => Document.Columns;

    internal int Rows => Document.Rows;

    /// <summary>DECTCEM. vim hides the cursor while redrawing and a stray block looks like a bug.</summary>
    internal bool CursorVisible { get; private set; } = true;

    /// <summary>DECCKM: the arrows must go out as SS3 while this is on.</summary>
    internal bool ApplicationCursorKeys { get; private set; }

    /// <summary>DECAWM, on by default.</summary>
    internal bool AutoWrap { get; private set; } = true;

    internal bool BracketedPaste { get; private set; }

    /// <summary>DECOM: row addressing is relative to the scroll region while this is on.</summary>
    internal bool OriginMode { get; private set; }

    /// <summary>Whether a program has asked for mouse events, and in which flavour.</summary>
    internal bool MouseReporting { get; private set; }

    internal bool MouseButtonTracking { get; private set; }

    internal bool MouseAnyEventTracking { get; private set; }

    internal bool MouseSgrEncoding { get; private set; }

    /// <summary>DECSET 2026: hold painting until the program says the frame is complete.</summary>
    internal bool SynchronizedOutput { get; private set; }

    /// <summary>Raised when the host asked a question the terminal must answer on the wire.</summary>
    internal event EventHandler<VtResponseEventArgs>? ResponseRequested;

    /// <summary>Raised for OSC 0 and OSC 2, which is how a shell reports the running command.</summary>
    internal event EventHandler<VtTitleEventArgs>? TitleChanged;

    internal void Feed(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            Process(character);
        }
    }

    internal void Feed(string value) => Feed(value.AsSpan());

    internal void Resize(int columns, int rows) => Document.Resize(columns, rows);

    // ---------------------------------------------------------------- parser

    private enum State
    {
        Ground,
        Escape,
        EscapeIntermediate,
        CsiEntry,
        CsiParam,
        CsiIntermediate,
        CsiIgnore,
        OscString,
        DcsPassthrough,
        SosPmApcString,
        StringTerminatorPending
    }

    private void Process(char character)
    {
        // A surrogate pair is two chars but one codepoint; combining them here keeps everything
        // below working in codepoints.
        if (char.IsHighSurrogate(character))
        {
            _highSurrogate = character;
            return;
        }

        var codepoint = character;
        if (_highSurrogate != 0)
        {
            if (char.IsLowSurrogate(character))
            {
                Dispatch(char.ConvertToUtf32((char)_highSurrogate, character));
                _highSurrogate = 0;
                return;
            }

            _highSurrogate = 0;
        }

        Dispatch(codepoint);
    }

    private void Dispatch(int codepoint)
    {
        // CAN and SUB abort whatever sequence is in progress, from any state.
        if (codepoint is 0x18 or 0x1A)
        {
            _state = State.Ground;
            return;
        }

        if (codepoint == 0x1B)
        {
            // ESC inside a string body ends it when followed by a backslash.
            if (_state is State.OscString or State.DcsPassthrough or State.SosPmApcString)
            {
                _state = State.StringTerminatorPending;
                return;
            }

            EnterEscape();
            return;
        }

        switch (_state)
        {
            case State.Ground:
                GroundByte(codepoint);
                return;
            case State.Escape:
                EscapeByte(codepoint);
                return;
            case State.EscapeIntermediate:
                EscapeIntermediateByte(codepoint);
                return;
            case State.CsiEntry:
            case State.CsiParam:
            case State.CsiIntermediate:
            case State.CsiIgnore:
                CsiByte(codepoint);
                return;
            case State.OscString:
                OscByte(codepoint);
                return;
            case State.DcsPassthrough:
            case State.SosPmApcString:
                // The payload is swallowed whole; printing it would fill the screen with garbage.
                if (codepoint == 0x07)
                {
                    _state = State.Ground;
                }

                return;
            case State.StringTerminatorPending:
                FinishString(codepoint);
                return;
        }
    }

    private void EnterEscape()
    {
        _state = State.Escape;
        _intermediates.Clear();
        _stringBuffer.Clear();
        ResetParameters();
    }

    private void GroundByte(int codepoint)
    {
        switch (codepoint)
        {
            case 0x07:
                return;
            case 0x08:
                Backspace();
                return;
            case 0x09:
                HorizontalTab();
                return;
            case 0x0A:
            case 0x0B:
            case 0x0C:
                LineFeed();
                return;
            case 0x0D:
                CarriageReturn();
                return;
            case 0x0E:
                _shiftOut = true;
                return;
            case 0x0F:
                _shiftOut = false;
                return;
            default:
                if (codepoint >= 0x20 && codepoint != 0x7F)
                {
                    Print(codepoint);
                }

                return;
        }
    }

    private void EscapeByte(int codepoint)
    {
        if (codepoint is >= 0x20 and <= 0x2F)
        {
            _intermediates.Append((char)codepoint);
            _state = State.EscapeIntermediate;
            return;
        }

        switch (codepoint)
        {
            case '[':
                _state = State.CsiEntry;
                ResetParameters();
                _intermediates.Clear();
                return;
            case ']':
                _state = State.OscString;
                _stringBuffer.Clear();
                return;
            case 'P':
            case 'X':
            case '^':
            case '_':
                _state = codepoint == 'P' ? State.DcsPassthrough : State.SosPmApcString;
                return;
            case '7':
                SaveCursor();
                break;
            case '8':
                RestoreCursor();
                break;
            case 'D':
                LineFeed();
                break;
            case 'E':
                CarriageReturn();
                LineFeed();
                break;
            case 'H':
                Document.Screen.SetTabStop(Document.Screen.CursorColumn);
                break;
            case 'M':
                ReverseIndex();
                break;
            case 'c':
                FullReset();
                break;
            case '=':
            case '>':
                // Keypad application mode: accepted, and the encoder does not vary on it today.
                break;
        }

        _state = State.Ground;
    }

    private void EscapeIntermediateByte(int codepoint)
    {
        var intermediate = _intermediates.Length > 0 ? _intermediates[0] : '\0';

        // ESC ( B and ESC ) 0 designate a character set. The designator byte has to be consumed
        // here; leaving it to the ground state is what used to print a stray "B" on connect.
        if (intermediate is '(' or ')')
        {
            var charset = VtCharsets.IsKnownDesignator(codepoint) ? codepoint : VtCharsets.Ascii;
            if (intermediate == '(')
            {
                _charsetG0 = charset;
            }
            else
            {
                _charsetG1 = charset;
            }
        }

        _state = State.Ground;
    }

    private void CsiByte(int codepoint)
    {
        if (_state == State.CsiIgnore)
        {
            if (codepoint is >= 0x40 and <= 0x7E)
            {
                _state = State.Ground;
            }

            return;
        }

        // Private marker, but only as the first byte of the sequence.
        if (codepoint is '?' or '<' or '=' or '>' && _state == State.CsiEntry && _parameterCount == 0 && !_parameterPending)
        {
            _privateMarker = codepoint == '?';
            _state = State.CsiParam;
            return;
        }

        if (codepoint is >= '0' and <= '9')
        {
            _state = State.CsiParam;
            if (_parameterCount < MaximumParameters)
            {
                if (!_parameterPending)
                {
                    _parameters[_parameterCount] = 0;
                    _parameterPending = true;
                }

                _parameters[_parameterCount] = Math.Min(65_535, (_parameters[_parameterCount] * 10) + (codepoint - '0'));
            }

            return;
        }

        // ':' separates sub-parameters, as in 38:2::r:g:b. Treated as a separator so a truecolour
        // sequence in the colon form still yields its components rather than one huge number.
        if (codepoint is ';' or ':')
        {
            _state = State.CsiParam;
            if (_parameterCount < MaximumParameters)
            {
                if (!_parameterPending)
                {
                    _parameters[_parameterCount] = 0;
                }

                _parameterCount++;
                _parameterPending = false;
            }

            return;
        }

        if (codepoint is >= 0x20 and <= 0x2F)
        {
            _intermediates.Append((char)codepoint);
            _state = State.CsiIntermediate;
            return;
        }

        if (codepoint is >= 0x40 and <= 0x7E)
        {
            if (_parameterPending && _parameterCount < MaximumParameters)
            {
                _parameterCount++;
                _parameterPending = false;
            }

            ExecuteCsi((char)codepoint);
            _state = State.Ground;
            return;
        }

        _state = State.CsiIgnore;
    }

    private void OscByte(int codepoint)
    {
        if (codepoint == 0x07)
        {
            FinishOsc();
            _state = State.Ground;
            return;
        }

        if (_stringBuffer.Length < MaximumStringLength)
        {
            _stringBuffer.Append(char.ConvertFromUtf32(codepoint));
        }
    }

    private void FinishString(int codepoint)
    {
        if (codepoint == '\\')
        {
            FinishOsc();
            _state = State.Ground;
            return;
        }

        // ESC followed by anything else was not a terminator, so start a fresh escape sequence.
        _stringBuffer.Clear();
        EnterEscape();
        EscapeByte(codepoint);
    }

    private void FinishOsc()
    {
        var text = _stringBuffer.ToString();
        _stringBuffer.Clear();
        var separator = text.IndexOf(';', StringComparison.Ordinal);
        if (separator < 0
            || !int.TryParse(text.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var command))
        {
            return;
        }

        var payload = text[(separator + 1)..];
        switch (command)
        {
            case 0:
            case 2:
                TitleChanged?.Invoke(this, new VtTitleEventArgs(payload));
                return;

            // OSC 52 lets the remote host write the local clipboard. Swallowed deliberately: it is
            // an exfiltration and injection route, and nothing here needs it.
            case 52:
                return;
        }
    }

    private void ResetParameters()
    {
        Array.Clear(_parameters);
        _parameterCount = 0;
        _parameterPending = false;
        _privateMarker = false;
    }

    private int Parameter(int index, int fallback = 0) =>
        index < _parameterCount && _parameters[index] != 0 ? _parameters[index] : fallback;

    private int RawParameter(int index) => index < _parameterCount ? _parameters[index] : 0;

    // ---------------------------------------------------------------- executor

    private void ExecuteCsi(char command)
    {
        var screen = Document.Screen;
        if (command != 'm')
        {
            // Only a style change leaves the cursor alone; everything else moves or rewrites it,
            // which cancels a wrap that Print left pending.
            screen.PendingWrap = false;
        }

        var amount = Math.Max(1, Parameter(0, 1));
        switch (command)
        {
            case 'A': MoveCursor(-amount, 0); break;
            case 'B': MoveCursor(amount, 0); break;
            case 'C': MoveCursor(0, amount); break;
            case 'D': MoveCursor(0, -amount); break;
            case 'E': SetCursor(screen.CursorRow + amount, 0); break;
            case 'F': SetCursor(screen.CursorRow - amount, 0); break;
            case 'G':
            case '`': SetCursor(screen.CursorRow, amount - 1); break;
            case 'd': SetCursorRowAbsolute(amount - 1); break;
            case 'H':
            case 'f': SetCursorRowAbsolute(Math.Max(1, Parameter(0, 1)) - 1, Math.Max(1, Parameter(1, 1)) - 1); break;
            case 'I': TabForward(amount); break;
            case 'Z': TabBackward(amount); break;
            case 'J': EraseDisplay(RawParameter(0)); break;
            case 'K': EraseLine(RawParameter(0)); break;
            case 'X': EraseCharacters(amount); break;
            case '@': InsertCharacters(amount); break;
            case 'P': DeleteCharacters(amount); break;
            case 'L': InsertLines(amount); break;
            case 'M': DeleteLines(amount); break;
            case 'S': Document.ScrollUp(amount); break;
            case 'T': Document.ScrollDown(amount); break;
            case 'm': ApplyGraphics(); break;
            case 'r': SetScrollRegion(); break;
            case 's': SaveCursor(); break;
            case 'u': RestoreCursor(); break;
            case 'g': ClearTabStop(RawParameter(0)); break;
            case 'h': SetModes(enable: true); break;
            case 'l': SetModes(enable: false); break;
            case 'n': DeviceStatusReport(RawParameter(0)); break;
            case 'c': DeviceAttributes(); break;
            case 'p': SoftReset(); break;
        }
    }

    private void SetModes(bool enable)
    {
        for (var index = 0; index < _parameterCount; index++)
        {
            var mode = _parameters[index];
            if (_privateMarker)
            {
                SetPrivateMode(mode, enable);
            }
            else if (mode == 4)
            {
                // IRM, insert/replace. Not modelled; accepted so it is not treated as unknown.
            }
        }
    }

    private void SetPrivateMode(int mode, bool enable)
    {
        switch (mode)
        {
            case 1:
                ApplicationCursorKeys = enable;
                break;
            case 6:
                OriginMode = enable;
                SetCursorRowAbsolute(0, 0);
                break;
            case 7:
                AutoWrap = enable;
                break;
            case 25:
                CursorVisible = enable;
                Document.MarkFullRepaint();
                break;
            case 1000:
                MouseReporting = enable;
                break;
            case 1002:
                MouseReporting = enable;
                MouseButtonTracking = enable;
                break;
            case 1003:
                MouseReporting = enable;
                MouseAnyEventTracking = enable;
                break;
            case 1006:
                MouseSgrEncoding = enable;
                break;
            case 2004:
                BracketedPaste = enable;
                break;
            case 2026:
                SynchronizedOutput = enable;
                break;

            case 47:
            case 1047:
                if (enable)
                {
                    Document.EnterAlternateScreen(clear: true);
                }
                else
                {
                    Document.LeaveAlternateScreen();
                }

                break;

            case 1048:
                if (enable)
                {
                    SaveCursor();
                }
                else
                {
                    RestoreCursor();
                }

                break;

            case 1049:
                // The combined form: save the cursor and style, switch, clear. Restoring on the
                // way out is what puts the shell back exactly as the program found it.
                if (enable)
                {
                    SaveCursor();
                    Document.EnterAlternateScreen(clear: true);
                }
                else
                {
                    Document.LeaveAlternateScreen();
                    RestoreCursor();
                }

                break;
        }
    }

    private void DeviceStatusReport(int request)
    {
        switch (request)
        {
            case 5:
                Respond("[0n");
                break;
            case 6:
                // Several TUIs block waiting for this reply, so not answering is a hang rather
                // than a cosmetic gap.
                var screen = Document.Screen;
                var row = OriginMode ? screen.CursorRow - screen.ScrollTop : screen.CursorRow;
                Respond($"[{row + 1};{screen.CursorColumn + 1}R");
                break;
        }
    }

    private void DeviceAttributes()
    {
        // Conservative on purpose: a VT220 that can do colour. Claiming more invites a program to
        // use sequences this terminal does not implement.
        Respond("[?62;1;6;22c");
    }

    private void SoftReset()
    {
        if (_intermediates.Length == 0 || _intermediates[0] != '!')
        {
            return;
        }

        var screen = Document.Screen;
        screen.ResetScrollRegion();
        screen.ResetTabStops();
        screen.SavedCursor = null;
        _foreground = VtColor.Default;
        _background = VtColor.Default;
        _flags = VtCellFlags.None;
        OriginMode = false;
        AutoWrap = true;
        CursorVisible = true;
        Document.MarkFullRepaint();
    }

    private void Respond(string text) =>
        ResponseRequested?.Invoke(this, new VtResponseEventArgs(Encoding.ASCII.GetBytes(text)));

    private void FullReset()
    {
        _foreground = VtColor.Default;
        _background = VtColor.Default;
        _flags = VtCellFlags.None;
        _charsetG0 = VtCharsets.Ascii;
        _charsetG1 = VtCharsets.Ascii;
        _shiftOut = false;
        CursorVisible = true;
        ApplicationCursorKeys = false;
        AutoWrap = true;
        BracketedPaste = false;
        OriginMode = false;
        MouseReporting = false;
        MouseButtonTracking = false;
        MouseAnyEventTracking = false;
        MouseSgrEncoding = false;
        SynchronizedOutput = false;
        Document.Reset();
    }

    // ---------------------------------------------------------------- printing

    private void Print(int codepoint)
    {
        var screen = Document.Screen;
        if (screen.PendingWrap)
        {
            screen.PendingWrap = false;
            if (AutoWrap)
            {
                screen.CurrentLine.WrappedToNext = true;
                screen.CursorColumn = 0;
                LineFeed();
            }
        }

        var mapped = VtCharsets.Map(codepoint, _shiftOut ? _charsetG1 : _charsetG0);
        var line = screen.CurrentLine;
        line[screen.CursorColumn] = new VtCell(mapped, _foreground, _background, _flags);
        Document.Touch(Document.CursorLineNumber);

        if (screen.CursorColumn + 1 >= Document.Columns)
        {
            if (AutoWrap)
            {
                // A real terminal parks on the last column and wraps only when another printable
                // character arrives, so a line that exactly fills the width still redraws in place.
                screen.PendingWrap = true;
            }

            return;
        }

        screen.CursorColumn++;
    }

    private void CarriageReturn()
    {
        var screen = Document.Screen;
        screen.PendingWrap = false;
        screen.CursorColumn = 0;
    }

    private void LineFeed()
    {
        var screen = Document.Screen;
        screen.PendingWrap = false;
        if (screen.CursorRow < screen.ScrollBottom)
        {
            screen.CursorRow++;
            return;
        }

        if (screen.CursorRow > screen.ScrollBottom)
        {
            screen.CursorRow = Math.Min(Document.Rows - 1, screen.CursorRow + 1);
            return;
        }

        Document.ScrollUp(1);
    }

    /// <summary>ESC M. nano and less scroll backwards with this; without it they redraw wrongly.</summary>
    private void ReverseIndex()
    {
        var screen = Document.Screen;
        screen.PendingWrap = false;
        if (screen.CursorRow > screen.ScrollTop)
        {
            screen.CursorRow--;
            return;
        }

        Document.ScrollDown(1);
    }

    private void Backspace()
    {
        var screen = Document.Screen;
        screen.PendingWrap = false;
        screen.CursorColumn = Math.Max(0, screen.CursorColumn - 1);
    }

    private void HorizontalTab() => TabForward(1);

    private void TabForward(int amount)
    {
        var screen = Document.Screen;
        screen.PendingWrap = false;
        for (var step = 0; step < amount; step++)
        {
            screen.CursorColumn = screen.NextTabStop(screen.CursorColumn);
        }
    }

    private void TabBackward(int amount)
    {
        var screen = Document.Screen;
        for (var step = 0; step < amount; step++)
        {
            screen.CursorColumn = screen.PreviousTabStop(screen.CursorColumn);
        }
    }

    private void ClearTabStop(int request)
    {
        var screen = Document.Screen;
        if (request == 3)
        {
            screen.ClearAllTabStops();
            return;
        }

        screen.ClearTabStop(screen.CursorColumn);
    }

    // ---------------------------------------------------------------- cursor

    private void MoveCursor(int rows, int columns)
    {
        var screen = Document.Screen;
        SetCursor(screen.CursorRow + rows, screen.CursorColumn + columns);
    }

    private void SetCursor(int row, int column)
    {
        var screen = Document.Screen;
        screen.CursorRow = Math.Clamp(row, 0, Document.Rows - 1);
        screen.CursorColumn = Math.Clamp(column, 0, Document.Columns - 1);
        Document.Touch(Document.CursorLineNumber);
    }

    private void SetCursorRowAbsolute(int row) => SetCursorRowAbsolute(row, Document.Screen.CursorColumn);

    private void SetCursorRowAbsolute(int row, int column)
    {
        var screen = Document.Screen;

        // Under DECOM a row number counts from the top of the scroll region, not the screen.
        var offset = OriginMode ? screen.ScrollTop : 0;
        var limit = OriginMode ? screen.ScrollBottom : Document.Rows - 1;
        screen.CursorRow = Math.Clamp(row + offset, 0, limit);
        screen.CursorColumn = Math.Clamp(column, 0, Document.Columns - 1);
        Document.Touch(Document.CursorLineNumber);
    }

    private void SaveCursor()
    {
        var screen = Document.Screen;
        screen.SavedCursor = new VtSavedCursor(
            screen.CursorRow, screen.CursorColumn, _foreground, _background, _flags,
            _charsetG0, _charsetG1, OriginMode);
    }

    private void RestoreCursor()
    {
        var screen = Document.Screen;
        if (screen.SavedCursor is not { } saved)
        {
            SetCursor(0, 0);
            return;
        }

        _foreground = saved.Foreground;
        _background = saved.Background;
        _flags = saved.Flags;
        _charsetG0 = saved.CharsetG0;
        _charsetG1 = saved.CharsetG1;
        OriginMode = saved.OriginMode;
        screen.PendingWrap = false;
        SetCursor(saved.Row, saved.Column);
    }

    private void SetScrollRegion()
    {
        var screen = Document.Screen;
        var top = Math.Max(1, Parameter(0, 1)) - 1;
        var bottom = Math.Max(1, Parameter(1, Document.Rows)) - 1;
        if (top >= bottom || bottom >= Document.Rows)
        {
            screen.ResetScrollRegion();
        }
        else
        {
            screen.ScrollTop = top;
            screen.ScrollBottom = bottom;
        }

        SetCursorRowAbsolute(0, 0);
    }

    // ---------------------------------------------------------------- erasing

    /// <summary>
    /// The cell an erase leaves behind: the current background, and nothing else. Inheriting the
    /// foreground and the attributes -- which is what the old buffer did -- is why erased regions
    /// came back bold and coloured, and background-colour-erase is what htop's bars rely on.
    /// </summary>
    private VtCell ErasedCell() => new(' ', VtColor.Default, _background, VtCellFlags.None);

    private void EraseDisplay(int mode)
    {
        var screen = Document.Screen;
        var erased = ErasedCell();
        switch (mode)
        {
            case 0:
                EraseInLine(screen.CurrentLine, screen.CursorColumn, Document.Columns - 1, erased);
                for (var row = screen.CursorRow + 1; row < screen.Lines.Count; row++)
                {
                    ClearLine(screen.Lines[row], erased);
                }

                break;
            case 1:
                EraseInLine(screen.CurrentLine, 0, screen.CursorColumn, erased);
                for (var row = 0; row < screen.CursorRow; row++)
                {
                    ClearLine(screen.Lines[row], erased);
                }

                break;
            default:
                foreach (var line in screen.Lines)
                {
                    ClearLine(line, erased);
                }

                break;
        }

        Document.MarkFullRepaint();
    }

    private void EraseLine(int mode)
    {
        var screen = Document.Screen;
        var erased = ErasedCell();
        var line = screen.CurrentLine;
        switch (mode)
        {
            case 0: EraseInLine(line, screen.CursorColumn, Document.Columns - 1, erased); break;
            case 1: EraseInLine(line, 0, screen.CursorColumn, erased); break;
            default: ClearLine(line, erased); break;
        }

        Document.Touch(Document.CursorLineNumber);
    }

    private static void ClearLine(VtLine line, VtCell erased)
    {
        line.Fill(erased);
        line.WrappedToNext = false;
    }

    private static void EraseInLine(VtLine line, int from, int to, VtCell erased)
    {
        for (var column = Math.Max(0, from); column <= Math.Min(line.Length - 1, to); column++)
        {
            line[column] = erased;
        }
    }

    private void EraseCharacters(int amount)
    {
        var screen = Document.Screen;
        EraseInLine(screen.CurrentLine, screen.CursorColumn, screen.CursorColumn + amount - 1, ErasedCell());
        Document.Touch(Document.CursorLineNumber);
    }

    private void InsertCharacters(int amount)
    {
        var screen = Document.Screen;
        var line = screen.CurrentLine;
        var erased = ErasedCell();
        for (var column = Document.Columns - 1; column >= screen.CursorColumn; column--)
        {
            var source = column - amount;
            line[column] = source >= screen.CursorColumn ? line[source] : erased;
        }

        Document.Touch(Document.CursorLineNumber);
    }

    private void DeleteCharacters(int amount)
    {
        var screen = Document.Screen;
        var line = screen.CurrentLine;
        var erased = ErasedCell();
        for (var column = screen.CursorColumn; column < Document.Columns; column++)
        {
            var source = column + amount;
            line[column] = source < Document.Columns ? line[source] : erased;
        }

        Document.Touch(Document.CursorLineNumber);
    }

    private void InsertLines(int amount)
    {
        var screen = Document.Screen;
        if (screen.CursorRow < screen.ScrollTop || screen.CursorRow > screen.ScrollBottom)
        {
            return;
        }

        amount = Math.Clamp(amount, 1, screen.ScrollBottom - screen.CursorRow + 1);
        for (var step = 0; step < amount; step++)
        {
            screen.Lines.RemoveAt(screen.ScrollBottom);
            screen.Lines.Insert(screen.CursorRow, new VtLine(Document.Columns));
        }

        Document.MarkFullRepaint();
    }

    private void DeleteLines(int amount)
    {
        var screen = Document.Screen;
        if (screen.CursorRow < screen.ScrollTop || screen.CursorRow > screen.ScrollBottom)
        {
            return;
        }

        amount = Math.Clamp(amount, 1, screen.ScrollBottom - screen.CursorRow + 1);
        for (var step = 0; step < amount; step++)
        {
            screen.Lines.RemoveAt(screen.CursorRow);
            screen.Lines.Insert(screen.ScrollBottom, new VtLine(Document.Columns));
        }

        Document.MarkFullRepaint();
    }

    // ---------------------------------------------------------------- SGR

    private void ApplyGraphics()
    {
        if (_parameterCount == 0)
        {
            _foreground = VtColor.Default;
            _background = VtColor.Default;
            _flags = VtCellFlags.None;
            return;
        }

        for (var index = 0; index < _parameterCount; index++)
        {
            var code = _parameters[index];
            switch (code)
            {
                case 0:
                    _foreground = VtColor.Default;
                    _background = VtColor.Default;
                    _flags = VtCellFlags.None;
                    break;
                case 1: _flags |= VtCellFlags.Bold; break;
                case 2: _flags |= VtCellFlags.Dim; break;
                case 3: _flags |= VtCellFlags.Italic; break;
                case 4: _flags |= VtCellFlags.Underline; break;
                case 5:
                case 6: _flags |= VtCellFlags.Blink; break;

                // Inverse is the one that matters most: htop's header and vim's status line are
                // drawn with it, and without it they are invisible.
                case 7: _flags |= VtCellFlags.Inverse; break;
                case 8: _flags |= VtCellFlags.Hidden; break;
                case 9: _flags |= VtCellFlags.Strike; break;
                case 21:
                case 22: _flags &= ~(VtCellFlags.Bold | VtCellFlags.Dim); break;
                case 23: _flags &= ~VtCellFlags.Italic; break;
                case 24: _flags &= ~VtCellFlags.Underline; break;
                case 25: _flags &= ~VtCellFlags.Blink; break;
                case 27: _flags &= ~VtCellFlags.Inverse; break;
                case 28: _flags &= ~VtCellFlags.Hidden; break;
                case 29: _flags &= ~VtCellFlags.Strike; break;
                case >= 30 and <= 37: _foreground = VtColor.FromIndex(code - 30); break;
                case 38: index = ApplyExtendedColor(index, foreground: true); break;
                case 39: _foreground = VtColor.Default; break;
                case >= 40 and <= 47: _background = VtColor.FromIndex(code - 40); break;
                case 48: index = ApplyExtendedColor(index, foreground: false); break;
                case 49: _background = VtColor.Default; break;
                case >= 90 and <= 97: _foreground = VtColor.FromIndex(code - 90 + 8); break;
                case >= 100 and <= 107: _background = VtColor.FromIndex(code - 100 + 8); break;
            }
        }
    }

    private int ApplyExtendedColor(int index, bool foreground)
    {
        var selector = index + 1 < _parameterCount ? _parameters[index + 1] : 0;
        if (selector == 5 && index + 2 < _parameterCount)
        {
            var color = VtColor.FromIndex(_parameters[index + 2]);
            if (foreground)
            {
                _foreground = color;
            }
            else
            {
                _background = color;
            }

            return index + 2;
        }

        if (selector == 2 && index + 4 < _parameterCount)
        {
            var color = VtColor.FromRgb(_parameters[index + 2], _parameters[index + 3], _parameters[index + 4]);
            if (foreground)
            {
                _foreground = color;
            }
            else
            {
                _background = color;
            }

            return index + 4;
        }

        return index;
    }
}
