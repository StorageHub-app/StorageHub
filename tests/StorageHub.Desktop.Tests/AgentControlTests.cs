using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The background agent window.
/// </summary>
/// <remarks>
/// The lifecycle controllers and the words for each state are pinned in the Core tests. What is
/// checked here is the seam: that the window says what the agent last reported, that the buttons
/// follow the state rather than being always live, that an outcome is reported either way, and
/// that a build which cannot control the agent says so instead of offering buttons that do nothing.
/// </remarks>
public sealed class AgentControlTests
{
    [AvaloniaFact]
    public void BeforeTheAgentHasSaidAnythingTheWindowSaysSo()
    {
        var model = new AgentControlModel(() => null, new StubController());

        Assert.Equal(Ui.Updates.AgentStateUnknown, model.State);
        Assert.Equal(Ui.Updates.NoStatusHasBeenReportedYet, model.Detail);
        Assert.Empty(model.Counters);
        Assert.Empty(model.Observed);

        // Start needs a state to be sure of; restart does not, because it is what fixes this.
        Assert.False(model.CanStart);
        Assert.False(model.CanStop);
        Assert.True(model.CanRestart);
    }

    [AvaloniaFact]
    public void TheWindowSaysWhatTheAgentReported()
    {
        var model = Model(Status(AgentConnectionState.Connected, "All good.", transfers: 3, syncRuns: 2));

        Assert.Equal(Ui.Updates.AgentStateRunning, model.State);
        Assert.Equal("All good.", model.Detail);
        Assert.Equal(Ui.Format(Ui.Updates.AgentActiveWorkFormat, 3, 2), model.Counters);
        Assert.StartsWith(Ui.Updates.LastReported, model.Observed, StringComparison.Ordinal);
        Assert.True(model.IsSuccess);
    }

    /// <summary>
    /// Recovery is drawn as a warning rather than a failure: the agent is running, and it is only
    /// its durable half that is not.
    /// </summary>
    [AvaloniaFact]
    public void RecoveryReadsAsAWarningRatherThanAFailure()
    {
        var model = Model(Status(AgentConnectionState.RecoveryOnly, string.Empty));

        Assert.True(model.IsWarning);
        Assert.False(model.IsDanger);
        Assert.Equal(
            AgentControlPresentation.DescribeDefaultDetail(AgentConnectionState.RecoveryOnly),
            model.Detail);
        Assert.True(model.CanStop);
        Assert.False(model.CanStart);
    }

    [AvaloniaFact]
    public void ADownAgentCanBeStartedButNotStopped()
    {
        var model = Model(Status(AgentConnectionState.Disconnected, string.Empty));

        Assert.True(model.IsDanger);
        Assert.True(model.CanStart);
        Assert.False(model.CanStop);
        Assert.True(model.CanRestart);
    }

    /// <summary>The window follows the agent, because it may start or stop while it is open.</summary>
    [AvaloniaFact]
    public void RefreshingFollowsTheAgent()
    {
        var status = Status(AgentConnectionState.Disconnected, string.Empty);
        var model = new AgentControlModel(() => status, new StubController());
        Assert.True(model.CanStart);

        status = Status(AgentConnectionState.Connected, string.Empty);
        model.Refresh();

        Assert.False(model.CanStart);
        Assert.True(model.CanStop);
        Assert.Equal(Ui.Updates.AgentStateRunning, model.State);
    }

    [AvaloniaFact]
    public async Task AnActionReportsWhatItDid()
    {
        var controller = new StubController { Result = new AgentLifecycleResult(true, "The agent restarted.") };
        var model = Model(Status(AgentConnectionState.Connected, string.Empty), controller);

        await model.RunAsync(AgentLifecycleAction.Restart);

        Assert.Equal(AgentLifecycleAction.Restart, controller.Requested);
        Assert.Equal("The agent restarted.", model.Outcome.Text);
        Assert.True(model.Outcome.IsSuccess);
    }

    /// <summary>A refusal is reported in the same place, and read as one.</summary>
    [AvaloniaFact]
    public async Task ARefusalIsReportedAsOne()
    {
        var controller = new StubController
        {
            Result = new AgentLifecycleResult(false, "Unit storagehub-agent.service not found.")
        };
        var model = Model(Status(AgentConnectionState.Disconnected, string.Empty), controller);

        await model.RunAsync(AgentLifecycleAction.Start);

        Assert.Equal("Unit storagehub-agent.service not found.", model.Outcome.Text);
        Assert.True(model.Outcome.IsDanger);
    }

    /// <summary>
    /// A controller that throws still leaves the window usable, with the reason on screen.
    /// </summary>
    [AvaloniaFact]
    public async Task AControllerThatThrowsIsReportedRatherThanEscaping()
    {
        var controller = new StubController { Failure = new TimeoutException("systemctl hung.") };
        var model = Model(Status(AgentConnectionState.Connected, string.Empty), controller);

        await model.RunAsync(AgentLifecycleAction.Stop);

        Assert.True(model.Outcome.IsDanger);
        Assert.Contains("systemctl hung.", model.Outcome.Text, StringComparison.Ordinal);
        // And the buttons come back, rather than being left dim by the failure.
        Assert.True(model.CanRestart);
    }

    /// <summary>
    /// A build with no packaged agent and no systemd unit can report the agent's state but not
    /// change it, and says so rather than offering buttons that do nothing.
    /// </summary>
    [AvaloniaFact]
    public async Task ABuildThatCannotControlTheAgentSaysSo()
    {
        var model = new AgentControlModel(
            () => Status(AgentConnectionState.Disconnected, string.Empty), controller: null);

        Assert.False(model.CanControl);
        Assert.False(model.CanStart);
        Assert.False(model.CanStop);
        Assert.False(model.CanRestart);

        await model.RunAsync(AgentLifecycleAction.Start);

        Assert.Equal(Ui.Updates.ThisBuildCannotControlTheAgentProcess, model.Outcome.Text);
    }

    /// <summary>The buttons dim while an action is in flight, so it cannot be asked for twice.</summary>
    [AvaloniaFact]
    public async Task TheButtonsDimWhileAnActionIsRunning()
    {
        var gate = new TaskCompletionSource();
        var controller = new StubController { Gate = gate.Task };
        var model = Model(Status(AgentConnectionState.Connected, string.Empty), controller);

        var running = model.RunAsync(AgentLifecycleAction.Restart);

        Assert.False(model.CanRestart);
        Assert.False(model.CanStop);
        Assert.Equal(
            AgentControlPresentation.DescribeInProgress(AgentLifecycleAction.Restart),
            model.Outcome.Text);

        gate.SetResult();
        await running;

        Assert.True(model.CanRestart);
    }

    /// <summary>
    /// Photographs the window in both appearances, for a human to look at.
    /// </summary>
    /// <remarks>
    /// Recovery, because it is the state with the longest sentence and the one whose colour has to
    /// read as a warning rather than a failure. Set STORAGEHUB_SHOT_DIR to keep the files.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheWindowCanBePhotographed(bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));

        var model = Model(Status(AgentConnectionState.RecoveryOnly, string.Empty, 2, 1));
        var window = new AgentControlWindow { DataContext = model };
        window.Show();
        window.Measure(new Size(520, 400));
        window.Arrange(new Rect(0, 0, 520, 400));
        window.UpdateLayout();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;

        Directory.CreateDirectory(directory);
        using var stream = File.Create(
            Path.Combine(directory, $"agent-control-{(dark ? "dark" : "light")}.png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }

    private static AgentControlModel Model(
        AgentMonitorStatus status, IAgentLifecycleController? controller = null) =>
        new(() => status, controller ?? new StubController());

    private static AgentMonitorStatus Status(
        AgentConnectionState state, string detail, int transfers = 0, int syncRuns = 0) =>
        new(state, transfers, syncRuns, detail, DateTimeOffset.UnixEpoch);

    private sealed class StubController : IAgentLifecycleController
    {
        internal AgentLifecycleResult Result { get; init; } = new(true, "Done.");

        internal Exception? Failure { get; init; }

        /// <summary>Held open so a test can look at the window while the action is in flight.</summary>
        internal Task? Gate { get; init; }

        internal AgentLifecycleAction? Requested { get; private set; }

        public async Task<AgentLifecycleResult> ExecuteAsync(
            AgentLifecycleAction action,
            CancellationToken cancellationToken = default)
        {
            Requested = action;
            if (Gate is not null) await Gate.ConfigureAwait(true);
            return Failure is null ? Result : throw Failure;
        }
    }
}
