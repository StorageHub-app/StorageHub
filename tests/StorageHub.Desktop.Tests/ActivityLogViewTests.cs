using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The activity log in the queue's Logs tab.
/// </summary>
/// <remarks>
/// What goes into the log and in what order is pinned in ActivityLogTests. What is checked here is
/// the screen: that a poll edits only the rows that changed, that the selection survives it, that a
/// background poll is throttled and quiet, and that the Logs tab reads the log rather than asking
/// the queue for a list with no states in it.
/// </remarks>
public sealed class ActivityLogViewTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A new record arriving at the top is one insert. The rows below it are the same objects they
    /// were, which is what keeps the table from redrawing every line on every poll.
    /// </summary>
    [AvaloniaFact]
    public void ANewRecordIsOneInsert()
    {
        var model = new ActivityLogModel(Reads());
        model.Reconcile([Row("b"), Row("c")]);
        var before = model.Rows.ToArray();

        model.Reconcile([Row("a"), Row("b"), Row("c")]);

        Assert.Equal(["a", "b", "c"], model.Rows.Select(row => row.Key));
        Assert.Same(before[0], model.Rows[1]);
        Assert.Same(before[1], model.Rows[2]);
    }

    /// <summary>An unchanged row is not touched at all.</summary>
    [AvaloniaFact]
    public void AnUnchangedPollChangesNothing()
    {
        var model = new ActivityLogModel(Reads());
        model.Reconcile([Row("a"), Row("b")]);
        var changes = 0;
        model.Rows.CollectionChanged += (_, _) => changes++;

        model.Reconcile([Row("a"), Row("b")]);

        Assert.Equal(0, changes);
    }

    /// <summary>
    /// A record whose state changed is replaced where it stands, and stays selected if it was.
    /// </summary>
    /// <remarks>
    /// Rows are records, so newer text is a new object; without carrying the selection across, a
    /// transfer ticking from Transferring to Completed would deselect itself under the pointer.
    /// </remarks>
    [AvaloniaFact]
    public void AChangedRowKeepsItsSelection()
    {
        var model = new ActivityLogModel(Reads());
        model.Reconcile([Row("a"), Row("b", state: "Transferring")]);
        model.Selected = model.Rows[1];

        model.Reconcile([Row("a"), Row("b", state: "Completed")]);

        Assert.Equal("Completed", model.Rows[1].State);
        Assert.Same(model.Rows[1], model.Selected);
    }

    /// <summary>A record that moved up keeps its object, and so its selection, without a replace.</summary>
    [AvaloniaFact]
    public void AMovedRowIsTheSameRow()
    {
        var model = new ActivityLogModel(Reads());
        model.Reconcile([Row("a"), Row("b"), Row("c")]);
        var moved = model.Rows[2];
        model.Selected = moved;

        model.Reconcile([Row("c"), Row("a"), Row("b")]);

        Assert.Same(moved, model.Rows[0]);
        Assert.Same(moved, model.Selected);
    }

    [AvaloniaFact]
    public void WhatTheLogNoLongerHoldsIsRemoved()
    {
        var model = new ActivityLogModel(Reads());
        model.Reconcile([Row("a"), Row("b"), Row("c")]);

        model.Reconcile([Row("b")]);

        Assert.Equal(["b"], model.Rows.Select(row => row.Key));
    }

    /// <summary>
    /// A timer tick does not reach the agent more than every five seconds, and does not announce
    /// itself. Somebody pressing Refresh always reaches it.
    /// </summary>
    [AvaloniaFact]
    public async Task ABackgroundPollIsThrottledAndQuiet()
    {
        var now = Noon;
        var reads = 0;
        var model = new ActivityLogModel(
            _ => { reads++; return Task.FromResult(new ActivityLogResult([Entry("a")], 0)); },
            () => now);

        await model.RefreshAsync(background: true);
        Assert.Equal(1, reads);

        now += TimeSpan.FromSeconds(2);
        await model.RefreshAsync(background: true);
        Assert.Equal(1, reads);

        await model.RefreshAsync(background: false);
        Assert.Equal(2, reads);

        now += ActivityLogModel.PollInterval;
        await model.RefreshAsync(background: true);
        Assert.Equal(3, reads);
    }

    /// <summary>
    /// A failed read leaves the rows where they were and says why. A table that emptied whenever
    /// the agent restarted would be worse than one that is a poll old.
    /// </summary>
    [AvaloniaFact]
    public async Task AFailedReadKeepsTheRowsAndSaysWhy()
    {
        var fail = false;
        var model = new ActivityLogModel(_ => Task.FromResult(fail
            ? new ActivityLogResult([], 0, "The background agent is not running.")
            : new ActivityLogResult([Entry("a"), Entry("b")], 0)));

        await model.RefreshAsync();
        fail = true;
        await model.RefreshAsync();

        Assert.Equal(2, model.Rows.Count);
        Assert.True(model.Status.IsDanger);
        Assert.Equal("The background agent is not running.", model.Status.Text);
    }

    /// <summary>
    /// The Logs tab reads the log, counts it, and never asks the queue for a list.
    /// </summary>
    /// <remarks>
    /// The queue's own client throws if it is made at all. A list request with no states would be
    /// refused by the contract, so the Logs tab asking the queue would be a failure on every poll.
    /// </remarks>
    [AvaloniaFact]
    public async Task TheLogsTabReadsTheLogRatherThanTheQueue()
    {
        var log = new ActivityLogModel(_ => Task.FromResult(new ActivityLogResult([Entry("a"), Entry("b")], 0)));
        await using var queue = new TransferQueueModel(
            () => throw new InvalidOperationException("The Logs tab must not ask the queue."), log);
        var logs = queue.Tabs.Single(tab => tab.IsLog);

        queue.SelectedTab = queue.Tabs.IndexOf(logs);
        await queue.RefreshAsync();

        Assert.Equal(2, log.Rows.Count);
        Assert.Contains("(2)", logs.Title, StringComparison.Ordinal);
        Assert.Single(queue.Tabs, tab => tab.IsLog);
    }

    /// <summary>
    /// A queue failure is not left on the toolbar once the Logs tab is showing.
    /// </summary>
    /// <remarks>
    /// Found in the first photograph of this tab: the toolbar said the queue was unavailable beside
    /// a log that had just been read from the same agent.
    /// </remarks>
    [AvaloniaFact]
    public async Task TheLogsTabDoesNotRepeatAStaleQueueFailure()
    {
        var log = new ActivityLogModel(_ => Task.FromResult(new ActivityLogResult([Entry("a")], 0)));
        await using var queue = new TransferQueueModel(() => throw new IOException("Pipe not found."), log);

        await queue.RefreshAsync();
        Assert.False(string.IsNullOrEmpty(queue.Message));

        queue.SelectedTab = queue.Tabs.IndexOf(queue.Tabs.Single(tab => tab.IsLog));
        await queue.RefreshAsync();

        Assert.Empty(queue.Message);
    }

    /// <summary>Without a log, the tab says it is not loaded rather than failing.</summary>
    [AvaloniaFact]
    public async Task AQueueWithoutALogSaysSo()
    {
        await using var queue = new TransferQueueModel(
            () => throw new InvalidOperationException("The Logs tab must not ask the queue."));

        queue.SelectedTab = queue.Tabs.IndexOf(queue.Tabs.Single(tab => tab.IsLog));
        await queue.RefreshAsync();

        Assert.Equal(Ui.Transfer.ActivityNotLoaded, queue.Message);
    }

    /// <summary>
    /// Photographs the queue on its Logs tab, in both appearances.
    /// </summary>
    /// <remarks>
    /// The first photograph of the queue as a view of its own, and of the log in it: a tab strip, a
    /// status line and five columns, the last of which carries the long text. Set
    /// STORAGEHUB_SHOT_DIR to keep the files.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheLogsTabCanBePhotographed(bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));

        var entries = ActivityLog.Build(
            [
                Transfer(Noon, TransferQueueState.Transferring, "/photos/2026/berlin", "/backup/photos/2026/berlin"),
                Transfer(Noon.AddMinutes(-4), TransferQueueState.Completed, "/invoices/q3.pdf", "/archive/invoices/q3.pdf"),
                Transfer(Noon.AddMinutes(-9), TransferQueueState.Failed, "/video/raw", "/nas/video/raw") with
                {
                    ErrorSummary = "The destination refused the write: quota exceeded."
                }
            ],
            []);
        var log = new ActivityLogModel(_ => Task.FromResult(new ActivityLogResult(entries, 0)));
        await using var queue = new TransferQueueModel(
            () => throw new InvalidOperationException("Not polled in a photograph."), log);
        queue.SelectedTab = queue.Tabs.IndexOf(queue.Tabs.Single(tab => tab.IsLog));
        await queue.RefreshAsync();

        var window = new Window { Content = new TransferQueueView { DataContext = queue } };
        window.Show();
        window.Measure(new Size(1000, 260));
        window.Arrange(new Rect(0, 0, 1000, 260));
        window.UpdateLayout();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;

        Directory.CreateDirectory(directory);
        using var stream = File.Create(
            Path.Combine(directory, $"activity-log-{(dark ? "dark" : "light")}.png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }

    private static Func<CancellationToken, Task<ActivityLogResult>> Reads() =>
        _ => Task.FromResult(new ActivityLogResult([], 0));

    private static ActivityRow Row(string key, string state = "Completed") =>
        new(key, "22-09-2026 12:00", "Transfer", key, state, "Copy: / → /backup");

    private static ActivityEntry Entry(string key) =>
        new(key, Noon, "Transfer", key, "Completed", "Copy: / → /backup");

    private static TransferQueueSummary Transfer(
        DateTimeOffset updated, TransferQueueState state, string source, string destination) => new(
        Guid.NewGuid(),
        TransferQueueOperation.Copy,
        Guid.NewGuid(),
        source,
        Guid.NewGuid(),
        destination,
        state,
        Revision: 1,
        Attempt: 1,
        Priority: 0,
        ExpectedBytes: 100,
        ProgressBytes: 50,
        UpdatedUtc: updated,
        RetryAvailableUtc: null,
        ErrorCode: null,
        ErrorSummary: null,
        CanCancel: true,
        CanRetry: false,
        NeedsReconciliation: false);
}
