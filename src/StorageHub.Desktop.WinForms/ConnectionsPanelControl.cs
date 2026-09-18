using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

internal sealed record ConnectionActivationEventArgs(ConnectionSummary Connection, bool InNewPane);

/// <summary>
/// The shell's saved-connection panel: search at the top, a grouped list in the middle, and the
/// selected connection's details at the bottom.
///
/// This is the only saved-connection list in the app. The Connection Manager is a pure editor that
/// this panel opens; keeping one list means the shell and the editor cannot disagree about what is
/// saved, which is exactly what the dialog's two parallel lists used to do.
/// </summary>
internal sealed class ConnectionsPanelControl : UserControl
{
    private readonly IRemoteStorageAgentClient _storageClient;
    private readonly IRemoteConnectionProfileClient _profileClient;
    private readonly IRemoteSecretVaultClient _secretClient;
    private readonly ConnectionManagerController _controller;
    private readonly bool _ownsClients;
    private readonly CancellationTokenSource _lifetime = new();

    private readonly ConnectionSidebarControl _sidebar;
    private readonly ConnectionDetailView _detail;
    private readonly StorageHubTextField _searchBox;
    private readonly Label _status;
    private readonly ContextMenuStrip _rowMenu;

    private readonly List<ConnectionCardModel> _cards = [];
    private readonly Dictionary<Guid, ConnectionSummary> _summaries = [];
    private ConnectionCardModel? _menuTarget;
    private CancellationTokenSource? _detailLoad;
    private int _refreshing;

    internal ConnectionsPanelControl()
        : this(
            new NamedPipeRemoteStorageAgentClient(),
            new NamedPipeRemoteConnectionProfileClient(),
            new NamedPipeRemoteSecretVaultClient(),
            ownsClients: true)
    {
    }

    internal ConnectionsPanelControl(
        IRemoteStorageAgentClient storageClient,
        IRemoteConnectionProfileClient profileClient,
        IRemoteSecretVaultClient secretClient)
        : this(storageClient, profileClient, secretClient, ownsClients: false)
    {
    }

    private ConnectionsPanelControl(
        IRemoteStorageAgentClient storageClient,
        IRemoteConnectionProfileClient profileClient,
        IRemoteSecretVaultClient secretClient,
        bool ownsClients)
    {
        _storageClient = storageClient ?? throw new ArgumentNullException(nameof(storageClient));
        _profileClient = profileClient ?? throw new ArgumentNullException(nameof(profileClient));
        _secretClient = secretClient ?? throw new ArgumentNullException(nameof(secretClient));
        _ownsClients = ownsClients;
        _controller = new ConnectionManagerController(_profileClient, _secretClient);

        Dock = DockStyle.Fill;
        BackColor = StorageHubTheme.Surface;
        AccessibleName = Ui.Connections.PanelTitle;
        AccessibleDescription = Ui.Connections.PanelAccessibleDescription;

        _sidebar = new ConnectionSidebarControl { ShowRowActions = true };
        _sidebar.ConnectionSelected += (_, card) => ShowDetail(card.ConnectionId);
        _sidebar.ConnectionActivated += (_, card) => RaiseActivation(card, inNewPane: false);
        _sidebar.ConnectionEditRequested += (_, card) => RaiseEdit(card.ConnectionId);
        _sidebar.ConnectionDeleteRequested += async (_, card) => await DeleteAsync(card).ConfigureAwait(true);
        _sidebar.ConnectionMenuRequested += RowMenuRequested;
        _sidebar.FolderIconRequested += FolderIconRequested;

        _rowMenu = BuildRowMenu();
        _detail = new ConnectionDetailView { Dock = DockStyle.Bottom, Height = 300 };
        _detail.OpenRequested += (_, _) => WithMenuTarget(card => RaiseActivation(card, inNewPane: false));
        _detail.EditRequested += (_, tab) => WithSelection(id => EditRequested?.Invoke(this, new ConnectionEditRequest(id, tab)));
        _detail.DeleteRequested += async (_, _) =>
        {
            if (SelectedCard() is { } card)
            {
                await DeleteAsync(card).ConfigureAwait(true);
            }
        };
        _detail.TestRequested += async (_, _) => await TestSelectedAsync().ConfigureAwait(true);

        _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 0,
            AutoSize = false,
            Padding = new Padding(12, 4, 12, 4),
            ForeColor = StorageHubTheme.Warning,
            Visible = false,
            AccessibleName = Ui.Connections.PanelStatus
        };

        _searchBox = new StorageHubTextField
        {
            // A magnifier says what the field is for without a label above it, and the clear
            // button saves selecting the text to get the full list back.
            Glyph = UiGlyph.Search,
            ShowClearButton = true,
            PlaceholderText = Ui.Connections.SearchPlaceholder,
            AccessibleName = Ui.Connections.SearchAccessibleName
        };
        _searchBox.TextChanged += (_, _) => ApplyFilter();

        Controls.Add(_sidebar);
        Controls.Add(_status);
        Controls.Add(new Splitter
        {
            Dock = DockStyle.Bottom,
            Height = 4,
            MinExtra = 120,
            MinSize = 120,
            BackColor = StorageHubTheme.Border
        });
        Controls.Add(_detail);
        Controls.Add(BuildHeader());
    }

    internal event EventHandler<ConnectionActivationEventArgs>? ConnectionActivated;

    /// <summary>Raised when the editor should open — on a saved connection, or on nothing for a new one.</summary>
    internal event EventHandler<ConnectionEditRequest>? EditRequested;

    /// <summary>Raised after this panel changes what is saved, so the rest of the shell can catch up.</summary>
    internal event EventHandler? ConnectionsChanged;

    /// <summary>Raised when the user asks for the panel to be docked to the other side.</summary>
    internal event EventHandler? MoveSideRequested;

    /// <summary>Raised when the user asks for the panel to be hidden.</summary>
    internal event EventHandler? HideRequested;

    internal Guid? SelectedConnectionId => _sidebar.SelectedConnectionId;

    /// <summary>
    /// Re-lists every saved connection, disabled ones included: the panel shows a Disabled group
    /// that the overview's own cache — which lists enabled connections only — cannot supply.
    /// </summary>
    internal async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        // Several routes can ask at once — a dialog closing, a pane finishing, the panel reappearing.
        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            return;
        }

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            var response = await _storageClient.ListConnectionsAsync(
                new ConnectionListRequest(
                    StorageIpcContract.CurrentVersion,
                    IncludeDisabled: true,
                    Limit: StorageIpcLimits.MaximumConnectionResults),
                linked.Token).ConfigureAwait(true);
            if (IsDisposed)
            {
                return;
            }

            if (response.Failure is not null)
            {
                ShowStatus(response.Failure.Message);
                return;
            }

            ShowStatus(null);
            _summaries.Clear();
            _cards.Clear();
            foreach (var connection in response.Connections)
            {
                _summaries[connection.ConnectionId] = connection;
                _cards.Add(ConnectionCardFactory.Create(connection));
            }

            ApplyFilter();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            ShowStatus(Ui.Connections.AgentUnavailable);
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    private Panel BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 100,
            Padding = new Padding(12, 10, 12, 10),
            BackColor = StorageHubTheme.Surface
        };

        var title = UiControlFactory.CreateSectionTitle(Ui.Connections.PanelTitle);
        title.Dock = DockStyle.Left;
        // A docked label is as tall as the row whatever its AutoSize says, so the text needs
        // centring or it sits at the top while the buttons beside it are centred.
        title.TextAlign = ContentAlignment.MiddleLeft;

        var overflow = new StorageHubButton
        {
            // Docked buttons ignore Margin, so these live in a flow panel instead: docking them
            // right put the New button hard up against the overflow button with no gap at all.
            Margin = new Padding(6, 0, 0, 0),
            Text = string.Empty,
            // A drawn glyph rather than the U+22EE character, which picks up whatever fallback
            // font the shell supplies and sits off-centre next to the New button.
            Glyph = UiGlyph.More,
            GlyphTone = UiIconTone.Muted,
            IsIconOnly = true,
            // Small, so the action row is no taller than the title beside it: a medium button
            // stands a few pixels proud of the panel heading and the two stop reading as one row.
            ButtonSize = StorageHubButtonSize.Small,
            AccessibleName = Ui.Connections.PanelOptions,
            AccessibleDescription = Ui.Connections.PanelOptionsTooltip
        };
        overflow.Click += (_, _) => BuildPanelMenu().Show(overflow, new Point(0, overflow.Height));

        var add = new StorageHubButton
        {
            Margin = new Padding(0, 0, 0, 0),
            Text = Ui.Connections.NewConnection,
            Variant = StorageHubButtonVariant.Primary,
            ButtonSize = StorageHubButtonSize.Small,
            Glyph = UiGlyph.Add,
            AccessibleName = Ui.Connections.NewConnectionAccessibleName,
            AccessibleDescription = Ui.Connections.NewConnectionTooltip
        };
        add.Click += (_, _) => EditRequested?.Invoke(this, new ConnectionEditRequest(null, ConnectionEditorTab.General));

        // Right to left, so the first control added is the rightmost and the gaps fall between
        // the buttons rather than after them.
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = StorageHubTheme.Surface
        };
        actions.Controls.Add(overflow);
        actions.Controls.Add(add);

        // Tall enough for whichever of the two is taller -- the buttons are small so the row
        // stays the height of the heading, but a longer translation or a larger font must not
        // clip the title.
        var rowHeight = Math.Max(
            add.GetPreferredSize(Size.Empty).Height,
            title.GetPreferredSize(Size.Empty).Height) + 4;
        var row = new Panel { Dock = DockStyle.Top, Height = rowHeight, BackColor = StorageHubTheme.Surface };
        row.Controls.Add(title);
        row.Controls.Add(actions);

        // The search field sits in a host that carries the gap above it as padding. A spacer
        // panel docked alongside it does not: docking order decides which of the two ends up
        // against the edge, and it put the spacer under the field, leaving the field flush
        // against the buttons.
        var searchHeight = StorageHubFieldChrome.MeasureHeight(_searchBox);
        var searchHost = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = searchHeight + 10,
            Padding = new Padding(0, 10, 0, 0),
            BackColor = StorageHubTheme.Surface
        };
        _searchBox.Dock = DockStyle.Fill;
        searchHost.Controls.Add(_searchBox);

        header.Height = header.Padding.Vertical + rowHeight + searchHost.Height;
        header.Controls.Add(searchHost);
        header.Controls.Add(row);
        return header;
    }

    private ContextMenuStrip BuildPanelMenu()
    {
        var menu = new ContextMenuStrip { Renderer = DesktopAppearanceService.MenuRenderer };
        var move = new ToolStripMenuItem(Ui.Connections.MoveToOtherSide);
        move.Click += (_, _) => MoveSideRequested?.Invoke(this, EventArgs.Empty);
        var refresh = new ToolStripMenuItem(Ui.Connections.Refresh);
        refresh.Click += async (_, _) => await RefreshAsync(_lifetime.Token).ConfigureAwait(true);
        var hide = new ToolStripMenuItem(Ui.Connections.HidePanel);
        hide.Click += (_, _) => HideRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.AddRange([move, refresh, new ToolStripSeparator(), hide]);
        return menu;
    }

    private ContextMenuStrip BuildRowMenu()
    {
        var menu = new ContextMenuStrip { Renderer = DesktopAppearanceService.MenuRenderer };
        var open = new ToolStripMenuItem(Ui.Connections.ContextOpen);
        open.Click += (_, _) => WithMenuTarget(card => RaiseActivation(card, inNewPane: false));
        var openInNewPane = new ToolStripMenuItem(Ui.Connections.ContextOpenInNewPane);
        openInNewPane.Click += (_, _) => WithMenuTarget(card => RaiseActivation(card, inNewPane: true));
        var favorite = new ToolStripMenuItem(Ui.Connections.ContextToggleFavorite) { Name = "ToggleFavorite" };
        favorite.Click += async (_, _) => await ToggleFavoriteAsync().ConfigureAwait(true);
        var edit = new ToolStripMenuItem(Ui.Connections.ContextEdit);
        edit.Click += (_, _) => WithMenuTarget(card => RaiseEdit(card.ConnectionId));
        var delete = new ToolStripMenuItem(Ui.Connections.ContextDelete);
        delete.Click += async (_, _) =>
        {
            if (_menuTarget is { } card)
            {
                await DeleteAsync(card).ConfigureAwait(true);
            }
        };
        menu.Items.AddRange([open, openInNewPane, new ToolStripSeparator(), favorite, edit, new ToolStripSeparator(), delete]);
        return menu;
    }

    private void RowMenuRequested(object? sender, ConnectionRowMenuEventArgs args)
    {
        _menuTarget = args.Connection;
        _rowMenu.Show(args.ScreenLocation);
    }

    private void WithMenuTarget(Action<ConnectionCardModel> action)
    {
        var card = _menuTarget ?? SelectedCard();
        if (card is not null)
        {
            action(card);
        }
    }

    private void WithSelection(Action<Guid?> action) => action(SelectedConnectionId);

    private ConnectionCardModel? SelectedCard() => SelectedConnectionId is { } id
        ? _cards.FirstOrDefault(card => card.ConnectionId == id)
        : null;

    private void RaiseEdit(Guid? connectionId) =>
        EditRequested?.Invoke(this, new ConnectionEditRequest(connectionId, ConnectionEditorTab.General));

    private void RaiseActivation(ConnectionCardModel card, bool inNewPane)
    {
        if (card.ConnectionId is { } id && _summaries.TryGetValue(id, out var summary))
        {
            ConnectionActivated?.Invoke(this, new ConnectionActivationEventArgs(summary, inNewPane));
        }
    }

    /// <summary>
    /// Icons chosen for folders, keyed by the sidebar's group key. Owned here rather than by the
    /// sidebar because it is persisted in desktop preferences, which the shell supplies.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal IReadOnlyDictionary<string, string> FolderIcons { get; set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Raised when a folder's icon changed, so the shell can persist the new map.</summary>
    internal event EventHandler<IReadOnlyDictionary<string, string>>? FolderIconsChanged;

    private void FolderIconRequested(object? sender, FolderIconRequest request)
    {
        using var picker = new IconPickerForm(
            FolderIcons.GetValueOrDefault(request.GroupKey),
            StorageHubTheme.Primary,
            Ui.Format(Ui.Connections.IconPickerFolderTitleFormat, request.Label));
        if (picker.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        var updated = new Dictionary<string, string>(FolderIcons, StringComparer.Ordinal);
        if (picker.SelectedKey is { } key)
        {
            updated[request.GroupKey] = key;
        }
        else
        {
            // Cleared rather than stored as empty, so the map only ever holds real choices.
            _ = updated.Remove(request.GroupKey);
        }

        FolderIcons = updated;
        FolderIconsChanged?.Invoke(this, updated);
        ApplyFilter();
    }

    /// <summary>
    /// Whether a favourite is also listed in its own folder. Read from preferences on each rebuild
    /// rather than cached, so toggling it in Settings takes effect without restarting the shell.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool ShowFavoritesInTheirFolders { get; set; } = true;

    private void ApplyFilter()
    {
        _sidebar.ShowFavoritesInTheirFolders = ShowFavoritesInTheirFolders;
        _sidebar.FolderIcons = FolderIcons;
        _sidebar.SetConnections(_cards, _searchBox.Text, SelectedConnectionId);
        ShowDetail(SelectedConnectionId);
    }

    private void ShowDetail(Guid? connectionId)
    {
        // Any load still in flight is for the previous selection.
        _detailLoad?.Cancel();
        _detailLoad?.Dispose();
        _detailLoad = null;

        if (connectionId is not { } id || !_summaries.TryGetValue(id, out var summary))
        {
            _detail.Show(null);
            return;
        }

        // Draw what the listing already knows straight away, then fill in the endpoint and
        // authentication detail, which only the full profile carries.
        _detail.Show(summary);
        var load = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _detailLoad = load;
        _ = LoadDetailAsync(id, load.Token);
    }

    private async Task LoadDetailAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _controller.GetAsync(connectionId, cancellationToken).ConfigureAwait(true);
            if (!cancellationToken.IsCancellationRequested && !IsDisposed)
            {
                _detail.ShowProfile(connectionId, response.Profile);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // The summary is already on screen; the agent being unreachable costs the extra
            // fields, not the selection.
        }
    }

    private void ShowStatus(string? message)
    {
        _status.Text = message ?? string.Empty;
        _status.Visible = message is not null;
        _status.Height = message is null ? 0 : 40;
    }

    private async Task DeleteAsync(ConnectionCardModel card)
    {
        if (card.ConnectionId is not { } id || !_summaries.TryGetValue(id, out var summary))
        {
            return;
        }

        if (MessageBox.Show(
                FindForm(),
                ConnectionCardFactory.DeleteConfirmationPrompt(summary.DisplayName),
                ConnectionCardFactory.DeleteConfirmationCaption,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        await DeleteConfirmedAsync(card).ConfigureAwait(true);
    }

    /// <summary>
    /// The delete itself, past the confirmation. Separate so the agent call can be exercised
    /// without a message pump to answer a modal prompt.
    /// </summary>
    private async Task DeleteConfirmedAsync(ConnectionCardModel card)
    {
        if (card.ConnectionId is not { } id || !_summaries.TryGetValue(id, out var summary))
        {
            return;
        }

        try
        {
            // The version comes from the last listing rather than a fresh read: if the editor saved
            // in between, this fails as a conflict instead of deleting a revision nobody saw.
            var response = await _controller.DeleteAsync(id, summary.Version, _lifetime.Token).ConfigureAwait(true);
            if (response.Status != ConnectionProfileWriteStatus.Succeeded)
            {
                ShowStatus(response.Failure?.Message ?? Ui.Connections.DeleteFailed);
                await RefreshAsync(_lifetime.Token).ConfigureAwait(true);
                return;
            }

            _sidebar.ClearSelection();
            _detail.Show(null);
            await RefreshAsync(_lifetime.Token).ConfigureAwait(true);
            ConnectionsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            ShowStatus(Ui.Connections.DeleteFailedThroughAgent);
        }
    }

    private async Task ToggleFavoriteAsync()
    {
        var card = _menuTarget ?? SelectedCard();
        if (card?.ConnectionId is not { } id)
        {
            return;
        }

        try
        {
            // IsFavorite lives inside the profile's metadata and saving takes a whole draft, so
            // unlike delete this genuinely needs the document first.
            var current = await _controller.GetAsync(id, _lifetime.Token).ConfigureAwait(true);
            if (current.Profile is not { } profile)
            {
                ShowStatus(current.Failure?.Message ?? Ui.Connections.LoadFailed);
                return;
            }

            var draft = profile.Draft with
            {
                Metadata = profile.Draft.Metadata with { IsFavorite = !profile.Draft.Metadata.IsFavorite }
            };
            var response = await _controller.SaveAsync(draft, profile, _lifetime.Token).ConfigureAwait(true);
            if (response.Status != ConnectionProfileWriteStatus.Succeeded)
            {
                ShowStatus(response.Failure?.Message ?? Ui.Connections.UpdateFailed);
                return;
            }

            await RefreshAsync(_lifetime.Token).ConfigureAwait(true);
            ConnectionsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            ShowStatus(Ui.Connections.UpdateFailedThroughAgent);
        }
    }

    private async Task TestSelectedAsync()
    {
        if (SelectedConnectionId is not { } id)
        {
            return;
        }

        try
        {
            _detail.ShowTesting();
            _ = await _storageClient.TestConnectionAsync(
                new ConnectionTestRequest(StorageIpcContract.CurrentVersion, id),
                _lifetime.Token).ConfigureAwait(true);

            // The agent records the outcome on the profile, so re-listing is what refreshes the
            // health shown here rather than the response itself.
            await RefreshAsync(_lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            ShowStatus(Ui.Connections.TestFailedThroughAgent);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lifetime.Cancel();
            _detailLoad?.Cancel();
            _detailLoad?.Dispose();
        }

        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        _rowMenu.Dispose();
        _lifetime.Dispose();
        if (!_ownsClients)
        {
            return;
        }

        _ = _storageClient.DisposeAsync().AsTask();
        _ = _profileClient.DisposeAsync().AsTask();
        _ = _secretClient.DisposeAsync().AsTask();
    }
}
