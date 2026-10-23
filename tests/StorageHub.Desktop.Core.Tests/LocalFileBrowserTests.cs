namespace StorageHub.Desktop.Tests;

public sealed class LocalFileBrowserTests
{
    [Fact]
    public void LocationParsingRecognizesThisPcQuotedPathsAndDriveParent()
    {
        Assert.True(LocalBrowserLocation.TryParseAddress("this pc", out var thisPc, out var rootError));
        Assert.True(thisPc.IsThisPc);
        Assert.Null(rootError);

        var expected = Path.GetFullPath(Path.GetTempPath());
        Assert.True(LocalBrowserLocation.TryParseAddress($"\"{expected}\"", out var folder, out var pathError));
        Assert.Equal(Path.TrimEndingDirectorySeparator(expected), folder.DirectoryPath);
        Assert.Null(pathError);

        var root = LocalBrowserLocation.FromDirectory(Path.GetPathRoot(expected)!);
        Assert.True(root.GetParent().IsThisPc);
    }

    [Fact]
    public void HistorySupportsBackForwardAndDropsForwardBranch()
    {
        var history = new LocalBrowserHistory();
        var first = LocalBrowserLocation.FromDirectory(Path.Combine(Path.GetTempPath(), "first"));
        var second = LocalBrowserLocation.FromDirectory(Path.Combine(Path.GetTempPath(), "second"));
        var branch = LocalBrowserLocation.FromDirectory(Path.Combine(Path.GetTempPath(), "branch"));

        history.Navigate(first);
        history.Navigate(second);
        Assert.True(history.MoveBack());
        Assert.True(history.Current.IsSameLocation(first));
        Assert.True(history.CanGoForward);

        history.Navigate(branch);

        Assert.True(history.Current.IsSameLocation(branch));
        Assert.True(history.CanGoBack);
        Assert.False(history.CanGoForward);
    }

    [Fact]
    public void PresentationFiltersBySubstringAndWildcardsWithoutChangingOrder()
    {
        LocalBrowserEntry[] entries =
        [
            Entry("Alpha.txt"),
            Entry("archive.zip"),
            Entry("photo.jpg")
        ];

        Assert.Equal(["Alpha.txt"], LocalBrowserPresentation.ApplyFilter(entries, "LPH").Select(item => item.Name));
        Assert.Equal(
            ["Alpha.txt", "archive.zip"],
            LocalBrowserPresentation.ApplyFilter(entries, "a*.*").Select(item => item.Name));
        Assert.Equal(entries, LocalBrowserPresentation.ApplyFilter(entries, ""));
        Assert.Equal("TXT file", LocalBrowserPresentation.DescribeFileType(".txt"));
        Assert.Equal("File", LocalBrowserPresentation.DescribeFileType(null));
    }

    [Fact]
    public async Task DataSourceEnumeratesDirectoriesFirstAndFormatsMetadataWithoutMutatingFiles()
    {
        var root = Directory.CreateTempSubdirectory("StorageHub.Browser.");
        try
        {
            var folderPath = Directory.CreateDirectory(Path.Combine(root.FullName, "Folder")).FullName;
            _ = Directory.CreateDirectory(Path.Combine(root.FullName, ".storagehub-internal"));
            var filePath = Path.Combine(root.FullName, "sample.bin");
            await File.WriteAllBytesAsync(filePath, [1, 2, 3, 4, 5]);
            var source = new LocalFileBrowserDataSource();

            var snapshot = await source.BrowseAsync(
                LocalBrowserLocation.FromDirectory(root.FullName),
                CancellationToken.None);

            Assert.Equal(root.FullName, snapshot.Location.DirectoryPath);
            Assert.Collection(
                snapshot.Entries,
                folder =>
                {
                    Assert.Equal("Folder", folder.Name);
                    Assert.Equal(folderPath, folder.FullPath);
                    Assert.True(folder.IsContainer);
                    Assert.Null(folder.Length);
                    Assert.Equal("File folder", folder.Type);
                },
                file =>
                {
                    Assert.Equal("sample.bin", file.Name);
                    Assert.Equal(filePath, file.FullPath);
                    Assert.False(file.IsContainer);
                    Assert.Equal(5, file.Length);
                    Assert.Equal("BIN file", file.Type);
                    Assert.NotNull(file.Modified);
                });
            Assert.Equal([1, 2, 3, 4, 5], await File.ReadAllBytesAsync(filePath));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PagedDataSourceStreamsLargeDirectoriesThroughOpaqueContinuations()
    {
        var root = Directory.CreateTempSubdirectory("StorageHub.PagedBrowser.");
        try
        {
            for (var index = 0; index < 205; index++)
            {
                await File.WriteAllTextAsync(Path.Combine(root.FullName, $"file-{index:D3}.txt"), "x");
            }

            using var source = new LocalFileBrowserDataSource();
            var location = LocalBrowserLocation.FromDirectory(root.FullName);
            var first = await source.BrowsePageAsync(location, 100, null, CancellationToken.None);
            var second = await source.BrowsePageAsync(location, 100, first.ContinuationToken, CancellationToken.None);
            var third = await source.BrowsePageAsync(location, 100, second.ContinuationToken, CancellationToken.None);

            Assert.Equal(100, first.Entries.Count);
            Assert.Equal(100, second.Entries.Count);
            Assert.Equal(5, third.Entries.Count);
            Assert.True(first.HasMore);
            Assert.True(second.HasMore);
            Assert.False(third.HasMore);
            Assert.Equal(205, third.IndexedEntryCount);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ControllerDoesNotCommitFailedNavigationOrExposeExceptionDetails()
    {
        await using var controller = new LocalBrowserController(new ThrowingDataSource(
            new UnauthorizedAccessException("sensitive-provider-detail")));
        var destination = LocalBrowserLocation.FromDirectory(Path.Combine(Path.GetTempPath(), "restricted"));

        var result = await controller.NavigateAsync(
            LocalBrowserNavigationKind.Navigate,
            destination,
            CancellationToken.None);

        Assert.Equal(LocalBrowserNavigationStatus.Failed, result.Status);
        Assert.Contains("Access denied", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sensitive-provider-detail", result.ErrorMessage, StringComparison.Ordinal);
        Assert.True(controller.CurrentLocation.IsThisPc);
        Assert.False(controller.CanGoBack);
    }

    [Fact]
    public async Task NewNavigationCancelsAndSupersedesAnOlderEnumeration()
    {
        var source = new SupersedingDataSource();
        await using var controller = new LocalBrowserController(source);
        var slow = LocalBrowserLocation.FromDirectory(Path.Combine(Path.GetTempPath(), "slow"));
        var fast = LocalBrowserLocation.FromDirectory(Path.Combine(Path.GetTempPath(), "fast"));

        var firstNavigation = controller.NavigateAsync(LocalBrowserNavigationKind.Navigate, slow);
        await source.SlowNavigationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondResult = await controller.NavigateAsync(
            LocalBrowserNavigationKind.Navigate,
            fast,
            CancellationToken.None);
        var firstResult = await firstNavigation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(LocalBrowserNavigationStatus.Succeeded, secondResult.Status);
        Assert.Equal(LocalBrowserNavigationStatus.Superseded, firstResult.Status);
        Assert.True(controller.CurrentLocation.IsSameLocation(fast));
        Assert.True(controller.CanGoBack);
    }

    [Fact]
    public async Task ControllerKeepsOnlyCurrentLocalPageWhileTrackingContinuationProgress()
    {
        var source = new PagedFakeDataSource();
        await using var controller = new LocalBrowserController(source);
        var location = LocalBrowserLocation.FromDirectory(Path.Combine(Path.GetTempPath(), "paged"));

        var opened = await controller.NavigateAsync(LocalBrowserNavigationKind.Navigate, location);
        var more = await controller.LoadMoreAsync();

        Assert.Single(opened.Snapshot!.Entries);
        Assert.True(opened.Snapshot.HasMore);
        Assert.Equal(LocalBrowserNavigationStatus.Succeeded, more.Status);
        Assert.True(more.AppendedPage);
        Assert.Single(more.Snapshot!.Entries);
        Assert.Equal("second.txt", more.Snapshot.Entries[0].Name);
        Assert.Equal(2, more.Snapshot.IndexedEntryCount);
        Assert.False(more.Snapshot.HasMore);
    }

    [Fact]
    public async Task NavigatingAwayDoesNotWaitForAPageStillBeingRead()
    {
        var root = Directory.CreateTempSubdirectory("StorageHub.SlowBrowser.");
        using var pageBlocked = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        try
        {
            var slow = Directory.CreateDirectory(Path.Combine(root.FullName, "slow"));
            var fast = Directory.CreateDirectory(Path.Combine(root.FullName, "fast"));
            var file = Path.Combine(slow.FullName, "a.txt");
            await File.WriteAllTextAsync(file, "x");

            // The second page of "slow" stalls after its first entry, the way a network share does.
            IEnumerable<FileSystemInfo> Enumerate(DirectoryInfo directory)
            {
                if (!string.Equals(directory.FullName, slow.FullName, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var item in directory.EnumerateFileSystemInfos()) yield return item;
                    yield break;
                }

                for (var index = 0; index < LocalBrowserController.DefaultPageSize + 1; index++)
                {
                    yield return new FileInfo(file);
                }

                pageBlocked.Set();
                gate.Wait(TimeSpan.FromSeconds(30));
                yield return new FileInfo(file);
            }

            await using var controller = new LocalBrowserController(new LocalFileBrowserDataSource(Enumerate));
            var opened = await controller.NavigateAsync(
                LocalBrowserNavigationKind.Navigate,
                LocalBrowserLocation.FromDirectory(slow.FullName));
            Assert.True(opened.Snapshot!.HasMore);

            var loadMore = controller.LoadMoreAsync();
            Assert.True(pageBlocked.Wait(TimeSpan.FromSeconds(5)));
            var navigation = Task.Run(() => controller.NavigateAsync(
                LocalBrowserNavigationKind.Navigate,
                LocalBrowserLocation.FromDirectory(fast.FullName)));
            var navigated = await navigation.WaitAsync(TimeSpan.FromSeconds(5));

            gate.Set();
            var late = await loadMore.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(LocalBrowserNavigationStatus.Succeeded, navigated.Status);
            Assert.True(controller.CurrentLocation.IsSameLocation(LocalBrowserLocation.FromDirectory(fast.FullName)));
            Assert.Equal(LocalBrowserNavigationStatus.Superseded, late.Status);
        }
        finally
        {
            gate.Set();
            root.Delete(recursive: true);
        }
    }

    private static LocalBrowserEntry Entry(string name) =>
        new(name, name, IsContainer: false, 1, null, "File", string.Empty);

    private sealed class ThrowingDataSource(Exception error) : ILocalFileBrowserDataSource
    {
        public Task<LocalBrowserSnapshot> BrowseAsync(
            LocalBrowserLocation location,
            CancellationToken cancellationToken) => Task.FromException<LocalBrowserSnapshot>(error);
    }

    private sealed class SupersedingDataSource : ILocalFileBrowserDataSource
    {
        public TaskCompletionSource SlowNavigationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<LocalBrowserSnapshot> BrowseAsync(
            LocalBrowserLocation location,
            CancellationToken cancellationToken)
        {
            if (location.DirectoryPath?.EndsWith("slow", StringComparison.OrdinalIgnoreCase) == true)
            {
                SlowNavigationStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new LocalBrowserSnapshot(location, []);
        }
    }

    private sealed class PagedFakeDataSource : IPagedLocalFileBrowserDataSource
    {
        public Task<LocalBrowserSnapshot> BrowseAsync(
            LocalBrowserLocation location,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LocalBrowserSnapshot> BrowsePageAsync(
            LocalBrowserLocation location,
            int pageSize,
            string? continuationToken,
            CancellationToken cancellationToken) => Task.FromResult(continuationToken is null
                ? new LocalBrowserSnapshot(location, [Entry("first.txt")], "next", 1)
                : new LocalBrowserSnapshot(location, [Entry("second.txt")], null, 2));

        public void Release(string? continuationToken) { }
    }
}
