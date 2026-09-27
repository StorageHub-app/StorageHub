using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Threading;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;

namespace StorageHub.Desktop.Views;

/// <summary>
/// The connections sidebar, backed by whatever the agent actually has.
/// </summary>
/// <remarks>
/// The first part of the shell to hold real data rather than stand-in rows.
///
/// The cards are <see cref="ConnectionCardModel"/>s built by ConnectionCardFactory, which the
/// WinForms shell already used for the same list. Projecting the agent's ConnectionSummary a second
/// time here would let the two surfaces disagree about a connection's name, provider or health
/// wording - the exact reason that factory was extracted in the first place.
/// </remarks>
/// <summary>One key and value in the details panel, e.g. "Provider  S3 / Object Storage".</summary>
internal sealed record ConnectionDetailRow(string Key, string Value);

internal sealed class ConnectionsSidebar : INotifyPropertyChanged
{
    private readonly Func<IRemoteStorageAgentClient>? _client;
    private readonly IDialogService? _dialogs;
    private readonly Func<IReadOnlyList<ConnectionGroupEntry>?>? _load;
    private readonly Action<IReadOnlyList<ConnectionGroupEntry>>? _save;
    private readonly Action<IReadOnlyDictionary<string, string>>? _saveIcons;
    private readonly Func<string?, string, Task<IconChoice>>? _pickIcon;
    private readonly Func<IRemoteConnectionProfileClient>? _profiles;
    private Dictionary<string, string> _icons = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<ConnectionCardModel> _cards = [];

    /// <summary>
    /// The saved connections' names by id, from the last listing the agent answered.
    /// </summary>
    /// <remarks>
    /// Kept apart from the cards, which a failed listing empties. A name does not stop being true
    /// because the agent could not be reached, and the queue went back to short ids until it was.
    /// </remarks>
    private IReadOnlyDictionary<Guid, string> _names = new Dictionary<Guid, string>();
    private string _search = string.Empty;
    private bool _showFavoritesInTheirFolders = true;

    /// <summary>What the Favorites group offers in place of rename and remove, which is nothing.</summary>
    private static readonly RelayCommand Inert = new(static _ => { }, static _ => false);

    /// <summary>
    /// What the last listing had to say, kept so a search cannot overwrite it.
    /// </summary>
    /// <remarks>
    /// "Nothing is saved" and "nothing answered" are different states that must read differently,
    /// and rebuilding for a keystroke has no business deciding which one is true.
    /// </remarks>
    private string _listingStatus = string.Empty;
    private IReadOnlyList<ConnectionGroupEntry> _arrangement = [];
    private string _status = string.Empty;
    private bool _isEmpty = true;
    private ConnectionRowModel? _selected;
    private string _detailStatus = string.Empty;
    private bool _testing;

    /// <summary>
    /// The listed version the details' last test result was about, or null when what they say is
    /// not a test result but why something the panel was asked to do was refused.
    /// </summary>
    /// <remarks>
    /// A test describes the connection as it was when tested, so once it has been saved at another
    /// version the result goes. A refusal answers what somebody just did, and stays until the
    /// selection moves, even through the listing that follows it.
    /// </remarks>
    private long? _testedVersion;

    /// <param name="load">Where the saved arrangement comes from. Null means nothing is remembered.</param>
    /// <param name="save">
    /// Where a rearrangement goes. Called on every drag, which is cheap: the file is small and the
    /// alternative is losing an arrangement to a shell that did not close cleanly.
    /// </param>
    /// <param name="profiles">
    /// Reads and writes a whole profile, which is what marking a favourite takes. A factory for the
    /// same reason <paramref name="client"/> is one: each toggle opens a connection and closes it.
    /// Null leaves Toggle favorite dim.
    /// </param>
    internal ConnectionsSidebar(
        ICommand newCommand,
        Func<IRemoteStorageAgentClient>? client = null,
        IDialogService? dialogs = null,
        Func<IReadOnlyList<ConnectionGroupEntry>?>? load = null,
        Action<IReadOnlyList<ConnectionGroupEntry>>? save = null,
        Func<IReadOnlyDictionary<string, string>?>? loadIcons = null,
        Action<IReadOnlyDictionary<string, string>>? saveIcons = null,
        Func<string?, string, Task<IconChoice>>? pickIcon = null,
        Func<IRemoteConnectionProfileClient>? profiles = null)
    {
        NewCommand = newCommand;
        _client = client;
        _dialogs = dialogs;
        _load = load;
        _save = save;
        _saveIcons = saveIcons;
        _pickIcon = pickIcon;
        _profiles = profiles;
        if (loadIcons?.Invoke() is { } icons)
        {
            _icons = new Dictionary<string, string>(icons, StringComparer.OrdinalIgnoreCase);
        }
        Status = Ui.Connections.SidebarEmpty;
        NewGroupCommand = new RelayCommand(_ => _ = AddGroupAsync(), _ => _dialogs is not null);
        ClearSearchCommand = new RelayCommand(_ => Search = string.Empty);
        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync());

        OpenSelectedCommand = new RelayCommand(
            _ => { if (_selected is { } row) OpenConnection?.Invoke(row.Id); },
            _ => _selected is not null && OpenConnection is not null);
        TestSelectedCommand = new RelayCommand(
            _ => _ = TestSelectedAsync(), _ => _selected is not null && _client is not null && !_testing);
        EditSelectedCommand = new RelayCommand(
            row => { if ((row as ConnectionRowModel ?? _selected) is { } chosen) EditConnection?.Invoke(chosen.Id, ConnectionEditorTab.General); },
            _ => EditConnection is not null);
        AttentionCommand = new RelayCommand(
            _ => { if (_selected is { } row && Attention is { } attention) EditConnection?.Invoke(row.Id, attention.Tab); },
            _ => EditConnection is not null);
        DeleteSelectedCommand = new RelayCommand(
            row => { if ((row as ConnectionRowModel ?? _selected) is { } chosen && DeleteConnection is { } delete) _ = DeleteAndRefreshAsync(delete, chosen.Id); },
            _ => DeleteConnection is not null);

        // The panel deletes on its own, as 1.x's did; it needs somewhere to ask and a profile store.
        if (profiles is not null && dialogs is not null)
        {
            DeleteConnection = id => DeleteListedAsync(id);
        }
    }

    /// <summary>
    /// The card the details panel is showing.
    /// </summary>
    /// <remarks>
    /// Kept by id across a rebuild, so a search or a drag does not throw the details away while
    /// the connection they describe is still on screen.
    /// </remarks>
    public ConnectionRowModel? Selected
    {
        get => _selected;
        private set
        {
            if (ReferenceEquals(_selected, value)) return;
            if (_selected is not null) _selected.IsSelected = false;
            _selected = value;

            // Both copies of a favourite light up together, as 1.x drew them, so the card in its
            // group does not look unselected while its twin under Favorites is lit.
            foreach (var row in Rows()) row.IsSelected = value is not null && row.Id == value.Id;
            DetailStatus = string.Empty;
            _testedVersion = null;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelection)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Details)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasAttention)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AttentionLabel)));
            RaiseDetailCommands();
        }
    }

    public bool HasSelection => _selected is not null;

    /// <summary>Selects a card, as a single click does.</summary>
    internal void Select(ConnectionRowModel? row) => Selected = row;

    /// <summary>
    /// A saved connection's name, from the list the agent last gave, or null when it is not on it.
    /// </summary>
    /// <remarks>
    /// The transfer queue names a transfer's two sides with it. Asking here, rather than the
    /// queue listing the connections again on every poll, keeps the two agreeing about a name.
    /// </remarks>
    internal string? NameOf(Guid connectionId) => _names.GetValueOrDefault(connectionId);

    /// <summary>
    /// Called on the UI thread once a listing the agent answered is in, so what shows a
    /// connection's name elsewhere can pick up a new or changed one then rather than later.
    /// Assigned by the shell.
    /// </summary>
    internal Action? Answered { get; set; }

    /// <summary>
    /// The details panel's rows for the selected connection, as 1.x listed them under it.
    /// </summary>
    /// <remarks>
    /// Favorite is always there, yes or no, as it was in 1.x. Toggling it is not a button here but
    /// the card's own right-click menu, where 1.x put it beside every other action on a connection.
    /// </remarks>
    public IReadOnlyList<ConnectionDetailRow> Details => _selected is not { Card: var card }
        ? []
        : [.. new ConnectionDetailRow[]
            {
                new(Ui.Connections.Provider, card.Descriptor.DisplayName),
                new(Ui.Connections.FieldFolder, card.FolderPath ?? string.Empty),
                new(Ui.Connections.FieldTags, string.Join(", ", card.DisplayTags)),
                new(Ui.Connections.FieldFavorite, card.IsFavorite ? Ui.Connections.DetailYes : Ui.Connections.DetailNo),
                new(Ui.Connections.FieldState, card.State)
            }.Where(static row => !string.IsNullOrWhiteSpace(row.Value))];

    /// <summary>What the last test said, under the details.</summary>
    public string DetailStatus
    {
        get => _detailStatus;
        private set
        {
            Set(ref _detailStatus, value, nameof(DetailStatus));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDetailStatus)));
        }
    }

    public bool HasDetailStatus => _detailStatus.Length > 0;

    /// <summary>
    /// What the selected connection needs somebody to fix, and the editor's tab that fixes it, or
    /// nothing, as 1.x's details panel worked it out from the connection's last health check.
    /// </summary>
    /// <remarks>
    /// Only the two states somebody can resolve in the editor get a route there: credentials that
    /// were refused, and a server identity waiting for a decision. A disabled connection gets none,
    /// as it could not be opened to check anyway.
    /// </remarks>
    private (string Label, ConnectionEditorTab Tab)? Attention =>
        _selected is { } row &&
        Connections.FirstOrDefault(listed => listed.ConnectionId == row.Id) is { IsEnabled: true, Health: { } health }
            ? health.RequiresCredentialAction
                ? (Ui.Connections.DetailFixCredentials, ConnectionEditorTab.Authentication)
                : health.RequiresTrustAction
                    ? (Ui.Connections.DetailReviewTrust, ConnectionEditorTab.Trust)
                    : null
            : null;

    /// <summary>Whether the details panel offers "Fix credentials…" or "Review trust…".</summary>
    public bool HasAttention => Attention is not null;

    /// <summary>"Fix credentials…" or "Review trust…", whichever the connection needs.</summary>
    public string AttentionLabel => Attention?.Label ?? string.Empty;

    /// <summary>Opens the Edit Connection dialog on the tab that fixes what the connection needs.</summary>
    public ICommand AttentionCommand { get; }

    public ICommand OpenSelectedCommand { get; }

    public ICommand TestSelectedCommand { get; }

    /// <summary>Edit, on the selected card and in the details panel. Its parameter is the row, if any.</summary>
    public ICommand EditSelectedCommand { get; }

    /// <summary>Delete, likewise. The panel asks first, and lists again after.</summary>
    public ICommand DeleteSelectedCommand { get; }

    /// <summary>
    /// Opens the Edit Connection dialog on a connection, on one of its tabs. Assigned by the shell,
    /// which owns the windows.
    /// </summary>
    public Action<Guid, ConnectionEditorTab>? EditConnection
    {
        get;
        internal set
        {
            field = value;
            RaiseDetailCommands();
        }
    }

    /// <summary>
    /// Deletes a connection, confirming first, and answers whether it went:
    /// <see cref="DeleteListedAsync"/> whenever the panel has a profile store and somewhere to ask.
    /// Settable, so a test can see which connection a command names without an agent behind it.
    /// </summary>
    public Func<Guid, Task<bool>>? DeleteConnection
    {
        get;
        internal set
        {
            field = value;
            RaiseDetailCommands();
        }
    }

    /// <summary>
    /// Deletes, and once the connection has gone lists again and tells the shell, as 1.x's panel
    /// did. A "no" or a refusal changes nothing saved, so Welcome is left alone.
    /// </summary>
    private async Task DeleteAndRefreshAsync(Func<Guid, Task<bool>> delete, Guid id)
    {
        if (!await delete(id).ConfigureAwait(true)) return;
        await RefreshAsync().ConfigureAwait(true);
        ConnectionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Asks, then deletes a connection at the version the panel listed it at, as 1.x's panel did.
    /// </summary>
    /// <returns>Whether the connection was deleted.</returns>
    /// <remarks>
    /// The version is the listing's rather than a fresh read, so a connection edited since it was
    /// listed fails as a conflict instead of being deleted at a revision nobody saw. Why a delete
    /// failed is said under the details, where a refused favourite is, and the panel lists again,
    /// as 1.x's did, so a second try is made at whatever the connection is now. What was said stays
    /// through that listing, because the connection is still there to describe.
    /// </remarks>
    internal async Task<bool> DeleteListedAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        if (_profiles is null || _dialogs is null) return false;
        if (Connections.FirstOrDefault(listed => listed.ConnectionId == connectionId) is not { } connection) return false;

        var choice = await _dialogs.ConfirmAsync(
            new DialogRequest
            {
                Title = Ui.Dialogs.DeleteConnectionCaption,
                Message = Ui.Format(Ui.Dialogs.DeleteConnectionPromptFormat, connection.DisplayName),
                Severity = DialogSeverity.Warning,
                Buttons = DialogButtons.YesNo
            },
            cancellationToken).ConfigureAwait(true);
        if (choice != DialogChoice.Yes) return false;

        ConnectionProfileWriteResponse response;
        try
        {
            await using var profiles = _profiles();
            response = await profiles
                .DeleteAsync(
                    new ConnectionProfileDeleteRequest(
                        ConnectionProfileIpcContract.CurrentVersion, connectionId, connection.Version),
                    cancellationToken)
                .ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Refused(Ui.Connections.DeleteFailedThroughAgent);
            return false;
        }

        if (response.Status == ConnectionProfileWriteStatus.Succeeded) return true;

        Refused(response.Failure?.Message ?? Ui.Connections.DeleteFailed);
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
        return false;
    }

    /// <summary>Says under the details why something the panel was asked to do did not happen.</summary>
    private void Refused(string reason)
    {
        DetailStatus = reason;
        _testedVersion = null;
    }

    /// <summary>
    /// Raised after the panel has changed what is saved, a favourite toggled or a connection
    /// deleted, so the rest of the shell can catch up, as 1.x's panel raised it.
    /// </summary>
    /// <remarks>
    /// Welcome's recent connections list favourites first and say so, and read the connections
    /// for themselves, so without this they would go on showing the old order until refreshed.
    /// Not raised for a listing: that changes nothing, and would reload Welcome on every one.
    /// </remarks>
    internal event EventHandler? ConnectionsChanged;

    /// <summary>Asks the agent whether the selected connection answers, and says so under it.</summary>
    internal async Task TestSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (_selected is not { } row || _client is null) return;

        _testing = true;
        DetailStatus = Ui.Connections.DetailTesting;
        _testedVersion = Connections.FirstOrDefault(listed => listed.ConnectionId == row.Id)?.Version;
        RaiseDetailCommands();
        try
        {
            await using var client = _client();
            var response = await client
                .TestConnectionAsync(
                    new ConnectionTestRequest(StorageIpcContract.CurrentVersion, row.Id),
                    cancellationToken)
                .ConfigureAwait(true);

            DetailStatus = response.Failure is { } failure
                ? failure.Message
                : response.Succeeded
                    ? Ui.Connections.ConnectionReachable
                    : Ui.Connections.ConnectionUnreachable;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DetailStatus = Ui.Shell.AgentNotConnected;
        }
        finally
        {
            _testing = false;
            RaiseDetailCommands();
        }
    }

    /// <summary>
    /// Makes a connection a favourite, or stops it being one, as 1.x's Toggle favorite did.
    /// </summary>
    /// <remarks>
    /// The flag is part of the profile, so the agent keeps it with the rest of the connection and
    /// it survives a restart. A write takes a whole draft, so the profile is read first and written
    /// back at the version read with only the flag changed: one edited in the meantime is refused
    /// as a conflict rather than overwritten. Why it failed is said under the details. The
    /// connection to the agent is closed before the panel lists again, so a toggle holds none open.
    /// </remarks>
    internal async Task ToggleFavoriteAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        if (_profiles is null) return;

        try
        {
            await using (var profiles = _profiles())
            {
                var current = await profiles
                    .GetAsync(
                        new ConnectionProfileGetRequest(ConnectionProfileIpcContract.CurrentVersion, connectionId),
                        cancellationToken)
                    .ConfigureAwait(true);
                if (current.Profile is not { } profile)
                {
                    Refused(current.Failure?.Message ?? Ui.Connections.LoadFailed);
                    return;
                }

                var draft = profile.Draft with
                {
                    Metadata = profile.Draft.Metadata with { IsFavorite = !profile.Draft.Metadata.IsFavorite }
                };
                var response = await profiles
                    .UpdateAsync(
                        new ConnectionProfileUpdateRequest(
                            ConnectionProfileIpcContract.CurrentVersion, profile.ConnectionId, profile.Version, draft),
                        cancellationToken)
                    .ConfigureAwait(true);
                if (response.Status != ConnectionProfileWriteStatus.Succeeded)
                {
                    Refused(response.Failure?.Message ?? Ui.Connections.UpdateFailed);
                    return;
                }
            }

            await RefreshAsync(cancellationToken).ConfigureAwait(true);
            ConnectionsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Refused(Ui.Connections.UpdateFailedThroughAgent);
        }
    }

    private void RaiseDetailCommands()
    {
        (OpenSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (TestSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (EditSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DeleteSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (AttentionCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// The panel's groups, in the order they are shown.
    /// </summary>
    /// <remarks>
    /// This replaced a flat list, which replaced nothing -- the WinForms sidebar split the panel
    /// into Storage and Clients, which is the provider's classification standing in for an
    /// organising principle. Somebody with four buckets and two shells for one project wants those
    /// six things together, and no amount of sorting inside two fixed lists gives them that.
    /// </remarks>
    public ObservableCollection<ConnectionGroupModel> Groups { get; } = [];

    /// <summary>
    /// The Favorites group above the others, or null when no favourite is showing.
    /// </summary>
    /// <remarks>
    /// 1.x's Favorites section: the enabled favourites, by name, flat, whatever group each is in.
    /// A state rather than a place, so it sits apart from the groups somebody made and is not one
    /// of them: nothing is filed into it, and it cannot be renamed, removed or reordered.
    /// </remarks>
    public ConnectionGroupModel? Favorites
    {
        get;
        private set
        {
            if (ReferenceEquals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Favorites)));
        }
    }

    /// <summary>
    /// Whether a favourite is listed in its own group as well as under Favorites, as Settings says.
    /// </summary>
    /// <remarks>
    /// On by default, as in 1.x: a favourite that vanishes from its group is confusing when the
    /// group is how somebody thinks of it. With many connections the repetition can be more noise
    /// than help, which is what turning it off is for.
    /// </remarks>
    internal bool ShowFavoritesInTheirFolders
    {
        get => _showFavoritesInTheirFolders;
        set
        {
            if (_showFavoritesInTheirFolders == value) return;
            _showFavoritesInTheirFolders = value;
            Rebuild();
        }
    }

    /// <summary>
    /// The connections the agent listed last, whole, for what else offers them: the Go menu's
    /// favourites. Empty until the first listing, and after one that failed.
    /// </summary>
    internal IReadOnlyList<ConnectionSummary> Connections { get; private set; } = [];

    /// <summary>Raised on the UI thread once a listing has been drawn.</summary>
    internal event EventHandler? Listed;

    public ICommand NewCommand { get; }

    public ICommand NewGroupCommand { get; }

    public ICommand ClearSearchCommand { get; }

    /// <summary>Reads the connections again, from the panel's own menu as 1.x offered it.</summary>
    public ICommand RefreshCommand { get; }

    /// <summary>
    /// Docks the panel to the other side of the window, and hides it: the panel's own menu offers
    /// both, as 1.x's did. Assigned by the shell, which owns the layout; while they are null the
    /// menu leaves them out.
    /// </summary>
    public ICommand? MoveToOtherSideCommand { get; internal set; }

    /// <inheritdoc cref="MoveToOtherSideCommand"/>
    public ICommand? HidePanelCommand { get; internal set; }

    /// <summary>
    /// What is typed in the search box, narrowing the panel as it is typed.
    /// </summary>
    /// <remarks>
    /// The matching is <see cref="ConnectionPickerFilter"/>, which is in Desktop.Core, tested, and
    /// was referenced from nowhere -- the box had no Text binding at all and was decoration. Every
    /// whitespace-separated term has to match, so typing more narrows rather than widens.
    /// </remarks>
    public string Search
    {
        get => _search;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_search, value, StringComparison.Ordinal)) return;
            _search = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Search)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSearch)));
            Rebuild();
        }
    }

    public bool HasSearch => _search.Length > 0;

    /// <summary>
    /// Opens a connection in the pane somebody is looking at.
    /// </summary>
    /// <remarks>
    /// Assigned by the shell, because the panel does not know there are panes. Null leaves a row
    /// inert, which is what it was before: the rows had no click behaviour at all.
    /// </remarks>
    /// <summary>Opens a connection in a new pane beside the active one. Assigned by the shell.</summary>
    public Action<Guid>? OpenConnectionInNewPane { get; internal set; }

    /// <summary>
    /// A card's right-click menu, as 1.x had it: Open and Open in new pane, then Toggle favorite
    /// and Edit, then Delete, with a line between each pair. Built when it opens, on the card it
    /// opened on, which the right-click has just selected. A label of "-" is one of the lines.
    /// </summary>
    internal IReadOnlyList<(string Label, Action Run, bool Enabled)> ContextEntriesFor(ConnectionRowModel row) =>
    [
        (Ui.Connections.ContextOpen, () => OpenConnection?.Invoke(row.Id), OpenConnection is not null),
        (Ui.Connections.ContextOpenInNewPane, () => OpenConnectionInNewPane?.Invoke(row.Id), OpenConnectionInNewPane is not null),
        (CommandEntry.SeparatorLabel, static () => { }, false),
        (Ui.Connections.ContextToggleFavorite, () => _ = ToggleFavoriteAsync(row.Id), _profiles is not null),
        (Ui.Connections.ContextEdit, () => EditConnection?.Invoke(row.Id, ConnectionEditorTab.General), EditConnection is not null),
        (CommandEntry.SeparatorLabel, static () => { }, false),
        (Ui.Connections.ContextDelete, () => { if (DeleteConnection is { } delete) _ = DeleteAndRefreshAsync(delete, row.Id); }, DeleteConnection is not null),
    ];

    public Action<Guid>? OpenConnection
    {
        get;
        internal set
        {
            field = value;
            RaiseDetailCommands();
        }
    }

    // A binding target has to be an instance property, and these are resolved per call rather than
    // captured so that they follow a language change. CA1822 sees only that they touch no field.
#pragma warning disable CA1822
    public string Title => Ui.Connections.PanelTitle;

    public string NewLabel => Ui.Connections.NewConnection;

    public string MoreLabel => Ui.Connections.PanelOptions;

    public string SearchPlaceholder => Ui.Connections.SearchPlaceholder;

    public string DetailPlaceholder => Ui.Connections.DetailEmpty;

    public string NewGroupLabel => Ui.Connections.NewGroup;

    public string MoveToOtherSideLabel => Ui.Connections.MoveToOtherSide;

    public string RefreshLabel => Ui.Connections.Refresh;

    public string HidePanelLabel => Ui.Connections.HidePanel;

    public string ClearSearchLabel => Ui.Connections.ClearSearch;

    public string DragHint => Ui.Connections.DragConnectionHint;

    public string DetailTitle => Ui.Connections.DetailViewTitle;

    public string OpenLabel => Ui.Connections.DetailOpen;

    public string TestLabel => Ui.Connections.DetailTest;

    public string EditLabel => Ui.Connections.DetailEdit;

    public string DeleteLabel => Ui.Connections.DetailDelete;
#pragma warning restore CA1822

    /// <summary>
    /// What the list area says when it has nothing to show.
    /// </summary>
    /// <remarks>
    /// An empty list because nothing is saved and an empty list because nothing answered are
    /// different states. Showing "No saved connections yet" for the second is how somebody spends an
    /// afternoon wondering where their connections went.
    /// </remarks>
    public string Status
    {
        get => _status;
        private set => Set(ref _status, value, nameof(Status));
    }

    public bool IsEmpty
    {
        get => _isEmpty;
        private set => Set(ref _isEmpty, value, nameof(IsEmpty));
    }

    /// <summary>
    /// Replaces the list with what the agent reports.
    /// </summary>
    /// <remarks>
    /// Failures are reported rather than thrown. The sidebar exists before the agent is necessarily
    /// up, and a shell that will not open because nothing answered a list call is worse than one
    /// that opens and says the agent is unreachable.
    /// </remarks>
    internal async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_client is null)
        {
            Apply([], Ui.Connections.SidebarEmpty);
            return;
        }

        try
        {
            await using var client = _client();
            var response = await client
                .ListConnectionsAsync(new ConnectionListRequest(IncludeDisabled: true), cancellationToken)
                .ConfigureAwait(false);

            if (response.Failure is { } failure)
            {
                Apply([], failure.Message);
                return;
            }

            Apply(response.Connections, Ui.Connections.SidebarEmpty, listed: true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Apply([], Ui.Shell.AgentNotConnected);
        }
    }

    /// <summary>
    /// Files a connection into a group, at a position, and remembers it.
    /// </summary>
    /// <remarks>
    /// The arrangement is recomputed and the groups rebuilt rather than the two collections being
    /// edited, because a drag can cross groups and a rebuild cannot leave a copy behind.
    /// </remarks>
    internal void Move(Guid connectionId, string groupName, int index)
    {
        _arrangement = ConnectionGrouping.Move(_arrangement, connectionId, groupName, index);
        Persist();
        Rebuild();
    }

    /// <summary>Moves a whole group up or down the panel.</summary>
    internal void ReorderGroup(string name, int index)
    {
        _arrangement = ConnectionGrouping.Reorder(_arrangement, name, index);
        Persist();
        Rebuild();
    }

    /// <summary>Asks for a name and adds an empty group to file things into.</summary>
    internal async Task AddGroupAsync(CancellationToken cancellationToken = default)
    {
        if (_dialogs is null) return;
        var name = await _dialogs.PromptAsync(
            new DialogPromptRequest
            {
                Title = Ui.Connections.NewGroup,
                Label = Ui.Connections.GroupName,
                Accept = Ui.Shell.Create,
                Validate = candidate => Taken(candidate)
            },
            cancellationToken).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name)) return;

        _arrangement = ConnectionGrouping.Add(_arrangement, name);
        Persist();
        Rebuild();
    }

    internal async Task RenameGroupAsync(string from, CancellationToken cancellationToken = default)
    {
        if (_dialogs is null) return;
        var name = await _dialogs.PromptAsync(
            new DialogPromptRequest
            {
                Title = Ui.Connections.RenameGroup,
                Label = Ui.Connections.GroupName,
                Value = from,
                Accept = Ui.Shell.RenameWorkspaceAccept,
                Validate = candidate => Taken(candidate, from)
            },
            cancellationToken).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name)) return;

        _arrangement = ConnectionGrouping.Rename(_arrangement, from, name);

        // The icon goes with the group; left behind, it would come back on the next group given
        // the old name.
        if (_icons.Remove(from, out var icon)) _icons[name.Trim()] = icon;
        PersistIcons();
        Persist();
        Rebuild();
    }

    /// <summary>Removes a group, keeping everything that was filed in it.</summary>
    internal void RemoveGroup(string name)
    {
        _arrangement = ConnectionGrouping.Remove(_arrangement, name);
        if (_icons.Remove(name)) PersistIcons();
        Persist();
        Rebuild();
    }

    /// <summary>
    /// Chooses a group's icon, or clears it back to a folder.
    /// </summary>
    internal async Task ChangeGroupIconAsync(string name)
    {
        if (_pickIcon is null) return;
        var choice = await _pickIcon(
            _icons.GetValueOrDefault(name),
            Ui.Format(Ui.Connections.IconPickerFolderTitleFormat, name)).ConfigureAwait(true);
        if (!choice.Chosen) return;

        // Cleared rather than stored as empty, so the map only ever holds real choices.
        if (choice.Key is { } key) _icons[name] = key;
        else _icons.Remove(name);
        PersistIcons();
        Rebuild();
    }

    private void PersistIcons() => _saveIcons?.Invoke(new Dictionary<string, string>(_icons, StringComparer.OrdinalIgnoreCase));

    /// <summary>Why a group cannot be called this, or nothing.</summary>
    private string? Taken(string candidate, string? except = null)
    {
        var trimmed = candidate?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) return Ui.Validation.AGroupNameIsRequired;

        var clashes = _arrangement.Any(group =>
            !string.Equals(group.Name, except, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(group.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        return clashes ? Ui.Shell.NameAlreadyExists : null;
    }

    /// <param name="listed">
    /// The agent answered with this list, rather than a failure leaving it empty; only then are
    /// the names replaced.
    /// </param>
    private void Apply(IReadOnlyList<ConnectionSummary> connections, string status, bool listed = false)
    {
        void Update()
        {
            IReadOnlyList<ConnectionCardModel> cards = [.. connections.Select(ConnectionCardFactory.Create)];
            Connections = connections;
            _cards = cards;
            _arrangement = ConnectionGrouping.Arrange(_arrangement.Count > 0 ? _arrangement : _load?.Invoke(), cards);
            _listingStatus = status;
            if (listed)
            {
                var names = new Dictionary<Guid, string>();
                foreach (var card in cards)
                {
                    if (card.ConnectionId is { } id) names[id] = card.Name;
                }

                _names = names;
            }

            Rebuild();
            if (listed) Answered?.Invoke();
            Listed?.Invoke(this, EventArgs.Empty);
        }

        if (Dispatcher.UIThread.CheckAccess()) Update();
        else Dispatcher.UIThread.Post(Update);
    }

    /// <summary>Turns the arrangement into what the panel draws.</summary>
    private void Rebuild()
    {
        var chosen = _selected?.Id;
        var byId = _cards
            .Where(static card => card.ConnectionId is not null)
            .ToDictionary(static card => card.ConnectionId!.Value);

        // Favourites first, as 1.x listed them: the enabled ones, by name. A disabled favourite
        // stays only in its group, where a connection that cannot be opened is shown dimmed.
        var favorites = _cards
            .Where(static card => card is { IsEnabled: true, IsFavorite: true, ConnectionId: not null })
            .Where(card => ConnectionPickerFilter.Matches(card, _search))
            .OrderBy(static card => card.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(static card => new ConnectionRowModel(card))
            .ToArray();
        Favorites = favorites.Length == 0
            ? null
            : new ConnectionGroupModel(Ui.Connections.GroupFavorites, favorites, Inert, Inert, isFavorites: true);

        Groups.Clear();
        var matched = favorites.Length;
        foreach (var group in _arrangement)
        {
            var name = group.Name;
            var rows = group.Members
                .Where(byId.ContainsKey)
                .Select(id => byId[id])
                .Where(card => _showFavoritesInTheirFolders || card is not { IsEnabled: true, IsFavorite: true })
                .Where(card => ConnectionPickerFilter.Matches(card, _search))
                .Select(card => new ConnectionRowModel(card))
                .ToArray();
            matched += rows.Length;
            Groups.Add(new ConnectionGroupModel(
                name,
                rows,
                new RelayCommand(_ => _ = RenameGroupAsync(name), _ => _dialogs is not null),
                new RelayCommand(_ => RemoveGroup(name), _ => _arrangement.Count > 1),
                new RelayCommand(_ => _ = ChangeGroupIconAsync(name), _ => _pickIcon is not null),
                _icons.GetValueOrDefault(name)));
        }

        // The same connection stays selected on its new row, and one that has gone, deleted or
        // hidden by a search, takes the details with it rather than leaving them describing
        // something off screen. The rows are new, so the setter always runs and says so. What the
        // details last said stays with a connection still there: a refusal until the selection
        // moves, a test result until the connection is saved at another version.
        var said = _detailStatus;
        var testedAt = _testedVersion;
        Selected = Rows().FirstOrDefault(row => row.Id == chosen);
        if (_selected is { } kept &&
            (testedAt is null || testedAt == Connections.FirstOrDefault(listed => listed.ConnectionId == kept.Id)?.Version))
        {
            DetailStatus = said;
            _testedVersion = testedAt;
        }

        // A search that matches nothing reads as an empty panel otherwise, which is the same
        // picture as having no connections at all and a very different situation. Anything the
        // listing itself had to say outranks it: an agent that did not answer is the more useful
        // thing to be told, and is still true whatever is typed in the box.
        var searchFoundNothing = HasSearch && matched == 0 && _cards.Count > 0;
        IsEmpty = _cards.Count == 0 || searchFoundNothing;
        Status = searchFoundNothing ? Ui.Connections.NoMatches : _listingStatus;
    }

    /// <summary>Every card on the panel, Favorites' first, a favourite's two copies both included.</summary>
    private IEnumerable<ConnectionRowModel> Rows() =>
        (Favorites?.Connections ?? Enumerable.Empty<ConnectionRowModel>())
            .Concat(Groups.SelectMany(static group => group.Connections));

    private void Persist() => _save?.Invoke(_arrangement);

    private void Set<T>(ref T field, T value, string name)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
