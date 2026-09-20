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

    // There was a WindowsService = 1 here: an agent running as LocalSystem, from boot, with nobody
    // signed in. It was removed because it bought one thing - schedules surviving sign-out on
    // Windows - and charged for it everywhere: UAC elevation, a service installer and an
    // administrator-only staged copy of the binaries, a machine-wide vault any administrator could
    // read, a second DPAPI scope with re-protection whenever the mode changed, and a SID-based ACL
    // on the pipe. Linux never needed any of it, because `systemd --user` with lingering keeps a
    // per-user agent running after sign-out with no privilege at all.
    //
    // StorageHub now runs one agent per user on both platforms. On Windows that means scheduled
    // work does not progress while signed out; everything else about the agent is simpler for it.
}

/// <summary>
/// The paths, names and identities each <see cref="AgentHostMode"/> implies.
/// </summary>
public static class AgentHostLayout
{
    /// <summary>Overrides the resolved data root; already honoured by the agent at startup.</summary>
    public const string DataRootVariable = "STORAGEHUB_DATA_ROOT";

    /// <summary>
    /// The name of the per-user autostart registration.
    /// </summary>
    /// <remarks>
    /// Shared so that whatever writes it and whatever reads it cannot disagree. They did: the
    /// desktop has always written "StorageHub.Agent" under HKCU Run, while the agent platform's own
    /// registration defaulted to "StorageHub" - so an agent that registered its own autostart wrote
    /// a value the desktop would never find, and the desktop would report the session mode as
    /// AppSession however the machine was actually set up. On Linux this is the systemd unit's name.
    /// </remarks>
    public const string AutostartEntryName = "StorageHub.Agent";

    /// <summary>
    /// Command-line switch that applies one named installation repair and reports through the
    /// exit code. Declared here rather than beside the other service commands because the desktop
    /// launches it and cannot reference the agent executable.s own types.
    /// </summary>


    /// <summary>
    /// Where the database, vault and logs live. One root per machine on Windows, whatever the mode.
    /// </summary>
    /// <remarks>
    /// This is the agent's data, not the desktop's. Preferences - theme, shortcuts, the toolbar -
    /// stay per-user, because only the desktop reads them and it always runs as the user. What moved
    /// is the state both a service and a session agent have to see.
    /// </remarks>
    public static string ResolveDataRoot(AgentHostMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        // Every Windows mode shares one root under %PROGRAMDATA%.
        //
        // It used to depend on the mode - the service under ProgramData, a session agent under
        // %LOCALAPPDATA% - and that is the source of a recurring class of problem rather than a
        // safeguard. The two roots are invisible to each other, so anything that changes the mode
        // moves the whole installation: install the service and the desktop's saved connections
        // vanish; uninstall it and they come back while the ones added since do not. A service runs
        // as LocalSystem and cannot read a user's LocalAppData at all, so there is no repair path
        // either, and the symptom every time is an empty installation rather than an error.
        //
        // The trade is real and deliberate: one root is machine-wide, so a second Windows user on
        // the same machine shares this installation instead of having one of their own. For a
        // single-user machine, which is what StorageHub is installed on, that is the behaviour
        // people already expect. STORAGEHUB_DATA_ROOT still overrides it.
        var baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
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

    /// <summary>
    /// Where a session agent kept its data before the roots were merged.
    /// </summary>
    /// <remarks>
    /// Nothing writes here any more. It is still worth knowing about: an installation upgraded from
    /// a build that split the root by mode has a populated database sitting in it, and the agent
    /// now looks somewhere else entirely. Pointing at it is the difference between "my connections
    /// are gone" and "my connections are over there".
    /// </remarks>
    public static string LegacyPerUserDatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StorageHub",
        AgentDirectoryName,
        DatabaseFileName);

    /// <summary>The agent database for a mode, whether or not it exists yet.</summary>
    public static string ResolveDatabasePath(AgentHostMode mode) =>
        Path.Combine(ResolveAgentDirectory(mode), DatabaseFileName);

    /// <summary>The agent's subtree of the data root. Public because each platform lays it out.</summary>
    public const string AgentDirectoryName = "Agent";

    public const string DatabaseFileName = "storagehub.db";

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
            : (Ipc.Windows.StorageHubIpcPipeNames.Normal, Ipc.Windows.StorageHubIpcPipeNames.Secret);

}
