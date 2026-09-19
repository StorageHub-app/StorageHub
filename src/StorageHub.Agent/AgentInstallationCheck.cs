namespace StorageHub.Agent;

/// <summary>
/// Whether a path is there, absent, or simply not visible from here.
///
/// The third case is not pedantry. A service data root is machine-owned and denies the signed-in
/// user any read at all, which is the point of it -- and File.Exists answers false for a path it
/// cannot open. Collapsing that into "missing" told a machine with a healthy, running service
/// that its database had vanished, and offered to create a directory that was already there.
/// </summary>
public enum PathVisibility
{
    Present = 0,
    Missing = 1,

    /// <summary>It may or may not exist; this account is not allowed to find out.</summary>
    Denied = 2,
}

/// <summary>How one installation check came out.</summary>
public enum InstallationCheckStatus
{
    /// <summary>Nothing to do.</summary>
    Ok = 0,

    /// <summary>Works today, but will not survive something ordinary happening.</summary>
    Warning = 1,

    /// <summary>Broken now: the agent is unreachable, or its state is not where it should be.</summary>
    Problem = 2,
}

/// <summary>
/// The action that resolves a finding, where one exists. Named rather than described so the
/// caller can offer it as a button and apply it without parsing a sentence.
/// </summary>
public enum InstallationRepair
{
    /// <summary>Nothing to apply; the finding is reported for the operator to act on.</summary>
    None = 0,

    /// <summary>Create the agent's data directory.</summary>
    CreateAgentDirectory = 1,

    /// <summary>Start the installed service.</summary>
    StartService = 2,

    /// <summary>Tell Windows to restart the service when it dies.</summary>
    ConfigureServiceRecovery = 3,

    /// <summary>Copy the current agent over the staged one the service runs from.</summary>
    RestageAgent = 4,
}

/// <summary>One thing that was checked, and what was found.</summary>
/// <param name="Title">A short name for the check, suitable for a list.</param>
/// <param name="Status">Whether it is fine, fragile, or broken.</param>
/// <param name="Detail">A sentence saying what was found and why it matters.</param>
/// <param name="Location">The path, pipe or service the check looked at, when there is one.</param>
/// <param name="Repair">What would fix it, when something would.</param>
/// <param name="RequiresElevation">Whether applying <paramref name="Repair"/> needs an admin token.</param>
public sealed record InstallationFinding(
    string Title,
    InstallationCheckStatus Status,
    string Detail,
    string? Location = null,
    InstallationRepair Repair = InstallationRepair.None,
    bool RequiresElevation = false);

/// <summary>Everything the check looked at, for one host mode.</summary>
public sealed record InstallationReport(AgentHostMode Mode, IReadOnlyList<InstallationFinding> Findings)
{
    /// <summary>The worst thing found, which is what a summary line should say.</summary>
    public InstallationCheckStatus Worst =>
        Findings.Count == 0 ? InstallationCheckStatus.Ok : Findings.Max(finding => finding.Status);

    /// <summary>The findings something can actually be done about.</summary>
    public IReadOnlyList<InstallationFinding> Repairable =>
        [.. Findings.Where(finding => finding.Repair != InstallationRepair.None)];
}

/// <summary>
/// The machine facts the check needs. Behind an interface because every one of them is a
/// filesystem, service control manager or named pipe call, and a check nobody can run offline is
/// a check nobody tests.
/// </summary>
public interface IInstallationProbe
{
    PathVisibility InspectDirectory(string path);

    PathVisibility InspectFile(string path);

    /// <summary>Size in bytes, or null when the file cannot be read.</summary>
    long? FileLength(string path);

    /// <summary>Whether anything is listening on the pipe right now.</summary>
    bool PipeExists(string pipeName);

    AgentServiceState DescribeService();

    /// <summary>Whether Windows has been told to restart the service after a failure.</summary>
    bool ServiceRestartsOnFailure();

    /// <summary>The version staged for the service, or null when nothing is staged.</summary>
    string? ReadStagedVersion(string executableName);

    /// <summary>Where the service runs its binaries from. Asked of the probe rather than
    /// resolved here, so the check itself stays free of platform-only calls.</summary>
    string StagedAgentDirectory { get; }

    bool IsElevated();
}

/// <summary>
/// Checks that an installation is actually in the shape its host mode says it is.
///
/// This exists because the failure it looks for is silent. A service that dies leaves no window
/// and no notification; the desktop simply reports that the agent did not become ready, which
/// reads as the desktop's fault. The state that decides it is spread over a per-user data root, a
/// machine data root, a staged binary tree, a service registration and a named pipe -- five
/// places, none of which an operator can reasonably be asked to inspect by hand.
/// </summary>
public static class AgentInstallationCheck
{
    /// <summary>
    /// Runs every check for <paramref name="mode"/>.
    ///
    /// <paramref name="runningVersion"/> is the version of the application asking, which is what
    /// the staged service copy is compared against.
    /// </summary>
    public static InstallationReport Inspect(
        AgentHostMode mode,
        string runningVersion,
        string agentExecutableName,
        IInstallationProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentExecutableName);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        List<InstallationFinding> findings =
        [
            CheckAgentDirectory(mode, probe),
            CheckDatabase(mode, probe),
        ];

        var stale = CheckStrayDataRoot(mode, probe);
        if (stale is not null)
        {
            findings.Add(stale);
        }

        if (mode == AgentHostMode.WindowsService)
        {
            var service = probe.DescribeService();
            findings.Add(CheckServiceInstalled(service));
            if (service.Installed)
            {
                findings.Add(CheckServiceRunning(service, probe));
                findings.Add(CheckServiceRecovery(probe));
                findings.Add(CheckStagedVersion(runningVersion, agentExecutableName, probe));
            }
        }

        findings.Add(CheckPipe(mode, probe));
        return new InstallationReport(mode, findings);
    }

    private static InstallationFinding CheckAgentDirectory(AgentHostMode mode, IInstallationProbe probe)
    {
        var directory = AgentHostLayout.ResolveAgentDirectory(mode);
        return probe.InspectDirectory(directory) switch
        {
            PathVisibility.Present =>
                new InstallationFinding("Data directory", InstallationCheckStatus.Ok, "Present.", directory),

            // Expected, and a good sign: the service data root denies the signed-in user any
            // read, which is what stops one account from rifling through a machine-wide vault.
            PathVisibility.Denied => new InstallationFinding(
                "Data directory",
                InstallationCheckStatus.Ok,
                "Machine-owned, and not readable from this account -- which is how the service keeps it.",
                directory),

            // A service directory lives under ProgramData, which an ordinary token cannot create.
            _ => new InstallationFinding(
                "Data directory",
                InstallationCheckStatus.Problem,
                "Missing. The agent keeps its database, vault and logs here and cannot start without it.",
                directory,
                InstallationRepair.CreateAgentDirectory,
                mode == AgentHostMode.WindowsService),
        };
    }

    private static InstallationFinding CheckDatabase(AgentHostMode mode, IInstallationProbe probe)
    {
        var path = AgentHostLayout.ResolveDatabasePath(mode);
        var visibility = probe.InspectFile(path);
        if (visibility == PathVisibility.Denied)
        {
            return new InstallationFinding(
                "Database",
                InstallationCheckStatus.Ok,
                "Not readable from this account, which is expected while the service owns it.",
                path);
        }

        if (visibility == PathVisibility.Missing)
        {
            // Not a fault on its own: a fresh installation has no database until the agent makes
            // one. It is only worth saying so the operator can tell "new" from "lost".
            return new InstallationFinding(
                "Database",
                InstallationCheckStatus.Warning,
                "Not created yet. Expected for a new installation; unexpected if connections were saved before.",
                path);
        }

        var length = probe.FileLength(path);
        return length is null or 0
            ? new InstallationFinding(
                "Database",
                InstallationCheckStatus.Problem,
                "Present but empty, which means saved connections, schedules and queue history are not readable.",
                path)
            : new InstallationFinding("Database", InstallationCheckStatus.Ok, Describe(length.Value), path);
    }

    /// <summary>
    /// Looks for a database belonging to the mode that is <em>not</em> in use.
    ///
    /// Switching mode moves the data root, so a populated database left behind in the other root
    /// is the difference between "my connections are gone" and "my connections are over there".
    /// Saying which is which is most of the value of the whole check.
    /// </summary>
    private static InstallationFinding? CheckStrayDataRoot(AgentHostMode mode, IInstallationProbe probe)
    {
        var other = mode == AgentHostMode.WindowsService
            ? AgentHostMode.UserSession
            : AgentHostMode.WindowsService;
        var path = AgentHostLayout.ResolveDatabasePath(other);
        // Only a database this account can actually read and size is worth pointing at. One it
        // cannot see says nothing either way, and a guess here is the scare the check exists to
        // prevent.
        if (probe.InspectFile(path) != PathVisibility.Present || probe.FileLength(path) is null or 0)
        {
            return null;
        }

        var name = other == AgentHostMode.WindowsService ? "service" : "session";
        return new InstallationFinding(
            "Database from the other mode",
            InstallationCheckStatus.Warning,
            $"A populated {name} database is still on disk. Nothing reads it in this mode, so anything saved "
                + "into it is not missing -- it is only out of reach until that mode is selected again.",
            path);
    }

    private static InstallationFinding CheckServiceInstalled(AgentServiceState service) =>
        service.Installed
            ? new InstallationFinding("Service registered", InstallationCheckStatus.Ok, "Registered.", AgentHostLayout.ServiceName)
            : new InstallationFinding(
                "Service registered",
                InstallationCheckStatus.Problem,
                "This installation is set to run the agent as a service, but no service is registered. "
                    + "Nothing will run while nobody is signed in.",
                AgentHostLayout.ServiceName);

    private static InstallationFinding CheckServiceRunning(AgentServiceState service, IInstallationProbe probe) =>
        service.Running
            ? new InstallationFinding("Service running", InstallationCheckStatus.Ok, "Running.", AgentHostLayout.ServiceName)
            : new InstallationFinding(
                "Service running",
                InstallationCheckStatus.Problem,
                "Registered but stopped, so the desktop has nothing to connect to and reports that the agent "
                    + "did not become ready.",
                AgentHostLayout.ServiceName,
                InstallationRepair.StartService,
                !probe.IsElevated());

    /// <summary>
    /// Whether Windows will restart the agent after it dies.
    ///
    /// Without failure actions a crash is permanent: the service stays stopped until somebody
    /// starts it by hand or the machine reboots, and the only symptom is the desktop timing out.
    /// An Automatic service that owns the queue, the scheduler and the vault should not be one
    /// crash away from being gone for the rest of the week.
    /// </summary>
    private static InstallationFinding CheckServiceRecovery(IInstallationProbe probe) =>
        probe.ServiceRestartsOnFailure()
            ? new InstallationFinding("Restart after a crash", InstallationCheckStatus.Ok, "Windows will restart it.", AgentHostLayout.ServiceName)
            : new InstallationFinding(
                "Restart after a crash",
                InstallationCheckStatus.Warning,
                "Windows has no failure actions for this service, so if it stops unexpectedly it stays stopped "
                    + "until it is started by hand or the machine restarts.",
                AgentHostLayout.ServiceName,
                InstallationRepair.ConfigureServiceRecovery,
                !probe.IsElevated());

    /// <summary>
    /// Whether the staged copy the service runs from is the version the application expects.
    ///
    /// Updating the application cannot update this copy: re-staging needs an elevated token the
    /// desktop does not have, and the service must not fetch it itself, because SYSTEM copying a
    /// binary out of a user-writable directory is the escalation staging exists to prevent.
    /// </summary>
    private static InstallationFinding CheckStagedVersion(
        string runningVersion,
        string agentExecutableName,
        IInstallationProbe probe)
    {
        var staged = probe.ReadStagedVersion(agentExecutableName);
        var directory = probe.StagedAgentDirectory;
        if (staged is null)
        {
            return new InstallationFinding(
                "Staged agent",
                InstallationCheckStatus.Problem,
                "The service is registered but no agent is staged for it to run.",
                directory,
                InstallationRepair.RestageAgent,
                !probe.IsElevated());
        }

        return ReleasesMatch(staged, runningVersion)
            ? new InstallationFinding("Staged agent", InstallationCheckStatus.Ok, $"Version {Release(staged)}.", directory)
            : new InstallationFinding(
                "Staged agent",
                InstallationCheckStatus.Warning,
                $"The service is running {Release(staged)} while this application is {Release(runningVersion)}. "
                    + "Updating the application does not update the staged copy.",
                directory,
                InstallationRepair.RestageAgent,
                !probe.IsElevated());
    }

    private static InstallationFinding CheckPipe(AgentHostMode mode, IInstallationProbe probe)
    {
        var (normal, _) = AgentHostLayout.ResolvePipeNames(mode);
        return probe.PipeExists(normal)
            ? new InstallationFinding("Agent reachable", InstallationCheckStatus.Ok, "Answering on its pipe.", normal)
            : new InstallationFinding(
                "Agent reachable",
                InstallationCheckStatus.Problem,
                "Nothing is listening on the pipe this mode uses, which is what the desktop reports as the "
                    + "agent not becoming ready.",
                normal);
    }

    /// <summary>
    /// Compares only the release portion. Both sides carry a build suffix that differs between
    /// builds of the same version, and reporting that as a mismatch would send the operator
    /// through an elevation prompt that changes nothing.
    /// </summary>
    private static bool ReleasesMatch(string staged, string running) =>
        string.Equals(Release(staged), Release(running), StringComparison.OrdinalIgnoreCase);

    private static string Release(string version)
    {
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? version : version[..plus];
    }

    private static string Describe(long bytes) =>
        bytes >= 1024 * 1024
            ? $"{bytes / (1024.0 * 1024.0):0.#} MB."
            : $"{Math.Max(1, bytes / 1024)} KB.";
}
