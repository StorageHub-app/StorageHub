using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Lucide.Avalonia;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>
/// Somewhere a pane can be pointed at: a saved connection, or this computer.
/// </summary>
/// <param name="Id">
/// The connection's id, or null for This PC. Null is what distinguishes the two, so the picker
/// needs no separate concept for the local entry and neither does anything reading the choice.
/// </param>
/// <param name="Kind">
/// What the pane becomes when this is chosen. An SSH client is a terminal rather than a listing,
/// and it is the only entry in the picker that is: carrying the answer on the choice means the
/// pane never has to look a connection back up to find out what it is showing.
/// </param>
internal sealed record PaneConnection(
    Guid? Id,
    string Name,
    LucideIconKind Icon,
    PaneContentKind Kind = PaneContentKind.SavedStorage);

/// <summary>
/// One browser pane: a connection, a path, and what is in it.
/// </summary>
/// <remarks>
/// <para>
/// A view model over <see cref="RemoteBrowserController"/>, which already owns connecting,
/// listing, paging, history and path normalisation, and has its own suite. What is here is the
/// part BrowserPaneControl mixed into 3,621 lines of drawing: which commands are available, what
/// the address bar says, and what the pane shows while it is waiting or after it failed.
/// </para>
/// <para>
/// Nothing here touches a control. That is what lets a headless test drive a pane through an
/// entire navigation - open a connection, enter a folder, go up, fail, recover - against a fake
/// agent, which is the thing the WinForms pane could never be tested for.
/// </para>
/// </remarks>
internal sealed class BrowserPaneModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly RemoteBrowserController _controller;
    private readonly Func<ILocalFileBrowserDataSource?> _localSource;
    private IPaneSource? _source;
    private string _status = string.Empty;
    private bool _busy;
    private bool _isActive;
    private bool _hasMore;
    private PaneConnection? _connection;
    private PaneContentKind _contentKind = PaneContentKind.ConnectionsHome;
    private string _path = "/";
    private string? _note;
    private BrowserListItem? _selected;

    /// <remarks>
    /// Built on the first listing rather than in the constructor, because it opens a SQLite file
    /// and a pane that is never pointed anywhere should not leave one behind. Four panes in a
    /// workspace would otherwise be four databases created before anything was browsed.
    /// </remarks>
    private PagedListingIndex? _index;
    private BrowserSortColumn _sortColumn = BrowserSortColumn.Name;
    private bool _sortAscending = true;
    private string _filter = string.Empty;
    private bool _isAtRoot = true;

    /// <param name="localSource">
    /// How This PC lists drives and folders. A factory so a test can hand over a directory tree in
    /// memory; null means the real filesystem.
    /// </param>
    internal BrowserPaneModel(
        IRemoteStorageAgentClient? client = null,
        Func<ILocalFileBrowserDataSource?>? localSource = null)
    {
        _controller = new RemoteBrowserController(client);
        _localSource = localSource ?? (static () => null);
        SelectedRows.CollectionChanged += (_, _) =>
        {
            Raise(nameof(HasSelection));
            Raise(nameof(SelectionSummary));
        };

        SortByCommand = new RelayCommand(column =>
        {
            if (column is BrowserSortColumn chosen) SortBy(chosen);
        });
        SelectAllCommand = new RelayCommand(_ => SelectAll(), _ => Selectable().Count > 0);
        InvertSelectionCommand = new RelayCommand(_ => InvertSelection(), _ => Selectable().Count > 0);
        ClearFilterCommand = new RelayCommand(_ => Filter = string.Empty, _ => HasFilter);

        OpenCommand = new RelayCommand(_ => _ = OpenSelectedAsync(), _ => Selected?.IsContainer == true);
        UpCommand = new RelayCommand(_ => _ = UpAsync(), _ => _source?.CanGoUp == true);
        BackCommand = new RelayCommand(_ => _ = BackAsync(), _ => _source?.CanGoBack == true);
        ForwardCommand = new RelayCommand(_ => _ = ForwardAsync(), _ => _source?.CanGoForward == true);
        RefreshCommand = new RelayCommand(_ => _ = MoveAsync(PaneNavigationKind.Refresh));
    }

    public ObservableCollection<PaneConnection> Connections { get; } = [];

    public ObservableCollection<BrowserListItem> Rows { get; } = [];

    /// <summary>
    /// Every row somebody has chosen.
    /// </summary>
    /// <remarks>
    /// Bound to the table's selection, so it is the table that owns the collection's contents. A
    /// transfer acts on all of these; <see cref="Selected"/> is only which one the keyboard is on
    /// and which one Open would enter.
    /// </remarks>
    public ObservableCollection<BrowserListItem> SelectedRows { get; } = [];

    /// <summary>The path as the address bar shows it: always rooted, never empty.</summary>
    public string Path
    {
        get => _path;
        private set
        {
            if (string.Equals(_path, value, StringComparison.Ordinal)) return;
            _path = value;
            Raise(nameof(Path));
        }
    }

    /// <summary>What the pane is doing, or why it is not showing anything.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            if (string.Equals(_status, value, StringComparison.Ordinal)) return;
            _status = value;
            Raise(nameof(Status));
            Raise(nameof(HasStatus));
        }
    }

    public bool HasStatus => !string.IsNullOrEmpty(_status);

    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (_busy == value) return;
            _busy = value;
            Raise(nameof(IsBusy));
        }
    }

    /// <summary>Whether this is the pane a pane command would act on.</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            Raise(nameof(IsActive));
        }
    }

    public PaneConnection? Connection
    {
        get => _connection;
        set
        {
            if (_connection == value) return;
            _connection = value;
            Raise(nameof(Connection));
            Raise(nameof(Title));
            if (value is not null) _ = OpenAsync(value);
        }
    }

    /// <summary>The listing index, built the first time there is a listing to put in it.</summary>
    private PagedListingIndex Index => _index ??= new PagedListingIndex();

    /// <summary>The pane's heading: the connection, or an invitation to choose one.</summary>
    public string Title => _connection?.Name ?? Ui.Pane.SelectProfileToConnect;

    public BrowserListItem? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            Raise(nameof(Selected));
            RaiseCommands();
        }
    }

    /// <summary>Whether anything a transfer could act on is selected.</summary>
    public bool HasSelection => SelectedRows.Any(static row => !row.IsParentNavigation);

    /// <summary>
    /// "3 selected, 1.2 MB", in the shell's own wording.
    /// </summary>
    /// <remarks>
    /// The same format string the status bar uses, so a pane and the bar below it never disagree
    /// about what is selected. A folder contributes no bytes because nobody has counted what is
    /// inside it yet, which is also what the old shell did.
    /// </remarks>
    public string SelectionSummary
    {
        get
        {
            var chosen = SelectedRows.Where(static row => !row.IsParentNavigation).ToArray();
            return chosen.Length == 0
                ? string.Empty
                : Ui.Format(
                    Ui.Shell.StatusSelectionFormat,
                    chosen.Length,
                    UiFormatting.FormatBytes(chosen.Sum(static row => row.Length ?? 0)));
        }
    }

    /// <summary>Which column the listing is ordered by.</summary>
    public BrowserSortColumn SortColumn => _sortColumn;

    /// <summary>Whether that order runs A to Z, smallest first, oldest first.</summary>
    public bool SortAscending => _sortAscending;

    /// <summary>
    /// What is typed in the filter box, narrowing the listing as it is typed.
    /// </summary>
    /// <remarks>
    /// It narrows what is shown, not what was fetched: the index still holds every row, so
    /// clearing the box restores the listing without asking the agent for it again. Wildcards are
    /// the index's, which is what makes a filter mean the same thing here as it did in 1.x.
    /// </remarks>
    public string Filter
    {
        get => _filter;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_filter, value, StringComparison.Ordinal)) return;
            _filter = value;
            Raise(nameof(Filter));
            Raise(nameof(HasFilter));
            ApplyView();
        }
    }

    public bool HasFilter => _filter.Length > 0;

    /// <summary>The column headings, each carrying the sort arrow when it is the sorted one.</summary>
    public string NameHeader => Heading(BrowserSortColumn.Name, Ui.Pane.ColumnName);

    /// <inheritdoc cref="NameHeader"/>
    public string SizeHeader => Heading(BrowserSortColumn.Size, Ui.Pane.ColumnSize);

    /// <inheritdoc cref="NameHeader"/>
    public string TypeHeader => Heading(BrowserSortColumn.Type, Ui.Pane.ColumnType);

    /// <inheritdoc cref="NameHeader"/>
    public string ModifiedHeader => Heading(BrowserSortColumn.Modified, Ui.Pane.ColumnModified);

    /// <inheritdoc cref="NameHeader"/>
    public string StatusHeader => Heading(BrowserSortColumn.Status, Ui.Pane.ColumnStatus);

    /// <summary>Where this pane is pointed, for a transfer to describe.</summary>
    internal IPaneSource? Source => _source;

    /// <summary>Whether the listing has pages nobody has asked for yet.</summary>
    internal bool HasMorePages => _hasMore;

    /// <summary>
    /// Copy and move, which the workspace owns and the pane only shows.
    /// </summary>
    /// <remarks>
    /// Assigned by WorkspaceModel rather than built here, because a transfer needs both panes and
    /// a pane knows nothing about the other one. The pane draws two buttons; what they do, and
    /// which pane is the destination, is a decision one level up.
    /// </remarks>
    public ICommand? CopyCommand
    {
        get;
        internal set
        {
            field = value;
            Raise(nameof(CopyCommand));
        }
    }

    /// <inheritdoc cref="CopyCommand"/>
    public ICommand? MoveCommand
    {
        get;
        internal set
        {
            field = value;
            Raise(nameof(MoveCommand));
        }
    }

    /// <inheritdoc cref="CopyCommand"/>
    public ICommand? PasteCommand
    {
        get;
        internal set
        {
            field = value;
            Raise(nameof(PasteCommand));
        }
    }

    /// <summary>
    /// What this pane is showing: a listing, or a terminal.
    /// </summary>
    /// <remarks>
    /// The same <see cref="PaneContentKind"/> a saved workspace stores, so what is reopened is
    /// what was closed. An SSH client is the one kind with no listing behind it, which is why the
    /// transfer commands ask before acting on this pane.
    /// </remarks>
    public PaneContentKind ContentKind
    {
        get => _contentKind;
        private set
        {
            if (_contentKind == value) return;
            _contentKind = value;
            Raise(nameof(ContentKind));
            Raise(nameof(IsTerminal));
            Raise(nameof(IsListing));
            RaiseCommands();
        }
    }

    /// <summary>Whether this pane is an SSH terminal rather than a file listing.</summary>
    public bool IsTerminal => _contentKind == PaneContentKind.SshClient;

    /// <summary>Whether this pane shows files, which every kind but an SSH client does.</summary>
    public bool IsListing => !IsTerminal;

    public static string RefreshLabel => Ui.Commands.ViewRefresh;

    public static string CopyLabel => Ui.Commands.EditCopy;

    public static string MoveLabel => Ui.Pane.Move;

    public static string PasteLabel => Ui.Pane.Paste;

    public static string CopyHint => Ui.Pane.StageForCopying;

    public static string MoveHint => Ui.Pane.StageForMoving;

    public static string PasteHint => Ui.Pane.ReviewAndPaste;

    public static string FilterPlaceholder => Ui.Pane.FilterPlaceholder;

    public static string FilterAccessibleName => Ui.Pane.FilterAccessibleName;

    public static string SelectAllLabel => Ui.Pane.SelectAll;

    public static string InvertSelectionLabel => Ui.Pane.InvertSelection;

    public static string TerminalPending => Ui.Pane.TerminalPending;

    public static string TerminalConnectHint => Ui.Pane.TerminalConnectHint;

    public ICommand OpenCommand { get; }

    public ICommand SortByCommand { get; }

    public ICommand SelectAllCommand { get; }

    public ICommand InvertSelectionCommand { get; }

    public ICommand ClearFilterCommand { get; }

    public ICommand UpCommand { get; }

    public ICommand BackCommand { get; }

    public ICommand ForwardCommand { get; }

    public ICommand RefreshCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Reads the connections this pane can be pointed at.
    /// </summary>
    /// <remarks>
    /// Storage connections and SSH clients only, which is the same filter the sidebar and the
    /// connection picker use: the others cannot be browsed, and listing them produces a pane that
    /// fails the moment somebody chooses one.
    /// </remarks>
    internal async Task LoadConnectionsAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        Status = Ui.Pane.LoadingConnections;
        try
        {
            var result = await _controller.LoadConnectionsAsync(cancellationToken).ConfigureAwait(true);
            Connections.Clear();

            // This PC first, always, and whether or not the agent answered. Most transfers have one
            // local end, and a pane that cannot reach the agent can still browse this computer -
            // which is also the state somebody is in while they work out why the agent is down.
            Connections.Add(new PaneConnection(
                null, Ui.Pane.ThisPc, LucideIconKind.HardDrive, PaneContentKind.ThisPc));

            if (result.Status != RemoteBrowserOperationStatus.Succeeded)
            {
                Status = result.ErrorMessage ?? Ui.Pane.Disconnected;
                return;
            }

            foreach (var connection in result.Connections.Where(CanBrowse))
            {
                Connections.Add(new PaneConnection(
                    connection.ConnectionId,
                    connection.DisplayName,
                    Themes.IconCatalog.Resolve(
                        ConnectionIconCatalog.ResolveForConnection(
                            connection.IconKey, MapProvider(connection.Provider), connection.Type))
                        ?? LucideIconKind.Cloud,
                    KindOf(connection)));
            }

            Status = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Points the pane at a connection, or at this computer.
    /// </summary>
    /// <remarks>
    /// The previous source is disposed, which for a connection closes its client. A pane left on a
    /// connection nobody is looking at would otherwise hold a socket for the life of the window.
    /// </remarks>
    internal async Task OpenAsync(PaneConnection choice, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(choice);
        IsBusy = true;
        try
        {
            if (_source is LocalPaneSource)
            {
                await _source.DisposeAsync().ConfigureAwait(true);
            }

            ContentKind = choice.Kind;

            // An SSH client has no listing to fetch. It keeps the pane's chrome -- the picker, the
            // title, the active border -- and replaces only the body, so switching a pane to a
            // shell and back is the same gesture as switching between two buckets.
            if (choice.Kind == PaneContentKind.SshClient)
            {
                _source = null;
                Rows.Clear();
                SelectedRows.Clear();
                Path = choice.Name;

                // Nothing in the status strip: the surface below it already says what a terminal
                // pane is waiting for, and a pane that says it twice reads as two problems.
                Status = string.Empty;
                RaiseCommands();
                return;
            }

            if (choice.Id is { } connectionId)
            {
                var remote = new RemotePaneSource(_controller, choice.Name);
                _source = remote;
                Report(await remote.OpenAsync(connectionId, cancellationToken).ConfigureAwait(true));
                return;
            }

            _source = new LocalPaneSource(new LocalBrowserController(_localSource()));
            Report(await _source
                .MoveAsync(PaneNavigationKind.Navigate, null, cancellationToken)
                .ConfigureAwait(true));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Opens a saved connection by id, for a caller that has one rather than a choice.</summary>
    internal Task OpenConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        var choice = Connections.FirstOrDefault(candidate => candidate.Id == connectionId)
            ?? new PaneConnection(connectionId, string.Empty, LucideIconKind.Cloud);
        return OpenAsync(choice, cancellationToken);
    }

    /// <summary>Goes to a path, or says why it could not.</summary>
    internal async Task NavigateAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        if (_source is null) return;

        IsBusy = true;
        try
        {
            Report(await _source
                .MoveAsync(PaneNavigationKind.Navigate, relativePath, cancellationToken)
                .ConfigureAwait(true));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Opens the selected row when it is a container.
    /// </summary>
    /// <remarks>
    /// The location rather than the name, because the two differ under a prefix: an S3 entry's name
    /// is its last segment and navigating to that from a nested path would look up the wrong key.
    /// </remarks>
    internal Task OpenSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (Selected is not { IsContainer: true } row) return Task.CompletedTask;

        return row.IsParentNavigation
            ? UpAsync(cancellationToken)
            : NavigateAsync(row.Location ?? row.Name, cancellationToken);
    }

    internal Task UpAsync(CancellationToken cancellationToken = default) =>
        MoveAsync(PaneNavigationKind.Up, cancellationToken);

    internal Task BackAsync(CancellationToken cancellationToken = default) =>
        MoveAsync(PaneNavigationKind.Back, cancellationToken);

    internal Task ForwardAsync(CancellationToken cancellationToken = default) =>
        MoveAsync(PaneNavigationKind.Forward, cancellationToken);

    /// <summary>
    /// Back, forward, up and refresh, which the controller already knows how to do.
    /// </summary>
    /// <remarks>
    /// Asking the source for Up rather than computing the parent here is the difference between one
    /// definition of "the folder above" and three: a remote prefix, a local directory and a drive
    /// list each have their own, and each browser already owns and tests its own.
    /// </remarks>
    private async Task MoveAsync(
        PaneNavigationKind kind,
        CancellationToken cancellationToken = default)
    {
        if (_source is null) return;

        IsBusy = true;
        try
        {
            Report(await _source
                .MoveAsync(kind, cancellationToken: cancellationToken)
                .ConfigureAwait(true));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_source is LocalPaneSource local)
        {
            await local.DisposeAsync().ConfigureAwait(false);
        }

        await _controller.DisposeAsync().ConfigureAwait(false);

        // The index owns a SQLite file. A workspace that switched from four panes to two would
        // otherwise leave two behind until the next start swept them.
        _index?.Dispose();
        _index = null;
    }

    /// <summary>
    /// Shows a navigation result, whatever it turned out to be.
    /// </summary>
    /// <remarks>
    /// A failed navigation leaves the rows alone. The old pane cleared them, which meant a typo in
    /// the address bar lost the listing somebody was looking at and cost another round trip to get
    /// back to it.
    /// </remarks>
    private void Report(PaneNavigationResult result)
    {
        if (result.Listing is not { } listing)
        {
            Status = result.Error ?? Ui.Pane.Disconnected;
            RaiseCommands();
            return;
        }

        Path = listing.DisplayPath;
        _hasMore = listing.HasMore;
        _isAtRoot = listing.IsAtRoot;
        _note = listing.Note;

        // A new folder is a new listing, so the filter goes with the old one. Carrying it across
        // would leave somebody in an empty folder that is not empty, with the reason two controls
        // away from where they are looking.
        _filter = string.Empty;
        Raise(nameof(Filter));
        Raise(nameof(HasFilter));

        Index.Reset(listing.Rows);
        ApplyView();
        Raise(nameof(Title));
    }

    /// <summary>
    /// Rebuilds the visible rows from the index, in the current order and under the current filter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ordering and the wildcard matching are <see cref="PagedListingIndex"/>'s, which is in
    /// Desktop.Core with its own suite and is what 1.x sorted through. That matters more than it
    /// sounds: "containers first, then by name, case-insensitively, ties broken by the order the
    /// provider gave" is four rules, and a pane that re-derived them would sort a folder
    /// differently from the shell it replaces.
    /// </para>
    /// <para>
    /// The rows are copied out rather than bound to the view, so a listing is held twice for now.
    /// The index exists to keep a large listing off the heap, and collecting that benefit means
    /// binding the view itself -- which needs <c>IndexedView</c> to be an <c>IList</c> before a
    /// TableView will read it by index instead of enumerating it.
    /// </para>
    /// </remarks>
    private void ApplyView()
    {
        // What was selected, by location: the index hands back decoded copies rather than the
        // instances that went in, and BrowserListItem is a record, so this compares by value.
        var chosen = SelectedRows
            .Where(static row => row.Location is not null)
            .Select(static row => row.Location!)
            .ToHashSet(StringComparer.Ordinal);

        Rows.Clear();
        SelectedRows.Clear();

        // The ".." row is part of the listing rather than a button, which is how every file
        // manager since Norton Commander has done it and what makes double-click enough. It is not
        // in the index: it is navigation, so it survives a filter that matches nothing.
        if (!_isAtRoot)
        {
            Rows.Add(BrowserParentNavigation.Item);
        }

        var matched = 0;
        foreach (var row in Index.CreateView(_sortColumn, _sortAscending, NullIfEmpty(_filter)))
        {
            Rows.Add(row);
            matched++;
            if (row.Location is not null && chosen.Contains(row.Location)) SelectedRows.Add(row);
        }

        // A navigation can succeed and still have something to say. Asking for a folder that has
        // been deleted lands on the nearest parent that does exist, and the message is the only
        // thing that explains why the listing is not the one that was asked for.
        Status = _note is { Length: > 0 } note
            ? note
            : matched > 0 ? string.Empty
            : HasFilter ? Ui.Pane.NoItemsMatchFilter
            : Ui.Pane.FolderIsEmpty;

        Raise(nameof(NameHeader));
        Raise(nameof(SizeHeader));
        Raise(nameof(TypeHeader));
        Raise(nameof(ModifiedHeader));
        Raise(nameof(StatusHeader));
        RaiseCommands();
    }

    /// <summary>
    /// Orders by a column, or reverses the order when it is already the one.
    /// </summary>
    /// <remarks>
    /// A new column starts ascending rather than keeping the previous direction, because "sort by
    /// size" means largest-last until somebody says otherwise, and inheriting a descending name
    /// sort would answer a question nobody asked.
    /// </remarks>
    internal void SortBy(BrowserSortColumn column)
    {
        if (_sortColumn == column)
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortColumn = column;
            _sortAscending = true;
        }

        Raise(nameof(SortColumn));
        Raise(nameof(SortAscending));
        ApplyView();
    }

    /// <summary>Selects everything the filter is showing, which is never the way back out.</summary>
    internal void SelectAll()
    {
        SelectedRows.Clear();
        foreach (var row in Selectable()) SelectedRows.Add(row);
    }

    /// <summary>
    /// Selects what was not selected, and clears what was.
    /// </summary>
    /// <remarks>
    /// Over the visible rows only. Inverting against rows a filter is hiding would select things
    /// nobody can see, which is the one way a filter could make a delete worse than no filter.
    /// </remarks>
    internal void InvertSelection()
    {
        var before = SelectedRows.ToHashSet();
        SelectedRows.Clear();
        foreach (var row in Selectable())
        {
            if (!before.Contains(row)) SelectedRows.Add(row);
        }
    }

    /// <summary>The visible rows a selection can contain: everything but the parent.</summary>
    private IReadOnlyList<BrowserListItem> Selectable() =>
        [.. Rows.Where(static row => !row.IsParentNavigation)];

    /// <summary>A column heading, carrying the sort arrow when it is the column being sorted by.</summary>
    /// <remarks>
    /// The glyph is appended rather than translated: an arrow means the same thing in every
    /// language this shell ships in, and a format string per direction would be two more strings
    /// for each to get wrong.
    /// </remarks>
    private string Heading(BrowserSortColumn column, string text) =>
        _sortColumn == column ? text + (_sortAscending ? " \u25b2" : " \u25bc") : text;

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    /// <summary>
    /// Which kind of pane a connection produces.
    /// </summary>
    /// <remarks>
    /// SFTP and SSH are the same protocol and different panes: one is a filesystem the agent can
    /// list, the other is a shell. The profile type is what separates them, because a saved SSH
    /// profile can be either depending on what it was created for.
    /// </remarks>
    private static PaneContentKind KindOf(ConnectionSummary connection) =>
        connection is { Type: ConnectionProfileType.Client, Provider: StorageConnectionProvider.Ssh }
            ? PaneContentKind.SshClient
            : PaneContentKind.SavedStorage;

    private static bool CanBrowse(ConnectionSummary connection) =>
        connection.IsEnabled &&
        (connection.Type == ConnectionProfileType.Storage ||
            connection is { Type: ConnectionProfileType.Client, Provider: StorageConnectionProvider.Ssh });

    private static StorageProviderKind MapProvider(StorageConnectionProvider provider) => provider switch
    {
        StorageConnectionProvider.Local => StorageProviderKind.Local,
        StorageConnectionProvider.S3 => StorageProviderKind.S3,
        StorageConnectionProvider.Ftp => StorageProviderKind.Ftp,
        StorageConnectionProvider.Ftps => StorageProviderKind.Ftps,
        StorageConnectionProvider.Sftp => StorageProviderKind.Sftp,
        StorageConnectionProvider.Ssh => StorageProviderKind.Ssh,
        _ => StorageProviderKind.Local
    };

    private void RaiseCommands()
    {
        (OpenCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (UpCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (BackCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ForwardCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
