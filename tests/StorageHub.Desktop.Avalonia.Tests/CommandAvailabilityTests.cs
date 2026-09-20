using Avalonia.Headless.XUnit;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Avalonia.Tests;

/// <summary>
/// A command is offered when it has somewhere to go, and dims when it does not.
/// </summary>
/// <remarks>
/// <para>
/// <c>CanExecute</c> returned true unconditionally, so every menu entry looked enabled whether or
/// not anything was behind it -- thirty-one of the thirty-seven the catalog admits did nothing when
/// pressed. That is worse than a missing feature: a menu that cannot be trusted has to be tested by
/// clicking everything.
/// </para>
/// <para>
/// With this, the count of enabled entries is the count of handlers, which makes the menu an
/// accurate account of what the port has actually reached.
/// </para>
/// </remarks>
public class CommandAvailabilityTests
{
    [AvaloniaFact]
    public void ACommandWithNoHandlerIsNotOffered()
    {
        var router = new ShellCommandRouter();

        Assert.False(router.For(UiCommandIds.ToolsSettings).CanExecute(null));

        router.Handle(UiCommandIds.ToolsSettings, static () => { });

        Assert.True(router.For(UiCommandIds.ToolsSettings).CanExecute(null));
    }

    /// <summary>
    /// An entry already on screen catches up when its handler arrives.
    /// </summary>
    /// <remarks>
    /// Handlers are registered after the shell is built, some by a window that does not exist yet.
    /// Without the notification an entry bound at startup would stay dim for the life of the
    /// process, which is the failure mode that makes "just bind CanExecute" not enough.
    /// </remarks>
    [AvaloniaFact]
    public void AnEntryIsToldWhenItsCommandBecomesAvailable()
    {
        var router = new ShellCommandRouter();
        var command = router.For(UiCommandIds.ToolsSettings);
        var raised = 0;
        command.CanExecuteChanged += (_, _) => raised++;

        router.Handle(UiCommandIds.ToolsSettings, static () => { });

        Assert.Equal(1, raised);
        Assert.True(command.CanExecute(null));
    }

    /// <summary>
    /// Every enabled menu entry has a handler, and every handler has an enabled entry.
    /// </summary>
    /// <remarks>
    /// The progress meter for the rest of the port: this number goes up as screens land, and the
    /// day it reaches what the catalog admits is the day the menu is done.
    /// </remarks>
    [AvaloniaFact]
    public void TheEnabledEntriesAreExactlyTheHandledOnes()
    {
        var model = ShellPreview.CreateOnWorkspace();

        var enabled = model.Menus
            .SelectMany(static section => section.Items)
            .Where(static entry => entry.Command.CanExecute(null))
            .Select(static entry => ((ShellCommand)entry.Command).Id)
            .ToArray();

        Assert.Equal(model.Router.HandledCount, enabled.Length);
        Assert.All(enabled, id => Assert.True(
            model.Router.IsHandled(id),
            $"{id} is offered but has no handler."));
    }

    /// <summary>
    /// The toolbar shows what the menu shows, which it did not.
    /// </summary>
    /// <remarks>
    /// The menu has always dropped what <see cref="UiCommandCatalog.IsAvailable"/> refuses; the
    /// toolbar drew every button in the layout, including Search and Compare panes, which 1.x
    /// itself never wired.
    /// </remarks>
    [AvaloniaFact]
    public void TheToolbarOffersNothingTheCatalogRefuses()
    {
        var model = ShellPreview.CreateOnWorkspace();

        var ids = model.Toolbar
            .OfType<CommandEntry>()
            .Select(static entry => ((ShellCommand)entry.Command).Id);

        Assert.All(ids, id => Assert.True(
            UiCommandCatalog.IsAvailable(id),
            $"The toolbar offers {id}, which 1.x never wired."));
    }

    /// <summary>A shortcut reaching something unbuilt says so rather than appearing ignored.</summary>
    [AvaloniaFact]
    public void InvokingAnUnbuiltCommandIsReported()
    {
        var router = new ShellCommandRouter();
        var unhandled = new List<string>();
        router.Unhandled += (_, id) => unhandled.Add(id);

        router.For(UiCommandIds.ToolsChecksums).Execute(null);

        Assert.Equal([UiCommandIds.ToolsChecksums], unhandled);
    }

    /// <summary>And one that is built does not report itself as missing.</summary>
    [AvaloniaFact]
    public void InvokingABuiltCommandReportsNothing()
    {
        var router = new ShellCommandRouter();
        var unhandled = new List<string>();
        var ran = 0;
        router.Unhandled += (_, id) => unhandled.Add(id);
        router.Handle(UiCommandIds.ViewRefresh, () => ran++);

        router.For(UiCommandIds.ViewRefresh).Execute(null);

        Assert.Equal(1, ran);
        Assert.Empty(unhandled);
    }
}
