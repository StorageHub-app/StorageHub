using System.Text.Json;
using StorageHub.Desktop.Configuration;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// That an installation which already had settings keeps them.
/// </summary>
/// <remarks>
/// The migration is one-way and runs once, on a machine whose settings nobody has a copy of, so
/// these are the tests that make "no settings loss" a fact rather than a hope. The acid test is
/// <see cref="CustomKeyboardShortcutsSurviveTheMigration"/>: shortcut overrides are keyed by
/// command id, and the command ids were re-derived in the same body of work.
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
            externalEditorPath = @"C:\Tools\editor.exe",
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
        Assert.Equal(@"C:\Tools\editor.exe", migrated.ExternalEditorPath);
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
    /// The command ids these are keyed by were re-derived from the English menu labels during this
    /// work. If a single one of them moved, a user's rebindings would come back as defaults and
    /// nothing would say so.
    /// </summary>
    [Fact]
    public void CustomKeyboardShortcutsSurviveTheMigration()
    {
        WriteLegacy(new
        {
            schemaVersion = 15,
            sshHostKeyDiscovery = 2,
            shortcuts = new Dictionary<string, int>
            {
                [UiCommandIds.EditCopy] = (int)(Keys.Control | Keys.Shift | Keys.C),
                [UiCommandIds.EditPaste] = (int)Keys.None,
                [UiCommandIds.ViewRefresh] = (int)Keys.F9
            }
        });

        var store = new DesktopConfigStore(_directory);
        store.Preflight();
        var bindings = ShortcutSettings.Resolve(ShortcutKeys.ToKeys(store.Load().Shortcuts));

        Assert.Equal(Keys.Control | Keys.Shift | Keys.C, bindings[UiCommandIds.EditCopy]);
        Assert.Equal(Keys.None, bindings[UiCommandIds.EditPaste]);
        Assert.Equal(Keys.F9, bindings[UiCommandIds.ViewRefresh]);
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
                new { path = @"C:\work\pinned.shw", name = "Pinned", lastOpenedUtc = "2025-01-02T03:04:05+00:00" }
            },
            recentWorkspaces = new[]
            {
                new { path = @"C:\work\recent.shw", name = "Recent", lastOpenedUtc = "2025-02-03T04:05:06+00:00" }
            }
        });

        var store = new DesktopConfigStore(_directory);
        store.Preflight();
        var migrated = store.Load();

        Assert.Equal(@"C:\work\pinned.shw", Assert.Single(migrated.PinnedWorkspaces!).Path);
        Assert.Equal(@"C:\work\recent.shw", Assert.Single(migrated.RecentWorkspaces!).Path);
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
