namespace StorageHub.Desktop.Tests;

/// <summary>
/// A PackagedDesktopLifecycle with every dependency faked, and the fakes themselves.
/// </summary>
/// <remarks>
/// These were private to PackagedDesktopLifecycleTests, which was fine while there was one suite.
/// The lifecycle's policy is portable and its hooks are not, so the suite split, and both halves
/// build the same fixture. StorageHub.Desktop.Windows.Tests links this file rather than keeping a
/// second copy of four fakes that must agree.
///
/// Every dependency is an interface for exactly this reason: nothing here starts a process, writes
/// to the registry or opens a pipe, which is what lets 30-odd assertions about startup ordering run
/// on a machine that has none of them.
/// </remarks>
internal static class LifecycleFixtures
{
    internal static LifecycleFixture CreateFixture(
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
}

internal sealed record LifecycleFixture(
    PackagedDesktopLifecycle Lifecycle,
    FakeRunEntryStore RunEntries,
    FakeProcessLauncher Launcher,
    FakeAgentLifecycleClient AgentClient,
    FakeProcessMonitor? ProcessMonitor,
    string ApplicationDirectory,
    string DesktopExecutable);

internal sealed class FakeRunEntryStore : ICurrentUserRunEntryStore
{
    public List<(string Name, string CommandLine)> SetCalls { get; } = [];

    public List<string> RemovedNames { get; } = [];

    public void SetValue(string valueName, string commandLine) =>
        SetCalls.Add((valueName, commandLine));

    public void Remove(string valueName) => RemovedNames.Add(valueName);
}

internal sealed class FakeProcessLauncher : IAgentProcessLauncher
{
    public List<(string Executable, string WorkingDirectory, string Argument)> Launches { get; } = [];

    public bool TryLaunchHidden(string executablePath, string workingDirectory, string argument)
    {
        Launches.Add((executablePath, workingDirectory, argument));
        return true;
    }
}

internal sealed class FakeAgentLifecycleClient : IPackagedAgentLifecycleClient
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

internal sealed class FakeProcessMonitor(bool running, bool terminateResult) : IPackagedAgentProcessMonitor
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
