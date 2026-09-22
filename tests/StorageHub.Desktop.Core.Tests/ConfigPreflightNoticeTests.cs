using StorageHub.Desktop.Configuration;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// What the shell says at startup about the settings files.
/// </summary>
/// <remarks>
/// 1.x said these on its splash. 2.0 has no splash to say them on, and until now said nothing: a
/// damaged settings file was set aside and its settings reset without a word.
/// </remarks>
public sealed class ConfigPreflightNoticeTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"storagehub-config-notice-{Guid.NewGuid():N}");

    [Fact]
    public void CleanSettingsSayNothing()
    {
        Assert.Null(new DesktopConfigStore(_directory).Preflight().ToNotice());
    }

    /// <summary>A file set aside is a warning, and names the file it now is.</summary>
    [Fact]
    public void ASetAsideFileIsAWarningThatNamesIt()
    {
        var store = new DesktopConfigStore(_directory);
        store.Preflight();
        File.WriteAllText(store.GeneralPath, "{ this is not json");

        var report = new DesktopConfigStore(_directory).Preflight();
        var notice = report.ToNotice();

        Assert.NotNull(notice);
        Assert.Equal(DialogSeverity.Warning, notice.Severity);
        Assert.Equal(Ui.Dialogs.StartupCheckCaption, notice.Title);
        Assert.Equal(Ui.Format(Ui.Dialogs.SettingsQuarantinedFormat, report.Quarantined[0]), notice.Message);
    }

    /// <summary>
    /// Only the first check can see the damage; the next finds the clean set it left behind.
    /// </summary>
    /// <remarks>
    /// Which is why the shell runs its check before anything else reads settings, and keeps the
    /// report: every window that opens later runs one of its own and finds nothing.
    /// </remarks>
    [Fact]
    public void OnlyTheFirstCheckSeesTheDamage()
    {
        var store = new DesktopConfigStore(_directory);
        store.Preflight();
        File.WriteAllText(store.GeneralPath, "{ this is not json");

        Assert.NotNull(new DesktopConfigStore(_directory).Preflight().ToNotice());
        Assert.Null(new DesktopConfigStore(_directory).Preflight().ToNotice());
    }

    /// <summary>Carried over, then set aside, then corrected, one paragraph each.</summary>
    [Fact]
    public void FindingsAreDescribedInOrder()
    {
        var report = new ConfigPreflightReport(["a.rejected", "b.rejected"], Repaired: true, MigratedFrom: "settings.json.migrated");

        Assert.Equal(
            [
                Ui.Format(Ui.Dialogs.SettingsMigratedFormat, "settings.json.migrated"),
                Ui.Format(Ui.Dialogs.SettingsQuarantinedFormat, "a.rejected"),
                Ui.Format(Ui.Dialogs.SettingsQuarantinedFormat, "b.rejected"),
                Ui.Dialogs.SettingsRepaired
            ],
            report.Describe());
        Assert.Equal(4, report.ToNotice()!.Message.Split(Environment.NewLine + Environment.NewLine).Length);
    }

    /// <summary>Carrying settings over is news, not a problem.</summary>
    [Fact]
    public void AMigrationAloneIsInformation()
    {
        var report = new ConfigPreflightReport([], Repaired: false, MigratedFrom: "settings.json.migrated");

        Assert.Equal(DialogSeverity.Information, report.ToNotice()!.Severity);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
