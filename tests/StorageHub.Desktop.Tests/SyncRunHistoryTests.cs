using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The run history and review screen showing what the agent actually said.
/// </summary>
/// <remarks>
/// It used to be a record of constants: the run id was <c>Guid.Empty</c>, both tables were empty
/// and the conflicts tab was a blank border. Nothing could fail because nothing happened. What is
/// checked here is the screen's own behaviour -- the confirmation in front of Approve, the pace it
/// re-reads a live run at, and the fact that a poll does not reshuffle a table underneath somebody.
/// </remarks>
public class SyncRunHistoryTests
{
    private const string PlanDigest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string ApprovalDigest = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";

    [Fact]
    public async Task HistoryRowsCarryTheirRunAndItsPhase()
    {
        var run = Run(SyncIpcRunPhase.Completed) with
        {
            DispatchState = SyncIpcDispatchState.DurablyDispatched,
            ConflictCount = 3
        };
        var agent = new StubReviewAgent();
        agent.HistoryPage([run], null);
        using var model = SyncRunHistoryModel.Create(() => agent);

        await model.RefreshHistoryAsync(null, TestContext.Current.CancellationToken);

        var row = Assert.Single(model.Runs);
        Assert.Equal(run.SyncRunId, row.SyncRunId);
        Assert.Equal(Ui.Sync.RunPhaseCompleted, row.Phase);
        Assert.Equal(Ui.Sync.DispatchDurablyDispatched, row.Dispatch);
        Assert.Equal("3", row.Conflicts);
    }

    /// <summary>
    /// A dispatch state is words, not a C# identifier.
    /// </summary>
    /// <remarks>
    /// The WinForms grid took the enum as it came, which is how a Danish shell showed
    /// "DurablyDispatched" in a column headed "Afsendelse". Same reason the phases have words.
    /// </remarks>
    [Fact]
    public void EveryEnumOnThisScreenHasWords()
    {
        Assert.Equal(Ui.Sync.DispatchNotDispatched,
            UiEnumNames.Describe(SyncIpcDispatchState.NotDispatched));
        Assert.Equal(Ui.Sync.PlanOperationCreateDirectory,
            UiEnumNames.Describe(SyncIpcPlanOperationKind.CreateDirectory));
        Assert.Equal(Ui.Sync.ConflictStateUnresolved,
            UiEnumNames.Describe(SyncIpcConflictState.Unresolved));
    }

    /// <summary>Selecting a row offers it for loading; it does not load it.</summary>
    /// <remarks>
    /// Reading down a list of runs to find the one you want should not fetch a plan per row, and a
    /// plan is three round trips to the agent.
    /// </remarks>
    [Fact]
    public async Task SelectingARowFillsTheRunIdWithoutLoadingIt()
    {
        var run = Run(SyncIpcRunPhase.AwaitingApproval);
        var agent = new StubReviewAgent();
        agent.HistoryPage([run], null);
        using var model = SyncRunHistoryModel.Create(() => agent);
        await model.RefreshHistoryAsync(null, TestContext.Current.CancellationToken);

        model.SelectedRun = model.Runs[0];

        Assert.Equal(run.SyncRunId.ToString("D"), model.RunId);
        Assert.Equal(0, agent.StatusRequests);
        Assert.Empty(model.Plan);
    }

    /// <summary>A run id that is not one is refused before anything is asked.</summary>
    [Fact]
    public async Task TypingSomethingThatIsNotARunIdIsRefused()
    {
        var agent = new StubReviewAgent();
        using var model = SyncRunHistoryModel.Create(() => agent);
        model.RunId = "not a run";

        await model.LoadRunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Ui.Sync.EnterValidRunId, model.HistoryStatus.Text);
        Assert.Equal(0, agent.StatusRequests);
    }

    [Fact]
    public async Task ALoadedRunFillsBothTables()
    {
        var run = Run(SyncIpcRunPhase.AwaitingApproval);
        var agent = new StubReviewAgent { Status = run };
        agent.PlanPage(run, [Operation(1, destructive: true)], null);
        agent.ConflictPage([Conflict("photos/a.jpg")], null);
        using var model = SyncRunHistoryModel.Create(() => agent);

        await model.LoadRunAsync(run.SyncRunId, TestContext.Current.CancellationToken);

        var operation = Assert.Single(model.Plan);
        Assert.Equal(Ui.Sync.PlanOperationCopy, operation.Action);
        Assert.Equal(Ui.Sync.DestructiveApprovalRequired, operation.Approval);

        var conflict = Assert.Single(model.Conflicts);
        Assert.Equal("photos/a.jpg", conflict.Path);
        Assert.Equal(Ui.Sync.ConflictStateUnresolved, conflict.State);
    }

    /// <summary>
    /// Waiting for approval reads as waiting, and says no changes have started.
    /// </summary>
    /// <remarks>
    /// The phase and the dispatch state together decide the sentence. Reporting a durably
    /// dispatched run as finished is the specific mistake this screen is built to avoid, because
    /// the agent makes the request durable and then executes it.
    /// </remarks>
    [Theory]
    [InlineData(SyncIpcRunPhase.AwaitingApproval, SyncIpcDispatchState.NotDispatched, false)]
    [InlineData(SyncIpcRunPhase.Executing, SyncIpcDispatchState.DurablyDispatched, false)]
    [InlineData(SyncIpcRunPhase.Completed, SyncIpcDispatchState.DurablyDispatched, true)]
    public async Task OnlyAFinishedRunReadsAsFinished(
        SyncIpcRunPhase phase,
        SyncIpcDispatchState dispatch,
        bool expectedCompleted)
    {
        var run = Run(phase) with { DispatchState = dispatch };
        var agent = new StubReviewAgent { Status = run };
        agent.PlanPage(run, [], null);
        agent.ConflictPage([], null);
        using var model = SyncRunHistoryModel.Create(() => agent);

        await model.LoadRunAsync(run.SyncRunId, TestContext.Current.CancellationToken);

        Assert.Equal(expectedCompleted, model.PlanStatus.Text == Ui.Sync.PhaseCompleted);
    }

    /// <summary>
    /// Approving asks first, and a cancelled question dispatches nothing.
    /// </summary>
    /// <remarks>
    /// This is the only button in the shell that authorises the agent to delete files without
    /// naming them one at a time. The default answer is Cancel, so a stray Enter on the dialog
    /// cannot carry it.
    /// </remarks>
    [Fact]
    public async Task ApprovingAsksBeforeItDispatches()
    {
        var run = Run(SyncIpcRunPhase.AwaitingApproval);
        var agent = new StubReviewAgent { Status = run };
        agent.PlanPage(run, [Operation(1, destructive: true), Operation(2)], null);
        agent.ConflictPage([], null);
        var dialogs = new RecordingDialogs { Choice = DialogChoice.Cancel };
        using var model = SyncRunHistoryModel.Create(() => agent, dialogs);
        await model.LoadRunAsync(run.SyncRunId, TestContext.Current.CancellationToken);

        await model.ApproveAsync(TestContext.Current.CancellationToken);

        Assert.Null(agent.ApprovalSent);
        Assert.Equal(DialogChoice.Cancel, dialogs.LastRequest!.Default);
        Assert.Equal(DialogSeverity.Warning, dialogs.LastRequest.Severity);

        // And it says what it is about to do, including how much of it removes data.
        Assert.Equal(Ui.Format(Ui.Sync.ApproveDestructiveFormat, 2, 1), dialogs.LastRequest.Detail);
    }

    /// <summary>And an accepted question dispatches exactly the run that was reviewed.</summary>
    [Fact]
    public async Task ApprovingDispatchesTheRunThatWasReviewed()
    {
        var run = Run(SyncIpcRunPhase.AwaitingApproval) with { Revision = 9 };
        var agent = new StubReviewAgent { Status = run };
        agent.PlanPage(run, [Operation(1)], null);
        agent.ConflictPage([], null);
        agent.Dispatched = run with { DispatchState = SyncIpcDispatchState.DurablyDispatched };
        var dialogs = new RecordingDialogs { Choice = DialogChoice.Ok };
        using var model = SyncRunHistoryModel.Create(() => agent, dialogs);
        await model.LoadRunAsync(run.SyncRunId, TestContext.Current.CancellationToken);

        await model.ApproveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(9, agent.ApprovalSent!.ExpectedRevision);
        Assert.Equal(ApprovalDigest, agent.ApprovalSent.ApprovalSha256);
        Assert.False(model.CanApprove);
    }

    /// <summary>
    /// With nowhere to ask, nothing is dispatched.
    /// </summary>
    /// <remarks>
    /// A screen built without a dialog service is a preview or a layout test. Treating the missing
    /// confirmation as consent would make the unconfigured case the dangerous one.
    /// </remarks>
    [Fact]
    public async Task WithoutSomewhereToAskNothingIsDispatched()
    {
        var run = Run(SyncIpcRunPhase.AwaitingApproval);
        var agent = new StubReviewAgent { Status = run };
        agent.PlanPage(run, [], null);
        agent.ConflictPage([], null);
        using var model = SyncRunHistoryModel.Create(() => agent);
        await model.LoadRunAsync(run.SyncRunId, TestContext.Current.CancellationToken);

        await model.ApproveAsync(TestContext.Current.CancellationToken);

        Assert.Null(agent.ApprovalSent);
    }

    /// <summary>Next page is offered only while the agent says there is one.</summary>
    [Fact]
    public async Task PagingForwardIsOfferedOnlyWhileThereIsMoreHistory()
    {
        var agent = new StubReviewAgent();
        agent.HistoryPage([Run(SyncIpcRunPhase.Completed)], "50");
        agent.HistoryPage([Run(SyncIpcRunPhase.Completed)], null);
        using var model = SyncRunHistoryModel.Create(() => agent);

        await model.RefreshHistoryAsync(null, TestContext.Current.CancellationToken);
        Assert.True(model.CanPageForward);

        await model.RefreshHistoryAsync("50", TestContext.Current.CancellationToken);
        Assert.False(model.CanPageForward);
    }

    /// <summary>
    /// The history loads once when the screen is first shown, not on every visit.
    /// </summary>
    /// <remarks>
    /// A table saying "no runs yet" when there are runs answers the question wrongly, which is
    /// worse than leaving it open. Asking again on every tab switch would be a pipe connection and
    /// a listing per click.
    /// </remarks>
    [Fact]
    public async Task TheHistoryLoadsOnceOnItsOwn()
    {
        var agent = new StubReviewAgent();
        agent.HistoryPage([Run(SyncIpcRunPhase.Completed)], null);
        using var model = SyncRunHistoryModel.Create(() => agent);

        await model.EnsureHistoryAsync(TestContext.Current.CancellationToken);
        await model.EnsureHistoryAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, agent.HistoryRequests);
        Assert.Single(model.Runs);
    }

    /// <summary>A failure says why and leaves the table alone.</summary>
    [Fact]
    public async Task AFailedHistoryReadKeepsTheRowsItHad()
    {
        var agent = new StubReviewAgent();
        agent.HistoryPage([Run(SyncIpcRunPhase.Completed)], null);
        using var model = SyncRunHistoryModel.Create(() => agent);
        await model.RefreshHistoryAsync(null, TestContext.Current.CancellationToken);

        agent.Throws = new IOException("there is no agent");
        await model.RefreshHistoryAsync(null, TestContext.Current.CancellationToken);

        Assert.Single(model.Runs);
        Assert.True(model.HistoryStatus.IsDanger);
    }

    /// <summary>A screen with nothing behind it never asks and never offers anything.</summary>
    [Fact]
    public async Task APreviewScreenNeverReaches()
    {
        using var model = SyncRunHistoryModel.Create();

        await model.EnsureHistoryAsync(TestContext.Current.CancellationToken);
        await model.RefreshHistoryAsync(null, TestContext.Current.CancellationToken);

        Assert.Empty(model.Runs);
        Assert.False(model.LoadRunCommand.CanExecute(null));
        Assert.False(model.ApproveCommand.CanExecute(null));
    }

    /// <summary>
    /// Photographs the screen with a run actually loaded, for a human to look at.
    /// </summary>
    /// <remarks>
    /// This is the densest screen in the app: a split, a table on the left, a heading with two
    /// buttons and two more tables under a tab strip on the right. It is exactly the sort of layout
    /// that goes wrong without failing anything, and the empty version that was here before could
    /// never have shown it. Set STORAGEHUB_SHOT_DIR to keep the files.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    public async Task TheScreenCanBePhotographedWithARunOnIt(bool dark, int tab)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));

        var run = Run(SyncIpcRunPhase.AwaitingApproval);
        var agent = new StubReviewAgent { Status = run };
        agent.HistoryPage(
            [
                run,
                Run(SyncIpcRunPhase.Completed) with
                {
                    DispatchState = SyncIpcDispatchState.DurablyDispatched
                },
                Run(SyncIpcRunPhase.Failed) with
                {
                    DispatchState = SyncIpcDispatchState.DurablyDispatched,
                    ConflictCount = 2
                }
            ],
            "50");
        agent.PlanPage(
            run,
            [.. Enumerable.Range(1, 12).Select(index => Operation(index, destructive: index % 4 == 0))],
            null);
        agent.ConflictPage([Conflict("photos/2019/IMG_0042.jpg"), Conflict("notes/todo.md")], null);

        using var model = SyncRunHistoryModel.Create(() => agent, new RecordingDialogs());
        await model.RefreshHistoryAsync(null, TestContext.Current.CancellationToken);
        await model.LoadRunAsync(run.SyncRunId, TestContext.Current.CancellationToken);

        var view = new SyncRunHistoryView { DataContext = model };
        var window = new Window { Content = view, Width = 1500, Height = 860 };
        window.Show();
        window.Measure(new Size(1500, 860));
        window.Arrange(new Rect(0, 0, 1500, 860));
        window.UpdateLayout();

        // Selecting a tab queues its content to be templated, so the conflicts table does not exist
        // until layout runs again.
        var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
        tabs.SelectedIndex = tab;
        window.UpdateLayout();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;

        var name = tab == 0 ? "plan" : "conflicts";
        Directory.CreateDirectory(directory);
        using var stream = File.Create(
            Path.Combine(directory, $"sync-run-history-{name}-{(dark ? "dark" : "light")}.png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }

    private static SyncRunSummary Run(SyncIpcRunPhase phase) => new(
        Guid.NewGuid(), Guid.NewGuid(), 1, phase, SyncIpcStatusCode.None, 1,
        DateTimeOffset.UtcNow, Guid.NewGuid(), PlanDigest, ApprovalDigest, 0,
        SyncIpcDispatchState.NotDispatched, null, DateTimeOffset.UtcNow,
        0, 0, 0, LeftSnapshotComplete: true, RightSnapshotComplete: true);

    private static SyncPlanOperationSummary Operation(int sequence, bool destructive = false) => new(
        sequence, SyncIpcPlanOperationKind.Copy, Guid.NewGuid(), $"left/{sequence}",
        Guid.NewGuid(), $"right/{sequence}", 2048, destructive);

    private static SyncConflictSummary Conflict(string path) => new(
        Guid.NewGuid(), path, "BothChanged", SyncIpcConflictState.Unresolved,
        "Both sides changed", DateTimeOffset.UtcNow, null, 1);

    /// <summary>The agent, scripted by the test.</summary>
    private sealed class StubReviewAgent : ISyncManagementAgentClient
    {
        private readonly Queue<(SyncRunSummary Plan, SyncPlanOperationSummary[] Operations, string? Token)> _plans = new();
        private readonly Queue<(SyncConflictSummary[] Conflicts, string? Token)> _conflicts = new();
        private readonly Queue<(SyncRunSummary[] Runs, string? Token)> _history = new();

        internal SyncRunSummary? Status { get; set; }

        internal SyncRunSummary? Dispatched { get; set; }

        internal SyncApproveDispatchRequest? ApprovalSent { get; private set; }

        internal Exception? Throws { get; set; }

        internal int StatusRequests { get; private set; }

        internal int HistoryRequests { get; private set; }

        internal void PlanPage(SyncRunSummary plan, SyncPlanOperationSummary[] operations, string? token) =>
            _plans.Enqueue((plan, operations, token));

        internal void ConflictPage(SyncConflictSummary[] conflicts, string? token) =>
            _conflicts.Enqueue((conflicts, token));

        internal void HistoryPage(SyncRunSummary[] runs, string? token) =>
            _history.Enqueue((runs, token));

        public Task<SyncRunStatusResponse> GetRunStatusAsync(
            SyncRunStatusRequest request, CancellationToken cancellationToken = default)
        {
            StatusRequests++;
            if (Throws is { } error) throw error;
            return Task.FromResult(new SyncRunStatusResponse(
                SyncManagementIpcContract.CurrentVersion, request.SyncRunId, Status));
        }

        public Task<SyncPlanPageResponse> GetPlanPageAsync(
            SyncPlanPageRequest request, CancellationToken cancellationToken = default)
        {
            if (Throws is { } error) throw error;
            var page = _plans.Count > 0
                ? _plans.Dequeue()
                : (Status ?? throw new InvalidOperationException("No plan page was scripted."), [], null);

            return Task.FromResult(new SyncPlanPageResponse(
                SyncManagementIpcContract.CurrentVersion,
                request.SyncRunId,
                page.Plan.PlanId,
                page.Plan.PlanSha256,
                page.Operations.Length,
                page.Operations,
                page.Token));
        }

        public Task<SyncConflictPageResponse> GetConflictPageAsync(
            SyncConflictPageRequest request, CancellationToken cancellationToken = default)
        {
            if (Throws is { } error) throw error;
            var page = _conflicts.Count > 0 ? _conflicts.Dequeue() : ([], null);
            return Task.FromResult(new SyncConflictPageResponse(
                SyncManagementIpcContract.CurrentVersion,
                request.SyncRunId,
                page.Conflicts.Length,
                page.Conflicts,
                page.Token,
                IsTruncatedAtSource: false));
        }

        public Task<SyncRunListResponse> ListRunsAsync(
            SyncRunListRequest request, CancellationToken cancellationToken = default)
        {
            HistoryRequests++;
            if (Throws is { } error) throw error;
            var page = _history.Count > 0 ? _history.Dequeue() : ([], null);
            return Task.FromResult(new SyncRunListResponse(
                SyncManagementIpcContract.CurrentVersion, page.Runs, page.Token));
        }

        public Task<SyncApproveDispatchResponse> ApproveAndDispatchAsync(
            SyncApproveDispatchRequest request, CancellationToken cancellationToken = default)
        {
            if (Throws is { } error) throw error;
            ApprovalSent = request;
            return Task.FromResult(new SyncApproveDispatchResponse(
                SyncManagementIpcContract.CurrentVersion, request.SyncRunId, true, Dispatched));
        }

        public Task<SyncProfileListResponse> ListProfilesAsync(
            SyncProfileListRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SyncProfileGetResponse> GetProfileAsync(
            SyncProfileGetRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SyncProfileMutationResponse> CreateProfileAsync(
            SyncProfileCreateRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SyncProfileMutationResponse> UpdateProfileAsync(
            SyncProfileUpdateRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SyncPreviewGenerateResponse> GeneratePreviewAsync(
            SyncPreviewGenerateRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>The confirmation, answered by the test instead of by a person.</summary>
    private sealed class RecordingDialogs : IDialogService
    {
        internal DialogChoice Choice { get; set; } = DialogChoice.Cancel;

        internal DialogRequest? LastRequest { get; private set; }

        public Task ShowAsync(DialogRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.CompletedTask;
        }

        public Task<DialogChoice> ConfirmAsync(
            DialogRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(Choice);
        }

        public Task<string?> PromptAsync(
            DialogPromptRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
