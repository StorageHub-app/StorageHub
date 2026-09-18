using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using System.Globalization;

namespace StorageHub.Desktop;

/// <summary>Builds a bounded, non-secret activity log from durable Agent transfer and sync records.</summary>
public sealed class ActivityLogControl : UserControl
{
    /// <summary>
    /// Desktop-local drags that have not yet become durable work. Optional: the log renders durable
    /// activity correctly without it.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public PendingDropRegistry? PendingDrops { get; set; }

    private readonly ITransferQueueAgentClient _transferClient;
    private readonly ISyncManagementAgentClient _syncClient;
    private readonly bool _ownsClients;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DataGridView _grid;
    private readonly Label _status;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private int _refreshing;
    private bool _disposed;

    public ActivityLogControl()
        : this(new NamedPipeTransferQueueAgentClient(), new NamedPipeSyncManagementAgentClient(), ownsClients: true)
    {
    }

    public ActivityLogControl(
        ITransferQueueAgentClient transferClient,
        ISyncManagementAgentClient syncClient,
        bool ownsClients = false)
    {
        _transferClient = transferClient ?? throw new ArgumentNullException(nameof(transferClient));
        _syncClient = syncClient ?? throw new ArgumentNullException(nameof(syncClient));
        _ownsClients = ownsClients;
        Dock = DockStyle.Fill;
        BackColor = StorageHubTheme.Surface;
        AccessibleName = Ui.Transfer.ActivityLog;
        AccessibleDescription = Ui.Transfer.ActivityLogAccessibleDescription;

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            Padding = new Padding(8, 6, 8, 4),
            WrapContents = false,
            BackColor = StorageHubTheme.Surface
        };
        var refresh = new StorageHubButton { Text = Ui.Transfer.ActivityRefresh, AutoSize = true };
        refresh.Variant = StorageHubButtonVariant.Secondary;
        refresh.Click += RefreshClicked;
        _status = new Label
        {
            Text = Ui.Transfer.ActivityNotLoaded,
            AutoSize = true,
            Margin = new Padding(12, 8, 0, 0),
            ForeColor = StorageHubTheme.TextMuted,
            AccessibleDescription = Ui.Transfer.ActivityStatusAccessibleDescription
        };
        toolbar.Controls.Add(refresh);
        toolbar.Controls.Add(_status);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            BackgroundColor = StorageHubTheme.Surface,
            BorderStyle = BorderStyle.None,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AccessibleName = Ui.Transfer.ActivityGridAccessibleName
        };
        StorageHubTheme.ReduceFlicker(_grid);
        _grid.Columns.Add("Updated", Ui.Transfer.ActivityColumnUpdated);
        _grid.Columns.Add("Area", Ui.Transfer.ActivityColumnArea);
        _grid.Columns.Add("Item", Ui.Transfer.ActivityColumnItem);
        _grid.Columns.Add("State", Ui.Transfer.ActivityColumnState);
        _grid.Columns.Add("Details", Ui.Transfer.ActivityColumnDetails);
        _grid.Columns[0].FillWeight = 55;
        _grid.Columns[1].FillWeight = 38;
        _grid.Columns[2].FillWeight = 90;
        _grid.Columns[3].FillWeight = 55;
        _grid.Columns[4].FillWeight = 130;

        Controls.Add(_grid);
        Controls.Add(toolbar);
        _pollTimer = new System.Windows.Forms.Timer { Interval = 5_000 };
        _pollTimer.Tick += PollTimerTick;
    }

    public int DisplayedEntryCount => _grid.Rows.Count;

    public string StatusText => _status.Text;

    /// <summary>
    /// Identity of each displayed row. Exposed so a test can prove a refresh reuses the existing
    /// rows rather than clearing and rebuilding them, which is what caused the visible blink.
    /// </summary>
    internal int[] RowIdentities() =>
        [.. _grid.Rows.Cast<DataGridViewRow>().Select(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode)];

    public Task RefreshActivityAsync(CancellationToken cancellationToken = default) =>
        RefreshCoreAsync(cancellationToken);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdatePollingState();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _pollTimer.Stop();
        base.OnHandleDestroyed(e);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        UpdatePollingState();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _pollTimer.Stop();
            _pollTimer.Tick -= PollTimerTick;
            _pollTimer.Dispose();
            _lifetime.Cancel();
            _lifetime.Dispose();
            if (_ownsClients)
            {
                _transferClient.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _syncClient.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }

        base.Dispose(disposing);
    }

    private async void RefreshClicked(object? sender, EventArgs e) =>
        await RefreshCoreAsync(_lifetime.Token).ConfigureAwait(true);

    private async void PollTimerTick(object? sender, EventArgs e) =>
        await RefreshCoreAsync(_lifetime.Token, background: true).ConfigureAwait(true);

    private async Task RefreshCoreAsync(CancellationToken cancellationToken, bool background = false)
    {
        if (_disposed || Interlocked.Exchange(ref _refreshing, 1) != 0)
        {
            return;
        }

        // A timed poll stays silent. Announcing "refreshing" on every tick made the status
        // line flicker between two strings for work the user never asked for.
        if (!background)
        {
            _status.Text = Ui.Transfer.ActivityRefreshing;
            _status.ForeColor = StorageHubTheme.TextMuted;
        }
        try
        {
            var transfersTask = _transferClient.ListAsync(new TransferListRequest(
                TransferQueueIpcContract.CurrentVersion,
                Enum.GetValues<TransferQueueState>(),
                TransferQueueIpcLimits.MaximumPageSize), cancellationToken);
            var runsTask = _syncClient.ListRunsAsync(new SyncRunListRequest(
                PageSize: SyncManagementIpcLimits.MaximumPageSize), cancellationToken);
            await Task.WhenAll(transfersTask, runsTask).ConfigureAwait(true);
            var transfers = await transfersTask.ConfigureAwait(true);
            var runs = await runsTask.ConfigureAwait(true);
            if (transfers.Failure is not null)
            {
                throw new InvalidOperationException(transfers.Failure.Message);
            }

            if (runs.Failure is not null)
            {
                throw new InvalidOperationException(runs.Failure.Message);
            }

            var pending = PendingDrops?.Snapshot() ?? [];
            var entries = transfers.Transfers.Select(ActivityEntry.FromTransfer)
                .Concat(runs.Runs.Select(ActivityEntry.FromRun))
                .Concat(pending.Select(ActivityEntry.FromPendingDrop))
                .OrderByDescending(static entry => entry.UpdatedUtc)
                .ThenBy(static entry => entry.Area, StringComparer.Ordinal)
                .Take(100)
                .ToArray();
            // Clearing and rebuilding made every row blink out and back on each poll. Rows are
            // reconciled in place, so unchanged text is never rewritten and the selection and
            // scroll position survive a refresh.
            PopulateActivity(entries);
            _status.Text = entries.Length == 0
                ? Ui.Transfer.ActivityNone
                : pending.Count == 0
                    ? Ui.Format(Ui.Transfer.ActivityShowingFormat, entries.Length)
                    : Ui.Format(Ui.Transfer.ActivityShowingPendingFormat, entries.Length, pending.Count);
            _status.ForeColor = StorageHubTheme.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            _status.Text = DesktopAgentAvailability.ReportFailure(error);
            _status.ForeColor = StorageHubTheme.Danger;
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    private void UpdatePollingState()
    {
        var shouldPoll = !_disposed && IsHandleCreated && Visible && FindForm()?.Visible == true;
        if (!shouldPoll)
        {
            _pollTimer.Stop();
            return;
        }

        if (!_pollTimer.Enabled)
        {
            _pollTimer.Start();
            _ = RefreshCoreAsync(_lifetime.Token);
        }
    }

    /// <summary>
    /// Reconciles the grid against the newest page without clearing it. A row is only written when
    /// a cell actually differs, so a poll that changes nothing repaints nothing.
    /// </summary>
    private void PopulateActivity(ActivityEntry[] entries)
    {
        var wasEmpty = _grid.Rows.Count == 0;

        while (_grid.Rows.Count > entries.Length)
        {
            _grid.Rows.RemoveAt(_grid.Rows.Count - 1);
        }

        while (_grid.Rows.Count < entries.Length)
        {
            _grid.Rows.Add();
        }

        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var row = _grid.Rows[index];
            SetCell(row, 0, entry.UpdatedUtc.LocalDateTime.ToString("g", CultureInfo.CurrentCulture));
            SetCell(row, 1, entry.Area);
            SetCell(row, 2, entry.Item);
            SetCell(row, 3, entry.State);
            SetCell(row, 4, entry.Details);
        }

        if (wasEmpty)
        {
            _grid.ClearSelection();
        }
    }

    private static void SetCell(DataGridViewRow row, int index, string value)
    {
        var cell = row.Cells[index];
        if (!string.Equals(cell.Value as string, value, StringComparison.Ordinal))
        {
            cell.Value = value;
        }
    }

    private sealed record ActivityEntry(
        DateTimeOffset UpdatedUtc,
        string Area,
        string Item,
        string State,
        string Details)
    {
        public static ActivityEntry FromTransfer(TransferQueueSummary transfer) => new(
            transfer.UpdatedUtc,
            Ui.Transfer.ActivityAreaTransfer,
            transfer.TransferId.ToString("N")[..8],
            UiEnumNames.Describe(transfer.State),
            transfer.ErrorSummary ?? Ui.Format(
                Ui.Transfer.ActivityTransferDetailsFormat,
                transfer.Operation,
                DisplayPath(transfer.SourcePath),
                DisplayPath(transfer.DestinationPath)));

        /// <summary>
        /// A drag that has left a pane but has no destination yet. It is shown under its own area so
        /// it reads as pending rather than as a durable record the agent has committed to.
        /// </summary>
        public static ActivityEntry FromPendingDrop(PendingDropEntry drop) => new(
            drop.UpdatedUtc,
            Ui.Transfer.ActivityAreaDrop,
            drop.Token[..8],
            drop.Describe(),
            drop.Destination is null
                ? Ui.Format(Ui.Transfer.ActivityDropToExplorerFormat, drop.DescribeSource())
                : Ui.Format(Ui.Transfer.ActivityDropToDestinationFormat, drop.DescribeSource(), drop.Destination));

        public static ActivityEntry FromRun(SyncRunSummary run) => new(
            run.UpdatedUtc,
            Ui.Transfer.ActivityAreaSync,
            run.SyncRunId.ToString("N")[..8],
            UiEnumNames.Describe(run.Phase),
            run.ConflictCount == 0
                ? Ui.Format(Ui.Transfer.ActivitySyncGenerationFormat, run.Generation, run.DispatchState)
                : Ui.Format(Ui.Transfer.ActivitySyncConflictsFormat, run.Generation, run.ConflictCount));

        private static string DisplayPath(string value) => value.Length == 0 ? "/" : value;
    }
}
