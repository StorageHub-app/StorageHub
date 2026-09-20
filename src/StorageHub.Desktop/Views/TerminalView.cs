using Avalonia;
using Avalonia.Controls;
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

    private Typeface _typeface;
    private Size _cell;

    static TerminalView()
    {
        AffectsRender<TerminalView>(DocumentProperty, CursorVisibleProperty);
        AffectsMeasure<TerminalView>(FontSizeProperty);
    }

    public TerminalView() => ClipToBounds = true;

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
        return (
            Math.Max(1, (int)(available.Width / _cell.Width)),
            Math.Max(1, (int)(available.Height / _cell.Height)));
    }

    public override void Render(DrawingContext context)
    {
        Measure();
        var background = Resolve("TerminalBackgroundColor", Color.FromRgb(15, 23, 42));
        var foreground = Resolve("TerminalForegroundColor", Color.FromRgb(226, 232, 240));
        context.FillRectangle(new SolidColorBrush(background), new Rect(Bounds.Size));

        if (Document is not { } document) return;

        var rows = Math.Max(0, (int)(Bounds.Height / _cell.Height));
        var top = document.ScreenTopLineNumber;
        for (var row = 0; row < rows; row++)
        {
            var cells = document.GetLine(top + row);
            if (cells.IsEmpty) continue;
            PaintRow(context, cells, row, foreground, background);
        }

        if (CursorVisible) PaintCursor(context, document, foreground);
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
        Color defaultBackground)
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
            if (bounds.Left >= Bounds.Width) return;

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
    private void PaintCursor(DrawingContext context, VtTerminalDocument document, Color foreground)
    {
        var row = (int)(document.CursorLineNumber - document.ScreenTopLineNumber);
        if (row < 0 || row * _cell.Height >= Bounds.Height) return;

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
