using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

public sealed class OverviewDashboardControl : UserControl
{
    private static readonly TransferQueueState[] ActiveStates =
    [
        TransferQueueState.Preparing,
        TransferQueueState.Connecting,
        TransferQueueState.Transferring,
        TransferQueueState.Verifying,
        TransferQueueState.Finalizing,
        TransferQueueState.CleanupPending
    ];

    private static readonly TransferQueueState[] QueuedStates =
    [
        TransferQueueState.Pending,
        TransferQueueState.Retrying
    ];

    private static readonly TransferQueueState[] AttentionStates =
    [
        TransferQueueState.Failed,
        TransferQueueState.Interrupted,
        TransferQueueState.NeedsReconciliation,
        TransferQueueState.BlockedCredential,
        TransferQueueState.BlockedTrust
    ];

    /// <summary>How many rows each card shows. Unrelated to the workspace caps.</summary>
    private const int MaximumListRows = 12;

    // Logical layout units. This page was laid out by eye on a 125% display, so each number here
    // started life as a device pixel measured at that scaling; these are those numbers divided by
    // 1.25. Read logically they reproduce the intended spacing at 125% and follow from the same
    // arithmetic at 100%, 150% or whatever else the display asks for.
    private const int PageGutter = 22;
    private const int PageTop = 19;
    private const int SectionGap = 14;

    /// <summary>Room to breathe above and below a metric tile's two lines of text.</summary>
    private const int MetricCardChrome = 18;

    /// <summary>List-card chrome: card inset, grid padding and the gap above the rows.</summary>
    private const int ListCardChrome = 26;

    // How many rows each card stands tall enough to show before it scrolls. Heights are derived
    // from this and the row height rather than stated in pixels, so a card cannot end up showing
    // three and a half rows at a scaling nobody tried.
    private const int WorkspaceRowsVisible = 4;
    private const int SummaryRowsVisible = 9;

    private readonly IRemoteStorageAgentClient _storageClient;
    private readonly ITransferQueueAgentClient _transferClient;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Label _agentValue;
    private readonly Label _activeValue;
    private readonly Label _queuedValue;
    private readonly Label _attentionValue;
    private readonly ListView _connections;
    private readonly ListView _attention;
    private readonly ListView _workspaces;
    private readonly Label _status;
    private readonly DashboardRow _metrics;
    private readonly DashboardRow _workspaceRow;
    private readonly DashboardRow _lists;
    /// <summary>
    /// The section titles of the list cards, kept so their height can be measured. Reading the
    /// live label rather than the font this control created matters because WinForms replaces a
    /// control's explicitly-set font with a scaled copy when the display scaling changes.
    /// </summary>
    private readonly List<Label> _sectionTitles = [];
    private readonly List<Label> _sectionSubtitles = [];
    private readonly List<Label> _metricCaptions = [];
    private int _metricCardInset;
    private readonly Font _sectionFont = StorageHubTheme.CreateSectionFont();
    private readonly Font _metricValueFont = new("Segoe UI Semibold", 17F, FontStyle.Regular, GraphicsUnit.Point);
    private readonly List<ConnectionSummary> _recentConnections = [];
    private IReadOnlyList<ConnectionSummary> _savedConnections = [];
    private int _refreshing;
    private bool _disposed;

    public OverviewDashboardControl()
        : this(new NamedPipeRemoteStorageAgentClient(), new NamedPipeTransferQueueAgentClient())
    {
    }

    internal OverviewDashboardControl(
        IRemoteStorageAgentClient storageClient,
        ITransferQueueAgentClient transferClient)
    {
        _storageClient = storageClient;
        _transferClient = transferClient;
        DesktopAgentAvailability.Changed += AgentAvailabilityChanged;
        Dock = DockStyle.Fill;
        BackColor = StorageHubTheme.Canvas;
        AccessibleName = Ui.Overview.OverviewAccessibleName;

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 1,
            Padding = this.LogicalToDeviceUnits(
                new Padding(PageGutter, PageTop, PageGutter, PageGutter)),
            BackColor = StorageHubTheme.Canvas
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var heading = new Label
        {
            Text = Ui.Overview.Headline,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 20F),
            ForeColor = StorageHubTheme.Text,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 0, 0, 2))
        };
        var subtitle = new Label
        {
            Text = Ui.Overview.Subheading,
            AutoSize = true,
            ForeColor = StorageHubTheme.TextMuted,
            Margin = this.LogicalToDeviceUnits(new Padding(1, 0, 0, 13))
        };
        content.Controls.Add(heading);
        content.Controls.Add(subtitle);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 0, 0, SectionGap))
        };
        actions.Controls.Add(CreateActionButton(Ui.Overview.ActionNewWorkspace, UiGlyph.Add, (_, _) => NewWorkspaceRequested?.Invoke(this, EventArgs.Empty), primary: true));
        actions.Controls.Add(CreateActionButton(Ui.Overview.ActionConnections, UiGlyph.Connections, (_, _) => ConnectionsRequested?.Invoke(this, EventArgs.Empty)));
        actions.Controls.Add(CreateActionButton(Ui.Overview.ActionSyncTasks, UiGlyph.Compare, (_, _) => SyncTasksRequested?.Invoke(this, EventArgs.Empty)));
        actions.Controls.Add(CreateActionButton(Ui.Overview.ActionRefresh, UiGlyph.Refresh, async (_, _) => await RefreshAsync()));
        content.Controls.Add(actions);

        _metrics = new DashboardRow
        {
            Dock = DockStyle.Top,
            Columns = 4,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 0, 0, SectionGap)),
            BackColor = Color.Transparent
        };
        _agentValue = AddMetric(_metrics, Ui.Overview.MetricAgent, Ui.Overview.AgentStarting, UiGlyph.Server, UiIconTone.Primary);
        _activeValue = AddMetric(_metrics, Ui.Overview.MetricActiveTransfers, "0", UiGlyph.Run, UiIconTone.Success);
        _queuedValue = AddMetric(_metrics, Ui.Overview.MetricQueued, "0", UiGlyph.Queue, UiIconTone.Primary);
        _attentionValue = AddMetric(_metrics, Ui.Overview.MetricNeedsAttention, "0", UiGlyph.Warning, UiIconTone.Warning);
        content.Controls.Add(_metrics);

        // A full-width row of its own rather than a third column beside the two lists: at the
        // shell's 1120px minimum width, three columns leave each one narrower than the columns
        // its ListView already declares, so every card would grow a horizontal scrollbar.
        _workspaces = CreateList(
            Ui.Overview.WorkspacesTitle,
            Ui.Overview.WorkspacesSubtitle,
            UiGlyph.Add,
            out var workspaceCard);
        // Only the leading columns get a fixed width. The trailing one absorbs whatever is left,
        // so the row never totals more than the card and never raises a horizontal scrollbar in a
        // narrow window.
        _workspaces.Columns[0].Width = LogicalToDeviceUnits(176);
        _workspaces.Columns[1].Text = Ui.Overview.ColumnLocation;
        _workspaces.Columns[1].Width = LogicalToDeviceUnits(336);
        _workspaces.Columns[2].Text = Ui.Overview.ColumnState;
        StorageHubTheme.FitTrailingColumn(_workspaces);
        // No ListViewGroups: the rows arrive pinned-first and the State column already says which
        // list each came from, so grouping would add native-control fragility for no information.
        _workspaces.MouseDoubleClick += (_, args) => OpenWorkspaceAt(_workspaces.HitTest(args.Location).Item);
        _workspaces.KeyDown += WorkspaceListKeyDown;
        _workspaces.ContextMenuStrip = BuildWorkspaceMenu();
        _workspaceRow = new DashboardRow
        {
            Dock = DockStyle.Top,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 0, 0, SectionGap)),
            BackColor = Color.Transparent
        };
        _workspaceRow.Controls.Add(workspaceCard);
        content.Controls.Add(_workspaceRow);

        _lists = new DashboardRow
        {
            Dock = DockStyle.Top,
            Columns = 2,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        _connections = CreateList(Ui.Overview.ConnectionsTitle, Ui.Overview.ConnectionsSubtitle, UiGlyph.Connections, out var connectionCard);
        _connections.Columns[1].Text = Ui.Overview.ColumnProvider;
        _connections.Columns[2].Text = Ui.Overview.ColumnDetails;
        _attention = CreateList(Ui.Overview.AttentionTitle, Ui.Overview.AttentionSubtitle, UiGlyph.Warning, out var attentionCard);
        _lists.Controls.Add(connectionCard);
        _lists.Controls.Add(attentionCard);
        content.Controls.Add(_lists);

        _status = new Label
        {
            Text = Ui.Overview.StatusDeferred,
            AutoSize = true,
            ForeColor = StorageHubTheme.TextMuted,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 10, 0, 0))
        };
        content.Controls.Add(_status);
        Controls.Add(content);
        ApplyRowHeights();
    }

    /// <summary>
    /// Sizes the three card bands from the text they hold rather than from pixel constants, so a
    /// card cannot clip its own contents at a scaling nobody tested -- or after the display it is
    /// sitting on changes scaling under an open window.
    /// </summary>
    private void ApplyRowHeights()
    {
        if (_disposed || IsDisposed)
        {
            return;
        }

        // Summed from the laid-out labels, not from the grid's PreferredSize: asked for a
        // preferred height the grid answered 86 at 100% and 238 at 125% for the same two lines
        // of text, because it measures against an unconstrained width. A label that has been
        // through layout already knows how tall its own text is.
        if (_metricCaptions.Count > 0)
        {
            _metrics.Height = _agentValue.Height
                + _agentValue.Margin.Vertical
                + _metricCaptions[0].Height
                + _metricCardInset
                + LogicalToDeviceUnits(MetricCardChrome);
        }

        _workspaceRow.Height = ListCardHeight(WorkspaceRowsVisible);
        _lists.Height = ListCardHeight(SummaryRowsVisible);
    }

    /// <summary>A list card's title, subtitle, column header and <paramref name="rows"/> of data.</summary>
    private int ListCardHeight(int rows)
    {
        var header = _sectionTitles.Count > 0 && _sectionSubtitles.Count > 0
            ? _sectionTitles[0].Height + _sectionTitles[0].Margin.Vertical + _sectionSubtitles[0].Height
            : _sectionFont.Height + Font.Height;
        // The column header band is a row's worth of chrome, so it is measured the same way.
        return header + (ListRowHeight * (rows + 1)) + LogicalToDeviceUnits(ListCardChrome);
    }

    private int ListRowHeight => Font.Height + LogicalToDeviceUnits(4);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // WinForms rescales a control tree's fonts after it is built and parented, so the heights
        // measured in the constructor were taken against 96-dpi text and come out short on a
        // scaled display. The next turn of the message loop is the first moment the fonts in hand
        // are the ones that will actually render.
        BeginInvoke(ApplyRowHeights);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        ApplyRowHeights();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyRowHeights();
    }

    public event EventHandler? NewWorkspaceRequested;

    public event EventHandler? ConnectionsRequested;

    public event EventHandler? SyncTasksRequested;

    internal event EventHandler<WorkspaceShortcutEventArgs>? WorkspaceOpenRequested;

    internal event EventHandler<WorkspaceShortcutEventArgs>? WorkspacePinToggleRequested;

    internal event EventHandler<WorkspaceShortcutEventArgs>? WorkspaceRemoveRequested;

    /// <summary>
    /// The connections read on the last refresh. Exposed so the shell can build a favourites menu
    /// without opening a second pipe client with its own lifetime and disposal path.
    /// </summary>
    internal IReadOnlyList<ConnectionSummary> SavedConnections => _savedConnections;

    /// <summary>
    /// The favourites worth offering as a navigation target: enabled, marked favourite, and of a
    /// kind a browser pane can actually open. Without the last filter the menu would list
    /// connections that do nothing when clicked.
    /// </summary>
    /// <summary>
    /// Replaces the workspace card's contents. The shell supplies the rows already ordered and
    /// de-duplicated, and decides which files look present: this control performs no IO.
    /// </summary>
    internal void ShowWorkspaceShortcuts(IReadOnlyList<WorkspaceShortcutView> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _workspaces.BeginUpdate();
        try
        {
            _workspaces.Items.Clear();
            foreach (var entry in entries)
            {
                var item = new ListViewItem(
                    entry.Entry.DisplayName,
                    entry.LooksPresent ? "connection" : "warning")
                {
                    Tag = entry,
                    ToolTipText = entry.Entry.Path,
                    // Dimmed, never disabled: clicking a missing entry is how it gets pruned.
                    ForeColor = entry.LooksPresent ? StorageHubTheme.Text : StorageHubTheme.TextMuted
                };
                item.SubItems.Add(entry.Entry.Path);
                // The State column, not just the colour, is what tells a screen reader the file
                // is gone.
                item.SubItems.Add(entry.LooksPresent
                    ? entry.IsPinned ? Ui.Overview.WorkspaceStatePinned : Ui.Overview.WorkspaceStateRecent
                    : Ui.Overview.WorkspaceStateMissing);
                _workspaces.Items.Add(item);
            }

            if (_workspaces.Items.Count == 0)
            {
                var empty = new ListViewItem(Ui.Overview.WorkspacesEmpty, "empty");
                empty.SubItems.Add(Ui.Overview.WorkspacesEmptyHint);
                _workspaces.Items.Add(empty);
            }
        }
        finally
        {
            _workspaces.EndUpdate();
        }
    }

    public void UpdateAgentStatus(ShellStatusSnapshot status)
    {
        _agentValue.Text = status.AgentState switch
        {
            AgentConnectionState.Connected => Ui.Overview.AgentConnected,
            AgentConnectionState.RecoveryOnly => Ui.Overview.AgentRecoveryMode,
            AgentConnectionState.Disconnected => Ui.Overview.AgentOffline,
            _ => Ui.Overview.AgentStarting
        };
        _agentValue.ForeColor = status.AgentState switch
        {
            AgentConnectionState.Connected => StorageHubTheme.Success,
            AgentConnectionState.RecoveryOnly => StorageHubTheme.Warning,
            AgentConnectionState.Disconnected => StorageHubTheme.Danger,
            _ => StorageHubTheme.TextMuted
        };
    }

    public void RecordRecentConnection(ConnectionSummary connection)
    {
        _recentConnections.RemoveAll(candidate => candidate.ConnectionId == connection.ConnectionId);
        _recentConnections.Insert(0, connection);
        if (_recentConnections.Count > 12)
        {
            _recentConnections.RemoveRange(12, _recentConnections.Count - 12);
        }

        PopulateConnections(_savedConnections);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _refreshing, 1) != 0 || _disposed)
        {
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            _status.Text = Ui.Overview.StatusRefreshing;
            var connectionsTask = _storageClient.ListConnectionsAsync(new ConnectionListRequest(
                IncludeDisabled: false,
                Limit: StorageIpcLimits.MaximumConnectionResults), linked.Token);
            var activeTask = ListTransfersAsync(ActiveStates, linked.Token);
            var queuedTask = ListTransfersAsync(QueuedStates, linked.Token);
            var attentionTask = ListTransfersAsync(AttentionStates, linked.Token);
            await Task.WhenAll(connectionsTask, activeTask, queuedTask, attentionTask).ConfigureAwait(true);

            var connectionResponse = await connectionsTask.ConfigureAwait(true);
            var active = await activeTask.ConfigureAwait(true);
            var queued = await queuedTask.ConfigureAwait(true);
            var attention = await attentionTask.ConfigureAwait(true);
            ThrowIfFailure(connectionResponse.Failure);

            _activeValue.Text = FormatBoundedCount(active);
            _queuedValue.Text = FormatBoundedCount(queued);
            _attentionValue.Text = FormatBoundedCount(attention);
            PopulateConnections(connectionResponse.Connections);
            PopulateAttention(attention.Transfers);
            _status.Text = Ui.Format(Ui.Overview.StatusUpdatedFormat, DateTime.Now);
            _status.ForeColor = StorageHubTheme.TextMuted;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            // What the person is told, and the decision to reconnect, both belong to one place.
            // This used to print exception.Message, which is how "Pipe is broken." reached the
            // window while the status bar underneath it still read "Agent: connected".
            _status.Text = DesktopAgentAvailability.ReportFailure(exception);
            _status.ForeColor = StorageHubTheme.Warning;
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);
        }
    }

    /// <summary>
    /// Reloads once the agent is answering again. The overview is usually the surface somebody is
    /// looking at when the agent restarts, so it is the one most worth not leaving stale.
    /// </summary>
    private void AgentAvailabilityChanged(object? sender, AgentAvailabilityChangedEventArgs e)
    {
        if (!e.Recovered || _disposed || IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(new Action(() =>
            {
                if (!_disposed && !IsDisposed)
                {
                    _ = RefreshAsync(_lifetime.Token);
                }
            }));
        }
        catch (InvalidOperationException)
        {
            // The window went away between the check and the post, which needs no reload.
        }
    }

    protected override async void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible && IsHandleCreated)
        {
            await RefreshAsync();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            DesktopAgentAvailability.Changed -= AgentAvailabilityChanged;
            _lifetime.Cancel();
            _storageClient.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _transferClient.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _lifetime.Dispose();
            _sectionFont.Dispose();
            _metricValueFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task<TransferListResponse> ListTransfersAsync(TransferQueueState[] states, CancellationToken cancellationToken)
    {
        var response = await _transferClient.ListAsync(new TransferListRequest(
            TransferQueueIpcContract.CurrentVersion,
            states,
            PageSize: 25), cancellationToken).ConfigureAwait(true);
        ThrowIfFailure(response.Failure);
        return response;
    }

    private void PopulateConnections(IEnumerable<ConnectionSummary> connections)
    {
        _savedConnections = connections.ToArray();
        _connections.BeginUpdate();
        try
        {
            _connections.Items.Clear();
            foreach (var connection in _recentConnections
                .Concat(_savedConnections
                    .OrderByDescending(static value => value.IsFavorite)
                    .ThenBy(static value => value.DisplayName, StringComparer.CurrentCultureIgnoreCase))
                .DistinctBy(static value => value.ConnectionId)
                .Take(12))
            {
                var item = new ListViewItem(connection.DisplayName, "connection")
                {
                    Tag = connection.ConnectionId,
                    ToolTipText = connection.FolderPath ?? connection.Provider.ToString()
                };
                item.SubItems.Add(connection.Provider.ToString());
                item.SubItems.Add(connection.IsFavorite ? Ui.Overview.ConnectionFavorite : connection.FolderPath ?? string.Empty);
                _connections.Items.Add(item);
            }

            if (_connections.Items.Count == 0)
            {
                _connections.Items.Add(new ListViewItem(Ui.Overview.ConnectionsEmpty, "empty"));
            }
        }
        finally
        {
            _connections.EndUpdate();
        }
    }

    private void PopulateAttention(IEnumerable<TransferQueueSummary> transfers)
    {
        _attention.BeginUpdate();
        try
        {
            _attention.Items.Clear();
            foreach (var transfer in transfers.OrderByDescending(static value => value.UpdatedUtc).Take(12))
            {
                var item = new ListViewItem(DescribeTransfer(transfer), "warning")
                {
                    Tag = transfer.TransferId,
                    ToolTipText = transfer.ErrorSummary ?? UiEnumNames.Describe(transfer.State)
                };
                item.SubItems.Add(UiEnumNames.Describe(transfer.State));
                item.SubItems.Add(transfer.UpdatedUtc.LocalDateTime.ToString(
                    "g",
                    System.Globalization.CultureInfo.CurrentCulture));
                _attention.Items.Add(item);
            }

            if (_attention.Items.Count == 0)
            {
                _attention.Items.Add(new ListViewItem(Ui.Overview.AttentionEmpty, "ok"));
            }
        }
        finally
        {
            _attention.EndUpdate();
        }
    }

    private static string DescribeTransfer(TransferQueueSummary transfer)
    {
        var path = string.IsNullOrWhiteSpace(transfer.SourcePath) ? transfer.DestinationPath : transfer.SourcePath;
        return $"{transfer.Operation}: {path}";
    }

    private static string FormatBoundedCount(TransferListResponse response) =>
        response.ContinuationToken is null
            ? response.Transfers.Length.ToString(System.Globalization.CultureInfo.CurrentCulture)
            : $"{response.Transfers.Length}+";

    private static void ThrowIfFailure(StorageIpcFailure? failure)
    {
        if (failure is not null)
        {
            throw new InvalidOperationException(failure.Message);
        }
    }

    private Label AddMetric(DashboardRow host, string title, string value, UiGlyph glyph, UiIconTone tone)
    {
        var accent = StorageHubTheme.ToneColor(tone);
        var card = CreateCard(accent);
        // The rail and the rounded corners are painted by the card, so its content stays inside
        // them: a 3px inset keeps an opaque rectangle within a 7px corner radius. The content
        // used to be transparent instead, and a transparent panel paints by asking its parent to
        // paint first, so every label refresh repainted the whole card, rail, curves and all.
        card.Padding = this.LogicalToDeviceUnits(new Padding(5, 2, 2, 2));
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = StorageHubTheme.Surface,
            Padding = this.LogicalToDeviceUnits(new Padding(7, 7, 9, 7)),
            Margin = Padding.Empty
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(37)));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        // Sized by its own glyph. Docking it made it drag the two auto-sizing rows around and
        // pushed the glyph below the text; an explicit pixel Size was worse again, because a size
        // stated in pixels is rescaled against the *system* dpi rather than the window's, so the
        // same tile measured 24 on a 100% primary, 48 on a 125% secondary beside it, and 21 on
        // that same secondary once the primary moved to 150%.
        //
        // Anchored to nothing, which is how a TableLayoutPanel centres a control in the rows it
        // spans. Anchoring it left instead pinned the glyph to the caption line rather than
        // sitting it beside both lines.
        var icon = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.AutoSize,
            Anchor = AnchorStyles.None,
            Margin = Padding.Empty
        };
        _ = StorageHubTheme.TrackIcon(icon, glyph, 24, tone);
        // Both lines size themselves. A Dock-filled label inside an auto-sizing row reports a
        // preferred height that does not track the font -- measured across two displays the two
        // rows traded their share of the tile instead of both growing by the scaling factor.
        var valueLabel = new Label
        {
            Text = value,
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            Font = _metricValueFont,
            ForeColor = StorageHubTheme.Text,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 0, 0, 1))
        };
        var titleLabel = new Label
        {
            Text = title,
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            ForeColor = StorageHubTheme.TextMuted,
            Margin = Padding.Empty
        };
        grid.Controls.Add(icon, 0, 0);
        grid.SetRowSpan(icon, 2);
        grid.Controls.Add(valueLabel, 1, 0);
        grid.Controls.Add(titleLabel, 1, 1);
        card.Controls.Add(grid);
        host.Controls.Add(card);
        // All four tiles are built alike, so one of them answers for the band's height.
        _metricCaptions.Add(titleLabel);
        _metricCardInset = card.Padding.Vertical + grid.Padding.Vertical;
        return valueLabel;
    }

    private ListView CreateList(string title, string subtitle, UiGlyph glyph, out UiCard card)
    {
        card = CreateCard();
        card.Dock = DockStyle.Fill;
        // Opaque and inset past the corner radius, for the reason given in AddMetric.
        card.Padding = this.LogicalToDeviceUnits(new Padding(2));
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            BackColor = StorageHubTheme.Surface,
            Padding = this.LogicalToDeviceUnits(new Padding(9, 7, 9, 9)),
            Margin = Padding.Empty
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(29)));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        // Title and subtitle each measure their own line of text, so a longer translation or a
        // rescaled font grows the band instead of being clipped by it.
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        // Sized by its own glyph and centred in the rows it spans, for the reasons in AddMetric.
        var icon = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.AutoSize,
            Anchor = AnchorStyles.None,
            Margin = Padding.Empty
        };
        _ = StorageHubTheme.TrackIcon(icon, glyph, 20, glyph == UiGlyph.Warning ? UiIconTone.Warning : UiIconTone.Primary);
        var titleLabel = new Label
        {
            Text = title,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = _sectionFont,
            ForeColor = StorageHubTheme.Text,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 0, 0, 2))
        };
        _sectionTitles.Add(titleLabel);
        var subtitleLabel = new Label
        {
            Text = subtitle,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = StorageHubTheme.TextMuted,
            Margin = Padding.Empty
        };
        _sectionSubtitles.Add(subtitleLabel);
        var images = new ImageList { ImageSize = LogicalToDeviceUnits(new Size(18, 18)), ColorDepth = ColorDepth.Depth32Bit };
        images.Images.Add("connection", UiIconFactory.Create(UiGlyph.Connections, StorageHubTheme.Primary, 18, DeviceDpi / 96F));
        images.Images.Add("warning", UiIconFactory.Create(UiGlyph.Warning, StorageHubTheme.Warning, 18, DeviceDpi / 96F));
        images.Images.Add("ok", UiIconFactory.Create(UiGlyph.Test, StorageHubTheme.Success, 18, DeviceDpi / 96F));
        images.Images.Add("empty", UiIconFactory.Create(UiGlyph.Info, StorageHubTheme.TextMuted, 18, DeviceDpi / 96F));
        var list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            SmallImageList = images,
            Dock = DockStyle.Fill,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 6, 0, 0)),
            BackColor = StorageHubTheme.Surface,
            ForeColor = StorageHubTheme.Text,
            ShowItemToolTips = true
        };
        StorageHubTheme.ConfigureList(list);
        list.Columns.Add(Ui.Overview.ColumnName, LogicalToDeviceUnits(192));
        list.Columns.Add(Ui.Overview.ColumnState, LogicalToDeviceUnits(88));
        list.Columns.Add(Ui.Overview.ColumnUpdated, LogicalToDeviceUnits(104));
        grid.Controls.Add(icon, 0, 0);
        grid.SetRowSpan(icon, 2);
        grid.Controls.Add(titleLabel, 1, 0);
        grid.Controls.Add(subtitleLabel, 1, 1);
        grid.Controls.Add(list, 0, 2);
        grid.SetColumnSpan(list, 2);
        card.Controls.Add(grid);
        return list;
    }

    private ContextMenuStrip BuildWorkspaceMenu()
    {
        var menu = new ContextMenuStrip { Renderer = DesktopAppearanceService.MenuRenderer };
        var open = new ToolStripMenuItem(Ui.Overview.ContextOpen);
        var pin = new ToolStripMenuItem(Ui.Overview.ContextPin);
        var remove = new ToolStripMenuItem(Ui.Overview.ContextRemoveFromList);
        var copy = new ToolStripMenuItem(Ui.Overview.ContextCopyPath);
        open.Click += (_, _) => OpenWorkspaceAt(SelectedWorkspace());
        pin.Click += (_, _) => Raise(WorkspacePinToggleRequested, SelectedWorkspace());
        remove.Click += (_, _) => Raise(WorkspaceRemoveRequested, SelectedWorkspace());
        copy.Click += (_, _) =>
        {
            if (SelectedWorkspace()?.Tag is WorkspaceShortcutView view)
            {
                Clipboard.SetText(view.Entry.Path);
            }
        };
        menu.Items.AddRange([open, pin, remove, copy]);
        menu.Opening += (_, args) =>
        {
            if (SelectedWorkspace()?.Tag is not WorkspaceShortcutView view)
            {
                args.Cancel = true;
                return;
            }

            pin.Text = view.IsPinned ? Ui.Overview.ContextUnpin : Ui.Overview.ContextPin;
        };
        return menu;
    }

    private ListViewItem? SelectedWorkspace() => _workspaces.SelectedItems.Count == 1
        ? _workspaces.SelectedItems[0]
        : null;

    private void WorkspaceListKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.KeyCode != Keys.Enter) return;
        args.Handled = true;
        OpenWorkspaceAt(SelectedWorkspace());
    }

    private void OpenWorkspaceAt(ListViewItem? item) => Raise(WorkspaceOpenRequested, item);

    private void Raise(EventHandler<WorkspaceShortcutEventArgs>? handler, ListViewItem? item)
    {
        if (item?.Tag is WorkspaceShortcutView view)
        {
            handler?.Invoke(this, new WorkspaceShortcutEventArgs(view));
        }
    }

    /// <summary>
    /// A band of equal-width cards across the page. The row divides its own width between its
    /// children rather than delegating to proportional columns, so the tiles line up from one
    /// arithmetic step instead of from a table's preferred-size negotiation with its contents.
    /// </summary>
    private sealed class DashboardRow : Panel
    {
        private const int Gutter = 10;

        /// <summary>How many equal columns the children are spread across.</summary>
        [System.ComponentModel.DefaultValue(1)]
        public int Columns { get; init; } = 1;

        public override Size GetPreferredSize(Size proposedSize) => new(0, Height);

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            var count = Math.Max(1, Columns);
            var gutter = LogicalToDeviceUnits(Gutter);
            var available = ClientSize.Width - (gutter * (count - 1));
            if (available <= 0 || Controls.Count == 0)
            {
                return;
            }

            var cell = available / count;
            var index = 0;
            foreach (Control child in Controls)
            {
                var left = index * (cell + gutter);
                // The last column absorbs the rounding remainder so the band ends flush.
                var width = index == count - 1 ? ClientSize.Width - left : cell;
                var bounds = new Rectangle(left, 0, Math.Max(1, width), ClientSize.Height);
                // Assigning unchanged bounds still invalidates the child, so a layout pass that
                // was triggered by something else would repaint every card for nothing.
                if (child.Bounds != bounds)
                {
                    child.Bounds = bounds;
                }

                index++;
                if (index >= count)
                {
                    break;
                }
            }
        }
    }

    private static UiCard CreateCard(Color? accent = null) => new()
    {
        BackColor = StorageHubTheme.Surface,
        Accent = accent
    };

    private StorageHubButton CreateActionButton(string text, UiGlyph glyph, EventHandler handler, bool primary = false)
    {
        var button = new StorageHubButton
        {
            Text = text,
            ImageAlign = ContentAlignment.MiddleLeft,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            AutoSize = true,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 0, 6, 0))
        };
        _ = StorageHubTheme.TrackIcon(button, glyph, 18, primary ? UiIconTone.OnPrimary : UiIconTone.Text);
        if (primary)
        {
            button.Variant = StorageHubButtonVariant.Primary;
        }
        else
        {
            button.Variant = StorageHubButtonVariant.Secondary;
        }

        button.Click += handler;
        return button;
    }
}
