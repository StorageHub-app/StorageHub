using CL.Storage.Events;
using CL.Storage.Models;
using CodeLogic.Core.Events;
using StorageHub.Domain.Identifiers;
using StorageHub.Storage.CodeLogic;

namespace StorageHub.Storage.CodeLogic.Tests;

public sealed class ConnectionLinkTrackerTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AnOutageIsOneRetryingOneLostAndOneBackHoweverManyEventsItTakes()
    {
        var tracker = new ConnectionLinkTracker();
        var changes = new List<ConnectionLinkChange>();
        tracker.Changed += changes.Add;
        var profile = ConnectionProfileId.New();

        // A session opening while nothing was wrong is not news.
        tracker.Reached(profile, At);
        tracker.Retrying(profile, 1, "storage.timeout", At.AddSeconds(1));
        tracker.Retrying(profile, 2, "storage.timeout", At.AddSeconds(2));
        tracker.Lost(profile, "storage.connection_lost", At.AddSeconds(3));
        // The library working its way back while lost is still lost.
        tracker.Retrying(profile, 3, "storage.timeout", At.AddSeconds(4));
        tracker.Reached(profile, At.AddSeconds(5));
        tracker.Reached(profile, At.AddSeconds(6));

        Assert.Equal(
            [ConnectionLinkState.Retrying, ConnectionLinkState.Lost, ConnectionLinkState.Back],
            changes.Select(static change => change.State));
        Assert.Equal(1, changes[0].Attempt);
        Assert.Equal("storage.timeout", changes[0].ErrorCode);
        Assert.Equal("storage.connection_lost", changes[1].ErrorCode);
        Assert.All(changes, change => Assert.Equal(profile, change.ProfileId));
    }

    /// <summary>
    /// Cancelling a transfer makes the library drop its session and reopen one a second later, and
    /// that is not the connection failing: the card said "Connected again" for pressing Cancel. A loss
    /// reported just after the interruption, on that connection, is not recorded; one on another
    /// connection, or well after, still is.
    /// </summary>
    [Fact]
    public void ASessionDroppedByCancellingATransferIsNotTheLinkFailing()
    {
        var tracker = new ConnectionLinkTracker();
        var changes = new List<ConnectionLinkChange>();
        tracker.Changed += changes.Add;
        var cancelled = ConnectionProfileId.New();
        var other = ConnectionProfileId.New();

        tracker.Reached(cancelled, At);
        tracker.Interrupted(cancelled, At);
        tracker.Lost(cancelled, "storage.connection_lost", At.AddSeconds(1));
        tracker.Reached(cancelled, At.AddSeconds(2));
        Assert.Empty(changes);

        // Another connection was not cancelled, so it is a real loss.
        tracker.Lost(other, "storage.connection_lost", At.AddSeconds(1));
        Assert.Equal(ConnectionLinkState.Lost, Assert.Single(changes).State);

        // And the same connection, long after, is a real loss too.
        tracker.Lost(cancelled, "storage.connection_lost", At.Add(ConnectionLinkTracker.InterruptionWindow).AddSeconds(5));
        Assert.Equal(2, changes.Count);
        Assert.Equal(cancelled, changes[1].ProfileId);
    }

    [Fact]
    public void OnlyAFailureToReachTheServerIsAboutTheLink()
    {
        var tracker = new ConnectionLinkTracker();
        var changes = new List<ConnectionLinkChange>();
        tracker.Changed += changes.Add;
        var profile = ConnectionProfileId.New();

        tracker.OperationFailed(profile, "storage.not_found", At);
        tracker.OperationFailed(profile, "storage.access_denied", At);
        tracker.OperationFailed(profile, null, At);
        Assert.Empty(changes);

        tracker.OperationFailed(profile, "storage.unavailable", At);
        tracker.HealthChecked(profile, healthy: true, null, At.AddSeconds(1));
        tracker.HealthChecked(profile, healthy: false, "storage.timeout", At.AddSeconds(2));

        Assert.Equal(
            [ConnectionLinkState.Lost, ConnectionLinkState.Back, ConnectionLinkState.Lost],
            changes.Select(static change => change.State));
    }

    [Fact]
    public void EachConnectionHasItsOwnLinkAndAnEmptyIdHasNone()
    {
        var tracker = new ConnectionLinkTracker();
        var changes = new List<ConnectionLinkChange>();
        tracker.Changed += changes.Add;
        var first = ConnectionProfileId.New();
        var second = ConnectionProfileId.New();

        tracker.Lost(first, null, At);
        tracker.Reached(second, At);
        tracker.Lost(default, null, At);
        tracker.Reached(first, At);

        Assert.Equal(
            [(first, ConnectionLinkState.Lost), (first, ConnectionLinkState.Back)],
            changes.Select(static change => (change.ProfileId, change.State)));
    }

    [Fact]
    public void ARuntimeIdNamesTheProfileItWasRegisteredForAndNothingElseDoes()
    {
        var profile = ConnectionProfileId.New();

        Assert.Equal(profile, CodeLogicStorageSessionFactory.ProfileOf($"storagehub-{profile.Value:N}-{Guid.NewGuid():N}"));
        Assert.Null(CodeLogicStorageSessionFactory.ProfileOf(null));
        Assert.Null(CodeLogicStorageSessionFactory.ProfileOf("primary"));
        Assert.Null(CodeLogicStorageSessionFactory.ProfileOf($"somebody-{profile.Value:N}-{Guid.NewGuid():N}"));
        Assert.Null(CodeLogicStorageSessionFactory.ProfileOf($"storagehub-{Guid.Empty:N}-{Guid.NewGuid():N}"));
        Assert.Null(CodeLogicStorageSessionFactory.ProfileOf($"storagehub-{profile.Value:N}_{Guid.NewGuid():N}"));
        Assert.Null(CodeLogicStorageSessionFactory.ProfileOf($"storagehub-{profile.Value:N}-{new string('z', 32)}"));
    }

    [Fact]
    public async Task TheLibrarysEventsReachTheTrackerForThisApplicationsRegistrationsOnly()
    {
        var bus = new EventBus();
        var tracker = new ConnectionLinkTracker();
        var changes = new List<ConnectionLinkChange>();
        tracker.Changed += change =>
        {
            lock (changes) changes.Add(change);
        };
        var profile = ConnectionProfileId.New();
        var ours = $"storagehub-{profile.Value:N}-{Guid.NewGuid():N}";
        var theirs = $"storagehub-{profile.Value:N}-{Guid.NewGuid():N}";

        using (new CodeLogicConnectionLinkFeed(bus, tracker))
        {
            await bus.PublishAsync(new StorageConnectionLostEvent("primary", StorageProvider.Sftp, "list", "storage.connection_lost", At));
            await bus.PublishAsync(new StorageConnectionRetryEvent(ours, StorageProvider.Sftp, "list", 1, TimeSpan.FromSeconds(1), "storage.timeout", At));
            await bus.PublishAsync(new StorageConnectionLostEvent(theirs, StorageProvider.Sftp, "list", "storage.connection_lost", At.AddSeconds(1)));
            await bus.PublishAsync(new StorageItemWrittenEvent(ours, StorageProvider.Sftp, "/a.txt", At.AddSeconds(2)));
        }

        // Unsubscribed: nothing more arrives.
        await bus.PublishAsync(new StorageConnectionLostEvent(ours, StorageProvider.Sftp, "list", "storage.connection_lost", At.AddSeconds(3)));

        lock (changes)
        {
            Assert.Equal(
                [ConnectionLinkState.Retrying, ConnectionLinkState.Lost, ConnectionLinkState.Back],
                changes.Select(static change => change.State));
            Assert.All(changes, change => Assert.Equal(profile, change.ProfileId));
        }
    }
}
