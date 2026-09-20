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
internal sealed class ShellCommand(string id, Action<string> invoke) : ICommand
{
    internal string Id { get; } = id;

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => invoke(Id);

    /// <remarks>
    /// Every command is currently always enabled, so there is nothing to raise. When availability
    /// becomes dynamic - a paste with an empty clipboard, a disconnect with nothing connected -
    /// this grows a real implementation rather than the view growing a refresh loop.
    /// </remarks>
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }
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
    private readonly HashSet<TopLevel> _attached = [];

    internal ShellCommandRouter()
    {
        foreach (var definition in UiCommandCatalog.Definitions)
        {
            _commands[definition.Id] = new ShellCommand(definition.Id, Invoke);
        }
    }

    /// <summary>Raised when a command fires, however it was invoked.</summary>
    internal event EventHandler<string>? Invoked;

    /// <summary>The most recent command, which the status bar shows while nothing is wired yet.</summary>
    internal string? LastInvoked { get; private set; }

    internal ShellCommand For(string id) => _commands[id];

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
    }
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
