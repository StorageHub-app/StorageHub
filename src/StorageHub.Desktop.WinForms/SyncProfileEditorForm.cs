using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>Edits persisted V6 sync profiles and drives the real preview/review/dispatch workflow.</summary>
public sealed class SyncProfileEditorForm : Form
{
    private readonly ISyncManagementAgentClient _syncClient;
    private readonly IRemoteStorageAgentClient _storageClient;
    private readonly bool _ownsClients;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly StorageHubChoiceField _profileChoice;
    private readonly StorageHubTextField _name;
    private readonly StorageHubCheckBox _enabled;
    private readonly StorageHubChoiceField _leftConnection;
    private readonly StorageHubTextField _leftRoot;
    private readonly StorageHubButton _leftBrowse;
    private readonly StorageHubChoiceField _rightConnection;
    private readonly StorageHubTextField _rightRoot;
    private readonly StorageHubButton _rightBrowse;
    private readonly SyncBehaviorPickerControl _behavior;
    private readonly StorageHubChoiceField _conflictPolicy;
    private readonly TextBox _includeGlobs;
    private readonly TextBox _excludeGlobs;
    private readonly StorageHubCheckBox _includeHiddenFiles;
    private readonly StorageHubNumberField _maximumDeletionCount;
    private readonly StorageHubNumberField _maximumDeletionPercentage;
    private readonly StorageHubNumberField _bufferSize;
    private readonly StorageHubCheckBox _allowNonAtomicDestinationWrites;
    private readonly Label _status;
    private readonly StorageHubButton _save;
    private readonly StorageHubButton _preview;
    private readonly SyncRunReviewControl _review;
    private readonly TabControl _tabs;
    private SyncProfileDocument? _currentProfile;
    private SyncRunSummary? _lastGeneratedRun;
    private bool _initialLoadStarted;
    private bool _suppressProfileSelection;
    private bool _disposed;

    public SyncProfileEditorForm()
        : this(
            new NamedPipeSyncManagementAgentClient(),
            new NamedPipeRemoteStorageAgentClient(),
            ownsClients: true)
    {
    }

    public SyncProfileEditorForm(
        ISyncManagementAgentClient syncClient,
        IRemoteStorageAgentClient storageClient,
        bool ownsClients = false)
    {
        _syncClient = syncClient ?? throw new ArgumentNullException(nameof(syncClient));
        _storageClient = storageClient ?? throw new ArgumentNullException(nameof(storageClient));
        _ownsClients = ownsClients;
        Text = Ui.Sync.EditorTitle;
        AccessibleName = Ui.Sync.EditorAccessibleName;
        AccessibleDescription = Ui.Sync.EditorAccessibleDescription;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = this.LogicalWindowSize(new Size(1000, 700));
        Size = this.LogicalWindowSize(new Size(1240, 860));
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = StorageHubTheme.Canvas;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        StorageHubTheme.Register(this);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = this.TextBoxHeight(51),
            ColumnCount = 3,
            Padding = this.LogicalToDeviceUnits(new Padding(18, 11, 18, 9)),
            BackColor = StorageHubTheme.Surface
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(330)));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty
        };
        title.Controls.Add(new Label
        {
            Text = Ui.Sync.SafeSynchronization,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 14F),
            ForeColor = StorageHubTheme.Text,
            Margin = Padding.Empty
        });
        title.Controls.Add(UiControlFactory.CreateDescription(
            Ui.Sync.SafeSynchronizationHint));
        header.Controls.Add(title, 0, 0);
        _profileChoice = new StorageHubChoiceField
        {
            Name = "SyncProfileChoice",
            // Anchored rather than filled: a field is one line tall, and stretching it to
            // the height of the header row left its border cut off by the row's edge.
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            AccessibleName = Ui.Sync.SavedProfile,
            Margin = this.LogicalToDeviceUnits(new Padding(8, 6, 8, 6))
        };
        _profileChoice.SelectedIndexChanged += ProfileSelectionChanged;
        header.Controls.Add(_profileChoice, 1, 0);
        var newProfile = new StorageHubButton { Text = Ui.Sync.NewProfile, AutoSize = true, Margin = this.LogicalToDeviceUnits(new Padding(0, 10, 0, 0)) };
        newProfile.Variant = StorageHubButtonVariant.Secondary;
        newProfile.Click += (_, _) => BeginNewProfile();
        header.Controls.Add(newProfile, 2, 0);

        _name = new StorageHubTextField
        {
            Name = "SyncProfileName",
            Text = Ui.Sync.NewProfileTitle,
            MaxLength = SyncManagementIpcLimits.MaximumDisplayNameLength
        };
        _enabled = new StorageHubCheckBox { Text = Ui.Sync.Enabled, AutoSize = true };
        _leftConnection = CreateChoice(Ui.Sync.LocationAConnection);
        _leftRoot = CreateRootTextBox(Ui.Sync.LocationAFolderAccessibleName);
        _leftRoot.Name = "LocationAFolder";
        _leftBrowse = CreateBrowseButton("BrowseLocationA", Ui.Sync.BrowseLocationA);
        _rightConnection = CreateChoice(Ui.Sync.LocationBConnection);
        _rightRoot = CreateRootTextBox(Ui.Sync.LocationBFolderAccessibleName);
        _rightRoot.Name = "LocationBFolder";
        _rightBrowse = CreateBrowseButton("BrowseLocationB", Ui.Sync.BrowseLocationB);
        _leftConnection.SelectedIndexChanged += LocationConnectionChanged;
        _rightConnection.SelectedIndexChanged += LocationConnectionChanged;
        _leftBrowse.Click += BrowseLeftClicked;
        _rightBrowse.Click += BrowseRightClicked;
        UpdateLocationSelectorState(_leftConnection, _leftRoot, _leftBrowse);
        UpdateLocationSelectorState(_rightConnection, _rightRoot, _rightBrowse);
        _behavior = new SyncBehaviorPickerControl
        {
            SelectedBehavior = SyncIpcBehavior.UpdateAToB
        };
        _conflictPolicy = CreateEnumChoice<SyncIpcConflictPolicy>(Ui.Sync.ConflictPolicy);
        _conflictPolicy.DisplayText = DescribeConflictPolicy;
        _conflictPolicy.SelectedItem = SyncIpcConflictPolicy.Block;
        _includeGlobs = CreateGlobTextBox(Ui.Sync.IncludeGlobFilters);
        _excludeGlobs = CreateGlobTextBox(Ui.Sync.ExcludeGlobFilters);
        _excludeGlobs.Lines = [".storagehub", ".storagehub/**", "**/.storagehub/**"];
        _includeHiddenFiles = new StorageHubCheckBox { Text = Ui.Sync.IncludeHiddenFiles, Checked = true, AutoSize = true };
        _maximumDeletionCount = CreateNumeric(
            SyncPresentationCatalog.DefaultMassDeleteItemLimit,
            1,
            SyncManagementIpcLimits.MaximumDeletionCount);
        _maximumDeletionPercentage = CreateNumeric(
            SyncPresentationCatalog.DefaultMassDeletePercentageLimit,
            0.01m,
            100m,
            decimalPlaces: 2,
            increment: 0.25m);
        _bufferSize = CreateNumeric(64 * 1024, 1, SyncManagementIpcLimits.MaximumTransferBufferSize);
        _allowNonAtomicDestinationWrites = new StorageHubCheckBox
        {
            Name = "AllowNonAtomicDestinationWrites",
            Text = Ui.Sync.AllowNonAtomicWritesCompatibility,
            AutoSize = true,
            AccessibleName = Ui.Sync.AllowNonAtomicWrites,
            ForeColor = StorageHubTheme.Warning
        };

        _tabs = new ThemedTabControl
        {
            Dock = DockStyle.Fill,
            AccessibleName = Ui.Sync.WorkflowAccessibleName
        };
        StorageHubTheme.ConfigureTabs(_tabs);
        _tabs.TabPages.Add(BuildProfilePage());
        _review = new SyncRunReviewControl(_syncClient);
        var previewPage = new TabPage(Ui.Sync.PlanAndRun)
        {
            Padding = this.LogicalToDeviceUnits(new Padding(5))
        };
        previewPage.Controls.Add(_review);
        _tabs.TabPages.Add(previewPage);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = this.TextBoxHeight(35),
            ColumnCount = 2,
            Padding = this.LogicalToDeviceUnits(new Padding(14, 9, 14, 8)),
            BackColor = StorageHubTheme.Surface
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _status = new Label
        {
            Name = "SyncProfileStatus",
            Text = Ui.Sync.EditorConnectsWhenShown,
            AutoSize = true,
            ForeColor = StorageHubTheme.TextMuted,
            Anchor = AnchorStyles.Left,
            AccessibleName = Ui.Sync.ProfileStatusAccessibleName
        };
        footer.Controls.Add(_status, 0, 0);
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        var close = new StorageHubButton { Text = Ui.Dialogs.ButtonClose, DialogResult = DialogResult.Cancel };
        close.Variant = StorageHubButtonVariant.Secondary;
        _save = new StorageHubButton { Name = "SaveSyncProfile", Text = Ui.Sync.SaveProfile, AutoSize = true };
        _save.Variant = StorageHubButtonVariant.Secondary;
        _save.Click += SaveClicked;
        _preview = new StorageHubButton { Name = "GenerateSyncPreview", Text = Ui.Sync.ReviewAndRun, AutoSize = true };
        _preview.Variant = StorageHubButtonVariant.Primary;
        _preview.Click += PreviewClicked;
        actions.Controls.Add(close);
        actions.Controls.Add(_save);
        actions.Controls.Add(_preview);
        footer.Controls.Add(actions, 1, 0);
        CancelButton = close;

        Controls.Add(_tabs);
        Controls.Add(footer);
        Controls.Add(header);
    }

    public SyncProfileDocument? CurrentProfile => _currentProfile;

    public SyncRunReviewControl Review => _review;

    public SyncRunSummary? LastGeneratedRun => _lastGeneratedRun;

    public string StatusText => _status.Text;

    /// <summary>Loads saved profiles and endpoints. Construction itself remains IPC-inert.</summary>
    public async Task LoadProfilesAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        _status.Text = Ui.Sync.LoadingProfiles;
        _status.ForeColor = StorageHubTheme.TextMuted;
        var profileTask = _syncClient.ListProfilesAsync(new SyncProfileListRequest(
            SyncManagementIpcContract.CurrentVersion,
            IncludeDisabled: true,
            MaximumCount: SyncManagementIpcLimits.MaximumProfileResults), linked.Token);
        var connectionTask = _storageClient.ListConnectionsAsync(new ConnectionListRequest(
            StorageIpcContract.CurrentVersion,
            IncludeDisabled: true,
            Limit: StorageIpcLimits.MaximumConnectionResults), linked.Token);

        SyncProfileListResponse? profiles = null;
        ConnectionListResponse? connections = null;
        Exception? profileError = null;
        Exception? connectionError = null;
        try
        {
            connections = await connectionTask.ConfigureAwait(true);
            ThrowIfFailure(connections.Failure);
            PopulateConnections(connections.Connections);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            connectionError = error;
        }

        try
        {
            profiles = await profileTask.ConfigureAwait(true);
            ThrowIfFailure(profiles.Failure);
            PopulateProfiles(profiles.Profiles);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            profileError = error;
        }

        if (profileError is null && connectionError is null)
        {
            _status.Text = profiles!.Profiles.Length == 0
                ? Ui.Sync.NoSavedProfileYet
                : Ui.Format(Ui.Sync.ProfilesLoadedFormat, profiles.Profiles.Length);
            _status.ForeColor = StorageHubTheme.Success;
            return;
        }

        _status.Text = (profileError, connectionError) switch
        {
            (not null, null) => Ui.Format(Ui.Sync.ProfilesUnavailableFormat, profileError.Message),
            (null, not null) => Ui.Format(Ui.Sync.ConnectionsUnavailableFormat, connectionError.Message),
            _ => Ui.Format(Ui.Sync.BothUnavailableFormat, profileError!.Message, connectionError!.Message)
        };
        _status.ForeColor = StorageHubTheme.Danger;
    }

    public async Task SelectProfileAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        if (profileId == Guid.Empty)
        {
            throw new ArgumentException("A sync profile ID is required.", nameof(profileId));
        }

        var response = await _syncClient.GetProfileAsync(new SyncProfileGetRequest(
            SyncManagementIpcContract.CurrentVersion,
            profileId), cancellationToken).ConfigureAwait(true);
        ThrowIfFailure(response.Failure);
        ApplyProfile(response.Profile ?? throw new InvalidDataException(Ui.Sync.AgentIncompleteProfile));
    }

    public async Task<SyncProfileDocument> SaveCurrentProfileAsync(
        CancellationToken cancellationToken = default)
    {
        var draft = BuildDraft();
        if (!draft.HasValidBounds)
        {
            throw new InvalidOperationException(
                Ui.Sync.CompleteLocationsHint);
        }

        SyncProfileMutationResponse response;
        if (_currentProfile is null)
        {
            var profileId = Guid.NewGuid();
            response = await _syncClient.CreateProfileAsync(new SyncProfileCreateRequest(
                SyncManagementIpcContract.CurrentVersion,
                profileId,
                draft), cancellationToken).ConfigureAwait(true);
        }
        else
        {
            response = await _syncClient.UpdateProfileAsync(new SyncProfileUpdateRequest(
                SyncManagementIpcContract.CurrentVersion,
                _currentProfile.ProfileId,
                _currentProfile.Revision,
                draft), cancellationToken).ConfigureAwait(true);
        }

        if (response.Outcome is not (SyncProfileMutationOutcome.Succeeded or SyncProfileMutationOutcome.AlreadyApplied))
        {
            throw new InvalidOperationException(response.Failure?.Message ?? Ui.Sync.ProfileSaveFailed);
        }

        var saved = response.Profile ?? throw new InvalidDataException(Ui.Sync.AgentNoRevision);
        ApplyProfile(saved);
        UpsertProfileChoice(saved);
        _status.Text = saved.Draft.AllowNonAtomicDestinationWrites
            ? Ui.Format(Ui.Sync.ProfileSavedNonAtomicFormat, saved.Revision)
            : Ui.Format(Ui.Sync.ProfileSavedFormat, saved.Revision);
        _status.ForeColor = saved.Draft.AllowNonAtomicDestinationWrites
            ? StorageHubTheme.Warning
            : StorageHubTheme.Success;
        return saved;
    }

    public async Task<SyncRunSummary> GeneratePreviewAsync(CancellationToken cancellationToken = default)
    {
        var profile = await SaveCurrentProfileAsync(cancellationToken).ConfigureAwait(true);
        _status.Text = Ui.Sync.ScanningLocations;
        _status.ForeColor = StorageHubTheme.TextMuted;
        var response = await _syncClient.GeneratePreviewAsync(new SyncPreviewGenerateRequest(
            SyncManagementIpcContract.CurrentVersion,
            profile.ProfileId,
            Guid.NewGuid()), cancellationToken).ConfigureAwait(true);
        ThrowIfFailure(response.Failure);
        var run = response.Run ?? throw new InvalidDataException(Ui.Sync.AgentNoRun);
        _lastGeneratedRun = run;
        var plan = response.Plan ?? throw new InvalidDataException(Ui.Sync.AgentNoPlanSummary);
        if (plan.SyncRunId != run.SyncRunId ||
            plan.PlanId != run.PlanId ||
            !string.Equals(plan.PlanSha256, run.PlanSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(Ui.Sync.PlanRunMismatch);
        }

        await _review.ShowPreviewAsync(run, cancellationToken).ConfigureAwait(true);
        _tabs.SelectedIndex = 1;

        // A disabled profile previews fine but never runs by itself, which is invisible once the
        // plan is on screen -- so say so here rather than leaving it to be discovered later.
        if (!profile.Draft.Enabled)
        {
            _status.Text = Ui.Sync.PreviewedWhileDisabled;
            _status.ForeColor = StorageHubTheme.Warning;
            return run;
        }

        _status.Text = profile.Draft.AllowNonAtomicDestinationWrites
            ? Ui.Sync.PlanReadyNonAtomic
            : Ui.Sync.PlanReady;
        _status.ForeColor = profile.Draft.AllowNonAtomicDestinationWrites
            ? StorageHubTheme.Warning
            : StorageHubTheme.Success;
        return run;
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_initialLoadStarted)
        {
            return;
        }

        _initialLoadStarted = true;
        try
        {
            await LoadProfilesAsync(_lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            ShowError(error);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _profileChoice.SelectedIndexChanged -= ProfileSelectionChanged;
            _lifetime.Cancel();
            _lifetime.Dispose();
            if (_ownsClients)
            {
                _syncClient.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _storageClient.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }

        base.Dispose(disposing);
    }

    private TabPage BuildProfilePage()
    {
        var page = new TabPage(Ui.Sync.ProfileTab) { Padding = new Padding(0) };
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = StorageHubTheme.Canvas,
            Padding = this.LogicalToDeviceUnits(new Padding(18))
        };
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Padding = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var intro = CreateSectionHeader(
            Ui.Sync.EditorHeading,
            Ui.Sync.EditorSubheading);
        content.Controls.Add(intro);

        var identity = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        identity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        identity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        identity.Controls.Add(CreateField(Ui.Sync.ProfileName, _name, Ui.Sync.ProfileNameHint), 0, 0);
        identity.Controls.Add(CreateField(Ui.Sync.ProfileState, _enabled, Ui.Sync.DisabledProfilesHint), 1, 0);
        content.Controls.Add(CreateCard(identity, this.LogicalToDeviceUnits(new Padding(16)), this.LogicalToDeviceUnits(new Padding(0, 0, 0, 14))));

        content.Controls.Add(CreateSectionHeader(
            Ui.Sync.StepLocations,
            Ui.Sync.LocationsRelativeHint));
        var locationGrid = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3 };
        locationGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        locationGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(112)));
        locationGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        locationGrid.Controls.Add(CreateLocationCard("A", "PRIMARY", _leftConnection, _leftRoot, _leftBrowse), 0, 0);
        var swap = new StorageHubButton { Text = Ui.Sync.SwapLocations, AutoSize = true, Anchor = AnchorStyles.None };
        swap.Variant = StorageHubButtonVariant.Secondary;
        swap.Click += (_, _) => SwapLocations();
        locationGrid.Controls.Add(swap, 1, 0);
        locationGrid.Controls.Add(CreateLocationCard("B", "PEER", _rightConnection, _rightRoot, _rightBrowse), 2, 0);
        content.Controls.Add(locationGrid);

        content.Controls.Add(CreateSectionHeader(
            Ui.Sync.StepBehavior,
            Ui.Sync.SelectPresetHint));
        content.Controls.Add(CreateCard(_behavior, this.LogicalToDeviceUnits(new Padding(14)), this.LogicalToDeviceUnits(new Padding(0, 0, 0, 14))));

        content.Controls.Add(CreateSectionHeader(
            Ui.Sync.StepSafety,
            Ui.Sync.FiltersHint));
        var advanced = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        advanced.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        advanced.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var policy = CreateFormTable();
        UiControlFactory.AddLabeledRow(policy, Ui.Sync.ConflictsTab, _conflictPolicy, Ui.Sync.ConflictPolicyHint);
        UiControlFactory.AddLabeledRow(policy, Ui.Sync.MaximumDeletes, _maximumDeletionCount, Ui.Sync.ItemLimitHint);
        UiControlFactory.AddLabeledRow(policy, Ui.Sync.MaximumBaselinePercent, _maximumDeletionPercentage, Ui.Sync.PercentageLimitHint);
        UiControlFactory.AddLabeledRow(policy, Ui.Sync.TransferBuffer, _bufferSize, Ui.Sync.TransferBufferHint);
        UiControlFactory.AddLabeledRow(
            policy,
            Ui.Sync.NonAtomicWrites,
            _allowNonAtomicDestinationWrites,
            Ui.Sync.AllowNonAtomicWritesWarning);
        var scope = CreateFormTable();
        // The filter lists are the one multi-line input in the app, so they wear the field
        // chrome through a host rather than carrying it themselves.
        UiControlFactory.AddLabeledRow(
            scope,
            Ui.Sync.IncludeGlobs,
            new StorageHubFieldHost(_includeGlobs) { Height = _includeGlobs.Height + LogicalToDeviceUnits(12) },
            Ui.Sync.GlobHint);
        UiControlFactory.AddLabeledRow(
            scope,
            Ui.Sync.ExcludeGlobs,
            new StorageHubFieldHost(_excludeGlobs) { Height = _excludeGlobs.Height + LogicalToDeviceUnits(12) },
            Ui.Sync.StagingExcludedHint);
        UiControlFactory.AddLabeledRow(scope, Ui.Sync.HiddenContent, _includeHiddenFiles, Ui.Sync.HiddenFilesHint);
        advanced.Controls.Add(CreateCard(policy, this.LogicalToDeviceUnits(new Padding(10)), this.LogicalToDeviceUnits(new Padding(0, 0, 7, 0))), 0, 0);
        advanced.Controls.Add(CreateCard(scope, this.LogicalToDeviceUnits(new Padding(10)), this.LogicalToDeviceUnits(new Padding(7, 0, 0, 0))), 1, 0);
        content.Controls.Add(advanced);

        panel.Controls.Add(content);
        page.Controls.Add(panel);
        return page;
    }

    private TableLayoutPanel CreateSectionHeader(string title, string description)
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 5, 0, 7))
        };
        header.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 12F),
            ForeColor = StorageHubTheme.Text,
            Margin = Padding.Empty
        });
        header.Controls.Add(new Label
        {
            Text = description,
            AutoSize = true,
            MaximumSize = new Size(LogicalToDeviceUnits(940), 0),
            ForeColor = StorageHubTheme.TextMuted,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 2, 0, 0))
        });
        return header;
    }

    private static Panel CreateCard(Control content, Padding padding, Padding margin)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = StorageHubTheme.Surface,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = padding,
            Margin = margin
        };
        content.Dock = DockStyle.Top;
        card.Controls.Add(content);
        return card;
    }

    private TableLayoutPanel CreateField(string label, Control control, string help)
    {
        var field = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 0, 12, 0))
        };
        field.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Font = StorageHubTheme.CreateSectionFont(),
            ForeColor = StorageHubTheme.Text,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 0, 0, 5))
        });
        control.Dock = DockStyle.Top;
        control.Margin = Padding.Empty;
        field.Controls.Add(control);
        var helpLabel = new Label
        {
            Text = help,
            AutoSize = true,
            ForeColor = StorageHubTheme.TextMuted,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 5, 0, 0))
        };

        // Bounded to the field so the text wraps rather than running past the column. Without
        // this an AutoSize label simply grows sideways, which clips any translation longer than
        // the English it was laid out against.
        field.ClientSizeChanged += (_, _) =>
            helpLabel.MaximumSize = new Size(Math.Max(1, field.ClientSize.Width - helpLabel.Margin.Horizontal), 0);
        field.Controls.Add(helpLabel);
        return field;
    }

    private Panel CreateLocationCard(
        string location,
        string badge,
        StorageHubChoiceField connection,
        StorageHubTextField root,
        Button browse)
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Margin = Padding.Empty
        };
        var heading = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        heading.Controls.Add(new Label
        {
            Text = Ui.Format(Ui.Sync.LocationHeadingFormat, location),
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 13F),
            ForeColor = StorageHubTheme.Text,
            Margin = Padding.Empty
        }, 0, 0);
        heading.Controls.Add(new Label
        {
            Text = badge,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 7.5F),
            ForeColor = StorageHubTheme.Primary,
            BackColor = StorageHubTheme.SurfaceMuted,
            Padding = this.LogicalToDeviceUnits(new Padding(7, 3, 7, 3)),
            Margin = Padding.Empty
        }, 1, 0);
        body.Controls.Add(heading);
        body.Controls.Add(new Label
        {
            Text = Ui.Sync.SavedConnection,
            AutoSize = true,
            ForeColor = StorageHubTheme.TextMuted,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 14, 0, 4))
        });
        connection.Dock = DockStyle.Top;
        connection.Margin = Padding.Empty;
        body.Controls.Add(connection);
        body.Controls.Add(new Label
        {
            Text = Ui.Sync.FolderInsideConnection,
            AutoSize = true,
            ForeColor = StorageHubTheme.TextMuted,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 12, 0, 4))
        });
        body.Controls.Add(CreateFolderSelector(root, browse));
        body.Controls.Add(new Label
        {
            Text = Ui.Sync.EmptyMeansRoot,
            AutoSize = true,
            ForeColor = StorageHubTheme.TextMuted,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 5, 0, 0))
        });
        return CreateCard(body, this.LogicalToDeviceUnits(new Padding(16)), new Padding(0));
    }

    private static TableLayoutPanel CreateFormTable()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Margin = Padding.Empty
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, table.LogicalToDeviceUnits(150)));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return table;
    }

    private static string DescribeConflictPolicy(object item) => item switch
    {
        SyncIpcConflictPolicy.Block => Ui.Sync.BlockAndReview,
        SyncIpcConflictPolicy.KeepBoth => Ui.Sync.KeepBoth,
        _ => item.ToString() ?? string.Empty
    };

    private SyncProfileDraftDocument BuildDraft()
    {
        var left = (_leftConnection.SelectedItem as ConnectionChoice)?.Connection.ConnectionId ?? Guid.Empty;
        var right = (_rightConnection.SelectedItem as ConnectionChoice)?.Connection.ConnectionId ?? Guid.Empty;
        return new SyncProfileDraftDocument(
            _name.Text.Trim(),
            left,
            _leftRoot.Text.Trim(),
            right,
            _rightRoot.Text.Trim(),
            _behavior.SelectedBehavior,
            SelectedEnum<SyncIpcConflictPolicy>(_conflictPolicy),
            ParseGlobs(_includeGlobs),
            ParseGlobs(_excludeGlobs),
            _includeHiddenFiles.Checked,
            decimal.ToInt32(_maximumDeletionCount.Value),
            _maximumDeletionPercentage.Value,
            decimal.ToInt32(_bufferSize.Value),
            _enabled.Checked)
        {
            AllowNonAtomicDestinationWrites = _allowNonAtomicDestinationWrites.Checked
        };
    }

    private void ApplyProfile(SyncProfileDocument profile)
    {
        _currentProfile = profile;
        _name.Text = profile.Draft.DisplayName;
        _enabled.Checked = profile.Draft.Enabled;
        SelectConnection(_leftConnection, profile.Draft.LeftConnectionId);
        _leftRoot.Text = profile.Draft.LeftRoot;
        SelectConnection(_rightConnection, profile.Draft.RightConnectionId);
        _rightRoot.Text = profile.Draft.RightRoot;
        _behavior.SelectedBehavior = profile.Draft.Behavior;
        _conflictPolicy.SelectedItem = profile.Draft.ConflictPolicy;
        _includeGlobs.Lines = profile.Draft.IncludeGlobs;
        _excludeGlobs.Lines = profile.Draft.ExcludeGlobs;
        _includeHiddenFiles.Checked = profile.Draft.IncludeHiddenFiles;
        _maximumDeletionCount.Value = profile.Draft.MaximumDeletionCount;
        _maximumDeletionPercentage.Value = profile.Draft.MaximumDeletionPercentage;
        _bufferSize.Value = profile.Draft.TransferBufferSize;
        _allowNonAtomicDestinationWrites.Checked = profile.Draft.AllowNonAtomicDestinationWrites;
        SelectProfileChoice(profile.ProfileId);
        _status.Text = Ui.Format(Ui.Sync.ProfileRevisionLoadedFormat, profile.Revision);
        _status.ForeColor = StorageHubTheme.TextMuted;
    }

    private void BeginNewProfile()
    {
        _currentProfile = null;
        _suppressProfileSelection = true;
        try
        {
            _profileChoice.SelectedIndex = _profileChoice.Items.Count == 0 ? -1 : 0;
        }
        finally
        {
            _suppressProfileSelection = false;
        }

        _name.Text = Ui.Sync.NewProfileTitle;
        _enabled.Checked = false;
        _leftRoot.Clear();
        _rightRoot.Clear();
        _behavior.SelectedBehavior = SyncIpcBehavior.UpdateAToB;
        _conflictPolicy.SelectedItem = SyncIpcConflictPolicy.Block;
        _includeGlobs.Clear();
        _excludeGlobs.Lines = [".storagehub", ".storagehub/**", "**/.storagehub/**"];
        _includeHiddenFiles.Checked = true;
        _maximumDeletionCount.Value = SyncPresentationCatalog.DefaultMassDeleteItemLimit;
        _maximumDeletionPercentage.Value = SyncPresentationCatalog.DefaultMassDeletePercentageLimit;
        _bufferSize.Value = 64 * 1024;
        _allowNonAtomicDestinationWrites.Checked = false;
        if (_leftConnection.Items.Count > 0)
        {
            _leftConnection.SelectedIndex = 0;
        }

        if (_rightConnection.Items.Count > 1)
        {
            _rightConnection.SelectedIndex = 1;
        }
        else if (_rightConnection.Items.Count > 0)
        {
            _rightConnection.SelectedIndex = 0;
        }

        _tabs.SelectedIndex = 0;
        _status.Text = Ui.Sync.NewProfileDraft;
        _status.ForeColor = StorageHubTheme.TextMuted;
    }

    private void PopulateProfiles(IEnumerable<SyncProfileSummary> profiles)
    {
        var selected = _currentProfile?.ProfileId;
        _suppressProfileSelection = true;
        try
        {
            _profileChoice.Items.Clear();
            _profileChoice.Items.Add(ProfileChoice.New);
            foreach (var profile in profiles.OrderBy(static profile => profile.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                _profileChoice.Items.Add(new ProfileChoice(profile.ProfileId, profile.DisplayName, profile.Enabled));
            }

            _profileChoice.SelectedIndex = 0;
            if (selected is { } profileId)
            {
                SelectProfileChoice(profileId);
            }
        }
        finally
        {
            _suppressProfileSelection = false;
        }
    }

    private void PopulateConnections(IEnumerable<ConnectionSummary> connections)
    {
        var choices = connections
            .Where(static connection => connection.Type == ConnectionProfileType.Storage)
            .OrderByDescending(static connection => connection.IsFavorite)
            .ThenBy(static connection => connection.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(static connection => new ConnectionChoice(connection))
            .ToArray();
        _leftConnection.Items.Clear();
        _rightConnection.Items.Clear();
        _leftConnection.Items.AddRange(choices.Cast<object>().ToArray());
        _rightConnection.Items.AddRange(choices.Cast<object>().ToArray());
        if (choices.Length > 0)
        {
            _leftConnection.SelectedIndex = 0;
            _rightConnection.SelectedIndex = choices.Length > 1 ? 1 : 0;
        }
    }

    private void UpsertProfileChoice(SyncProfileDocument profile)
    {
        _suppressProfileSelection = true;
        try
        {
            for (var index = 1; index < _profileChoice.Items.Count; index++)
            {
                if (_profileChoice.Items[index] is ProfileChoice choice && choice.ProfileId == profile.ProfileId)
                {
                    _profileChoice.Items[index] = new ProfileChoice(
                        profile.ProfileId,
                        profile.Draft.DisplayName,
                        profile.Draft.Enabled);
                    _profileChoice.SelectedIndex = index;
                    return;
                }
            }

            _profileChoice.Items.Add(new ProfileChoice(
                profile.ProfileId,
                profile.Draft.DisplayName,
                profile.Draft.Enabled));
            _profileChoice.SelectedIndex = _profileChoice.Items.Count - 1;
        }
        finally
        {
            _suppressProfileSelection = false;
        }
    }

    private void SelectProfileChoice(Guid profileId)
    {
        for (var index = 1; index < _profileChoice.Items.Count; index++)
        {
            if (_profileChoice.Items[index] is ProfileChoice choice && choice.ProfileId == profileId)
            {
                _profileChoice.SelectedIndex = index;
                return;
            }
        }
    }

    private static void SelectConnection(StorageHubChoiceField combo, Guid connectionId)
    {
        for (var index = 0; index < combo.Items.Count; index++)
        {
            if (combo.Items[index] is ConnectionChoice choice && choice.Connection.ConnectionId == connectionId)
            {
                combo.SelectedIndex = index;
                return;
            }
        }

        throw new InvalidOperationException(Ui.Sync.ConnectionMissing);
    }

    private void SwapLocations()
    {
        var connection = _leftConnection.SelectedItem;
        var root = _leftRoot.Text;
        _leftConnection.SelectedItem = _rightConnection.SelectedItem;
        _leftRoot.Text = _rightRoot.Text;
        _rightConnection.SelectedItem = connection;
        _rightRoot.Text = root;
    }

    private void LocationConnectionChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _leftConnection))
        {
            _leftRoot.Clear();
        }
        else if (ReferenceEquals(sender, _rightConnection))
        {
            _rightRoot.Clear();
        }

        UpdateLocationSelectorState(_leftConnection, _leftRoot, _leftBrowse);
        UpdateLocationSelectorState(_rightConnection, _rightRoot, _rightBrowse);
    }

    private void BrowseLeftClicked(object? sender, EventArgs e) =>
        BrowseLocation(_leftConnection, _leftRoot, Ui.Sync.LocationA);

    private void BrowseRightClicked(object? sender, EventArgs e) =>
        BrowseLocation(_rightConnection, _rightRoot, Ui.Sync.LocationB);

    private void BrowseLocation(StorageHubChoiceField connectionChoice, StorageHubTextField root, string locationName)
    {
        if (connectionChoice.SelectedItem is not ConnectionChoice choice)
        {
            _status.Text = Ui.Format(Ui.Sync.SelectConnectionForLocationFormat, locationName);
            _status.ForeColor = StorageHubTheme.Danger;
            return;
        }

        using var picker = new SyncLocationPickerForm(
            _storageClient,
            choice.Connection,
            root.Text.Trim(),
            locationName);
        if (picker.ShowDialog(this) == DialogResult.OK)
        {
            root.Text = picker.SelectedRelativePath;
            _status.Text = picker.SelectedRelativePath.Length == 0
                ? $"{locationName} uses the root of {choice.Connection.DisplayName}."
                : $"{locationName} folder selected: {picker.SelectedRelativePath}";
            _status.ForeColor = StorageHubTheme.Success;
        }

    }

    private async void ProfileSelectionChanged(object? sender, EventArgs e)
    {
        if (_suppressProfileSelection)
        {
            return;
        }

        if (_profileChoice.SelectedItem is not ProfileChoice choice || choice.ProfileId == Guid.Empty)
        {
            BeginNewProfile();
            return;
        }

        try
        {
            await SelectProfileAsync(choice.ProfileId, _lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            ShowError(error);
        }
    }

    private async void SaveClicked(object? sender, EventArgs e)
    {
        await RunBusyAsync(
            token => SaveCurrentProfileAsync(token),
            Ui.Sync.SavingProfile).ConfigureAwait(true);
    }

    private async void PreviewClicked(object? sender, EventArgs e)
    {
        await RunBusyAsync(
            token => GeneratePreviewAsync(token),
            Ui.Sync.PreparingPlan).ConfigureAwait(true);
    }

    private async Task RunBusyAsync<T>(Func<CancellationToken, Task<T>> action, string status)
    {
        _save.Enabled = false;
        _preview.Enabled = false;
        _status.Text = status;
        _status.ForeColor = StorageHubTheme.TextMuted;
        try
        {
            _ = await action(_lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            ShowError(error);
        }
        finally
        {
            if (!_disposed && !IsDisposed && !Disposing)
            {
                _save.Enabled = true;
                _preview.Enabled = true;
            }
        }
    }

    private void ShowError(Exception error)
    {
        _status.Text = error.Message;
        _status.ForeColor = StorageHubTheme.Danger;
    }

    private static void ThrowIfFailure(StorageIpcFailure? failure)
    {
        if (failure is not null)
        {
            throw new InvalidOperationException(SyncFailureMessages.Describe(failure));
        }
    }

    private StorageHubChoiceField CreateChoice(string accessibleName) => new()
    {
        Width = LogicalToDeviceUnits(320),
        AccessibleName = accessibleName
    };

    private static StorageHubButton CreateBrowseButton(string name, string accessibleName)
    {
        var button = new StorageHubButton
        {
            Name = name,
            Text = Ui.Sync.Browse,
            AutoSize = true,
            Enabled = false,
            AccessibleName = accessibleName,
            AccessibleDescription = Ui.Sync.BrowseFoldersHint
        };
        button.Variant = StorageHubButtonVariant.Secondary;
        return button;
    }

    private TableLayoutPanel CreateFolderSelector(StorageHubTextField root, Button browse)
    {
        var selector = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            Margin = Padding.Empty
        };
        selector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        selector.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.Dock = DockStyle.Fill;
        root.Margin = this.LogicalToDeviceUnits(new Padding(0, 0, 8, 0));
        browse.Margin = Padding.Empty;
        selector.Controls.Add(root, 0, 0);
        selector.Controls.Add(browse, 1, 0);
        return selector;
    }

    private static void UpdateLocationSelectorState(StorageHubChoiceField connection, StorageHubTextField root, Button browse)
    {
        var choice = connection.SelectedItem as ConnectionChoice;
        var canBrowse = choice?.Connection.IsEnabled == true;
        root.Enabled = canBrowse;
        browse.Enabled = canBrowse;
        root.PlaceholderText = choice switch
        {
            null => Ui.Sync.SelectConnectionFirst,
            { Connection.IsEnabled: false } => Ui.Sync.ConnectionDisabled,
            { Connection.FolderPath.Length: > 0 } => Ui.Format(Ui.Sync.ConnectionRootFormat, choice.Connection.FolderPath),
            _ => Ui.Sync.ConnectionRootHint
        };
    }

    private StorageHubChoiceField CreateEnumChoice<T>(string accessibleName) where T : struct, Enum
    {
        var combo = CreateChoice(accessibleName);
        combo.Items.AddRange(Enum.GetValues<T>().Cast<object>().ToArray());
        if (combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }

        return combo;
    }

    private static StorageHubTextField CreateRootTextBox(string accessibleName) => new()
    {
        MaxLength = SyncManagementIpcLimits.MaximumRelativeRootLength,
        PlaceholderText = Ui.Sync.ConnectionRelativePath,
        AccessibleName = accessibleName
    };

    /// <summary>
    /// A filter list is several lines, which is the one shape the app's own field does not take.
    /// It stays a text box, stripped of its border, and the caller wraps it in the chrome every
    /// other input carries.
    /// </summary>
    private TextBox CreateGlobTextBox(string accessibleName) => new()
    {
        Multiline = true,
        BorderStyle = BorderStyle.None,
        BackColor = StorageHubTheme.Input,
        ForeColor = StorageHubTheme.Text,
        ScrollBars = ScrollBars.Vertical,
        Height = this.TextBoxHeight(30),
        MaxLength = SyncManagementIpcLimits.MaximumFilterCount * SyncManagementIpcLimits.MaximumGlobLength,
        AccessibleName = accessibleName
    };

    private static string[] ParseGlobs(TextBox textBox) => textBox.Lines
        .Select(static line => line.Trim())
        .Where(static line => line.Length > 0)
        .ToArray();

    private StorageHubNumberField CreateNumeric(
        decimal value,
        decimal minimum,
        decimal maximum,
        int decimalPlaces = 0,
        decimal increment = 1) => new()
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = Math.Clamp(value, minimum, maximum),
            DecimalPlaces = decimalPlaces,
            Increment = increment,
            ThousandsSeparator = true,
            Width = LogicalToDeviceUnits(180)
        };

    private static T SelectedEnum<T>(StorageHubChoiceField combo) where T : struct, Enum =>
        combo.SelectedItem is T value
            ? value
            : throw new InvalidOperationException(Ui.Format(Ui.Sync.SelectValidValueFormat, typeof(T).Name));

    private sealed record ProfileChoice(Guid ProfileId, string DisplayName, bool Enabled)
    {
        public static ProfileChoice New { get; } = new(Guid.Empty, Ui.Sync.CreateNewProfile, false);

        public override string ToString() => ProfileId == Guid.Empty
            ? DisplayName
            : Enabled ? DisplayName : $"{DisplayName} (disabled)";
    }

    private sealed record ConnectionChoice(ConnectionSummary Connection)
    {
        public override string ToString() =>
            $"[{Connection.Provider}] {Connection.DisplayName}{(Connection.IsEnabled ? string.Empty : " (disabled)")}";
    }
}
