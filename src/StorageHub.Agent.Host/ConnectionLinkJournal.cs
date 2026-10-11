using StorageHub.Domain.Identifiers;
using StorageHub.Storage.CodeLogic;

namespace StorageHub.Agent.Host;

/// <summary>
/// The agent's memory of how its connections' links have changed since it started: the most
/// recent changes for the activity log, and each connection's latest for its card.
/// </summary>
/// <remarks>
/// <para>
/// In memory and bounded on purpose. A link that dropped an hour ago and came back is worth a
/// line in the log, not a row in the database; an agent restart starts the journal again, which
/// the sequence numbers make plain, and a connection with nothing recorded is simply one that has
/// had no trouble since.
/// </para>
/// <para>
/// Fed by a <see cref="ConnectionLinkTracker"/>, which has already collapsed the library's events
/// to real changes, so the journal never fills with a retry storm: an outage is two entries.
/// </para>
/// </remarks>
public sealed class ConnectionLinkJournal
{
    /// <summary>How many changes are kept: a few hundred outages, far more than the log shows.</summary>
    internal const int Capacity = 256;

    private readonly object _gate = new();
    private readonly LinkedList<Entry> _recent = new();
    private readonly Dictionary<ConnectionProfileId, Entry> _latest = [];
    private readonly ConnectionLinkTracker _tracker;
    private readonly TimeProvider _time;
    private long _sequence;

    public ConnectionLinkJournal(ConnectionLinkTracker tracker, TimeProvider? timeProvider = null)
    {
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _time = timeProvider ?? TimeProvider.System;
        _tracker.Changed += Append;
    }

    /// <summary>One change, with its place in the journal.</summary>
    public sealed record Entry(long Sequence, ConnectionLinkChange Change);

    /// <summary>
    /// Tells the tracker the connection answered just now, for an operation the agent saw succeed
    /// itself: a listing, a test, a free-space read. For an object store, which opens no sessions,
    /// this is the only way it comes back.
    /// </summary>
    public void Reached(ConnectionProfileId profile) => _tracker.Reached(profile, _time.GetUtcNow());

    /// <summary>The newest changes, newest first, and each connection's latest, newest first.</summary>
    public (IReadOnlyList<Entry> Recent, IReadOnlyList<Entry> Latest) Read(int recent, int latest)
    {
        lock (_gate)
        {
            return (
                [.. _recent.Reverse().Take(Math.Max(0, recent))],
                [.. _latest.Values.OrderByDescending(static entry => entry.Sequence).Take(Math.Max(0, latest))]);
        }
    }

    private void Append(ConnectionLinkChange change)
    {
        lock (_gate)
        {
            var entry = new Entry(++_sequence, change);
            _recent.AddLast(entry);
            while (_recent.Count > Capacity) _recent.RemoveFirst();

            _latest[change.ProfileId] = entry;
            if (_latest.Count > Capacity)
            {
                var oldest = _latest.MinBy(static pair => pair.Value.Sequence).Key;
                _latest.Remove(oldest);
            }
        }
    }
}
