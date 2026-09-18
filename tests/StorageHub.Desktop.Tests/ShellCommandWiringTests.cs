using static StorageHub.Desktop.Tests.TempDirectoryCleanup;
namespace StorageHub.Desktop.Tests;

/// <summary>
/// The menus are built from the command catalog, so these cover the seam between a declared
/// command and what a user actually sees and presses.
/// </summary>
public sealed class ShellCommandWiringTests
{
    [Fact]
    public void EveryMenuItemCarriesTheCommandIdThatDispatchesIt() =>
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var main = new MainForm();

            var tagged = MenuItems(main)
                .Where(item => item.Tag is string)
                .ToArray();

            Assert.NotEmpty(tagged);
            Assert.All(tagged, item => Assert.True(
                MainForm.IsAvailableCommand((string)item.Tag!),
                $"Menu item '{item.Text}' is tagged with an id no command dispatches."));
        });

    [Fact]
    public void MenuTitlesComeFromTheShellStrings() =>
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var main = new MainForm();

            var titles = main.MainMenuStrip!.Items
                .OfType<ToolStripMenuItem>()
                .Select(item => item.Text ?? string.Empty)
                .ToArray();

            // Transfer is absent because none of its commands are wired up yet, and a root whose
            // every command is unavailable is dropped rather than shown empty.
            Assert.Equal(
                ["Workspace", "Edit", "View", "Go", "Connections", "Sync", "Tools", "Help"],
                titles);
        });

    /// <summary>
    /// The end of the chain the id re-key had to preserve: a shortcut rebound by an older build is
    /// stored under an id that used to be derived from the English label, and it must still reach
    /// the menu the user opens.
    /// </summary>
    [Fact]
    public void AShortcutReboundByAnOlderBuildStillShowsInTheMenu() =>
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"storagehub-wiring-{Guid.NewGuid():N}");
            try
            {
                // Written through the store rather than by hand, so this is a settings file the
                // shipping loader genuinely accepts.
                var store = new DesktopConfigStore(path);
                store.Save(DesktopUpdatePreferences.Defaults with
                {
                    Shortcuts = new Dictionary<string, Keys>(StringComparer.Ordinal)
                    {
                        // The id a build that derived ids from "Edit" + "Copy" would have written.
                        ["edit.copy"] = Keys.Control | Keys.Shift | Keys.C
                    }
                });

                using var main = new MainForm(store);

                var copy = MenuItems(main).Single(item =>
                    item.Tag is string id && id == UiCommandIds.EditCopy);
                Assert.Equal(
                    ShortcutSettings.Format(Keys.Control | Keys.Shift | Keys.C),
                    copy.ShortcutKeyDisplayString);
            }
            finally
            {
                DeleteDirectory(path);
            }
        });

    private static IEnumerable<ToolStripMenuItem> MenuItems(MainForm main)
    {
        foreach (var root in main.MainMenuStrip!.Items.OfType<ToolStripMenuItem>())
        {
            foreach (var item in root.DropDownItems.OfType<ToolStripMenuItem>())
            {
                yield return item;
            }
        }
    }
}
