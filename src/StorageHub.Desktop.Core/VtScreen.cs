namespace StorageHub.Desktop;

/// <summary>The cursor state DECSC saves and DECRC restores: position, style and charset.</summary>
internal readonly record struct VtSavedCursor(
    int Row,
    int Column,
    VtColor Foreground,
    VtColor Background,
    VtCellFlags Flags,
    int CharsetG0,
    int CharsetG1,
    bool OriginMode);

/// <summary>
/// One screen's worth of rows plus everything that belongs to it rather than to the terminal: the
/// cursor, the scroll region and the tab stops. There are two of these -- the primary screen and
/// the alternate screen a full-screen program switches to -- and keeping them as separate
/// instances is what lets quitting vim put the shell's scrollback back untouched.
/// </summary>
internal sealed class VtScreen
{
    private bool[] _tabStops;

    internal VtScreen(int columns, int rows)
    {
        Columns = Math.Max(1, columns);
        Rows = Math.Max(1, rows);
        Lines = new List<VtLine>(Rows);
        for (var row = 0; row < Rows; row++)
        {
            Lines.Add(new VtLine(Columns));
        }

        _tabStops = new bool[Columns];
        ResetTabStops();
        ScrollBottom = Rows - 1;
    }

    internal List<VtLine> Lines { get; }

    internal int Columns { get; private set; }

    internal int Rows { get; private set; }

    internal int CursorRow { get; set; }

    internal int CursorColumn { get; set; }

    /// <summary>
    /// The cursor sits on the last column with a wrap owed. A line that exactly fills the width
    /// must not scroll until another printable character actually arrives, or a progress bar that
    /// fills the row scrolls the screen on every update.
    /// </summary>
    internal bool PendingWrap { get; set; }

    internal int ScrollTop { get; set; }

    internal int ScrollBottom { get; set; }

    internal VtSavedCursor? SavedCursor { get; set; }

    internal VtLine CurrentLine => Lines[Math.Clamp(CursorRow, 0, Lines.Count - 1)];

    internal void SetSize(int columns, int rows)
    {
        Columns = Math.Max(1, columns);
        Rows = Math.Max(1, rows);
        if (_tabStops.Length != Columns)
        {
            _tabStops = new bool[Columns];
            ResetTabStops();
        }
    }

    internal void ResetTabStops()
    {
        Array.Clear(_tabStops);
        for (var column = 8; column < _tabStops.Length; column += 8)
        {
            _tabStops[column] = true;
        }
    }

    internal void SetTabStop(int column)
    {
        if ((uint)column < (uint)_tabStops.Length)
        {
            _tabStops[column] = true;
        }
    }

    internal void ClearTabStop(int column)
    {
        if ((uint)column < (uint)_tabStops.Length)
        {
            _tabStops[column] = false;
        }
    }

    internal void ClearAllTabStops() => Array.Clear(_tabStops);

    /// <summary>
    /// The next tab stop at or after <paramref name="column"/>, or the last column when the rest
    /// of the row has none. A real stop table rather than rounding up to a multiple of eight,
    /// because a program that moved its stops relies on where it put them.
    /// </summary>
    internal int NextTabStop(int column)
    {
        for (var next = column + 1; next < Columns; next++)
        {
            if (next < _tabStops.Length && _tabStops[next])
            {
                return next;
            }
        }

        return Columns - 1;
    }

    internal int PreviousTabStop(int column)
    {
        for (var previous = column - 1; previous > 0; previous--)
        {
            if (previous < _tabStops.Length && _tabStops[previous])
            {
                return previous;
            }
        }

        return 0;
    }

    internal void ResetScrollRegion()
    {
        ScrollTop = 0;
        ScrollBottom = Rows - 1;
    }
}
