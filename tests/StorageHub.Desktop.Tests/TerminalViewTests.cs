using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The terminal painter, checked by looking at the pixels it produced.
/// </summary>
/// <remarks>
/// <para>
/// Everything behind it is tested in Desktop.Core without a screen: the emulator parses, the
/// document holds, <see cref="VtPalette"/> decides the colours. What only a render can tell you is
/// whether any of that reached the control -- a painter that draws nothing at all satisfies every
/// assertion that stops short of the bitmap.
/// </para>
/// <para>
/// So these feed real escape sequences through the real emulator and then read the frame.
/// </para>
/// </remarks>
public class TerminalViewTests
{
    [AvaloniaFact]
    public void AGridOfCharactersHasACellSize()
    {
        var view = Rendered(Emulator("hello"));

        Assert.True(view.CellSize.Width > 0);
        Assert.True(view.CellSize.Height > 0);

        // A cell is taller than it is wide in every monospace face worth using; a square cell
        // means the sample was measured wrong.
        Assert.True(
            view.CellSize.Height > view.CellSize.Width,
            $"cell is {view.CellSize.Width} x {view.CellSize.Height}");
    }

    /// <summary>The grid a window holds is what the session gets resized to.</summary>
    [AvaloniaFact]
    public void TheGridSizeFollowsTheArea()
    {
        var view = Rendered(Emulator(string.Empty));

        var (columns, rows) = view.GridSize(new Size(800, 400));
        var (wider, taller) = view.GridSize(new Size(1600, 800));

        Assert.True(columns > 0 && rows > 0);
        Assert.True(wider > columns);
        Assert.True(taller > rows);
    }

    /// <summary>A control measured to nothing still has to describe a terminal.</summary>
    [AvaloniaFact]
    public void AnEmptyAreaStillHasOneCell()
    {
        var view = Rendered(Emulator(string.Empty));

        var (columns, rows) = view.GridSize(new Size(0, 0));

        Assert.Equal(1, columns);
        Assert.Equal(1, rows);
    }

    /// <summary>
    /// Text written to the terminal reaches the screen.
    /// </summary>
    /// <remarks>
    /// The weakest possible claim, and the one that catches a painter wired to nothing: a frame
    /// with writing in it differs from a frame without.
    /// </remarks>
    [AvaloniaFact]
    public void WritingTextChangesWhatIsDrawn()
    {
        var blank = Capture(Rendered(Emulator(string.Empty)));
        var written = Capture(Rendered(Emulator("hello world")));

        Assert.NotEqual(blank, written);
    }

    /// <summary>
    /// A colour set by an escape sequence is the colour on screen.
    /// </summary>
    /// <remarks>
    /// Red is ANSI index 1, which VtPalette draws as its own shade rather than pure red -- so this
    /// asserts the palette reached the painter, not that some red pixel exists.
    /// </remarks>
    [AvaloniaFact]
    public void AnAnsiColourReachesThePixels()
    {
        var view = Rendered(Emulator("\u001b[31mRED\u001b[0m"));

        var expected = VtPalette.Ansi[1];
        Assert.Contains(
            Pixels(view),
            pixel => Close(pixel, expected));
    }

    /// <summary>And a 24-bit colour, which does not go through the palette at all.</summary>
    [AvaloniaFact]
    public void ATrueColourReachesThePixels()
    {
        var view = Rendered(Emulator("\u001b[38;2;0;255;0mGREEN\u001b[0m"));

        Assert.Contains(Pixels(view), pixel => Close(pixel, new VtRgb(0, 255, 0)));
    }

    /// <summary>
    /// A background set on a run is filled behind it.
    /// </summary>
    /// <remarks>
    /// The painter skips a fill when the background matches the screen's, which is most of a
    /// terminal. This is the case where it must not skip.
    /// </remarks>
    [AvaloniaFact]
    public void ABackgroundColourIsFilled()
    {
        var view = Rendered(Emulator("\u001b[44m          \u001b[0m"));

        Assert.Contains(Pixels(view), pixel => Close(pixel, VtPalette.Ansi[4]));
    }

    /// <summary>Hiding the cursor changes the frame, which is the only way to see it drawn.</summary>
    [AvaloniaFact]
    public void TheCursorIsDrawnWhenItIsVisible()
    {
        var shown = Rendered(Emulator("x"));
        var hidden = Rendered(Emulator("x"), cursorVisible: false);

        Assert.NotEqual(Capture(shown), Capture(hidden));
    }

    /// <summary>A painter with nothing to draw draws the screen background and does not throw.</summary>
    [AvaloniaFact]
    public void NoDocumentIsNotAFailure()
    {
        var view = new TerminalView();
        var window = Show(view);

        Assert.Null(view.Document);
        Assert.NotNull(window.CaptureRenderedFrame());
    }

    /// <summary>
    /// Photographs a terminal, for a human to look at.
    /// </summary>
    /// <remarks>
    /// Set STORAGEHUB_SHOT_DIR to keep it. Colours, weights and the cursor are the sort of thing
    /// that passes every assertion and still looks wrong.
    /// </remarks>
    [AvaloniaFact]
    public void ATerminalCanBePhotographed()
    {
        var view = Rendered(Emulator(
            "\u001b[32mclaus@storagehub\u001b[0m:\u001b[34m~/projects\u001b[0m$ ls -la\r\n" +
            "total 48\r\n" +
            "\u001b[34mdrwxr-xr-x\u001b[0m  6 claus claus  4096 Sep 20 18:02 \u001b[1;34m.\u001b[0m\r\n" +
            "-rw-r--r--  1 claus claus  1104 Sep 20 17:58 README.md\r\n" +
            "\u001b[31merror\u001b[0m: \u001b[1mbold\u001b[0m \u001b[4munderlined\u001b[0m " +
            "\u001b[7minverse\u001b[0m \u001b[2mdim\u001b[0m\r\n" +
            "\u001b[38;2;255;170;0mtruecolour\u001b[0m and \u001b[48;5;27mindexed background\u001b[0m\r\n"));

        var frame = Capture(view);
        Assert.NotNull(frame);

        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;

        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, "terminal.png"));
        Window(view).CaptureRenderedFrame()!.Save(stream, new PngBitmapEncoderOptions());
    }

    /// <summary>An emulator that has been fed some output, through the real parser.</summary>
    private static VtTerminalEmulator Emulator(string output)
    {
        var emulator = new VtTerminalEmulator(80, 24);
        if (output.Length > 0) emulator.Feed(output);
        return emulator;
    }

    private static TerminalView Rendered(VtTerminalEmulator emulator, bool cursorVisible = true)
    {
        var view = new TerminalView
        {
            Document = emulator.Document,
            CursorVisible = cursorVisible
        };
        _ = Show(view);
        return view;
    }

    private static readonly Dictionary<TerminalView, Window> Windows = [];

    private static Window Show(TerminalView view)
    {
        var window = new Window { Content = view, Width = 720, Height = 400 };
        window.Show();
        window.Measure(new Size(720, 400));
        window.Arrange(new Rect(0, 0, 720, 400));
        window.UpdateLayout();
        Windows[view] = window;
        return window;
    }

    private static Window Window(TerminalView view) => Windows[view];

    private static byte[] Capture(TerminalView view)
    {
        using var frame = Window(view).CaptureRenderedFrame()!;
        using var stream = new MemoryStream();
        frame.Save(stream, new PngBitmapEncoderOptions());
        return stream.ToArray();
    }

    /// <summary>Every pixel of the rendered frame, as colours.</summary>
    private static IEnumerable<VtRgb> Pixels(TerminalView view)
    {
        using var frame = Window(view).CaptureRenderedFrame()!;
        var width = frame.PixelSize.Width;
        var height = frame.PixelSize.Height;
        var buffer = new byte[width * height * 4];

        // Through a pinned handle rather than a fixed block, so the suite needs no /unsafe.
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(
            buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            frame.CopyPixels(
                new PixelRect(0, 0, width, height),
                handle.AddrOfPinnedObject(),
                buffer.Length,
                width * 4);
        }
        finally
        {
            handle.Free();
        }

        for (var index = 0; index + 3 < buffer.Length; index += 4)
        {
            // Rgba, which is what a headless frame comes back as here -- reading it as Bgra makes
            // every assertion fail against a colour that is its own reverse.
            yield return new VtRgb(buffer[index], buffer[index + 1], buffer[index + 2]);
        }
    }

    /// <summary>
    /// Whether two colours are the same, allowing for text antialiasing.
    /// </summary>
    /// <remarks>
    /// A glyph's interior is the exact colour; its edges are blended with the background. Matching
    /// exactly would work for a filled rectangle and fail for every letter.
    /// </remarks>
    private static bool Close(VtRgb pixel, VtRgb expected) =>
        Math.Abs(pixel.Red - expected.Red) <= 12 &&
        Math.Abs(pixel.Green - expected.Green) <= 12 &&
        Math.Abs(pixel.Blue - expected.Blue) <= 12;
}
