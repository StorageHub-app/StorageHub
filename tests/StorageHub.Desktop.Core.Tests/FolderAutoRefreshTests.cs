namespace StorageHub.Desktop.Tests;

/// <summary>
/// When a pane reads its folder again by itself: after a watched folder settles, every so often
/// for a connection's, and never while the setting is off.
/// </summary>
public sealed class FolderAutoRefreshTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly FolderAutoRefreshSettings On = FolderAutoRefreshSettings.From(true, 30);

    [Fact]
    public void AConnectionsFolderIsDueEveryIntervalFromItsLastRead()
    {
        var now = Noon;
        using var refresh = new FolderAutoRefresh(static (_, _) => throw new InvalidOperationException("Nothing to watch."));
        refresh.Follow(PaneFolderChanges.Polling, On, () => now);

        Assert.False(refresh.Due(Noon.AddSeconds(29)));
        Assert.True(refresh.Due(Noon.AddSeconds(30)));
        refresh.Read(Noon.AddSeconds(31));
        Assert.False(refresh.Due(Noon.AddSeconds(60)));
        Assert.True(refresh.Due(Noon.AddSeconds(61)));
    }

    [Fact]
    public void AWatchedFolderIsDueOnceItSettlesOrHasWaitedLongEnough()
    {
        var now = Noon;
        Action? report = null;
        var watched = new List<string>();
        using var refresh = new FolderAutoRefresh((folder, changed) =>
        {
            watched.Add(folder);
            report = changed;
            return new Stopped();
        });
        refresh.Follow(PaneFolderChanges.Watched("/data/photos"), On, () => now);
        Assert.Equal(["/data/photos"], watched);
        Assert.True(refresh.IsWatching);

        // Nothing changed: never due, however long it waits.
        Assert.False(refresh.Due(Noon.AddHours(1)));

        report!();
        Assert.False(refresh.Due(Noon.AddMilliseconds(500)));
        Assert.True(refresh.Due(Noon + FolderAutoRefresh.Settle));
        refresh.Read(Noon + FolderAutoRefresh.Settle);
        Assert.False(refresh.Due(Noon.AddMinutes(1)));

        // A folder that never stops changing is still shown, every so often.
        for (var half = 0; half <= 10; half++)
        {
            now = Noon.AddMinutes(2).AddSeconds(half * 0.5);
            report();
        }

        Assert.True(refresh.Due(now));
    }

    [Fact]
    public void FollowingTheSameFolderAgainChangesNothingAndElsewhereLetsTheOldGo()
    {
        var stopped = new List<Stopped>();
        using var refresh = new FolderAutoRefresh((_, _) =>
        {
            var watcher = new Stopped();
            stopped.Add(watcher);
            return watcher;
        });
        refresh.Follow(PaneFolderChanges.Watched("/a"), On, () => Noon);
        refresh.Follow(PaneFolderChanges.Watched("/a"), On, () => Noon);
        Assert.Single(stopped);

        refresh.Follow(PaneFolderChanges.Watched("/b"), On, () => Noon);
        Assert.Equal([true, false], stopped.Select(static watcher => watcher.Disposed));

        // Turned off: nothing is watched, and nothing is ever due.
        refresh.Follow(PaneFolderChanges.Watched("/b"), FolderAutoRefreshSettings.Off, () => Noon);
        Assert.True(stopped[1].Disposed);
        Assert.False(refresh.IsWatching);
        refresh.Changed();
        Assert.False(refresh.Due(Noon.AddHours(1)));

        refresh.Follow(PaneFolderChanges.Polling, FolderAutoRefreshSettings.Off, () => Noon);
        Assert.False(refresh.Due(Noon.AddHours(1)));
    }

    [Fact]
    public void AFolderThatCannotBeWatchedIsLeftAsItWas()
    {
        using var refresh = new FolderAutoRefresh(static (_, _) => null);
        refresh.Follow(PaneFolderChanges.Watched("/gone"), On, () => Noon);

        Assert.False(refresh.IsWatching);
        Assert.False(refresh.Due(Noon.AddHours(1)));
        Assert.Null(LocalFolderWatch.Start(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), static () => { }));
    }

    [Fact]
    public void TheIntervalIsKeptWithinItsBounds()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), FolderAutoRefreshSettings.From(true, 1).RemoteInterval);
        Assert.Equal(TimeSpan.FromSeconds(600), FolderAutoRefreshSettings.From(true, 86_400).RemoteInterval);
        Assert.False(FolderAutoRefreshSettings.IsValidRemoteSeconds(9));
        Assert.True(FolderAutoRefreshSettings.IsValidRemoteSeconds(600));
        Assert.False(FolderAutoRefreshSettings.Off.Enabled);
    }

    /// <summary>A real watcher reports a file landing in the folder it watches, and nothing after it stops.</summary>
    [Fact]
    public async Task AFolderOnThisComputerReportsAFileLandingInIt()
    {
        var folder = Directory.CreateTempSubdirectory("storagehub-watch-").FullName;
        try
        {
            var changes = 0;
            using (var watcher = LocalFolderWatch.Start(folder, () => Interlocked.Increment(ref changes)))
            {
                Assert.NotNull(watcher);
                await File.WriteAllTextAsync(Path.Combine(folder, "landed.txt"), "hello");
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (Volatile.Read(ref changes) == 0 && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(50);
                }

                Assert.True(Volatile.Read(ref changes) > 0);
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private sealed class Stopped : IDisposable
    {
        internal bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
