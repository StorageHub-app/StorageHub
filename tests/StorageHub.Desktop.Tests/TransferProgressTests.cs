using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The queue's progress column: a bar behind the percentage, and none when the size is unknown.
/// </summary>
public sealed class TransferProgressTests
{
    [AvaloniaFact]
    public void AKnownSizeHasABarAndAPercentage()
    {
        var row = TransferQueueModel.ToRow(Transfer(expected: 200, done: 50));

        Assert.True(row.HasProgressBar);
        Assert.Equal(25, row.ProgressPercent, precision: 6);
        Assert.Contains("25", row.Progress, StringComparison.Ordinal);
        Assert.False(row.IsComplete);
    }

    /// <summary>
    /// An unknown size shows bytes moved and no bar. A bar stuck at nothing for a provider that
    /// does not report a length was 1.x's one standing complaint about this column.
    /// </summary>
    [AvaloniaFact]
    public void AnUnknownSizeHasNoBar()
    {
        var row = TransferQueueModel.ToRow(Transfer(expected: null, done: 4096));

        Assert.False(row.HasProgressBar);
        Assert.Null(row.ProgressFraction);
        Assert.NotEmpty(row.Progress);
    }

    /// <summary>A running transfer of known size shows its speed and the time left at that speed.</summary>
    [AvaloniaFact]
    public void ARunningTransferShowsItsSpeedAndTimeLeft()
    {
        var row = TransferQueueModel.ToRow(Transfer(expected: 4 * 1024 * 1024, done: 1024 * 1024, rate: 1024 * 1024));

        Assert.Equal("25% · 1 MiB/s · 0:03 left", row.Progress);
    }

    /// <summary>An unknown size still shows the speed, but there is nothing to count down to.</summary>
    [AvaloniaFact]
    public void AnUnknownSizeShowsSpeedButNoTimeLeft()
    {
        var row = TransferQueueModel.ToRow(Transfer(expected: null, done: 4096, rate: 2048));

        Assert.Equal("4 KiB · 2 KiB/s", row.Progress);
    }

    /// <summary>A stalled transfer says so, rather than promising a time from a speed it no longer has.</summary>
    [AvaloniaFact]
    public void AStalledTransferShowsNoTimeLeft()
    {
        var row = TransferQueueModel.ToRow(Transfer(expected: 200, done: 50, rate: 0));

        Assert.Equal("25% · 0 B/s", row.Progress);
    }

    /// <summary>More bytes than expected is 100% in the text and a full bar, never past the end.</summary>
    [AvaloniaFact]
    public void OvershootIsAFullBar()
    {
        var row = TransferQueueModel.ToRow(Transfer(expected: 100, done: 140));

        Assert.Equal(1, row.ProgressFraction);
        Assert.True(row.IsComplete);
        Assert.Contains("100", row.Progress, StringComparison.Ordinal);
    }

    /// <summary>
    /// Photographs the Active tab with bars at several stages, for a human to look at.
    /// </summary>
    /// <remarks>
    /// The text has to stay readable over the fill in both appearances; that is the thing no
    /// assertion here can check. Set STORAGEHUB_SHOT_DIR to keep the files.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheColumnCanBePhotographed(bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));

        await using var queue = new TransferQueueModel(() => throw new InvalidOperationException("Not polled."));
        foreach (var (expected, done, rate) in new (long?, long, long?)[]
                 {
                     (1_200_000_000, 144_000_000, 11_800_000),
                     (1000, 640, null),
                     (1000, 1000, null),
                     (null, 3_400_000, 350_000),
                 })
        {
            queue.Rows.Add(TransferQueueModel.ToRow(Transfer(expected, done, rate)));
        }

        var window = new Window { Content = new TransferQueueView { DataContext = queue } };
        window.Show();
        window.Measure(new Size(1000, 300));
        window.Arrange(new Rect(0, 0, 1000, 300));
        window.UpdateLayout();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, $"transfer-progress-{(dark ? "dark" : "light")}.png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }

    private static TransferQueueSummary Transfer(long? expected, long done, long? rate = null) => new(
        Guid.NewGuid(),
        TransferQueueOperation.Copy,
        Guid.NewGuid(),
        "/photos/2026/berlin/IMG_0412.jpg",
        Guid.NewGuid(),
        "/backup/photos/2026/berlin/IMG_0412.jpg",
        done >= (expected ?? long.MaxValue) ? TransferQueueState.Completed : TransferQueueState.Transferring,
        Revision: 1,
        Attempt: 1,
        Priority: 0,
        ExpectedBytes: expected,
        ProgressBytes: done,
        UpdatedUtc: DateTimeOffset.UnixEpoch,
        RetryAvailableUtc: null,
        ErrorCode: null,
        ErrorSummary: null,
        CanCancel: true,
        CanRetry: false,
        NeedsReconciliation: false,
        BytesPerSecond: rate);
}
