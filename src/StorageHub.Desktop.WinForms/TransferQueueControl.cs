using System.ComponentModel;
using System.Globalization;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// Live, optimistic-concurrency queue surface backed by the durable agent. The control remains
/// inert until it is visible in a shown form, so constructing the desktop never opens IPC.
/// </summary>
public sealed class TransferQueueControl : UserControl
{
    private ActivityLogControl? _activityLog;
    private PendingDropRegistry? _pendingDrops;

    private const int ActivePollIntervalMilliseconds = 500;
    private const int IdlePollIntervalMilliseconds = 2_000;

    private static readonly QueueTabDefinition[] QueueTabs =
    [
        new(TabKeys.Active, static strings => strings.TabActive, UiGlyph.Run,
        [
            TransferQueueState.Preparing,
            TransferQueueState.Connecting,
            TransferQueueState.Transferring,
            TransferQueueState.Verifying,
            TransferQueueState.Finalizing,
            TransferQueueState.CleanupPending
        ]),
        new(TabKeys.Queued, static strings => strings.TabQueued, UiGlyph.More,
            [TransferQueueState.Pending, TransferQueueState.Retrying]),
        new(TabKeys.Paused, static strings => strings.TabPaused, UiGlyph.Pause,
        [
            TransferQueueState.Paused,
            TransferQueueState.BlockedCredential,
            TransferQueueState.BlockedTrust,
            TransferQueueState.RestartRequired
        ]),
        new(TabKeys.Failed, static strings => strings.TabFailed, UiGlyph.Warning,
            [TransferQueueState.Failed]),
        new(TabKeys.Completed, static strings => strings.TabCompleted, UiGlyph.Test,
            [TransferQueueState.Completed, TransferQueueState.Cancelled]),
        new(TabKeys.Conflicts, static strings => strings.TabConflicts, UiGlyph.Compare,
            [TransferQueueState.Interrupted, TransferQueueState.NeedsReconciliation])
    ];

    /// <summary>
    /// The identity of each queue tab.
    /// </summary>
    /// <remarks>
    /// Deliberately not translated. These are the TabPage names and the ImageList keys, and one of
    /// them is compared to decide which tab polls continuously, so they have to stay put while the
    /// visible label follows the language.
    /// </remarks>
    private static class TabKeys
    {
        internal const string Active = "Active";
        internal const string Queued = "Queued";
        internal const string Paused = "Paused";
        internal const string Failed = "Failed";
        internal const string Completed = "Completed";
        internal const string Conflicts = "Conflicts";
        internal const string Logs = "Logs";
    }

    private readonly ITransferQueueAgentClient _client;
    private readonly bool _ownsClient;
    private readonly DesktopConfigStore? _preferencesStore;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly System.Windows.Forms.Timer _pollTimer;
    private readonly TabControl _tabs;
    private readonly ImageList _tabImages;
    private readonly List<Image> _ownedImages = [];
    private readonly ToolStripButton _cancelButton;
    private readonly ToolStripButton _retryButton;
    private readonly ToolStripButton _reconcileButton;
    private readonly ToolStripButton _nextButton;
    private readonly StorageHubToolStripChoice _reconcileAction;
    private readonly ToolStripLabel _status;
    private readonly Dictionary<TabPage, QueueTabDefinition> _definitions = [];
    private readonly Dictionary<TabPage, DataGridView> _grids = [];
    private string? _pageCursor;
    private string? _nextCursor;
    private int _refreshing;
    private bool _disposed;

    public TransferQueueControl()
        : this(new NamedPipeTransferQueueAgentClient(), ownsClient: true, preferencesStore: null)
    {
    }

    public TransferQueueControl(ITransferQueueAgentClient client, bool ownsClient = false)
        : this(client, ownsClient, preferencesStore: null)
    {
    }

    internal TransferQueueControl(DesktopConfigStore preferencesStore)
        : this(new NamedPipeTransferQueueAgentClient(), ownsClient: true, preferencesStore)
    {
    }

    private TransferQueueControl(
        ITransferQueueAgentClient client,
        bool ownsClient,
        DesktopConfigStore? preferencesStore)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _ownsClient = ownsClient;
        _preferencesStore = preferencesStore;
        Dock = DockStyle.Fill;
        BackColor = StorageHubTheme.Surface;
        AccessibleName = Ui.Transfer.QueueTitle;
        AccessibleDescription = Ui.Transfer.QueueDescription;

        var toolbar = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            BackColor = StorageHubTheme.Surface,
            ForeColor = StorageHubTheme.Text,
            AccessibleName = Ui.Transfer.QueueCommands,
            Padding = new Padding(6, 4, 6, 4),
            ImageScalingSize = new Size(18, 18),
            AutoSize = true
        };
        var refresh = CreateButton(UiGlyph.Refresh, Ui.Transfer.Refresh, Ui.Transfer.RefreshQueue, RefreshButtonClicked);
        _cancelButton = CreateButton(UiGlyph.Delete, Ui.Transfer.Cancel, Ui.Transfer.CancelSelected, CancelButtonClicked);
        _retryButton = CreateButton(UiGlyph.Run, Ui.Transfer.Retry, Ui.Transfer.RetrySelected, RetryButtonClicked);
        _reconcileButton = CreateButton(UiGlyph.Test, Ui.Transfer.Apply, Ui.Transfer.ApplyReconciliation, ReconcileButtonClicked);
        _nextButton = CreateButton(UiGlyph.Forward, Ui.Transfer.Next, Ui.Transfer.NextPage, NextButtonClicked);
        _reconcileAction = new StorageHubToolStripChoice
        {
            Name = "ReconciliationAction",
            AccessibleName = Ui.Transfer.ReconciliationAction,
            AutoSize = false,
            Width = 170
        };
        // Typed values captioned by a function, not names: the list has to read as words, and the
        // selection has to come back as the value the request carries rather than a parsed string.
        _reconcileAction.Field.DisplayText = static item => item is TransferReconciliationAction action
            ? UiEnumNames.Describe(action)
            : item.ToString() ?? string.Empty;
        _reconcileAction.Field.Items.AddRange(
            Enum.GetValues<TransferReconciliationAction>().Cast<object>().ToArray());
        _reconcileAction.SelectedItem = TransferReconciliationAction.Review;
        _status = new ToolStripLabel(Ui.Transfer.QueueDeferred)
        {
            Alignment = ToolStripItemAlignment.Right,
            ForeColor = StorageHubTheme.TextMuted,
            AccessibleDescription = Ui.Transfer.QueueStatusAccessibleName
        };
        toolbar.Items.Add(refresh);
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(_cancelButton);
        toolbar.Items.Add(_retryButton);
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(new ToolStripLabel(Ui.Transfer.ReconcileLabel)
        {
            ForeColor = StorageHubTheme.TextMuted
        });
        toolbar.Items.Add(_reconcileAction);
        toolbar.Items.Add(_reconcileButton);
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(_nextButton);
        toolbar.Items.Add(_status);

        _tabImages = CreateTabImages();
        _tabs = new ThemedTabControl
        {
            Dock = DockStyle.Fill,
            AccessibleName = Ui.Transfer.QueueViews,
            ImageList = _tabImages,
            SizeMode = TabSizeMode.Fixed
        };
        foreach (var definition in QueueTabs)
        {
            AddQueueTab(definition);
        }

        AddLogsTab();
        StorageHubTheme.ConfigureTabs(_tabs);
        ConfigureTabSize();
        _tabs.SelectedIndex = 0;
        _tabs.SelectedIndexChanged += SelectedTabChanged;
        Controls.Add(_tabs);
        Controls.Add(toolbar);

        _pollTimer = new System.Windows.Forms.Timer { Interval = IdlePollIntervalMilliseconds };
        _pollTimer.Tick += PollTimerTick;
        UpdateActionState();
    }

    /// <summary>Refreshes the selected transfer view. Public for host commands and UI tests.</summary>
    public Task RefreshQueueAsync(CancellationToken cancellationToken = default) =>
        RefreshQueueCoreAsync(resetPage: true, cancellationToken);

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
            _tabs.SelectedIndexChanged -= SelectedTabChanged;
            if (_pendingDrops is not null)
            {
                _pendingDrops.Changed -= PendingDropsChanged;
            }
            _lifetime.Cancel();
            _lifetime.Dispose();
            if (_ownsClient)
            {
                _client.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            _pollTimer.Dispose();
        }

        base.Dispose(disposing);
        if (disposing)
        {
            foreach (var image in _ownedImages)
            {
                image.Dispose();
            }
            _ownedImages.Clear();
            _tabImages.Dispose();
        }
    }

    private void AddQueueTab(QueueTabDefinition definition)
    {
        var page = new TabPage(Ui.Format(Ui.Transfer.TabCountFormat, definition.Text, 0))
        {
            Name = definition.Key,
            AccessibleName = definition.Text,
            ImageKey = definition.Key
        };
        var grid = CreateGrid(definition.Text);
        ConfigureHistoryMenu(grid);
        grid.SelectionChanged += (_, _) => UpdateActionState();
        page.Controls.Add(grid);
        _definitions.Add(page, definition);
        _grids.Add(page, grid);
        _tabs.TabPages.Add(page);
    }

    private void AddLogsTab()
    {
        var page = new TabPage(Ui.Transfer.TabLogs)
        {
            AccessibleName = Ui.Transfer.ActivityLog,
            ImageKey = TabKeys.Logs
        };
        _activityLog = new ActivityLogControl();
        page.Controls.Add(_activityLog);
        _tabs.TabPages.Add(page);
    }

    private static DataGridView CreateGrid(string name)
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
            BackgroundColor = StorageHubTheme.Surface,
            GridColor = StorageHubTheme.Border,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
            AccessibleName = $"{name} transfer jobs",
            AccessibleDescription = Ui.Transfer.GridAccessible
        };
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = StorageHubTheme.SurfaceMuted;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = StorageHubTheme.Text;
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4);
        grid.DefaultCellStyle.BackColor = StorageHubTheme.Surface;
        grid.DefaultCellStyle.ForeColor = StorageHubTheme.Text;
        grid.DefaultCellStyle.SelectionBackColor = StorageHubTheme.Selection;
        grid.DefaultCellStyle.SelectionForeColor = StorageHubTheme.Text;
        StorageHubTheme.ReduceFlicker(grid);
        grid.Columns.Add("Operation", Ui.Transfer.ColumnOperation);
        grid.Columns.Add("Source", Ui.Transfer.ColumnSource);
        grid.Columns.Add("Destination", Ui.Transfer.ColumnDestination);
        grid.Columns.Add(new TransferProgressColumn { Name = "Progress", HeaderText = Ui.Transfer.ColumnProgress });
        grid.Columns.Add("Attempt", Ui.Transfer.ColumnAttempt);
        grid.Columns.Add("Status", Ui.Transfer.ColumnStatus);
        grid.Columns[0].FillWeight = 55;
        grid.Columns[3].FillWeight = 65;
        grid.Columns[4].FillWeight = 45;
        return grid;
    }

    private void ConfigureHistoryMenu(DataGridView grid)
    {
        var menu = new ContextMenuStrip
        {
            Renderer = DesktopAppearanceService.MenuRenderer,
            AccessibleName = Ui.Transfer.HistoryCommands
        };
        var clearSelected = new ToolStripMenuItem(Ui.Transfer.ClearSelectedHistory);
        var clearAll = new ToolStripMenuItem(Ui.Transfer.ClearAllHistory);
        clearSelected.Click += async (_, _) => await ClearSelectedHistoryAsync(grid).ConfigureAwait(true);
        clearAll.Click += async (_, _) => await ClearAllHistoryAsync().ConfigureAwait(true);
        menu.Items.Add(clearSelected);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(clearAll);
        menu.Opening += (_, _) => clearSelected.Enabled = grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(row => row.Tag)
            .OfType<TransferQueueSummary>()
            .Any(IsHistoryState);
        grid.ContextMenuStrip = menu;
    }

    private async Task ClearSelectedHistoryAsync(DataGridView grid)
    {
        var ids = grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(row => row.Tag)
            .OfType<TransferQueueSummary>()
            .Where(IsHistoryState)
            .Select(transfer => transfer.TransferId)
            .Distinct()
            .Take(TransferQueueIpcLimits.MaximumPageSize)
            .ToArray();
        if (ids.Length == 0) return;
        await ClearHistoryAsync(new TransferHistoryClearRequest(
            TransferQueueIpcContract.CurrentVersion, ids, ClearAll: false)).ConfigureAwait(true);
    }

    private async Task ClearAllHistoryAsync()
    {
        var preferences = _preferencesStore?.Load() ?? DesktopUpdatePreferences.Defaults;
        if (preferences.ConfirmBeforeClearingTransferHistory)
        {
            using var confirmation = new ClearTransferHistoryConfirmationForm();
            if (confirmation.ShowDialog(FindForm()) != DialogResult.OK) return;
            if (confirmation.DontShowAgain && _preferencesStore is not null)
            {
                try { _preferencesStore.Save(preferences with { ConfirmBeforeClearingTransferHistory = false }); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    _status.Text = Ui.Transfer.WarningPreferenceFailed;
                }
            }
        }
        await ClearHistoryAsync(new TransferHistoryClearRequest(
            TransferQueueIpcContract.CurrentVersion, [], ClearAll: true)).ConfigureAwait(true);
    }

    private async Task ClearHistoryAsync(TransferHistoryClearRequest request)
    {
        SetBusy(true, Ui.Transfer.ClearingHistory);
        try
        {
            var response = await _client.ClearHistoryAsync(request, _lifetime.Token).ConfigureAwait(true);
            _status.Text = response.Failure is null
                ? response.ClearedCount == 0 ? Ui.Transfer.NoHistoryToClear : Ui.Format(Ui.Transfer.ClearedHistoryFormat, response.ClearedCount)
                : response.Failure.Message;
            if (response.Failure is null)
                await RefreshQueueCoreAsync(resetPage: true, _lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { SetUnavailable(error); }
        finally { SetBusy(false); }
    }

    private static bool IsHistoryState(TransferQueueSummary transfer) => transfer.State is
        TransferQueueState.Completed or TransferQueueState.Cancelled or TransferQueueState.Failed;

    private ToolStripButton CreateButton(
        UiGlyph glyph,
        string text,
        string description,
        EventHandler handler)
    {
        var image = UiIconFactory.Create(glyph, StorageHubTheme.Text, 18, DeviceDpi / 96F);
        _ownedImages.Add(image);
        var button = new ToolStripButton(text)
        {
            Image = image,
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            ImageAlign = ContentAlignment.MiddleLeft,
            TextAlign = ContentAlignment.MiddleRight,
            Padding = new Padding(4, 1, 4, 1),
            ToolTipText = description,
            AccessibleName = description,
            AccessibleDescription = description,
            AutoToolTip = true
        };
        button.Click += handler;
        return button;
    }

    private static ImageList CreateTabImages()
    {
        var images = new ImageList
        {
            ColorDepth = ColorDepth.Depth32Bit,
            ImageSize = new Size(18, 18),
            TransparentColor = Color.Transparent
        };
        images.Images.Add("Active", UiIconFactory.Create(UiGlyph.Run, StorageHubTheme.Success, 18));
        images.Images.Add("Queued", UiIconFactory.Create(UiGlyph.More, StorageHubTheme.Primary, 18));
        images.Images.Add("Paused", UiIconFactory.Create(UiGlyph.Pause, StorageHubTheme.Warning, 18));
        images.Images.Add("Failed", UiIconFactory.Create(UiGlyph.Warning, StorageHubTheme.Danger, 18));
        images.Images.Add("Completed", UiIconFactory.Create(UiGlyph.Test, StorageHubTheme.Success, 18));
        images.Images.Add("Conflicts", UiIconFactory.Create(UiGlyph.Compare, StorageHubTheme.Warning, 18));
        images.Images.Add("Logs", UiIconFactory.Create(UiGlyph.File, StorageHubTheme.TextMuted, 18));
        return images;
    }

    private void ConfigureTabSize()
    {
        var widestText = _tabs.TabPages.Cast<TabPage>()
            .Select(page => TextRenderer.MeasureText(
                page.Text,
                _tabs.Font,
                Size.Empty,
                TextFormatFlags.NoPadding).Width)
            .DefaultIfEmpty(70)
            .Max();
        _tabs.ItemSize = new Size(
            widestText + _tabImages.ImageSize.Width + 36,
            Math.Max(34, _tabImages.ImageSize.Height + 12));
    }

    private async void RefreshButtonClicked(object? sender, EventArgs e) =>
        await RefreshQueueCoreAsync(resetPage: true, _lifetime.Token).ConfigureAwait(true);

    private async void NextButtonClicked(object? sender, EventArgs e)
    {
        if (_nextCursor is null)
        {
            return;
        }

        _pageCursor = _nextCursor;
        await RefreshQueueCoreAsync(resetPage: false, _lifetime.Token).ConfigureAwait(true);
    }

    private async void CancelButtonClicked(object? sender, EventArgs e)
    {
        // A folder still being read is not a durable job the agent can cancel, so it is stopped
        // where it is running. The files it already queued are real work and stay.
        foreach (var token in SelectedGatheringTokens())
        {
            _pendingDrops?.RequestCancel(token);
        }

        await ApplySelectedAsync(
            static (client, transfer, token) => client.CancelAsync(
                new TransferCancelRequest(
                    TransferQueueIpcContract.CurrentVersion,
                    transfer.TransferId,
                    transfer.Revision),
                token)).ConfigureAwait(true);
    }

    private string[] SelectedGatheringTokens()
    {
        if (_tabs.SelectedTab is null || !_grids.TryGetValue(_tabs.SelectedTab, out var grid))
        {
            return [];
        }

        return grid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(static row => row.Tag)
            .OfType<PendingDropEntry>()
            .Where(static drop => drop.State == PendingDropState.Gathering)
            .Select(static drop => drop.Token)
            .ToArray();
    }

    private async void RetryButtonClicked(object? sender, EventArgs e) =>
        await ApplySelectedAsync(
            static (client, transfer, token) => client.RetryAsync(
                new TransferRetryRequest(
                    TransferQueueIpcContract.CurrentVersion,
                    transfer.TransferId,
                    transfer.Revision),
                token)).ConfigureAwait(true);

    private async void ReconcileButtonClicked(object? sender, EventArgs e)
    {
        if (_reconcileAction.SelectedItem is not TransferReconciliationAction action)
        {
            return;
        }

        await ApplySelectedAsync(
            (client, transfer, token) => client.ReconcileAsync(
                new TransferReconcileRequest(
                    TransferQueueIpcContract.CurrentVersion,
                    transfer.TransferId,
                    transfer.Revision,
                    action),
                token)).ConfigureAwait(true);
    }

    private async Task ApplySelectedAsync(
        Func<ITransferQueueAgentClient, TransferQueueSummary, CancellationToken, Task<TransferMutationResponse>> action)
    {
        if (!TryGetSelectedTransfers(out var selected) || selected.Count == 0)
        {
            return;
        }

        SetBusy(true, Ui.Transfer.ApplyingAction);
        try
        {
            var applied = 0;
            var conflicts = 0;
            foreach (var transfer in selected)
            {
                var response = await action(_client, transfer, _lifetime.Token).ConfigureAwait(true);
                if (response.Outcome is TransferQueueMutationOutcome.Applied or
                    TransferQueueMutationOutcome.Accepted)
                {
                    applied++;
                }
                else
                {
                    conflicts++;
                }
            }

            _status.Text = conflicts == 0
                ? Ui.Format(Ui.Transfer.UpdatedTransfersFormat, applied)
                : Ui.Format(Ui.Transfer.UpdatedWithConflictsFormat, applied, conflicts);
            await RefreshQueueCoreAsync(resetPage: true, _lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The control is closing.
        }
        catch (Exception error)
        {
            SetUnavailable(error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void PollTimerTick(object? sender, EventArgs e) =>
        await RefreshQueueCoreAsync(resetPage: false, _lifetime.Token, background: true).ConfigureAwait(true);

    private async Task RefreshQueueCoreAsync(
        bool resetPage,
        CancellationToken cancellationToken,
        bool background = false)
    {
        var selectedTab = _tabs.SelectedTab;
        if (_disposed || selectedTab is null || !_definitions.TryGetValue(selectedTab, out var definition) ||
            Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
        {
            return;
        }

        if (resetPage)
        {
            _pageCursor = null;
        }

        // A background poll must stay invisible: showing a wait cursor and a "refreshing" status
        // every couple of seconds made an in-flight transfer look stalled rather than live.
        if (!background)
        {
            SetBusy(true, Ui.Transfer.Refreshing);
        }

        try
        {
            var response = await _client.ListAsync(
                new TransferListRequest(
                    TransferQueueIpcContract.CurrentVersion,
                    definition.States,
                    PageSize: 25,
                    ContinuationToken: _pageCursor),
                cancellationToken).ConfigureAwait(true);
            if (response.Failure is not null)
            {
                SetUnavailable();
                return;
            }

            PopulateGrid(_grids[selectedTab], ComposeRows(definition, response.Transfers));
            AdjustPollInterval(response.Transfers);
            UpdateTabCounters(response, definition);
            PublishQueueCounts(response);
            ConfigureTabSize();
            _nextCursor = response.ContinuationToken;
            _nextButton.Enabled = _nextCursor is not null;
            _status.Text = response.Transfers.Length == 0
                ? EmptyStatus(definition)
                : Ui.Format(Ui.Transfer.TransferCountFormat, response.Transfers.Length);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A closing or superseded control does not need a UI error.
        }
        catch (Exception error)
        {
            SetUnavailable(error);
        }
        finally
        {
            _ = Interlocked.Exchange(ref _refreshing, 0);
            if (background)
            {
                UpdateActionState();
            }
            else
            {
                SetBusy(false);
            }
        }
    }

    /// <summary>
    /// Reconciles the grid against the latest page in place. Clearing and rebuilding the rows on
    /// every poll discarded the user's selection and made the progress bars restart their paint
    /// each tick, so rows are matched by transfer id and only the changed cells are written.
    /// </summary>
    /// <summary>
    /// Projects a durable transfer into grid cells. Provisional drag entries project into the same
    /// shape, which is what lets both share one diffing pass without the grid knowing the
    /// difference.
    /// </summary>
    internal static QueueRow ToRow(TransferQueueSummary transfer) => new(
        transfer.TransferId.ToString("N"),
        UiEnumNames.Describe(transfer.Operation),
        FormatEndpoint(transfer.SourceConnectionId, transfer.SourcePath),
        FormatEndpoint(transfer.DestinationConnectionId, transfer.DestinationPath),
        FormatProgress(transfer.ProgressBytes, transfer.ExpectedBytes),
        transfer.Attempt.ToString(CultureInfo.CurrentCulture),
        FormatStatus(transfer),
        ProgressFraction(transfer),
        transfer);

    internal static QueueRow ToRow(PendingDropEntry drop) => new(
        "drop:" + drop.Token,
        "Copy",
        drop.DescribeSource(),
        drop.Destination ?? Ui.Transfer.FileExplorerSource,
        // No byte total exists yet: for a drag out the destination is still unknown, and for a
        // folder being read the total is exactly what the reading is there to discover.
        drop.State is PendingDropState.AwaitingDestination or PendingDropState.Gathering
            ? "Pending"
            : "-",
        "-",
        drop.Describe(),
        Fraction: null,
        drop);

    private static void PopulateGrid(DataGridView grid, IEnumerable<QueueRow> rows)
    {
        var ordered = rows as IList<QueueRow> ?? rows.ToList();
        var wasEmpty = grid.Rows.Count == 0;

        while (grid.Rows.Count > ordered.Count)
        {
            grid.Rows.RemoveAt(grid.Rows.Count - 1);
        }

        while (grid.Rows.Count < ordered.Count)
        {
            grid.Rows.Add();
        }

        for (var index = 0; index < ordered.Count; index++)
        {
            var source = ordered[index];
            var row = grid.Rows[index];
            SetCell(row, 0, source.Operation);
            SetCell(row, 1, source.Source);
            SetCell(row, 2, source.Destination);
            SetCell(row, 3, source.Progress);
            SetCell(row, 4, source.Attempt);
            SetCell(row, 5, source.Status);
            row.Cells[3].Style.Tag = source.Fraction;
            row.Tag = source.Payload;
        }

        if (wasEmpty)
        {
            grid.ClearSelection();
        }
    }

    private static void SetCell(DataGridViewRow row, int index, object? value)
    {
        var cell = row.Cells[index];
        if (!Equals(cell.Value, value))
        {
            cell.Value = value;
        }
    }

    /// <summary>
    /// The completed fraction to paint, or null when the total is unknown and only a byte count
    /// can be shown. A finished transfer always paints full so a rounded percentage cannot leave
    /// a completed row looking short.
    /// </summary>
    internal static double? ProgressFraction(TransferQueueSummary transfer)
    {
        if (transfer.State is TransferQueueState.Completed)
        {
            return 1D;
        }

        if (transfer.ExpectedBytes is not { } expected || expected <= 0)
        {
            return null;
        }

        return Math.Clamp(transfer.ProgressBytes / (double)expected, 0D, 1D);
    }

    private static string FormatEndpoint(Guid connectionId, string path) =>
        $"{connectionId.ToString("N")[..8]} · {(path.Length == 0 ? "/" : path)}";

    private static string EmptyStatus(QueueTabDefinition definition) => definition.Key switch
    {
        TabKeys.Conflicts => Ui.Transfer.NoReconciliationNeeded,
        _ => Ui.Transfer.NoTransfers
    };

    private static string FormatProgress(long progress, long? expected) => expected switch
    {
        > 0 => $"{Math.Min(100D, progress * 100D / expected.Value):0.#}%",
        0 => "100%",
        _ => FormatBytes(progress)
    };

    internal static string FormatQueueTabTitle(string name, IReadOnlyCollection<TransferQueueSummary> transfers)
    {
        if (transfers.Count == 0) return name;
        var knownTotal = transfers.Where(static transfer => transfer.ExpectedBytes.HasValue)
            .Sum(static transfer => transfer.ExpectedBytes!.Value);
        var progress = transfers.Sum(static transfer => transfer.ProgressBytes);
        var size = knownTotal > 0
            ? name == "Active" && progress < knownTotal
                ? $"{FormatBytes(progress)}/{FormatBytes(knownTotal)}"
                : FormatBytes(knownTotal)
            : progress > 0 ? FormatBytes(progress) : null;
        return size is null ? $"{name} · {transfers.Count}" : $"{name} · {transfers.Count} · {size}";
    }

    private void UpdateTabCounters(TransferListResponse response, QueueTabDefinition selectedDefinition)
    {
        foreach (var (page, definition) in _definitions)
        {
            var count = response.StateCounts is null
                ? definition == selectedDefinition ? response.Transfers.Length : 0
                : definition.States.Sum(state => response.StateCounts.GetValueOrDefault(state));
            page.Text = Ui.Format(Ui.Transfer.TabCountFormat, definition.Text, count);
            page.AccessibleDescription = Ui.Format(Ui.Transfer.TabDescriptionFormat, count);
        }
    }

    private static string FormatBytes(long value) => value switch
    {
        >= 1_099_511_627_776 => $"{value / 1_099_511_627_776D:0.##} TB",
        >= 1_073_741_824 => $"{value / 1_073_741_824D:0.##} GB",
        >= 1_048_576 => $"{value / 1_048_576D:0.##} MB",
        >= 1_024 => $"{value / 1_024D:0.##} KB",
        _ => $"{value} B"
    };

    private static string FormatStatus(TransferQueueSummary transfer) => transfer.ErrorSummary is null
        ? UiEnumNames.Describe(transfer.State)
        : Ui.Format(Ui.Transfer.StateWithErrorFormat, UiEnumNames.Describe(transfer.State), transfer.ErrorSummary);

    private bool TryGetSelectedTransfers(out IReadOnlyList<TransferQueueSummary> transfers)
    {
        if (_tabs.SelectedTab is null || !_grids.TryGetValue(_tabs.SelectedTab, out var grid))
        {
            transfers = [];
            return false;
        }

        transfers = grid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(static row => row.Tag)
            .OfType<TransferQueueSummary>()
            .OrderBy(static transfer => transfer.UpdatedUtc)
            .ToArray();
        return true;
    }

    private void SelectedTabChanged(object? sender, EventArgs e)
    {
        _pageCursor = null;
        _nextCursor = null;
        _nextButton.Enabled = false;
        UpdateActionState();
        if (_pollTimer.Enabled)
        {
            _ = RefreshQueueCoreAsync(resetPage: true, _lifetime.Token);
        }
    }

    private void UpdateActionState()
    {
        _ = TryGetSelectedTransfers(out var selected);
        _cancelButton.Enabled = selected.Any(static transfer => transfer.CanCancel);
        _retryButton.Enabled = selected.Any(static transfer => transfer.CanRetry);
        var canReconcile = selected.Any(static transfer => transfer.NeedsReconciliation);
        _reconcileAction.Enabled = canReconcile;
        _reconcileButton.Enabled = canReconcile;
        // The values are enum members, so both sides of this are the enum. It used to compare
        // against a name and then assign a string, which a combo box quietly ignored -- the
        // default action never moved off Review.
        //
        // It moves for anything reconcilable, not only for NeedsReconciliation. The conflicts tab
        // also holds Interrupted transfers, and Review on one of those moves it to
        // NeedsReconciliation -- still a conflict, still on this tab, still counted. Applying the
        // default therefore reported success and changed nothing anybody could see, which reads as
        // a button that does not work.
        if (canReconcile && _reconcileAction.SelectedItem is TransferReconciliationAction.Review)
        {
            _reconcileAction.SelectedItem = TransferReconciliationAction.Restart;
        }

        _nextButton.Enabled = _nextCursor is not null;
    }

    /// <summary>
    /// Prepends still-pending Explorer drags to the Active view. They are not durable work yet, so
    /// they appear only there and are replaced by real jobs as soon as the agent enqueues them.
    /// </summary>
    private List<QueueRow> ComposeRows(
        QueueTabDefinition definition,
        TransferQueueSummary[] transfers)
    {
        var rows = new List<QueueRow>(transfers.Length + 4);
        if (PendingDrops is { } pending &&
            string.Equals(definition.Key, TabKeys.Active, StringComparison.Ordinal))
        {
            rows.AddRange(pending.Snapshot().Select(ToRow));
        }

        rows.AddRange(transfers.Select(ToRow));
        return rows;
    }

    /// <summary>
    /// Desktop-local record of drags that have started but have no destination yet. Optional: the
    /// queue renders durable state correctly without it.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PendingDropRegistry? PendingDrops
    {
        get => _pendingDrops;
        set
        {
            if (ReferenceEquals(_pendingDrops, value))
            {
                return;
            }

            if (_pendingDrops is not null)
            {
                _pendingDrops.Changed -= PendingDropsChanged;
            }

            _pendingDrops = value;
            if (_activityLog is not null)
            {
                _activityLog.PendingDrops = value;
            }

            if (value is not null)
            {
                value.Changed += PendingDropsChanged;
            }
        }
    }

    /// <summary>
    /// A drag starting or settling is a user gesture, so the views refresh immediately rather than
    /// waiting for the next poll tick.
    /// </summary>
    private void PendingDropsChanged(object? sender, EventArgs e)
    {
        if (_disposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(new Action(() =>
            {
                if (_disposed) return;
                _ = RefreshQueueCoreAsync(resetPage: false, _lifetime.Token, background: true);
                _ = _activityLog?.RefreshActivityAsync(_lifetime.Token);
            }));
        }
        catch (InvalidOperationException)
        {
            // The handle can disappear between the guard and BeginInvoke during shutdown.
        }
    }

    /// <summary>
    /// Raised whenever a refresh produces fresh queue counts, so the shell status bar can stay
    /// current without the queue panel being on screen.
    /// </summary>
    public event EventHandler<TransferQueueCountsEventArgs>? QueueCountsChanged;

    private void PublishQueueCounts(TransferListResponse response)
    {
        if (response.StateCounts is not { } counts)
        {
            return;
        }

        var queued = 0;
        var active = 0;
        foreach (var (state, count) in counts)
        {
            if (IsActiveState(state))
            {
                active += count;
            }
            else if (state is TransferQueueState.Pending or TransferQueueState.Retrying)
            {
                queued += count;
            }
        }

        QueueCountsChanged?.Invoke(this, new TransferQueueCountsEventArgs(queued, active));
    }

    /// <summary>
    /// Polls quickly while work is actually moving and backs off when the queue is settled, so a
    /// running transfer advances visibly without charging an idle queue the same wake-up cost.
    /// </summary>
    private void AdjustPollInterval(TransferQueueSummary[] transfers)
    {
        var active = false;
        for (var index = 0; index < transfers.Length; index++)
        {
            if (IsActiveState(transfers[index].State))
            {
                active = true;
                break;
            }
        }

        var interval = active ? ActivePollIntervalMilliseconds : IdlePollIntervalMilliseconds;
        if (_pollTimer.Interval != interval)
        {
            _pollTimer.Interval = interval;
        }
    }

    internal static bool IsActiveState(TransferQueueState state) => state is
        TransferQueueState.Preparing or
        TransferQueueState.Connecting or
        TransferQueueState.Transferring or
        TransferQueueState.Verifying or
        TransferQueueState.Finalizing or
        TransferQueueState.CleanupPending;

    private void SetBusy(bool busy, string? message = null)
    {
        UseWaitCursor = busy;
        if (message is not null)
        {
            _status.Text = message;
        }

        if (!busy)
        {
            UpdateActionState();
        }
    }

    private void SetUnavailable(Exception? error = null)
    {
        _status.Text = error is null
            ? Ui.Transfer.QueueUnavailable
            : DesktopAgentAvailability.ReportFailure(error);
        _nextCursor = null;
        _nextButton.Enabled = false;
    }

    private void UpdatePollingState()
    {
        // Polling follows the window, not this panel. Gating on the panel's own visibility meant
        // the queue stopped refreshing the moment the user switched to a browser tab, which is
        // exactly when a transfer is running and its progress most needs to stay live.
        var shouldPoll = !_disposed && IsHandleCreated && FindForm()?.Visible == true;
        if (!shouldPoll)
        {
            _pollTimer.Stop();
            return;
        }

        if (!_pollTimer.Enabled)
        {
            _pollTimer.Start();
            _ = RefreshQueueCoreAsync(resetPage: true, _lifetime.Token);
        }
    }

    private sealed record QueueTabDefinition(
    string Key,
    Func<TransferStrings, string> Label,
    UiGlyph Glyph,
    TransferQueueState[] States)
{
    /// <summary>The tab's caption in the current language.</summary>
    internal string Text => Label(Ui.Transfer);
}
}

internal sealed class ClearTransferHistoryConfirmationForm : Form
{
    private readonly CheckBox _dontShowAgain;

    internal ClearTransferHistoryConfirmationForm()
    {
        Text = Ui.Transfer.ClearAllTitle;
        AccessibleName = Ui.Transfer.ClearAllAccessibleName;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(500, 190);
        BackColor = StorageHubTheme.Canvas;
        ForeColor = StorageHubTheme.Text;
        StorageHubTheme.Register(this);
        var message = new Label
        {
            AutoSize = false,
            Location = new Point(18, 18),
            Size = new Size(464, 70),
            Text = Ui.Transfer.ClearAllBody,
            ForeColor = StorageHubTheme.Text
        };
        _dontShowAgain = new StorageHubCheckBox
        {
            Text = Ui.Transfer.ClearAllSuppress,
            AutoSize = true,
            Location = new Point(18, 100)
        };
        var clear = new StorageHubButton
        {
            Text = Ui.Transfer.ClearHistory,
            DialogResult = DialogResult.OK,
            Location = new Point(282, 140),
            Size = new Size(100, 32)
        };
        var cancel = new StorageHubButton
        {
            Text = Ui.Dialogs.ButtonCancel,
            DialogResult = DialogResult.Cancel,
            Location = new Point(392, 140),
            Size = new Size(90, 32)
        };
        clear.Variant = StorageHubButtonVariant.Primary;
        clear.BackColor = StorageHubTheme.Danger;
        cancel.Variant = StorageHubButtonVariant.Secondary;
        Controls.AddRange([message, _dontShowAgain, clear, cancel]);
        AcceptButton = clear;
        CancelButton = cancel;
        StorageHubTheme.Apply(this);
    }

    internal bool DontShowAgain => _dontShowAgain.Checked;
}

/// <summary>Live queue counts published by a background refresh.</summary>
public sealed class TransferQueueCountsEventArgs(int queuedJobs, int activeJobs) : EventArgs
{
    public int QueuedJobs { get; } = queuedJobs;

    public int ActiveJobs { get; } = activeJobs;
}

/// <summary>One rendered queue row, from either a durable transfer or a still-pending drag.</summary>
internal sealed record QueueRow(
    string Key,
    string Operation,
    string Source,
    string Destination,
    string Progress,
    string Attempt,
    string Status,
    double? Fraction,
    object Payload);
