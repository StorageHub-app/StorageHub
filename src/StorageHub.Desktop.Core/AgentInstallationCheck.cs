using StorageHub.Agent;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// Whether a path is there, absent, or simply not visible from here.
/// </summary>
/// <remarks>
/// The third case is not pedantry. File.Exists answers false for a path it cannot open, and
/// collapsing that into "missing" offered to create a directory that was already there. 1.4 met it
/// under the Windows service, whose root denied the signed-in user by design. 2.0's agent runs as
/// that user, so a root this account cannot open is now a fault, but it is still not a missing one.
/// </remarks>
internal enum PathVisibility
{
    Present = 0,
    Missing = 1,

    /// <summary>It may or may not exist; this account is not allowed to find out.</summary>
    Denied = 2,
}

/// <summary>How one installation check came out.</summary>
internal enum InstallationCheckStatus
{
    /// <summary>Nothing to do.</summary>
    Ok = 0,

    /// <summary>Works today, but worth knowing about.</summary>
    Warning = 1,

    /// <summary>Broken now: the agent is unreachable, or its state is not where it should be.</summary>
    Problem = 2,
}

/// <summary>
/// The action that resolves a finding, where one exists. Named rather than described so the
/// window can offer it as a button and apply it without parsing a sentence.
/// </summary>
/// <remarks>
/// 1.4 also started, re-staged and set failure actions on the Windows service. The service went in
/// 2.0 (AgentHostMode says why), and with it every repair that needed an administrator.
/// </remarks>
internal enum InstallationRepair
{
    /// <summary>Nothing to apply; the finding is reported for the reader to act on.</summary>
    None = 0,

    /// <summary>Create the agent's data directory.</summary>
    CreateAgentDirectory = 1,
}

/// <summary>One thing that was checked, and what was found.</summary>
/// <param name="Title">A short name for the check, suitable for a list.</param>
/// <param name="Status">Whether it is fine, worth knowing about, or broken.</param>
/// <param name="Detail">A sentence saying what was found and why it matters.</param>
/// <param name="Location">The path or endpoint the check looked at, when there is one.</param>
/// <param name="Repair">What would fix it, when something would.</param>
internal sealed record InstallationFinding(
    string Title,
    InstallationCheckStatus Status,
    string Detail,
    string? Location = null,
    InstallationRepair Repair = InstallationRepair.None);

/// <summary>Everything the check looked at, for the mode the agent is in.</summary>
/// <param name="Mode">How the agent is run, or null where systemd starts it (Linux).</param>
/// <param name="Findings">One per thing checked, in the order the window lists them.</param>
internal sealed record InstallationReport(AgentHostMode? Mode, IReadOnlyList<InstallationFinding> Findings)
{
    /// <summary>The worst thing found, which is what the summary line says.</summary>
    internal InstallationCheckStatus Worst =>
        Findings.Count == 0 ? InstallationCheckStatus.Ok : Findings.Max(finding => finding.Status);
}

/// <summary>What happened when a repair was applied.</summary>
internal sealed record InstallationRepairResult(bool Succeeded, string Message);

/// <summary>
/// The machine facts the check needs. Behind an interface because every one of them is a
/// filesystem or IPC call, and a check nobody can run offline is a check nobody tests.
/// </summary>
internal interface IInstallationProbe
{
    PathVisibility InspectDirectory(string path);

    PathVisibility InspectFile(string path);

    /// <summary>Size in bytes, or null when the file cannot be read.</summary>
    long? FileLength(string path);

    /// <summary>
    /// What the agent says about itself when asked right now, or Disconnected when nothing answers.
    /// </summary>
    Task<AgentConnectionState> AgentStateAsync(CancellationToken cancellationToken);
}

/// <summary>Where this machine's installation keeps the things the check looks at.</summary>
/// <param name="Paths">The agent's data root and what it keeps under it.</param>
/// <param name="Endpoint">Where the agent listens, as a log line would name it.</param>
/// <param name="LegacyDatabasePath">
/// Where a build that kept the database per user left it (Windows), or null where no build did.
/// </param>
/// <param name="AgentProgramPath">
/// The agent the desktop launches (Windows), or null where something else starts it.
/// </param>
/// <param name="AgentUnitPaths">
/// Where the systemd user unit that starts the agent may be (Linux), or null where there is none.
/// </param>
internal sealed record InstallationLayout(
    AgentPaths Paths,
    string Endpoint,
    string? LegacyDatabasePath = null,
    string? AgentProgramPath = null,
    IReadOnlyList<string>? AgentUnitPaths = null)
{
    /// <summary>
    /// The layout this desktop's agent uses, resolved the way the agent resolves it.
    /// </summary>
    /// <remarks>
    /// STORAGEHUB_DATA_ROOT is applied after the platform answers, as the agent host does, so the
    /// check looks where an agent started from this environment would write.
    /// </remarks>
    internal static InstallationLayout ForThisMachine()
    {
        var mode = DesktopAgentHost.Mode;
        var paths = DesktopAgentHost.Platform.ResolvePaths(mode);
        var overridden = Environment.GetEnvironmentVariable(AgentHostLayout.DataRootVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            paths = paths.WithDataRoot(overridden);
        }

        var endpoint = DesktopAgentHost.NormalEndpoint.Moniker;
        if (OperatingSystem.IsWindows())
        {
            var options = new PackagedDesktopLifecycleOptions();
            return new InstallationLayout(
                paths,
                endpoint,
                LegacyDatabasePath: AgentHostLayout.LegacyPerUserDatabasePath,
                AgentProgramPath: Path.Combine(
                    AppContext.BaseDirectory, options.AgentSubdirectory, options.AgentExecutableName));
        }

        if (OperatingSystem.IsLinux())
        {
            return new InstallationLayout(paths, endpoint, AgentUnitPaths: LinuxUnitPaths());
        }

        return new InstallationLayout(paths, endpoint);
    }

    /// <summary>
    /// The places systemd reads a user unit from that StorageHub puts one: the one the agent's own
    /// autostart writes, and the one the .deb installs for every user.
    /// </summary>
    private static string[] LinuxUnitPaths()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configHome))
        {
            var home = Environment.GetEnvironmentVariable("HOME")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            configHome = Path.Combine(home, ".config");
        }

        return
        [
            Path.Combine(configHome, "systemd", "user", SystemdAgentLifecycleController.UnitName),
            "/usr/lib/systemd/user/" + SystemdAgentLifecycleController.UnitName,
            "/etc/systemd/user/" + SystemdAgentLifecycleController.UnitName,
        ];
    }
}

/// <summary>
/// Checks that an installation is in the shape the agent needs, and says which part is not.
/// </summary>
/// <remarks>
/// <para>
/// Ported from 1.4, where it exists because the failure it looks for is silent: when the agent's
/// state is wrong, the only symptom is the desktop reporting that the agent did not become ready,
/// which points at the wrong thing entirely. The state that decides it is spread over a data root,
/// a database, whatever starts the agent and the endpoint it listens on.
/// </para>
/// <para>
/// 1.4's service checks (registered, running, restarted after a crash, staged version) went with
/// the service. What starts the agent is checked instead, since that is what fails in their place:
/// the packaged agent beside the desktop on Windows, the systemd user unit on Linux.
/// </para>
/// </remarks>
internal static class AgentInstallationCheck
{
    /// <summary>Checks this machine's installation, where an agent started from here would look.</summary>
    /// <remarks>
    /// No mode is named on Linux. The platform reads it from a unit file in the user's own systemd
    /// folder, and enabling the unit the .deb installs does not write one, so it would name "only
    /// while StorageHub is open" for every packaged install; the agent unit card says what matters.
    /// </remarks>
    internal static Task<InstallationReport> InspectThisMachineAsync(CancellationToken cancellationToken = default) =>
        InspectAsync(
            OperatingSystem.IsLinux() ? null : DesktopAgentHost.Mode,
            InstallationLayout.ForThisMachine(),
            new MachineInstallationProbe(),
            cancellationToken);

    /// <summary>Runs every check. It only looks, so it is safe to run as often as asked.</summary>
    internal static async Task<InstallationReport> InspectAsync(
        AgentHostMode? mode,
        InstallationLayout layout,
        IInstallationProbe probe,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(probe);

        List<InstallationFinding> findings =
        [
            CheckAgentDirectory(layout, probe),
            CheckDatabase(layout, probe),
        ];

        if (CheckLegacyDatabase(layout, probe) is { } legacy)
        {
            findings.Add(legacy);
        }

        if (layout.AgentProgramPath is { } program)
        {
            findings.Add(CheckAgentProgram(program, probe));
        }

        if (layout.AgentUnitPaths is { } units)
        {
            findings.Add(CheckAgentUnit(units, probe));
        }

        findings.Add(await CheckAgentAnswersAsync(layout, probe, cancellationToken).ConfigureAwait(false));
        return new InstallationReport(mode, findings);
    }

    /// <summary>The line above the findings: how it went, and how the agent is run.</summary>
    internal static string Summarize(InstallationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var mode = report.Mode switch
        {
            null => Ui.Updates.InstallationModeSystemd,
            AgentHostMode.AppSession => Ui.Updates.InstallationModeAppSession,
            _ => Ui.Updates.InstallationModeUserSession,
        };
        return report.Worst switch
        {
            InstallationCheckStatus.Ok => Ui.Format(Ui.Updates.InstallationSummaryOkFormat, mode),
            InstallationCheckStatus.Warning => Ui.Format(Ui.Updates.InstallationSummaryWarningFormat, mode),
            _ => Ui.Format(Ui.Updates.InstallationSummaryProblemFormat, mode),
        };
    }

    private static InstallationFinding CheckAgentDirectory(InstallationLayout layout, IInstallationProbe probe)
    {
        var directory = layout.Paths.AgentDirectory;
        var title = Ui.Updates.InstallationDataDirectory;
        return probe.InspectDirectory(directory) switch
        {
            PathVisibility.Present => new(title, InstallationCheckStatus.Ok, Ui.Updates.InstallationPresent, directory),

            // In 1.4 this was the service keeping its root to itself, and fine. The agent runs as
            // this account now, so a directory this account cannot open is one the agent cannot
            // open either. On Windows that is almost always the same root, still owned by the old
            // service, so the detail says what has to go before the agent can have it.
            PathVisibility.Denied => new(
                title,
                InstallationCheckStatus.Problem,
                OperatingSystem.IsWindows()
                    ? Ui.Updates.InstallationDataDirectoryDeniedByService
                    : Ui.Updates.InstallationDataDirectoryDenied,
                directory),

            _ => new(
                title,
                InstallationCheckStatus.Problem,
                Ui.Updates.InstallationDataDirectoryMissing,
                directory,
                InstallationRepair.CreateAgentDirectory),
        };
    }

    private static InstallationFinding CheckDatabase(InstallationLayout layout, IInstallationProbe probe)
    {
        var path = layout.Paths.DatabasePath;
        var title = Ui.Updates.InstallationDatabase;
        switch (probe.InspectFile(path))
        {
            case PathVisibility.Denied:
                return new(title, InstallationCheckStatus.Problem, Ui.Updates.InstallationDatabaseDenied, path);

            case PathVisibility.Missing:
                // Not a fault on its own: a fresh installation has no database until the agent
                // makes one. It is only worth saying so "new" can be told from "lost".
                return new(title, InstallationCheckStatus.Warning, Ui.Updates.InstallationDatabaseMissing, path);
        }

        var length = probe.FileLength(path);
        return length is null or 0
            ? new(title, InstallationCheckStatus.Problem, Ui.Updates.InstallationDatabaseEmpty, path)
            : new(title, InstallationCheckStatus.Ok, DescribeSize(length.Value), path);
    }

    /// <summary>
    /// Looks for a populated database where an earlier build kept it.
    /// </summary>
    /// <remarks>
    /// 1.4 pointed at the database of the mode not in use, because switching mode moved the root.
    /// 2.0 has one root, but a build that split it by mode left a database under the user's own
    /// folder, and that is the same difference: "my connections are gone" against "my connections
    /// are over there". Only one this account can read and size is pointed at; a guess here is the
    /// scare the check exists to prevent.
    /// </remarks>
    private static InstallationFinding? CheckLegacyDatabase(InstallationLayout layout, IInstallationProbe probe)
    {
        if (layout.LegacyDatabasePath is not { } path ||
            string.Equals(
                Path.GetFullPath(path),
                Path.GetFullPath(layout.Paths.DatabasePath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            probe.InspectFile(path) != PathVisibility.Present ||
            probe.FileLength(path) is null or 0)
        {
            return null;
        }

        return new(
            Ui.Updates.InstallationLegacyDatabase,
            InstallationCheckStatus.Warning,
            Ui.Updates.InstallationLegacyDatabaseDetail,
            path);
    }

    private static InstallationFinding CheckAgentProgram(string program, IInstallationProbe probe) =>
        probe.InspectFile(program) == PathVisibility.Present
            ? new(Ui.Updates.InstallationAgentProgram, InstallationCheckStatus.Ok, Ui.Updates.InstallationPresent, program)
            : new(
                Ui.Updates.InstallationAgentProgram,
                InstallationCheckStatus.Problem,
                Ui.Updates.InstallationAgentProgramMissing,
                program);

    /// <summary>
    /// Whether systemd has a unit to start the agent with. The .deb installs one for every user and
    /// the agent's own autostart writes one per user; either will do.
    /// </summary>
    private static InstallationFinding CheckAgentUnit(IReadOnlyList<string> units, IInstallationProbe probe)
    {
        var found = units.FirstOrDefault(unit => probe.InspectFile(unit) == PathVisibility.Present);
        return found is not null
            ? new(Ui.Updates.InstallationAgentUnit, InstallationCheckStatus.Ok, Ui.Updates.InstallationAgentUnitPresent, found)
            : new(
                Ui.Updates.InstallationAgentUnit,
                InstallationCheckStatus.Problem,
                Ui.Updates.InstallationAgentUnitMissing,
                units.Count > 0 ? units[0] : null);
    }

    /// <summary>
    /// Whether anything answers where the agent listens, and what it says about itself.
    /// </summary>
    /// <remarks>
    /// An agent still starting is answering. It is the case the splash offers this check for when
    /// the agent did not become ready in time, and calling it silent would blame the endpoint for
    /// what is a slow start.
    /// </remarks>
    private static async Task<InstallationFinding> CheckAgentAnswersAsync(
        InstallationLayout layout,
        IInstallationProbe probe,
        CancellationToken cancellationToken)
    {
        var (status, detail) = await probe.AgentStateAsync(cancellationToken).ConfigureAwait(false) switch
        {
            AgentConnectionState.Connected => (InstallationCheckStatus.Ok, Ui.Updates.InstallationAgentAnswers),
            AgentConnectionState.Starting or AgentConnectionState.Reconnecting =>
                (InstallationCheckStatus.Warning, Ui.Updates.InstallationAgentStarting),
            AgentConnectionState.RecoveryOnly =>
                (InstallationCheckStatus.Problem, Ui.Updates.TheAgentStartedButItsDurableState),
            _ => (InstallationCheckStatus.Problem, Ui.Updates.InstallationAgentSilent),
        };
        return new(Ui.Updates.InstallationAgentReachable, status, detail, layout.Endpoint);
    }

    private static string DescribeSize(long bytes) =>
        bytes >= 1024 * 1024
            ? Ui.Format(Ui.Updates.InstallationSizeMegabytesFormat, bytes / (1024.0 * 1024.0))
            : Ui.Format(Ui.Updates.InstallationSizeKilobytesFormat, Math.Max(1, bytes / 1024));
}

/// <summary>
/// Applies the repairs <see cref="AgentInstallationCheck"/> names.
/// </summary>
/// <remarks>
/// Kept apart from the check so that looking is always safe: only this changes the machine, and
/// only for a repair somebody asked for by name. Nothing here needs an administrator any more, so
/// 1.4's elevated helper, which ran the agent with the runas verb, has nothing left to do.
/// </remarks>
internal static class AgentInstallationRepair
{
    /// <summary>Applies one repair, returning rather than throwing: a failure is for reading.</summary>
    internal static InstallationRepairResult Apply(InstallationRepair repair, InstallationLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return repair switch
        {
            InstallationRepair.CreateAgentDirectory => CreateAgentDirectory(layout.Paths.AgentDirectory),
            _ => new InstallationRepairResult(false, Ui.Updates.InstallationNothingToRepair),
        };
    }

    private static InstallationRepairResult CreateAgentDirectory(string directory)
    {
        try
        {
            _ = Directory.CreateDirectory(directory);
            return new InstallationRepairResult(true, Ui.Format(Ui.Updates.InstallationDirectoryCreatedFormat, directory));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            return new InstallationRepairResult(
                false, Ui.Format(Ui.Updates.InstallationDirectoryNotCreatedFormat, directory, error.Message));
        }
    }
}

/// <summary>
/// Answers <see cref="IInstallationProbe"/> from the machine itself.
/// </summary>
/// <remarks>
/// Every method swallows the failure it can provoke and answers "no" rather than throwing: a check
/// that cannot inspect one thing should still report the others. Somebody whose agent will not
/// start is already having a bad time without the diagnostic also failing.
/// </remarks>
internal sealed class MachineInstallationProbe : IInstallationProbe
{
    /// <summary>
    /// How long the agent gets to answer. The connect has its own second; this bounds an agent
    /// that accepts the connection and then never replies, which would otherwise hold the window.
    /// </summary>
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(5);

    public PathVisibility InspectDirectory(string path) =>
        Inspect(path, Directory.Exists, probe => new DirectoryInfo(probe).EnumerateFileSystemInfos().Any());

    public PathVisibility InspectFile(string path) =>
        Inspect(path, File.Exists, probe => new FileInfo(probe).Length >= 0);

    public long? FileLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Asked the way the status bar asks, rather than by looking for the pipe or the socket: a file
    /// that is there proves nothing about whether anything is answering on it.
    /// </summary>
    /// <remarks>
    /// The state is taken from this poll's own report. ProbeAsync's answer reads the desktop-wide
    /// availability, which folds a starting agent in with a missing one and which the shell's own
    /// monitor writes at the same time.
    /// </remarks>
    public async Task<AgentConnectionState> AgentStateAsync(CancellationToken cancellationToken)
    {
        var observed = AgentConnectionState.Disconnected;
        await using var monitor = new AgentStatusMonitor();
        monitor.StatusChanged += (_, e) => observed = e.Status.State;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(AnswerTimeout);
        try
        {
            _ = await monitor.ProbeAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AgentConnectionState.Disconnected;
        }

        return observed;
    }

    /// <summary>
    /// Tells "not there" from "not allowed to look". Exists answers false for both, so a false is
    /// followed by an access attempt, which throws differently for each.
    /// </summary>
    private static PathVisibility Inspect(string path, Func<string, bool> exists, Func<string, bool> touch)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return PathVisibility.Missing;
        }

        if (exists(path))
        {
            return PathVisibility.Present;
        }

        try
        {
            _ = touch(path);
            return PathVisibility.Present;
        }
        catch (UnauthorizedAccessException)
        {
            return PathVisibility.Denied;
        }
        catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException)
        {
            return PathVisibility.Missing;
        }
    }
}
