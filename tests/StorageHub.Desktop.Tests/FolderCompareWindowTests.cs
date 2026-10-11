using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using StorageHub.Contracts.Ipc;
using StorageHub.Contracts.Results;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Views;
using Xunit;
using static StorageHub.Desktop.Tests.WorkspaceFakes;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Compare Panes: the active pane and its neighbour compared, shown filtered, and the chosen
/// differences queued as ordinary copies.
/// </summary>
public sealed class FolderCompareWindowTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [AvaloniaFact]
    public async Task TheTwoPanesAreComparedAndADifferenceIsQueued()
    {
        var queued = 0;
        var fixture = await WorkspaceAsync(
            [
                new("report.pdf", FolderCompareKind.OnlyLeft, FolderCompareReasons.None, File(10, "etag-r"), null),
                new("notes.txt", FolderCompareKind.Same, FolderCompareReasons.None, File(1, "a"), File(1, "b"))
            ],
            () => { queued++; return Task.CompletedTask; });
        await using var _ = fixture.Workspace;

        var compare = fixture.Workspace.CreateFolderCompare(out var refusal);
        Assert.Null(refusal);
        Assert.NotNull(compare);
        Assert.StartsWith("Studio Assets", compare.LeftLabel, StringComparison.Ordinal);
        Assert.StartsWith("Site Backups", compare.RightLabel, StringComparison.Ordinal);

        await compare.CompareAsync();

        // Same is hidden until asked for: the differences are what the window is for.
        var row = Assert.Single(compare.Rows);
        Assert.Equal("report.pdf", row.Path);
        Assert.Equal(Ui.Format(Ui.Sync.CompareSameFormat, 1), compare.SameLabel);
        Assert.False(compare.CopyToRightCommand.CanExecute(null));

        compare.SelectedRows.Add(row);
        Assert.True(compare.CopyToRightCommand.CanExecute(null));
        Assert.False(compare.CopyToLeftCommand.CanExecute(null));
        await compare.CopyAsync(FolderCompareDirection.LeftToRight);

        var request = Assert.Single(fixture.Queue.Enqueued);
        Assert.Equal(TransferQueueOperation.Copy, request.Operation);
        Assert.Equal("report.pdf", request.Destination.RelativePath);
        Assert.Equal(1, queued);
        Assert.Equal(Ui.Format(Ui.Sync.CompareCopiedFormat, 1, 0), compare.Status.Text);
        compare.Dispose();
    }

    [AvaloniaFact]
    public async Task APaneWithNoFolderIsRefused()
    {
        var fixture = await WorkspaceAsync([], openRight: false);
        await using var _ = fixture.Workspace;

        Assert.Null(fixture.Workspace.CreateFolderCompare(out var refusal));
        Assert.Equal(Ui.Validation.FolderComparisonNeedsAFolder, refusal);
    }

    [AvaloniaFact]
    public async Task TheFiltersNarrowTheList()
    {
        var model = Model(Sample());
        await model.CompareAsync();
        Assert.Equal(5, model.Rows.Count);

        model.ShowSame = true;
        Assert.Equal(6, model.Rows.Count);

        model.ShowOnlyRight = false;
        model.NameFilter = "*.psd";
        Assert.Equal(["assets/hero.psd"], model.Rows.Select(static row => row.Path));
        Assert.Equal(Ui.Format(Ui.Sync.CompareStatusDifferentFormat, Ui.Sync.CompareReasonSize + ", " + Ui.Sync.CompareReasonLeftNewer), model.Rows[0].Status);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheWindowCanBePhotographed(bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));

        var model = Model(Sample(), caseInsensitive: true);
        await model.CompareAsync();
        model.SelectedRows.Add(model.Rows[1]);
        var window = new FolderCompareWindow { DataContext = model };
        window.Show();
        window.Measure(new Size(1040, 620));
        window.Arrange(new Rect(0, 0, 1040, 620));
        window.UpdateLayout();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        window.Close();
        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var stream = System.IO.File.Create(Path.Combine(directory, $"folder-compare-{(dark ? "dark" : "light")}.png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }

    private static FolderCompareEntry[] Sample() =>
    [
        new("assets", FolderCompareKind.Same, FolderCompareReasons.None, Folder(), Folder()),
        new("assets/hero.psd", FolderCompareKind.Different, FolderCompareReasons.Size | FolderCompareReasons.LeftNewer,
            File(48_000_000, "a", Noon.AddHours(2)), File(46_500_000, "b")),
        new("assets/logo.svg", FolderCompareKind.OnlyLeft, FolderCompareReasons.None, File(12_400, "c"), null),
        new("drafts", FolderCompareKind.OnlyRight, FolderCompareReasons.None, null, Folder()),
        new("drafts/old-brief.docx", FolderCompareKind.OnlyRight, FolderCompareReasons.None, null, File(88_000, "d")),
        new("index.html", FolderCompareKind.Different, FolderCompareReasons.Checksum, File(5_120, "e"), File(5_120, "f"))
    ];

    private static FolderCompareModel Model(FolderCompareEntry[] entries, bool caseInsensitive = false)
    {
        var left = PaneTransferContext.Create(PaneTransferContextKind.SavedConnection, Guid.NewGuid(), "root", "site").Value;
        var right = PaneTransferContext.Create(PaneTransferContextKind.SavedConnection, Guid.NewGuid(), "root", "").Value;
        return new FolderCompareModel(
            "Studio Assets — /site",
            "Site Backups — /",
            (_, _) => Task.FromResult(StorageResult<FolderCompareOutcome>.Success(
                new FolderCompareOutcome(left, right, entries, caseInsensitive, uncheckedPairs: 0))),
            (_, _, _, _) => throw new NotSupportedException());
    }

    private static FolderCompareItem File(long size, string entityTag, DateTimeOffset? modified = null) =>
        new(StorageItemKind.File, size, modified ?? Noon, EntityTag: entityTag);

    private static FolderCompareItem Folder() => new(StorageItemKind.Directory, null, Noon);

    private sealed record Fixture(WorkspaceModel Workspace, FakeTransferQueue Queue);

    private static async Task<Fixture> WorkspaceAsync(
        FolderCompareEntry[] entries,
        Func<Task>? onQueueChanged = null,
        bool openRight = true)
    {
        var source = Summary("Studio Assets");
        var destination = Summary("Site Backups");
        var agent = new FakeBrowsingAgent([source, destination]);
        var queue = new FakeTransferQueue();
        var workspace = new WorkspaceModel(
            () => new BrowserPaneModel(agent),
            () => queue,
            () => agent,
            () => new FakeInspector(),
            onQueueChanged)
        {
            FolderCompareClient = () => new FakeCompareClient(entries)
        };

        foreach (var pane in workspace.Panes)
        {
            await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
        }

        await workspace.Panes[0].OpenConnectionAsync(source.ConnectionId, TestContext.Current.CancellationToken);
        if (openRight)
        {
            await workspace.Panes[1].OpenConnectionAsync(destination.ConnectionId, TestContext.Current.CancellationToken);
        }

        workspace.Panes[0].IsActive = true;
        return new Fixture(workspace, queue);
    }

    /// <summary>An agent that has already finished comparing, and answers in one page.</summary>
    private sealed class FakeCompareClient(FolderCompareEntry[] entries) : IFolderCompareAgentClient
    {
        private static readonly Guid Id = Guid.NewGuid();

        public Task<FolderCompareStartResponse> StartAsync(
            FolderCompareStartRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FolderCompareStartResponse(request.ContractVersion, Id));

        public Task<FolderCompareStatusResponse> GetStatusAsync(
            FolderCompareStatusRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FolderCompareStatusResponse(
                request.ContractVersion, Id, FolderCompareState.Completed, entries.Length, [.. entries.Skip(request.Offset)]));

        public Task<FolderCompareReleaseResponse> ReleaseAsync(
            FolderCompareReleaseRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FolderCompareReleaseResponse(request.ContractVersion, Id, true));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
