using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Services;
using StorageHub.Desktop.Shell;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The X on a workspace tab, which 1.x had and the port had dropped (ui-reference 05).
/// </summary>
public class WorkspaceCloseTests
{
    [AvaloniaFact]
    public void OnlyAWorkspaceTabCarriesACloseButton()
    {
        var model = ShellPreview.CreateOnWorkspace();
        var window = new MainWindow { DataContext = model };
        window.Show();
        window.Measure(new Size(1500, 920));
        window.Arrange(new Rect(0, 0, 1500, 920));
        window.UpdateLayout();

        var closers = window.GetVisualDescendants()
            .OfType<Button>()
            .Where(static button => button.Classes.Contains("tab-close") && button.IsVisible)
            .ToArray();

        Assert.Equal(model.Workspaces.Count(static tab => tab.IsClosable), closers.Length);
        Assert.All(closers, static closer => Assert.True(closer.Command?.CanExecute(closer.CommandParameter)));
    }

    [AvaloniaFact]
    public async Task ClosingAWorkspaceRemovesItsTabAndShowsTheOneBeforeIt()
    {
        var model = ShellPreview.CreateOnWorkspace();
        var workspace = model.Workspaces.Last(static tab => tab.IsClosable);
        model.SelectedWorkspace = model.Workspaces.IndexOf(workspace);
        var before = model.SelectedWorkspace - 1;

        await model.CloseWorkspaceAsync(workspace);

        Assert.DoesNotContain(workspace, model.Workspaces);
        Assert.Equal(before, model.SelectedWorkspace);
    }

    [AvaloniaFact]
    public async Task ThePagesOfTheShellCannotBeClosed()
    {
        var model = ShellPreview.CreateOnWorkspace();
        var page = model.Workspaces.First(static tab => !tab.IsClosable);
        var count = model.Workspaces.Count;

        Assert.False(model.CloseWorkspaceCommand.CanExecute(page));
        await model.CloseWorkspaceAsync(page);

        Assert.Equal(count, model.Workspaces.Count);
    }

    /// <summary>
    /// Closing the window, by its X or by Exit: each changed workspace is asked about, as 1.x's
    /// MainForm did, and Cancel keeps the shell. Once answered, what the shell holds is let go and
    /// then the agent stopped, all before the window goes; a part that fails is logged and does not
    /// skip the rest, and a close that arrives meanwhile neither asks again nor starts it again.
    /// </summary>
    [AvaloniaFact]
    public async Task ClosingTheWindowAsksFirstAndLetsEverythingGoBeforeItCloses()
    {
        var model = ShellPreview.CreateOnWorkspace();
        model.AddWorkspace(WorkspacePreset.All[0]);
        var changed = model.Workspaces.Select(static tab => tab.Workspace).OfType<WorkspaceModel>().ToArray();
        var preferences = new DesktopUpdatePreferences();
        var dialogs = new KeyStoreTests.RecordingDialogs { Choice = DialogChoice.Cancel };
        var files = new WorkspaceFiles(
            model,
            dialogs,
            new StubFilePicker(),
            new WorkspaceBookmarks(() => preferences, saved => preferences = saved));
        model.Files = files;

        var released = new List<string>();
        var logged = new List<Exception>();
        var agentAsked = new TaskCompletionSource();
        var agentStopped = new TaskCompletionSource();
        var closed = new TaskCompletionSource();
        var window = new Window();
        window.Closed += (_, _) => closed.TrySetResult();
        ShellShutdown.Attach(
            window,
            files.ConfirmExitAsync,
            [
                () =>
                {
                    released.Add("queue");
                    throw new IOException("The pipe has gone.");
                },
                .. changed.Select(workspace => (Func<Task>)(() =>
                {
                    released.Add(workspace.Name);
                    return workspace.DisposeAsync().AsTask();
                }))
            ],
            () =>
            {
                released.Add("agent");
                agentAsked.SetResult();
                return agentStopped.Task;
            },
            logged.Add);
        window.Show();

        // Cancel on the first changed workspace: nothing more is asked and nothing is let go.
        window.Close();
        Assert.Equal((1, Ui.Dialogs.ExitCaption), (dialogs.Asked, dialogs.LastRequest?.Title));
        Assert.Empty(released);
        Assert.True(window is { IsVisible: true, IsEnabled: true });

        // Don't save, for each: everything goes, the agent last, and the window waits for it without
        // taking any more input. Closing again meanwhile changes nothing.
        dialogs.Choice = DialogChoice.No;
        window.Close();
        await agentAsked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        window.Close();
        Assert.Equal(1 + changed.Length, dialogs.Asked);
        Assert.Equal(["queue", .. changed.Select(static workspace => workspace.Name), "agent"], released);
        Assert.IsType<IOException>(Assert.Single(logged));
        Assert.True(window is { IsVisible: true, IsEnabled: false });

        // And once the agent has stopped, the window closes without asking again.
        agentStopped.SetResult();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1 + changed.Length, dialogs.Asked);
    }
}
