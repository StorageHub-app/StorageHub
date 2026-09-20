using System.Text;
using StorageHub.Agent.Host;
using StorageHub.Agent.Windows;
using StorageHub.Testing;

namespace StorageHub.Agent.Windows.Tests;

public sealed class SshTerminalOutputRingTests
{
    [WindowsOnlyFact]
    public void Appended_bytes_read_back_from_their_sequence()
    {
        var ring = new SshTerminalOutputRing();

        ring.Append("hello "u8);
        ring.Append("world"u8);

        var slice = ring.Read(0, 64);
        Assert.Equal(0, slice.StartSequence);
        Assert.Equal("hello world", Encoding.UTF8.GetString(slice.Content));
        Assert.False(slice.Truncated);
        Assert.Equal(11, ring.NextSequence);
    }

    [WindowsOnlyFact]
    public void A_read_that_never_arrived_replays_identically()
    {
        var ring = new SshTerminalOutputRing();
        ring.Append("first chunk"u8);

        var first = ring.Read(0, 64);

        // The desktop never acknowledged, because the response was cancelled, timed out or threw.
        // Asking again from the same place must hand back exactly the same bytes -- that is what
        // makes a lost read harmless by construction instead of silently losing output.
        var replay = ring.Read(0, 64);

        Assert.Equal(first.StartSequence, replay.StartSequence);
        Assert.Equal(first.Content, replay.Content);
    }

    [WindowsOnlyFact]
    public void Acknowledging_discards_only_what_was_consumed()
    {
        var ring = new SshTerminalOutputRing();
        ring.Append("aaaa"u8);
        ring.Append("bbbb"u8);

        ring.Acknowledge(4);

        Assert.Equal(4, ring.FirstSequence);
        Assert.Equal(4, ring.BufferedBytes);
        Assert.Equal("bbbb", Encoding.UTF8.GetString(ring.Read(4, 64).Content));
    }

    [WindowsOnlyFact]
    public void Acknowledging_part_of_a_chunk_still_reads_from_the_right_offset()
    {
        var ring = new SshTerminalOutputRing();
        ring.Append("abcdefgh"u8);

        // The UTF-8 decoder legitimately holds a partial sequence back, so an ack can land in the
        // middle of a chunk; the next read has to resume exactly there.
        ring.Acknowledge(3);

        Assert.Equal("defgh", Encoding.UTF8.GetString(ring.Read(3, 64).Content));
    }

    [WindowsOnlyFact]
    public void An_acknowledgement_behind_the_cursor_is_ignored()
    {
        var ring = new SshTerminalOutputRing();
        ring.Append("12345678"u8);
        ring.Acknowledge(8);

        ring.Acknowledge(2);

        Assert.Equal(8, ring.FirstSequence);
    }

    [WindowsOnlyFact]
    public void A_read_is_capped_at_the_requested_size_and_resumes_where_it_stopped()
    {
        var ring = new SshTerminalOutputRing();
        ring.Append("0123456789"u8);

        var first = ring.Read(0, 4);
        Assert.Equal("0123", Encoding.UTF8.GetString(first.Content));

        var second = ring.Read(4, 4);
        Assert.Equal(4, second.StartSequence);
        Assert.Equal("4567", Encoding.UTF8.GetString(second.Content));
    }

    [WindowsOnlyFact]
    public void Overflowing_the_capacity_reports_the_loss_rather_than_hiding_it()
    {
        var ring = new SshTerminalOutputRing(capacityBytes: 64 * 1024);
        var block = new byte[16 * 1024];
        Array.Fill(block, (byte)'x');
        for (var index = 0; index < 8; index++)
        {
            ring.Append(block);
        }

        Assert.True(ring.HasOverflowed);
        Assert.True(ring.FirstSequence > 0);

        // Asking from the start, which no longer exists, says so instead of quietly returning a
        // later stretch as though nothing had gone missing.
        var slice = ring.Read(0, 1024);
        Assert.True(slice.Truncated);
        Assert.Equal(ring.FirstSequence, slice.StartSequence);
    }

    [WindowsOnlyFact]
    public void Reading_at_the_live_end_returns_nothing_without_claiming_truncation()
    {
        var ring = new SshTerminalOutputRing();
        ring.Append("data"u8);
        ring.Acknowledge(4);

        var slice = ring.Read(4, 64);

        Assert.Empty(slice.Content);
        Assert.False(slice.Truncated);
    }

    [WindowsOnlyFact]
    public async Task Waiting_returns_at_once_when_data_is_already_there()
    {
        var ring = new SshTerminalOutputRing();
        ring.Append("ready"u8);

        await ring.WaitForDataAsync(0, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal("ready", Encoding.UTF8.GetString(ring.Read(0, 64).Content));
    }

    [WindowsOnlyFact]
    public async Task Waiting_wakes_as_soon_as_output_arrives()
    {
        var ring = new SshTerminalOutputRing();

        var waiter = ring.WaitForDataAsync(0, TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.False(waiter.IsCompleted);

        ring.Append("late"u8);
        await waiter;

        // This is what a long poll buys: output reaches the screen when it is produced rather than
        // up to one poll interval afterwards.
        Assert.Equal("late", Encoding.UTF8.GetString(ring.Read(0, 64).Content));
    }

    [WindowsOnlyFact]
    public async Task Waiting_gives_up_quietly_when_the_budget_expires()
    {
        var ring = new SshTerminalOutputRing();

        await ring.WaitForDataAsync(0, TimeSpan.FromMilliseconds(50), CancellationToken.None);

        // An empty read is an ordinary outcome, not a failure.
        Assert.Empty(ring.Read(0, 64).Content);
    }

    [WindowsOnlyFact]
    public async Task Waiting_honours_cancellation()
    {
        var ring = new SshTerminalOutputRing();
        using var cancellation = new CancellationTokenSource();
        var waiter = ring.WaitForDataAsync(0, TimeSpan.FromSeconds(30), cancellation.Token);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
    }
}
