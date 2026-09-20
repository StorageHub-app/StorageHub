using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;

namespace StorageHub.Desktop.Views;

/// <summary>
/// Draws a terminal screen buffer.
/// </summary>
/// <remarks>
/// <para>
/// The last piece of the SSH pane. Everything it draws is already in Desktop.Core with its own
/// suites -- <see cref="VtTerminalEmulator"/> parses the stream, <see cref="VtTerminalDocument"/>
/// holds the screen and the scrollback, and <see cref="VtPalette"/> decides what colour a cell
/// ends up. This turns the result into a picture, and that is all it does.
/// </para>
/// <para>
/// A grid of characters is drawn as runs, not cells. Neighbouring cells sharing a style become one
/// fill and one text draw, because a 200-column screen is 200 draws per row otherwise and eighty
/// rows of that is not a frame budget. <see cref="VtCell.SameStyle"/> is what decides a run, and it
/// already existed for the painter this replaces.
/// </para>
/// <para>
/// The viewport is a line number, not a pixel offset, and it follows the live end only while it
/// is there. New output never moves the view while somebody is reading back through history --
/// the unconditional scroll-to-caret this replaces is what made the 1.x terminal unusable while
/// output was flowing. The scroll bar is the pane's <see cref="ScrollViewer"/>, driven through
/// <see cref="ILogicalScrollable"/> so it counts lines rather than pixels.
/// </para>
/// <para>
/// Selection is anchored in absolute line number and column, not in an offset into a rebuilt
/// string, so it survives repaints and scrollback trimming underneath it. Copy is Ctrl+Shift+C or
/// Ctrl+Insert, for the same reason paste is shifted: plain Ctrl+C is SIGINT and must stay so.
/// </para>
/// </remarks>
internal sealed class TerminalView : Control, ILogicalScrollable
{
    /// <summary>How many lines one notch of the wheel moves.</summary>
    private const int WheelLines = 3;

    /// <summary>
    /// A wide sample, because measuring one character rounds its width up.
    /// </summary>
    /// <remarks>
    /// That rounding is a fraction of a pixel per cell and a column or two per window, which is
    /// the difference between a remote program's idea of the width and ours -- and a redraw that
    /// wraps in the wrong place. The WinForms painter learned this the same way.
    /// </remarks>
    private const string WidthSample = "MMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMM";

    public static readonly StyledProperty<VtTerminalDocument?> DocumentProperty =
        AvaloniaProperty.Register<TerminalView, VtTerminalDocument?>(nameof(Document));

    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<TerminalView, double>(nameof(FontSize), defaultValue: 13);

    public static readonly StyledProperty<bool> CursorVisibleProperty =
        AvaloniaProperty.Register<TerminalView, bool>(nameof(CursorVisible), defaultValue: true);

    /// <summary>
    /// The session this control is a window onto, or nothing while it is only a picture.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Document"/> so the painter can still be handed a screen buffer with
    /// no session behind it, which is how it is tested and how a disconnected pane keeps showing
    /// what the session said before it ended.
    /// </remarks>
    public static readonly StyledProperty<ITerminalSession?> SessionProperty =
        AvaloniaProperty.Register<TerminalView, ITerminalSession?>(nameof(Session));

    /// <summary>
    /// Breathing room between the text and the edge of the surface.
    /// </summary>
    /// <remarks>
    /// Inside the control rather than around it, because the background belongs to the terminal.
    /// A margin would frame a dark screen in whatever the pane behind it is painted, which in the
    /// light appearance is a pale border around a black rectangle.
    /// </remarks>
    public static readonly StyledProperty<Thickness> PaddingProperty =
        AvaloniaProperty.Register<TerminalView, Thickness>(
            nameof(Padding), defaultValue: new Thickness(8));

    private Typeface _typeface;
    private Size _cell;
    private ITerminalSession? _attached;
    private (int Columns, int Rows) _reported;
    private long _viewportTop;
    private bool _followTail = true;
    private long _selectionAnchorLine = -1;
    private int _selectionAnchorColumn;
    private long _selectionFocusLine = -1;
    private int _selectionFocusColumn;
    private bool _selecting;

    static TerminalView()
    {
        AffectsRender<TerminalView>(DocumentProperty, CursorVisibleProperty, PaddingProperty);
        AffectsMeasure<TerminalView>(FontSizeProperty);
    }

    public TerminalView()
    {
        ClipToBounds = true;

        // A terminal that cannot take focus cannot be typed at, and one that is skipped by Tab is
        // unreachable without a mouse.
        Focusable = true;
    }

    public Thickness Padding
    {
        get => GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    /// <summary>The session driving this control.</summary>
    public ITerminalSession? Session
    {
        get => GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>The screen to draw, or nothing before a session is open.</summary>
    public VtTerminalDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>Whether the cursor block is drawn, which a remote program can turn off.</summary>
    public bool CursorVisible
    {
        get => GetValue(CursorVisibleProperty);
        set => SetValue(CursorVisibleProperty, value);
    }

    /// <summary>
    /// One character cell, in device-independent pixels.
    /// </summary>
    /// <remarks>
    /// Measured on demand rather than only while rendering, so asking before the first frame gives
    /// the answer instead of zero. A caller should not have to know a paint has happened.
    /// </remarks>
    internal Size CellSize
    {
        get
        {
            Measure();
            return _cell;
        }
    }

    /// <summary>
    /// How many columns and rows this control's area holds.
    /// </summary>
    /// <remarks>
    /// What the session is resized to, so a remote program wraps where the window does. At least
    /// one of each: a control measured to nothing still has to describe a terminal.
    /// </remarks>
    internal (int Columns, int Rows) GridSize(Size available)
    {
        Measure();
        var padding = Padding;
        return (
            Math.Max(1, (int)((available.Width - padding.Left - padding.Right) / _cell.Width)),
            Math.Max(1, (int)((available.Height - padding.Top - padding.Bottom) / _cell.Height)));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SessionProperty) Attach(change.GetNewValue<ITerminalSession?>());
        if (change.Property == DocumentProperty)
        {
            // A new screen starts at its live end, with nothing selected in a buffer that no
            // longer exists.
            ClearSelection();
            ScrollToBottom();
        }
    }

    /// <summary>
    /// Follows a session, and stops following the one before it.
    /// </summary>
    /// <remarks>
    /// The document is taken from the session rather than bound separately: two properties that
    /// have to agree about which screen is on display is one property too many.
    /// </remarks>
    private void Attach(ITerminalSession? session)
    {
        if (_attached is not null) _attached.OutputReceived -= OnOutputReceived;
        _attached = session;
        if (_attached is not null) _attached.OutputReceived += OnOutputReceived;

        Document = session?.Document;
        CursorVisible = session?.CursorVisible ?? true;
        _reported = default;
        ReportGridSize();
        InvalidateVisual();
    }

    private void OnOutputReceived(object? sender, EventArgs e)
    {
        // The cursor is the emulator's to hide, and a full-screen program hides it while it draws.
        if (_attached is not null) CursorVisible = _attached.CursorVisible;
        FollowOutput();
    }

    /// <summary>
    /// Keeps the viewport where it belongs after the document changed: at the live end when it
    /// was there, and exactly where it was when somebody had scrolled back.
    /// </summary>
    internal void FollowOutput()
    {
        if (_followTail) _viewportTop = BottomViewportTop();
        ClampViewport();
        RaiseScrollInvalidated(EventArgs.Empty);
        InvalidateVisual();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        ReportGridSize(arranged);

        // A new height is a new number of rows, and a view at the live end stays at the live
        // end: before the first layout there was one row, and the bottom of the document was
        // the last line alone. Bounds still holds the old size here, so the rows come from the
        // size being arranged.
        var rows = RowsFor(arranged.Height);
        if (_followTail) _viewportTop = BottomViewportTop(rows);
        ClampViewport(rows);
        RaiseScrollInvalidated(EventArgs.Empty);
        return arranged;
    }

    // ---------------------------------------------------------------- viewport

    /// <summary>The first line on screen, as an absolute line number in the document.</summary>
    internal long ViewportTopLine => _viewportTop;

    /// <summary>Whether the view is at the live end and will stay there as output arrives.</summary>
    internal bool FollowsTail => _followTail;

    /// <summary>How many rows the text area holds now.</summary>
    internal int VisibleRows => RowsFor(Bounds.Height);

    private int RowsFor(double height)
    {
        Measure();
        var padding = Padding;
        return Math.Max(1, (int)((height - padding.Top - padding.Bottom) / _cell.Height));
    }

    /// <summary>Puts the viewport back at the live end and keeps it there.</summary>
    internal void ScrollToBottom()
    {
        _followTail = true;
        _viewportTop = BottomViewportTop();
        RaiseScrollInvalidated(EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>Moves the view by whole lines; positive is towards the live end.</summary>
    internal void ScrollByLines(long lines)
    {
        _viewportTop += lines;
        ClampViewport();
        _followTail = _viewportTop >= BottomViewportTop();
        RaiseScrollInvalidated(EventArgs.Empty);
        InvalidateVisual();
    }

    internal void ScrollByPages(int pages) => ScrollByLines((long)pages * VisibleRows);

    private long BottomViewportTop() => BottomViewportTop(VisibleRows);

    private long BottomViewportTop(int rows) => Document is { } document
        ? Math.Max(document.FirstLineNumber, document.LastLineNumber - rows + 1)
        : 0;

    private void ClampViewport() => ClampViewport(VisibleRows);

    private void ClampViewport(int rows)
    {
        if (Document is not { } document)
        {
            _viewportTop = 0;
            return;
        }

        _viewportTop = Math.Clamp(
            _viewportTop,
            document.FirstLineNumber,
            Math.Max(document.FirstLineNumber, BottomViewportTop(rows)));
    }

    // ------------------------------------------------------ ILogicalScrollable

    /// <summary>
    /// The scroll bar speaks lines.
    /// </summary>
    /// <remarks>
    /// Extent and offset are in cell heights, so one step of the bar is one line of the document
    /// and the thumb's size is the screen's share of the scrollback. A pixel-based ScrollViewer
    /// around a control that redraws itself would instead scroll a picture of the terminal.
    /// </remarks>
    public Size Extent
    {
        get
        {
            Measure();
            var lines = Document?.TotalLineCount ?? 0;
            return new Size(Bounds.Width, Math.Max(Bounds.Height, lines * _cell.Height));
        }
    }

    public Vector Offset
    {
        get
        {
            Measure();
            var first = Document?.FirstLineNumber ?? 0;
            return new Vector(0, (_viewportTop - first) * _cell.Height);
        }
        set
        {
            if (Document is not { } document) return;
            Measure();
            _viewportTop = document.FirstLineNumber + (long)Math.Round(value.Y / _cell.Height);
            ClampViewport();
            _followTail = _viewportTop >= BottomViewportTop();
            InvalidateVisual();
        }
    }

    public Size Viewport => Bounds.Size;

    public Size ScrollSize
    {
        get
        {
            Measure();
            return new Size(0, _cell.Height);
        }
    }

    public Size PageScrollSize
    {
        get
        {
            Measure();
            return new Size(0, VisibleRows * _cell.Height);
        }
    }

    public bool CanHorizontallyScroll { get; set; }

    public bool CanVerticallyScroll { get; set; } = true;

    public bool IsLogicalScrollEnabled => true;

    public event EventHandler? ScrollInvalidated;

    public bool BringIntoView(Control target, Rect targetRect) => false;

    public Control? GetControlInDirection(NavigationDirection direction, Control? from) => null;

    public void RaiseScrollInvalidated(EventArgs e) => ScrollInvalidated?.Invoke(this, e);

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Handled || Document is null) return;

        // Handled here rather than left to the ScrollViewer, so the terminal decides what a
        // notch means: three lines, whatever the platform's pixel delta.
        var notches = e.Delta.Y;
        if (notches == 0) return;
        ScrollByLines(-(long)Math.Round(notches) * WheelLines);
        e.Handled = true;
    }

    // ---------------------------------------------------------------- selection

    internal bool HasSelection =>
        _selectionAnchorLine >= 0 && _selectionFocusLine >= 0 &&
        (_selectionAnchorLine != _selectionFocusLine || _selectionAnchorColumn != _selectionFocusColumn);

    /// <summary>Selects from one cell to another, given in absolute line numbers and columns.</summary>
    internal void Select(long anchorLine, int anchorColumn, long focusLine, int focusColumn)
    {
        _selectionAnchorLine = anchorLine;
        _selectionAnchorColumn = anchorColumn;
        _selectionFocusLine = focusLine;
        _selectionFocusColumn = focusColumn;
        InvalidateVisual();
    }

    /// <summary>
    /// Selects the word under a cell: letters, digits and underscores, as a shell would.
    /// </summary>
    internal void SelectWordAt(long line, int column)
    {
        if (Document is not { } document) return;
        var cells = document.GetLine(line);
        if (cells.IsEmpty) return;

        static bool IsWord(int codepoint) =>
            codepoint == '_' || char.IsLetterOrDigit(char.ConvertFromUtf32(codepoint), 0);

        var start = Math.Clamp(column, 0, cells.Length - 1);
        if (!IsWord(cells[start].Codepoint)) return;
        var end = start;
        while (start > 0 && IsWord(cells[start - 1].Codepoint)) start--;
        while (end + 1 < cells.Length && IsWord(cells[end + 1].Codepoint)) end++;

        Select(line, start, line, end + 1);
    }

    /// <summary>Selects a whole line, which is what a triple click means.</summary>
    internal void SelectLine(long line)
    {
        if (Document is not { } document) return;
        Select(line, 0, line, document.Columns);
    }

    internal void ClearSelection()
    {
        if (_selectionAnchorLine < 0 && _selectionFocusLine < 0) return;
        _selectionAnchorLine = -1;
        _selectionFocusLine = -1;
        InvalidateVisual();
    }

    /// <summary>
    /// The selected text, or nothing.
    /// </summary>
    /// <remarks>
    /// Lines the terminal wrapped are joined without a break, because that break belongs to the
    /// window width rather than to what was written: pasting a copied command back should not
    /// insert a newline in the middle of it. Trailing spaces on a full-width row are padding.
    /// </remarks>
    internal string SelectedText
    {
        get
        {
            if (Document is not { } document || OrderedSelection() is not { } selection) return string.Empty;

            var builder = new StringBuilder();
            for (var number = selection.Line; number <= selection.EndLine; number++)
            {
                var line = document.FindLine(number);
                if (line is null) continue;

                var from = number == selection.Line ? selection.Column : 0;
                var to = number == selection.EndLine ? selection.EndColumn : line.TrimmedLength();
                to = Math.Min(to, line.Length);
                if (to > from)
                {
                    var start = builder.Length;
                    line.AppendTextTo(builder, from, to - from);
                    while (builder.Length > start && builder[^1] == ' ') builder.Length--;
                }

                if (number < selection.EndLine && !line.WrappedToNext) builder.Append(Environment.NewLine);
            }

            return builder.ToString();
        }
    }

    /// <summary>The cell under a point on this control, clamped to the document.</summary>
    internal (long Line, int Column) HitTest(Point point)
    {
        Measure();
        if (Document is not { } document) return (0, 0);
        var padding = Padding;
        var row = (long)Math.Floor((point.Y - padding.Top) / _cell.Height);
        var column = (int)Math.Floor((point.X - padding.Left) / _cell.Width);
        return (
            Math.Clamp(_viewportTop + row, document.FirstLineNumber, document.LastLineNumber),
            Math.Clamp(column, 0, document.Columns));
    }

    private (long Line, int Column, long EndLine, int EndColumn)? OrderedSelection()
    {
        if (!HasSelection) return null;

        var forward = _selectionAnchorLine < _selectionFocusLine ||
            (_selectionAnchorLine == _selectionFocusLine && _selectionAnchorColumn <= _selectionFocusColumn);
        return forward
            ? (_selectionAnchorLine, _selectionAnchorColumn, _selectionFocusLine, _selectionFocusColumn)
            : (_selectionFocusLine, _selectionFocusColumn, _selectionAnchorLine, _selectionAnchorColumn);
    }

    private bool IsSelected(long line, int column)
    {
        if (OrderedSelection() is not { } selection) return false;
        if (line < selection.Line || line > selection.EndLine) return false;

        var from = line == selection.Line ? selection.Column : 0;
        var to = line == selection.EndLine ? selection.EndColumn : Document?.Columns ?? 0;
        return column >= from && column < to;
    }

    /// <summary>Puts the selection on the clipboard, when there is one.</summary>
    private async Task CopyAsync()
    {
        var text = SelectedText;
        if (text.Length == 0) return;
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text).ConfigureAwait(true);
            }
        }
        catch (Exception error) when (error is InvalidOperationException or TimeoutException or IOException)
        {
        }
    }

    /// <summary>
    /// Tells the session how wide its window is, when that has actually changed.
    /// </summary>
    /// <remarks>
    /// Arrange runs for reasons that have nothing to do with size, and a drag across a few hundred
    /// pixels produces a stream of them. Only a change of whole columns or rows is worth a message:
    /// the remote is sent a window-size signal for each one, and a shell that redraws its prompt on
    /// every signal would spend a drag redrawing.
    /// </remarks>
    private void ReportGridSize(Size? arranged = null)
    {
        if (_attached is null) return;
        var size = arranged ?? Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0) return;

        var grid = GridSize(size);
        if (grid == _reported) return;
        _reported = grid;
        _attached.Resize(grid.Columns, grid.Rows);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // Clicking a terminal is how one starts typing at it.
        Focus();
        if (e.Handled || Document is null) return;

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;

        var (line, column) = HitTest(point.Position);
        switch (e.ClickCount)
        {
            case 2:
                SelectWordAt(line, column);
                _selecting = false;
                break;
            case 3:
                SelectLine(line);
                _selecting = false;
                break;
            default:
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _selectionAnchorLine >= 0)
                {
                    Select(_selectionAnchorLine, _selectionAnchorColumn, line, column);
                }
                else
                {
                    Select(line, column, line, column);
                }

                _selecting = true;
                e.Pointer.Capture(this);
                break;
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_selecting || Document is null) return;

        // Dragging past the top or bottom edge scrolls, so a selection can run past one screen.
        var position = e.GetPosition(this);
        if (position.Y < 0) ScrollByLines(-1);
        else if (position.Y > Bounds.Height) ScrollByLines(1);

        var (line, column) = HitTest(position);
        Select(_selectionAnchorLine, _selectionAnchorColumn, line, column);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_selecting) return;
        _selecting = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Session is not { } session) return;

        var modifiers = e.KeyModifiers;
        var control = modifiers.HasFlag(KeyModifiers.Control);
        var shift = modifiers.HasFlag(KeyModifiers.Shift);

        // Ctrl+Shift+C and Ctrl+Insert copy the selection, for the reason paste is shifted below.
        if ((control && shift && e.Key == Key.C) || (control && e.Key == Key.Insert))
        {
            if (HasSelection)
            {
                _ = CopyAsync();
                e.Handled = true;
                return;
            }
        }

        // Shift+PageUp and Shift+PageDown read back through history; the unshifted keys go to
        // the remote program, which is what a pager expects them to do.
        if (shift && !control && e.Key is Key.PageUp or Key.PageDown)
        {
            ScrollByPages(e.Key == Key.PageUp ? -1 : 1);
            e.Handled = true;
            return;
        }

        // Ctrl+Shift+V and Shift+Insert paste, which leaves plain Ctrl+V to the remote program and
        // -- far more importantly -- leaves plain Ctrl+C meaning SIGINT. Binding paste or copy to
        // the unshifted chord would make interrupting a runaway process depend on what the
        // clipboard or a stray selection happened to contain.
        if ((control && shift && e.Key == Key.V) || (shift && e.Key == Key.Insert))
        {
            _ = PasteAsync(session);
            e.Handled = true;
            return;
        }

        e.Handled = session.SendKey(e.Key, shift, modifiers.HasFlag(KeyModifiers.Alt), control);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Handled || Session is not { } session || string.IsNullOrEmpty(e.Text)) return;

        // Control characters have already gone out through the encoder on key-down; sending them
        // again here would double every Ctrl+letter and every Enter.
        if (e.Text.All(char.IsControl)) return;

        session.SendText(e.Text);
        e.Handled = true;
    }

    /// <summary>
    /// Pastes the clipboard into the session.
    /// </summary>
    /// <remarks>
    /// Avalonia's clipboard is asynchronous and belongs to the window, not the process, which is
    /// what an X11 clipboard actually is. A clipboard that cannot be read is not worth a dialog.
    /// </remarks>
    private async Task PasteAsync(ITerminalSession session)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
            // TryGetTextAsync, not GetTextAsync: a clipboard holding an image or a file list has
            // no text to give, and that is an ordinary answer rather than a failure.
            var text = await clipboard.TryGetTextAsync().ConfigureAwait(true);
            if (!string.IsNullOrEmpty(text)) session.Paste(text);
        }
        catch (Exception error) when (error is InvalidOperationException or TimeoutException or IOException)
        {
        }
    }

    public override void Render(DrawingContext context)
    {
        Measure();
        var background = Resolve("TerminalBackgroundColor", Color.FromRgb(15, 23, 42));
        var foreground = Resolve("TerminalForegroundColor", Color.FromRgb(226, 232, 240));
        context.FillRectangle(new SolidColorBrush(background), new Rect(Bounds.Size));

        if (Document is not { } document) return;

        // The background covers the whole control; only the text is inset. Clipping to the padded
        // area as well keeps a long row from drawing over the right-hand inset.
        var padding = Padding;
        var text = new Rect(Bounds.Size).Deflate(padding);
        if (text.Width <= 0 || text.Height <= 0) return;

        using var _ = context.PushClip(text);
        using var __ = context.PushTransform(Matrix.CreateTranslation(text.X, text.Y));

        var rows = Math.Max(0, (int)(text.Height / _cell.Height));
        ClampViewport();
        var top = _viewportTop;
        for (var row = 0; row < rows; row++)
        {
            var cells = document.GetLine(top + row);
            if (cells.IsEmpty) continue;
            PaintRow(context, cells, row, top + row, foreground, background, text.Width);
        }

        if (CursorVisible) PaintCursor(context, document, foreground, text);
    }

    /// <summary>
    /// One row, as runs of cells that share a style.
    /// </summary>
    /// <remarks>
    /// A background is filled only when it differs from the screen's, which has already been
    /// painted -- most of a terminal is default-on-default and filling it again is the single
    /// largest waste available here.
    /// </remarks>
    private void PaintRow(
        DrawingContext context,
        ReadOnlySpan<VtCell> cells,
        int row,
        long lineNumber,
        Color defaultForeground,
        Color defaultBackground,
        double width)
    {
        var y = row * _cell.Height;
        var column = 0;
        var anySelected = HasSelection;
        while (column < cells.Length)
        {
            var start = column;
            var style = cells[column];
            // A selection boundary ends a run as a style change does: the selected half is drawn
            // inverted and the rest is not.
            var selected = anySelected && IsSelected(lineNumber, column);
            while (column < cells.Length && cells[column].SameStyle(style) &&
                (!anySelected || IsSelected(lineNumber, column) == selected))
            {
                column++;
            }

            var bounds = new Rect(
                start * _cell.Width, y, (column - start) * _cell.Width, _cell.Height);
            if (bounds.Left >= width) return;

            var (foreground, background) = VtPalette.Resolve(
                style, ToRgb(defaultForeground), ToRgb(defaultBackground), selected);

            if (!ToColor(background).Equals(defaultBackground))
            {
                context.FillRectangle(new SolidColorBrush(ToColor(background)), bounds);
            }

            if (style.Flags.HasFlag(VtCellFlags.Hidden)) continue;

            DrawRun(context, cells[start..column], bounds, ToColor(foreground), style.Flags);
        }
    }

    /// <summary>
    /// The text of one run, and the lines struck through or under it.
    /// </summary>
    /// <remarks>
    /// A run of spaces is skipped: the background is already right and there is nothing to see.
    /// That is most of a terminal.
    /// </remarks>
    private void DrawRun(
        DrawingContext context,
        ReadOnlySpan<VtCell> run,
        Rect bounds,
        Color foreground,
        VtCellFlags flags)
    {
        var builder = new System.Text.StringBuilder(run.Length);
        foreach (var cell in run)
        {
            // The right half of a double-width character is a placeholder and is never drawn; the
            // left half already occupies both columns.
            if (cell.Flags.HasFlag(VtCellFlags.WideTrailing)) continue;
            builder.Append(char.ConvertFromUtf32(cell.Codepoint));
        }

        var text = builder.ToString();
        var brush = new SolidColorBrush(foreground);
        if (text.Length > 0 && !text.All(static character => character == ' '))
        {
            var formatted = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                TypefaceFor(flags),
                FontSize,
                brush);
            context.DrawText(formatted, bounds.TopLeft);
        }

        DrawDecorations(context, bounds, brush, flags);
    }

    /// <summary>Underline and strike-through, which are lines rather than a font.</summary>
    private void DrawDecorations(DrawingContext context, Rect bounds, IBrush brush, VtCellFlags flags)
    {
        var thickness = Math.Max(1, Math.Round(FontSize / 14));
        var pen = new Pen(brush, thickness);

        if (flags.HasFlag(VtCellFlags.Underline))
        {
            var y = bounds.Bottom - thickness;
            context.DrawLine(pen, new Point(bounds.Left, y), new Point(bounds.Right, y));
        }

        if (flags.HasFlag(VtCellFlags.Strike))
        {
            var y = bounds.Top + (bounds.Height / 2);
            context.DrawLine(pen, new Point(bounds.Left, y), new Point(bounds.Right, y));
        }
    }

    /// <summary>
    /// The cursor, as a block over the cell it is on.
    /// </summary>
    /// <remarks>
    /// Drawn over the text rather than under it, and inverted, so the character beneath stays
    /// readable instead of being hidden by its own cursor.
    /// </remarks>
    private void PaintCursor(
        DrawingContext context, VtTerminalDocument document, Color foreground, Rect text)
    {
        var row = (int)(document.CursorLineNumber - _viewportTop);
        if (row < 0 || row * _cell.Height >= text.Height) return;

        var bounds = new Rect(
            document.Screen.CursorColumn * _cell.Width,
            row * _cell.Height,
            _cell.Width,
            _cell.Height);

        using (context.PushOpacity(0.7))
        {
            context.FillRectangle(new SolidColorBrush(foreground), bounds);
        }
    }

    /// <summary>
    /// Measures a cell, once per font size.
    /// </summary>
    /// <remarks>
    /// Across a run of characters rather than one, for the rounding reason above. The typeface is
    /// whatever the theme's monospace family resolves to; a proportional fallback would shear
    /// every column after the first wide glyph, which is why the family is asked for by name.
    /// </remarks>
    private void Measure()
    {
        if (_cell.Width > 0 && _typeface != default) return;

        _typeface = new Typeface(FontFamily.Parse("Cascadia Mono, Consolas, DejaVu Sans Mono, monospace"));
        var sample = new FormattedText(
            WidthSample,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _typeface,
            FontSize,
            Brushes.Black);

        _cell = new Size(
            Math.Max(1, sample.Width / WidthSample.Length),
            Math.Max(1, sample.Height));
    }

    /// <summary>Bold and italic are the font's business; the rest are drawn.</summary>
    private Typeface TypefaceFor(VtCellFlags flags) => new(
        _typeface.FontFamily,
        flags.HasFlag(VtCellFlags.Italic) ? FontStyle.Italic : FontStyle.Normal,
        flags.HasFlag(VtCellFlags.Bold) ? FontWeight.Bold : FontWeight.Normal);

    private Color Resolve(string key, Color fallback) =>
        this.TryFindResource(key, out var value) && value is Color colour ? colour : fallback;

    private static VtRgb ToRgb(Color colour) => new(colour.R, colour.G, colour.B);

    private static Color ToColor(VtRgb colour) =>
        Color.FromRgb((byte)colour.Red, (byte)colour.Green, (byte)colour.Blue);
}
