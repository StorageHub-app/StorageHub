using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
    [AvaloniaFact]
    public void TheStatusBarStartsWhereTheShellAlwaysStarted()
    {
        var model = ShellPreview.Sample;

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
        Assert.Contains(model.ShellStatus.QueueText, texts);
        Assert.Contains(model.ShellStatus.AgentText, texts);
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
