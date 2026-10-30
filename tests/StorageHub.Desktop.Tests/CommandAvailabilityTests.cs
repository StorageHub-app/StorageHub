using Avalonia.Headless.XUnit;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

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
    private static readonly string[] TransferEntriesWaitingOnTheQueue =
    [
        UiCommandIds.TransferStartQueue,
        UiCommandIds.TransferPauseAll,
        UiCommandIds.TransferResumeAll,
        UiCommandIds.TransferCancelSelected,
    ];

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

        // A handled entry is dimmed only while its condition says there is nothing to act on --
        // Reconnect with no connection open -- so the count is of those that can run now.
        Assert.Equal(
            UiCommandCatalog.MenuLayout.Values.SelectMany(static ids => ids).Count(id => model.Router.CanRun(id)),
            enabled.Length);
        Assert.All(enabled, id => Assert.True(
            model.Router.IsHandled(id),
            $"{id} is offered but has no handler."));
        Assert.All(TransferEntriesWaitingOnTheQueue, id =>
        {
            Assert.True(model.Router.IsHandled(id));
            Assert.DoesNotContain(id, enabled);
        });
    }

    /// <summary>
    /// A pane command dims when the pane would refuse it, in the menu, on the toolbar and for its
    /// shortcut alike, rather than staying lit and doing nothing as 1.x's did.
    /// </summary>
    [AvaloniaFact]
    public void APaneCommandDimsWhenThePaneWouldRefuseIt()
    {
        var model = ShellPreview.CreateOnWorkspace();
        var pane = model.ActivePane()!;

        // A pane just opened has nowhere to go back to and nothing selected to delete.
        Assert.False(pane.BackCommand.CanExecute(null));
        Assert.False(model.Router.For(UiCommandIds.GoBack).CanExecute(null));
        Assert.Equal(pane.DeleteCommand.CanExecute(null), model.Router.For(UiCommandIds.EditDelete).CanExecute(null));
        Assert.Equal(pane.RefreshCommand.CanExecute(null), model.Router.For(UiCommandIds.ViewRefresh).CanExecute(null));

        // On Welcome there is no pane at all, so nothing that acts on one is lit.
        model.SelectedWorkspace = 0;
        Assert.All(
            new[] { UiCommandIds.ViewRefresh, UiCommandIds.EditCopy, UiCommandIds.EditPaste, UiCommandIds.GoUp },
            id => Assert.False(model.Router.For(id).CanExecute(null), id));
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
