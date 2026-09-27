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
    private Dictionary<string, string> _icons = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<ConnectionCardModel> _cards = [];
    private string _search = string.Empty;

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

    /// <param name="load">Where the saved arrangement comes from. Null means nothing is remembered.</param>
    /// <param name="save">
    /// Where a rearrangement goes. Called on every drag, which is cheap: the file is small and the
    /// alternative is losing an arrangement to a shell that did not close cleanly.
    /// </param>
    internal ConnectionsSidebar(
        ICommand newCommand,
        Func<IRemoteStorageAgentClient>? client = null,
        IDialogService? dialogs = null,
        Func<IReadOnlyList<ConnectionGroupEntry>?>? load = null,
        Action<IReadOnlyList<ConnectionGroupEntry>>? save = null,
        Func<IReadOnlyDictionary<string, string>?>? loadIcons = null,
        Action<IReadOnlyDictionary<string, string>>? saveIcons = null,
        Func<string?, string, Task<IconChoice>>? pickIcon = null)
    {
        NewCommand = newCommand;
        _client = client;
        _dialogs = dialogs;
        _load = load;
        _save = save;
        _saveIcons = saveIcons;
        _pickIcon = pickIcon;
        if (loadIcons?.Invoke() is { } icons)
        {
            _icons = new Dictionary<string, string>(icons, StringComparer.OrdinalIgnoreCase);
        }
        Status = Ui.Connections.SidebarEmpty;
        NewGroupCommand = new RelayCommand(_ => _ = AddGroupAsync(), _ => _dialogs is not null);
        ClearSearchCommand = new RelayCommand(_ => Search = string.Empty);

        OpenSelectedCommand = new RelayCommand(
            _ => { if (_selected is { } row) OpenConnection?.Invoke(row.Id); },
            _ => _selected is not null && OpenConnection is not null);
        TestSelectedCommand = new RelayCommand(
            _ => _ = TestSelectedAsync(), _ => _selected is not null && _client is not null && !_testing);
        EditSelectedCommand = new RelayCommand(
            row => { if ((row as ConnectionRowModel ?? _selected) is { } chosen) EditConnection?.Invoke(chosen.Id); },
            _ => EditConnection is not null);
        DeleteSelectedCommand = new RelayCommand(
            row => { if ((row as ConnectionRowModel ?? _selected) is { } chosen && DeleteConnection is { } delete) _ = DeleteAndRefreshAsync(delete, chosen.Id); },
            _ => DeleteConnection is not null);
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
            if (value is not null) value.IsSelected = true;
            DetailStatus = string.Empty;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelection)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Details)));
            RaiseDetailCommands();
        }
    }

    public bool HasSelection => _selected is not null;

    /// <summary>Selects a card, as a single click does.</summary>
    internal void Select(ConnectionRowModel? row) => Selected = row;

    /// <summary>
    /// The details panel's rows for the selected connection, as 1.x listed them under it.
    /// </summary>
    public IReadOnlyList<ConnectionDetailRow> Details => _selected is not { Card: var card }
        ? []
        : [.. new ConnectionDetailRow[]
            {
                new(Ui.Connections.Provider, card.Descriptor.DisplayName),
                new(Ui.Connections.FieldFolder, card.FolderPath ?? string.Empty),
                new(Ui.Connections.FieldTags, string.Join(", ", card.DisplayTags)),
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

    public ICommand OpenSelectedCommand { get; }

    public ICommand TestSelectedCommand { get; }

    /// <summary>Edit, on the selected card and in the details panel. Its parameter is the row, if any.</summary>
    public ICommand EditSelectedCommand { get; }

    /// <summary>Delete, likewise. The shell asks first; the panel refreshes after.</summary>
    public ICommand DeleteSelectedCommand { get; }

    /// <summary>Opens the editor on a connection. Assigned by the shell, which owns the windows.</summary>
    public Action<Guid>? EditConnection
    {
        get;
        internal set
        {
            field = value;
            RaiseDetailCommands();
        }
    }

    /// <summary>
    /// Deletes a connection, confirming first. Assigned by the shell, so the check against the
    /// listed version is the Connection Manager's and not a second copy of it.
    /// </summary>
    public Func<Guid, Task>? DeleteConnection
    {
        get;
        internal set
        {
            field = value;
            RaiseDetailCommands();
        }
    }

    private async Task DeleteAndRefreshAsync(Func<Guid, Task> delete, Guid id)
    {
        await delete(id).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Asks the agent whether the selected connection answers, and says so under it.</summary>
    internal async Task TestSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (_selected is not { } row || _client is null) return;

        _testing = true;
        DetailStatus = Ui.Connections.DetailTesting;
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

    private void RaiseDetailCommands()
    {
        (OpenSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (TestSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (EditSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DeleteSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
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

    public ICommand NewCommand { get; }

    public ICommand NewGroupCommand { get; }

    public ICommand ClearSearchCommand { get; }

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
    /// A card's right-click menu, as 1.x had it: Open, Open in new pane, Edit, Delete. Built when
    /// it opens, on the card it opened on, which the right-click has just selected.
    /// </summary>
    internal IReadOnlyList<(string Label, Action Run, bool Enabled)> ContextEntriesFor(ConnectionRowModel row) =>
    [
        (Ui.Connections.ContextOpen, () => OpenConnection?.Invoke(row.Id), OpenConnection is not null),
        (Ui.Connections.ContextOpenInNewPane, () => OpenConnectionInNewPane?.Invoke(row.Id), OpenConnectionInNewPane is not null),
        (Ui.Connections.ContextEdit, () => EditConnection?.Invoke(row.Id), EditConnection is not null),
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

    /// <summary>
    /// Opens the Connection Manager, where a saved connection is edited.
    /// </summary>
    /// <remarks>
    /// Assigned by the shell rather than built here, because opening a window needs one to be the
    /// owner and a panel does not know which. Null until it is, which leaves the menu entry dim
    /// rather than failing when it is pressed.
    /// </remarks>
    public ICommand? ManageCommand
    {
        get;
        internal set
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ManageCommand)));
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

    public string ManageLabel => Ui.Connections.ManagerTitle;

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

            Apply(
                [.. response.Connections.Select(ConnectionCardFactory.Create)],
                Ui.Connections.SidebarEmpty);
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

    private void Apply(IReadOnlyList<ConnectionCardModel> cards, string status)
    {
        void Update()
        {
            _cards = cards;
            _arrangement = ConnectionGrouping.Arrange(_arrangement.Count > 0 ? _arrangement : _load?.Invoke(), cards);
            _listingStatus = status;
            Rebuild();
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

        Groups.Clear();
        var matched = 0;
        foreach (var group in _arrangement)
        {
            var name = group.Name;
            var rows = group.Members
                .Where(byId.ContainsKey)
                .Select(id => byId[id])
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

        // A search that matches nothing reads as an empty panel otherwise, which is the same
        // picture as having no connections at all and a very different situation. Anything the
        // listing itself had to say outranks it: an agent that did not answer is the more useful
        // thing to be told, and is still true whatever is typed in the box.
        _selected = null;
        Selected = Groups.SelectMany(static group => group.Connections)
            .FirstOrDefault(row => row.Id == chosen);

        var searchFoundNothing = HasSearch && matched == 0 && _cards.Count > 0;
        IsEmpty = _cards.Count == 0 || searchFoundNothing;
        Status = searchFoundNothing ? Ui.Connections.NoMatches : _listingStatus;
    }

    private void Persist() => _save?.Invoke(_arrangement);

    private void Set<T>(ref T field, T value, string name)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
