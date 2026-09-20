using Velopack.Locators;

namespace StorageHub.Desktop;

/// <summary>
/// The packaged lifecycle wired to this machine: the registry Run entry, a hidden process
/// launcher, and a named-pipe client to the agent.
/// </summary>
/// <remarks>
/// This was PackagedDesktopLifecycle.CreateDefault, which is what kept an otherwise portable class
/// -- 470 lines of autostart policy, readiness waiting and shutdown ordering, with an interface per
/// dependency precisely so it could be tested without any of them -- pinned to the Windows shell.
/// The class moved to Desktop.Core; only the three implementations it names are Windows, and they
/// are here.
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
}
