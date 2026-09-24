namespace StorageHub.Transfers;

/// <summary>
/// Measures how fast a transfer is moving, over the last few seconds rather than since it began, so
/// the figure follows a connection that speeds up or slows down. A transfer that stops reporting
/// drops towards zero as time passes instead of showing its last speed forever.
/// </summary>
public sealed class TransferRateMeter
{
    /// <summary>How far back the speed is measured.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    /// <summary>Less than this and a speed would be mostly noise, so none is given.</summary>
    public static readonly TimeSpan MinimumSpan = TimeSpan.FromSeconds(1);

    // Reports can arrive for every buffer; keeping one sample per interval bounds the queue.
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(250);

    private readonly TimeProvider _timeProvider;
    private readonly Queue<(long Timestamp, long Bytes)> _samples = new();
    private readonly object _gate = new();
    private long _latestBytes;
    private long _lastSampleTimestamp;

    public TransferRateMeter(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        var now = _timeProvider.GetTimestamp();
        _samples.Enqueue((now, 0));
        _lastSampleTimestamp = now;
    }

    /// <summary>Records the total bytes moved so far. A total lower than one already seen is ignored.</summary>
    public void Record(long totalBytes)
    {
        lock (_gate)
        {
            if (totalBytes < _latestBytes)
            {
                return;
            }

            var now = _timeProvider.GetTimestamp();
            _latestBytes = totalBytes;
            if (_timeProvider.GetElapsedTime(_lastSampleTimestamp, now) >= SampleInterval)
            {
                _samples.Enqueue((now, totalBytes));
                _lastSampleTimestamp = now;
            }

            Trim(now);
        }
    }

    /// <summary>Bytes per second over the last <see cref="Window"/>, or null while too little time has passed.</summary>
    public long? BytesPerSecond()
    {
        lock (_gate)
        {
            var now = _timeProvider.GetTimestamp();
            Trim(now);
            var (oldestTimestamp, oldestBytes) = _samples.Peek();
            var span = _timeProvider.GetElapsedTime(oldestTimestamp, now);
            if (span < MinimumSpan)
            {
                return null;
            }

            return (long)((_latestBytes - oldestBytes) / span.TotalSeconds);
        }
    }

    // Keeps the newest sample that is at least a window old as the starting point, so the span
    // measured is always about one window long once the transfer has run that long.
    private void Trim(long now)
    {
        while (_samples.Count > 1)
        {
            var second = _samples.ElementAt(1);
            if (_timeProvider.GetElapsedTime(second.Timestamp, now) < Window)
            {
                break;
            }

            _samples.Dequeue();
        }
    }
}
