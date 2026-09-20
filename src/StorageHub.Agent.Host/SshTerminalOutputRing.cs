namespace StorageHub.Agent.Host;

/// <summary>
/// What a read produced: the bytes, and the absolute sequence the first of them sat at.
/// </summary>
internal readonly record struct SshTerminalOutputSlice(long StartSequence, byte[] Content, bool Truncated);

/// <summary>
/// A bounded buffer of a session's output, addressed by an absolute sequence number.
///
/// Bytes stay here until the desktop acknowledges having fed them to its emulator, which is what
/// makes a dropped, cancelled or timed-out read harmless: the next read simply asks from the same
/// place and gets the same bytes again. Previously output was handed over destructively, so any
/// failure between the agent reading from the shell and the desktop parsing it lost that output
/// with no way to notice, let alone recover.
/// </summary>
internal sealed class SshTerminalOutputRing(int capacityBytes = SshTerminalOutputRing.DefaultCapacityBytes)
{
    internal const int DefaultCapacityBytes = 1024 * 1024;

    private readonly Lock _gate = new();
    private readonly int _capacity = Math.Clamp(capacityBytes, 64 * 1024, 8 * 1024 * 1024);
    private readonly Queue<byte[]> _chunks = new();
    private TaskCompletionSource _dataArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _firstSequence;
    private long _nextSequence;
    private int _bufferedBytes;
    private bool _overflowed;

    /// <summary>The sequence the next byte appended will occupy.</summary>
    internal long NextSequence
    {
        get
        {
            lock (_gate)
            {
                return _nextSequence;
            }
        }
    }

    /// <summary>The oldest sequence still retained.</summary>
    internal long FirstSequence
    {
        get
        {
            lock (_gate)
            {
                return _firstSequence;
            }
        }
    }

    internal int BufferedBytes
    {
        get
        {
            lock (_gate)
            {
                return _bufferedBytes;
            }
        }
    }

    internal void Append(ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
        {
            return;
        }

        TaskCompletionSource signal;
        lock (_gate)
        {
            _chunks.Enqueue(content.ToArray());
            _bufferedBytes += content.Length;
            _nextSequence += content.Length;
            TrimToCapacity();
            signal = _dataArrived;
            _dataArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        signal.TrySetResult();
    }

    /// <summary>
    /// Discards everything at or below <paramref name="sequence"/>. The desktop sends this only
    /// after the bytes have actually reached its emulator, so nothing is dropped in flight.
    /// </summary>
    internal void Acknowledge(long sequence)
    {
        lock (_gate)
        {
            if (sequence <= _firstSequence)
            {
                return;
            }

            DropThrough(Math.Min(sequence, _nextSequence));
        }
    }

    /// <summary>
    /// The buffered bytes from <paramref name="fromSequence"/> onwards, up to
    /// <paramref name="maximumBytes"/>. <c>Truncated</c> says the requested start had already been
    /// dropped to stay within capacity, so output really was lost and the user should be told
    /// rather than left with a silent gap.
    /// </summary>
    internal SshTerminalOutputSlice Read(long fromSequence, int maximumBytes)
    {
        lock (_gate)
        {
            var start = Math.Max(fromSequence, _firstSequence);
            var truncated = fromSequence < _firstSequence;
            if (start >= _nextSequence)
            {
                return new SshTerminalOutputSlice(start, [], truncated);
            }

            var take = (int)Math.Min(maximumBytes, _nextSequence - start);
            var buffer = new byte[take];
            var written = 0;
            var position = _firstSequence;
            foreach (var chunk in _chunks)
            {
                var chunkEnd = position + chunk.Length;
                if (chunkEnd > start && written < take)
                {
                    var offset = (int)Math.Max(0, start - position);
                    var length = Math.Min(chunk.Length - offset, take - written);
                    chunk.AsSpan(offset, length).CopyTo(buffer.AsSpan(written));
                    written += length;
                }

                position = chunkEnd;
                if (written >= take)
                {
                    break;
                }
            }

            return new SshTerminalOutputSlice(start, written == buffer.Length ? buffer : buffer[..written], truncated);
        }
    }

    /// <summary>
    /// Completes as soon as there is anything at or after <paramref name="fromSequence"/>, or when
    /// the wait budget runs out. This is what lets a read block briefly instead of the desktop
    /// polling on a timer: output reaches the screen as it arrives rather than up to one interval
    /// later.
    /// </summary>
    internal async Task WaitForDataAsync(long fromSequence, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Task pending;
        lock (_gate)
        {
            if (_nextSequence > Math.Max(fromSequence, _firstSequence))
            {
                return;
            }

            pending = _dataArrived.Task;
        }

        if (timeout <= TimeSpan.Zero)
        {
            return;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            await pending.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The wait budget expired with nothing to report, which is an ordinary empty read.
        }
    }

    /// <summary>True once capacity has forced older, unacknowledged bytes to be dropped.</summary>
    internal bool HasOverflowed
    {
        get
        {
            lock (_gate)
            {
                return _overflowed;
            }
        }
    }

    private void TrimToCapacity()
    {
        while (_bufferedBytes > _capacity && _chunks.Count > 0)
        {
            var dropped = _chunks.Dequeue();
            _bufferedBytes -= dropped.Length;
            _firstSequence += dropped.Length;
            _overflowed = true;
        }
    }

    private void DropThrough(long sequence)
    {
        while (_chunks.Count > 0)
        {
            var head = _chunks.Peek();
            if (_firstSequence + head.Length > sequence)
            {
                break;
            }

            _ = _chunks.Dequeue();
            _bufferedBytes -= head.Length;
            _firstSequence += head.Length;
        }

        // A partially acknowledged chunk is kept whole; the read simply starts part-way into it.
        _firstSequence = Math.Min(_firstSequence, sequence);
    }
}
