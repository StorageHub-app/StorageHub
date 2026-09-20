using System.Security.Principal;

namespace StorageHub.Agent;

/// <summary>
/// How the background agent is hosted. This is not a cosmetic preference: each mode changes where
/// the database lives, which DPAPI key protects the vault, and which pipe the desktop must find.
/// Those three have to move together, which is why they are derived here rather than decided
/// independently at each call site.
/// </summary>
public enum AgentHostMode
{
    /// <summary>
    /// A hidden process in the signed-in user's session, started by the desktop and kept alive by
    /// a per-user autostart entry. Secrets are protected with the user's own DPAPI key, so nothing
    /// else on the machine can read them -- and nothing runs while that user is signed out.
    /// </summary>
    UserSession = 0,

    /// <summary>
    /// Like <see cref="UserSession"/>, but with no autostart entry and no surviving the app: the
    /// agent starts with StorageHub and stops with it. Nothing is left running or registered on
    /// the machine, which is the point -- at the cost of transfers and schedules only progressing
    /// while the window is open.
    ///
    /// It shares the user's data root, DPAPI scope and pipes, because the only thing that differs
    /// is who ends the process.
    /// </summary>
    AppSession = 2,

    /// <summary>
    /// A Windows service running as LocalSystem. Runs from boot with nobody signed in, which is
    /// the only way a schedule can be relied on, at the cost of a machine-scoped vault that any
    /// administrator could read.
    /// </summary>
    WindowsService = 1,
}

/// <summary>
/// The paths, names and identities each <see cref="AgentHostMode"/> implies.
/// </summary>
public static class AgentHostLayout
{
    /// <summary>The service's registered name, used by the SCM and by the desktop's status check.</summary>
    public const string ServiceName = "StorageHubAgent";

    public const string ServiceDisplayName = "StorageHub Agent";

    /// <summary>Overrides the resolved data root; already honoured by the agent at startup.</summary>
    public const string DataRootVariable = "STORAGEHUB_DATA_ROOT";

    /// <summary>Command-line switch that runs the agent under the service control manager.</summary>
    public const string ServiceArgument = "--service";

    /// <summary>
    /// Command-line switch that applies one named installation repair and reports through the
    /// exit code. Declared here rather than beside the other service commands because the desktop
    /// launches it and cannot reference the agent executable.s own types.
    /// </summary>
    public const string RepairArgument = "--repair-installation";

    private const string MachinePipePrefix = "StorageHub.Agent.v1.machine";
    private const string MachineSecretPipePrefix = "StorageHub.Agent.Secrets.v1.machine";

    /// <summary>
    /// Where the database, vault and logs live. A service running as LocalSystem would otherwise
    /// resolve LocalApplicationData to the system profile, silently presenting an empty
    /// installation rather than the user's saved connections.
    /// </summary>
    public static string ResolveDataRoot(AgentHostMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var baseFolder = mode == AgentHostMode.WindowsService
            ? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseFolder))
        {
            throw new InvalidOperationException("Windows did not report a data folder for this mode.");
        }

        return Path.Combine(baseFolder, "StorageHub");
    }

    /// <summary>
    /// Where the agent keeps its own files inside the data root. Spelled out here because the
    /// convention was repeated at four call sites, and a repair that looks in the wrong place is
    /// worse than no repair at all.
    /// </summary>
    public static string ResolveAgentDirectory(AgentHostMode mode) =>
        Path.Combine(ResolveDataRoot(mode), AgentDirectoryName);

    /// <summary>The agent database for a mode, whether or not it exists yet.</summary>
    public static string ResolveDatabasePath(AgentHostMode mode) =>
        Path.Combine(ResolveAgentDirectory(mode), DatabaseFileName);

    internal const string AgentDirectoryName = "Agent";

    internal const string DatabaseFileName = "storagehub.db";

    /// <summary>
    /// Pipe names for the mode. The per-user names hash the account SID so two signed-in users
    /// never share an agent; the service names deliberately do not, because the whole point is
    /// that one machine-wide agent serves whoever is signed in. Access is then decided by the
    /// pipe's ACL rather than by the name being unguessable.
    /// </summary>
    /// <remarks>
    /// Windows-only, and CA1416 now says so rather than leaving it to be discovered at runtime.
    /// Both halves are: the machine names address a Windows service, and the per-user names hash a
    /// Windows account SID. An endpoint on Linux is a socket path under XDG_RUNTIME_DIR whose
    /// privacy comes from the directory's mode, so this does not generalise - it is replaced by
    /// IAgentPlatform.ResolveEndpoint rather than extended.
    /// </remarks>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static (string Normal, string Secret) ResolvePipeNames(AgentHostMode mode) =>
        !Enum.IsDefined(mode)
            ? throw new ArgumentOutOfRangeException(nameof(mode))
            : mode == AgentHostMode.WindowsService
                ? (MachinePipePrefix, MachineSecretPipePrefix)
                : (Ipc.Windows.StorageHubIpcPipeNames.Normal, Ipc.Windows.StorageHubIpcPipeNames.Secret);

    /// <summary>
    /// True when this process is already running with the privileges a service install needs.
    /// Installing or removing a service is an administrative act, so the desktop has to know
    /// whether to ask Windows to elevate before offering the choice.
    /// </summary>
    public static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
