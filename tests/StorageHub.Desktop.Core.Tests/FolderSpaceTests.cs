using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Tests;

public sealed class FolderSpaceTests
{
    /// <summary>
    /// The status line says the free space, with the total beside it when the server gave one,
    /// and nothing at all when the free space is not known.
    /// </summary>
    [Fact]
    public void TheLineNamesTheFreeSpaceAndOnlyThat()
    {
        Assert.Equal(
            Ui.Format(Ui.Pane.FreeSpaceOfTotalFormat, UiFormatting.FormatBytes(1_536), UiFormatting.FormatBytes(4_096)),
            FolderSpace.Describe(new FolderSpace(4_096, 1_536)));
        Assert.Equal(
            Ui.Format(Ui.Pane.FreeSpaceFormat, UiFormatting.FormatBytes(2_048)),
            FolderSpace.Describe(new FolderSpace(null, 2_048)));
        Assert.Null(FolderSpace.Describe(new FolderSpace(4_096, null)));
        Assert.Null(FolderSpace.Describe(null));

        // What the agent reported, kept only where it has a free figure.
        Assert.Equal(new FolderSpace(10, 4), FolderSpace.From(new StorageSpaceDocument(10, 4, 6)));
        Assert.Null(FolderSpace.From(new StorageSpaceDocument(10, null, 6)));
    }

    /// <summary>
    /// A folder on this computer reports the volume it is on; This PC, whose drive rows say it
    /// already, and a data source that cannot tell report nothing.
    /// </summary>
    [Fact]
    public async Task ALocalFolderReportsItsVolumeAndThisPcDoesNot()
    {
        var folder = Directory.CreateTempSubdirectory("storagehub-space-");
        try
        {
            await using var controller = new LocalBrowserController(new LocalFileBrowserDataSource());
            Assert.Null(await controller.GetSpaceAsync(CancellationToken.None));

            var moved = await controller.NavigateAsync(
                LocalBrowserNavigationKind.Navigate,
                LocalBrowserLocation.FromDirectory(folder.FullName),
                CancellationToken.None);
            Assert.Equal(LocalBrowserNavigationStatus.Succeeded, moved.Status);

            var space = await controller.GetSpaceAsync(CancellationToken.None);
            Assert.NotNull(space);
            Assert.True(space.TotalBytes > 0);
            Assert.InRange(space.AvailableBytes!.Value, 0, space.TotalBytes!.Value);

            await using var silent = new LocalBrowserController(new MemoryOnly());
            await silent.NavigateAsync(
                LocalBrowserNavigationKind.Navigate,
                LocalBrowserLocation.FromDirectory(folder.FullName),
                CancellationToken.None);
            Assert.Null(await silent.GetSpaceAsync(CancellationToken.None));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private sealed class MemoryOnly : ILocalFileBrowserDataSource
    {
        public Task<LocalBrowserSnapshot> BrowseAsync(LocalBrowserLocation location, CancellationToken cancellationToken) =>
            Task.FromResult(new LocalBrowserSnapshot(location, []));
    }
}
