using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>Selects a canonical connection-relative folder without exposing provider credentials.</summary>
internal sealed class SyncLocationPickerForm : Form
{
    private const int MaximumVisibleEntries = 2_000;
    private readonly IRemoteStorageAgentClient _client;
    private readonly ConnectionSummary _connection;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly StorageHubTextField _address;
    private readonly ListView _folders;
    private readonly StorageHubButton _up;
    private readonly StorageHubButton _refresh;
    private readonly StorageHubButton _loadMore;
    private readonly StorageHubButton _select;
    private readonly Label _status;
    private string? _continuationToken;
    private string _currentPath = string.Empty;
    private bool _hasLoadedCurrentPath;
    private bool _loading;
    private bool _initialLoadStarted;
    private bool _disposed;

    public SyncLocationPickerForm(
        IRemoteStorageAgentClient client,
        ConnectionSummary connection,
        string initialRelativePath,
        string locationName)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        ArgumentException.ThrowIfNullOrWhiteSpace(locationName);
        SelectedRelativePath = RemoteBrowserPath.TryNormalize(initialRelativePath, out var normalized, out _)
            ? normalized
            : string.Empty;

        Text = Ui.Format(Ui.Sync.PickerTitleWithConnectionFormat, locationName, connection.DisplayName);
        AccessibleName = Ui.Format(Ui.Sync.PickerTitleFormat, locationName);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = this.LogicalWindowSize(new Size(680, 480));
        Size = this.LogicalWindowSize(new Size(780, 570));
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = StorageHubTheme.Canvas;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        StorageHubTheme.Register(this);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = this.TextBoxHeight(50),
            ColumnCount = 1,
            Padding = this.LogicalToDeviceUnits(new Padding(18, 10, 18, 8)),
            BackColor = StorageHubTheme.Surface
        };
        header.Controls.Add(UiControlFactory.CreateSectionTitle($"{connection.DisplayName} folders"), 0, 0);
        header.Controls.Add(UiControlFactory.CreateDescription(
            Ui.Format(Ui.Sync.PickerBrowseHintFormat, connection.Provider)), 0, 1);

        var navigation = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = this.TextBoxHeight(22),
            ColumnCount = 4,
            Padding = this.LogicalToDeviceUnits(new Padding(12, 8, 12, 6)),
            BackColor = StorageHubTheme.SurfaceMuted
        };
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _up = CreateSecondaryButton("Up");
        _up.Name = "SyncLocationUp";
        _up.Click += async (_, _) => await NavigateAsync(RemoteBrowserPath.GetParent(_currentPath)).ConfigureAwait(true);
        _address = new StorageHubTextField
        {
            Name = "SyncLocationAddress",
            Dock = DockStyle.Fill,
            PlaceholderText = Ui.Sync.PickerConnectionRoot,
            AccessibleName = Ui.Sync.PickerAddressAccessibleName,
            Margin = this.LogicalToDeviceUnits(new Padding(8, 3, 8, 0))
        };
        _address.KeyDown += AddressKeyDown;
        _refresh = CreateSecondaryButton(Ui.Sync.PickerRefresh);
        _refresh.Click += async (_, _) => await NavigateAsync(_currentPath).ConfigureAwait(true);
        var root = CreateSecondaryButton(Ui.Sync.PickerRoot);
        root.Click += async (_, _) => await NavigateAsync(string.Empty).ConfigureAwait(true);
        navigation.Controls.Add(_up, 0, 0);
        navigation.Controls.Add(_address, 1, 0);
        navigation.Controls.Add(root, 2, 0);
        navigation.Controls.Add(_refresh, 3, 0);

        _folders = new ListView
        {
            Name = "SyncLocationFolders",
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            AccessibleName = Ui.Sync.PickerFoldersAccessibleName
        };
        StorageHubTheme.ConfigureList(_folders);
        _folders.Columns.Add(Ui.Sync.PickerFolder, LogicalToDeviceUnits(430));
        _folders.Columns.Add(Ui.Sync.PickerPath, LogicalToDeviceUnits(280));
        _folders.DoubleClick += FolderDoubleClicked;
        _folders.KeyDown += FoldersKeyDown;

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = this.TextBoxHeight(37),
            ColumnCount = 3,
            Padding = this.LogicalToDeviceUnits(new Padding(12, 8, 12, 8)),
            BackColor = StorageHubTheme.Surface
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _status = new Label
        {
            Text = Ui.Sync.PickerLoadingFolders,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = StorageHubTheme.TextMuted,
            AccessibleName = Ui.Sync.PickerStatusAccessibleName
        };
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        _loadMore = CreateSecondaryButton(Ui.Sync.PickerLoadMore);
        _loadMore.Enabled = false;
        _loadMore.Click += async (_, _) => await LoadPageAsync(append: true).ConfigureAwait(true);
        var cancel = CreateSecondaryButton("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        _select = new StorageHubButton { Name = "SelectSyncLocation", Text = Ui.Sync.PickerSelectFolder, AutoSize = true };
        _select.Variant = StorageHubButtonVariant.Primary;
        _select.Click += (_, _) =>
        {
            SelectedRelativePath = _currentPath;
            DialogResult = DialogResult.OK;
        };
        actions.Controls.Add(cancel);
        actions.Controls.Add(_select);
        footer.Controls.Add(_status, 0, 0);
        footer.Controls.Add(_loadMore, 1, 0);
        footer.Controls.Add(actions, 2, 0);

        Controls.Add(_folders);
        Controls.Add(footer);
        Controls.Add(navigation);
        Controls.Add(header);
        AcceptButton = _select;
        CancelButton = cancel;
    }

    public string SelectedRelativePath { get; private set; }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_initialLoadStarted)
        {
            return;
        }

        _initialLoadStarted = true;
        await NavigateAsync(SelectedRelativePath).ConfigureAwait(true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _lifetime.Cancel();
            _lifetime.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task NavigateAsync(string path)
    {
        if (!RemoteBrowserPath.TryNormalize(path, out var normalized, out var error))
        {
            ShowError(error ?? Ui.Sync.PickerInvalidPath);
            return;
        }

        _currentPath = normalized;
        _hasLoadedCurrentPath = false;
        _continuationToken = null;
        _folders.Items.Clear();
        await LoadPageAsync(append: false).ConfigureAwait(true);
    }

    private async Task LoadPageAsync(bool append)
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        SetBusy(true);
        _status.Text = append ? Ui.Sync.PickerLoadingMoreFolders : Ui.Sync.PickerLoadingFolders;
        _status.ForeColor = StorageHubTheme.TextMuted;
        try
        {
            var response = await _client.ListStorageAsync(new StorageListPageRequest(
                StorageIpcContract.CurrentVersion,
                _connection.ConnectionId,
                _currentPath,
                StorageIpcLimits.MaximumStableIdentityPageSize,
                append ? _continuationToken : null,
                IncludeVersions: false,
                Recursive: false), _lifetime.Token).ConfigureAwait(true);
            if (response.Failure is not null)
            {
                ShowError(response.Failure.Message);
                return;
            }

            _currentPath = response.RelativePath;
            _hasLoadedCurrentPath = true;
            _address.Text = _currentPath;
            foreach (var entry in response.Entries.Where(static entry => entry.IsContainer))
            {
                if (_folders.Items.Count >= MaximumVisibleEntries)
                {
                    _continuationToken = null;
                    ShowError(Ui.Sync.PickerDisplayLimit);
                    return;
                }

                var item = new ListViewItem(entry.Name) { Tag = entry.RelativePath };
                item.SubItems.Add(entry.RelativePath);
                _folders.Items.Add(item);
            }

            _continuationToken = response.ContinuationToken;
            _status.Text = _currentPath.Length == 0
                ? Ui.Format(Ui.Sync.PickerRootCountFormat, _folders.Items.Count)
                : $"{_currentPath} - {_folders.Items.Count} folder(s) shown.";
            _status.ForeColor = StorageHubTheme.TextMuted;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error) when (RemoteBrowserErrors.IsExpected(error))
        {
            ShowError(RemoteBrowserErrors.ForException(error));
        }
        finally
        {
            _loading = false;
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _up.Enabled = !busy && _currentPath.Length > 0;
        _refresh.Enabled = !busy;
        _address.Enabled = !busy;
        _folders.Enabled = !busy;
        _select.Enabled = !busy && _hasLoadedCurrentPath;
        _loadMore.Enabled = !busy && _continuationToken is not null;
    }

    private void ShowError(string message)
    {
        _status.Text = message;
        _status.ForeColor = StorageHubTheme.Danger;
    }

    private async void AddressKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;
        await NavigateAsync(_address.Text).ConfigureAwait(true);
    }

    private async void FolderDoubleClicked(object? sender, EventArgs e) => await OpenSelectedFolderAsync().ConfigureAwait(true);

    private async void FoldersKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            await OpenSelectedFolderAsync().ConfigureAwait(true);
        }
    }

    private async Task OpenSelectedFolderAsync()
    {
        if (_folders.SelectedItems.Count == 1 && _folders.SelectedItems[0].Tag is string path)
        {
            await NavigateAsync(path).ConfigureAwait(true);
        }
    }

    private static StorageHubButton CreateSecondaryButton(string text)
    {
        var button = new StorageHubButton { Text = text, AutoSize = true };
        button.Variant = StorageHubButtonVariant.Secondary;
        return button;
    }
}
