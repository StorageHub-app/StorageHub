using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace StorageHub.Desktop.Views;

/// <summary>A command, identified by the catalog id it came from.</summary>
/// <remarks>
/// One implementation for every entry rather than one per command: nothing about invoking differs
/// between them, and the shell's job here is to say which id fired. What each id then does arrives
/// per screen, as the handlers move out of MainForm.
/// </remarks>
internal sealed class ShellCommand(string id, Action<string> invoke, Func<string, bool> canInvoke)
    : ICommand
{
    internal string Id { get; } = id;

    /// <summary>
    /// Whether this command has somewhere to go.
    /// </summary>
    /// <remarks>
    /// It used to return true unconditionally, and the result was a menu that could not be trusted:
    /// thirty-one entries looked enabled and did nothing when pressed. The catalog already dims the
    /// twenty-five commands 1.x never wired; this extends the same honesty to the ones this shell
    /// has not reached yet, and turns the menu into an accurate account of what is built.
    /// </remarks>
    public bool CanExecute(object? parameter) => canInvoke(Id);

    public void Execute(object? parameter) => invoke(Id);

    /// <summary>
    /// Raised when the command gains a handler, so a menu drawn before it did catches up.
    /// </summary>
    /// <remarks>
    /// Handlers are registered after the shell is built -- some of them by a window that does not
    /// exist yet -- so an entry bound at startup would otherwise stay dim for the life of the
    /// process.
    /// </remarks>
    public event EventHandler? CanExecuteChanged;

    internal void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Turns a keystroke into a command, under the rules the WinForms shell already established.
/// </summary>
/// <remarks>
/// This is the port's least visible behavioural difference and the most likely to be noticed.
/// <c>MainForm.ProcessCmdKey</c> saw every key <em>before</em> any control did, whereas Avalonia's
/// <c>Window.KeyBindings</c> run <em>after</em> the focused control has had it. Binding the shell's
/// shortcuts the ordinary way would therefore change behaviour: Ctrl+C would stop reaching the
/// shell's Copy while a pane filter box had focus.
///
/// So the handler tunnels instead, which restores the old ordering exactly, and then applies the
/// same focus rules the old shell did - <see cref="UiCommandCatalog.CanDispatch"/>, unchanged and
/// now shared by both shells. A shortcut typed into a text box is still declined; the difference is
/// that declining is now a decision this class makes, rather than an accident of routing.
/// </remarks>
internal sealed class ShellCommandRouter
{
    private readonly Dictionary<string, ShellCommand> _commands = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Action> _handlers = new(StringComparer.Ordinal);
    private readonly HashSet<TopLevel> _attached = [];

    internal ShellCommandRouter()
    {
        foreach (var definition in UiCommandCatalog.Definitions)
        {
            _commands[definition.Id] = new ShellCommand(definition.Id, Invoke, IsHandled);
        }
    }

    /// <summary>Raised when a command fires, however it was invoked.</summary>
    internal event EventHandler<string>? Invoked;

    /// <summary>The most recent command, which the status bar shows while nothing is wired yet.</summary>
    internal string? LastInvoked { get; private set; }

    internal ShellCommand For(string id) => _commands[id];

    /// <summary>
    /// Says what a command actually does.
    /// </summary>
    /// <remarks>
    /// Registered rather than switched on, so a screen owns its own commands and the router keeps
    /// knowing nothing about them. An id with no handler still raises <see cref="Invoked"/>, which
    /// is what puts it in the status bar - the stand-in that proves the path works while the rest
    /// of the handlers are still in MainForm.
    /// </remarks>
    internal void Handle(string id, Action handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[id] = handler;

        // The entry may already be on screen and dim, so it is told it can go now.
        if (_commands.TryGetValue(id, out var command)) command.RaiseCanExecuteChanged();
    }

    /// <summary>How many commands have a handler, which is the port's own progress meter.</summary>
    internal int HandledCount => _handlers.Count;

    /// <summary>Whether an id has somewhere to go, for a test and for a menu that dims.</summary>
    internal bool IsHandled(string id) => _handlers.ContainsKey(id);

    /// <summary>Attaches to a window, tunnelling so the shell sees a key before the focus does.</summary>
    internal void Attach(TopLevel topLevel)
    {
        ArgumentNullException.ThrowIfNull(topLevel);

        // A DataContext can be assigned more than once, and handlers do not deduplicate themselves.
        if (!_attached.Add(topLevel)) return;

        topLevel.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Dispatches a gesture, or declines and says nothing happened.
    /// </summary>
    /// <remarks>
    /// Kept separate from the event handler so the rules can be tested without a keyboard, which is
    /// how the WinForms shell's shortcut tests were written too.
    /// </remarks>
    internal bool TryDispatch(KeyGesture gesture, ShellFocusContext focus)
    {
        ArgumentNullException.ThrowIfNull(gesture);

        var command = UiCommandCatalog.Definitions.FirstOrDefault(definition =>
            definition.Shortcut is { } shortcut &&
            shortcut.Equals(gesture) &&
            UiCommandCatalog.IsAvailable(definition.Id));

        if (command is null) return false;

        // A pane command with the active pane on an SSH connection is the one case the old shell
        // refused outright rather than routing, because the keystroke belongs to the remote shell.
        if (UiCommandCatalog.IsPaneCommand(command) && focus.IsSshPane) return false;

        if (!UiCommandCatalog.CanDispatch(command, focus.IsSshFocused, focus.IsTextFocused, focus.HasPane))
            return false;

        Invoke(command.Id);
        return true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            return;
        }

        var focus = ShellFocusContext.Describe(sender as TopLevel);
        if (TryDispatch(new KeyGesture(e.Key, e.KeyModifiers), focus)) e.Handled = true;
    }

    private void Invoke(string id)
    {
        LastInvoked = id;
        Invoked?.Invoke(this, id);
        if (_handlers.TryGetValue(id, out var handler))
        {
            handler();
            return;
        }

        // Reachable through a shortcut, which does not consult CanExecute the way a menu does. It
        // leaves a trace rather than doing nothing silently, because a shortcut that appears to be
        // ignored is indistinguishable from one that is not bound.
        Unhandled?.Invoke(this, id);
    }

    /// <summary>Raised when a command with no handler was invoked anyway.</summary>
    internal event EventHandler<string>? Unhandled;
}

/// <summary>What has focus, as far as shortcut dispatch is concerned.</summary>
/// <param name="IsTextFocused">
/// Focus is somewhere that consumes typing. Without this a plain letter bound to a command would
/// fire while somebody was typing into a filter box.
/// </param>
/// <param name="IsSshFocused">Focus is a remote shell, which owns every key it is sent.</param>
/// <param name="HasPane">There is an active browser pane for a pane command to act on.</param>
/// <param name="IsSshPane">That pane is an SSH connection.</param>
internal readonly record struct ShellFocusContext(
    bool IsTextFocused,
    bool IsSshFocused,
    bool HasPane,
    bool IsSshPane)
{
    internal static ShellFocusContext Describe(TopLevel? topLevel)
    {
        if (topLevel?.FocusManager?.GetFocusedElement() is not Visual focused)
        {
            return new ShellFocusContext(false, false, HasPane: true, IsSshPane: false);
        }

        // The WinForms shell walked the parent chain naming the controls that host a text box,
        // because a drop-down in list mode takes focus itself and is none of them. The Avalonia
        // equivalent is the same walk over the visual tree - the stock controls below all host an
        // editable surface, and TerminalView will join them when the terminal is ported.
        var textFocused = focused.GetSelfAndVisualAncestors().Any(visual =>
            visual is TextBox or ComboBox or AutoCompleteBox or NumericUpDown);

        return new ShellFocusContext(textFocused, IsSshFocused: false, HasPane: true, IsSshPane: false);
    }
}
