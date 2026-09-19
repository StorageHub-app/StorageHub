using System.ComponentModel;
using System.Security;
using Velopack.Locators;

namespace StorageHub.Desktop;

public enum AgentEnsureStatus
{
    AlreadyRunning,
    Started,
    MissingExecutable,
    LaunchFailed,
    StartupTimedOut
}

public sealed record AgentEnsureResult(AgentEnsureStatus Status)
{
    public bool IsReady => Status is AgentEnsureStatus.AlreadyRunning or AgentEnsureStatus.Started;
}

public enum AgentShutdownReason
{
    Update,
    Uninstall,
    Restart
}

public enum AutostartConfigurationStatus
{
    Registered,
    Disabled,
    Removed,
    Failed
}

public interface ICurrentUserRunEntryStore
{
    void SetValue(string valueName, string commandLine);

    void Remove(string valueName);
}

public interface IAgentProcessLauncher
{
    bool TryLaunchHidden(string executablePath, string workingDirectory, string argument);
}

public interface IPackagedAgentLifecycleClient
{
    ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    ValueTask<bool> WaitUntilAvailableAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    ValueTask<bool> RequestShutdownAndWaitAsync(
        AgentShutdownReason reason,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

public interface IPackagedAgentProcessMonitor
{
    bool IsRunning(string executablePath);

    ValueTask<bool> TryTerminateAsync(
        string executablePath,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    ValueTask<bool> WaitForExitAsync(
        int processId,
        string executablePath,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

public sealed record PackagedDesktopLifecycleOptions
{
    public const string DisableAutostartEnvironmentVariable = "STORAGEHUB_DISABLE_AUTOSTART";

    public string RunEntryName { get; init; } = "StorageHub.Agent";

    public string AgentSubdirectory { get; init; } = "Agent";

    public string AgentExecutableName { get; init; } = "StorageHub.Agent.Windows.exe";

    public string AgentArgument { get; init; } = "--background";

    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// How long to wait for a service-hosted agent to publish its pipe.
    /// </summary>
    /// <remarks>
    /// Longer than <see cref="StartupTimeout"/> on purpose. That one covers an agent this process
    /// launched, at a moment of its own choosing, on a machine already running. This one covers a
    /// service Windows is starting concurrently with the sign-in that starts the desktop -- a cold
    /// boot contending for the same disk, with no way for the desktop to hurry it along. Spending
    /// the difference is free when the agent is already up, because the wait returns as soon as the
    /// pipe answers.
    ///
    /// Twelve seconds is the ceiling the lifecycle client itself enforces on any wait, so that is
    /// the most this can ask for. A boot slower than that still recovers: the window opens and the
    /// shell's own reconnect reports the agent arriving, rather than refusing to start at all.
    /// </remarks>
    public TimeSpan ServiceReadyTimeout { get; init; } = TimeSpan.FromSeconds(12);

    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Whether this process owns the agent's lifetime. False under a Windows service, where the
    /// service control manager owns it. Injected rather than read from the machine so the
    /// lifecycle stays deterministic: reading the service control manager directly would make
    /// this class -- and its tests -- behave differently depending on the machine they run on.
    /// </summary>
    public Func<bool> DesktopOwnsAgent { get; init; } = static () => DesktopAgentHost.DesktopStartsAgent;

    /// <summary>
    /// Whether a logon entry should exist. Narrower than <see cref="DesktopOwnsAgent"/>: the
    /// desktop starts the agent in both session modes, but only one of them wants Windows to start
    /// it again at sign-in. Injected for the same reason -- so the lifecycle never reads machine
    /// state behind its tests' backs.
    /// </summary>
    public Func<bool> RegistersAutostart { get; init; } = static () => DesktopAgentHost.RegistersAutostart;

    internal void Validate()
    {
        ValidateIdentity(RunEntryName, nameof(RunEntryName));
        ValidateSinglePathSegment(AgentSubdirectory, nameof(AgentSubdirectory));
        ValidateSinglePathSegment(AgentExecutableName, nameof(AgentExecutableName));
        ValidateArgument(AgentArgument, nameof(AgentArgument));
        ValidateTimeout(StartupTimeout, nameof(StartupTimeout));
        ValidateTimeout(ServiceReadyTimeout, nameof(ServiceReadyTimeout));
        ValidateTimeout(ShutdownTimeout, nameof(ShutdownTimeout));
    }

    private static void ValidateIdentity(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl))
        {
            throw new ArgumentException(
                "The lifecycle identity must be between 1 and 128 non-control characters.",
                parameterName);
        }
    }

    private static void ValidateSinglePathSegment(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value is "." or ".." ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            value.Contains(Path.DirectorySeparatorChar) ||
            value.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("A single valid path segment is required.", parameterName);
        }
    }

    private static void ValidateArgument(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl))
        {
            throw new ArgumentException("A bounded, non-control process argument is required.", parameterName);
        }
    }

    private static void ValidateTimeout(TimeSpan value, string parameterName)
    {
        if (value <= TimeSpan.Zero || value > TimeSpan.FromSeconds(12))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Package lifecycle waits must be between zero and twelve seconds.");
        }
    }

}

/// <summary>
/// Owns the installed Desktop's narrow responsibilities: the per-user logon command,
/// starting the sibling background Agent, and requesting a bounded graceful stop.
/// It never reads, writes, or removes StorageHub's durable data directory.
/// </summary>
public sealed class PackagedDesktopLifecycle : IDisposable
{
    private readonly SemaphoreSlim _ensureGate = new(1, 1);
    private readonly string _desktopExecutablePath;
    private readonly string _agentDirectory;
    private readonly string _agentExecutablePath;
    private readonly ICurrentUserRunEntryStore _runEntryStore;
    private readonly IAgentProcessLauncher _processLauncher;
    private readonly IPackagedAgentLifecycleClient _agentClient;
    private readonly IPackagedAgentProcessMonitor? _processMonitor;
    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly Func<string, bool> _fileExists;
    private readonly PackagedDesktopLifecycleOptions _options;

    public PackagedDesktopLifecycle(
        string desktopExecutablePath,
        string applicationDirectory,
        ICurrentUserRunEntryStore runEntryStore,
        IAgentProcessLauncher processLauncher,
        IPackagedAgentLifecycleClient agentClient,
        Func<string, string?> getEnvironmentVariable,
        Func<string, bool>? fileExists = null,
        PackagedDesktopLifecycleOptions? options = null,
        IPackagedAgentProcessMonitor? processMonitor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(desktopExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        ArgumentNullException.ThrowIfNull(runEntryStore);
        ArgumentNullException.ThrowIfNull(processLauncher);
        ArgumentNullException.ThrowIfNull(agentClient);
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        _options = options ?? new PackagedDesktopLifecycleOptions();
        _options.Validate();
        _desktopExecutablePath = RequireAbsoluteExecutablePath(
            desktopExecutablePath,
            nameof(desktopExecutablePath));
        if (!Path.IsPathFullyQualified(applicationDirectory))
        {
            throw new ArgumentException("An absolute application directory is required.", nameof(applicationDirectory));
        }

        var fullApplicationDirectory = Path.GetFullPath(applicationDirectory);
        _agentDirectory = Path.Combine(fullApplicationDirectory, _options.AgentSubdirectory);
        _agentExecutablePath = Path.Combine(_agentDirectory, _options.AgentExecutableName);
        _runEntryStore = runEntryStore;
        _processLauncher = processLauncher;
        _agentClient = agentClient;
        _processMonitor = processMonitor;
        _getEnvironmentVariable = getEnvironmentVariable;
        _fileExists = fileExists ?? File.Exists;
    }

    public string AgentExecutablePath => _agentExecutablePath;

    public string AutostartCommandLine => $"{QuoteWindowsArgument(_desktopExecutablePath)} --agent-only";

    public static PackagedDesktopLifecycle CreateDefault()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("The StorageHub Desktop executable path is unavailable.");
        }

        var applicationDirectory = AppContext.BaseDirectory;
        var options = new PackagedDesktopLifecycleOptions();
        var agentExecutablePath = Path.Combine(
            applicationDirectory,
            options.AgentSubdirectory,
            options.AgentExecutableName);
        var processMonitor = new WindowsPackagedAgentProcessMonitor();
        return new PackagedDesktopLifecycle(
            ResolveStableDesktopExecutable(executablePath),
            applicationDirectory,
            new WindowsCurrentUserRunEntryStore(),
            new WindowsHiddenAgentProcessLauncher(),
            new NamedPipePackagedAgentLifecycleClient(
                DesktopApplicationVersion.Current,
                agentExecutablePath,
                processMonitor),
            Environment.GetEnvironmentVariable,
            File.Exists,
            options,
            processMonitor);
    }

    private static string ResolveStableDesktopExecutable(string executablePath)
    {
        if (!VelopackLocator.IsCurrentSet)
        {
            return executablePath;
        }

        var rootDirectory = VelopackLocator.Current.RootAppDir;
        if (string.IsNullOrWhiteSpace(rootDirectory) ||
            !Path.IsPathFullyQualified(rootDirectory))
        {
            return executablePath;
        }

        // Velopack gives the root execution stub the same filename as the
        // packaged main executable. Point logon startup at that stable root
        // stub so it remains valid while Velopack replaces current.
        var stableExecutable = Path.Combine(rootDirectory, Path.GetFileName(executablePath));
        return File.Exists(stableExecutable) ? stableExecutable : executablePath;
    }

    /// <param name="force">
    /// Registers regardless of the current mode. Used only by the post-install hook, where no mode
    /// has been chosen yet: with no service and no entry, mode detection would read a brand-new
    /// installation as "only while the app is open" and quietly change what a fresh install does.
    /// </param>
    public AutostartConfigurationStatus ConfigureAutostart(bool force = false)
    {
        try
        {
            // Under a service there is nothing to start at sign-in, and an entry that survived the
            // switch would quietly resurrect a session agent beside the service -- two agents on
            // two different databases. This runs after every update as well as after install, so
            // without the check a single update would undo whichever mode was chosen.
            if (IsAutostartDisabled || (!force && !_options.RegistersAutostart()))
            {
                _runEntryStore.Remove(_options.RunEntryName);
                return AutostartConfigurationStatus.Disabled;
            }

            _runEntryStore.SetValue(_options.RunEntryName, AutostartCommandLine);
            return AutostartConfigurationStatus.Registered;
        }
        catch (Exception error) when (IsExpectedPlatformFailure(error))
        {
            return AutostartConfigurationStatus.Failed;
        }
    }

    public bool IsAutostartDisabled => string.Equals(
        _getEnvironmentVariable(PackagedDesktopLifecycleOptions.DisableAutostartEnvironmentVariable)?.Trim(),
        "1",
        StringComparison.Ordinal);

    public AutostartConfigurationStatus RemoveAutostart()
    {
        try
        {
            _runEntryStore.Remove(_options.RunEntryName);
            return AutostartConfigurationStatus.Removed;
        }
        catch (Exception error) when (IsExpectedPlatformFailure(error))
        {
            return AutostartConfigurationStatus.Failed;
        }
    }

    public async ValueTask<AgentEnsureResult> EnsureAgentAsync(
        CancellationToken cancellationToken = default)
    {
        await _ensureGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Under a service the agent's lifetime belongs to Windows, not to this process. Left
            // out, the desktop would start a second agent in the session beside the service --
            // two processes on one database, and a desktop talking to whichever it found first.
            //
            // Not starting it is not the same as not waiting for it. This asked once and gave up,
            // which lost a race it runs at every sign-in: Windows brings the auto-start service up
            // alongside the session, the service answers the control manager immediately and then
            // spends seconds opening its database and subsystems before the pipe exists, and the
            // desktop had already decided the agent was never coming. Waiting is what the session
            // branch below does for an agent it launched itself, and a service the desktop does not
            // control deserves it at least as much.
            if (!_options.DesktopOwnsAgent())
            {
                return new AgentEnsureResult(
                    await _agentClient
                        .WaitUntilAvailableAsync(_options.ServiceReadyTimeout, cancellationToken)
                        .ConfigureAwait(false)
                        ? AgentEnsureStatus.AlreadyRunning
                        : AgentEnsureStatus.StartupTimedOut);
            }

            var available = await _agentClient.IsAvailableAsync(cancellationToken).ConfigureAwait(false);
            if (available && (_processMonitor is null || _processMonitor.IsRunning(_agentExecutablePath)))
            {
                return new AgentEnsureResult(AgentEnsureStatus.AlreadyRunning);
            }

            if (available)
            {
                var stopped = await _agentClient.RequestShutdownAndWaitAsync(
                    AgentShutdownReason.Restart,
                    _options.ShutdownTimeout,
                    cancellationToken).ConfigureAwait(false);
                if (!stopped || !await WaitUntilUnavailableAsync(cancellationToken).ConfigureAwait(false))
                {
                    return new AgentEnsureResult(AgentEnsureStatus.LaunchFailed);
                }
            }

            // A process can be alive while its IPC listener is still starting, temporarily
            // saturated, or irrecoverably hung. Give the existing instance the full startup
            // window before replacing only the exact packaged Agent executable. Launching a
            // second copy immediately just makes the data-directory singleton reject it.
            if (_processMonitor?.IsRunning(_agentExecutablePath) == true)
            {
                if (await _agentClient
                    .WaitUntilAvailableAsync(_options.StartupTimeout, cancellationToken)
                    .ConfigureAwait(false))
                {
                    return new AgentEnsureResult(AgentEnsureStatus.AlreadyRunning);
                }

                if (!await _processMonitor
                    .TryTerminateAsync(
                        _agentExecutablePath,
                        _options.ShutdownTimeout,
                        cancellationToken)
                    .ConfigureAwait(false))
                {
                    return new AgentEnsureResult(AgentEnsureStatus.LaunchFailed);
                }
            }

            if (!_fileExists(_agentExecutablePath))
            {
                return new AgentEnsureResult(AgentEnsureStatus.MissingExecutable);
            }

            if (!_processLauncher.TryLaunchHidden(
                    _agentExecutablePath,
                    _agentDirectory,
                    _options.AgentArgument))
            {
                return new AgentEnsureResult(AgentEnsureStatus.LaunchFailed);
            }

            var becameAvailable = await _agentClient
                .WaitUntilAvailableAsync(_options.StartupTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (becameAvailable)
            {
                return new AgentEnsureResult(AgentEnsureStatus.Started);
            }

            // A child which exits during startup is a launch failure, not a slow
            // but otherwise healthy Agent. This also gives the desktop actionable
            // wording for rejected data directories and other fail-fast errors.
            return _processMonitor is not null && !_processMonitor.IsRunning(_agentExecutablePath)
                ? new AgentEnsureResult(AgentEnsureStatus.LaunchFailed)
                : new AgentEnsureResult(AgentEnsureStatus.StartupTimedOut);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (IsExpectedPlatformFailure(error))
        {
            return new AgentEnsureResult(AgentEnsureStatus.LaunchFailed);
        }
        finally
        {
            _ensureGate.Release();
        }
    }

    private async ValueTask<bool> WaitUntilUnavailableAsync(CancellationToken cancellationToken)
    {
        var deadline = TimeProvider.System.GetUtcNow() + _options.ShutdownTimeout;
        while (TimeProvider.System.GetUtcNow() < deadline)
        {
            if (!await _agentClient.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
            {
                return true;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
        }
        return false;
    }

    public async ValueTask<bool> TryStopAgentAsync(
        AgentShutdownReason reason,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _agentClient
                .RequestShutdownAndWaitAsync(reason, _options.ShutdownTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception error) when (IsExpectedPlatformFailure(error))
        {
            return false;
        }
    }

    public void Dispose()
    {
        // Program owns this lifecycle for the entire UI lifetime. If window shutdown
        // cancels an in-flight recovery, let its finally block leave the gate before
        // releasing the native wait handle.
        _ensureGate.Wait();
        _ensureGate.Release();
        _ensureGate.Dispose();
    }

    private static string RequireAbsoluteExecutablePath(string value, string parameterName)
    {
        if (!Path.IsPathFullyQualified(value))
        {
            throw new ArgumentException("An absolute executable path is required.", parameterName);
        }

        var fullPath = Path.GetFullPath(value);
        if (fullPath.Contains('"'))
        {
            throw new ArgumentException("An absolute executable path is required.", parameterName);
        }

        return fullPath;
    }

    private static string QuoteWindowsArgument(string value) => $"\"{value}\"";

    private static bool IsExpectedPlatformFailure(Exception error) => error is
        IOException or
        UnauthorizedAccessException or
        SecurityException or
        Win32Exception or
        InvalidDataException or
        InvalidOperationException or
        TimeoutException;
}

public sealed class DesktopPackageLifecycleHooks(PackagedDesktopLifecycle lifecycle)
{
    private readonly PackagedDesktopLifecycle _lifecycle = lifecycle ??
        throw new ArgumentNullException(nameof(lifecycle));
    private readonly Func<bool> _unregisterExplorerDropBroker = ExplorerDropBrokerInstaller.Unregister;

    internal DesktopPackageLifecycleHooks(
        PackagedDesktopLifecycle lifecycle,
        Func<bool> unregisterExplorerDropBroker)
        : this(lifecycle)
    {
        _unregisterExplorerDropBroker = unregisterExplorerDropBroker ??
            throw new ArgumentNullException(nameof(unregisterExplorerDropBroker));
    }

    // A fresh install has no mode yet, so it establishes the historical default rather than letting
    // an absent logon entry be read as a deliberate choice.
    public void AfterInstall() => _ = _lifecycle.ConfigureAutostart(force: true);

    public void AfterUpdate() => _ = _lifecycle.ConfigureAutostart();

    public void BeforeUpdate() => StopSynchronously(AgentShutdownReason.Update);

    public void BeforeUninstall()
    {
        StopSynchronously(AgentShutdownReason.Uninstall);
        _ = _lifecycle.RemoveAutostart();
        TryRemoveAgentService();
        _ = _unregisterExplorerDropBroker();
    }

    /// <summary>
    /// Removes the agent service so uninstalling does not leave an auto-starting LocalSystem
    /// service behind, running binaries from a machine directory for an application that is gone.
    ///
    /// Best effort: StorageHub installs per user, so the uninstaller usually has no elevated
    /// token, and a Velopack fast hook is the wrong place to raise a consent prompt. The data in
    /// ProgramData is deliberately left alone either way -- it holds credentials, and uninstalling
    /// an application is not consent to destroy them.
    /// </summary>
    private static void TryRemoveAgentService()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            StorageHub.Agent.AgentServiceInstaller.Uninstall();
        }
        catch (Exception)
        {
            // Not elevated, or the service is already gone. Switching back to a session mode
            // before uninstalling is the clean path, and is what the release notes say.
        }
    }

    private void StopSynchronously(AgentShutdownReason reason)
    {
        // Velopack fast hooks cannot veto an update or uninstall. Give the
        // Agent a bounded graceful-stop window; if it cannot acknowledge,
        // Velopack's normal locking-process handling remains the final fallback.
        _ = _lifecycle.TryStopAgentAsync(reason).AsTask().GetAwaiter().GetResult();
    }
}

public static class DesktopCommandLine
{
    public const string AgentOnlyArgument = "--agent-only";

    /// <summary>
    /// Arguments CodeLogic's own parser claims. It reads <see cref="Environment.GetCommandLineArgs"/>
    /// unconditionally during initialization and there is no option to suppress that, so a desktop
    /// launched with one of these would have the framework write to a console a WinExe does not own
    /// and then ask the process to exit — no window, no message, no exit code the user can read.
    /// The shell answers for these itself instead.
    /// </summary>
    private static readonly string[] FrameworkArguments =
    [
        "--version",
        "--info",
        "--health",
        "--dry-run",
        "--generate-configs",
        "--generate-configs-force"
    ];

    public static bool IsAgentOnly(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.Any(argument => string.Equals(
            argument,
            AgentOnlyArgument,
            StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Handles the arguments CodeLogic would otherwise intercept, before the framework is
    /// initialized.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the shell should exit without starting, with
    /// <paramref name="exitCode"/> set.
    /// </returns>
    public static bool TryHandleFrameworkArguments(IEnumerable<string> arguments, out int exitCode)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        exitCode = 0;

        var claimed = arguments.FirstOrDefault(argument => FrameworkArguments.Contains(
            argument,
            StringComparer.OrdinalIgnoreCase));
        if (claimed is null)
        {
            return false;
        }

        // --version is the one a user plausibly means, and answering it costs nothing. The rest
        // describe a framework the desktop hosts but does not administer; the agent is the process
        // that answers those.
        if (string.Equals(claimed, "--version", StringComparison.OrdinalIgnoreCase))
        {
            Console.Out.WriteLine(DesktopApplicationVersion.Current);
            return true;
        }

        Console.Error.WriteLine(
            $"StorageHub Desktop does not support {claimed}. Run the StorageHub Agent with that argument instead.");
        exitCode = 2;
        return true;
    }
}
