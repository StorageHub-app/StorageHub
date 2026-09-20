using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Shortcut dispatch, and the focus rules that decide when a shortcut is not one.
/// </summary>
/// <remarks>
/// The plan called this the port's least visible behavioural difference and the most likely to
/// generate bug reports. MainForm.ProcessCmdKey saw a key before any control did; Avalonia's
/// KeyBindings see it after. The router tunnels to restore the old ordering and then applies the
/// old shell's own rules, so these tests are about the rules rather than the plumbing.
/// </remarks>
public class ShellCommandRouterTests
{
    private static ShellFocusContext Shell => new(
        IsTextFocused: false, IsSshFocused: false, HasPane: true, IsSshPane: false);

    [Fact]
    public void AShortcutRunsItsCommand()
    {
        var router = new ShellCommandRouter();
        string? fired = null;
        router.Invoked += (_, id) => fired = id;

        Assert.True(router.TryDispatch(new KeyGesture(Key.T, KeyModifiers.Control), Shell));
        Assert.Equal(UiCommandIds.WorkspaceNewWorkspace, fired);
    }

    [Fact]
    public void AnUnboundGestureDoesNothing()
    {
        var router = new ShellCommandRouter();
        Assert.False(router.TryDispatch(new KeyGesture(Key.Q, KeyModifiers.Control), Shell));
        Assert.Null(router.LastInvoked);
    }

    /// <summary>The rule that keeps Ctrl+C in a filter box from being the shell's Copy.</summary>
    [Fact]
    public void AShortcutDoesNotFireWhileTypeIsBeingTyped()
    {
        var router = new ShellCommandRouter();
        var typing = Shell with { IsTextFocused = true };

        Assert.False(router.TryDispatch(new KeyGesture(Key.C, KeyModifiers.Control), typing));
        Assert.Null(router.LastInvoked);
    }

    [Fact]
    public void AShortcutDoesNotReachThroughARemoteShell()
    {
        var router = new ShellCommandRouter();
        var ssh = Shell with { IsSshFocused = true };

        Assert.False(router.TryDispatch(new KeyGesture(Key.F5), ssh));
    }

    /// <summary>A pane command with no pane has nothing to act on.</summary>
    [Fact]
    public void APaneCommandNeedsAPane()
    {
        var router = new ShellCommandRouter();
        var refresh = new KeyGesture(Key.F5);

        Assert.True(router.TryDispatch(refresh, Shell));
        Assert.False(router.TryDispatch(refresh, Shell with { HasPane = false }));
    }

    [Fact]
    public void APaneCommandDoesNotStealAKeyFromAnSshPane()
    {
        var router = new ShellCommandRouter();
        Assert.False(router.TryDispatch(new KeyGesture(Key.F5), Shell with { IsSshPane = true }));
    }

    /// <summary>Dispatch keys on the same catalog the menu displays, so the two cannot disagree.</summary>
    [Fact]
    public void EveryDisplayedShortcutIsDispatchable()
    {
        var router = new ShellCommandRouter();

        var undispatchable = UiCommandCatalog.Definitions
            .Where(definition => definition.Shortcut is not null)
            .Where(definition => UiCommandCatalog.IsAvailable(definition.Id))
            .Where(definition => !UiCommandCatalog.IsPaneCommand(definition))
            .Where(definition => !router.TryDispatch(definition.Shortcut!, Shell))
            .Select(definition => definition.Id)
            .ToList();

        Assert.Empty(undispatchable);
    }

    /// <summary>The whole path, through a real window: keystroke to command.</summary>
    [AvaloniaFact]
    public void TypingAShortcutIntoTheWindowRunsTheCommand()
    {
        var window = new MainWindow { DataContext = ShellPreview.Sample };
        window.Show();

        window.KeyPressQwerty(PhysicalKey.T, RawInputModifiers.Control);

        Assert.Equal(UiCommandIds.WorkspaceNewWorkspace, ShellPreview.Sample.Router.LastInvoked);
    }

    /// <summary>
    /// The same keystroke, with a text box focused, must not reach the shell.
    /// </summary>
    /// <remarks>
    /// This is the case that would have changed behaviour silently. It passes because the router
    /// tunnels and then declines, not because Avalonia happened to route the key elsewhere.
    /// </remarks>
    [AvaloniaFact]
    public void TypingAShortcutIntoASearchBoxDoesNotRunTheCommand()
    {
        var window = new MainWindow { DataContext = ShellPreview.Sample };
        window.Show();
        window.Measure(new Size(1500, 920));
        window.Arrange(new Rect(0, 0, 1500, 920));

        var search = window.GetVisualDescendants().OfType<TextBox>().First();
        search.Focus();

        var before = ShellPreview.Sample.Router.LastInvoked;
        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);

        Assert.Equal(before, ShellPreview.Sample.Router.LastInvoked);
    }

    /// <summary>Clicking a menu entry runs the same command the shortcut would.</summary>
    [AvaloniaFact]
    public void AMenuEntryRunsItsCommand()
    {
        var model = ShellPreview.Sample;
        var entry = model.Menus
            .SelectMany(section => section.Items)
            .First(item => item.Id == UiCommandIds.ViewRefresh);

        entry.Command.Execute(null);

        Assert.Equal(UiCommandIds.ViewRefresh, model.Router.LastInvoked);

        // The status bar used to print the id of whatever was invoked, which was the stand-in that
        // proved the path worked while nothing was wired. It says something now only when a
        // command has nowhere to go, and view.refresh has somewhere.
        Assert.DoesNotContain(UiCommandIds.ViewRefresh, model.Status, StringComparison.Ordinal);
    }
}
