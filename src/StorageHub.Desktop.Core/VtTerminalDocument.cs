using System.Text;

namespace StorageHub.Desktop;

/// <summary>
/// What changed since the view last painted. The row range is in absolute line numbers so it
/// stays meaningful even when scrollback trimmed underneath it.
/// </summary>
internal readonly record struct VtDamage(bool FullRepaint, long FirstLine, long LastLine, bool HasRows)
{
    internal static readonly VtDamage None = new(false, 0, 0, false);

    internal bool IsEmpty => !FullRepaint && !HasRows;
}

/// <summary>
/// The whole terminal's storage: the primary screen with its scrollback, the alternate screen, and
/// the numbering that ties them to what the view is showing.
///
/// Every line carries an absolute number that never shifts. Scrollback is a ring, so trimming it
/// moves <see cref="FirstLineNumber"/> forward rather than renumbering anything, which is what
/// keeps a selection and a scroll position still pointing at the same text while output floods in
/// underneath them.
/// </summary>
internal sealed class VtTerminalDocument
{
    private readonly int _maximumScrollbackLines;
    private readonly List<VtLine> _scrollback = [];
    private VtScreen _primary;
    private VtScreen _alternate;
    private bool _damaged;
    private bool _fullRepaint;
    private long _damageFirst;
    private long _damageLast;

    internal VtTerminalDocument(int columns, int rows, int maximumScrollbackLines)
    {
        Columns = Math.Max(1, columns);
        Rows = Math.Max(1, rows);
        _maximumScrollbackLines = Math.Clamp(maximumScrollbackLines, 100, 20_000);
        _primary = new VtScreen(Columns, Rows);
        _alternate = new VtScreen(Columns, Rows);
        MarkFullRepaint();
    }

    internal int Columns { get; private set; }

    internal int Rows { get; private set; }

    /// <summary>True while a full-screen program has switched to the alternate screen.</summary>
    internal bool UsingAlternateScreen { get; private set; }

    internal VtScreen Screen => UsingAlternateScreen ? _alternate : _primary;

    /// <summary>The absolute number of the oldest line still held; it only ever grows.</summary>
    internal long FirstLineNumber { get; private set; }

    internal int ScrollbackCount => UsingAlternateScreen ? 0 : _scrollback.Count;

    internal long TotalLineCount => ScrollbackCount + Rows;

    internal long LastLineNumber => FirstLineNumber + TotalLineCount - 1;

    /// <summary>The absolute number of the screen's top row.</summary>
    internal long ScreenTopLineNumber => FirstLineNumber + ScrollbackCount;

    internal long CursorLineNumber => ScreenTopLineNumber + Screen.CursorRow;

    /// <summary>Bumped on every change, so a view can tell at a glance whether anything happened.</summary>
    internal long Revision { get; private set; }

    /// <summary>
    /// The line with the given absolute number, or an empty span when it has been trimmed away or
    /// does not exist yet. Returns the storage itself: nothing is allocated to read a row.
    /// </summary>
    internal ReadOnlySpan<VtCell> GetLine(long lineNumber)
    {
        var line = FindLine(lineNumber);
        return line is null ? ReadOnlySpan<VtCell>.Empty : line.ReadOnlyCells;
    }

    internal VtLine? FindLine(long lineNumber)
    {
        var offset = lineNumber - FirstLineNumber;
        if (offset < 0 || offset >= TotalLineCount)
        {
            return null;
        }

        var scrollback = ScrollbackCount;
        return offset < scrollback
            ? _scrollback[(int)offset]
            : Screen.Lines[(int)(offset - scrollback)];
    }

    internal void Touch(long lineNumber)
    {
        Revision++;
        if (!_damaged)
        {
            _damaged = true;
            _damageFirst = lineNumber;
            _damageLast = lineNumber;
            return;
        }

        _damageFirst = Math.Min(_damageFirst, lineNumber);
        _damageLast = Math.Max(_damageLast, lineNumber);
    }

    internal void TouchScreen()
    {
        Touch(ScreenTopLineNumber);
        Touch(ScreenTopLineNumber + Rows - 1);
    }

    internal void MarkFullRepaint()
    {
        Revision++;
        _fullRepaint = true;
        _damaged = true;
    }

    internal VtDamage TakeDamage()
    {
        var damage = _fullRepaint
            ? new VtDamage(true, FirstLineNumber, LastLineNumber, true)
            : _damaged
                ? new VtDamage(false, _damageFirst, _damageLast, true)
                : VtDamage.None;
        _damaged = false;
        _fullRepaint = false;
        return damage;
    }

    // ---------------------------------------------------------------- scrolling

    /// <summary>
    /// Scrolls the region up, pushing the displaced top line into scrollback when the region is
    /// the whole primary screen. The alternate screen never contributes: that is exactly what
    /// makes "quit vim and the shell's scrollback is still there" work.
    /// </summary>
    internal void ScrollUp(int amount)
    {
        var screen = Screen;
        amount = Math.Clamp(amount, 1, Math.Max(1, screen.ScrollBottom - screen.ScrollTop + 1));
        var wholeScreen = screen.ScrollTop == 0 && screen.ScrollBottom == Rows - 1;
        for (var step = 0; step < amount; step++)
        {
            var displaced = screen.Lines[screen.ScrollTop];
            screen.Lines.RemoveAt(screen.ScrollTop);
            screen.Lines.Insert(screen.ScrollBottom, new VtLine(Columns));
            if (wholeScreen && !UsingAlternateScreen)
            {
                _scrollback.Add(displaced);
            }
        }

        TrimScrollback();
        MarkFullRepaint();
    }

    internal void ScrollDown(int amount)
    {
        var screen = Screen;
        amount = Math.Clamp(amount, 1, Math.Max(1, screen.ScrollBottom - screen.ScrollTop + 1));
        for (var step = 0; step < amount; step++)
        {
            screen.Lines.RemoveAt(screen.ScrollBottom);
            screen.Lines.Insert(screen.ScrollTop, new VtLine(Columns));
        }

        MarkFullRepaint();
    }

    private void TrimScrollback()
    {
        if (_scrollback.Count <= _maximumScrollbackLines)
        {
            return;
        }

        var excess = _scrollback.Count - _maximumScrollbackLines;
        _scrollback.RemoveRange(0, excess);

        // Numbering moves forward instead of everything being renumbered, so anything holding a
        // line number keeps pointing at the same text.
        FirstLineNumber += excess;
    }

    // ------------------------------------------------------------ alternate screen

    internal void EnterAlternateScreen(bool clear)
    {
        if (UsingAlternateScreen)
        {
            return;
        }

        UsingAlternateScreen = true;
        _alternate.SetSize(Columns, Rows);
        SetScreenRowCount(_alternate, Rows);
        if (clear)
        {
            foreach (var line in _alternate.Lines)
            {
                line.Fill(VtCell.Blank);
                line.WrappedToNext = false;
            }

            _alternate.CursorRow = 0;
            _alternate.CursorColumn = 0;
            _alternate.PendingWrap = false;
        }

        _alternate.ResetScrollRegion();
        MarkFullRepaint();
    }

    internal void LeaveAlternateScreen()
    {
        if (!UsingAlternateScreen)
        {
            return;
        }

        UsingAlternateScreen = false;
        MarkFullRepaint();
    }

    // ---------------------------------------------------------------- resize

    internal void Resize(int columns, int rows)
    {
        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);
        if (columns == Columns && rows == Rows)
        {
            return;
        }

        var reflowColumns = columns != Columns;
        Columns = columns;
        Rows = rows;

        // The primary screen is reflowed even while the alternate one is showing, so exiting vim
        // after a resize still hands back correctly wrapped scrollback.
        if (reflowColumns)
        {
            ReflowPrimary(columns, rows);
        }

        ResizePrimaryRows(rows);

        // xterm clamps and clears the alternate screen rather than reflowing it; a full-screen
        // program redraws itself on SIGWINCH anyway, and reflowing would fight that redraw.
        _alternate.SetSize(columns, rows);
        foreach (var line in _alternate.Lines)
        {
            line.SetWidth(columns, VtCell.Blank);
        }

        SetScreenRowCount(_alternate, rows);
        _alternate.CursorRow = Math.Clamp(_alternate.CursorRow, 0, rows - 1);
        _alternate.CursorColumn = Math.Clamp(_alternate.CursorColumn, 0, columns - 1);
        _alternate.ResetScrollRegion();

        _primary.SetSize(columns, rows);
        _primary.ResetScrollRegion();
        _primary.CursorRow = Math.Clamp(_primary.CursorRow, 0, rows - 1);
        _primary.CursorColumn = Math.Clamp(_primary.CursorColumn, 0, columns - 1);
        _primary.PendingWrap = false;
        MarkFullRepaint();
    }

    /// <summary>
    /// Joins every run of lines the terminal wrapped into the logical line the program actually
    /// wrote, re-wraps those at the new width, and puts the cursor back on the same character.
    /// </summary>
    private void ReflowPrimary(int columns, int rows)
    {
        var all = new List<VtLine>(_scrollback.Count + _primary.Lines.Count);
        all.AddRange(_scrollback);
        all.AddRange(_primary.Lines);

        var cursorIndex = _scrollback.Count + Math.Clamp(_primary.CursorRow, 0, _primary.Lines.Count - 1);
        var cursorColumn = Math.Clamp(_primary.CursorColumn, 0, Math.Max(0, _primary.Columns - 1));

        // Record the cursor against the logical line and offset, because the physical row it is on
        // is exactly what re-wrapping is about to change.
        var logicalLines = new List<List<VtCell>>();
        var logicalCursorLine = 0;
        var logicalCursorOffset = 0;
        List<VtCell>? current = null;
        for (var index = 0; index < all.Count; index++)
        {
            var line = all[index];
            if (current is null)
            {
                current = [];
                logicalLines.Add(current);
            }

            var offsetInLogical = current.Count;
            var keep = line.WrappedToNext ? line.Length : line.TrimmedLength();
            for (var column = 0; column < keep; column++)
            {
                current.Add(line[column]);
            }

            if (index == cursorIndex)
            {
                logicalCursorLine = logicalLines.Count - 1;
                logicalCursorOffset = offsetInLogical + cursorColumn;
            }

            if (!line.WrappedToNext)
            {
                current = null;
            }
        }

        var rewrapped = new List<VtLine>(all.Count);
        var newCursorRow = 0;
        var newCursorColumn = 0;
        for (var logical = 0; logical < logicalLines.Count; logical++)
        {
            var cells = logicalLines[logical];
            var produced = 0;
            var offset = 0;
            do
            {
                var take = Math.Min(columns, cells.Count - offset);
                var buffer = new VtCell[columns];
                Array.Fill(buffer, VtCell.Blank);
                for (var index = 0; index < take; index++)
                {
                    buffer[index] = cells[offset + index];
                }

                var line = VtLine.FromCells(buffer);
                line.WrappedToNext = offset + take < cells.Count;
                rewrapped.Add(line);

                if (logical == logicalCursorLine
                    && logicalCursorOffset >= offset
                    && (logicalCursorOffset < offset + columns || !line.WrappedToNext))
                {
                    newCursorRow = rewrapped.Count - 1;
                    newCursorColumn = Math.Clamp(logicalCursorOffset - offset, 0, columns - 1);
                }

                offset += Math.Max(1, take);
                produced++;
            }
            while (offset < cells.Count && produced < 10_000);
        }

        // Blank rows below the cursor are padding rather than content, and counting them here is
        // what used to scroll the first line of a session away: a terminal opens at a default grid
        // and is resized to its pane's real one immediately, so a nearly empty screen keeping "the
        // last `rows` lines" keeps mostly blanks and pushes the opening line into the scrollback.
        // ResizePrimaryRows pads the screen back out to its height straight afterwards.
        var lastContent = rewrapped.Count - 1;
        while (lastContent > newCursorRow && rewrapped[lastContent].TrimmedLength() == 0)
        {
            lastContent--;
        }

        rewrapped.RemoveRange(lastContent + 1, rewrapped.Count - lastContent - 1);

        // The screen keeps the last `rows` lines; everything above becomes scrollback.
        var screenStart = Math.Max(0, rewrapped.Count - rows);

        // Do not let the cursor fall off the top of the screen: if it landed in what would become
        // scrollback, keep enough lines on screen to still show it.
        if (newCursorRow < screenStart)
        {
            screenStart = Math.Max(0, newCursorRow);
        }

        _scrollback.Clear();
        for (var index = 0; index < screenStart; index++)
        {
            _scrollback.Add(rewrapped[index]);
        }

        _primary.Lines.Clear();
        for (var index = screenStart; index < rewrapped.Count; index++)
        {
            _primary.Lines.Add(rewrapped[index]);
        }

        _primary.CursorRow = Math.Max(0, newCursorRow - screenStart);
        _primary.CursorColumn = newCursorColumn;
        TrimScrollback();
    }

    private void ResizePrimaryRows(int rows)
    {
        foreach (var line in _primary.Lines)
        {
            line.SetWidth(Columns, VtCell.Blank);
        }

        while (_primary.Lines.Count > rows)
        {
            // Empty rows below the cursor go first. A session opens at a default grid and is
            // resized to the pane's real one the moment it has been laid out, so almost every
            // terminal shrinks by a row or two while its screen is nearly empty -- and trimming
            // from the top there scrolls the first line of the session away before anybody has
            // read it. Only once the content itself does not fit does anything leave the top.
            var last = _primary.Lines.Count - 1;
            if (last > _primary.CursorRow && _primary.Lines[last].TrimmedLength() == 0)
            {
                _primary.Lines.RemoveAt(last);
                continue;
            }

            // Trim from the top and keep the text: the lines pushed off the screen are exactly
            // what scrollback is for, and dropping them is how a resize used to lose output.
            _scrollback.Add(_primary.Lines[0]);
            _primary.Lines.RemoveAt(0);
            _primary.CursorRow--;
        }

        while (_primary.Lines.Count < rows)
        {
            // Pull lines back out of scrollback before inventing blank ones, so widening the
            // window shows the text that was there rather than empty space.
            if (_scrollback.Count > 0)
            {
                _primary.Lines.Insert(0, _scrollback[^1]);
                _scrollback.RemoveAt(_scrollback.Count - 1);
                _primary.CursorRow++;
            }
            else
            {
                _primary.Lines.Add(new VtLine(Columns));
            }
        }

        _primary.CursorRow = Math.Clamp(_primary.CursorRow, 0, rows - 1);
        TrimScrollback();
    }

    private void SetScreenRowCount(VtScreen screen, int rows)
    {
        while (screen.Lines.Count > rows)
        {
            screen.Lines.RemoveAt(screen.Lines.Count - 1);
        }

        while (screen.Lines.Count < rows)
        {
            screen.Lines.Add(new VtLine(Columns));
        }
    }

    // ---------------------------------------------------------------- reset and text

    internal void Reset()
    {
        _scrollback.Clear();
        FirstLineNumber = 0;
        UsingAlternateScreen = false;
        _primary = new VtScreen(Columns, Rows);
        _alternate = new VtScreen(Columns, Rows);
        MarkFullRepaint();
    }

    /// <summary>
    /// The text of a run of lines. Wrapped lines are joined without a newline, because the break
    /// between them belongs to the window width and not to what the program wrote.
    /// </summary>
    internal string GetText(long fromLineNumber, long count, bool joinWrapped)
    {
        var builder = new StringBuilder();
        var last = Math.Min(LastLineNumber, fromLineNumber + count - 1);
        for (var number = Math.Max(FirstLineNumber, fromLineNumber); number <= last; number++)
        {
            var line = FindLine(number);
            if (line is null)
            {
                continue;
            }

            // A line that wrapped has no trailing padding to trim: every cell up to its width is
            // content, including a space sitting in the last column. Trimming it would silently
            // delete that space when the two halves are joined, turning "could be read" into
            // "could beread" -- the same rule the reflow above already follows.
            line.AppendTextTo(builder, 0, line.WrappedToNext ? line.Length : line.TrimmedLength());
            if (number < last && !(joinWrapped && line.WrappedToNext))
            {
                builder.Append(Environment.NewLine);
            }
        }

        return builder.ToString();
    }
}
