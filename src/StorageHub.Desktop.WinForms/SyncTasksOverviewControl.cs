using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

public sealed class SyncTasksOverviewControl : UserControl
{
    private const int MaximumLoadedRuns = 1_000;
    private readonly ISyncManagementAgentClient _client;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Label _enabledValue;
    private readonly Label _disabledValue;
    private readonly Label _knownRunsValue;
    private readonly ListView _profiles;
    private readonly ListView _runs;
    private readonly Label _status;
    private readonly TabControl _views;
    private readonly TabPage _runReviewPage;
    private readonly SyncRunsControl _runReview;
    private readonly SplitContainer _lists;
    private readonly List<SyncRunSummary> _sessionRuns = [];
    private int _refreshing;
    private bool _disposed;

    public SyncTasksOverviewControl()
        : this(new NamedPipeSyncManagementAgentClient())
    {
    }

    internal SyncTasksOverviewControl(ISyncManagementAgentClient client)
    {
        _client = client;
        Dock = DockStyle.Fill;
        BackColor = StorageHubTheme.Canvas;
        AccessibleName = Ui.Sync.TasksOverviewAccessibleName;

        _views = new ThemedTabControl
        {
            Dock = DockStyle.Fill,
            AccessibleName = Ui.Sync.TasksViews,
            HotTrack = true,
            Padding = new Point(LogicalToDeviceUnits(16), LogicalToDeviceUnits(4))
        };
        StorageHubTheme.ConfigureTabs(_views);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = this.LogicalToDeviceUnits(new Padding(28, 24, 28, 24)),
            BackColor = StorageHubTheme.Canvas
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        content.Controls.Add(new Label
        {
            Text = Ui.Sync.TasksTitle,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 20F),
            ForeColor = StorageHubTheme.Text,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 0, 0, 2))
        });
        content.Controls.Add(new Label
        {
            Text = Ui.Sync.TasksAccessibleDescription,
            AutoSize = true,
            ForeColor = StorageHubTheme.TextMuted,
            Margin = this.LogicalToDeviceUnits(new Padding(1, 0, 0, 16))
        });

        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = this.LogicalToDeviceUnits(new Padding(0, 0, 0, 18)) };
        actions.Controls.Add(CreateButton(Ui.Sync.NewSyncProfile, UiGlyph.Add, (_, _) => NewProfileRequested?.Invoke(this, EventArgs.Empty), primary: true));
        actions.Controls.Add(CreateButton("Schedules", UiGlyph.Run, (_, _) => SchedulesRequested?.Invoke(this, EventArgs.Empty)));
        actions.Controls.Add(CreateButton(Ui.Sync.RunHistoryAndReview, UiGlyph.Compare, ReviewRunClicked));
        actions.Controls.Add(CreateButton(Ui.Sync.PickerRefresh, UiGlyph.Refresh, async (_, _) => await RefreshAsync()));
        content.Controls.Add(actions);

        var metrics = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = this.TextBoxHeight(70),
            MinimumSize = new Size(0, this.TextBoxHeight(70)),
            ColumnCount = 3,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 0, 0, 18))
        };
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        // The row has to fill the band. Left undeclared it auto-sizes, so the cards take their
        // own preferred height and hang out of the bottom -- which only stayed invisible while
        // the band was a generous fixed 108px.
        metrics.RowCount = 1;
        metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _enabledValue = AddMetric(metrics, 0, Ui.Sync.EnabledTasks, UiGlyph.Run, StorageHubTheme.Success);
        _disabledValue = AddMetric(metrics, 1, Ui.Sync.DisabledTasks, UiGlyph.Pause, StorageHubTheme.TextMuted);
        _knownRunsValue = AddMetric(metrics, 2, Ui.Sync.RunsThisSession, UiGlyph.Compare, StorageHubTheme.Primary);
        content.Controls.Add(metrics);

        var lists = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            // Canvas, not Border. The gutter below is page space between two cards; painted in
            // the border colour it read as a rule joining them into one block instead.
            BackColor = StorageHubTheme.Canvas,
            Margin = Padding.Empty
        };
        // Neither card may be starved below its heading, column header and a few rows.
        lists.Panel1MinSize = this.TextBoxHeight(50);
        lists.Panel2MinSize = this.TextBoxHeight(50);
        _lists = lists;

        _profiles = CreateList(Ui.Sync.SavedTasks, Ui.Sync.ColumnState, UiGlyph.Compare, out var profilesCard);
        _profiles.Columns.Insert(1, Ui.Sync.ColumnBehavior, 180);
        _runs = CreateList(Ui.Sync.LastSyncs, Ui.Sync.ColumnState, UiGlyph.Run, out var runsCard);
        lists.Panel1.BackColor = StorageHubTheme.Canvas;
        lists.Panel2.BackColor = StorageHubTheme.Canvas;
        lists.Panel1.Padding = this.LogicalToDeviceUnits(new Padding(0, 0, 0, 7));
        lists.Panel2.Padding = this.LogicalToDeviceUnits(new Padding(0, 7, 0, 0));
        lists.Panel1.Controls.Add(profilesCard);
        lists.Panel2.Controls.Add(runsCard);
        content.Controls.Add(lists);

        _status = new Label
        {
            Text = Ui.Sync.TasksDeferred,
            AutoSize = true,
            ForeColor = StorageHubTheme.TextMuted,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 10, 0, 0))
        };
        content.Controls.Add(_status);

        var tasksPage = new TabPage("Tasks")
        {
            AccessibleName = Ui.Sync.TasksHeading
        };
        tasksPage.Controls.Add(content);
        _runReview = new SyncRunsControl(_client);
        _runReviewPage = new TabPage(Ui.Sync.RunHistoryAndReview)
        {
            AccessibleName = Ui.Sync.TasksRunHistory
        };
        _runReviewPage.Controls.Add(_runReview);
        _views.TabPages.Add(tasksPage);
        _views.TabPages.Add(_runReviewPage);
        Controls.Add(_views);
        PopulateRuns();
    }

    public event EventHandler? NewProfileRequested;

    public event EventHandler? SchedulesRequested;

    public event EventHandler? ReviewRunRequested;

    public SyncRunsControl RunReview => _runReview;

    public void ShowRunReview()
    {
        _views.SelectedTab = _runReviewPage;
        ReviewRunRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RecordRun(SyncRunSummary run)
    {
        _sessionRuns.RemoveAll(candidate => candidate.SyncRunId == run.SyncRunId);
        _sessionRuns.Insert(0, run);
        if (_sessionRuns.Count > 20)
        {
            _sessionRuns.RemoveRange(20, _sessionRuns.Count - 20);
        }

        PopulateRuns();
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
            _status.Text = Ui.Sync.TasksRefreshing;
            var response = await _client.ListProfilesAsync(new SyncProfileListRequest(
                IncludeDisabled: true,
                MaximumCount: SyncManagementIpcLimits.MaximumProfileResults), linked.Token).ConfigureAwait(true);
            if (response.Failure is not null)
            {
                throw new InvalidOperationException(response.Failure.Message);
            }

            var runHistory = await LoadRunHistoryAsync(linked.Token).ConfigureAwait(true);

            var enabled = response.Profiles.Count(static value => value.Enabled);
            _enabledValue.Text = enabled.ToString(System.Globalization.CultureInfo.CurrentCulture);
            _disabledValue.Text = (response.Profiles.Length - enabled).ToString(System.Globalization.CultureInfo.CurrentCulture);
            PopulateProfiles(response.Profiles);
            _sessionRuns.Clear();
            _sessionRuns.AddRange(runHistory);
            PopulateRuns();
            _status.Text = Ui.Format(Ui.Sync.TasksUpdatedFormat, DateTime.Now, _sessionRuns.Count);
            _status.ForeColor = StorageHubTheme.TextMuted;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _status.Text = DesktopAgentAvailability.ReportFailure(exception);
            _status.ForeColor = StorageHubTheme.Warning;
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);
        }
    }

    private void ReviewRunClicked(object? sender, EventArgs e) => ShowRunReview();

    private async Task<IReadOnlyList<SyncRunSummary>> LoadRunHistoryAsync(CancellationToken cancellationToken)
    {
        var runs = new List<SyncRunSummary>();
        string? continuation = null;
        do
        {
            var response = await _client.ListRunsAsync(new SyncRunListRequest(
                PageSize: Math.Min(SyncManagementIpcLimits.MaximumPageSize, MaximumLoadedRuns - runs.Count),
                ContinuationToken: continuation), cancellationToken).ConfigureAwait(true);
            if (response.Failure is not null)
            {
                throw new InvalidOperationException(response.Failure.Message);
            }

            runs.AddRange(response.Runs);
            if (runs.Count >= MaximumLoadedRuns)
            {
                break;
            }

            if (response.ContinuationToken is not null &&
                string.Equals(response.ContinuationToken, continuation, StringComparison.Ordinal))
            {
                throw new InvalidDataException(Ui.Sync.RepeatedHistoryToken);
            }

            continuation = response.ContinuationToken;
        }
        while (continuation is not null);

        return runs;
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
            _lifetime.Cancel();
            _client.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _lifetime.Dispose();
        }

        base.Dispose(disposing);
    }

    private void PopulateProfiles(IEnumerable<SyncProfileSummary> profiles)
    {
        _profiles.BeginUpdate();
        try
        {
            _profiles.Items.Clear();
            foreach (var profile in profiles.OrderByDescending(static value => value.UpdatedUtc))
            {
                var item = new ListViewItem(profile.DisplayName, profile.Enabled ? "enabled" : "disabled")
                {
                    Tag = profile.ProfileId
                };
                item.SubItems.Add(SyncBehaviorPickerControl.GetDisplayName(profile.Behavior));
                item.SubItems.Add(profile.Enabled ? Ui.Sync.Enabled : Ui.Sync.TaskDisabled);
                item.SubItems.Add(profile.UpdatedUtc.LocalDateTime.ToString(
                    "g",
                    System.Globalization.CultureInfo.CurrentCulture));
                _profiles.Items.Add(item);
            }

            if (_profiles.Items.Count == 0)
            {
                _profiles.Items.Add(new ListViewItem(Ui.Sync.NoTasksConfigured, "empty"));
            }
        }
        finally
        {
            _profiles.EndUpdate();
        }
    }

    private void PopulateRuns()
    {
        _knownRunsValue.Text = _sessionRuns.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
        _runs.BeginUpdate();
        try
        {
            _runs.Items.Clear();
            foreach (var run in _sessionRuns)
            {
                var item = new ListViewItem(run.SyncRunId.ToString("D"), "run") { Tag = run.SyncRunId };
                item.SubItems.Add(UiEnumNames.Describe(run.Phase));
                item.SubItems.Add(run.UpdatedUtc.LocalDateTime.ToString(
                    "g",
                    System.Globalization.CultureInfo.CurrentCulture));
                _runs.Items.Add(item);
            }

            if (_runs.Items.Count == 0)
            {
                var item = new ListViewItem(Ui.Sync.NoRunOpened, "empty");
                item.SubItems.Add(Ui.Sync.UseReviewAndRun);
                _runs.Items.Add(item);
            }
        }
        finally
        {
            _runs.EndUpdate();
        }
    }

    private Label AddMetric(TableLayoutPanel host, int column, string title, UiGlyph glyph, Color accent)
    {
        var card = CreateCard();
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(column == 0 ? 0 : 6, 0, column == 2 ? 0 : 6, 0);
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = this.LogicalToDeviceUnits(new Padding(12, 10, 12, 10)),
            Margin = Padding.Empty
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(42)));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        var icon = new PictureBox
        {
            Image = UiIconFactory.Create(glyph, accent, 24, DeviceDpi / 96F),
            SizeMode = PictureBoxSizeMode.CenterImage,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        var value = new Label
        {
            Text = "0",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font("Segoe UI Semibold", 15F),
            ForeColor = StorageHubTheme.Text,
            Margin = Padding.Empty
        };
        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = StorageHubTheme.TextMuted,
            Margin = Padding.Empty
        };
        grid.Controls.Add(icon, 0, 0);
        grid.SetRowSpan(icon, 2);
        grid.Controls.Add(value, 1, 0);
        grid.Controls.Add(titleLabel, 1, 1);
        card.Controls.Add(grid);
        host.Controls.Add(card, column, 0);
        return value;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // A SplitContainer is 150px tall until it has been laid out, so a distance assigned in
        // the constructor is clamped to fit that and then carried forward. The next turn of the
        // message loop is the first moment the real height is known.
        BeginInvoke(PlaceSplitter);
    }

    /// <summary>
    /// Half the band each. The two lists matter equally, and giving the upper one a fixed height
    /// spends the whole window on it and leaves the lower card on its minimum -- a heading with
    /// a clipped column header under it.
    /// </summary>
    private void PlaceSplitter()
    {
        if (IsDisposed || _lists.IsDisposed)
        {
            return;
        }

        var room = _lists.Height - _lists.Panel2MinSize - _lists.SplitterWidth;
        if (room < _lists.Panel1MinSize)
        {
            return;
        }

        _lists.SplitterDistance = Math.Clamp(_lists.Height / 2, _lists.Panel1MinSize, room);
    }

    private ListView CreateList(string title, string thirdColumn, UiGlyph glyph, out Panel card)

    {
        card = CreateCard();
        card.Dock = DockStyle.Fill;
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = this.LogicalToDeviceUnits(new Padding(14, 10, 14, 14)),
            Margin = Padding.Empty
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(36)));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, this.TextBoxHeight(8)));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var icon = new PictureBox
        {
            Image = UiIconFactory.Create(glyph, StorageHubTheme.Primary, 20, DeviceDpi / 96F),
            SizeMode = PictureBoxSizeMode.CenterImage,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = StorageHubTheme.CreateSectionFont(),
            ForeColor = StorageHubTheme.Text,
            Margin = Padding.Empty
        };
        var images = new ImageList { ImageSize = LogicalToDeviceUnits(new Size(18, 18)), ColorDepth = ColorDepth.Depth32Bit };
        images.Images.Add("enabled", UiIconFactory.Create(UiGlyph.Test, StorageHubTheme.Success, 18, DeviceDpi / 96F));
        images.Images.Add("disabled", UiIconFactory.Create(UiGlyph.Pause, StorageHubTheme.TextMuted, 18, DeviceDpi / 96F));
        images.Images.Add("run", UiIconFactory.Create(UiGlyph.Run, StorageHubTheme.Primary, 18, DeviceDpi / 96F));
        images.Images.Add("empty", UiIconFactory.Create(UiGlyph.More, StorageHubTheme.TextMuted, 18, DeviceDpi / 96F));
        var list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            SmallImageList = images,
            BackColor = StorageHubTheme.Surface,
            ForeColor = StorageHubTheme.Text,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            Dock = DockStyle.Fill,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 8, 0, 0))
        };
        StorageHubTheme.ConfigureList(list);
        list.Columns.Add(Ui.Sync.ColumnName, LogicalToDeviceUnits(360));
        list.Columns.Add(thirdColumn, LogicalToDeviceUnits(180));
        list.Columns.Add(Ui.Sync.ColumnUpdated, LogicalToDeviceUnits(180));
        grid.Controls.Add(icon, 0, 0);
        grid.Controls.Add(titleLabel, 1, 0);
        grid.Controls.Add(list, 0, 1);
        grid.SetColumnSpan(list, 2);
        card.Controls.Add(grid);
        return list;
    }

    private static Panel CreateCard() => new()
    {
        BackColor = StorageHubTheme.Surface,
        BorderStyle = BorderStyle.FixedSingle
    };

    private StorageHubButton CreateButton(string text, UiGlyph glyph, EventHandler handler, bool primary = false)
    {
        var button = new StorageHubButton
        {
            Text = text,
            Image = UiIconFactory.Create(glyph, primary ? Color.White : StorageHubTheme.Text, 18, DeviceDpi / 96F),
            TextImageRelation = TextImageRelation.ImageBeforeText,
            AutoSize = true,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 0, 8, 0))
        };
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
