using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// How a connection's link changes read on its card and in the activity log, and how the log
/// reads them from the agent.
/// </summary>
public sealed class ConnectionLinkPresentationTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ACardSaysRetryingAndLostForAsLongAsTheyStandAndBackOnlyForAWhile()
    {
        var retrying = Badge(Change(ConnectionLinkEventKind.Retrying, Noon, attempt: 3), Noon.AddHours(1));
        Assert.Equal(Ui.Format(Ui.Connections.LinkRetryingAttemptFormat, 3), retrying?.Text);
        Assert.Equal(ConnectionLinkTone.Warning, retrying?.Tone);
        Assert.Equal(Ui.Connections.LinkRetrying, Badge(Change(ConnectionLinkEventKind.Retrying, Noon), Noon)?.Text);

        var lost = Badge(Change(ConnectionLinkEventKind.Lost, Noon), Noon.AddDays(1));
        Assert.Equal(Ui.Connections.LinkLost, lost?.Text);
        Assert.Equal(ConnectionLinkTone.Danger, lost?.Tone);
        Assert.Equal(Ui.Format(Ui.Connections.LinkSinceFormat, Noon.ToLocalTime()), lost?.Since);

        var back = Change(ConnectionLinkEventKind.Back, Noon);
        Assert.Equal(ConnectionLinkTone.Success, Badge(back, Noon.AddMinutes(1))?.Tone);
        Assert.Null(Badge(back, Noon + ConnectionLinkPresentation.BackShownFor));
        Assert.Null(Badge(null, Noon));
    }

    [Fact]
    public void EachChangeIsALogLineInTheConnectionArea()
    {
        var retrying = ConnectionLinkPresentation.ToActivity(Change(ConnectionLinkEventKind.Retrying, Noon, attempt: 2));
        Assert.Equal(Ui.Transfer.ActivityAreaConnection, retrying.Area);
        Assert.Equal("Archive", retrying.Item);
        Assert.Equal(Ui.Transfer.ActivityLinkRetrying, retrying.State);
        Assert.Equal(Ui.Format(Ui.Transfer.ActivityLinkRetryingFormat, 2), retrying.Details);
        Assert.StartsWith(ConnectionLinkPresentation.ActivityKeyPrefix, retrying.Key, StringComparison.Ordinal);

        var lost = ConnectionLinkPresentation.ToActivity(Change(ConnectionLinkEventKind.Lost, Noon));
        Assert.Equal(Ui.Transfer.ActivityLinkLost, lost.State);
        Assert.Equal(Ui.Transfer.ActivityLinkLostDetails, lost.Details);

        // With a code, the line says which way it was lost, as the pane's banner would.
        var timedOut = ConnectionLinkPresentation.ToActivity(
            Change(ConnectionLinkEventKind.Lost, Noon, code: "storage.timeout"));
        Assert.False(string.IsNullOrWhiteSpace(timedOut.Details));

        var back = ConnectionLinkPresentation.ToActivity(Change(ConnectionLinkEventKind.Back, Noon.AddMinutes(1)));
        Assert.Equal(Ui.Transfer.ActivityLinkBack, back.State);
        Assert.Equal(Ui.Transfer.ActivityLinkBackDetails, back.Details);
        Assert.NotEqual(lost.Key, back.Key);
    }

    [Fact]
    public void LinkChangesTakeTheirPlaceInTheLogByTime()
    {
        var entries = ActivityLog.Build(
            [],
            [],
            links: [Change(ConnectionLinkEventKind.Back, Noon), Change(ConnectionLinkEventKind.Lost, Noon.AddMinutes(-2))]);

        Assert.Equal([Ui.Transfer.ActivityLinkBack, Ui.Transfer.ActivityLinkLost], entries.Select(static entry => entry.State));
    }

    [Fact]
    public async Task TheLogReadsLinkChangesAndShowsItsWorkWhenTheyCannotBeRead()
    {
        var transfers = new StubActivityTransfers { Answer = () => new TransferListResponse(TransferQueueIpcContract.CurrentVersion, [], null) };
        var runs = new StubActivityRuns { Answer = () => new SyncRunListResponse(SyncManagementIpcContract.CurrentVersion, [], null) };
        var links = new LinkAgent(() => new ConnectionLinkEventsResponse(
            StorageIpcContract.CurrentVersion, [Change(ConnectionLinkEventKind.Lost, Noon)], []));

        var result = await new ActivityLogReader(() => transfers, () => runs, links: () => links).ReadAsync(CancellationToken.None);
        Assert.False(result.Failed);
        Assert.Equal(Ui.Transfer.ActivityLinkLost, Assert.Single(result.Entries).State);
        Assert.True(links.Disposed);

        // An agent too old to keep them, one that refuses, and one that fails: the log still reads.
        foreach (var answer in new Func<ConnectionLinkEventsResponse>[]
        {
            () => throw new NotSupportedException(),
            () => throw new IOException("Pipe broken."),
            () => new ConnectionLinkEventsResponse(
                StorageIpcContract.CurrentVersion,
                [],
                [],
                new StorageIpcFailure("storage.connections.unavailable", StorageIpcFailureCategory.Unavailable, "No.", true))
        })
        {
            var refusing = new LinkAgent(answer);
            var read = await new ActivityLogReader(() => transfers, () => runs, links: () => refusing).ReadAsync(CancellationToken.None);
            Assert.False(read.Failed);
            Assert.Empty(read.Entries);
        }
    }

    private static ConnectionLinkBadge? Badge(ConnectionLinkEvent? change, DateTimeOffset now) =>
        ConnectionLinkPresentation.Badge(change, now);

    private static long _sequence;

    internal static ConnectionLinkEvent Change(
        ConnectionLinkEventKind kind,
        DateTimeOffset at,
        int? attempt = null,
        string? code = null) =>
        new(Interlocked.Increment(ref _sequence), Guid.NewGuid(), "Archive", kind, at, attempt, code);

    private sealed class LinkAgent(Func<ConnectionLinkEventsResponse> answer) : IRemoteStorageAgentClient
    {
        internal bool Disposed { get; private set; }

        public Task<ConnectionLinkEventsResponse> ListLinkEventsAsync(
            ConnectionLinkEventsRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(answer());

        public Task<ConnectionListResponse> ListConnectionsAsync(
            ConnectionListRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The activity log never lists connections.");

        public Task<ConnectionTestResponse> TestConnectionAsync(
            ConnectionTestRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The activity log never tests connections.");

        public Task<StorageListPageResponse> ListStorageAsync(
            StorageListPageRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The activity log never lists folders.");

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
