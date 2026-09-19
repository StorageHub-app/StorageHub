using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// How far a dragged-out selection has got before it becomes durable agent work.
/// </summary>
public enum PendingDropState
{
    /// <summary>The drag has started; Explorer has not yet reported where it was dropped.</summary>
    AwaitingDestination = 1,

    /// <summary>The destination is known and the agent has accepted the export.</summary>
    Queued = 2,

    /// <summary>The gesture ended without a usable drop.</summary>
    Cancelled = 3,

    /// <summary>The drop could not be turned into agent work.</summary>
    Failed = 4,

    /// <summary>
    /// The destination is known and the folders that were dropped are being read. Files reach the
    /// queue as they are found, so this entry stands beside the rows it is producing.
    /// </summary>
    Gathering = 5
}

public sealed record PendingDropEntry(
    string Token,
    string Source,
    int ItemCount,
    PendingDropState State,
    string? Destination,
    string? Detail,
    DateTimeOffset StartedUtc,
    DateTimeOffset UpdatedUtc)
{
    /// <summary>Files queued so far, while <see cref="State"/> is <see cref="PendingDropState.Gathering"/>.</summary>
    public int FilesFound { get; init; }

    /// <summary>Folders created so far, while <see cref="State"/> is <see cref="PendingDropState.Gathering"/>.</summary>
    public int FoldersFound { get; init; }

    public bool IsTerminal => State is PendingDropState.Cancelled or PendingDropState.Queued
        or PendingDropState.Failed;

    public string Describe() => State switch
    {
        PendingDropState.AwaitingDestination => Ui.Transfer.DropWaitingForDestination,
        PendingDropState.Gathering => FilesFound == 0 && FoldersFound == 0
            ? Ui.Transfer.DropGathering
            : Ui.Format(Ui.Transfer.DropGatheringFormat, FilesFound, FoldersFound),
        PendingDropState.Queued => Ui.Transfer.DropQueued,
        PendingDropState.Cancelled => Detail is null
            ? Ui.Transfer.DropCancelled
            : Ui.Format(Ui.Transfer.DropCancelledFormat, Detail),
        _ => Detail is null
            ? Ui.Transfer.DropFailed
            : Ui.Format(Ui.Transfer.DropFailedFormat, Detail)
    };

    public string DescribeSource() => ItemCount == 1
        ? Source
        : Ui.Format(Ui.Transfer.DropItemsFromFormat, ItemCount, Source);
}

/// <summary>
/// Tracks drags out to File Explorer between the moment the gesture starts and the moment the agent
/// turns them into durable transfers.
///
/// These entries are deliberately desktop-local and never durable: until Explorer reports a
/// destination there is no transfer intent to record, which is why the agent cannot create a real
/// job yet. Surfacing them anyway closes the window where a drag looked like it had done nothing,
/// but they are always labelled as pending so they cannot be mistaken for committed work.
/// </summary>
public sealed class PendingDropRegistry
{
    /// <summary>Terminal entries linger briefly so an outcome can be read, then clear themselves.</summary>
    public static readonly TimeSpan TerminalLifetime = TimeSpan.FromMinutes(1);

    private const int MaximumEntries = 32;

    private readonly object _gate = new();
    private readonly Dictionary<string, PendingDropEntry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public PendingDropRegistry(TimeProvider? timeProvider = null) =>
        _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>Raised whenever an entry is added or changes state, so views can refresh promptly.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised when somebody cancels a drop that is still being read. The registry records gestures
    /// and owns no work, so whoever is doing the reading listens for this and stops.
    /// </summary>
    public event EventHandler<string>? CancelRequested;

    /// <summary>Whether the named drop is still reading folders, and so can be stopped.</summary>
    public bool IsGathering(string token)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(token, out var entry) && entry.State == PendingDropState.Gathering;
        }
    }

    /// <summary>Asks whoever is reading this drop's folders to stop.</summary>
    public void RequestCancel(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (IsGathering(token))
        {
            CancelRequested?.Invoke(this, token);
        }
    }

    public void Begin(string token, string source, int itemCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            PruneExpired(now);
            if (_entries.Count >= MaximumEntries && !_entries.ContainsKey(token))
            {
                // Drop the oldest terminal entry rather than refuse to record a live gesture.
                var stale = _entries.Values
                    .Where(static entry => entry.IsTerminal)
                    .OrderBy(static entry => entry.UpdatedUtc)
                    .FirstOrDefault();
                if (stale is not null)
                {
                    _entries.Remove(stale.Token);
                }
                else
                {
                    return;
                }
            }

            _entries[token] = new PendingDropEntry(
                token,
                string.IsNullOrWhiteSpace(source) ? Ui.Transfer.DropSelection : source,
                itemCount < 1 ? 1 : itemCount,
                PendingDropState.AwaitingDestination,
                Destination: null,
                Detail: null,
                now,
                now);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Records that the drop's folders are being read, and how far that has got. Called on every
    /// page, so it leaves the entry alone when nothing has changed rather than making the views
    /// redraw an identical row.
    /// </summary>
    public void MarkGathering(string token, string? destination, int filesFound, int foldersFound)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            if (!_entries.TryGetValue(token, out var entry))
            {
                return;
            }

            if (entry.State == PendingDropState.Gathering &&
                entry.FilesFound == filesFound &&
                entry.FoldersFound == foldersFound)
            {
                return;
            }

            _entries[token] = entry with
            {
                State = PendingDropState.Gathering,
                Destination = destination ?? entry.Destination,
                Detail = null,
                FilesFound = filesFound,
                FoldersFound = foldersFound,
                UpdatedUtc = now
            };
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Forgets a marker whose gesture went somewhere else. A drag out to Explorer that lands on a
    /// StorageHub pane instead was never Explorer's work, and the in-app drop keeps a row of its
    /// own, so leaving a second row behind -- reading as cancelled, for a drop that succeeded --
    /// says something untrue about a gesture that worked.
    /// </summary>
    public void Discard(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        lock (_gate)
        {
            if (!_entries.Remove(token))
            {
                return;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MarkQueued(string token, string? destination) =>
        Transition(token, PendingDropState.Queued, destination, detail: null);

    public void MarkCancelled(string token, string? reason) =>
        Transition(token, PendingDropState.Cancelled, destination: null, reason);

    public void MarkFailed(string token, string? reason) =>
        Transition(token, PendingDropState.Failed, destination: null, reason);

    /// <summary>
    /// Returns live entries newest first, pruning any terminal entry whose display window has
    /// passed. Reading is what expires them, so no timer is needed.
    /// </summary>
    public IReadOnlyList<PendingDropEntry> Snapshot()
    {
        var now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            PruneExpired(now);
            return [.. _entries.Values.OrderByDescending(static entry => entry.StartedUtc)];
        }
    }

    private void Transition(string token, PendingDropState state, string? destination, string? detail)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            if (!_entries.TryGetValue(token, out var existing))
            {
                return;
            }

            // A gesture only settles once. Reporting cancellation after a successful queue would
            // otherwise overwrite the outcome the user needs to see.
            if (existing.IsTerminal)
            {
                return;
            }

            _entries[token] = existing with
            {
                State = state,
                Destination = destination ?? existing.Destination,
                Detail = detail,
                UpdatedUtc = now
            };
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void PruneExpired(DateTimeOffset now)
    {
        List<string>? expired = null;
        foreach (var entry in _entries.Values)
        {
            if (entry.IsTerminal && now - entry.UpdatedUtc >= TerminalLifetime)
            {
                (expired ??= []).Add(entry.Token);
            }
        }

        if (expired is null)
        {
            return;
        }

        foreach (var token in expired)
        {
            _entries.Remove(token);
        }
    }
}
