using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The folder tree reads the folders below the one the pane is in, within limits that are gentler
/// on a server than on a disk, and only while the Scan subfolders setting is on.
/// </summary>
public sealed class PaneTreeScanTests
{
    private static BrowserListItem Folder(string name, string location) =>
        new(name, string.Empty, "Folder", string.Empty, string.Empty, location, IsContainer: true);

    [Fact]
    public async Task TheTreeScansWithinItsLimitsStopsWhenThePaneMovesOnAndReadsNothingWhenOff()
    {
        await ReadsAFewLevelsAndOpensWhatIsBeyondReach();
        await ARefusedReadIsNotRememberedAsEmpty();
        await StaysWithinItsRequestBudgetAndNumberAtOnce();
        await MovingOnStopsAScanAndTheSettingOffReadsNothing();
    }

    private static async Task ReadsAFewLevelsAndOpensWhatIsBeyondReach()
    {
        // a -> b (b1 -> b1x, b2), c (c1)
        var folders = new Dictionary<string, string[]>
        {
            ["a/b"] = ["b1", "b2"],
            ["a/c"] = ["c1"],
            ["a/b/b1"] = ["b1x"],
        };
        var requests = new List<string>();
        var model = new PaneTreeModel
        {
            ScanEnabled = () => true,
            ListFolders = (target, _, _) =>
            {
                lock (requests) requests.Add(target);
                return Task.FromResult<IReadOnlyList<PaneFolder>?>(folders.TryGetValue(target, out var names)
                    ? [.. names.Select(name => new PaneFolder(name, $"{target}/{name}", false))]
                    : []);
            },
        };

        model.Follow("conn", "Server", local: false, "a", [Folder("b", "a/b"), Folder("c", "a/c")]);
        var a = model.Roots[0].Children.Single(node => node.Target == "a");
        await WaitUntil(() => a.Children.All(child => child.Children.Count > 0));

        // Two levels down, and no further: the folders it lists, and what is inside them.
        Assert.Equal(["a/b", "a/c"], requests.Order().ToArray());
        var b = a.Children.Single(node => node.Name == "b");
        Assert.Equal(["b1", "b2"], b.Children.Select(node => node.Name).ToArray());
        var b1 = b.Children.Single(node => node.Name == "b1");
        var b2 = b.Children.Single(node => node.Name == "b2");

        // Nothing is shown as openable that is not known to hold folders: what is below b's
        // folders has not been read, so they have no expander yet, rather than one that opens
        // onto nothing.
        Assert.Empty(b1.Children);
        Assert.Empty(b2.Children);

        // Opening b reads the folders in view in it, so each shows an expander only if it should.
        b.IsExpanded = true;
        await WaitUntil(() => b1.Children.Any(node => node.Name == "b1x"));
        Assert.Equal(4, requests.Count);
        Assert.Empty(b2.Children);

        // And opening those reads theirs, once.
        b1.IsExpanded = true;
        await WaitUntil(() => requests.Count == 5);
        b.IsExpanded = false;
        b.IsExpanded = true;
        b1.IsExpanded = false;
        b1.IsExpanded = true;
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Equal(5, requests.Count);
    }

    private static async Task ARefusedReadIsNotRememberedAsEmpty()
    {
        var answers = 0;
        var model = new PaneTreeModel
        {
            ScanEnabled = () => true,
            ListFolders = (target, _, _) =>
            {
                Interlocked.Increment(ref answers);
                return Task.FromResult<IReadOnlyList<PaneFolder>?>(null);
            },
        };

        model.Follow("conn", "Server", local: false, "a", [Folder("b", "a/b")]);
        await WaitUntil(() => Volatile.Read(ref answers) == 1);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        var b = model.Roots[0].Children.Single(node => node.Target == "a").Children.Single();

        // Not "read, and empty": it is marked as failed, so it is left alone for a little while
        // and then tried again, instead of being believed for five minutes.
        Assert.Null(b.ScannedUtc);
        Assert.NotNull(b.ScanFailedUtc);
        model.Follow("conn", "Server", local: false, "a", [Folder("b", "a/b")]);
        await Task.Delay(150, TestContext.Current.CancellationToken);
        Assert.Equal(1, Volatile.Read(ref answers));
        b.ScanFailedUtc = DateTimeOffset.UtcNow - PaneTreeModel.ScanRetryAfter - TimeSpan.FromSeconds(1);
        model.Follow("conn", "Server", local: false, "a", [Folder("b", "a/b")]);
        await WaitUntil(() => Volatile.Read(ref answers) == 2);
    }

    private static async Task StaysWithinItsRequestBudgetAndNumberAtOnce()
    {
        var inFlight = 0;
        var mostAtOnce = 0;
        var requests = 0;
        var model = new PaneTreeModel
        {
            ScanEnabled = () => true,
            ListFolders = async (_, _, _) =>
            {
                Interlocked.Increment(ref requests);
                var now = Interlocked.Increment(ref inFlight);
                lock (Gate) mostAtOnce = Math.Max(mostAtOnce, now);
                await Task.Delay(2, CancellationToken.None);
                Interlocked.Decrement(ref inFlight);
                return [];
            },
        };

        var rows = Enumerable.Range(0, 250).Select(number => Folder($"f{number:000}", $"a/f{number:000}")).ToArray();
        model.Follow("conn", "Server", local: false, "a", rows);
        await WaitUntil(() => Volatile.Read(ref requests) >= PaneTreeModel.ScanLimits.Remote.Requests);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        Assert.Equal(PaneTreeModel.ScanLimits.Remote.Requests, Volatile.Read(ref requests));
        Assert.True(mostAtOnce <= PaneTreeModel.ScanLimits.Remote.Parallel, $"{mostAtOnce} at once");
    }

    private static async Task MovingOnStopsAScanAndTheSettingOffReadsNothing()
    {
        var hold = new TaskCompletionSource();
        var requests = 0;
        var scanning = true;
        var model = new PaneTreeModel
        {
            ScanEnabled = () => scanning,
            ListFolders = async (target, _, _) =>
            {
                Interlocked.Increment(ref requests);
                await hold.Task;
                return [new PaneFolder("deeper", target + "/deeper", false)];
            },
        };

        model.Follow("one", "One", local: false, "a", [Folder("b", "a/b")]);
        await WaitUntil(() => Volatile.Read(ref requests) == 1);

        // The pane has moved on to somewhere else before the read answered, so it is dropped.
        scanning = false;
        model.Follow("two", "Two", local: false, "x", [Folder("y", "x/y")]);
        hold.SetResult();
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.Equal("Two", model.Roots[0].Name);
        Assert.Equal(1, Volatile.Read(ref requests));

        // With the setting off nothing is read and nothing has a stand-in.
        var x = model.Roots[0].Children.Single(node => node.Target == "x");
        Assert.All(x.Children, child => Assert.Empty(child.Children));
    }

    private static readonly Lock Gate = new();

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition(), "The condition was not met in time.");
    }
}
