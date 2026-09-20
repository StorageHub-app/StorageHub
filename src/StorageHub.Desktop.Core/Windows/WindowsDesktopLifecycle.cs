namespace StorageHub.Desktop;

/// <summary>
/// The packaged lifecycle wired to this machine: the registry Run entry, a hidden process
/// launcher, and a named-pipe client to the agent.
/// </summary>
/// <remarks>
/// <para>
/// This was PackagedDesktopLifecycle.CreateDefault, which is what kept an otherwise portable class
/// -- 470 lines of autostart policy, readiness waiting and shutdown ordering, with an interface per
/// dependency precisely so it could be tested without any of them -- pinned to the Windows shell.
/// The class moved to Desktop.Core; only the three implementations it names are Windows, and they
/// are here beside it, guarded rather than held in a project of their own.
/// </para>
/// <para>
/// The executable path used to be resolved through Velopack, which gave the root execution stub the
/// packaged executable's filename so the logon entry stayed valid while Velopack replaced
/// "current". An MSI installs to a directory that does not move, so the running executable's own
/// path is already the stable one.
/// </para>
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class WindowsDesktopLifecycle
{
    public static PackagedDesktopLifecycle Create()
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
            executablePath,
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
}
