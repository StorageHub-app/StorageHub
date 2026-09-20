using System.ComponentModel;
using System.Text;
using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop;

/// <summary>
/// Paints a terminal's cell grid.
///
/// The previous implementation rendered into a <see cref="RichTextBox"/>, which has no notion of a
/// cell: every frame rebuilt the whole document, re-applied every style run through a temporary
/// selection, and scrolled to the caret -- so the view jumped to the bottom whenever output
/// arrived, and any selection the user had made was destroyed sixteen times a second.
/// </summary>
internal sealed class TerminalView : Control
{
    private readonly VtTerminalEmulator _emulator;
    private readonly VScrollBar _scrollBar;
    private readonly Dictionary<int, SolidBrush> _brushes = [];
    private readonly Font?[] _fonts = new Font?[4];
    private Font _baseFont;
    private Size _cellSize;
    private long _viewportTopLine;
    private bool _followTail = true;
    private bool _renderBoldText = true;

    private long _selectionAnchorLine = -1;
    private int _selectionAnchorColumn;
    private long _selectionFocusLine = -1;
    private int _selectionFocusColumn;
    private bool _selecting;

    internal TerminalView(VtTerminalEmulator emulator, Font font)
    {
        _emulator = emulator ?? throw new ArgumentNullException(nameof(emulator));
        _baseFont = font ?? throw new ArgumentNullException(nameof(font));
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.Selectable
            | ControlStyles.ResizeRedraw,
            true);
        TabStop = true;
        Dock = DockStyle.Fill;
        BackColor = DefaultBackground;
        ForeColor = DefaultForeground;
        _scrollBar = new VScrollBar { Dock = DockStyle.Right, Minimum = 0, SmallChange = 1 };
        _scrollBar.Scroll += ScrollBarScrolled;
        Controls.Add(_scrollBar);
        MeasureGrid();
    }

    internal static Color DefaultForeground { get; } = Color.FromArgb(226, 232, 240);

    internal static Color DefaultBackground { get; } = Color.FromArgb(12, 18, 28);

    /// <summary>The 16 ANSI colours, in the order a host addresses them.</summary>
    private static readonly Color[] AnsiPalette =
    [
        Color.FromArgb(15, 23, 42), Color.FromArgb(220, 38, 38),
        Color.FromArgb(22, 163, 74), Color.FromArgb(202, 138, 4),
        Color.FromArgb(37, 99, 235), Color.FromArgb(147, 51, 234),
        Color.FromArgb(8, 145, 178), Color.FromArgb(203, 213, 225),
        Color.FromArgb(100, 116, 139), Color.FromArgb(248, 113, 113),
        Color.FromArgb(74, 222, 128), Color.FromArgb(250, 204, 21),
        Color.FromArgb(96, 165, 250), Color.FromArgb(216, 180, 254),
        Color.FromArgb(34, 211, 238), Color.FromArgb(248, 250, 252)
    ];

    internal Size CellSize => _cellSize;

    /// <summary>
    /// Raised with the bytes for a mouse event a remote program asked to receive. Holding Shift
    /// bypasses forwarding entirely, which is how the user still selects text locally inside a
    /// program that has taken the mouse over.
    /// </summary>
    internal event EventHandler<VtInputEventArgs>? MouseInputProduced;

    /// <summary>Whether SGR bold should pick a bold font as well as a brighter colour.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool RenderBoldText
    {
        get => _renderBoldText;
        set
        {
            if (_renderBoldText == value)
            {
                return;
            }

            _renderBoldText = value;
            Invalidate();
        }
    }

    internal bool HasSelection => _selectionAnchorLine >= 0 && _selectionFocusLine >= 0
        && (_selectionAnchorLine != _selectionFocusLine || _selectionAnchorColumn != _selectionFocusColumn);

    internal void SetFont(Font font)
    {
        ArgumentNullException.ThrowIfNull(font);
        _baseFont = font;
        DisposeFonts();
        MeasureGrid();
        Invalidate();
    }

    /// <summary>
    /// The cell size, measured across a run of characters rather than one. Measuring a single "M"
    /// rounds its width up, and that rounding costs a column or two at every window size.
    /// </summary>
    internal Size MeasureGrid()
    {
        const string Sample = "MMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMM";
        var measured = TextRenderer.MeasureText(Sample, _baseFont, Size.Empty, TextFormatFlags.NoPadding);
        _cellSize = new Size(
            Math.Max(1, (int)Math.Round(measured.Width / (double)Sample.Length)),
            Math.Max(1, measured.Height));
        return _cellSize;
    }

    /// <summary>The grid size this control's client area can hold, in columns and rows.</summary>
    internal (int Columns, int Rows) GetGridSize()
    {
        var width = Math.Max(1, ClientSize.Width - _scrollBar.Width);
        var columns = Math.Clamp(
            width / Math.Max(1, _cellSize.Width),
            SshTerminalIpcContract.MinimumColumns,
            SshTerminalIpcContract.MaximumColumns);
        var rows = Math.Clamp(
            Math.Max(1, ClientSize.Height) / Math.Max(1, _cellSize.Height),
            SshTerminalIpcContract.MinimumRows,
            SshTerminalIpcContract.MaximumRows);
        return (columns, rows);
    }

    // ---------------------------------------------------------------- viewport

    /// <summary>
    /// Applies whatever the emulator changed. New output never moves the viewport while the user
    /// is reading back through history -- the old unconditional ScrollToCaret is what made the
    /// terminal unusable while output was flowing.
    /// </summary>
    internal void ApplyDamage()
    {
        var damage = _emulator.Document.TakeDamage();
        if (damage.IsEmpty)
        {
            return;
        }

        if (_followTail)
        {
            _viewportTopLine = BottomViewportTopLine();
        }

        ClampViewport();
        UpdateScrollBar();
        if (damage.FullRepaint)
        {
            Invalidate();
            return;
        }

        InvalidateLines(damage.FirstLine, damage.LastLine);
    }

    private void InvalidateLines(long first, long last)
    {
        var top = (int)Math.Max(0, first - _viewportTopLine);
        var bottom = (int)Math.Min(VisibleRows, last - _viewportTopLine + 1);
        if (bottom <= top)
        {
            return;
        }

        Invalidate(new Rectangle(
            0,
            top * _cellSize.Height,
            Math.Max(1, ClientSize.Width - _scrollBar.Width),
            Math.Max(1, (bottom - top) * _cellSize.Height)));
    }

    private int VisibleRows => Math.Max(1, ClientSize.Height / Math.Max(1, _cellSize.Height));

    private long BottomViewportTopLine() =>
        Math.Max(_emulator.Document.FirstLineNumber, _emulator.Document.LastLineNumber - VisibleRows + 1);

    private void ClampViewport() => _viewportTopLine = Math.Clamp(
        _viewportTopLine,
        _emulator.Document.FirstLineNumber,
        Math.Max(_emulator.Document.FirstLineNumber, BottomViewportTopLine()));

    private void UpdateScrollBar()
    {
        var document = _emulator.Document;
        var total = (int)Math.Min(int.MaxValue, document.TotalLineCount);
        _scrollBar.Maximum = Math.Max(0, total - 1);
        _scrollBar.LargeChange = Math.Max(1, VisibleRows);
        var value = (int)Math.Clamp(_viewportTopLine - document.FirstLineNumber, 0, Math.Max(0, _scrollBar.Maximum));
        if (_scrollBar.Value != value)
        {
            _scrollBar.Value = value;
        }
    }

    private void ScrollBarScrolled(object? sender, ScrollEventArgs e)
    {
        _viewportTopLine = _emulator.Document.FirstLineNumber + e.NewValue;
        _followTail = _viewportTopLine >= BottomViewportTopLine();
        ClampViewport();
        Invalidate();
    }

    /// <summary>Puts the viewport back at the live end and keeps it there.</summary>
    internal void ScrollToBottom()
    {
        _followTail = true;
        _viewportTopLine = BottomViewportTopLine();
        UpdateScrollBar();
        Invalidate();
    }

    internal void ScrollByLines(int lines)
    {
        _viewportTopLine += lines;
        ClampViewport();
        _followTail = _viewportTopLine >= BottomViewportTopLine();
        UpdateScrollBar();
        Invalidate();
    }

    internal void ScrollByPages(int pages) => ScrollByLines(pages * VisibleRows);

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseWheel(e);
        var wheel = e.Delta > 0 ? VtKeyEncoder.VtMouseEvent.WheelUp : VtKeyEncoder.VtMouseEvent.WheelDown;
        if (TryForwardMouse(wheel, e))
        {
            return;
        }

        ScrollByLines(-Math.Sign(e.Delta) * SystemInformation.MouseWheelScrollLines);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ClampViewport();
        UpdateScrollBar();
    }

    // ---------------------------------------------------------------- selection

    /// <summary>
    /// Selection is anchored in absolute (line number, column), not in a character offset into a
    /// rebuilt string, so it survives repaints and scrollback trimming underneath it.
    /// </summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseDown(e);
        Focus();
        if (TryForwardMouse(VtKeyEncoder.VtMouseEvent.Press, e))
        {
            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var (line, column) = HitTest(e.Location);
        if (ModifierKeys.HasFlag(Keys.Shift) && _selectionAnchorLine >= 0)
        {
            _selectionFocusLine = line;
            _selectionFocusColumn = column;
        }
        else
        {
            _selectionAnchorLine = line;
            _selectionAnchorColumn = column;
            _selectionFocusLine = line;
            _selectionFocusColumn = column;
        }

        _selecting = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        if (!_selecting)
        {
            if (_emulator.MouseAnyEventTracking
                || (_emulator.MouseButtonTracking && e.Button != MouseButtons.None))
            {
                _ = TryForwardMouse(VtKeyEncoder.VtMouseEvent.Move, e);
            }

            return;
        }

        // Dragging past the top or bottom edge scrolls, so a selection can run past one screen.
        if (e.Y < 0)
        {
            ScrollByLines(-1);
        }
        else if (e.Y > ClientSize.Height)
        {
            ScrollByLines(1);
        }

        (_selectionFocusLine, _selectionFocusColumn) = HitTest(e.Location);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (TryForwardMouse(VtKeyEncoder.VtMouseEvent.Release, e))
        {
            return;
        }

        _selecting = false;
    }

    protected override void OnDoubleClick(EventArgs e)
    {
        base.OnDoubleClick(e);
        var point = PointToClient(MousePosition);
        var (line, column) = HitTest(point);
        var cells = _emulator.Document.GetLine(line);
        if (cells.IsEmpty)
        {
            return;
        }

        static bool IsWord(int codepoint) =>
            codepoint == '_' || char.IsLetterOrDigit(char.ConvertFromUtf32(codepoint), 0);

        var start = Math.Clamp(column, 0, cells.Length - 1);
        var end = start;
        while (start > 0 && IsWord(cells[start - 1].Codepoint)) { start--; }
        while (end + 1 < cells.Length && IsWord(cells[end + 1].Codepoint)) { end++; }

        _selectionAnchorLine = line;
        _selectionAnchorColumn = start;
        _selectionFocusLine = line;
        _selectionFocusColumn = end + 1;
        Invalidate();
    }

    internal void SelectLine(long lineNumber)
    {
        _selectionAnchorLine = lineNumber;
        _selectionAnchorColumn = 0;
        _selectionFocusLine = lineNumber;
        _selectionFocusColumn = _emulator.Document.Columns;
        Invalidate();
    }

    internal void ClearSelection()
    {
        _selectionAnchorLine = -1;
        _selectionFocusLine = -1;
        Invalidate();
    }

    /// <summary>
    /// Sends the event to the remote when a program has asked for the mouse, unless Shift is
    /// held. Returns whether it was forwarded, in which case the local handling is skipped.
    /// </summary>
    /// <summary>The one button that produced this event, as the encoder names it.</summary>
    /// <remarks>
    /// MouseButtons is a flags enum and can report several at once; the encoder takes a single
    /// button because the SGR form has one code per event. Taking them in this order reports the
    /// same button WinForms would have called primary.
    /// </remarks>
    private static Avalonia.Input.MouseButton ToMouseButton(MouseButtons buttons) =>
        buttons.HasFlag(MouseButtons.Left) ? Avalonia.Input.MouseButton.Left
            : buttons.HasFlag(MouseButtons.Middle) ? Avalonia.Input.MouseButton.Middle
            : buttons.HasFlag(MouseButtons.Right) ? Avalonia.Input.MouseButton.Right
            : Avalonia.Input.MouseButton.None;

    private bool TryForwardMouse(VtKeyEncoder.VtMouseEvent mouseEvent, MouseEventArgs e)
    {
        if (!_emulator.MouseReporting || ModifierKeys.HasFlag(Keys.Shift))
        {
            return false;
        }

        var column = e.X / Math.Max(1, _cellSize.Width);
        var row = e.Y / Math.Max(1, _cellSize.Height);
        var bytes = VtKeyEncoder.EncodeMouse(
            mouseEvent,
            ToMouseButton(e.Button),
            column,
            row,
            shift: false,
            ModifierKeys.HasFlag(Keys.Alt),
            ModifierKeys.HasFlag(Keys.Control),
            _emulator.MouseSgrEncoding);
        if (bytes is null)
        {
            return false;
        }

        MouseInputProduced?.Invoke(this, new VtInputEventArgs(bytes));
        return true;
    }

    private (long Line, int Column) HitTest(Point point)
    {
        var row = point.Y / Math.Max(1, _cellSize.Height);
        var column = point.X / Math.Max(1, _cellSize.Width);
        return (
            Math.Clamp(_viewportTopLine + row, _emulator.Document.FirstLineNumber, _emulator.Document.LastLineNumber),
            Math.Clamp(column, 0, _emulator.Document.Columns));
    }

    private (long Line, int Column, long EndLine, int EndColumn)? OrderedSelection()
    {
        if (!HasSelection)
        {
            return null;
        }

        var forward = _selectionAnchorLine < _selectionFocusLine
            || (_selectionAnchorLine == _selectionFocusLine && _selectionAnchorColumn <= _selectionFocusColumn);
        return forward
            ? (_selectionAnchorLine, _selectionAnchorColumn, _selectionFocusLine, _selectionFocusColumn)
            : (_selectionFocusLine, _selectionFocusColumn, _selectionAnchorLine, _selectionAnchorColumn);
    }

    /// <summary>
    /// The selected text. Lines the terminal wrapped are joined without a break, because that
    /// break belongs to the window width rather than to what was written -- pasting a copied
    /// command back should not insert a newline in the middle of it.
    /// </summary>
    internal string GetSelectedText()
    {
        if (OrderedSelection() is not { } selection)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var number = selection.Line; number <= selection.EndLine; number++)
        {
            var line = _emulator.Document.FindLine(number);
            if (line is null)
            {
                continue;
            }

            var from = number == selection.Line ? selection.Column : 0;
            var to = number == selection.EndLine ? selection.EndColumn : line.TrimmedLength();
            to = Math.Min(to, line.Length);
            if (to > from)
            {
                var start = builder.Length;
                line.AppendTextTo(builder, from, to - from);

                // Trailing run of spaces on a full-width row is padding, not content.
                while (builder.Length > start && builder[^1] == ' ')
                {
                    builder.Length--;
                }
            }

            if (number < selection.EndLine && !line.WrappedToNext)
            {
                builder.Append(Environment.NewLine);
            }
        }

        return builder.ToString();
    }

    private bool IsSelected(long line, int column)
    {
        if (OrderedSelection() is not { } selection)
        {
            return false;
        }

        if (line < selection.Line || line > selection.EndLine)
        {
            return false;
        }

        var from = line == selection.Line ? selection.Column : 0;
        var to = line == selection.EndLine ? selection.EndColumn : _emulator.Document.Columns;
        return column >= from && column < to;
    }

    // ---------------------------------------------------------------- painting

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var document = _emulator.Document;
        var width = Math.Max(1, ClientSize.Width - _scrollBar.Width);
        e.Graphics.FillRectangle(Brush(DefaultBackground), e.ClipRectangle);

        // Only the rows the clip rectangle actually touches are painted.
        var firstRow = Math.Max(0, e.ClipRectangle.Top / Math.Max(1, _cellSize.Height));
        var lastRow = Math.Min(VisibleRows, (e.ClipRectangle.Bottom / Math.Max(1, _cellSize.Height)) + 1);
        var cursorLine = document.CursorLineNumber;

        for (var row = firstRow; row < lastRow; row++)
        {
            var lineNumber = _viewportTopLine + row;
            var cells = document.GetLine(lineNumber);
            if (cells.IsEmpty)
            {
                continue;
            }

            PaintRow(e.Graphics, cells, lineNumber, row, width);
        }

        if (_emulator.CursorVisible)
        {
            PaintCursor(e.Graphics, cursorLine, document.Screen.CursorColumn);
        }
    }

    private void PaintRow(Graphics graphics, ReadOnlySpan<VtCell> cells, long lineNumber, int row, int width)
    {
        var y = row * _cellSize.Height;
        var column = 0;
        while (column < cells.Length)
        {
            var selected = IsSelected(lineNumber, column);
            var start = column;
            var cell = cells[column];

            // Batch neighbouring cells that share a style into one run: one fill and one DrawText
            // for the run instead of per cell.
            while (column < cells.Length
                && cells[column].SameStyle(cell)
                && IsSelected(lineNumber, column) == selected)
            {
                column++;
            }

            var runLength = column - start;
            var bounds = new Rectangle(start * _cellSize.Width, y, runLength * _cellSize.Width, _cellSize.Height);
            if (bounds.Left >= width)
            {
                break;
            }

            var (foreground, background) = Resolve(cell, selected);
            if (background != DefaultBackground)
            {
                graphics.FillRectangle(Brush(background), bounds);
            }

            if (!cell.Flags.HasFlag(VtCellFlags.Hidden))
            {
                DrawRun(graphics, cells[start..column], bounds, foreground, cell.Flags);
            }

            DrawDecorations(graphics, bounds, foreground, cell.Flags);
        }
    }

    private void DrawRun(Graphics graphics, ReadOnlySpan<VtCell> run, Rectangle bounds, Color foreground, VtCellFlags flags)
    {
        var font = FontFor(flags);
        var builder = new StringBuilder(run.Length);
        var nonAscii = false;
        foreach (var cell in run)
        {
            if (cell.Flags.HasFlag(VtCellFlags.WideTrailing))
            {
                continue;
            }

            nonAscii |= cell.Codepoint is < 0x20 or > 0x7E;
            builder.Append(char.ConvertFromUtf32(cell.Codepoint));
        }

        var text = builder.ToString();
        if (text.Length == 0 || text.All(character => character == ' '))
        {
            return;
        }

        const TextFormatFlags Format = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix
            | TextFormatFlags.SingleLine | TextFormatFlags.Left | TextFormatFlags.Top;

        if (!nonAscii)
        {
            TextRenderer.DrawText(graphics, text, font, bounds.Location, foreground, Format);
            return;
        }

        // A fallback font for a non-ASCII glyph is not necessarily monospaced, and one wider
        // glyph would shear every column after it. Those runs are drawn cell by cell at exact
        // offsets so the grid holds.
        var index = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var origin = new Point(bounds.Left + (index * _cellSize.Width), bounds.Top);
            TextRenderer.DrawText(graphics, rune.ToString(), font, origin, foreground, Format);
            index++;
        }
    }

    private static void DrawDecorations(Graphics graphics, Rectangle bounds, Color foreground, VtCellFlags flags)
    {
        if (!flags.HasFlag(VtCellFlags.Underline) && !flags.HasFlag(VtCellFlags.Strike))
        {
            return;
        }

        using var pen = new Pen(foreground);
        if (flags.HasFlag(VtCellFlags.Underline))
        {
            var y = bounds.Bottom - 1;
            graphics.DrawLine(pen, bounds.Left, y, bounds.Right, y);
        }

        if (flags.HasFlag(VtCellFlags.Strike))
        {
            var y = bounds.Top + (bounds.Height / 2);
            graphics.DrawLine(pen, bounds.Left, y, bounds.Right, y);
        }
    }

    private void PaintCursor(Graphics graphics, long lineNumber, int column)
    {
        var row = lineNumber - _viewportTopLine;
        if (row < 0 || row >= VisibleRows)
        {
            return;
        }

        var bounds = new Rectangle(
            column * _cellSize.Width,
            (int)row * _cellSize.Height,
            Math.Max(1, _cellSize.Width),
            _cellSize.Height);
        using var brush = new SolidBrush(Color.FromArgb(Focused ? 170 : 90, DefaultForeground));
        graphics.FillRectangle(brush, bounds);
    }

    /// <summary>
    /// Turns a cell's stored colours into the ones to paint with. Inverse and selection are both
    /// applied here rather than being baked into the cell, so toggling either is just a repaint.
    /// </summary>
    private static (Color Foreground, Color Background) Resolve(VtCell cell, bool selected)
    {
        var foreground = ResolveColor(cell.Foreground, DefaultForeground);
        var background = ResolveColor(cell.Background, DefaultBackground);
        if (cell.Flags.HasFlag(VtCellFlags.Bold)
            && cell.Foreground.Kind == VtColorKind.Indexed
            && cell.Foreground.Index < 8)
        {
            foreground = AnsiPalette[cell.Foreground.Index + 8];
        }

        if (cell.Flags.HasFlag(VtCellFlags.Dim))
        {
            foreground = Color.FromArgb(foreground.R / 2, foreground.G / 2, foreground.B / 2);
        }

        if (cell.Flags.HasFlag(VtCellFlags.Inverse))
        {
            (foreground, background) = (background, foreground);
        }

        if (selected)
        {
            (foreground, background) = (background, foreground);
        }

        return (foreground, background);
    }

    private static Color ResolveColor(VtColor color, Color fallback) => color.Kind switch
    {
        VtColorKind.Indexed => color.Index < 16 ? AnsiPalette[color.Index] : XtermColor(color.Index),
        VtColorKind.Rgb => Color.FromArgb(color.Red, color.Green, color.Blue),
        _ => fallback
    };

    private static Color XtermColor(int index)
    {
        if (index >= 232)
        {
            var shade = 8 + ((index - 232) * 10);
            return Color.FromArgb(shade, shade, shade);
        }

        var cube = index - 16;
        static int Component(int value) => value == 0 ? 0 : 55 + (value * 40);
        return Color.FromArgb(Component(cube / 36), Component((cube / 6) % 6), Component(cube % 6));
    }

    private Font FontFor(VtCellFlags flags)
    {
        var bold = _renderBoldText && flags.HasFlag(VtCellFlags.Bold);
        var italic = flags.HasFlag(VtCellFlags.Italic);
        var slot = (bold ? 1 : 0) | (italic ? 2 : 0);
        if (_fonts[slot] is { } cached)
        {
            return cached;
        }

        var style = (bold ? FontStyle.Bold : FontStyle.Regular) | (italic ? FontStyle.Italic : FontStyle.Regular);
        var font = slot == 0 ? _baseFont : new Font(_baseFont, style);
        _fonts[slot] = font;
        return font;
    }

    private SolidBrush Brush(Color color)
    {
        if (_brushes.TryGetValue(color.ToArgb(), out var cached))
        {
            return cached;
        }

        var brush = new SolidBrush(color);
        _brushes[color.ToArgb()] = brush;
        return brush;
    }

    private void DisposeFonts()
    {
        for (var index = 1; index < _fonts.Length; index++)
        {
            _fonts[index]?.Dispose();
            _fonts[index] = null;
        }

        _fonts[0] = null;
    }

    // ---------------------------------------------------------------- input plumbing

    /// <summary>
    /// Tab and the arrows are terminal input, not navigation. Without this WinForms swallows them
    /// to move focus and they never reach the remote.
    /// </summary>
    protected override bool IsInputKey(Keys keyData) => true;

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        MeasureGrid();
    }

    /// <summary>
    /// A RichTextBox gave screen readers the text for free; a custom control gives them nothing,
    /// so the viewport is exposed deliberately here.
    /// </summary>
    protected override AccessibleObject CreateAccessibilityInstance() => new TerminalAccessibleObject(this);

    private sealed class TerminalAccessibleObject(TerminalView owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.Text;

        public override string? Value => owner._emulator.Document.GetText(
            owner._viewportTopLine,
            owner.VisibleRows,
            joinWrapped: false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scrollBar.Scroll -= ScrollBarScrolled;
            DisposeFonts();
            foreach (var brush in _brushes.Values)
            {
                brush.Dispose();
            }

            _brushes.Clear();
        }

        base.Dispose(disposing);
    }
}
