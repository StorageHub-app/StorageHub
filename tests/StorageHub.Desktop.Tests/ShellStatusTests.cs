using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StorageHub.Desktop;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The status bar, which says what the shell believes about the agent.
/// </summary>
/// <remarks>
/// The strings come from ShellStatusSnapshot, which the WinForms shell has always used and which
/// moved to Desktop.Core with the other presentation models. Binding to it rather than writing four
/// strings in the view is what stops the two shells drifting apart while both exist.
/// </remarks>
public class ShellStatusTests
{
    /// <remarks>
    /// A shell of its own, on Welcome as the application opens: the shared sample carries whatever
    /// message another test's keystroke left in its first cell, such as a command with nowhere to go.
    /// </remarks>
    [AvaloniaFact]
    public void TheStatusBarStartsWhereTheShellAlwaysStarted()
    {
        var model = ShellPreview.CreateOnWorkspace();
        model.SelectedWorkspace = 0;

        Assert.Equal(ShellStatusSnapshot.Initial.Location, model.ShellStatus.Location);
        Assert.Equal(ShellStatusSnapshot.Initial.AgentText, model.ShellStatus.AgentText);
    }

    /// <summary>Every cell is the snapshot's, not a literal the view invented.</summary>
    [AvaloniaFact]
    public void TheStatusBarShowsWhatTheSnapshotSays()
    {
        var model = ShellPreview.Sample;
        var window = new MainWindow { DataContext = model };
        window.Show();
        window.Measure(new Size(1500, 920));
        window.Arrange(new Rect(0, 0, 1500, 920));

        var bar = window.GetVisualDescendants().OfType<Border>()
            .First(border => border.Classes.Contains("statusbar"));
        var texts = bar.GetVisualDescendants().OfType<TextBlock>()
            .Select(block => block.Text)
            .ToList();

        Assert.Contains(model.ShellStatus.SelectionText, texts);
        Assert.Contains(model.ShellStatus.TransferRateText, texts);
        Assert.Contains(model.ShellStatus.QueueText, texts);
        Assert.Contains(model.ShellStatus.AgentText, texts);
    }

    /// <summary>
    /// The bar does what 1.4's did: a click on the agent cell opens Agent control, whose tooltip
    /// says so until the agent says more, and a short message such as "clipboard cleared" stands in
    /// the first cell for a while and then gives the location back. The last cell is 1.4's update
    /// link: what the updater last said, coloured by it, and a click opens the update window.
    /// </summary>
    /// <remarks>
    /// Another pane picked does not write over it, as it did not redraw 1.4's bar. It used to, so
    /// the "Choose a destination and paste" that staging says went as the destination was chosen.
    /// </remarks>
    [AvaloniaFact]
    public async Task TheAgentCellOpensAgentControlAndAMessageComesAndGoes()
    {
        var model = ShellPreview.CreateOnWorkspace();
        var opened = 0;
        model.Router.Handle(UiCommandIds.ToolsBackgroundAgent, () => opened++);
        var updatesOpened = 0;
        model.Router.Handle(UiCommandIds.HelpCheckForUpdates, () => updatesOpened++);
        var window = new MainWindow { DataContext = model };
        window.Show();
        window.Measure(new Size(1500, 920));
        window.Arrange(new Rect(0, 0, 1500, 920));
        var bar = window.GetVisualDescendants().OfType<Border>()
            .First(border => border.Classes.Contains("statusbar"));

        var cells = bar.GetVisualDescendants().OfType<Button>().ToArray();
        Assert.Equal(2, cells.Length);
        var (agent, update) = (cells[0], cells[1]);
        Assert.Equal(Ui.Shell.AgentControlsTooltip, ToolTip.GetTip(agent));
        var centre = agent.TranslatePoint(new Point(agent.Bounds.Width / 2, agent.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Assert.Equal(1, opened);

        Assert.Equal(Ui.Shell.CheckForUpdatesTooltip, ToolTip.GetTip(update));
        Assert.Equal(Ui.Shell.UpdateStatus, AutomationProperties.GetName(update));
        Assert.True(update.Bounds.X > agent.Bounds.X);
        var updateText = update.GetVisualDescendants().OfType<TextBlock>().Single();
        Assert.Equal(Ui.Validation.UpdatesIdle, updateText.Text);
        model.ShowUpdate(new DesktopUpdateSnapshot(DesktopUpdateState.UpdateAvailable, "Update 2.0.1 available", "2.0.1"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Update 2.0.1 available", updateText.Text);
        Assert.Contains("available", updateText.Classes);
        model.ShowUpdate(new DesktopUpdateSnapshot(DesktopUpdateState.Failed, Ui.Validation.UpdatesCheckFailedTryAgainLater));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("failed", updateText.Classes);
        Assert.DoesNotContain("available", updateText.Classes);
        centre = update.TranslatePoint(new Point(update.Bounds.Width / 2, update.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Assert.Equal(1, updatesOpened);

        var location = model.ShellStatus.Location;
        model.MessageLifetime = TimeSpan.FromMilliseconds(50);
        var workspace = model.Workspaces[model.SelectedWorkspace].Workspace!;
        workspace.ClearClipboardCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(Ui.Shell.StatusClipboardCleared, bar.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));

        var first = workspace.Active;
        workspace.Panes.First(pane => !ReferenceEquals(pane, first)).IsActive = true;
        Assert.Equal(Ui.Shell.StatusClipboardCleared, model.ShellStatus.Location);
        first.IsActive = true;

        await RunJobsUntil(() => model.ShellStatus.Location == location);
        Assert.Equal(location, model.ShellStatus.Location);
    }

    /// <summary>
    /// Only a connected agent is green.
    /// </summary>
    /// <remarks>
    /// The cell was painted SuccessBrush unconditionally, so a shell that had reached nothing still
    /// showed "Agent: starting" in green. The state decides the colour now, beside the text.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(AgentConnectionState.Connected, true, false)]
    [InlineData(AgentConnectionState.Starting, false, false)]
    [InlineData(AgentConnectionState.Reconnecting, false, false)]
    [InlineData(AgentConnectionState.RecoveryOnly, false, false)]
    [InlineData(AgentConnectionState.Disconnected, false, true)]
    public void OnlyAConnectedAgentReadsAsHealthy(
        AgentConnectionState state, bool healthy, bool down)
    {
        var snapshot = ShellStatusSnapshot.Initial with { AgentState = state };

        Assert.Equal(healthy, snapshot.AgentIsHealthy);
        Assert.Equal(down, snapshot.AgentIsDown);
    }

    /// <summary>
    /// The agent's state reaches the bar without the view knowing the states exist.
    /// </summary>
    /// <remarks>
    /// Asserted through the snapshot rather than by running a monitor: a real one would need a real
    /// agent, and what is worth testing here is that a state change becomes the right text.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(AgentConnectionState.Starting)]
    [InlineData(AgentConnectionState.Connected)]
    [InlineData(AgentConnectionState.RecoveryOnly)]
    [InlineData(AgentConnectionState.Reconnecting)]
    [InlineData(AgentConnectionState.Disconnected)]
    public void EveryAgentStateHasItsOwnWording(AgentConnectionState state)
    {
        var text = (ShellStatusSnapshot.Initial with { AgentState = state }).AgentText;

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.StartsWith("Agent:", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A saved change to concurrency restarts the agent only once nothing is running, as 1.4 did,
    /// and the status bar says what it is waiting for.
    /// </summary>
    /// <remarks>
    /// A synchronization is running work as much as a transfer is. Before this, saving restarted
    /// the agent at once and cut off whatever was copying in order to apply a number.
    /// </remarks>
    [AvaloniaFact]
    public async Task AConcurrencyChangeWaitsForRunningWorkBeforeRestartingTheAgent()
    {
        var model = ShellPreview.CreateOnWorkspace();
        var agent = new CountingLifecycle();
        model.AgentLifecycle = () => agent;

        model.Observe(Running(transfers: 1, syncRuns: 0));
        await model.ApplyAgentSettingsAsync();
        Assert.Equal(0, agent.Restarts);
        Assert.Equal(Ui.Shell.StatusConcurrencyPendingIdle, model.ShellStatus.Location);

        // The wait is a state rather than news: it does not time out, and a message said over it
        // gives the cell back to it, since the settings are still waiting.
        model.MessageLifetime = TimeSpan.FromMilliseconds(50);
        model.Say(Ui.Shell.StatusClipboardCleared);
        Assert.Equal(Ui.Shell.StatusClipboardCleared, model.ShellStatus.Location);
        await RunJobsUntil(() => model.ShellStatus.Location != Ui.Shell.StatusClipboardCleared);
        Assert.Equal(Ui.Shell.StatusConcurrencyPendingIdle, model.ShellStatus.Location);

        model.Observe(Running(transfers: 0, syncRuns: 1));
        Assert.Equal(0, agent.Restarts);

        model.Observe(Running(transfers: 0, syncRuns: 0));
        Assert.Equal(1, agent.Restarts);
        Assert.Equal(Ui.Shell.AdaptiveConcurrencyActive, model.ShellStatus.Location);

        // Once done it is done: a quiet agent is not restarted again on every poll.
        model.Observe(Running(transfers: 0, syncRuns: 0));
        Assert.Equal(1, agent.Restarts);

        // Nothing running restarts at once.
        await model.ApplyAgentSettingsAsync();
        Assert.Equal(2, agent.Restarts);

        // A save while the agent is restarting follows that restart rather than overlapping it,
        // and the agent being down meanwhile is not brought back by a second launch beside it.
        var recoveries = 0;
        model.RecoverAgent = _ =>
        {
            recoveries++;
            return Task.FromResult(new AgentEnsureResult(AgentEnsureStatus.Started));
        };
        var held = new TaskCompletionSource();
        agent.Hold = held.Task;
        var restarting = model.ApplyAgentSettingsAsync();
        _ = model.ApplyAgentSettingsAsync();
        model.Observe(Running(transfers: 0, syncRuns: 0) with { State = AgentConnectionState.Disconnected });
        Assert.Equal(3, agent.Restarts);
        Assert.Equal(0, recoveries);
        held.SetResult();
        await restarting;
        Assert.Equal(4, agent.Restarts);

        // A machine that cannot restart its agent is told to restart StorageHub instead, and once
        // the shell is closing nothing is restarted at all.
        model.AgentLifecycle = static () => null;
        await model.ApplyAgentSettingsAsync();
        Assert.Equal(Ui.Shell.StatusConcurrencyRestartRequired, model.ShellStatus.Location);
        model.AgentLifecycle = () => agent;
        model.StopRestartingAgent();
        await model.ApplyAgentSettingsAsync();
        Assert.Equal(4, agent.Restarts);
    }

    private static AgentMonitorStatus Running(int transfers, int syncRuns) =>
        new(AgentConnectionState.Connected, transfers, syncRuns, string.Empty, DateTimeOffset.UnixEpoch);

    /// <summary>Runs the dispatcher until the condition holds, or five seconds have gone.</summary>
    private static async Task RunJobsUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private sealed class CountingLifecycle : IAgentLifecycleController
    {
        internal int Restarts { get; private set; }

        /// <summary>What a restart waits on before it finishes, to catch the agent mid-restart.</summary>
        internal Task Hold { get; set; } = Task.CompletedTask;

        public async Task<AgentLifecycleResult> ExecuteAsync(
            AgentLifecycleAction action,
            CancellationToken cancellationToken = default)
        {
            if (action == AgentLifecycleAction.Restart) Restarts++;
            await Hold;
            return new AgentLifecycleResult(true, string.Empty);
        }
    }
}
