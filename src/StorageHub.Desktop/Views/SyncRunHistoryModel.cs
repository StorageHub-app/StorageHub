using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;

namespace StorageHub.Desktop.Views;

/// <summary>
/// A line of status text and how much it should stand out.
/// </summary>
/// <remarks>
/// The tone travels with the words so a view never decides a brush per screen, which is how the
/// same state came to read green in one place and grey in another. It matters most here: a run that
/// failed and a run waiting for approval must not look alike at a glance.
/// </remarks>
internal sealed record StatusLine(string Text, MetricTone Tone = MetricTone.Neutral)
{
    internal static StatusLine Muted(string text) => new(text);

    public bool IsPrimary => Tone == MetricTone.Primary;

    public bool IsSuccess => Tone == MetricTone.Success;

    public bool IsWarning => Tone == MetricTone.Warning;

    public bool IsDanger => Tone == MetricTone.Danger;
}

/// <summary>A row of the run history table.</summary>
/// <param name="SyncRunId">Not shown. It is what selecting the row puts in the run id box.</param>
/// <param name="Updated">When, as briefly as is still unambiguous; see <c>Moment</c>.</param>
/// <param name="UpdatedInFull">When, with the date in full, for the cell's tip.</param>
internal sealed record SyncRunRow(
    Guid SyncRunId,
    string Updated,
    string Run,
    string Phase,
    string Dispatch,
    string Conflicts,
    string UpdatedInFull = "")
{
    /// <summary>The whole run id, for the Run cell's tip.</summary>
    public string RunInFull => SyncRunId.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>A row of the plan table.</summary>
/// <param name="From">The source in full: its connection, then its path. The cell's tip.</param>
/// <param name="To">The destination in full, likewise. Empty for an operation with none.</param>
internal sealed record PlanRow(
    string Index,
    string Action,
    string From,
    string To,
    string ExpectedBytes,
    string Approval)
{
    /// <summary>The source's connection with the separator after it, as the cell starts.</summary>
    public string FromConnection => ConnectionPart(From);

    /// <summary>The source's path, which the cell cuts from the left so the file's name stays.</summary>
    public string FromPath => PathPart(From);

    public string ToConnection => ConnectionPart(To);

    public string ToPath => PathPart(To);

    internal const string Separator = " · ";

    private static string ConnectionPart(string endpoint) =>
        endpoint.IndexOf(Separator, StringComparison.Ordinal) is var at and >= 0
            ? endpoint[..(at + Separator.Length)]
            : string.Empty;

    private static string PathPart(string endpoint) =>
        endpoint.IndexOf(Separator, StringComparison.Ordinal) is var at and >= 0
            ? endpoint[(at + Separator.Length)..]
            : endpoint;
}

/// <summary>A row of the conflicts table.</summary>
internal sealed record ConflictRow(string Path, string Kind, string State, string Reason);

/// <summary>
/// The Run history and review sub-tab, against docs/ui-reference/04-sync-run-history.png.
/// </summary>
/// <remarks>
/// <para>
/// This was a record of constants: the run id was <c>Guid.Empty</c>, both tables were empty, the
/// conflicts tab was an empty border, and every button that needs a loaded run was disabled because
/// the literal said so. It described the screen instead of being it.
/// </para>
/// <para>
/// It now reads real runs. <see cref="SyncRunReviewController"/> does the asking and holds the
/// checks that make approval safe; what is here is the screen's own behaviour -- which row fills
/// the run id box, how often a loaded run is re-read, and the confirmation in front of the one
/// button on this page that moves somebody's files.
/// </para>
/// </remarks>
internal sealed class SyncRunHistoryModel : INotifyPropertyChanged, IDisposable
{
    private readonly SyncRunReviewController? _controller;
    private readonly IDialogService? _dialogs;
    private readonly IClipboardService? _clipboard;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _waiting;
    private SyncRunReview _review = SyncRunReview.Empty;
    private CancellationTokenSource? _watch;
    private string _runId = string.Empty;
    private string? _nextHistoryToken;
    private StatusLine _historyStatus = StatusLine.Muted(Ui.Sync.EnterRunId);
    private StatusLine _planStatus = StatusLine.Muted(Ui.Sync.ChooseReviewAndRun);
    private string _planTitle = Ui.Sync.NoPlanLoaded;
    private string _planSubtitle = string.Empty;
    private SyncRunRow? _selectedRun;
    private bool _isBusy;
    private bool _historyLoaded;
    private bool _disposed;

    /// <param name="controller">
    /// How it reaches the agent. Null leaves a screen that shows its empty state and never loads,
    /// which is what a layout test wants.
    /// </param>
    /// <param name="dialogs">
    /// Where the approval confirmation goes. Null refuses to approve rather than approving without
    /// asking: an unconfirmed dispatch is the one outcome this screen must not produce by accident.
    /// </param>
    /// <param name="clipboard">Where Copy run ID puts the whole id. Null leaves it unavailable.</param>
    internal SyncRunHistoryModel(
        SyncRunReviewController? controller = null,
        IDialogService? dialogs = null,
        IClipboardService? clipboard = null)
    {
        _controller = controller;
        _dialogs = dialogs;
        _clipboard = clipboard;

        LoadRunCommand = new RelayCommand(
            _ => _ = LoadRunAsync(), _ => Live && !IsBusy);
        RefreshHistoryCommand = new RelayCommand(
            _ => _ = RefreshHistoryAsync(), _ => Live && !IsBusy);
        NextPageCommand = new RelayCommand(
            _ => _ = RefreshHistoryAsync(_nextHistoryToken), _ => Live && !IsBusy && CanPageForward);
        RefreshStatusCommand = new RelayCommand(
            _ => _ = RefreshStatusAsync(), _ => Live && !IsBusy && _review.Run is not null);
        ApproveCommand = new RelayCommand(
            _ => _ = ApproveAsync(), _ => Live && !IsBusy && CanApprove);
        LoadMoreOperationsCommand = new RelayCommand(
            _ => _ = LoadMoreAsync(operations: true), _ => Live && !IsBusy && CanLoadMoreOperations);
        LoadMoreConflictsCommand = new RelayCommand(
            _ => _ = LoadMoreAsync(operations: false), _ => Live && !IsBusy && CanLoadMoreConflicts);
        CopyRunIdCommand = new RelayCommand(
            _ => _ = _clipboard!.SetTextAsync(PlanRunId!), _ => _clipboard is not null && PlanRunId is not null);
    }

    /// <summary>A screen with nothing behind it, for a preview or a layout test.</summary>
    internal static SyncRunHistoryModel Create() => new();

    /// <summary>And one that will ask the agent.</summary>
    internal static SyncRunHistoryModel Create(
        Func<ISyncManagementAgentClient> clients,
        IDialogService? dialogs = null,
        IClipboardService? clipboard = null) =>
        new(new SyncRunReviewController(clients), dialogs, clipboard);

    public ObservableCollection<SyncRunRow> Runs { get; } = [];

    public ObservableCollection<PlanRow> Plan { get; } = [];

    public ObservableCollection<ConflictRow> Conflicts { get; } = [];

    /// <summary>The run to load, as typed or as filled in by selecting a row.</summary>
    public string RunId
    {
        get => _runId;
        set
        {
            if (Set(ref _runId, value)) Refresh(LoadRunCommand);
        }
    }

    /// <summary>
    /// The history row in hand, which fills the run id box.
    /// </summary>
    /// <remarks>
    /// Selecting does not load. Reading down a list of runs to find the one you want should not
    /// fetch a plan per row, and on this screen a plan is three round trips.
    /// </remarks>
    public SyncRunRow? SelectedRun
    {
        get => _selectedRun;
        set
        {
            if (!Set(ref _selectedRun, value)) return;
            if (value is { } row && row.SyncRunId != Guid.Empty)
            {
                RunId = row.SyncRunId.ToString("D", CultureInfo.InvariantCulture);
            }
        }
    }

    public StatusLine HistoryStatus
    {
        get => _historyStatus;
        private set => Set(ref _historyStatus, value);
    }

    /// <summary>
    /// The heading over the plan: which run, by the eight characters the history list knows it by.
    /// </summary>
    /// <remarks>
    /// The whole id, its phase and its revision on one bold line was wider than the room beside the
    /// two buttons, so it wrapped and left the revision number alone on a line of its own. The
    /// whole id is on the heading's tip and behind Copy run ID instead.
    /// </remarks>
    public string PlanTitle
    {
        get => _planTitle;
        private set => Set(ref _planTitle, value);
    }

    /// <summary>The loaded run's phase and revision, under the heading. Empty with no run.</summary>
    public string PlanSubtitle
    {
        get => _planSubtitle;
        private set => Set(ref _planSubtitle, value);
    }

    /// <summary>The loaded run's whole id, or null with no run.</summary>
    public string? PlanRunId => _review.Run?.SyncRunId.ToString("D", CultureInfo.InvariantCulture);

    public StatusLine PlanStatus
    {
        get => _planStatus;
        private set => Set(ref _planStatus, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value)) RefreshCommands();
        }
    }

    public bool CanPageForward => _nextHistoryToken is not null;

    public bool CanApprove => _review.CanApprove;

    public bool CanLoadMoreOperations => _review.HasMoreOperations;

    public bool CanLoadMoreConflicts => _review.HasMoreConflicts;

    public ICommand LoadRunCommand { get; }

    public ICommand RefreshHistoryCommand { get; }

    public ICommand NextPageCommand { get; }

    public ICommand RefreshStatusCommand { get; }

    public ICommand ApproveCommand { get; }

    public ICommand LoadMoreOperationsCommand { get; }

    public ICommand LoadMoreConflictsCommand { get; }

    public ICommand CopyRunIdCommand { get; }

    /// <summary>
    /// Loads the history the first time the screen is shown, and not again.
    /// </summary>
    /// <remarks>
    /// The WinForms screen loaded on becoming visible, and it should: a table saying "no runs yet"
    /// when there are runs is worse than an empty one, because it answers the question wrongly
    /// rather than leaving it open. Refresh history is still there to ask again.
    /// </remarks>
    internal Task EnsureHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (_historyLoaded || _controller is null) return Task.CompletedTask;
        _historyLoaded = true;
        return RefreshHistoryAsync(null, cancellationToken);
    }

    /// <summary>Reads a page of history, from the start unless a cursor is given.</summary>
    internal async Task RefreshHistoryAsync(
        string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        if (_controller is not { } controller) return;
        await ExclusiveAsync(async token =>
        {
            HistoryStatus = new StatusLine(Ui.Sync.LoadingHistory);
            var page = await controller.LoadHistoryAsync(continuationToken, token)
                .ConfigureAwait(true);

            if (page.Failed)
            {
                HistoryStatus = new StatusLine(page.ErrorMessage!, MetricTone.Danger);
                return;
            }

            _historyLoaded = true;
            _nextHistoryToken = page.NextPageToken;
            Raise(nameof(CanPageForward));
            Show(page.Runs);
            SelectLoadedRun();
            HistoryStatus = page.Runs.Count == 0
                ? StatusLine.Muted(Ui.Sync.NoRunsYet)
                : new StatusLine(
                    Ui.Format(Ui.Sync.ShowingRunsFormat, page.Runs.Count), MetricTone.Success);
        }, cancellationToken).ConfigureAwait(true);
        Refresh(NextPageCommand);
    }

    /// <summary>Loads whatever run id is in the box.</summary>
    internal Task LoadRunAsync(CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(RunId, out var runId) || runId == Guid.Empty)
        {
            HistoryStatus = new StatusLine(Ui.Sync.EnterValidRunId, MetricTone.Warning);
            return Task.CompletedTask;
        }

        return LoadRunAsync(runId, cancellationToken);
    }

    /// <summary>Loads one run, its plan and its conflicts.</summary>
    internal async Task LoadRunAsync(Guid syncRunId, CancellationToken cancellationToken = default)
    {
        if (_controller is not { } controller) return;
        RunId = syncRunId.ToString("D", CultureInfo.InvariantCulture);
        await ExclusiveAsync(async token =>
        {
            HistoryStatus = new StatusLine(Ui.Sync.LoadingPlan);
            var review = await controller.LoadRunAsync(syncRunId, token).ConfigureAwait(true);
            Adopt(review);
            HistoryStatus = review.Failed
                ? new StatusLine(Ui.Sync.RunLoadFailed, MetricTone.Danger)
                : new StatusLine(Ui.Sync.RunLoaded, MetricTone.Success);
        }, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Shows a run the editor has just made, and reads the history again so it is in the list.
    /// </summary>
    /// <remarks>
    /// The history is read once, as the screen is first shown, so a run made after that was loaded
    /// beside a list that did not have it until Refresh history. A history not read yet is left to
    /// that first read, which will find the run anyway.
    /// </remarks>
    internal Task ReviewNewRunAsync(Guid syncRunId, CancellationToken cancellationToken = default)
    {
        var run = LoadRunAsync(syncRunId, cancellationToken);
        var history = _historyLoaded
            ? RefreshHistoryAsync(null, cancellationToken)
            : Task.CompletedTask;
        return Task.WhenAll(run, history);
    }

    /// <summary>Re-reads the loaded run's phase, keeping the plan where it is.</summary>
    internal async Task RefreshStatusAsync(CancellationToken cancellationToken = default)
    {
        if (_controller is not { } controller || IsBusy || _review.Run is null) return;
        await ExclusiveAsync(async token => Adopt(
                await controller.RefreshStatusAsync(_review, token).ConfigureAwait(true)),
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Confirms, then approves the loaded run and asks the agent to make the request durable.
    /// </summary>
    /// <remarks>
    /// The confirmation is not a formality. This is the only button in the shell that authorises
    /// the agent to delete files on somebody's storage without naming them one at a time, and its
    /// default answer is Cancel so a stray Enter cannot carry it.
    /// </remarks>
    internal async Task ApproveAsync(CancellationToken cancellationToken = default)
    {
        if (_controller is not { } controller || IsBusy || !_review.CanApprove) return;
        var confirmed = _review.Run!;

        // No dialog service means no way to ask, and dispatching unasked is not the safer default.
        if (_dialogs is null)
        {
            PlanStatus = new StatusLine(Ui.Sync.ApproveHint, MetricTone.Warning);
            return;
        }

        var choice = await _dialogs.ConfirmAsync(
            new DialogRequest
            {
                Title = Ui.Dialogs.ApproveSyncCaption,
                Message = Ui.Dialogs.ApproveSyncPrompt,
                Detail = Describe(_review),
                Severity = DialogSeverity.Warning,
                Buttons = DialogButtons.OkCancel,
                Default = DialogChoice.Cancel
            },
            cancellationToken).ConfigureAwait(true);
        if (choice != DialogChoice.Ok) return;

        await ExclusiveAsync(async token =>
        {
            // The run that was confirmed, still approvable. Something queued ahead of this may have
            // loaded another run, or read this one dispatched, while the question was open. The
            // idle poll may also have read a newer revision of it meanwhile, so what is approved is
            // the revision and digest the reviewer confirmed, and the agent refuses them if the run
            // has moved on since.
            if (_review.Run?.SyncRunId != confirmed.SyncRunId || !_review.CanApprove) return;
            Adopt(await controller.ApproveAndDispatchAsync(_review with { Run = confirmed }, token)
                .ConfigureAwait(true));
        }, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Appends the next page of plan operations, or of conflicts.</summary>
    internal async Task LoadMoreAsync(
        bool operations,
        CancellationToken cancellationToken = default)
    {
        if (_controller is not { } controller || IsBusy) return;
        await ExclusiveAsync(async token => Adopt(
                await (operations
                    ? controller.LoadMoreOperationsAsync(_review, token)
                    : controller.LoadMoreConflictsAsync(_review, token))
                .ConfigureAwait(true)),
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Runs one exchange with the agent at a time, waiting for the one in flight rather than
    /// dropping the second.
    /// </summary>
    /// <remarks>
    /// Coming from the editor asks for two things at once on the first visit: the view asks for the
    /// history as it is attached, and the shell asks for the run the preview made. With "busy
    /// means return", whichever came second was lost, so the screen showed the run without the
    /// history or the history without the run, depending on which was first. 1.4 read the history
    /// apart from the run and put the run's own reads in a queue; this queues both. Busy stays
    /// true until the queue is empty, so the buttons do not flicker between one read and the next.
    /// </remarks>
    private async Task ExclusiveAsync(
        Func<CancellationToken, Task> exchange,
        CancellationToken cancellationToken)
    {
        _waiting++;
        IsBusy = true;
        var entered = false;
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(true);
            entered = true;
            await exchange(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            // Busy is settled before the next in the queue is let in, so its own ending is last.
            IsBusy = --_waiting > 0;
            if (entered) _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopWatching();
    }

    /// <summary>Takes a review as the current one, and re-decides how closely to watch it.</summary>
    private void Adopt(SyncRunReview review)
    {
        Apply(review);
        StartWatching();
    }

    /// <summary>Puts a review on screen without disturbing the polling already under way.</summary>
    private void Apply(SyncRunReview review)
    {
        var replacing = _review.Run?.SyncRunId != review.Run?.SyncRunId;
        _review = review;

        if (review.Run is { } run)
        {
            PlanTitle = Ui.Format(Ui.Sync.RunTitleFormat, ShortId(run.SyncRunId));
            PlanSubtitle = Ui.Format(Ui.Sync.RunSubtitleFormat, UiEnumNames.Describe(run.Phase), run.Revision);
            PlanStatus = review.Failed
                ? new StatusLine(review.ErrorMessage!, MetricTone.Danger)
                : Narrate(review);
        }
        else
        {
            PlanTitle = Ui.Sync.NoPlanLoaded;
            PlanSubtitle = string.Empty;
            PlanStatus = review.Failed
                ? new StatusLine(review.ErrorMessage!, MetricTone.Danger)
                : StatusLine.Muted(Ui.Sync.ChooseReviewAndRun);
        }

        // The tables are only rebuilt when they could have changed. A poll every half second that
        // cleared and refilled a thousand-row plan would take the reviewer's place in it with it.
        if (replacing) SelectLoadedRun();
        if (replacing || review.Operations.Count != Plan.Count) ShowPlan(review.Operations);
        if (replacing || review.Conflicts.Count != Conflicts.Count) ShowConflicts(review.Conflicts);

        Raise(nameof(PlanRunId));
        Raise(nameof(CanApprove));
        Raise(nameof(CanLoadMoreOperations));
        Raise(nameof(CanLoadMoreConflicts));
        RefreshCommands();
    }

    /// <summary>
    /// What the run's phase and dispatch state mean, in a sentence.
    /// </summary>
    /// <remarks>
    /// Dispatched and running is deliberately not reported as finished, and neither is dispatched
    /// and nothing-yet. The agent makes the apply request durable and then executes it, and the gap
    /// between those two is where a half-done sync would be read as a completed one.
    /// </remarks>
    /// <remarks>
    /// A plan with nothing in it says so rather than asking for approval as if it had work to do.
    /// It is still approvable, as it was in 1.4, because approving it is what records that the two
    /// locations match.
    /// </remarks>
    private static StatusLine Narrate(SyncRunReview review)
    {
        var run = review.Run!;
        if (run.DispatchState != SyncIpcDispatchState.DurablyDispatched)
        {
            if (run.Phase == SyncIpcRunPhase.AwaitingApproval && IsEmpty(review))
            {
                return new StatusLine(Ui.Sync.NoPlanOperations, MetricTone.Success);
            }

            return run.Phase == SyncIpcRunPhase.AwaitingApproval
                ? new StatusLine(Ui.Sync.AwaitingApproval, MetricTone.Warning)
                : StatusLine.Muted(
                    Ui.Format(Ui.Sync.RunPhaseFormat, UiEnumNames.Describe(run.Phase)));
        }

        return run.Phase switch
        {
            SyncIpcRunPhase.Completed => new StatusLine(Ui.Sync.PhaseCompleted, MetricTone.Success),
            SyncIpcRunPhase.Failed => new StatusLine(Ui.Sync.PhaseFailed, MetricTone.Danger),
            SyncIpcRunPhase.NeedsReconciliation =>
                new StatusLine(Ui.Sync.PhaseUncertain, MetricTone.Warning),
            SyncIpcRunPhase.Cancelled => new StatusLine(Ui.Sync.PhaseCancelled, MetricTone.Warning),
            SyncIpcRunPhase.Executing =>
                new StatusLine(Ui.Sync.PhaseSynchronizing, MetricTone.Primary),
            SyncIpcRunPhase.Verifying => new StatusLine(Ui.Sync.PhaseVerifying, MetricTone.Primary),
            SyncIpcRunPhase.CommittingBaseline =>
                new StatusLine(Ui.Sync.PhaseCommitting, MetricTone.Primary),
            _ => new StatusLine(Ui.Sync.PhaseQueued, MetricTone.Primary)
        };
    }

    /// <summary>Whether the plan has been read to its end and holds nothing, conflicts included.</summary>
    private static bool IsEmpty(SyncRunReview review) =>
        review.Operations.Count == 0 && !review.HasMoreOperations &&
        review.Conflicts.Count == 0 && review.Run!.ConflictCount == 0;

    /// <summary>
    /// What the confirmation says is about to happen.
    /// </summary>
    /// <remarks>
    /// How many operations, and how many of them remove data, because those are the two numbers
    /// somebody would want before saying yes. Only the loaded pages can be counted, which is
    /// another reason the plan is read in full-size pages rather than lazily.
    /// </remarks>
    private static string Describe(SyncRunReview review)
    {
        var destructive = review.Operations.Count(static operation => operation.IsDestructive);
        return destructive == 0
            ? Ui.Format(Ui.Sync.ApproveDetailFormat, review.Operations.Count)
            : Ui.Format(Ui.Sync.ApproveDestructiveFormat, review.Operations.Count, destructive);
    }

    private void Show(IReadOnlyList<SyncRunSummary> runs)
    {
        Runs.Clear();
        var now = DateTimeOffset.Now;
        foreach (var run in runs)
        {
            Runs.Add(new SyncRunRow(
                run.SyncRunId,
                Moment(run.UpdatedUtc, now),
                ShortId(run.SyncRunId),
                UiEnumNames.Describe(run.Phase),
                UiEnumNames.Describe(run.DispatchState),
                run.ConflictCount.ToString(CultureInfo.CurrentCulture),
                run.UpdatedUtc.ToLocalTime().ToString("f", CultureInfo.CurrentCulture)));
        }
    }

    /// <summary>Marks the loaded run's row in the history, when it is on the page.</summary>
    private void SelectLoadedRun()
    {
        if (_review.Run is not { } run) return;
        if (Runs.FirstOrDefault(row => row.SyncRunId == run.SyncRunId) is { } row) SelectedRun = row;
    }

    private void ShowPlan(IReadOnlyList<SyncPlanOperationSummary> operations)
    {
        Plan.Clear();
        foreach (var operation in operations)
        {
            Plan.Add(new PlanRow(
                operation.Sequence.ToString(CultureInfo.CurrentCulture),
                UiEnumNames.Describe(operation.Kind),
                Endpoint(operation.SourceConnectionId, operation.SourcePath),
                operation.DestinationConnectionId is { } destination
                    ? Endpoint(destination, operation.DestinationPath ?? string.Empty)
                    : string.Empty,
                operation.ExpectedLength?.ToString("N0", CultureInfo.CurrentCulture) ?? "—",
                operation.IsDestructive ? Ui.Sync.DestructiveApprovalRequired : Ui.Sync.Guarded));
        }
    }

    private void ShowConflicts(IReadOnlyList<SyncConflictSummary> conflicts)
    {
        Conflicts.Clear();
        foreach (var conflict in conflicts)
        {
            Conflicts.Add(new ConflictRow(
                conflict.RelativePath,
                conflict.ConflictKind,
                UiEnumNames.Describe(conflict.State),
                conflict.SafeReason));
        }
    }

    /// <summary>
    /// Re-reads the loaded run on its own, for as long as it is worth re-reading.
    /// </summary>
    /// <remarks>
    /// <see cref="SyncRunPolling"/> decides the pace and when to stop; this only has to not fight
    /// the person using the screen, so a tick that lands while something else is in flight is
    /// skipped rather than queued.
    /// </remarks>
    private void StartWatching()
    {
        StopWatching();
        if (_disposed || _controller is null || SyncRunPolling.Interval(_review.Run) is null) return;

        var source = new CancellationTokenSource();
        _watch = source;
        _ = WatchAsync(source.Token);
    }

    private void StopWatching()
    {
        if (_watch is not { } watch) return;
        _watch = null;
        watch.Cancel();
        watch.Dispose();
    }

    private async Task WatchAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (SyncRunPolling.Interval(_review.Run) is { } wait)
            {
                await Task.Delay(wait, cancellationToken).ConfigureAwait(true);
                if (cancellationToken.IsCancellationRequested) return;
                if (IsBusy) continue;

                var review = await _controller!.RefreshStatusAsync(_review, cancellationToken)
                    .ConfigureAwait(true);
                if (cancellationToken.IsCancellationRequested) return;

                // Apply rather than Adopt: restarting the watch from inside it would cancel the
                // token this loop is running on and leave a new task behind on every tick.
                Apply(review);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Where an operation reads from or writes to.
    /// </summary>
    /// <remarks>
    /// As 1.4's review wrote it (<c>SyncRunReviewControl.FormatEndpoint</c>): the whole
    /// thirty-six-character id, hyphens included, and <c>&lt;root&gt;</c> for a connection's top
    /// folder, untranslated. 1.4's transfer queue shortened the id to eight characters and wrote
    /// the top folder "/"; its review did not, and neither does this.
    ///
    /// The view puts the whole of it on the cell's tip, since the id leaves little of the path in
    /// the column. The saved connection's name is left out on purpose, though the shell could
    /// hand this screen the lookup the queue names connections with (P.4.5): 1.4's review printed
    /// the id.
    /// </remarks>
    /// <summary>
    /// One side of a planned operation: the connection's name and the path, as the transfer queue
    /// writes it.
    /// </summary>
    /// <remarks>
    /// 1.4 wrote the connection's full id, which filled the column, so the path, the part a
    /// reviewer needs before approving, was cut off behind it. The id is still what is written
    /// for a connection whose name is not known.
    /// </remarks>
    private string Endpoint(Guid connectionId, string path)
    {
        var connection = ConnectionName?.Invoke(connectionId) is { Length: > 0 } name
            ? name
            : connectionId.ToString("D", CultureInfo.InvariantCulture);
        return $"{connection}{PlanRow.Separator}{(path.Length == 0 ? "<root>" : path)}";
    }

    /// <summary>Names a connection by its id, from the connections panel, when the shell has one.</summary>
    internal Func<Guid, string?>? ConnectionName { get; set; }

    /// <summary>
    /// A timestamp in the reader's own zone, as short as it can be without being ambiguous.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The agent works in UTC and says so on the wire. Showing that unconverted is how a sync that
    /// ran ten minutes ago reads as having run two hours from now.
    /// </para>
    /// <para>
    /// 1.4 wrote every one in full ("g"), which in the history's narrow first column was cut to
    /// "02-10-2026 1…", the time being the part lost. Today's runs are the time alone, this year's
    /// leave the year out, and older ones are in full; the cell's tip has the whole date.
    /// </para>
    /// </remarks>
    internal static string Moment(DateTimeOffset value, DateTimeOffset now)
    {
        var culture = CultureInfo.CurrentCulture;
        var local = value.ToLocalTime();
        var today = now.ToLocalTime();
        if (local.Date == today.Date) return local.ToString("t", culture);
        if (local.Year != today.Year) return local.ToString("g", culture);

        var format = culture.DateTimeFormat;
        var dayAndMonth = WithoutYear(format.ShortDatePattern);
        return dayAndMonth.Length == 0
            ? local.ToString("g", culture)
            : local.ToString($"{dayAndMonth} {format.ShortTimePattern}", culture);
    }

    /// <summary>
    /// A short date pattern with its year and the separator beside it taken out: dd-MM-yyyy is
    /// dd-MM, M/d/yyyy is M/d and yyyy-MM-dd is MM-dd. Empty when what is left is not a day and a
    /// month, so the caller writes the date in full instead.
    /// </summary>
    private static string WithoutYear(string pattern)
    {
        var left = System.Text.RegularExpressions.Regex.Replace(pattern, @"[^dM]*y+[^dM]*", string.Empty).Trim();
        return left.Contains('d', StringComparison.Ordinal) && left.Contains('M', StringComparison.Ordinal)
            ? left
            : string.Empty;
    }

    /// <summary>A run id as the history list and the heading write it: its first eight characters.</summary>
    private static string ShortId(Guid syncRunId) =>
        syncRunId.ToString("N", CultureInfo.InvariantCulture)[..8];

    private bool Live => _controller is not null;

    private void RefreshCommands()
    {
        Refresh(LoadRunCommand);
        Refresh(RefreshHistoryCommand);
        Refresh(NextPageCommand);
        Refresh(RefreshStatusCommand);
        Refresh(ApproveCommand);
        Refresh(LoadMoreOperationsCommand);
        Refresh(LoadMoreConflictsCommand);
        Refresh(CopyRunIdCommand);
    }

    private static void Refresh(ICommand command) =>
        (command as RelayCommand)?.RaiseCanExecuteChanged();

    public static string RunIdLabel => Ui.Sync.RunIdLabel;

    public static string RunIdWatermark => "00000000-0000-0000-0000-000000000000";

    public static string RunIdAccessibleName => Ui.Sync.RunIdAccessibleName;

    public static string LoadRunLabel => Ui.Sync.LoadRun;

    public static string RefreshHistoryLabel => Ui.Sync.RefreshHistory;

    public static string NextPageLabel => Ui.Sync.NextPage;

    public static string RefreshStatusLabel => Ui.Sync.RefreshStatus;

    public static string ApproveLabel => Ui.Sync.ApproveAndDispatch;

    public static string ApproveAccessibleDescription => Ui.Sync.ApproveHint;

    public static string CopyRunIdLabel => Ui.Sync.CopyRunId;

    public static string PlanTabLabel => Ui.Sync.PlanTab;

    public static string ConflictsTabLabel => Ui.Sync.ConflictsTab;

    public static string LoadMoreLabel => Ui.Sync.LoadNextOperations;

    public static string LoadMoreConflictsLabel => Ui.Sync.LoadNextConflicts;

    public static string HistoryAccessibleName => Ui.Sync.RunsHistoryAccessibleName;

    public static string PlanAccessibleName => Ui.Sync.PlanOperations;

    public static string ConflictsAccessibleName => Ui.Sync.PlanConflicts;

    public static string ColumnUpdated => Ui.Sync.ColumnUpdated;

    public static string ColumnRun => Ui.Sync.ColumnRun;

    public static string ColumnPhase => Ui.Sync.ColumnPhase;

    public static string ColumnDispatch => Ui.Sync.ColumnDispatch;

    public static string ColumnConflicts => Ui.Sync.ConflictsTab;

    public static string ColumnSequence => Ui.Sync.ColumnSequence;

    public static string ColumnAction => Ui.Sync.ColumnAction;

    public static string ColumnFromLocation => Ui.Sync.ColumnFromLocation;

    public static string ColumnToLocation => Ui.Sync.ColumnToLocation;

    public static string ColumnExpectedBytes => Ui.Sync.ColumnExpectedBytes;

    public static string ColumnSafety => Ui.Sync.ColumnSafety;

    public static string ColumnPath => Ui.Sync.PickerPath;

    public static string ColumnKind => Ui.Sync.ColumnKind;

    public static string ColumnState => Ui.Sync.ColumnState;

    public static string ColumnSafeReason => Ui.Sync.ColumnSafeReason;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
