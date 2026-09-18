using System.Text.Json;
using StorageHub.Desktop.Configuration;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The guarantees the settings files keep now that CodeLogic owns their shape but not their bytes.
/// </summary>
/// <remarks>
/// These are the behaviours the old single-file store had and the framework's own configuration
/// writer does not: refuse an oversize file, refuse to write through a reparse point, replace
/// atomically, and -- above all -- never let a damaged settings file stop StorageHub starting.
/// </remarks>
public sealed class DesktopConfigStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"storagehub-config-store-{Guid.NewGuid():N}");

    [Fact]
    public void PreflightOnAnEmptyDirectoryWritesAllThreeFiles()
    {
        var store = new DesktopConfigStore(_directory);

        var report = store.Preflight();

        Assert.False(report.HasFindings);
        Assert.True(File.Exists(store.GeneralPath));
        Assert.True(File.Exists(store.ShortcutsPath));
        Assert.True(File.Exists(store.WorkspacesPath));
        Assert.Equal(DesktopUpdatePreferences.Defaults, store.Load());
    }

    [Fact]
    public void SettingsRoundTripAcrossStoreInstances()
    {
        var saved = DesktopUpdatePreferences.Defaults with
        {
            Appearance = DesktopAppearance.Dark,
            Language = "da-DK",
            MaximumTransferConcurrency = 9,
            ConnectionsPanelWidth = 420,
            DefaultWorkspacePaneCount = 3
        };
        new DesktopConfigStore(_directory).Save(saved);

        Assert.Equal(saved, new DesktopConfigStore(_directory).Load());
    }

    [Fact]
    public void ACorruptFileIsSetAsideAndStorageHubStillStarts()
    {
        var store = new DesktopConfigStore(_directory);
        store.Save(DesktopUpdatePreferences.Defaults with { Appearance = DesktopAppearance.Dark });
        File.WriteAllText(store.GeneralPath, "{ this is not json");

        var report = new DesktopConfigStore(_directory).Preflight();

        var rejected = Assert.Single(report.Quarantined);
        Assert.StartsWith("config.json.rejected-", rejected, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_directory, rejected)));
        Assert.Equal(DesktopAppearance.System, new DesktopConfigStore(_directory).Load().Appearance);
    }

    [Fact]
    public void AnOversizeFileIsSetAsideRatherThanRead()
    {
        Directory.CreateDirectory(_directory);
        var store = new DesktopConfigStore(_directory);
        File.WriteAllText(store.GeneralPath, new string('x', DesktopConfigStore.MaximumFileBytes + 1));

        var report = store.Preflight();

        Assert.Single(report.Quarantined);
        Assert.InRange(new FileInfo(store.GeneralPath).Length, 1, DesktopConfigStore.MaximumFileBytes);
    }

    [Fact]
    public void AnEmptyFileIsSetAsideRatherThanRead()
    {
        Directory.CreateDirectory(_directory);
        var store = new DesktopConfigStore(_directory);
        File.WriteAllBytes(store.GeneralPath, []);

        Assert.Single(store.Preflight().Quarantined);
    }

    [Fact]
    public void OnlyTheDamagedFileIsSetAsideAndTheOthersKeepTheirValues()
    {
        var store = new DesktopConfigStore(_directory);
        store.Save(DesktopUpdatePreferences.Defaults with
        {
            Appearance = DesktopAppearance.Dark,
            PinnedWorkspaces = [new WorkspaceShortcutEntry(@"C:\work\one.shw", "One", DateTimeOffset.UnixEpoch)]
        });
        File.WriteAllText(store.WorkspacesPath, "{ broken");

        var report = new DesktopConfigStore(_directory).Preflight();
        var reloaded = new DesktopConfigStore(_directory).Load();

        Assert.Single(report.Quarantined);
        Assert.Equal(DesktopAppearance.Dark, reloaded.Appearance);
        Assert.Null(reloaded.PinnedWorkspaces);
    }

    [Fact]
    public void AnOutOfRangeValueIsCorrectedAndReported()
    {
        Directory.CreateDirectory(_directory);
        var store = new DesktopConfigStore(_directory);
        File.WriteAllText(
            store.GeneralPath,
            """{"schemaVersion":1,"maximumTransferConcurrency":9000,"connectionsPanelWidth":5}""");

        var report = store.Preflight();

        Assert.True(report.Repaired);
        Assert.Empty(report.Quarantined);
        Assert.Equal(
            DesktopUpdatePreferences.Defaults.MaximumTransferConcurrency,
            store.Load().MaximumTransferConcurrency);
        Assert.Equal(DesktopUpdatePreferences.DefaultConnectionsPanelWidth, store.Load().ConnectionsPanelWidth);
    }

    [Fact]
    public void SavingRefusesValuesTheDialogWouldNotAccept()
    {
        var store = new DesktopConfigStore(_directory);

        _ = Assert.Throws<ArgumentException>(() =>
            store.Save(DesktopUpdatePreferences.Defaults with { MaximumTransferConcurrency = 9000 }));
    }

    [Fact]
    public void AFailedSaveLeavesTheCommittedFileAndNoTemporaryFilesBehind()
    {
        var store = new DesktopConfigStore(_directory);
        store.Save(DesktopUpdatePreferences.Defaults with { Appearance = DesktopAppearance.Dark });
        var committed = File.ReadAllBytes(store.GeneralPath);

        _ = Assert.Throws<ArgumentException>(() =>
            store.Save(DesktopUpdatePreferences.Defaults with { ConnectionsPanelWidth = 9000 }));

        Assert.Equal(committed, File.ReadAllBytes(store.GeneralPath));
        Assert.Empty(Directory.GetFiles(_directory, ".*.tmp"));
    }

    [Fact]
    public void TheStoreRequiresAnAbsoluteDirectory()
    {
        _ = Assert.Throws<ArgumentException>(() => new DesktopConfigStore("Desktop"));
    }

    [Fact]
    public void PreflightIsIdempotent()
    {
        var store = new DesktopConfigStore(_directory);
        store.Preflight();
        var bytes = File.ReadAllBytes(store.GeneralPath);

        var second = new DesktopConfigStore(_directory).Preflight();

        Assert.False(second.HasFindings);
        Assert.Equal(bytes, File.ReadAllBytes(store.GeneralPath));
    }

    /// <summary>
    /// The shortcut file is the one a user is most likely to edit by hand, so a binding it no
    /// longer understands must cost that binding rather than the file.
    /// </summary>
    [Fact]
    public void AnUnreadableShortcutIsDroppedAndTheRestAreKept()
    {
        Directory.CreateDirectory(_directory);
        var store = new DesktopConfigStore(_directory);
        File.WriteAllText(store.ShortcutsPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            shortcuts = new Dictionary<string, string>
            {
                [UiCommandIds.EditCopy] = "Ctrl+Shift+C",
                [UiCommandIds.EditPaste] = "Hyper+Q"
            }
        }));

        store.Preflight();
        var resolved = ShortcutSettings.Resolve(store.Load().Shortcuts);

        Assert.Equal(Keys.Control | Keys.Shift | Keys.C, resolved[UiCommandIds.EditCopy]);
        Assert.Equal(
            UiCommandCatalog.Definitions.Single(command => command.Id == UiCommandIds.EditPaste).Shortcut,
            resolved[UiCommandIds.EditPaste]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
