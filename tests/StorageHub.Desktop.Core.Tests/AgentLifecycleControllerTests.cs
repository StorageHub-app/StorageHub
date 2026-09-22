namespace StorageHub.Desktop.Tests;

/// <summary>
/// Controlling the background agent, and what the screen says about its state.
/// </summary>
/// <remarks>
/// The systemd half is the reason this screen was not simply a port: on Windows the desktop owns
/// the agent process, and on Linux systemd does. These run everywhere because the command is
/// behind <see cref="IProcessRunner"/> -- neither the Windows dev machine nor the CI runner has
/// systemctl, and a controller that could only be tested on the target is one that never is.
/// </remarks>
public sealed class AgentLifecycleControllerTests
{
    [Theory]
    [InlineData(AgentLifecycleAction.Start, "start")]
    [InlineData(AgentLifecycleAction.Stop, "stop")]
    [InlineData(AgentLifecycleAction.Restart, "restart")]
    public async Task EachActionAsksSystemdForThatVerb(AgentLifecycleAction action, string verb)
    {
        var runner = new StubProcessRunner();
        var controller = new SystemdAgentLifecycleController(runner);

        var result = await controller.ExecuteAsync(action, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("systemctl", runner.FileName);
        Assert.Equal(
            ["--user", verb, SystemdAgentLifecycleController.UnitName],
            runner.Arguments);
    }

    /// <summary>
    /// The unit is a user unit, so none of this needs privilege. A <c>--system</c> call would ask
    /// for root and reach an agent that owns none of this account's data.
    /// </summary>
    [Fact]
    public async Task TheUnitIsControlledForThisUserOnly()
    {
        var runner = new StubProcessRunner();

        await new SystemdAgentLifecycleController(runner)
            .ExecuteAsync(AgentLifecycleAction.Start, CancellationToken.None);

        Assert.Equal("--user", runner.Arguments[0]);
        Assert.DoesNotContain("--system", runner.Arguments);
    }

    /// <summary>
    /// The unit name has to be the one the .deb installs and the one the agent's own autostart
    /// writes, because either may have registered it. Nothing else connects the three.
    /// </summary>
    [Fact]
    public void ThePackagedUnitNameIsTheOneThisControls()
    {
        var packaged = File.ReadAllText(TestPaths.RepositoryFile("eng/package-linux.sh"));

        Assert.Contains(
            "/usr/lib/systemd/user/" + SystemdAgentLifecycleController.UnitName,
            packaged,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// systemd's own words reach the screen. "Unit storagehub-agent.service not found" is what a
    /// user sees when the package is installed but the unit was never enabled, and it is the
    /// difference between a fixable message and a shrug.
    /// </summary>
    [Fact]
    public async Task ARefusalCarriesWhatSystemdSaid()
    {
        var runner = new StubProcessRunner
        {
            Result = new ProcessRunResult(true, 5, "Unit storagehub-agent.service not found.")
        };

        var result = await new SystemdAgentLifecycleController(runner)
            .ExecuteAsync(AgentLifecycleAction.Start, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Unit storagehub-agent.service not found.", result.Message);
    }

    /// <summary>A refusal with nothing to say still says something.</summary>
    [Fact]
    public async Task ASilentRefusalStillExplainsItself()
    {
        var runner = new StubProcessRunner { Result = new ProcessRunResult(true, 1, "   ") };

        var result = await new SystemdAgentLifecycleController(runner)
            .ExecuteAsync(AgentLifecycleAction.Stop, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(Localization.Ui.Updates.AgentCouldNotBeControlled, result.Message);
    }

    /// <summary>
    /// A machine with no systemd is not a machine where the agent failed to start -- it is one
    /// where this build cannot control the agent at all, and it says so.
    /// </summary>
    [Fact]
    public async Task AMachineWithoutSystemdSaysSo()
    {
        var runner = new StubProcessRunner { Result = ProcessRunResult.NotRun };

        var result = await new SystemdAgentLifecycleController(runner)
            .ExecuteAsync(AgentLifecycleAction.Restart, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(Localization.Ui.Updates.ThisBuildCannotControlTheAgentProcess, result.Message);
    }

    [Fact]
    public async Task AnUnknownActionIsRefusedWithoutRunningAnything()
    {
        var runner = new StubProcessRunner();

        var result = await new SystemdAgentLifecycleController(runner)
            .ExecuteAsync((AgentLifecycleAction)99, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(runner.FileName);
    }

    /// <summary>Every outcome is a translated string rather than an English literal.</summary>
    [Theory]
    [InlineData(AgentLifecycleAction.Start)]
    [InlineData(AgentLifecycleAction.Stop)]
    [InlineData(AgentLifecycleAction.Restart)]
    public async Task OutcomesComeFromTheStringsRatherThanTheCode(AgentLifecycleAction action)
    {
        var result = await new SystemdAgentLifecycleController(new StubProcessRunner())
            .ExecuteAsync(action, CancellationToken.None);

        Assert.Contains(
            result.Message,
            new[]
            {
                Localization.Ui.Updates.AgentStarted,
                Localization.Ui.Updates.AgentStopped,
                Localization.Ui.Updates.AgentRestarted
            },
            StringComparer.Ordinal);
    }

    private sealed class StubProcessRunner : IProcessRunner
    {
        internal ProcessRunResult Result { get; init; } = new(true, 0, string.Empty);

        internal string? FileName { get; private set; }

        internal IReadOnlyList<string> Arguments { get; private set; } = [];

        public Task<ProcessRunResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            FileName = fileName;
            Arguments = arguments;
            return Task.FromResult(Result);
        }
    }
}

/// <summary>What the agent control screen says about a state, and which buttons it allows.</summary>
public sealed class AgentControlPresentationTests
{
    /// <summary>Every state reads as something, in words rather than as an enum name.</summary>
    [Fact]
    public void EveryStateIsDescribed()
    {
        foreach (var state in Enum.GetValues<AgentConnectionState>())
        {
            Assert.False(string.IsNullOrWhiteSpace(AgentControlPresentation.DescribeState(state)));
            Assert.False(string.IsNullOrWhiteSpace(AgentControlPresentation.DescribeDefaultDetail(state)));

            // Not the raw identifier. A state that fell through to ToString would read as
            // "RecoveryOnly" -- which is how an unhandled case shows itself, and the only thing
            // here that a translator could not fix.
            Assert.NotEqual(state.ToString(), AgentControlPresentation.DescribeState(state));
        }
    }

    /// <summary>
    /// Recovery is the one state where the agent is running and nothing durable works, so it does
    /// not read as running and does not read as down.
    /// </summary>
    [Fact]
    public void RecoveryIsItsOwnState()
    {
        Assert.NotEqual(
            AgentControlPresentation.DescribeState(AgentConnectionState.Connected),
            AgentControlPresentation.DescribeState(AgentConnectionState.RecoveryOnly));
        Assert.NotEqual(
            AgentControlPresentation.DescribeState(AgentConnectionState.Disconnected),
            AgentControlPresentation.DescribeState(AgentConnectionState.RecoveryOnly));

        // Running, so it can be stopped, and not startable, so a second one cannot be invited.
        Assert.True(AgentControlPresentation.CanStop(AgentConnectionState.RecoveryOnly));
        Assert.False(AgentControlPresentation.CanStart(AgentConnectionState.RecoveryOnly));
    }

    /// <summary>What the agent said about itself beats the sentence for its state.</summary>
    [Fact]
    public void TheAgentsOwnDetailIsPreferred()
    {
        var reported = Status(AgentConnectionState.RecoveryOnly, "The database is locked by another process.");
        Assert.Equal("The database is locked by another process.", AgentControlPresentation.DescribeDetail(reported));

        var silent = Status(AgentConnectionState.RecoveryOnly, "   ");
        Assert.Equal(
            AgentControlPresentation.DescribeDefaultDetail(AgentConnectionState.RecoveryOnly),
            AgentControlPresentation.DescribeDetail(silent));
    }

    /// <summary>
    /// Start is offered only when the agent is known to be down: one account has one agent, and
    /// the vault and durable queue belong to it.
    /// </summary>
    [Fact]
    public void StartIsOfferedOnlyWhenTheAgentIsDown()
    {
        Assert.True(AgentControlPresentation.CanStart(AgentConnectionState.Disconnected));

        foreach (var state in Enum.GetValues<AgentConnectionState>()
            .Where(state => state != AgentConnectionState.Disconnected))
        {
            Assert.False(AgentControlPresentation.CanStart(state));
        }

        // And not while the shell has yet to hear anything at all.
        Assert.False(AgentControlPresentation.CanStart(null));
    }

    [Fact]
    public void StopIsOfferedWheneverThereIsAProcessToStop()
    {
        Assert.True(AgentControlPresentation.CanStop(AgentConnectionState.Connected));
        Assert.True(AgentControlPresentation.CanStop(AgentConnectionState.RecoveryOnly));
        Assert.True(AgentControlPresentation.CanStop(AgentConnectionState.Starting));
        Assert.True(AgentControlPresentation.CanStop(AgentConnectionState.Reconnecting));

        Assert.False(AgentControlPresentation.CanStop(AgentConnectionState.Disconnected));
        Assert.False(AgentControlPresentation.CanStop(null));
    }

    private static AgentMonitorStatus Status(AgentConnectionState state, string detail) =>
        new(state, 0, 0, detail, DateTimeOffset.UnixEpoch);
}
