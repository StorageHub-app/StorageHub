using StorageHub.Agent.Linux;
using StorageHub.Testing;

namespace StorageHub.Agent.Linux.Tests;

/// <summary>
/// The systemd user unit, and what it promises.
/// </summary>
/// <remarks>
/// Registering and enabling are exercised through systemctl, which these do not drive: a test that
/// enables a real unit changes the machine it runs on, and one that asserts on a stubbed systemctl
/// asserts on the stub. What is asserted here is the unit text and where it is written, which is
/// what would actually be wrong if it were wrong.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class SystemdUserAutostartTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"storagehub-systemd-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    [LinuxOnlyFact]
    public void TheUnitRunsTheRecordedCommandAndRestartsOnFailure()
    {
        var unit = SystemdUserAutostart.BuildUnit("/opt/storagehub/StorageHub.Agent.Host");

        Assert.Contains("ExecStart=/opt/storagehub/StorageHub.Agent.Host", unit, StringComparison.Ordinal);
        Assert.Contains("Restart=on-failure", unit, StringComparison.Ordinal);
        Assert.Contains("WantedBy=default.target", unit, StringComparison.Ordinal);
    }

    [LinuxOnlyFact]
    public void TheUnitRecordsTheExecutablePathItWasGiven()
    {
        // An AppImage invalidates this the moment it is moved or replaced, which is why the host
        // compares the recorded path against its own on every start and re-registers when they
        // differ. A unit that silently stops starting is worse than one that was never written.
        var unit = SystemdUserAutostart.BuildUnit("/home/someone/Apps/StorageHub-1.4.6.AppImage");

        Assert.Contains("/home/someone/Apps/StorageHub-1.4.6.AppImage", unit, StringComparison.Ordinal);
    }

    [LinuxOnlyFact]
    public void NothingIsRegisteredUntilAUnitIsWritten()
    {
        var autostart = new SystemdUserAutostart(_directory);

        Assert.False(autostart.IsRegistered);
    }

    [LinuxOnlyFact]
    public void AWrittenUnitIsReportedAsRegistered()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, SystemdUserAutostart.UnitName),
            SystemdUserAutostart.BuildUnit("/usr/bin/true"));

        Assert.True(new SystemdUserAutostart(_directory).IsRegistered);
    }

    [LinuxOnlyFact]
    public void LingeringIsReadRatherThanAssumed()
    {
        // A user unit stops when the last session ends unless this account lingers. Reporting a
        // registered unit as "keeps running" without checking would misstate exactly the promise a
        // scheduled sync depends on.
        var autostart = new SystemdUserAutostart(_directory);

        // Whatever this machine's answer is, it has to come from loginctl rather than from the
        // presence of a unit file.
        Assert.False(autostart.IsRegistered);
        _ = autostart.SurvivesSignOut;
    }
}
