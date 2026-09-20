using Avalonia;
using Avalonia.Controls;
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
/// </remarks>
internal sealed class TerminalView : Control
{
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
        InvalidateVisual();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        ReportGridSize(arranged);
        return arranged;
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
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Session is not { } session) return;

        var modifiers = e.KeyModifiers;
        var control = modifiers.HasFlag(KeyModifiers.Control);
        var shift = modifiers.HasFlag(KeyModifiers.Shift);

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
        var top = document.ScreenTopLineNumber;
        for (var row = 0; row < rows; row++)
        {
            var cells = document.GetLine(top + row);
            if (cells.IsEmpty) continue;
            PaintRow(context, cells, row, foreground, background, text.Width);
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
        Color defaultForeground,
        Color defaultBackground,
        double width)
    {
        var y = row * _cell.Height;
        var column = 0;
        while (column < cells.Length)
        {
            var start = column;
            var style = cells[column];
            while (column < cells.Length && cells[column].SameStyle(style)) column++;

            var bounds = new Rect(
                start * _cell.Width, y, (column - start) * _cell.Width, _cell.Height);
            if (bounds.Left >= width) return;

            var (foreground, background) = VtPalette.Resolve(
                style, ToRgb(defaultForeground), ToRgb(defaultBackground), selected: false);

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
        var row = (int)(document.CursorLineNumber - document.ScreenTopLineNumber);
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
