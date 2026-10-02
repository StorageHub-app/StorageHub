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
internal sealed record ConnectionDetailRow(string Key, string Value)
{
    /// <summary>A section's heading, "Server" or "Security", drawn across both columns.</summary>
    public bool IsSection { get; init; }

    public bool IsFact => !IsSection && Secret is null;

    /// <summary>
    /// For a secret, where it came from and its name, drawn as the connection editor draws the
    /// field: a badge, then the name. Null for every other fact.
    /// </summary>
    public SecretReferenceDisplay? Secret { get; init; }

    public bool IsSecret => !IsSection && Secret is not null;
}

internal sealed class ConnectionsSidebar : INotifyPropertyChanged
{
    private readonly Func<IRemoteStorageAgentClient>? _client;
    private readonly IDialogService? _dialogs;
    private readonly Func<IReadOnlyList<ConnectionGroupEntry>?>? _legacyGroups;
    private readonly Func<IReadOnlyDictionary<string, string>?>? _legacyIcons;
    private readonly Func<string?, string?, string, Task<IconChoice>>? _pickIcon;
    private readonly Func<IRemoteConnectionProfileClient>? _profiles;
    private readonly Func<IKeyStoreAgentClient>? _keyStore;

    /// <summary>The Key Store's entries by reference, so the details can name the key a connection uses.</summary>
    private readonly SecretReferenceNames _keyNames = new();

    /// <summary>
    /// The groups closed by hand, by id, so a listing or a search that draws them again draws
    /// them closed, as 1.x's <c>_collapsedGroups</c> kept them. For the session only, as 1.x's was.
    /// Ungrouped is <see cref="Guid.Empty"/>.
    /// </summary>
    private readonly HashSet<Guid> _collapsed = [];

    /// <summary>The groups the agent keeps, in the panel's order, from its last answer.</summary>
    private IReadOnlyList<ConnectionGroupDocument> _groups = [];

    /// <summary>
    /// Whether the agent has been offered the arrangement this desktop kept before groups were the
    /// agent's, and answered, this session. It keeps the first one it is sent, so asking again
    /// would change nothing; this only saves the round trip.
    /// </summary>
    private volatile bool _legacyOffered;

    /// <summary>The group last opened, for a new connection when no card is selected.</summary>
    private Guid? _openedGroup;

    /// <summary>Whether Favorites was closed, kept apart because a group of one's own may share its name.</summary>
    private bool _favoritesCollapsed;
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

    /// <param name="legacyGroups">
    /// The arrangement this desktop kept in its settings file before groups were the agent's, read
    /// once to bring it across. Null means there is none.
    /// </param>
    /// <param name="legacyIcons">The icons chosen for those groups, by name.</param>
    /// <param name="pickIcon">Asks for a group's icon and colour: the current icon, colour and a title.</param>
    /// <param name="profiles">
    /// Reads and writes a whole profile, which is what marking a favourite takes. A factory for the
    /// same reason <paramref name="client"/> is one: each toggle opens a connection and closes it.
    /// Null leaves Toggle favorite dim.
    /// </param>
    /// <param name="keyStore">
    /// Lists the Key Store, so the details can name the entry a connection's key or certificate
    /// is. Null says Vault for every secret, which is true as far as it goes.
    /// </param>
    internal ConnectionsSidebar(
        ICommand newCommand,
        Func<IRemoteStorageAgentClient>? client = null,
        IDialogService? dialogs = null,
        Func<IReadOnlyList<ConnectionGroupEntry>?>? legacyGroups = null,
        Func<IReadOnlyDictionary<string, string>?>? legacyIcons = null,
        Func<string?, string?, string, Task<IconChoice>>? pickIcon = null,
        Func<IRemoteConnectionProfileClient>? profiles = null,
        Func<IKeyStoreAgentClient>? keyStore = null)
    {
        _keyStore = keyStore;
        NewCommand = newCommand;
        _client = client;
        _dialogs = dialogs;
        _legacyGroups = legacyGroups;
        _legacyIcons = legacyIcons;
        _pickIcon = pickIcon;
        _profiles = profiles;
        Status = Ui.Connections.SidebarEmpty;
        NewGroupCommand = new RelayCommand(_ => _ = AddGroupAsync(), _ => _dialogs is not null && _profiles is not null);
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
            LoadProfile();
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
    public IReadOnlyList<ConnectionDetailRow> Details => _selected is not { Card: var card } row
        ? []
        : ConnectionDetailFacts.Build(
            card,
            Connections.FirstOrDefault(listed => listed.ConnectionId == row.Id),
            _profile is { } profile && profile.ConnectionId == row.Id ? profile : null,
            _keyNames,
            GroupNameOf(card.GroupId) ?? Ui.Connections.Ungrouped);

    /// <summary>
    /// The selected connection's saved profile, which the Server, Authentication, Security and
    /// Transfer sections are read from, or null until it has been read.
    /// </summary>
    private ConnectionProfileDocument? _profile;

    /// <summary>The connection and listed version last asked for, so a listing does not ask again.</summary>
    private (Guid Id, long? Version)? _profileAsked;

    /// <summary>
    /// Reads the selected connection's profile for the details, once per connection and version, as
    /// 1.4's panel did on selection. A connection that cannot be read keeps saying "Loading…" in
    /// those sections rather than inventing an answer.
    /// </summary>
    private void LoadProfile()
    {
        if (_selected is not { } row || _profiles is null) return;
        var version = Connections.FirstOrDefault(listed => listed.ConnectionId == row.Id)?.Version;
        if (_profileAsked == (row.Id, version)) return;
        _profileAsked = (row.Id, version);
        _ = LoadProfileAsync(row.Id);
    }

    private async Task LoadProfileAsync(Guid connectionId)
    {
        try
        {
            ConnectionProfileGetResponse response;
            await using (var profiles = _profiles!())
            {
                response = await profiles
                    .GetAsync(new ConnectionProfileGetRequest(ConnectionProfileIpcContract.CurrentVersion, connectionId))
                    .ConfigureAwait(true);
            }

            // Read with the profile, so the details name its key from a listing as fresh as it.
            var keys = await ReadKeyStoreAsync().ConfigureAwait(true);

            void Show()
            {
                if (response.Profile is not { } profile || _selected?.Id != connectionId) return;
                _profile = profile;
                if (keys is not null) _keyNames.Replace(keys);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Details)));
            }

            if (Dispatcher.UIThread.CheckAccess()) Show();
            else Dispatcher.UIThread.Post(Show);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The sections go on saying "Loading…"; the next selection or listing asks again.
            _profileAsked = null;
        }
    }

    /// <summary>
    /// The whole Key Store, or nothing when there is none to ask or it did not answer, in which
    /// case the names already known are kept.
    /// </summary>
    private async Task<KeyStoreEntryDocument[]?> ReadKeyStoreAsync()
    {
        if (_keyStore is null) return null;
        try
        {
            await using var client = _keyStore();
            var listed = await client
                .ListAsync(new KeyStoreListRequest(KeyStoreIpcContract.CurrentVersion, Limit: KeyStoreIpcLimits.MaximumEntriesPerPage))
                .ConfigureAwait(true);
            return listed.Failure is null ? listed.Entries : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

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

            // The agent records the outcome against the connection, so listing again is what moves
            // the card off "Not tested" to Healthy or Unavailable, as 1.4's panel did. What the test
            // said stays under the details through it.
            await RefreshAsync(cancellationToken).ConfigureAwait(true);
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
    /// The panel's groups: the agent's, in their order, then Ungrouped while anything is in it.
    /// </summary>
    /// <remarks>
    /// Groups rather than the Storage and Clients split 1.x had, which was the provider's
    /// classification standing in for an organising principle: somebody with four buckets and two
    /// shells for one project wants those six things together. The agent keeps the groups, so a
    /// new connection is filed in one from the editor, from a list, rather than by typing a folder.
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

            // The old arrangement first, so the listing drawn is one with it in; a connection it
            // filed is at a new version, so the connections are listed again once it is in.
            if (!_legacyOffered && await OfferLegacyArrangementAsync(response.Connections, cancellationToken)
                    .ConfigureAwait(false))
            {
                response = await client
                    .ListConnectionsAsync(new ConnectionListRequest(IncludeDisabled: true), cancellationToken)
                    .ConfigureAwait(false);
                if (response.Failure is { } relisted)
                {
                    Apply([], relisted.Message);
                    return;
                }
            }

            var groups = await ReadGroupsAsync(cancellationToken).ConfigureAwait(false);
            Apply(response.Connections, Ui.Connections.SidebarEmpty, listed: true, groups);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Apply([], Ui.Shell.AgentNotConnected);
        }
    }

    /// <summary>
    /// Brings the arrangement this desktop kept before groups were the agent's into the agent, if
    /// it has not had one.
    /// </summary>
    /// <returns>Whether connections may have been filed, so the listing is out of date.</returns>
    /// <remarks>
    /// The saved groups, their order, members and icons, and a group for each folder path nobody
    /// had filed elsewhere, as the panel used to show them. The agent keeps the first arrangement
    /// it is sent and answers every later one with AlreadyImported, so a second desktop, or this
    /// one after a failure, changes nothing. A failure leaves it to be offered at the next listing.
    /// </remarks>
    private async Task<bool> OfferLegacyArrangementAsync(
        IReadOnlyList<ConnectionSummary> connections,
        CancellationToken cancellationToken)
    {
        if (_profiles is null) return false;
        try
        {
            IReadOnlyList<ConnectionCardModel> cards = [.. connections.Select(ConnectionCardFactory.Create)];
            var entries = ConnectionGrouping.LegacyImport(_legacyGroups?.Invoke(), _legacyIcons?.Invoke(), cards);
            await using var profiles = _profiles();
            var response = await profiles
                .ImportGroupsAsync(
                    new ConnectionGroupImportRequest(ConnectionProfileIpcContract.CurrentVersion, [.. entries]),
                    cancellationToken)
                .ConfigureAwait(false);
            if (response.Status is not (ConnectionGroupWriteStatus.Succeeded or ConnectionGroupWriteStatus.AlreadyImported))
            {
                return false;
            }

            _legacyOffered = true;
            return response.Status == ConnectionGroupWriteStatus.Succeeded;
        }
        catch (NotSupportedException)
        {
            // A profile client that keeps no groups, as in a test of something else.
            _legacyOffered = true;
            return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>The agent's groups, or null when they could not be read, which keeps the last ones known.</summary>
    private async Task<IReadOnlyList<ConnectionGroupDocument>?> ReadGroupsAsync(CancellationToken cancellationToken)
    {
        if (_profiles is null) return null;
        try
        {
            await using var profiles = _profiles();
            var listed = await profiles
                .ListGroupsAsync(new ConnectionGroupListRequest(ConnectionProfileIpcContract.CurrentVersion), cancellationToken)
                .ConfigureAwait(false);
            return listed.Failure is null ? listed.Groups : null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>The name of a group the agent keeps, or null for none or one it no longer has.</summary>
    internal string? GroupNameOf(Guid? groupId) =>
        groupId is { } id ? _groups.FirstOrDefault(group => group.GroupId == id)?.Name : null;

    /// <summary>The groups the agent keeps, in the panel's order, for the connection editor.</summary>
    internal IReadOnlyList<ConnectionGroupDocument> KnownGroups => _groups;

    /// <summary>
    /// The group a new connection starts in: the selected card's, or else the group last opened,
    /// or else none, which is Ungrouped.
    /// </summary>
    internal Guid? SuggestedGroupId
    {
        get
        {
            var candidate = _selected is { } row ? row.Card.GroupId : _openedGroup;
            return GroupNameOf(candidate) is null ? null : candidate;
        }
    }

    /// <summary>
    /// Files a connection in a group, or in Ungrouped, as dropping it there does.
    /// </summary>
    /// <remarks>
    /// The agent moves the connection to a new version, so the panel lists again rather than
    /// guessing; one already in that group is left alone. Favorites is not somewhere to file
    /// anything, so a drop there does nothing.
    /// </remarks>
    internal async Task MoveToGroupAsync(Guid connectionId, ConnectionGroupModel target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.IsFavorites || connectionId == Guid.Empty) return;
        var card = _cards.FirstOrDefault(candidate => candidate.ConnectionId == connectionId);
        if (card is null || card.GroupId == target.GroupId && GroupNameOf(card.GroupId) is not null) return;
        if (card.GroupId is null && target.IsUngrouped) return;

        if (await WriteGroupsAsync(
                profiles => profiles.AssignGroupAsync(
                    new ConnectionGroupAssignRequest(ConnectionProfileIpcContract.CurrentVersion, connectionId, target.GroupId),
                    cancellationToken),
                cancellationToken).ConfigureAwait(true))
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(true);
            ConnectionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Moves a whole group up or down the panel, by one place.</summary>
    internal Task MoveGroupAsync(Guid groupId, int step, CancellationToken cancellationToken = default)
    {
        var index = _groups.ToList().FindIndex(group => group.GroupId == groupId);
        if (index < 0 || index + step < 0 || index + step >= _groups.Count) return Task.CompletedTask;
        return WriteGroupsAsync(
            profiles => profiles.MoveGroupAsync(
                new ConnectionGroupMoveRequest(ConnectionProfileIpcContract.CurrentVersion, groupId, index + step),
                cancellationToken),
            cancellationToken);
    }

    /// <summary>Asks for a name and adds an empty group, after the others, to file things into.</summary>
    internal async Task AddGroupAsync(CancellationToken cancellationToken = default)
    {
        if (_dialogs is null || _profiles is null) return;
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

        await WriteGroupsAsync(
            profiles => profiles.CreateGroupAsync(
                new ConnectionGroupCreateRequest(ConnectionProfileIpcContract.CurrentVersion, name.Trim()),
                cancellationToken),
            cancellationToken).ConfigureAwait(true);
    }

    internal async Task RenameGroupAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        if (_dialogs is null || _groups.FirstOrDefault(group => group.GroupId == groupId) is not { } current) return;
        var name = await _dialogs.PromptAsync(
            new DialogPromptRequest
            {
                Title = Ui.Connections.RenameGroup,
                Label = Ui.Connections.GroupName,
                Value = current.Name,
                Accept = Ui.Shell.RenameWorkspaceAccept,
                Validate = candidate => Taken(candidate, groupId)
            },
            cancellationToken).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name.Trim(), current.Name, StringComparison.Ordinal)) return;

        await WriteGroupsAsync(
            profiles => profiles.UpdateGroupAsync(
                new ConnectionGroupUpdateRequest(
                    ConnectionProfileIpcContract.CurrentVersion, groupId, name.Trim(), current.IconKey, current.ColorKey),
                cancellationToken),
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Removes a group once somebody has said yes, keeping everything that was filed in it.
    /// </summary>
    /// <remarks>
    /// Asked first, because the connections it held would have to be filed again one by one to
    /// put it back. They go to Ungrouped, each at a new version, so the panel lists again.
    /// </remarks>
    internal async Task RemoveGroupAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        if (_groups.FirstOrDefault(group => group.GroupId == groupId) is not { } current) return;
        if (_dialogs is not null)
        {
            var choice = await _dialogs.ConfirmAsync(
                new DialogRequest
                {
                    Title = Ui.Connections.RemoveGroupTitle,
                    Message = Ui.Format(Ui.Connections.RemoveGroupPromptFormat, current.Name),
                    Severity = DialogSeverity.Warning,
                    Buttons = DialogButtons.YesNo
                },
                cancellationToken).ConfigureAwait(true);
            if (choice != DialogChoice.Yes) return;
        }

        if (await WriteGroupsAsync(
                profiles => profiles.DeleteGroupAsync(
                    new ConnectionGroupDeleteRequest(ConnectionProfileIpcContract.CurrentVersion, groupId),
                    cancellationToken),
                cancellationToken).ConfigureAwait(true))
        {
            _collapsed.Remove(groupId);
            await RefreshAsync(cancellationToken).ConfigureAwait(true);
            ConnectionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Chooses a group's icon and colour, or clears either back to a plain folder.
    /// </summary>
    internal async Task ChangeGroupIconAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        if (_pickIcon is null || _groups.FirstOrDefault(group => group.GroupId == groupId) is not { } current) return;
        var choice = await _pickIcon(
            current.IconKey,
            current.ColorKey,
            Ui.Format(Ui.Connections.IconPickerGroupTitleFormat, current.Name)).ConfigureAwait(true);
        if (!choice.Chosen) return;

        await WriteGroupsAsync(
            profiles => profiles.UpdateGroupAsync(
                new ConnectionGroupUpdateRequest(
                    ConnectionProfileIpcContract.CurrentVersion, groupId, current.Name, choice.Key, choice.Color),
                cancellationToken),
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Sends one change to the groups, and draws the groups the agent answers with.
    /// </summary>
    /// <returns>Whether the agent made the change.</returns>
    /// <remarks>
    /// A refusal is said in a dialog, because nothing in the panel may be selected to say it
    /// under, and the groups are drawn as the agent has them either way.
    /// </remarks>
    private async Task<bool> WriteGroupsAsync(
        Func<IRemoteConnectionProfileClient, Task<ConnectionGroupWriteResponse>> write,
        CancellationToken cancellationToken)
    {
        if (_profiles is null) return false;
        string? refusal;
        try
        {
            ConnectionGroupWriteResponse response;
            await using (var profiles = _profiles())
            {
                response = await write(profiles).ConfigureAwait(true);
            }

            if (response.Groups is { } groups) _groups = groups;
            Rebuild();
            if (response.Status == ConnectionGroupWriteStatus.Succeeded) return true;
            refusal = response.Failure?.Message ?? Ui.Connections.GroupWriteFailed;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            refusal = Ui.Connections.GroupWriteFailed;
        }

        if (_dialogs is not null)
        {
            await _dialogs.ShowAsync(
                new DialogRequest
                {
                    Title = Ui.Connections.PanelTitle,
                    Message = refusal,
                    Severity = DialogSeverity.Warning
                },
                cancellationToken).ConfigureAwait(true);
        }

        return false;
    }

    /// <summary>Why a group cannot be called this, or nothing.</summary>
    private string? Taken(string candidate, Guid? except = null)
    {
        var trimmed = candidate?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) return Ui.Validation.AGroupNameIsRequired;

        var clashes = _groups.Any(group =>
            group.GroupId != except &&
            string.Equals(group.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        return clashes ? Ui.Shell.NameAlreadyExists : null;
    }

    /// <param name="listed">
    /// The agent answered with this list, rather than a failure leaving it empty; only then are
    /// the names replaced.
    /// </param>
    /// <param name="groups">The agent's groups as just read, or null to keep the last ones known.</param>
    private void Apply(
        IReadOnlyList<ConnectionSummary> connections,
        string status,
        bool listed = false,
        IReadOnlyList<ConnectionGroupDocument>? groups = null)
    {
        void Update()
        {
            IReadOnlyList<ConnectionCardModel> cards = [.. connections.Select(ConnectionCardFactory.Create)];
            Connections = connections;
            _cards = cards;
            if (groups is not null) _groups = groups;
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

        // Favourites first, as 1.x listed them: the enabled ones, by name. A disabled favourite
        // stays only in its group, where a connection that cannot be opened is shown dimmed.
        var favorites = _cards
            .Where(static card => card is { IsEnabled: true, IsFavorite: true, ConnectionId: not null })
            .Where(card => ConnectionPickerFilter.Matches(card, _search))
            .OrderBy(static card => card.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(card => new ConnectionRowModel(card, GroupNameOf(card.GroupId)))
            .ToArray();
        Favorites = favorites.Length == 0
            ? null
            : new ConnectionGroupModel(Ui.Connections.GroupFavorites, favorites, ConnectionGroupKind.Favorites)
            {
                IsExpanded = !_favoritesCollapsed
            };
        if (Favorites is { } starred)
        {
            starred.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ConnectionGroupModel.IsExpanded)) _favoritesCollapsed = !starred.IsExpanded;
            };
        }

        // In a group, by name: the agent keeps which group, and a name is what somebody looks for.
        ConnectionRowModel[] RowsFor(Func<ConnectionCardModel, bool> filed, string? groupName) => [.. _cards
            .Where(static card => card.ConnectionId is not null)
            .Where(filed)
            .Where(card => _showFavoritesInTheirFolders || card is not { IsEnabled: true, IsFavorite: true })
            .Where(card => ConnectionPickerFilter.Matches(card, _search))
            .OrderBy(static card => card.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(card => new ConnectionRowModel(card, groupName))];

        Groups.Clear();
        var matched = favorites.Length;
        var ordered = _groups.OrderBy(static group => group.SortOrder).ToArray();
        for (var position = 0; position < ordered.Length; position++)
        {
            var group = ordered[position];
            var id = group.GroupId;
            var rows = RowsFor(card => card.GroupId == id, group.Name);
            matched += rows.Length;

            // A search shows where it found something, not every group it found nothing in.
            if (HasSearch && rows.Length == 0) continue;

            var first = position == 0;
            var last = position == ordered.Length - 1;
            var drawn = new ConnectionGroupModel(group.Name, rows, ConnectionGroupKind.Group, id, group.IconKey, group.ColorKey)
            {
                IsExpanded = !_collapsed.Contains(id),
                RenameCommand = new RelayCommand(_ => _ = RenameGroupAsync(id), _ => _dialogs is not null),
                ChangeIconCommand = new RelayCommand(_ => _ = ChangeGroupIconAsync(id), _ => _pickIcon is not null),
                MoveUpCommand = new RelayCommand(_ => _ = MoveGroupAsync(id, -1), _ => !first),
                MoveDownCommand = new RelayCommand(_ => _ = MoveGroupAsync(id, 1), _ => !last),
                RemoveCommand = new RelayCommand(_ => _ = RemoveGroupAsync(id))
            };
            Track(drawn, id);
            Groups.Add(drawn);
        }

        // Ungrouped last, and only while something is in it: a connection in no group, or in one
        // the agent no longer has.
        var known = ordered.Select(static group => group.GroupId).ToHashSet();
        var loose = RowsFor(card => card.GroupId is not { } group || !known.Contains(group), null);
        matched += loose.Length;
        if (loose.Length > 0)
        {
            var ungrouped = new ConnectionGroupModel(Ui.Connections.Ungrouped, loose, ConnectionGroupKind.Ungrouped)
            {
                IsExpanded = !_collapsed.Contains(Guid.Empty)
            };
            Track(ungrouped, Guid.Empty);
            Groups.Add(ungrouped);
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

    /// <summary>Remembers a group being closed or opened, and an opened one as where a new connection goes.</summary>
    private void Track(ConnectionGroupModel drawn, Guid key)
    {
        drawn.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ConnectionGroupModel.IsExpanded)) return;
            if (drawn.IsExpanded)
            {
                _collapsed.Remove(key);
                _openedGroup = drawn.GroupId;
            }
            else
            {
                _collapsed.Add(key);
            }
        };
    }

    /// <summary>Every card on the panel, Favorites' first, a favourite's two copies both included.</summary>
    private IEnumerable<ConnectionRowModel> Rows() =>
        (Favorites?.Connections ?? Enumerable.Empty<ConnectionRowModel>())
            .Concat(Groups.SelectMany(static group => group.Connections));

    private void Set<T>(ref T field, T value, string name)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
