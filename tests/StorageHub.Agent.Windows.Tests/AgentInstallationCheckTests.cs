using StorageHub.Agent;
using StorageHub.Testing;
using StorageHub.Agent.Host;

namespace StorageHub.Agent.Windows.Tests;

/// <summary>
/// The installation check exists for a failure nobody can see: the service dies, nothing restarts
/// it, and the only symptom is the desktop saying the agent did not become ready. These pin the
/// shape of what it reports, including the exact combination that produced that message.
/// </summary>
public sealed class AgentInstallationCheckTests
{
    private const string AgentExecutable = "StorageHub.Agent.Host.exe";

    [WindowsOnlyFact]
    public void AHealthyServiceInstallationReportsNothingToDo()
    {
        var probe = FakeProbe.HealthyService();

        var report = AgentInstallationCheck.Inspect(
            AgentHostMode.WindowsService,
            "1.4.4+abc",
            AgentExecutable,
            probe);

        Assert.Equal(InstallationCheckStatus.Ok, report.Worst);
        Assert.Empty(report.Repairable);
    }

    [WindowsOnlyFact]
    public void AStoppedServiceIsAProblemThatStartingItRepairs()
    {
        var probe = FakeProbe.HealthyService();
        probe.Service = new AgentServiceState(Installed: true, Running: false);
        probe.ListeningPipes.Clear();

        var report = AgentInstallationCheck.Inspect(
            AgentHostMode.WindowsService,
            "1.4.4",
            AgentExecutable,
            probe);

        var running = Single(report, "Service running");
        Assert.Equal(InstallationCheckStatus.Problem, running.Status);
        Assert.Equal(InstallationRepair.StartService, running.Repair);
    }

    /// <summary>
    /// The gap that turned one crash into a week without an agent: Windows does nothing for a
    /// service with no failure actions, so it stays stopped until somebody notices.
    /// </summary>
    [WindowsOnlyFact]
    public void AServiceWindowsWillNotRestartIsReportedAsFragileRatherThanBroken()
    {
        var probe = FakeProbe.HealthyService();
        probe.RestartsOnFailure = false;

        var report = AgentInstallationCheck.Inspect(
            AgentHostMode.WindowsService,
            "1.4.4",
            AgentExecutable,
            probe);

        var recovery = Single(report, "Restart after a crash");
        Assert.Equal(InstallationCheckStatus.Warning, recovery.Status);
        Assert.Equal(InstallationRepair.ConfigureServiceRecovery, recovery.Repair);
    }

    [WindowsOnlyFact]
    public void AStagedAgentOlderThanTheApplicationIsReported()
    {
        var probe = FakeProbe.HealthyService();
        probe.StagedVersion = "1.4.3";

        var report = AgentInstallationCheck.Inspect(
            AgentHostMode.WindowsService,
            "1.4.4",
            AgentExecutable,
            probe);

        var staged = Single(report, "Staged agent");
        Assert.Equal(InstallationCheckStatus.Warning, staged.Status);
        Assert.Equal(InstallationRepair.RestageAgent, staged.Repair);
        Assert.Contains("1.4.3", staged.Detail, StringComparison.Ordinal);
        Assert.Contains("1.4.4", staged.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// Both versions carry a build suffix that differs between builds of the same release.
    /// Reporting that would send the operator through an elevation prompt that changes nothing.
    /// </summary>
    [WindowsOnlyFact]
    public void ABuildSuffixAloneIsNotAVersionMismatch()
    {
        var probe = FakeProbe.HealthyService();
        probe.StagedVersion = "1.4.4+1cc874f";

        var report = AgentInstallationCheck.Inspect(
            AgentHostMode.WindowsService,
            "1.4.4+08b90b7",
            AgentExecutable,
            probe);

        Assert.Equal(InstallationCheckStatus.Ok, Single(report, "Staged agent").Status);
    }

    [WindowsOnlyFact]
    public void AMissingAgentDirectoryIsAProblemThatCreatingItRepairs()
    {
        var probe = FakeProbe.HealthySession();
        probe.Directories.Clear();

        var report = AgentInstallationCheck.Inspect(
            AgentHostMode.UserSession,
            "1.4.4",
            AgentExecutable,
            probe);

        var directory = Single(report, "Data directory");
        Assert.Equal(InstallationCheckStatus.Problem, directory.Status);
        Assert.Equal(InstallationRepair.CreateAgentDirectory, directory.Repair);
        Assert.False(directory.RequiresElevation);
    }

    [WindowsOnlyFact]
    public void AnEmptyDatabaseIsAProblemBecauseSavedStateIsNotReadable()
    {
        var probe = FakeProbe.HealthySession();
        probe.Files[AgentHostLayout.ResolveDatabasePath(AgentHostMode.UserSession)] = 0;

        var report = AgentInstallationCheck.Inspect(
            AgentHostMode.UserSession,
            "1.4.4",
            AgentExecutable,
            probe);

        Assert.Equal(InstallationCheckStatus.Problem, Single(report, "Database").Status);
    }

    [WindowsOnlyFact]
    public void AMissingDatabaseIsOnlyNotedBecauseAFreshInstallHasNone()
    {
        var probe = FakeProbe.HealthySession();
        probe.Files.Remove(AgentHostLayout.ResolveDatabasePath(AgentHostMode.UserSession));

        var report = AgentInstallationCheck.Inspect(
            AgentHostMode.UserSession,
            "1.4.4",
            AgentExecutable,
            probe);

        Assert.Equal(InstallationCheckStatus.Warning, Single(report, "Database").Status);
    }

    /// <summary>
    /// Switching mode moves the data root, so the connections are not gone -- they are in the
    /// other root. Saying so is the difference between a scare and a setting.
    /// </summary>
    [WindowsOnlyFact]
    public void APopulatedDatabaseLeftInTheOtherModesRootIsPointedAt()
    {
        var probe = FakeProbe.HealthyService();
        var sessionDatabase = AgentHostLayout.ResolveDatabasePath(AgentHostMode.UserSession);
        probe.Files[sessionDatabase] = 512 * 1024;

        var report = AgentInstallationCheck.Inspect(
            AgentHostMode.WindowsService,
            "1.4.4",
            AgentExecutable,
            probe);

        var stray = Single(report, "Database from the other mode");
        Assert.Equal(InstallationCheckStatus.Warning, stray.Status);
        Assert.Equal(sessionDatabase, stray.Location);
    }

    [WindowsOnlyFact]
    public void ASessionInstallationIsNotAskedAboutAService()
    {
        var report = AgentInstallationCheck.Inspect(
            AgentHostMode.UserSession,
            "1.4.4",
            AgentExecutable,
            FakeProbe.HealthySession());

        Assert.DoesNotContain(report.Findings, finding => finding.Title.StartsWith("Service", StringComparison.Ordinal));
    }

    /// <summary>The state this machine was actually in when the desktop reported a timeout.</summary>
    [WindowsOnlyFact]
    public void TheServiceStoppedAndUnrecoverableCaseReportsEveryPartOfIt()
    {
        var probe = FakeProbe.HealthyService();
        probe.Service = new AgentServiceState(Installed: true, Running: false);
        probe.RestartsOnFailure = false;
        probe.ListeningPipes.Clear();
        probe.Elevated = false;

        var report = AgentInstallationCheck.Inspect(
            AgentHostMode.WindowsService,
            "1.4.4",
            AgentExecutable,
            probe);

        Assert.Equal(InstallationCheckStatus.Problem, report.Worst);
        Assert.Equal(InstallationCheckStatus.Problem, Single(report, "Agent reachable").Status);
        Assert.Equal(
            [InstallationRepair.StartService, InstallationRepair.ConfigureServiceRecovery],
            report.Repairable.Select(finding => finding.Repair));
        // Nothing here can be applied from the desktop's own token.
        Assert.All(report.Repairable, finding => Assert.True(finding.RequiresElevation));
    }

    [WindowsOnlyFact]
    public void AnElevatedCallerIsNotToldItNeedsToElevate()
    {
        var probe = FakeProbe.HealthyService();
        probe.Service = new AgentServiceState(Installed: true, Running: false);
        probe.Elevated = true;

        var report = AgentInstallationCheck.Inspect(
            AgentHostMode.WindowsService,
            "1.4.4",
            AgentExecutable,
            probe);

        Assert.False(Single(report, "Service running").RequiresElevation);
    }

    [WindowsOnlyTheory]
    [InlineData(InstallationRepair.StartService, true)]
    [InlineData(InstallationRepair.ConfigureServiceRecovery, true)]
    [InlineData(InstallationRepair.RestageAgent, true)]
    [InlineData(InstallationRepair.CreateAgentDirectory, false)]
    public void OnlyTheServiceRepairsNeedAnAdministrator(InstallationRepair repair, bool expected) =>
        Assert.Equal(expected, AgentInstallationRepair.RequiresElevation(repair));

    /// <summary>
    /// The machine-owned service root denies the signed-in user any read, which is the point of
    /// it. Reporting that as missing told a machine with a healthy, running service that its
    /// database had vanished, and offered to create a directory that was already there.
    /// </summary>
    [WindowsOnlyFact]
    public void ADirectoryThisAccountMayNotReadIsNotReportedAsMissing()
    {
        var probe = FakeProbe.HealthyService();
        var directory = AgentHostLayout.ResolveAgentDirectory(AgentHostMode.WindowsService);
        probe.Directories.Clear();
        probe.Denied.Add(directory);

        var report = AgentInstallationCheck.Inspect(
            AgentHostMode.WindowsService,
            "1.4.4",
            AgentExecutable,
            probe);

        var finding = Single(report, "Data directory");
        Assert.Equal(InstallationCheckStatus.Ok, finding.Status);
        Assert.Equal(InstallationRepair.None, finding.Repair);
    }

    [WindowsOnlyFact]
    public void ADatabaseThisAccountMayNotReadIsNotReportedAsLost()
    {
        var probe = FakeProbe.HealthyService();
        var path = AgentHostLayout.ResolveDatabasePath(AgentHostMode.WindowsService);
        probe.Files.Remove(path);
        probe.Denied.Add(path);

        var report = AgentInstallationCheck.Inspect(
            AgentHostMode.WindowsService,
            "1.4.4",
            AgentExecutable,
            probe);

        Assert.Equal(InstallationCheckStatus.Ok, Single(report, "Database").Status);
    }

    /// <summary>A database that cannot be read cannot be said to hold anything.</summary>
    [WindowsOnlyFact]
    public void AnUnreadableDatabaseInTheOtherModeIsNotPointedAt()
    {
        var probe = FakeProbe.HealthyService();
        probe.Denied.Add(AgentHostLayout.ResolveDatabasePath(AgentHostMode.UserSession));

        var report = AgentInstallationCheck.Inspect(
            AgentHostMode.WindowsService,
            "1.4.4",
            AgentExecutable,
            probe);

        Assert.DoesNotContain(report.Findings, f => f.Title == "Database from the other mode");
    }

    private static InstallationFinding Single(InstallationReport report, string title) =>
        Assert.Single(report.Findings, finding => finding.Title == title);

    private sealed class FakeProbe : IInstallationProbe
    {
        public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, long> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> ListeningPipes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public AgentServiceState Service { get; set; } = new(false, false);

        public bool RestartsOnFailure { get; set; }

        public string? StagedVersion { get; set; }

        public bool Elevated { get; set; }

        public string StagedAgentDirectory => @"C:\ProgramData\StorageHubAgent\bin";

        /// <summary>Paths this account is not allowed to look at, whatever is really there.</summary>
        public HashSet<string> Denied { get; } = new(StringComparer.OrdinalIgnoreCase);

        public PathVisibility InspectDirectory(string path) =>
            Denied.Contains(path) ? PathVisibility.Denied
            : Directories.Contains(path) ? PathVisibility.Present
            : PathVisibility.Missing;

        public PathVisibility InspectFile(string path) =>
            Denied.Contains(path) ? PathVisibility.Denied
            : Files.ContainsKey(path) ? PathVisibility.Present
            : PathVisibility.Missing;

        public long? FileLength(string path) => Files.TryGetValue(path, out var length) ? length : null;

        public bool PipeExists(string pipeName) => ListeningPipes.Contains(pipeName);

        public AgentServiceState DescribeService() => Service;

        public bool ServiceRestartsOnFailure() => RestartsOnFailure;

        public string? ReadStagedVersion(string executableName) => StagedVersion;

        public bool IsElevated() => Elevated;

        public static FakeProbe HealthyService()
        {
            var probe = new FakeProbe
            {
                Service = new AgentServiceState(true, true),
                RestartsOnFailure = true,
                StagedVersion = "1.4.4",
                Elevated = false,
            };
            probe.Directories.Add(AgentHostLayout.ResolveAgentDirectory(AgentHostMode.WindowsService));
            probe.Files[AgentHostLayout.ResolveDatabasePath(AgentHostMode.WindowsService)] = 434_176;
            probe.ListeningPipes.Add(AgentHostLayout.ResolvePipeNames(AgentHostMode.WindowsService).Normal);
            return probe;
        }

        public static FakeProbe HealthySession()
        {
            var probe = new FakeProbe();
            probe.Directories.Add(AgentHostLayout.ResolveAgentDirectory(AgentHostMode.UserSession));
            probe.Files[AgentHostLayout.ResolveDatabasePath(AgentHostMode.UserSession)] = 434_176;
            probe.ListeningPipes.Add(AgentHostLayout.ResolvePipeNames(AgentHostMode.UserSession).Normal);
            return probe;
        }
    }
}
