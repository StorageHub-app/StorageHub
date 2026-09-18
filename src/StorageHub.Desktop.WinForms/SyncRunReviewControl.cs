using System.Globalization;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// Reviews one immutable preview and can durably dispatch its exact revision and approval digest.
/// Dispatch is never presented as provider execution completion.
/// </summary>
public sealed class SyncRunReviewControl : UserControl
{
    private const int CompletionPollLimit = 20;
    private static readonly TimeSpan CompletionPollInterval = TimeSpan.FromMilliseconds(500);
    private readonly ISyncManagementAgentClient _client;
    private readonly bool _ownsClient;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly Label _status;
    private readonly Label _summary;
    private readonly DataGridView _planGrid;
    private readonly DataGridView _conflictGrid;
    private readonly StorageHubButton _approveButton;
    private readonly StorageHubButton _nextPlanButton;
    private readonly StorageHubButton _nextConflictButton;
    private string? _planContinuation;
    private string? _conflictContinuation;
    private bool _disposed;

    public SyncRunReviewControl()
        : this(new NamedPipeSyncManagementAgentClient(), ownsClient: true)
    {
    }

    public SyncRunReviewControl(ISyncManagementAgentClient client, bool ownsClient = false)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _ownsClient = ownsClient;
        Dock = DockStyle.Fill;
        BackColor = StorageHubTheme.Surface;
        AccessibleName = Ui.Sync.RunReviewAccessibleName;
        AccessibleDescription = Ui.Sync.RunReviewAccessibleDescription;

        var heading = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 94,
            ColumnCount = 2,
            Padding = new Padding(12, 8, 12, 6),
            BackColor = StorageHubTheme.Surface
        };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var text = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty
        };
        _summary = UiControlFactory.CreateSectionTitle(Ui.Sync.NoPlanLoaded);
        _status = UiControlFactory.CreateDescription(
            Ui.Sync.ChooseReviewAndRun);
        _status.Name = "SyncRunStatus";
        text.Controls.Add(_summary);
        text.Controls.Add(_status);
        heading.Controls.Add(text, 0, 0);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(8, 8, 0, 0)
        };
        var refresh = new StorageHubButton { Text = Ui.Sync.RefreshStatus, AutoSize = true };
        refresh.Variant = StorageHubButtonVariant.Secondary;
        refresh.Click += RefreshClicked;
        _approveButton = new StorageHubButton
        {
            Name = "ApproveAndDispatch",
            Text = Ui.Sync.ApproveAndDispatch,
            AutoSize = true,
            Enabled = false,
            AccessibleDescription = Ui.Sync.ApproveHint
        };
        _approveButton.Variant = StorageHubButtonVariant.Primary;
        _approveButton.Click += ApproveClicked;
        actions.Controls.Add(refresh);
        actions.Controls.Add(_approveButton);
        heading.Controls.Add(actions, 1, 0);

        var tabs = new ThemedTabControl
        {
            Dock = DockStyle.Fill,
            AccessibleName = Ui.Sync.PlanDetails
        };
        StorageHubTheme.ConfigureTabs(tabs);
        _planGrid = CreateGrid(Ui.Sync.PlanOperations);
        _planGrid.Columns.Add(Ui.Sync.ColumnSequence, "#");
        _planGrid.Columns.Add(Ui.Sync.ColumnAction, Ui.Sync.ColumnAction);
        _planGrid.Columns.Add("FromLocation", Ui.Sync.ColumnFromLocation);
        _planGrid.Columns.Add("ToLocation", Ui.Sync.ColumnToLocation);
        _planGrid.Columns.Add(Ui.Sync.ColumnBytes, Ui.Sync.ColumnExpectedBytes);
        _planGrid.Columns.Add(Ui.Sync.ColumnSafety, Ui.Sync.ColumnSafety);
        _nextPlanButton = CreateNextButton(Ui.Sync.LoadNextOperations, NextPlanClicked);
        tabs.TabPages.Add(CreatePagedTab(Ui.Sync.PlanTab, _planGrid, _nextPlanButton));

        _conflictGrid = CreateGrid(Ui.Sync.PlanConflicts);
        _conflictGrid.Columns.Add(Ui.Sync.PickerPath, Ui.Sync.PickerPath);
        _conflictGrid.Columns.Add(Ui.Sync.ColumnKind, Ui.Sync.ColumnKind);
        _conflictGrid.Columns.Add(Ui.Sync.ColumnState, Ui.Sync.ColumnState);
        _conflictGrid.Columns.Add(Ui.Sync.ColumnReason, Ui.Sync.ColumnSafeReason);
        _nextConflictButton = CreateNextButton(Ui.Sync.LoadNextConflicts, NextConflictClicked);
        tabs.TabPages.Add(CreatePagedTab(Ui.Sync.ConflictsTab, _conflictGrid, _nextConflictButton));

        Controls.Add(tabs);
        Controls.Add(heading);
    }

    public SyncRunSummary? CurrentRun { get; private set; }

    public string StatusText => _status.Text;

    public int LoadedOperationCount => _planGrid.Rows.Count;

    public int LoadedConflictCount => _conflictGrid.Rows.Count;

    public async Task LoadRunAsync(Guid syncRunId, CancellationToken cancellationToken = default)
    {
        if (syncRunId == Guid.Empty)
        {
            throw new ArgumentException("A sync run ID is required.", nameof(syncRunId));
        }

        await ExecuteSerializedAsync(async token =>
        {
            var response = await _client.GetRunStatusAsync(new SyncRunStatusRequest(
                SyncManagementIpcContract.CurrentVersion,
                syncRunId), token).ConfigureAwait(true);
            var run = Require(response.Run, response.Failure);
            SetRun(run, resetPages: true);
            await LoadPlanPageCoreAsync(reset: true, token).ConfigureAwait(true);
            await LoadConflictPageCoreAsync(reset: true, token).ConfigureAwait(true);
        }, cancellationToken).ConfigureAwait(true);
    }

    public async Task ShowPreviewAsync(
        SyncRunSummary run,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        await ExecuteSerializedAsync(async token =>
        {
            SetRun(run, resetPages: true);
            await LoadPlanPageCoreAsync(reset: true, token).ConfigureAwait(true);
            await LoadConflictPageCoreAsync(reset: true, token).ConfigureAwait(true);
        }, cancellationToken).ConfigureAwait(true);
    }

    public async Task RefreshStatusAsync(CancellationToken cancellationToken = default)
    {
        var runId = CurrentRun?.SyncRunId ?? throw new InvalidOperationException(Ui.Sync.NoRunLoaded);
        await ExecuteSerializedAsync(async token =>
        {
            var response = await _client.GetRunStatusAsync(new SyncRunStatusRequest(
                SyncManagementIpcContract.CurrentVersion,
                runId), token).ConfigureAwait(true);
            SetRun(Require(response.Run, response.Failure), resetPages: false);
        }, cancellationToken).ConfigureAwait(true);
    }

    /// <returns><see langword="true"/> only when the exact apply request was durably dispatched.</returns>
    public async Task<bool> ApproveAndDispatchAsync(CancellationToken cancellationToken = default)
    {
        var run = CurrentRun ?? throw new InvalidOperationException(Ui.Sync.NoRunLoaded);
        if (run.DispatchState == SyncIpcDispatchState.DurablyDispatched)
        {
            SetRun(run, resetPages: false);
            return true;
        }

        if (run.Phase != SyncIpcRunPhase.AwaitingApproval)
        {
            throw new InvalidOperationException(Ui.Sync.NotAwaitingApproval);
        }

        return await ExecuteSerializedAsync(async token =>
        {
            // These values come from the same immutable summary shown to the reviewer.
            var response = await _client.ApproveAndDispatchAsync(new SyncApproveDispatchRequest(
                SyncManagementIpcContract.CurrentVersion,
                run.SyncRunId,
                run.Revision,
                run.ApprovalSha256), token).ConfigureAwait(true);
            var approved = Require(response.Run, response.Failure);
            if (!response.DurablyDispatched ||
                approved.SyncRunId != run.SyncRunId ||
                approved.DispatchState != SyncIpcDispatchState.DurablyDispatched)
            {
                throw new InvalidDataException(Ui.Sync.DispatchNotConfirmed);
            }

            SetRun(approved, resetPages: false);
            await PollForCompletionAsync(approved.SyncRunId, token).ConfigureAwait(true);
            return true;
        }, cancellationToken).ConfigureAwait(true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _lifetime.Cancel();
            _lifetime.Dispose();
            _operationGate.Dispose();
            if (_ownsClient)
            {
                _client.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }

        base.Dispose(disposing);
    }

    private async Task LoadPlanPageCoreAsync(bool reset, CancellationToken cancellationToken)
    {
        var run = CurrentRun ?? throw new InvalidOperationException(Ui.Sync.NoRunLoaded);
        var continuation = reset ? null : _planContinuation;
        if (!reset && continuation is null)
        {
            return;
        }

        var response = await _client.GetPlanPageAsync(new SyncPlanPageRequest(
            SyncManagementIpcContract.CurrentVersion,
            run.SyncRunId,
            PageSize: SyncManagementIpcLimits.MaximumPageSize,
            continuation), cancellationToken).ConfigureAwait(true);
        ThrowIfFailure(response.Failure);
        if (response.PlanId != run.PlanId ||
            !string.Equals(response.PlanSha256, run.PlanSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(Ui.Sync.PlanPageMismatch);
        }

        if (reset)
        {
            _planGrid.Rows.Clear();
        }

        foreach (var operation in response.Operations)
        {
            _planGrid.Rows.Add(
                operation.Sequence,
                operation.Kind,
                FormatEndpoint(operation.SourceConnectionId, operation.SourcePath),
                operation.DestinationConnectionId is { } destinationId
                    ? FormatEndpoint(destinationId, operation.DestinationPath ?? string.Empty)
                    : string.Empty,
                operation.ExpectedLength?.ToString("N0", CultureInfo.CurrentCulture) ?? "—",
                operation.IsDestructive ? Ui.Sync.DestructiveApprovalRequired : Ui.Sync.Guarded);
        }

        _planContinuation = response.ContinuationToken;
        _nextPlanButton.Enabled = _planContinuation is not null;
    }

    private async Task LoadConflictPageCoreAsync(bool reset, CancellationToken cancellationToken)
    {
        var run = CurrentRun ?? throw new InvalidOperationException(Ui.Sync.NoRunLoaded);
        var continuation = reset ? null : _conflictContinuation;
        if (!reset && continuation is null)
        {
            return;
        }

        var response = await _client.GetConflictPageAsync(new SyncConflictPageRequest(
            SyncManagementIpcContract.CurrentVersion,
            run.SyncRunId,
            State: null,
            PageSize: SyncManagementIpcLimits.MaximumPageSize,
            continuation), cancellationToken).ConfigureAwait(true);
        ThrowIfFailure(response.Failure);
        if (reset)
        {
            _conflictGrid.Rows.Clear();
        }

        foreach (var conflict in response.Conflicts)
        {
            _conflictGrid.Rows.Add(
                conflict.RelativePath,
                conflict.ConflictKind,
                conflict.State,
                conflict.SafeReason);
        }

        _conflictContinuation = response.ContinuationToken;
        _nextConflictButton.Enabled = _conflictContinuation is not null;
    }

    private void SetRun(SyncRunSummary run, bool resetPages)
    {
        CurrentRun = run;
        if (resetPages)
        {
            _planContinuation = null;
            _conflictContinuation = null;
            _planGrid.Rows.Clear();
            _conflictGrid.Rows.Clear();
            _nextPlanButton.Enabled = false;
            _nextConflictButton.Enabled = false;
        }

        _summary.Text = Ui.Format(Ui.Sync.RunHeaderFormat, run.SyncRunId, run.Phase, run.Revision);
        if (run.DispatchState == SyncIpcDispatchState.DurablyDispatched)
        {
            (_status.Text, _status.ForeColor) = run.Phase switch
            {
                SyncIpcRunPhase.Completed => (Ui.Sync.PhaseCompleted, StorageHubTheme.Success),
                SyncIpcRunPhase.Failed => (Ui.Sync.PhaseFailed, StorageHubTheme.Danger),
                SyncIpcRunPhase.NeedsReconciliation => (Ui.Sync.PhaseUncertain, StorageHubTheme.Warning),
                SyncIpcRunPhase.Cancelled => (Ui.Sync.PhaseCancelled, StorageHubTheme.Warning),
                SyncIpcRunPhase.Executing => (Ui.Sync.PhaseSynchronizing, StorageHubTheme.Primary),
                SyncIpcRunPhase.Verifying => (Ui.Sync.PhaseVerifying, StorageHubTheme.Primary),
                SyncIpcRunPhase.CommittingBaseline => (Ui.Sync.PhaseCommitting, StorageHubTheme.Primary),
                _ => (Ui.Sync.PhaseQueued, StorageHubTheme.Primary)
            };
        }
        else if (run.Phase == SyncIpcRunPhase.AwaitingApproval)
        {
            _status.Text = Ui.Sync.AwaitingApproval;
            _status.ForeColor = StorageHubTheme.Warning;
        }
        else
        {
            _status.Text = Ui.Format(Ui.Sync.RunPhaseFormat, run.Phase);
            _status.ForeColor = StorageHubTheme.TextMuted;
        }

        _approveButton.Enabled =
            run.Phase == SyncIpcRunPhase.AwaitingApproval &&
            run.DispatchState == SyncIpcDispatchState.NotDispatched;
    }

    private async Task PollForCompletionAsync(Guid runId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < CompletionPollLimit; attempt++)
        {
            var response = await _client.GetRunStatusAsync(new SyncRunStatusRequest(
                SyncManagementIpcContract.CurrentVersion,
                runId), cancellationToken).ConfigureAwait(true);
            var current = Require(response.Run, response.Failure);
            SetRun(current, resetPages: false);
            if (IsTerminal(current.Phase))
            {
                return;
            }

            await Task.Delay(CompletionPollInterval, cancellationToken).ConfigureAwait(true);
        }

        _status.Text = Ui.Sync.PhaseRunning;
        _status.ForeColor = StorageHubTheme.Primary;
    }

    private static bool IsTerminal(SyncIpcRunPhase phase) => phase is
        SyncIpcRunPhase.Completed or
        SyncIpcRunPhase.Failed or
        SyncIpcRunPhase.Cancelled or
        SyncIpcRunPhase.Interrupted or
        SyncIpcRunPhase.NeedsReconciliation or
        SyncIpcRunPhase.BlockedConflict or
        SyncIpcRunPhase.BlockedDeletionGuard or
        SyncIpcRunPhase.BlockedEndpoint or
        SyncIpcRunPhase.BlockedCredential or
        SyncIpcRunPhase.BlockedTrust;

    private async Task ExecuteSerializedAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _operationGate.WaitAsync(linked.Token).ConfigureAwait(true);
        try
        {
            await action(linked.Token).ConfigureAwait(true);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<T> ExecuteSerializedAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _operationGate.WaitAsync(linked.Token).ConfigureAwait(true);
        try
        {
            return await action(linked.Token).ConfigureAwait(true);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async void RefreshClicked(object? sender, EventArgs e)
    {
        try
        {
            await RefreshStatusAsync(_lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            ShowError(error);
        }
    }

    private async void ApproveClicked(object? sender, EventArgs e)
    {
        var confirmation = MessageBox.Show(
            this,
            Ui.Dialogs.ApproveSyncPrompt,
            Ui.Dialogs.ApproveSyncCaption,
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.OK)
        {
            return;
        }

        try
        {
            await ApproveAndDispatchAsync(_lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            ShowError(error);
        }
    }

    private async void NextPlanClicked(object? sender, EventArgs e)
    {
        try
        {
            await ExecuteSerializedAsync(
                token => LoadPlanPageCoreAsync(reset: false, token),
                _lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            ShowError(error);
        }
    }

    private async void NextConflictClicked(object? sender, EventArgs e)
    {
        try
        {
            await ExecuteSerializedAsync(
                token => LoadConflictPageCoreAsync(reset: false, token),
                _lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            ShowError(error);
        }
    }

    private void ShowError(Exception error)
    {
        _status.Text = error.Message;
        _status.ForeColor = StorageHubTheme.Danger;
    }

    private static T Require<T>(T? value, StorageIpcFailure? failure) where T : class
    {
        ThrowIfFailure(failure);
        return value ?? throw new InvalidDataException(Ui.Sync.IncompleteSyncResponse);
    }

    private static void ThrowIfFailure(StorageIpcFailure? failure)
    {
        if (failure is not null)
        {
            throw new InvalidOperationException(SyncFailureMessages.Describe(failure));
        }
    }

    private static string FormatEndpoint(Guid connectionId, string path) =>
        $"{connectionId:D} · {(path.Length == 0 ? "<root>" : path)}";

    private static DataGridView CreateGrid(string accessibleName)
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BackgroundColor = StorageHubTheme.Surface,
            BorderStyle = BorderStyle.None,
            GridColor = StorageHubTheme.Border,
            AccessibleName = accessibleName
        };
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = StorageHubTheme.SurfaceMuted;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = StorageHubTheme.Text;
        return grid;
    }

    private static StorageHubButton CreateNextButton(string text, EventHandler click)
    {
        var button = new StorageHubButton
        {
            Text = text,
            Dock = DockStyle.Right,
            Width = 170,
            Enabled = false
        };
        button.Variant = StorageHubButtonVariant.Secondary;
        button.Click += click;
        return button;
    }

    private static TabPage CreatePagedTab(string name, Control content, Button next)
    {
        var page = new TabPage(name) { Padding = new Padding(4) };
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(4) };
        footer.Controls.Add(next);
        page.Controls.Add(content);
        page.Controls.Add(footer);
        return page;
    }
}
