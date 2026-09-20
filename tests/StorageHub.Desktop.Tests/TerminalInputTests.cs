using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The control's half of a terminal: focus, keys, and telling the session how big the window is.
/// </summary>
/// <remarks>
/// <see cref="SshTerminalSessionTests"/> covers the other half against a fake agent. What is left
/// for here is everything that only happens because there is a window: that a click puts focus in
/// the terminal, that a keystroke is offered to the session before anything else claims it, and
/// that a layout pass reports a grid rather than a pixel count.
/// </remarks>
public class TerminalInputTests
{
    [AvaloniaFact]
    public void AClickPutsFocusInTheTerminal()
    {
        var (window, view, _) = Shown();

        window.MouseDown(new Point(20, 20), MouseButton.Left);

        Assert.True(view.IsFocused);
    }

    [AvaloniaFact]
    public void TypingGoesToTheSession()
    {
        var (window, view, session) = Shown();
        view.Focus();

        window.KeyTextInput("ls");

        Assert.Equal("ls", session.Text);
    }

    /// <summary>
    /// A key the terminal wants is not left for a shortcut to claim.
    /// </summary>
    /// <remarks>
    /// The whole point of a focused terminal: Ctrl+L clears a remote screen rather than focusing an
    /// address bar. Marking the event handled is what stops the shell competing for it.
    /// </remarks>
    [AvaloniaFact]
    public void AKeyTheTerminalWantsIsMarkedHandled()
    {
        var (window, view, session) = Shown();
        view.Focus();

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.Equal([(Key.Enter, false, false, false)], session.Keys);
    }

    /// <summary>
    /// And a key it does not want is left alone.
    /// </summary>
    /// <remarks>
    /// A terminal that swallowed everything would trap focus: Tab would never move on, and no
    /// shortcut would work while a shell was open.
    /// </remarks>
    [AvaloniaFact]
    public void AKeyTheTerminalDeclinesIsNotSwallowed()
    {
        var (window, view, session) = Shown();
        session.Accept = false;
        view.Focus();

        var handled = false;
        view.AddHandler(
            InputElement.KeyDownEvent,
            (object? _, KeyEventArgs e) => handled = e.Handled,
            Avalonia.Interactivity.RoutingStrategies.Bubble);

        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);

        Assert.False(handled);
    }

    /// <summary>
    /// Being shown a session tells it how large the window is.
    /// </summary>
    /// <remarks>
    /// The session opens at a default grid because it cannot wait for a layout pass to connect.
    /// This is the correction, and without it a remote program wraps its output at eighty columns
    /// inside a window that is not eighty columns wide.
    /// </remarks>
    [AvaloniaFact]
    public void ASessionIsToldTheGridItGot()
    {
        var (_, view, session) = Shown();

        var (columns, rows) = view.GridSize(view.Bounds.Size);

        Assert.Equal([(columns, rows)], session.Resizes);
    }

    /// <summary>
    /// A layout pass that did not change the grid says nothing.
    /// </summary>
    /// <remarks>
    /// Arrange runs for reasons that have nothing to do with size, and every resize sent becomes a
    /// window-size signal on the remote host. A shell that redraws its prompt for each one would
    /// spend a drag redrawing.
    /// </remarks>
    [AvaloniaFact]
    public void ALayoutPassThatChangedNothingIsNotReported()
    {
        var (window, _, session) = Shown();

        window.Measure(new Size(720, 400));
        window.Arrange(new Rect(0, 0, 720, 400));
        window.UpdateLayout();

        Assert.Single(session.Resizes);
    }

    /// <summary>And one that did change it reports the new grid, once.</summary>
    [AvaloniaFact]
    public void AResizedWindowReportsTheNewGrid()
    {
        var (window, view, session) = Shown();
        var first = session.Resizes[0];

        // The control's own size, rather than the window's: a headless window keeps the client size
        // it was shown at, so shrinking it never reaches the control being tested.
        view.Width = 300;
        view.Height = 200;
        window.UpdateLayout();

        Assert.Equal(2, session.Resizes.Count);
        Assert.NotEqual(first, session.Resizes[1]);
        Assert.Equal(view.GridSize(new Size(300, 200)), session.Resizes[1]);
    }

    /// <summary>Output redraws the control, which is the only reason the session raises it.</summary>
    [AvaloniaFact]
    public void OutputFromTheSessionRedrawsTheScreen()
    {
        var (window, _, session) = Shown();
        var before = Capture(window);

        session.Write("hello world");

        Assert.NotEqual(before, Capture(window));
    }

    /// <summary>
    /// A cursor the remote program hid stays hidden.
    /// </summary>
    /// <remarks>
    /// Full-screen programs hide the cursor while they redraw. A painter that ignored that would
    /// leave a block flickering across the screen through every frame of a redraw.
    /// </remarks>
    [AvaloniaFact]
    public void HidingTheCursorFollowsTheSession()
    {
        var (_, view, session) = Shown();

        session.Write("\u001b[?25l");

        Assert.False(view.CursorVisible);
    }

    /// <summary>
    /// Being given a different session forgets the one before it.
    /// </summary>
    /// <remarks>
    /// A pane can be re-pointed at another connection. A control still listening to the old session
    /// would redraw the new screen whenever the old one said anything.
    /// </remarks>
    [AvaloniaFact]
    public void ReplacingTheSessionStopsFollowingTheOldOne()
    {
        var (_, view, first) = Shown();
        var second = new RecordingSession();
        view.Session = second;

        first.Write("from the old session");

        Assert.Same(second.Document, view.Document);
        Assert.DoesNotContain("from the old", Text(view));
    }

    private static (Window Window, TerminalView View, RecordingSession Session) Shown()
    {
        var session = new RecordingSession();
        var view = new TerminalView { Session = session };
        var window = new Window { Content = view, Width = 720, Height = 400 };
        window.Show();
        window.Measure(new Size(720, 400));
        window.Arrange(new Rect(0, 0, 720, 400));
        window.UpdateLayout();
        return (window, view, session);
    }

    private static string Text(TerminalView view) => view.Document is { } document
        ? document.GetText(document.FirstLineNumber, document.TotalLineCount, joinWrapped: true)
        : string.Empty;

    private static byte[] Capture(Window window)
    {
        using var frame = window.CaptureRenderedFrame()!;
        using var stream = new MemoryStream();
        frame.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        return stream.ToArray();
    }

    /// <summary>A session that records rather than connects.</summary>
    private sealed class RecordingSession : ITerminalSession
    {
        private readonly VtTerminalEmulator _emulator = new(80, 24);

        public VtTerminalDocument Document => _emulator.Document;

        public bool CursorVisible => _emulator.CursorVisible;

        public event EventHandler? OutputReceived;

        /// <summary>Whether keys are claimed, which a real session decides per key.</summary>
        internal bool Accept { get; set; } = true;

        internal List<(Key Key, bool Shift, bool Alt, bool Control)> Keys { get; } = [];

        internal List<(int Columns, int Rows)> Resizes { get; } = [];

        internal string Text { get; private set; } = string.Empty;

        /// <summary>Feeds output the way a read from the agent would.</summary>
        internal void Write(string output)
        {
            _emulator.Feed(output);
            OutputReceived?.Invoke(this, EventArgs.Empty);
        }

        public bool SendKey(Key key, bool shift, bool alt, bool control)
        {
            Keys.Add((key, shift, alt, control));
            return Accept;
        }

        public void SendText(string text) => Text += text;

        public void Paste(string text) => Text += text;

        public void Resize(int columns, int rows) => Resizes.Add((columns, rows));
    }
}
