namespace StorageHub.Desktop.Tests;

public sealed class PackagedDesktopLifecycleTests
{
    [Fact]
    public void ConfigureAutostartRegistersAgentOnlyDesktopCommandForCurrentUser()
    {
        var fixture = CreateFixture();

        var result = fixture.Lifecycle.ConfigureAutostart();

        Assert.Equal(AutostartConfigurationStatus.Registered, result);
        var entry = Assert.Single(fixture.RunEntries.SetCalls);
        Assert.Equal("StorageHub.Agent", entry.Name);
        Assert.Equal($"\"{fixture.DesktopExecutable}\" --agent-only", entry.CommandLine);
        Assert.Empty(fixture.RunEntries.RemovedNames);
    }

    [Fact]
    public void ConfigureAutostartDisableSwitchRemovesAnyExistingEntry()
    {
        var fixture = CreateFixture(disableAutostart: true);

        var result = fixture.Lifecycle.ConfigureAutostart();

        Assert.Equal(AutostartConfigurationStatus.Disabled, result);
        Assert.Empty(fixture.RunEntries.SetCalls);
        Assert.Equal("StorageHub.Agent", Assert.Single(fixture.RunEntries.RemovedNames));
    }

    /// <summary>
    /// "Only while StorageHub is open" has no logon entry, and this method runs after every
    /// update. Registering unconditionally silently moved anyone on that mode back to starting at
    /// sign-in the next time they updated.
    /// </summary>
    [Fact]
    public void ConfigureAutostartLeavesNoEntryWhenTheModeDoesNotWantOne()
    {
        var fixture = CreateFixture(registersAutostart: false);

        var result = fixture.Lifecycle.ConfigureAutostart();

        Assert.Equal(AutostartConfigurationStatus.Disabled, result);
        Assert.Empty(fixture.RunEntries.SetCalls);
        Assert.Equal("StorageHub.Agent", Assert.Single(fixture.RunEntries.RemovedNames));
    }

    /// <summary>
    /// A fresh install has chosen nothing yet, and "no service and no entry" is exactly what the
    /// app-session mode looks like. Without the override, installing would read a brand-new
    /// machine as a deliberate choice and change what a first install does.
    /// </summary>
    [Fact]
    public void ConfigureAutostartCanForceAnEntryForAFreshInstall()
    {
        var fixture = CreateFixture(registersAutostart: false);

        var result = fixture.Lifecycle.ConfigureAutostart(force: true);

        Assert.Equal(AutostartConfigurationStatus.Registered, result);
        Assert.Equal("StorageHub.Agent", Assert.Single(fixture.RunEntries.SetCalls).Name);
    }

    /// <summary>The disable switch still wins over a forced registration.</summary>
    [Fact]
    public void ConfigureAutostartDisableSwitchBeatsTheInstallOverride()
    {
        var fixture = CreateFixture(disableAutostart: true, registersAutostart: false);

        Assert.Equal(
            AutostartConfigurationStatus.Disabled,
            fixture.Lifecycle.ConfigureAutostart(force: true));
        Assert.Empty(fixture.RunEntries.SetCalls);
    }

    [Fact]
    public async Task EnsureAgentDoesNotLaunchWhenPipeIsAlreadyAvailable()
    {
        var fixture = CreateFixture(agentAvailable: true);

        var result = await fixture.Lifecycle.EnsureAgentAsync();

        Assert.Equal(AgentEnsureStatus.AlreadyRunning, result.Status);
        Assert.True(result.IsReady);
        Assert.Empty(fixture.Launcher.Launches);
        Assert.Equal(0, fixture.AgentClient.WaitCalls);
    }

    [Fact]
    public async Task EnsureAgentLaunchesPackagedSiblingHiddenAndWaitsForReadiness()
    {
        var fixture = CreateFixture(agentAvailable: false, fileExists: true, waitResult: true);

        var result = await fixture.Lifecycle.EnsureAgentAsync();

        Assert.Equal(AgentEnsureStatus.Started, result.Status);
        var launch = Assert.Single(fixture.Launcher.Launches);
        Assert.Equal(Path.Combine(fixture.ApplicationDirectory, "Agent", "StorageHub.Agent.Host.exe"), launch.Executable);
        Assert.Equal(Path.Combine(fixture.ApplicationDirectory, "Agent"), launch.WorkingDirectory);
        Assert.Equal("--background", launch.Argument);
        Assert.Equal(1, fixture.AgentClient.WaitCalls);
    }

    [Fact]
    public async Task EnsureAgentFailsClosedWhenPackagedExecutableIsMissing()
    {
        var fixture = CreateFixture(agentAvailable: false, fileExists: false);

        var result = await fixture.Lifecycle.EnsureAgentAsync();

        Assert.Equal(AgentEnsureStatus.MissingExecutable, result.Status);
        Assert.False(result.IsReady);
        Assert.Empty(fixture.Launcher.Launches);
        Assert.Equal(0, fixture.AgentClient.WaitCalls);
    }

    [Fact]
    public async Task EnsureAgentReplacesAvailableAgentFromAnotherBuildDirectory()
    {
        var fixture = CreateFixture(
            agentAvailable: true,
            waitResult: true,
            shutdownResult: true,
            enforceExpectedProcess: true,
            expectedProcessRunning: false);

        var result = await fixture.Lifecycle.EnsureAgentAsync();

        Assert.Equal(AgentEnsureStatus.Started, result.Status);
        Assert.Equal(AgentShutdownReason.Restart, Assert.Single(fixture.AgentClient.ShutdownReasons));
        Assert.Single(fixture.Launcher.Launches);
    }

    [Fact]
    public async Task EnsureAgentWaitsForAnExistingProcessThatIsStillStarting()
    {
        var fixture = CreateFixture(
            agentAvailable: false,
            waitResult: true,
            enforceExpectedProcess: true,
            expectedProcessRunning: true);

        var result = await fixture.Lifecycle.EnsureAgentAsync();

        Assert.Equal(AgentEnsureStatus.AlreadyRunning, result.Status);
        Assert.Empty(fixture.Launcher.Launches);
        Assert.Equal(1, fixture.AgentClient.WaitCalls);
        Assert.Equal(0, fixture.ProcessMonitor!.TerminateCalls);
    }

    [Fact]
    public async Task EnsureAgentReplacesAnUnresponsiveExpectedProcessBeforeLaunching()
    {
        var fixture = CreateFixture(
            agentAvailable: false,
            waitResult: false,
            enforceExpectedProcess: true,
            expectedProcessRunning: true,
            terminateResult: true);

        var result = await fixture.Lifecycle.EnsureAgentAsync();

        Assert.Equal(AgentEnsureStatus.StartupTimedOut, result.Status);
        Assert.Single(fixture.Launcher.Launches);
        Assert.Equal(1, fixture.ProcessMonitor!.TerminateCalls);
        Assert.Equal(2, fixture.AgentClient.WaitCalls);
    }

    /// <summary>
    /// The sign-in race under a service. Windows starts the auto-start service alongside the
    /// session that starts the desktop, and the service answers the control manager long before its
    /// pipe exists. Asking once and giving up turned that ordinary ordering into "the background
    /// agent did not become ready in time" on every boot, with a perfectly healthy service running.
    /// </summary>
    [Fact]
    public async Task EnsureAgentWaitsForAServiceThatIsStillStarting()
    {
        var fixture = CreateFixture(
            agentAvailable: false,
            waitResult: true,
            desktopOwnsAgent: false);

        var result = await fixture.Lifecycle.EnsureAgentAsync();

        Assert.Equal(AgentEnsureStatus.AlreadyRunning, result.Status);
        Assert.True(result.IsReady);
        Assert.Equal(1, fixture.AgentClient.WaitCalls);
        // Still never starts one: that is the service control manager's job, and a second agent
        // beside it would be two processes on two databases.
        Assert.Empty(fixture.Launcher.Launches);
    }

    /// <summary>
    /// A service that really never arrives is still reported, rather than waited on forever.
    /// </summary>
    [Fact]
    public async Task EnsureAgentReportsAServiceThatNeverBecomesAvailable()
    {
        var fixture = CreateFixture(
            agentAvailable: false,
            waitResult: false,
            desktopOwnsAgent: false);

        var result = await fixture.Lifecycle.EnsureAgentAsync();

        Assert.Equal(AgentEnsureStatus.StartupTimedOut, result.Status);
        Assert.False(result.IsReady);
        Assert.Empty(fixture.Launcher.Launches);
    }

    /// <summary>
    /// An already-running service costs nothing: the wait returns as soon as the pipe answers, so
    /// the longer budget is never actually spent on the common path.
    /// </summary>
    [Fact]
    public async Task EnsureAgentReturnsImmediatelyWhenTheServiceIsAlreadyUp()
    {
        var fixture = CreateFixture(
            agentAvailable: true,
            waitResult: true,
            desktopOwnsAgent: false);

        var result = await fixture.Lifecycle.EnsureAgentAsync();

        Assert.Equal(AgentEnsureStatus.AlreadyRunning, result.Status);
        Assert.Empty(fixture.Launcher.Launches);
    }

    [Fact]
    public async Task EnsureAgentReportsLaunchFailureWhenNewProcessExitsBeforeReadiness()
    {
        var fixture = CreateFixture(
            agentAvailable: false,
            waitResult: false,
            enforceExpectedProcess: true,
            expectedProcessRunning: false);

        var result = await fixture.Lifecycle.EnsureAgentAsync();

        Assert.Equal(AgentEnsureStatus.LaunchFailed, result.Status);
        Assert.Single(fixture.Launcher.Launches);
        Assert.Equal(1, fixture.AgentClient.WaitCalls);
    }

    [Theory]
    [InlineData(AgentEnsureStatus.MissingExecutable, "missing")]
    [InlineData(AgentEnsureStatus.LaunchFailed, "could not be started")]
    [InlineData(AgentEnsureStatus.StartupTimedOut, "did not become ready")]
    public void StartupPreflightExplainsWhyTheDesktopWillNotOpen(
        AgentEnsureStatus status,
        string expectedText)
    {
        var message = DesktopStartupPreflight.DescribeFailure(status);

        Assert.Contains(expectedText, message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VelopackHooksRegisterRefreshStopAndUnregisterWithoutADataDeletionSurface()
    {
        var fixture = CreateFixture(shutdownResult: true);
        var brokerUnregisterCalls = 0;
        var hooks = new DesktopPackageLifecycleHooks(
            fixture.Lifecycle,
            () =>
            {
                brokerUnregisterCalls++;
                return true;
            });

        hooks.AfterInstall();
        hooks.BeforeUpdate();
        hooks.AfterUpdate();
        hooks.BeforeUninstall();

        Assert.Equal(2, fixture.RunEntries.SetCalls.Count);
        Assert.Equal("StorageHub.Agent", Assert.Single(fixture.RunEntries.RemovedNames));
        Assert.Equal(
            [AgentShutdownReason.Update, AgentShutdownReason.Uninstall],
            fixture.AgentClient.ShutdownReasons);
        Assert.Equal(1, brokerUnregisterCalls);
    }

    [Fact]
    public void VelopackBootstrapDisablesFrameworkAutoApplySoUpdaterPreferencesRemainAuthoritative()
    {
        Assert.False(VelopackDesktopBootstrap.AutoApplyOnStartup);
    }

    [Theory]
    [InlineData("--agent-only")]
    [InlineData("--AGENT-ONLY")]
    public void AgentOnlyArgumentIsCaseInsensitive(string argument)
    {
        Assert.True(DesktopCommandLine.IsAgentOnly([argument]));
        Assert.False(DesktopCommandLine.IsAgentOnly(["--health"]));
    }

    [Fact]
    public void ApplicationVersionComesFromAssemblyMetadata()
    {
        var assembly = typeof(PackagedDesktopLifecycle).Assembly;

        var version = DesktopApplicationVersion.Resolve(assembly);
        var expected = assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), inherit: false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>()
            .SingleOrDefault()?
            .InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "0.0.0";
        var metadataSeparator = expected.IndexOf('+', StringComparison.Ordinal);
        if (metadataSeparator >= 0)
        {
            expected = expected[..metadataSeparator];
        }

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.DoesNotContain(version, char.IsControl);
        Assert.Equal(expected, version);
    }

    /// <summary>
    /// A service-hosted agent answers the same pipe as one the desktop started, so an
    /// unconditional shutdown before an update stopped the service.s own process behind the
    /// service control manager.s back. Windows logged an unexpected termination and, with no
    /// failure actions, left it stopped -- so every update ended with "the background agent did
    /// not become ready in time". It happened four times on one machine in a single evening.
    /// </summary>
    [Fact]
    public void AnUpdateLeavesAServiceHostedAgentAlone()
    {
        var fixture = CreateFixture(desktopOwnsAgent: false);

        new DesktopPackageLifecycleHooks(fixture.Lifecycle).BeforeUpdate();

        Assert.Empty(fixture.AgentClient.ShutdownReasons);
    }

    [Fact]
    public void AnUpdateStillStopsAnAgentTheDesktopStarted()
    {
        var fixture = CreateFixture(desktopOwnsAgent: true, shutdownResult: true);

        new DesktopPackageLifecycleHooks(fixture.Lifecycle).BeforeUpdate();

        Assert.Equal(
            AgentShutdownReason.Update,
            Assert.Single(fixture.AgentClient.ShutdownReasons));
    }

    /// <summary>Removing the service stops it, through the control manager rather than behind it.</summary>
    [Fact]
    public void UninstallingLeavesAServiceHostedAgentToTheServiceControlManager()
    {
        var fixture = CreateFixture(desktopOwnsAgent: false);

        new DesktopPackageLifecycleHooks(fixture.Lifecycle, () => true).BeforeUninstall();

        Assert.Empty(fixture.AgentClient.ShutdownReasons);
    }

    [Fact]
    public void UninstallingStopsAnAgentTheDesktopStarted()
    {
        var fixture = CreateFixture(desktopOwnsAgent: true, shutdownResult: true);

        new DesktopPackageLifecycleHooks(fixture.Lifecycle, () => true).BeforeUninstall();

        Assert.Contains(AgentShutdownReason.Uninstall, fixture.AgentClient.ShutdownReasons);
    }

    private static LifecycleFixture CreateFixture(
        bool disableAutostart = false,
        bool agentAvailable = false,
        bool fileExists = true,
        bool waitResult = false,
        bool shutdownResult = false,
        bool enforceExpectedProcess = false,
        bool expectedProcessRunning = false,
        bool terminateResult = false,
        bool registersAutostart = true,
        bool desktopOwnsAgent = true)
    {
        var applicationDirectory = Path.Combine(
            Path.GetTempPath(),
            "StorageHub lifecycle fixture",
            Guid.NewGuid().ToString("N"));
        var desktopExecutable = Path.Combine(applicationDirectory, "StorageHub.Desktop.exe");
        var runEntries = new FakeRunEntryStore();
        var launcher = new FakeProcessLauncher();
        var agentClient = new FakeAgentLifecycleClient
        {
            Available = agentAvailable,
            WaitResult = waitResult,
            ShutdownResult = shutdownResult
        };
        var processMonitor = enforceExpectedProcess
            ? new FakeProcessMonitor(expectedProcessRunning, terminateResult)
            : null;
        var lifecycle = new PackagedDesktopLifecycle(
            desktopExecutable,
            applicationDirectory,
            runEntries,
            launcher,
            agentClient,
            name => disableAutostart && string.Equals(
                name,
                PackagedDesktopLifecycleOptions.DisableAutostartEnvironmentVariable,
                StringComparison.Ordinal)
                    ? "1"
                    : null,
            _ => fileExists,
            // Pinned rather than read from the machine, so these tests describe the mode they name
            // regardless of whether the machine running them happens to have the service installed.
            options: new PackagedDesktopLifecycleOptions
            {
                DesktopOwnsAgent = () => desktopOwnsAgent,
                RegistersAutostart = () => registersAutostart
            },
            processMonitor: processMonitor);
        return new LifecycleFixture(
            lifecycle,
            runEntries,
            launcher,
            agentClient,
            processMonitor,
            applicationDirectory,
            desktopExecutable);
    }

    private sealed record LifecycleFixture(
        PackagedDesktopLifecycle Lifecycle,
        FakeRunEntryStore RunEntries,
        FakeProcessLauncher Launcher,
        FakeAgentLifecycleClient AgentClient,
        FakeProcessMonitor? ProcessMonitor,
        string ApplicationDirectory,
        string DesktopExecutable);

    private sealed class FakeRunEntryStore : ICurrentUserRunEntryStore
    {
        public List<(string Name, string CommandLine)> SetCalls { get; } = [];

        public List<string> RemovedNames { get; } = [];

        public void SetValue(string valueName, string commandLine) =>
            SetCalls.Add((valueName, commandLine));

        public void Remove(string valueName) => RemovedNames.Add(valueName);
    }

    private sealed class FakeProcessLauncher : IAgentProcessLauncher
    {
        public List<(string Executable, string WorkingDirectory, string Argument)> Launches { get; } = [];

        public bool TryLaunchHidden(string executablePath, string workingDirectory, string argument)
        {
            Launches.Add((executablePath, workingDirectory, argument));
            return true;
        }
    }

    private sealed class FakeAgentLifecycleClient : IPackagedAgentLifecycleClient
    {
        /// <summary>
        /// The real client refuses any wait longer than this, so the double has to as well.
        /// A fake that accepts whatever it is handed let a twelve-second contract be broken by a
        /// forty-five-second default: every test passed, and the application threw on startup.
        /// </summary>
        private static readonly TimeSpan MaximumWait = TimeSpan.FromSeconds(12);

        public bool Available { get; set; }

        public bool WaitResult { get; init; }

        public bool ShutdownResult { get; init; }

        public int WaitCalls { get; private set; }

        public List<AgentShutdownReason> ShutdownReasons { get; } = [];

        public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Available);
        }

        public ValueTask<bool> WaitUntilAvailableAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAcceptableWait(timeout);
            WaitCalls++;
            return ValueTask.FromResult(WaitResult);
        }

        private static void EnsureAcceptableWait(TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero || timeout > MaximumWait)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeout),
                    "The Agent lifecycle timeout must be between zero and twelve seconds.");
            }
        }

        public ValueTask<bool> RequestShutdownAndWaitAsync(
            AgentShutdownReason reason,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAcceptableWait(timeout);
            ShutdownReasons.Add(reason);
            if (ShutdownResult)
            {
                Available = false;
            }
            return ValueTask.FromResult(ShutdownResult);
        }
    }

    private sealed class FakeProcessMonitor(bool running, bool terminateResult) : IPackagedAgentProcessMonitor
    {
        public int TerminateCalls { get; private set; }

        public bool IsRunning(string executablePath) => running;

        public ValueTask<bool> TryTerminateAsync(
            string executablePath,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TerminateCalls++;
            return ValueTask.FromResult(terminateResult);
        }

        public ValueTask<bool> WaitForExitAsync(
            int processId,
            string executablePath,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }
}
