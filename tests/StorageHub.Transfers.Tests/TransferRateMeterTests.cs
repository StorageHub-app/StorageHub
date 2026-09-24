namespace StorageHub.Transfers.Tests;

public sealed class TransferRateMeterTests
{
    [Fact]
    public void No_speed_is_given_until_a_second_has_passed()
    {
        var clock = new SteppingTimeProvider();
        var meter = new TransferRateMeter(clock);

        clock.Advance(TimeSpan.FromMilliseconds(500));
        meter.Record(1_000_000);

        Assert.Null(meter.BytesPerSecond());
    }

    [Fact]
    public void A_steady_transfer_is_measured()
    {
        var clock = new SteppingTimeProvider();
        var meter = new TransferRateMeter(clock);

        for (var second = 1; second <= 3; second++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            meter.Record(second * 100_000L);
        }

        Assert.Equal(100_000, meter.BytesPerSecond());
    }

    [Fact]
    public void The_recent_speed_is_given_not_the_average_since_the_start()
    {
        var clock = new SteppingTimeProvider();
        var meter = new TransferRateMeter(clock);
        long total = 0;

        // Twenty seconds at 1 MB/s, then ten at 100 KB/s: the window only sees the slow part.
        for (var second = 0; second < 20; second++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            meter.Record(total += 1_000_000);
        }

        for (var second = 0; second < 10; second++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            meter.Record(total += 100_000);
        }

        Assert.Equal(100_000, meter.BytesPerSecond());
    }

    [Fact]
    public void A_stalled_transfer_falls_to_zero()
    {
        var clock = new SteppingTimeProvider();
        var meter = new TransferRateMeter(clock);
        for (var second = 1; second <= 5; second++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            meter.Record(second * 500_000L);
        }

        clock.Advance(TimeSpan.FromSeconds(2));
        var slowing = meter.BytesPerSecond();
        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.InRange(slowing!.Value, 1, 499_999);
        Assert.Equal(0, meter.BytesPerSecond());
    }

    [Fact]
    public void A_total_lower_than_one_already_seen_is_ignored()
    {
        var clock = new SteppingTimeProvider();
        var meter = new TransferRateMeter(clock);
        clock.Advance(TimeSpan.FromSeconds(2));
        meter.Record(200_000);
        meter.Record(50);

        Assert.Equal(100_000, meter.BytesPerSecond());
    }

    private sealed class SteppingTimeProvider : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}
