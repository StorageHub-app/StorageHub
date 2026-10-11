using CL.Storage.Events;
using CodeLogic.Core.Events;
using StorageHub.Domain.Identifiers;

namespace StorageHub.Storage.CodeLogic;

/// <summary>How a saved connection's link to its server has just changed.</summary>
public enum ConnectionLinkState
{
    /// <summary>An operation failed on something that may pass, and the library is trying again.</summary>
    Retrying = 1,

    /// <summary>A session dropped, a health check failed, or an operation ran out of retries on it.</summary>
    Lost = 2,

    /// <summary>After retrying or losing it, the connection has worked again.</summary>
    Back = 3
}

/// <param name="Attempt">Which retry this is, for <see cref="ConnectionLinkState.Retrying"/>.</param>
/// <param name="ErrorCode">The library's <c>storage.*</c> code for what went wrong, if anything did.</param>
public sealed record ConnectionLinkChange(
    ConnectionProfileId ProfileId,
    ConnectionLinkState State,
    DateTimeOffset OccurredUtc,
    int? Attempt = null,
    string? ErrorCode = null);

/// <summary>
/// Turns what the storage library says about its connections into the three changes somebody
/// cares about -- retrying, lost, back -- once each, per saved connection.
/// </summary>
/// <remarks>
/// <para>
/// The library has no single "connection state". It says when a session opens, when one is retired
/// because it dropped, when an operation is about to be tried again, when one failed, and when a
/// health check comes out differently from the last. Each of those is per registration, and a
/// profile has several (the panes', the queue's, sync's), so this keeps one state per profile and
/// reports only when it moves: a burst of five retries is one "retrying", and the sessions every
/// listing opens are not news unless the connection was in trouble.
/// </para>
/// <para>
/// "Back" has no event of its own, because the library reports no success. It is inferred from
/// the things that only happen when the server is answering: a new session opening (which is how
/// FTP and SFTP recover), a passing health check, a write, copy, move or delete that went through,
/// and -- for an object store, which has no sessions -- whatever the caller tells
/// <see cref="Reached"/>, such as a listing that came back.
/// </para>
/// </remarks>
public sealed class ConnectionLinkTracker
{
    /// <summary>The library's codes for a failure that is about reaching the server at all.</summary>
    private static readonly HashSet<string> LinkFailures = new(StringComparer.Ordinal)
    {
        "storage.timeout",
        "storage.unavailable",
        "storage.connection_failed",
        "storage.connection_lost",
        "storage.server_busy",
        "storage.tls_failure"
    };

    /// <summary>More connections than anybody saves; past it, the quietest are forgotten.</summary>
    private const int MaximumTracked = 512;

    private readonly object _gate = new();
    private readonly Dictionary<ConnectionProfileId, (ConnectionLinkState State, DateTimeOffset At)> _states = [];

    /// <summary>Raised outside the lock, once per change, in the order the changes happened.</summary>
    public event Action<ConnectionLinkChange>? Changed;

    /// <summary>The library is about to try an operation again.</summary>
    public void Retrying(ConnectionProfileId profile, int attempt, string? errorCode, DateTimeOffset at) =>
        Move(profile, ConnectionLinkState.Retrying, at, attempt, errorCode, from: null);

    /// <summary>A session dropped and was retired.</summary>
    public void Lost(ConnectionProfileId profile, string? errorCode, DateTimeOffset at) =>
        Move(profile, ConnectionLinkState.Lost, at, null, errorCode, from: null);

    /// <summary>
    /// An operation failed. Only a failure to reach the server is about the link; a missing file
    /// or a refused permission says the server answered.
    /// </summary>
    public void OperationFailed(ConnectionProfileId profile, string? errorCode, DateTimeOffset at)
    {
        if (errorCode is not null && LinkFailures.Contains(errorCode))
        {
            Move(profile, ConnectionLinkState.Lost, at, null, errorCode, from: null);
        }
    }

    /// <summary>A health check came out differently from the one before it.</summary>
    public void HealthChecked(ConnectionProfileId profile, bool healthy, string? errorCode, DateTimeOffset at)
    {
        if (healthy) Reached(profile, at);
        else Move(profile, ConnectionLinkState.Lost, at, null, errorCode, from: null);
    }

    /// <summary>
    /// Something worked on the connection: a session opened, a write went through, a listing came
    /// back. Back, if it was retrying or lost; nothing otherwise.
    /// </summary>
    public void Reached(ConnectionProfileId profile, DateTimeOffset at) =>
        Move(profile, ConnectionLinkState.Back, at, null, null, from: [ConnectionLinkState.Retrying, ConnectionLinkState.Lost]);

    private void Move(
        ConnectionProfileId profile,
        ConnectionLinkState state,
        DateTimeOffset at,
        int? attempt,
        string? errorCode,
        ConnectionLinkState[]? from)
    {
        if (profile.IsEmpty) return;
        ConnectionLinkChange change;
        lock (_gate)
        {
            var known = _states.TryGetValue(profile, out var current);
            if (from is not null && (!known || !from.Contains(current.State))) return;
            if (known && current.State == state) return;

            // Retrying after it was already lost is still lost: the retry is the library working
            // its way back, and "lost, retrying, lost" for one outage reads as three.
            if (known && current.State == ConnectionLinkState.Lost && state == ConnectionLinkState.Retrying) return;

            if (!known && _states.Count >= MaximumTracked)
            {
                var quietest = _states.MinBy(static pair => pair.Value.At).Key;
                _states.Remove(quietest);
            }

            _states[profile] = (state, at);
            change = new ConnectionLinkChange(profile, state, at.ToUniversalTime(), attempt, errorCode);
        }

        Changed?.Invoke(change);
    }
}

/// <summary>
/// Feeds a <see cref="ConnectionLinkTracker"/> from the storage library's events, for every
/// registration this application made.
/// </summary>
/// <remarks>
/// The library publishes on the CodeLogic event bus, fire-and-forget, from whatever thread raised
/// it; the handlers here only translate and hand on, so nothing they do can slow a transfer down.
/// Events for a registration somebody else made carry an id that names no profile, and are
/// ignored.
/// </remarks>
public sealed class CodeLogicConnectionLinkFeed : IDisposable
{
    private readonly IEventSubscription[] _subscriptions;

    public CodeLogicConnectionLinkFeed(IEventBus events, ConnectionLinkTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(tracker);
        _subscriptions =
        [
            events.Subscribe<StorageConnectionRetryEvent>(e =>
                On(e.ConnectionId, profile => tracker.Retrying(profile, e.Attempt, e.ErrorCode, e.Timestamp))),
            events.Subscribe<StorageConnectionLostEvent>(e =>
                On(e.ConnectionId, profile => tracker.Lost(profile, e.ErrorCode, e.Timestamp))),
            events.Subscribe<StorageOperationFailedEvent>(e =>
                On(e.ConnectionId, profile => tracker.OperationFailed(profile, e.ErrorCode, e.Timestamp))),
            events.Subscribe<StorageConnectionHealthChangedEvent>(e =>
                On(e.ConnectionId, profile => tracker.HealthChecked(profile, e.Healthy, e.ErrorCode, e.Timestamp))),
            events.Subscribe<StorageConnectionOpenedEvent>(e =>
                On(e.ConnectionId, profile => tracker.Reached(profile, e.Timestamp))),
            events.Subscribe<StorageItemWrittenEvent>(e =>
                On(e.ConnectionId, profile => tracker.Reached(profile, e.Timestamp))),
            events.Subscribe<StorageItemCopiedEvent>(e =>
                On(e.ConnectionId, profile => tracker.Reached(profile, e.Timestamp))),
            events.Subscribe<StorageItemMovedEvent>(e =>
                On(e.ConnectionId, profile => tracker.Reached(profile, e.Timestamp))),
            events.Subscribe<StorageItemDeletedEvent>(e =>
                On(e.ConnectionId, profile => tracker.Reached(profile, e.Timestamp)))
        ];
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions) subscription.Dispose();
    }

    private static void On(string connectionId, Action<ConnectionProfileId> apply)
    {
        if (CodeLogicStorageSessionFactory.ProfileOf(connectionId) is { } profile) apply(profile);
    }
}
