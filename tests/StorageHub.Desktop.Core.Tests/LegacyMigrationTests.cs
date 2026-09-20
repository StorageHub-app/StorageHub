using static StorageHub.Desktop.Tests.TestPaths;

using System.Text.Json;
using StorageHub.Desktop.Configuration;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// That an installation which already had settings keeps them.
/// </summary>
/// <remarks>
/// The migration is one-way and runs once, on a machine whose settings nobody has a copy of, so
/// these are the tests that make "no settings loss" a fact rather than a hope - with one deliberate
/// exception recorded in <see cref="LegacyShortcutsAreDroppedButTheRestOfTheFileMigrates"/>.
/// </remarks>
public sealed class LegacyMigrationTests : IDisposable
{
    private static readonly JsonSerializerOptions LegacyOptions = new(JsonSerializerDefaults.Web);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"storagehub-migration-{Guid.NewGuid():N}");

    [Fact]
    public void AVersionFifteenFileIsCarriedOverCompletely()
    {
        WriteLegacy(new
        {
            schemaVersion = 15,
            checkAutomatically = false,
            downloadAutomatically = false,
            restartAutomatically = true,
            includePrereleases = false,
            sshHostKeyDiscovery = 3,
            externalEditorPath = Rooted(@"Tools\editor.exe"),
            adaptiveConcurrency = false,
            minimumConcurrency = 2,
            maximumTransferConcurrency = 12,
            perConnectionConcurrency = 3,
            maximumSyncConcurrency = 5,
            appearance = 2,
            warnBeforeUnsafeExternalEdit = false,
            defaultWorkspaceLayout = 2,
            reconnectRemotePanesAutomatically = false,
            confirmBeforeClearingTransferHistory = false,
            confirmBeforeDeletingItems = false,
            defaultWorkspacePaneCount = 3,
            connectionsPanelWidth = 420,
            connectionsPanelVisible = false,
            connectionsPanelSide = 2
        });

        var store = new DesktopConfigStore(_directory);
        var report = store.Preflight();
        var migrated = store.Load();

        Assert.Equal("settings.json.migrated", report.MigratedFrom);
        Assert.False(migrated.CheckAutomatically);
        Assert.False(migrated.DownloadAutomatically);
        Assert.True(migrated.RestartAutomatically);
        Assert.False(migrated.IncludePrereleases);
        Assert.Equal(SshHostKeyDiscoveryMode.Automatic, migrated.SshHostKeyDiscovery);
        Assert.Equal(Rooted(@"Tools\editor.exe"), migrated.ExternalEditorPath);
        Assert.False(migrated.AdaptiveConcurrency);
        Assert.Equal(2, migrated.MinimumConcurrency);
        Assert.Equal(12, migrated.MaximumTransferConcurrency);
        Assert.Equal(3, migrated.PerConnectionConcurrency);
        Assert.Equal(5, migrated.MaximumSyncConcurrency);
        Assert.Equal(DesktopAppearance.Dark, migrated.Appearance);
        Assert.False(migrated.WarnBeforeUnsafeExternalEdit);
        Assert.Equal(WorkspaceLayout.TopAndBottom, migrated.DefaultWorkspaceLayout);
        Assert.False(migrated.ReconnectRemotePanesAutomatically);
        Assert.False(migrated.ConfirmBeforeClearingTransferHistory);
        Assert.False(migrated.ConfirmBeforeDeletingItems);
        Assert.Equal(3, migrated.DefaultWorkspacePaneCount);
        Assert.Equal(420, migrated.ConnectionsPanelWidth);
        Assert.False(migrated.ConnectionsPanelVisible);
        Assert.Equal(ConnectionsPanelSide.Right, migrated.ConnectionsPanelSide);
    }

    /// <summary>
    /// A 1.x file's shortcut overrides are not carried across, and everything else still is.
    /// </summary>
    /// <remarks>
    /// This used to assert the opposite, and the change is deliberate. A 1.x file stored each chord
    /// as the numeric WinForms key enum; the shell now uses a different enum, numbered differently,
    /// so those numbers cannot be reinterpreted - they would come back as other keys entirely.
    /// StorageHub 2.0 drops them and falls back to the defaults, which a user can see and correct,
    /// rather than silently rebinding their commands to whatever the numbers happen to mean now.
    ///
    /// What matters is that dropping them costs only them: the rest of the file still migrates.
    /// </remarks>
    [Fact]
    public void LegacyShortcutsAreDroppedButTheRestOfTheFileMigrates()
    {
        WriteLegacy(new
        {
            schemaVersion = 15,
            sshHostKeyDiscovery = 2,
            confirmBeforeDeletingItems = false,
            // The numbers a 1.x build wrote: System.Windows.Forms.Keys cast to int, where Ctrl
            // is 0x20000 and Shift 0x10000. So this is Ctrl+Shift+C and F9. Spelled out rather
            // than computed, because the enum that produced them is not on every platform this
            // now runs on - and the file holds numbers, not names.
            shortcuts = new Dictionary<string, int>
            {
                [UiCommandIds.EditCopy] = 0x30043,
                [UiCommandIds.ViewRefresh] = 0x78
            }
        });

        var store = new DesktopConfigStore(_directory);
        store.Preflight();
        var loaded = store.Load();
        var bindings = ShortcutSettings.Resolve(loaded.Shortcuts);

        // The setting beside them came across.
        Assert.False(loaded.ConfirmBeforeDeletingItems);

        // The rebindings did not, so these are the catalog's defaults.
        Assert.Equal(
            UiCommandCatalog.GetDefinition(UiCommandIds.EditCopy).Shortcut,
            bindings[UiCommandIds.EditCopy]);
        Assert.Equal(
            UiCommandCatalog.GetDefinition(UiCommandIds.ViewRefresh).Shortcut,
            bindings[UiCommandIds.ViewRefresh]);
    }

    [Fact]
    public void PinnedAndRecentWorkspacesAreCarriedOver()
    {
        WriteLegacy(new
        {
            schemaVersion = 15,
            sshHostKeyDiscovery = 2,
            pinnedWorkspaces = new[]
            {
                new { path = Rooted(@"work\pinned.shw"), name = "Pinned", lastOpenedUtc = "2025-01-02T03:04:05+00:00" }
            },
            recentWorkspaces = new[]
            {
                new { path = Rooted(@"work\recent.shw"), name = "Recent", lastOpenedUtc = "2025-02-03T04:05:06+00:00" }
            }
        });

        var store = new DesktopConfigStore(_directory);
        store.Preflight();
        var migrated = store.Load();

        Assert.Equal(Rooted(@"work\pinned.shw"), Assert.Single(migrated.PinnedWorkspaces!).Path);
        Assert.Equal(Rooted(@"work\recent.shw"), Assert.Single(migrated.RecentWorkspaces!).Path);
    }

    /// <summary>
    /// The one field the legacy file never held. An upgraded installation follows Windows until
    /// its owner chooses otherwise, rather than being pinned to whatever happened to be first.
    /// </summary>
    [Fact]
    public void AMigratedInstallationStartsOnTheAutomaticLanguage()
    {
        WriteLegacy(new { schemaVersion = 15, sshHostKeyDiscovery = 2, appearance = 2 });

        var store = new DesktopConfigStore(_directory);
        store.Preflight();

        Assert.Equal(DesktopCulture.AutomaticLanguage, store.Load().Language);
    }

    [Fact]
    public void TheLegacyFileIsKeptRatherThanDeleted()
    {
        WriteLegacy(new { schemaVersion = 15, sshHostKeyDiscovery = 2, appearance = 2 });

        new DesktopConfigStore(_directory).Preflight();

        Assert.False(File.Exists(Path.Combine(_directory, "settings.json")));
        Assert.True(File.Exists(Path.Combine(_directory, "settings.json.migrated")));
    }

    /// <summary>
    /// Guarded on the new file being absent, so a second start cannot undo whatever the user has
    /// changed since.
    /// </summary>
    [Fact]
    public void TheMigrationRunsOnceAndNotAgain()
    {
        WriteLegacy(new { schemaVersion = 15, sshHostKeyDiscovery = 2, appearance = 2 });
        var store = new DesktopConfigStore(_directory);
        store.Preflight();
        store.Save(store.Load() with { Appearance = DesktopAppearance.Light });

        var second = new DesktopConfigStore(_directory);
        var report = second.Preflight();

        Assert.Null(report.MigratedFrom);
        Assert.Equal(DesktopAppearance.Light, second.Load().Appearance);
    }

    /// <summary>
    /// Deleting config.json is how a user resets their settings. It must not resurrect the file
    /// they migrated away from years earlier.
    /// </summary>
    [Fact]
    public void DeletingTheNewFileResetsRatherThanReimporting()
    {
        WriteLegacy(new { schemaVersion = 15, sshHostKeyDiscovery = 2, appearance = 2 });
        var store = new DesktopConfigStore(_directory);
        store.Preflight();
        File.Delete(store.GeneralPath);

        var second = new DesktopConfigStore(_directory);
        var report = second.Preflight();

        Assert.Null(report.MigratedFrom);
        Assert.Equal(DesktopAppearance.System, second.Load().Appearance);
    }

    [Fact]
    public void AVersionOneFileMigratesThroughTheWholeLadder()
    {
        WriteLegacy(new
        {
            schemaVersion = 1,
            checkAutomatically = false,
            downloadAutomatically = false,
            restartAutomatically = true,
            includePrereleases = false,
            sshHostKeyDiscovery = 2
        });

        var store = new DesktopConfigStore(_directory);
        store.Preflight();
        var migrated = store.Load();

        Assert.False(migrated.CheckAutomatically);
        Assert.True(migrated.RestartAutomatically);

        // Everything a version 1 file predates comes back at its default rather than at zero.
        Assert.Equal(SshHostKeyDiscoveryMode.AskBeforeFetching, migrated.SshHostKeyDiscovery);
        Assert.Equal(DesktopAppearance.System, migrated.Appearance);
        Assert.Equal(DesktopUpdatePreferences.DefaultConnectionsPanelWidth, migrated.ConnectionsPanelWidth);
        Assert.True(migrated.ConnectionsPanelVisible);
    }

    [Fact]
    public void AnUnreadableLegacyFileLeavesDefaultsRatherThanFailing()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "settings.json"), "{ not json at all");

        var store = new DesktopConfigStore(_directory);
        store.Preflight();

        Assert.Equal(DesktopUpdatePreferences.Defaults, store.Load());
    }

    /// <summary>
    /// Writes a legacy settings file. Every fixture here names <c>sshHostKeyDiscovery</c>, which
    /// real files always carried: the loader rejects a version 15 file without it, because that
    /// enum starts at 1 and the absent value therefore fails its defined-value check.
    /// </summary>
    private void WriteLegacy(object document)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            JsonSerializer.Serialize(document, LegacyOptions));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
