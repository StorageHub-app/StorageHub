using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Lucide.Avalonia;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;

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
/// One entry in a pane's Move or swap menu: another pane, and the four ways to reach it.
/// </summary>
/// <remarks>
/// Built by the workspace, because every one of these names a second pane and a pane knows nothing
/// about the others. Rebuilt whenever the arrangement changes, so a menu never offers a pane that
/// has been closed.
/// </remarks>
internal sealed record PaneMoveTarget(string Title, IReadOnlyList<PaneMoveOption> Options);

/// <summary>One thing that can be done to a pane relative to another: swap, or dock on an edge.</summary>
internal sealed record PaneMoveOption(string Title, ICommand Command);

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

    /// <summary>
    /// How this pane reaches a shell, or nothing if it is not allowed to open one.
    /// </summary>
    /// <remarks>
    /// A factory, and a fresh client per session: the terminal protocol holds two pipe connections
    /// open for the life of a session, and panes sharing one would interleave their reads.
    /// </remarks>
    private readonly Func<ISshTerminalAgentClient>? _terminals;
    private readonly Func<IAgentLifecycleController?>? _agentLifecycle;
    private SshTerminalSession? _terminal;
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
    private readonly Func<PaneMutationController>? _mutations;
    private readonly IDialogService? _dialogs;
    private readonly Func<ObjectInspectorAddress, Task>? _inspect;
    private readonly Func<ObjectInspectorAddress, string, long?, Task>? _edit;
    private readonly Func<IReadOnlyList<string>, IReadOnlyList<string>, Task<IReadOnlyList<BatchRenameLine>?>>? _batchRename;
    private PagedListingIndex? _index;
    private int _paneNumber = 1;
    private bool _showConnectionBar = true;
    private bool _showFilesBar = true;
    private BrowserSortColumn _sortColumn = BrowserSortColumn.Name;
    private bool _sortAscending = true;
    private string _filter = string.Empty;
    private bool _isAtRoot = true;
    private bool _failed;
    private Task<bool>? _loadingMore;

    /// <summary>
    /// Which navigation the pane is on: moved on by everything that points it somewhere -- another
    /// source, another folder, back, up, a refresh -- and when it is closed.
    /// </summary>
    /// <remarks>
    /// A listing, a page or a quiet re-read lands only if this has not moved since it was asked
    /// for, as 1.x checked its <c>_uiNavigationSequence</c>. Checking the source alone was not
    /// enough: a drive list still on its way when a connection was opened landed on top of the
    /// connection's rows, and a page asked for in one folder is the same source as the next folder.
    /// </remarks>
    private long _navigation;

    /// <summary>The last read of the connection list, so a restore can wait for one in flight.</summary>
    private Task? _connectionsLoad;

    /// <summary>
    /// Set while a saved workspace is putting this pane back, so the list arriving does not first
    /// point it at This PC.
    /// </summary>
    private bool _restoring;

    /// <summary>
    /// Set while a restored connection waits for a click to connect, because "reconnect remote
    /// panes" is off. Not a failure: the banner is a notice somebody can click.
    /// </summary>
    private bool _standingBy;

    /// <param name="mutations">
    /// How the pane creates, renames and deletes. Null leaves those commands unavailable, which is
    /// what a pane in a test that is not about file operations wants.
    /// </param>
    /// <param name="dialogs">
    /// Where the name prompt and the delete confirmation go. Null has the same effect as null
    /// mutations: nothing to ask with is nothing to do.
    /// </param>
    /// <param name="localSource">
    /// How This PC lists drives and folders. A factory so a test can hand over a directory tree in
    /// memory; null means the real filesystem.
    /// </param>
    /// <param name="terminals">
    /// How the pane opens a shell. Null leaves an SSH pane showing what it is waiting for, which is
    /// what a pane in a test that is not about terminals wants.
    /// </param>
    /// <param name="agentLifecycle">
    /// How a stale agent gets restarted when it turns out to be speaking an older protocol. Only the
    /// application's shell supplies one, and only where the agent can be started again. Without
    /// one the terminal reports the mismatch in the agent's own words, which is what 1.x's did when
    /// it had no controller to hand.
    /// </param>
    /// <param name="inspect">
    /// Opens the object inspector for a file. The shell supplies the window; a test supplies a
    /// recorder. Null leaves Properties unavailable.
    /// </param>
    internal BrowserPaneModel(
        IRemoteStorageAgentClient? client = null,
        Func<ILocalFileBrowserDataSource?>? localSource = null,
        Func<PaneMutationController>? mutations = null,
        IDialogService? dialogs = null,
        Func<ISshTerminalAgentClient>? terminals = null,
        Func<IAgentLifecycleController?>? agentLifecycle = null,
        Func<ObjectInspectorAddress, Task>? inspect = null,
        Func<ObjectInspectorAddress, string, long?, Task>? edit = null,
        Func<IReadOnlyList<string>, IReadOnlyList<string>, Task<IReadOnlyList<BatchRenameLine>?>>? batchRename = null)
    {
        _terminals = terminals;
        _agentLifecycle = agentLifecycle;
        _controller = new RemoteBrowserController(client);
        _localSource = localSource ?? (static () => null);
        _mutations = mutations;
        _dialogs = dialogs;
        _inspect = inspect;
        _edit = edit;
        _batchRename = batchRename;
        SelectedRows.CollectionChanged += (_, _) =>
        {
            (OpenCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (EditCommand as RelayCommand)?.RaiseCanExecuteChanged();
            Raise(nameof(HasSelection));
            Raise(nameof(SelectionSummary));
            (RenameCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (BatchRenameCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DeleteCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (PropertiesCommand as RelayCommand)?.RaiseCanExecuteChanged();
        };
        PropertiesCommand = new RelayCommand(_ => _ = InspectAsync(), _ => CanInspect);
        EditCommand = new RelayCommand(_ => _ = EditAsync(), _ => CanEdit);

        NewFolderCommand = new RelayCommand(
            _ => _ = CreateAsync(container: true), _ => CanMutateHere);
        NewFileCommand = new RelayCommand(
            _ => _ = CreateAsync(container: false), _ => CanMutateHere);
        RenameCommand = new RelayCommand(
            _ => _ = RenameAsync(), _ => CanMutateHere && Chosen().Count == 1);
        DeleteCommand = new RelayCommand(
            _ => _ = DeleteAsync(), _ => CanMutateHere && Chosen().Count > 0);
        BatchRenameCommand = new RelayCommand(
            _ => _ = BatchRenameAsync(), _ => _batchRename is not null && CanMutateHere && Chosen().Count > 1);

        SortByCommand = new RelayCommand(column =>
        {
            if (column is BrowserSortColumn chosen) SortBy(chosen);
        });
        SelectAllCommand = new RelayCommand(_ => SelectAll(), _ => Selectable().Count > 0);
        InvertSelectionCommand = new RelayCommand(_ => InvertSelection(), _ => Selectable().Count > 0);
        ClearFilterCommand = new RelayCommand(_ => Filter = string.Empty, _ => HasFilter);

        OpenCommand = new RelayCommand(
            _ => _ = OpenSelectedAsync(), _ => Selected?.IsContainer == true || CanEdit);
        UpCommand = new RelayCommand(_ => _ = UpAsync(), _ => _source?.CanGoUp == true);
        BackCommand = new RelayCommand(_ => _ = BackAsync(), _ => _source?.CanGoBack == true);
        ForwardCommand = new RelayCommand(_ => _ = ForwardAsync(), _ => _source?.CanGoForward == true);
        RefreshCommand = new RelayCommand(_ => _ = MoveAsync(PaneNavigationKind.Refresh));
        Tree.NavigateRequested += (_, target) => _ = NavigateAsync(target);
        LoadMoreCommand = new RelayCommand(_ => _ = LoadMoreAsync(), _ => _hasMore && _loadingMore is null);
    }

    public ObservableCollection<PaneConnection> Connections { get; } = [];

    /// <summary>
    /// Whether to ask before deleting: the "Warn before deleting" setting. Null asks every time.
    /// </summary>
    internal Func<bool>? DeleteConfirmation { get; set; }

    /// <summary>Turns that setting off, for the review's "Don't show this warning again".</summary>
    internal Action? StopDeleteConfirmation { get; set; }

    /// <summary>
    /// The SSH terminal settings, read when a session opens. Null opens it on the defaults.
    /// </summary>
    /// <remarks>
    /// Read per session rather than once per pane, as 1.4 read them per terminal window, so a
    /// change made in Settings reaches the next session without the pane being replaced.
    /// </remarks>
    internal Func<SshTerminalPreferences?>? TerminalPreferences { get; set; }

    /// <summary>The folder tree beside the list.</summary>
    public PaneTreeModel Tree { get; } = new();

    /// <summary>
    /// The same connections as cards, for the picker: name, endpoint, group, and what it is.
    /// </summary>
    /// <remarks>
    /// Kept beside <see cref="Connections"/> rather than replacing it, because a PaneConnection is
    /// what the pane opens and a card is what the picker searches. They are built together from one
    /// listing, so they cannot disagree about what is there.
    /// </remarks>
    internal IReadOnlyList<ConnectionCardModel> Cards => _cards;

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
            Address = value;
        }
    }

    private string _address = "/";

    /// <summary>
    /// What is typed in the address bar. It follows <see cref="Path"/> until somebody types in it,
    /// and Enter goes there, as 1.x's editable address did; 2.0's box was read-only.
    /// </summary>
    public string Address
    {
        get => _address;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_address, value, StringComparison.Ordinal)) return;
            _address = value;
            Raise(nameof(Address));
        }
    }

    /// <summary>Goes to what is typed in the address bar.</summary>
    public ICommand GoToAddressCommand => _goToAddress ??= new RelayCommand(
        _ => _ = GoToAddressAsync(), _ => _source is not null && !IsTerminal);

    private RelayCommand? _goToAddress;

    /// <summary>
    /// Goes to a typed address. A path that does not work says why in the pane and leaves the
    /// listing where it was, so a typo costs nothing; the typed text stays to be corrected.
    /// </summary>
    internal async Task GoToAddressAsync(CancellationToken cancellationToken = default)
    {
        var typed = _address.Trim();
        if (typed.Length == 0 || string.Equals(typed, _path, StringComparison.Ordinal)) return;

        // This PC's own name goes back to the drive list, as it did in 1.x.
        await NavigateAsync(
            string.Equals(typed, Ui.Pane.ThisPc, StringComparison.OrdinalIgnoreCase) && _source is LocalPaneSource
                ? string.Empty
                : typed,
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Puts the current path back in the address bar, as Escape does.</summary>
    internal void ResetAddress() => Address = _path;

    /// <summary>Asks the view to put the cursor in the address bar, as Ctrl+L does.</summary>
    internal void FocusAddress() => FocusAddressRequested?.Invoke(this, EventArgs.Empty);

    internal event EventHandler? FocusAddressRequested;

    /// <summary>What the pane is doing, or why it is not showing anything.</summary>
    public string Status
    {
        get => _status;
        internal set
        {
            if (string.Equals(_status, value, StringComparison.Ordinal)) return;
            _status = value;
            Raise(nameof(Status));
            Raise(nameof(HasStatus));
        }
    }

    public bool HasStatus => !string.IsNullOrEmpty(_status);

    /// <summary>
    /// "This folder is empty." or "No items match the current filter.", centred over the list as
    /// 1.x showed it, rather than in a strip at the bottom of an otherwise blank table.
    /// </summary>
    public string EmptyNotice =>
        _standingBy ? Ui.Pane.Disconnected + ". " + Ui.Pane.SelectProfileToConnect + "."
        : !IsListing || _source is null || _failed || _busy || Rows.Any(static row => !row.IsParentNavigation)
            ? string.Empty
            : HasFilter ? Ui.Pane.NoItemsMatchFilter
            : _source is ConnectionsHomeSource ? Ui.Pane.NoEnabledConnections + ". " + Ui.Pane.UseManageToAddOne + "."
            : Ui.Pane.FolderIsEmpty;

    public bool HasEmptyNotice => EmptyNotice.Length > 0;

    /// <summary>"Fetching folder…" over the list while it is being read, as 1.x's overlay said.</summary>
    public bool ShowsLoading => _busy && IsListing;

    public static string FetchingFolderLabel => Ui.Pane.FetchingFolder;

    /// <summary>
    /// Whether what the banner says is a failure, drawn as a warning and clickable to try again.
    /// Anything else there -- a note that the pane landed on the nearest folder that exists -- is
    /// information and is drawn quietly.
    /// </summary>
    public bool StatusIsWarning => _failed;

    /// <summary>
    /// Whether a click on the banner does something: tries again after a failure, or connects a
    /// pane restored with "reconnect remote panes" off, which 1.x marked with a hand cursor.
    /// </summary>
    public bool BannerIsClickable => _failed || _standingBy;

    /// <summary>Tries again: the connection if it never opened, the folder if it did.</summary>
    public ICommand RetryCommand => _retry ??= new RelayCommand(_ =>
    {
        if (_source is null && _connection is { } connection) _ = OpenAsync(connection);
        else RefreshCommand.Execute(null);
    });

    private RelayCommand? _retry;

    public static string RetryHint => Ui.Commands.ViewRefresh;

    private void RaiseListingState()
    {
        Raise(nameof(EmptyNotice));
        Raise(nameof(HasEmptyNotice));
        Raise(nameof(ShowsLoading));
        Raise(nameof(StatusIsWarning));
        Raise(nameof(BannerIsClickable));
    }

    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (_busy == value) return;
            _busy = value;
            Raise(nameof(IsBusy));
            RaiseConnectionState();
            RaiseListingState();
        }
    }

    /// <summary>
    /// The line beside the connection button: "● Ready", "● Loading…", "○ Choose a saved
    /// connection", as 1.x's pane header said it. Connections Home is somewhere to choose a
    /// connection rather than one, so it says to choose one, as 1.x's PresentConnectionsHome did.
    /// </summary>
    public string ConnectionState =>
        _connection is null ? Ui.Pane.StateChooseConnection
        : _busy ? Ui.Pane.StateLoading
        : IsTerminal ? Ui.Pane.StateSshTerminal
        : _failed ? Ui.Pane.StateLocationUnavailable
        : _source is ConnectionsHomeSource ? Ui.Pane.StateChooseConnection
        : Ui.Pane.StateReady;

    /// <summary>The tone the state line is drawn in: the words and the colour say the same thing.</summary>
    public bool ConnectionStateIsReady =>
        _connection is not null && !_busy && !_failed && _source is not ConnectionsHomeSource;

    public bool ConnectionStateIsBusy => _connection is not null && _busy;

    public bool ConnectionStateIsFailed => _connection is not null && !_busy && _failed;

    /// <summary>
    /// The chip's first badge: what kind of connection this is. Storage is the accent, as in 1.x.
    /// </summary>
    public string TypeBadge => ConnectionCard()?.Type == ConnectionProfileType.Client
        ? Ui.Connections.BadgeClient
        : Ui.Connections.BadgeStorage;

    /// <summary>The chip's second badge: the protocol, e.g. LOCAL, S3, SFTP.</summary>
    public string ProviderBadge =>
        (ConnectionCard()?.Provider ?? StorageProviderKind.Local).ToString().ToUpperInvariant();

    public bool HasBadges => _connection is not null;

    /// <summary>
    /// The connection's colour, for the thin strip across the top of the pane 1.x drew: with
    /// four panes on four servers, colour is what tells them apart before any text is read.
    /// </summary>
    public Avalonia.Media.IBrush AccentBrush =>
        AccentSwatch.BrushFor(ConnectionCard()?.AccentHex ?? ConnectionProviderCatalog.Get(StorageProviderKind.Local).AccentHex);

    /// <summary>
    /// "12 items", "3 of 12 items" while a filter narrows it, and "| more available" or
    /// "| indexing next page…" while the folder has more than has been read, as 1.x's footer said.
    /// </summary>
    public string ItemCount
    {
        get
        {
            var shown = Rows.Count(static row => !row.IsParentNavigation);
            var count = HasFilter && _index is not null
                ? Ui.Format(Ui.Pane.ItemCountFilteredFormat, shown,
                    _index.CreateView(_sortColumn, _sortAscending, null).Count)
                : shown == 1 ? Ui.Pane.ItemCountOne : Ui.Format(Ui.Pane.ItemCountFormat, shown);
            return !_hasMore ? count
                : _loadingMore is not null ? count + Ui.Pane.IndexingNextPageSuffix
                : count + Ui.Pane.MoreAvailableSuffix;
        }
    }

    public static string FilesCaption => Ui.Pane.FilesCaption;

    public static string FilterLabel => Ui.Pane.FilterLabel;

    public static string MoreFileCommandsLabel => Ui.Pane.MoreFileCommands;

    /// <summary>The saved connection behind the pane's choice, or This PC's card for no id.</summary>
    private ConnectionCardModel? ConnectionCard() => _connection is null
        ? null
        : _cards.FirstOrDefault(card => card.ConnectionId == _connection.Id);

    private void RaiseConnectionState()
    {
        Raise(nameof(ConnectionState));
        Raise(nameof(ConnectionStateIsReady));
        Raise(nameof(ConnectionStateIsBusy));
        Raise(nameof(ConnectionStateIsFailed));
        Raise(nameof(TypeBadge));
        Raise(nameof(ProviderBadge));
        Raise(nameof(HasBadges));
        Raise(nameof(AccentBrush));
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
            Raise(nameof(PaneHeading));
        }
    }

    private IReadOnlyList<ConnectionCardModel> _cards = [ConnectionPickerFilter.ThisComputer()];

    /// <summary>Connections Home in the picker, beside This PC, as 1.x offered it.</summary>
    private readonly ConnectionCardModel _homeCard = new(
        Ui.Pane.ConnectionsHome, StorageProviderKind.Local, Ui.Pane.SavedConnections, Ui.Pane.Ready);

    public PaneConnection? Connection
    {
        get => _connection;
        set
        {
            if (_connection == value) return;
            _connection = value;
            Raise(nameof(Connection));
            Raise(nameof(Title));
            Raise(nameof(ConnectionIcon));
            _failed = false;
            RaiseConnectionState();
            if (value is not null) _ = OpenAsync(value);
        }
    }

    /// <summary>The listing index, built the first time there is a listing to put in it.</summary>
    private PagedListingIndex Index => _index ??= new PagedListingIndex();

    /// <summary>What the connection button shows beside the name: the connection's own icon.</summary>
    public LucideIconKind ConnectionIcon => _connection?.Icon ?? LucideIconKind.Plug;

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

    /// <summary>
    /// Where this pane sits in the arrangement, counting from one.
    /// </summary>
    /// <remarks>
    /// Set by the workspace on every rebuild rather than held by the pane, because a pane's number
    /// is its position and closing pane 2 makes the old pane 3 the new pane 2. A header that kept
    /// its first number would leave the Move or swap menu naming panes that are not where it says.
    /// </remarks>
    public int PaneNumber
    {
        get => _paneNumber;
        internal set
        {
            if (_paneNumber == value) return;
            _paneNumber = value;
            Raise(nameof(PaneNumber));
            Raise(nameof(PaneHeading));
        }
    }

    /// <summary>"Pane 2", or "Pane 2 (Active)" for the one every command acts on.</summary>
    public string PaneHeading => Ui.Format(
        _isActive ? Ui.Shell.PaneActiveFormat : Ui.Shell.PaneNumberFormat, _paneNumber);

    /// <summary>
    /// Whether the connection picker is shown above the listing.
    /// </summary>
    /// <remarks>
    /// Worth hiding once a pane is pointed where it belongs: four panes each keeping a row for a
    /// choice already made is most of a listing's worth of height. This is the same flag a saved
    /// workspace stores as <c>HeaderHidden</c>, so a pane reopens the way it was closed.
    /// </remarks>
    public bool ShowConnectionBar
    {
        get => _showConnectionBar;
        set
        {
            if (_showConnectionBar == value) return;
            _showConnectionBar = value;
            Raise(nameof(ShowConnectionBar));
        }
    }

    /// <summary>
    /// Whether the FILES row of file commands is shown above the listing.
    /// </summary>
    /// <remarks>
    /// New in 2.0: 1.x always showed it. Worth hiding for the same reason as the connection bar,
    /// and nothing is lost by it, since the list's right-click menu offers every command the row and
    /// its "..." do. Saved with the workspace as <c>FilesBarHidden</c>.
    /// </remarks>
    public bool ShowFilesBar
    {
        get => _showFilesBar;
        set
        {
            if (_showFilesBar == value) return;
            _showFilesBar = value;
            Raise(nameof(ShowFilesBar));
        }
    }

    /// <summary>
    /// The pane's own actions, which all need a second pane or the arrangement to act on.
    /// </summary>
    /// <remarks>
    /// Assigned by the workspace for the same reason copy and paste are: splitting a pane changes
    /// the tree the pane is a leaf of, and a leaf is not where that decision belongs.
    /// </remarks>
    public ICommand? SplitRightCommand
    {
        get;
        internal set
        {
            field = value;
            Raise(nameof(SplitRightCommand));
        }
    }

    /// <inheritdoc cref="SplitRightCommand"/>
    public ICommand? SplitBelowCommand
    {
        get;
        internal set
        {
            field = value;
            Raise(nameof(SplitBelowCommand));
        }
    }

    /// <inheritdoc cref="SplitRightCommand"/>
    public ICommand? ClosePaneCommand
    {
        get;
        internal set
        {
            field = value;
            Raise(nameof(ClosePaneCommand));
        }
    }

    /// <summary>The other panes, and the ways this one can move relative to each.</summary>
    public ObservableCollection<PaneMoveTarget> MoveTargets { get; } = [];

    public bool HasMoveTargets => MoveTargets.Count > 0;

    public static string PaneActionsLabel => Ui.Shell.PaneActions;

    public static string SplitRightLabel => Ui.Shell.SplitRight;

    public static string SplitBelowLabel => Ui.Shell.SplitBelow;

    public static string ClosePaneLabel => Ui.Shell.ClosePane;

    public static string ShowConnectionBarLabel => Ui.Shell.ShowConnectionBar;

    public static string ShowFilesBarLabel => Ui.Shell.ShowFilesBar;

    public static string MoveOrSwapLabel => Ui.Shell.MoveOrSwapPane;

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
    public bool HasMorePages => _hasMore;

    /// <summary>
    /// Every row of the folder that has been read, whatever the filter is showing.
    /// </summary>
    /// <remarks>
    /// What a paste checks for name clashes. It used the filtered rows, so a file the filter was
    /// hiding looked absent and a clash with it was not caught before queueing.
    /// </remarks>
    internal IReadOnlyList<BrowserListItem> AllRows =>
        _index?.CreateView(_sortColumn, _sortAscending, null) ?? [];

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
            RaiseConnectionState();
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

    /// <summary>What a screen reader calls the terminal surface.</summary>
    public static string TerminalOutputAccessibleName => Ui.Connections.TerminalOutputAccessibleName;

    public ICommand OpenCommand { get; }

    public ICommand SortByCommand { get; }

    public ICommand SelectAllCommand { get; }

    public ICommand InvertSelectionCommand { get; }

    public ICommand ClearFilterCommand { get; }

    public ICommand NewFolderCommand { get; }

    public ICommand NewFileCommand { get; }

    public ICommand RenameCommand { get; }

    public ICommand DeleteCommand { get; }

    /// <summary>Find and replace across several selected names.</summary>
    public ICommand BatchRenameCommand { get; }

    public static string BatchRenameLabel => Ui.Pane.BatchRename;

    /// <summary>Opens the read-only object inspector on the one selected file.</summary>
    public ICommand PropertiesCommand { get; }

    /// <summary>Opens the one selected file in the external editor.</summary>
    public ICommand EditCommand { get; }

    public static string EditLabel => Ui.Pane.EditInExternalEditor;

    /// <summary>
    /// Whether the selection is one file on a saved connection, which is what can be edited.
    /// </summary>
    /// <remarks>
    /// The inspector's rule exactly, because it is the same address: the agent reads and writes the
    /// file by it. A file on this computer is not offered; it is already somewhere an editor can
    /// open it directly, which is what 1.x said too.
    /// </remarks>
    internal bool CanEdit =>
        _edit is not null &&
        !IsTerminal &&
        PaneTransferSnapshots.SelectionFor(_source, SelectedRows) is { IsSuccess: true } selection &&
        PaneInspection.CanInspect(Here(), selection.Value.Items);

    /// <summary>
    /// Opens the selected file in the external editor, or says why it cannot.
    /// </summary>
    internal async Task EditAsync(CancellationToken cancellationToken = default)
    {
        if (_edit is null) return;

        var selection = PaneTransferSnapshots.SelectionFor(_source, SelectedRows);
        if (selection.IsFailure)
        {
            await RefuseAsync(selection.Error.Message).ConfigureAwait(true);
            return;
        }

        var address = PaneInspection.AddressFor(
            Here(), selection.Value.Items, out var problem, Ui.Shell.ExternalEditRequiresOneFile);
        if (address is null)
        {
            await RefuseAsync(problem!).ConfigureAwait(true);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var item = selection.Value.Items[0];
        await _edit(address, item.Name, item.Length).ConfigureAwait(true);
    }

    /// <summary>
    /// What a drop on this pane does with what landed. Set by the workspace, which owns the queue.
    /// </summary>
    /// <remarks>
    /// A delegate rather than a reference to the workspace, so a pane in a test that is not about
    /// transfers has nothing behind it and refuses drops, the way it refuses a paste.
    /// </remarks>
    internal Func<PaneClipboard, Task>? DropReceiver { get; set; }

    /// <summary>Whether something could be dropped here: a folder is open, and there is a queue.</summary>
    internal bool CanReceiveDrop => DropReceiver is not null && !IsTerminal && _source is not null;

    /// <summary>Hands what was dropped to the workspace, as the paste it amounts to.</summary>
    internal Task ReceiveDropAsync(PaneClipboard clipboard)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        return DropReceiver is { } receive && CanReceiveDrop ? receive(clipboard) : Task.CompletedTask;
    }

    /// <summary>
    /// What a drop of files from the operating system's file manager does. Set by the workspace,
    /// which asks the agent about them first, as 1.x did.
    /// </summary>
    internal Func<IReadOnlyList<string>, string?, Task>? FilesDropReceiver { get; set; }

    /// <summary>Hands files dropped from Explorer, Nautilus or Dolphin to the workspace.</summary>
    /// <param name="folder">The folder in the tree they were dropped on; null for where the pane is.</param>
    internal Task ReceiveFilesAsync(IReadOnlyList<string> paths, string? folder = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return FilesDropReceiver is { } receive && CanReceiveDrop ? receive(paths, folder) : Task.CompletedTask;
    }

    /// <summary>
    /// Readies a drag of this pane's rows for somewhere outside StorageHub. Set by the workspace;
    /// with none, a drag carries nothing out.
    /// </summary>
    internal Func<PaneSelectionSnapshot, ExplorerDrag>? DragOut { get; set; }

    internal ExplorerDrag StartDragOut(PaneSelectionSnapshot selection) =>
        DragOut?.Invoke(selection) ?? ExplorerDrag.Nothing;

    /// <summary>Says what came of a drag out, in the pane's banner, as 1.x's did.</summary>
    internal void ReportDragOut(string? outcome)
    {
        if (!string.IsNullOrEmpty(outcome)) Status = outcome;
    }

    /// <summary>
    /// Where a drop or a file operation that failed is refused. Set by the workspace, which shows
    /// it in the "Transfer queue" warning a refused paste gets, as 1.x's ShowManualTransferFailure
    /// showed a failed new file or folder, rename, batch rename, delete or external edit.
    /// </summary>
    internal Func<string, Task>? Refused { get; set; }

    /// <summary>Says why something asked of this pane failed; on the status line, with no workspace.</summary>
    internal Task RefuseAsync(string reason)
    {
        if (Refused is { } refuse) return refuse(reason);
        Status = reason;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Where what a file operation came to is said. Set by the workspace, which says it in the
    /// status bar's first cell, as 1.x's <c>_locationStatus</c> said a created item, a rename or a
    /// delete.
    /// </summary>
    internal Action<string>? Announce { get; set; }

    /// <summary>Says what a file operation came to; on the status line, with no workspace.</summary>
    private void Say(string news)
    {
        if (Announce is { } announce) announce(news);
        else Status = news;
    }

    /// <summary>
    /// Whether this pane is somewhere things can be made and removed.
    /// </summary>
    /// <remarks>
    /// A terminal has no folder; the connections list is a pane state rather than a location; and
    /// a pane with nothing behind it to ask has nothing to do. All three come out here rather than
    /// as three refusals after the fact.
    /// </remarks>
    internal bool CanMutateHere =>
        _mutations is not null &&
        _dialogs is not null &&
        !IsTerminal &&
        PaneTransferSnapshots.ContextFor(_source) is { IsSuccess: true, Value: var here } &&
        (here.Kind == PaneTransferContextKind.SavedConnection ||
         // This PC's own listing is the drives. Nothing is created there, and a drive is not a
         // folder to rename or delete: deleting a row there asked the filesystem to remove a
         // whole volume's contents, permanently. 1.x refused it too.
         (here.Kind == PaneTransferContextKind.ThisPc && here.RelativePath.Length > 0));

    public static string NewFolderLabel => Ui.Shell.NewFolderTitle;

    public static string NewFileLabel => Ui.Shell.NewEmptyFile;

    public static string RenameLabel => Ui.Shell.RenameWorkspaceAccept;

    public static string DeleteLabel => Ui.Commands.EditDelete;

    public static string PropertiesLabel => Ui.Commands.EditProperties;

    /// <summary>
    /// Whether the selection is one file on a saved connection, which is what can be inspected.
    /// </summary>
    /// <remarks>
    /// The same rules as <see cref="InspectAsync"/> applies, so the button dims where the menu
    /// path would refuse with a sentence.
    /// </remarks>
    internal bool CanInspect =>
        _inspect is not null &&
        !IsTerminal &&
        PaneTransferSnapshots.SelectionFor(_source, SelectedRows) is { IsSuccess: true } selection &&
        PaneInspection.CanInspect(Here(), selection.Value.Items);

    /// <summary>
    /// Opens the inspector on the selected file, or says why it cannot.
    /// </summary>
    /// <remarks>
    /// The rules live in <see cref="PaneInspection"/>: exactly one file, on a saved connection the
    /// agent has identified, with an identity the contract accepts. Each refusal is a sentence in
    /// the status rather than a dialog, because it answers a menu click, not a question.
    /// </remarks>
    internal async Task InspectAsync(CancellationToken cancellationToken = default)
    {
        if (_inspect is null) return;

        var selection = PaneTransferSnapshots.SelectionFor(_source, SelectedRows);
        if (selection.IsFailure)
        {
            Status = selection.Error.Message;
            return;
        }

        var address = PaneInspection.AddressFor(Here(), selection.Value.Items, out var problem);
        if (address is null)
        {
            Status = problem!;
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await _inspect(address).ConfigureAwait(true);
    }

    public ICommand UpCommand { get; }

    public ICommand BackCommand { get; }

    public ICommand ForwardCommand { get; }

    public ICommand RefreshCommand { get; }

    /// <summary>"Load more", in the footer while the folder has more than has been read.</summary>
    public ICommand LoadMoreCommand { get; }

    public static string LoadMoreLabel => Ui.Pane.LoadMore;

    public bool IsLoadingMore => _loadingMore is not null;

    /// <summary>
    /// Reads the next page of this folder and adds it to what is shown.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One page at a time: a second call while one is in flight waits for that one rather than
    /// asking for the same page twice. The view calls this as the list is scrolled near its end,
    /// as 1.x's did, and the footer's Load more calls it by hand.
    /// </para>
    /// <para>
    /// The new rows are merged in where the sort puts them, not by rebuilding the list, which
    /// would throw the scroll position back to the top in the middle of scrolling.
    /// </para>
    /// </remarks>
    /// <returns>Whether a page was added.</returns>
    internal Task<bool> LoadMoreAsync(CancellationToken cancellationToken = default)
    {
        if (_loadingMore is { } running) return running;

        // Not while the pane is on its way somewhere else: the page would be the folder it is
        // leaving, and a connection's browser gives up whatever it was doing for the newer request,
        // so asking would cancel the navigation rather than the page.
        if (!_hasMore || _busy || _source is not { } source) return Task.FromResult(false);

        // Recorded only if it is still running: a page that came back at once has already cleared
        // the marker on its way out, and storing the finished task after that would leave every
        // later call handed the same finished page, forever.
        var page = LoadNextPageAsync(source, cancellationToken);
        if (!page.IsCompleted)
        {
            _loadingMore = page;
            Raise(nameof(IsLoadingMore));
            Raise(nameof(ItemCount));
            (LoadMoreCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        return page;
    }

    /// <summary>
    /// Re-reads the folder without covering the list, keeping the filter, the selection and where
    /// the list is scrolled to -- what the shell does every few seconds while transfers run, so
    /// files appear as they land, as they did in 1.x.
    /// </summary>
    /// <remarks>
    /// Skipped while the pane is busy with anything else, and a failure changes nothing: the next
    /// tick tries again, and a banner every five seconds for a flaky connection would be noise.
    /// </remarks>
    internal async Task RefreshQuietlyAsync(CancellationToken cancellationToken = default)
    {
        if (_source is not { } source || IsTerminal || _busy || _loadingMore is not null) return;

        // Not a navigation of its own: the same folder read again, which anything that moves the
        // pane meanwhile makes stale.
        var navigation = _navigation;
        var result = await source.MoveAsync(PaneNavigationKind.Refresh, cancellationToken: cancellationToken)
            .ConfigureAwait(true);
        if (navigation != _navigation || _busy || result.Listing is not { } listing) return;

        _hasMore = listing.HasMore;
        Index.Reset(listing.Rows);
        SyncView();
        FollowTree(listing.Rows, append: false);
        Raise(nameof(HasMorePages));
    }

    /// <summary>
    /// Reads every remaining page, which a paste or a drop needs: the conflict check has to see the
    /// whole destination, and 1.x read it all before building one.
    /// </summary>
    /// <returns>Whether the folder is now fully read.</returns>
    internal async Task<bool> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        while (_hasMore)
        {
            var before = AllRows.Count;
            if (!await LoadMoreAsync(cancellationToken).ConfigureAwait(true)) return !_hasMore;

            // A page that added nothing is a provider going round in circles; stop rather than
            // spin, and leave the folder marked as not fully read.
            if (AllRows.Count == before) return false;
        }

        return true;
    }

    private async Task<bool> LoadNextPageAsync(IPaneSource source, CancellationToken cancellationToken)
    {
        var navigation = _navigation;
        try
        {
            var result = await source.LoadMoreAsync(cancellationToken).ConfigureAwait(true);

            // The pane moved on while the page was in flight; the page belongs to a folder that is
            // no longer showing, even when it is the same connection's.
            if (navigation != _navigation) return false;

            if (result.Listing is not { } listing)
            {
                if (!string.IsNullOrEmpty(result.Error)) Status = result.Error;
                return false;
            }

            _hasMore = listing.HasMore;
            Index.Append(listing.Rows);
            SyncView();
            FollowTree(listing.Rows, append: true);
            return true;
        }
        finally
        {
            _loadingMore = null;
            Raise(nameof(IsLoadingMore));
            Raise(nameof(HasMorePages));
            Raise(nameof(ItemCount));
            (LoadMoreCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Brings the list in line with the index without rebuilding it: rows that went are removed,
    /// rows that came are inserted where the sort puts them, and the rest stay where they are.
    /// </summary>
    /// <remarks>
    /// Rebuilding clears the list, which throws the scroll position back to the top -- fine for a
    /// new folder, wrong for a further page arriving while somebody scrolls, and wrong for the quiet
    /// re-read during a transfer. A row that changed (a size, a date) is a different row and is
    /// replaced in place. What was selected stays selected, by location.
    /// </remarks>
    private void SyncView()
    {
        var chosen = SelectedRows
            .Where(static row => row.Location is not null)
            .Select(static row => row.Location!)
            .ToHashSet(StringComparer.Ordinal);

        var target = new List<BrowserListItem>();
        if (!_isAtRoot) target.Add(BrowserParentNavigation.Item);
        target.AddRange(Index.CreateView(_sortColumn, _sortAscending, NullIfEmpty(_filter)));
        var wanted = target.ToHashSet();

        for (var index = Rows.Count - 1; index >= 0; index--)
        {
            if (!wanted.Contains(Rows[index])) Rows.RemoveAt(index);
        }

        for (var index = 0; index < target.Count; index++)
        {
            if (index < Rows.Count && Rows[index] == target[index]) continue;
            var existing = Rows.IndexOf(target[index]);
            if (existing > index) Rows.Move(existing, index);
            else Rows.Insert(index, target[index]);
        }

        while (Rows.Count > target.Count) Rows.RemoveAt(Rows.Count - 1);

        foreach (var row in Rows)
        {
            if (row.Location is not null && chosen.Contains(row.Location) && !SelectedRows.Contains(row))
            {
                SelectedRows.Add(row);
            }
        }

        Raise(nameof(ItemCount));
        RaiseListingState();
        RaiseCommands();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Reads the connections this pane can be pointed at.
    /// </summary>
    /// <remarks>
    /// Storage connections and SSH clients only, which is the same filter the sidebar and the
    /// connection picker use: the others cannot be browsed, and listing them produces a pane that
    /// fails the moment somebody chooses one.
    /// </remarks>
    internal Task LoadConnectionsAsync(CancellationToken cancellationToken = default) =>
        _connectionsLoad = ReadConnectionsAsync(cancellationToken);

    private async Task ReadConnectionsAsync(CancellationToken cancellationToken)
    {
        // Not a navigation of its own, but the pane can be pointed somewhere, or closed, while the
        // list is read, and 1.x's list checked _uiNavigationSequence too. The list still fills the
        // picker then, but the loading cover and the status line are the newer navigation's, and
        // nothing is opened: not This PC over a connection, and not anything in a closed pane.
        var navigation = _navigation;
        IsBusy = true;
        Status = Ui.Pane.LoadingConnections;
        try
        {
            var result = await _controller.LoadConnectionsAsync(cancellationToken).ConfigureAwait(true);
            Connections.Clear();
            List<ConnectionCardModel> cards = [ConnectionPickerFilter.ThisComputer()];
            _cards = cards;

            // This PC first, always, and whether or not the agent answered. Most transfers have one
            // local end, and a pane that cannot reach the agent can still browse this computer -
            // which is also the state somebody is in while they work out why the agent is down.
            Connections.Add(new PaneConnection(
                null, Ui.Pane.ThisPc, LucideIconKind.HardDrive, PaneContentKind.ThisPc));
            cards.Add(_homeCard);
            Connections.Add(new PaneConnection(
                null, Ui.Pane.ConnectionsHome, LucideIconKind.House, PaneContentKind.ConnectionsHome));

            if (result.Status != RemoteBrowserOperationStatus.Succeeded)
            {
                if (navigation == _navigation) Status = result.ErrorMessage ?? Ui.Pane.Disconnected;
                return;
            }

            foreach (var connection in result.Connections.Where(CanBrowse))
            {
                cards.Add(ConnectionCardFactory.Create(connection));
                Connections.Add(new PaneConnection(
                    connection.ConnectionId,
                    connection.DisplayName,
                    Themes.IconCatalog.Resolve(
                        ConnectionIconCatalog.ResolveForConnection(
                            connection.IconKey, MapProvider(connection.Provider), connection.Type))
                        ?? LucideIconKind.Cloud,
                    KindOf(connection)));
            }

            if (navigation == _navigation) Status = string.Empty;
        }
        finally
        {
            var current = navigation == _navigation;
            if (current) IsBusy = false;

            // A pane that has not been pointed anywhere opens on This PC, as 1.x's did, rather
            // than on an empty "/" waiting to be told. Whether or not the agent answered: this
            // computer can be browsed either way. Not while a saved workspace is restoring it,
            // which knows better where it goes.
            if (current && _connection is null && !_restoring && Connections.Count > 0)
            {
                // The first pane on this computer and the rest on the saved connections, as 1.x
                // opened a workspace.
                Connection = _paneNumber > 1 &&
                    Connections.FirstOrDefault(static candidate => candidate.Kind == PaneContentKind.ConnectionsHome) is { } home
                        ? home
                        : Connections[0];
            }
            else if (_connection is { Name.Length: 0, Id: { } opened } &&
                     Connections.FirstOrDefault(candidate => candidate.Id == opened) is { } named)
            {
                // Opened by id before the list arrived; now it has a name and an icon to show.
                _connection = named;
                Raise(nameof(Connection));
                Raise(nameof(Title));
                Raise(nameof(ConnectionIcon));
                RaiseConnectionState();
            }
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
        var navigation = ++_navigation;
        _standingBy = false;
        IsBusy = true;
        try
        {
            if (_source is LocalPaneSource)
            {
                await _source.DisposeAsync().ConfigureAwait(true);
            }

            // Whatever this pane is about to show, it is no longer showing the old shell. Pointing
            // a terminal pane at a bucket would otherwise leave the session open and unreachable,
            // holding two pipes and a keep-alive for a window nobody can see.
            await CloseTerminalAsync().ConfigureAwait(true);

            // Chosen again while the shell closed: the later choice has the pane.
            if (navigation != _navigation) return;

            ContentKind = choice.Kind;

            // An SSH client has no listing to fetch. It keeps the pane's chrome -- the picker, the
            // title, the active border -- and replaces only the body, so switching a pane to a
            // shell and back is the same gesture as switching between two buckets.
            if (choice.Kind == PaneContentKind.SshClient)
            {
                _source = null;
                Tree.Clear();
                Rows.Clear();
                SelectedRows.Clear();
                Path = choice.Name;

                // Nothing in the status strip yet: the session puts its own state there as soon as
                // it has one, and a pane that says it twice reads as two problems.
                Status = string.Empty;
                await OpenTerminalAsync(choice, cancellationToken).ConfigureAwait(true);
                RaiseCommands();
                return;
            }

            if (choice.Kind == PaneContentKind.ConnectionsHome)
            {
                _source = new ConnectionsHomeSource(() => _cards);
                Report(navigation, await _source.MoveAsync(PaneNavigationKind.Navigate, cancellationToken: cancellationToken)
                    .ConfigureAwait(true));
                return;
            }

            if (choice.Id is { } connectionId)
            {
                var remote = new RemotePaneSource(_controller, choice.Name);
                _source = remote;
                var opened = await remote.OpenAsync(connectionId, cancellationToken).ConfigureAwait(true);

                // Left for something else before it answered: whatever the pane shows now is not
                // this connection's to clear.
                if (navigation != _navigation) return;

                // A connection that would not open shows nothing, not the previous connection's
                // files under this one's name -- rows nothing could act on, since they belong to
                // a source this pane no longer holds.
                if (opened.Listing is null)
                {
                    Tree.Clear();
                    Rows.Clear();
                    SelectedRows.Clear();
                    Index.Reset([]);
                    _hasMore = false;
                    _isAtRoot = true;
                    Path = "/";
                    Raise(nameof(ItemCount));
                    Raise(nameof(HasMorePages));
                }

                Report(navigation, opened);
                return;
            }

            _source = new LocalPaneSource(new LocalBrowserController(_localSource()));
            Report(navigation, await _source
                .MoveAsync(PaneNavigationKind.Navigate, null, cancellationToken)
                .ConfigureAwait(true));
        }
        finally
        {
            EndNavigation(navigation);
        }
    }

    /// <summary>
    /// A picker over this pane's connections, with what it has open marked.
    /// </summary>
    /// <remarks>
    /// Made fresh for each opening, so the search box starts empty and the highlight starts on the
    /// connection in use, which is what the WinForms popup did by being rebuilt every time.
    /// </remarks>
    internal ConnectionPickerModel CreatePicker()
    {
        var picker = new ConnectionPickerModel(_cards, ActiveCard);
        picker.Chosen += (_, card) => Choose(card);
        return picker;
    }

    /// <summary>The card for what this pane has open, or null when it has nothing open yet.</summary>
    internal ConnectionCardModel? ActiveCard => _connection switch
    {
        null => null,
        { Kind: PaneContentKind.ConnectionsHome } => _homeCard,
        { Id: { } id } => _cards.FirstOrDefault(card => card.ConnectionId == id),
        _ => _cards.FirstOrDefault(card => card.ConnectionId is null)
    };

    /// <summary>
    /// Opens whatever a card stands for.
    /// </summary>
    /// <remarks>
    /// Through <see cref="Connection"/>, which is what opens a connection however it was chosen.
    /// A card with no id is this computer, which is the first entry in the list.
    /// </remarks>
    internal void Choose(ConnectionCardModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var choice = card.ConnectionId is { } id
            ? Connections.FirstOrDefault(candidate => candidate.Id == id)
            : ReferenceEquals(card, _homeCard)
                ? Connections.FirstOrDefault(static candidate => candidate.Kind == PaneContentKind.ConnectionsHome)
                : Connections.FirstOrDefault(static candidate => candidate.Kind == PaneContentKind.ThisPc);
        if (choice is not null) Connection = choice;
    }

    /// <summary>Opens a saved connection by id, for a caller that has one rather than a choice.</summary>
    internal Task OpenConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        var choice = Connections.FirstOrDefault(candidate => candidate.Id == connectionId)
            ?? new PaneConnection(connectionId, string.Empty, LucideIconKind.Cloud);
        return PointAtAsync(choice, cancellationToken);
    }

    /// <summary>
    /// Records a choice as the pane's and opens it, for a caller that has to wait for the listing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Recorded as the pane's choice, not only opened: the chip names what is showing, and a pane
    /// with no choice recorded is one that falls back to This PC once its list arrives. Setting
    /// <see cref="Connection"/> would open it too, but without anything to await.
    /// </para>
    /// <para>
    /// A saved connection is opened from the list the pane reads, so one chosen while that list
    /// is still on its way -- "Open in new pane", or a connection opened from Welcome into a new
    /// workspace -- waits for it, as 1.x's <c>RestoreStateAsync</c> did. Opened at once, it failed
    /// as no longer saved.
    /// </para>
    /// </remarks>
    private async Task PointAtAsync(PaneConnection choice, CancellationToken cancellationToken)
    {
        _connection = choice;
        _failed = false;
        Raise(nameof(Connection));
        Raise(nameof(Title));
        Raise(nameof(ConnectionIcon));
        RaiseConnectionState();

        if (choice.Id is { } id && _connectionsLoad is { IsCompleted: false } load)
        {
            var navigation = _navigation;
            await load.ConfigureAwait(true);

            // Pointed somewhere else, or closed, while the list was read: that has the pane.
            if (navigation != _navigation || _connection?.Id != id) return;

            // The list has named it by now, if it holds it.
            choice = _connection;
        }

        await OpenAsync(choice, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// What a saved workspace stores about this pane: what it shows, where, and how it is ordered
    /// and filtered.
    /// </summary>
    /// <remarks>
    /// The same record, member for member, 1.x wrote into a <c>.shw</c>, so a workspace saved by
    /// either opens in the other. The folder is the one a transfer would use -- a full path on this
    /// computer, a relative one on a connection -- and nothing at the top, where there is none.
    /// </remarks>
    internal BrowserPaneState CaptureState()
    {
        var kind = _connection?.Kind ?? PaneContentKind.Unresolved;
        var inFolder = kind == PaneContentKind.ThisPc
            ? _source is LocalPaneSource
            : kind == PaneContentKind.SavedStorage && _source is RemotePaneSource;
        var folder = inFolder && PaneTransferSnapshots.ContextFor(_source) is { IsSuccess: true } here &&
            here.Value.RelativePath.Length > 0
                ? here.Value.RelativePath
                : null;
        return new BrowserPaneState(
            kind,
            _connection?.Id,
            string.IsNullOrEmpty(_connection?.Name) ? null : _connection.Name,
            folder,
            _filter,
            _sortColumn,
            _sortAscending,
            HeaderHidden: !_showConnectionBar,
            FilesBarHidden: !_showFilesBar);
    }

    /// <summary>
    /// Puts the pane back the way a saved workspace describes it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// As 1.x's <c>RestoreStateAsync</c> did: the order, the filter and the bars first, then what the
    /// pane was pointed at and the folder it was in. A connection that no longer exists leaves the
    /// pane on Connections Home saying so, rather than failing the whole workspace over one pane.
    /// </para>
    /// <para>
    /// With "reconnect remote panes" off, a connection is chosen but not opened: the banner says so
    /// and a click on it connects, which is where 1.x put that choice.
    /// </para>
    /// </remarks>
    internal async Task RestoreAsync(
        BrowserPaneState state,
        bool reconnectRemote,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        _sortColumn = Enum.IsDefined(state.SortColumn) ? state.SortColumn : BrowserSortColumn.Name;
        _sortAscending = state.SortAscending;
        _filter = state.Filter ?? string.Empty;
        Raise(nameof(SortColumn));
        Raise(nameof(SortAscending));
        Raise(nameof(Filter));
        Raise(nameof(HasFilter));

        // The arrows too, which a pane left standing by would otherwise show on Name until it
        // connected and listed.
        RaiseHeaders();
        ShowConnectionBar = !state.HeaderHidden;
        ShowFilesBar = !state.FilesBarHidden;

        _restoring = true;
        try
        {
            await (_connectionsLoad ?? LoadConnectionsAsync(cancellationToken)).ConfigureAwait(true);
        }
        finally
        {
            _restoring = false;
        }

        // Closed while the list was read: nothing is opened for a pane that is going away.
        cancellationToken.ThrowIfCancellationRequested();

        var home = Connections.FirstOrDefault(static candidate => candidate.Kind == PaneContentKind.ConnectionsHome)
            ?? new PaneConnection(null, Ui.Pane.ConnectionsHome, LucideIconKind.House, PaneContentKind.ConnectionsHome);
        if (state.ContentKind == PaneContentKind.ThisPc)
        {
            await PointAtAsync(
                Connections.FirstOrDefault(static candidate => candidate.Kind == PaneContentKind.ThisPc)
                    ?? new PaneConnection(null, Ui.Pane.ThisPc, LucideIconKind.HardDrive, PaneContentKind.ThisPc),
                cancellationToken).ConfigureAwait(true);

            // A folder saved on another kind of computer -- C:\ on Linux, /home on Windows -- is not
            // one here, and the pane stays on This PC, as 1.x's did for a folder it could not read.
            if (!string.IsNullOrWhiteSpace(state.FolderPath) && System.IO.Path.IsPathFullyQualified(state.FolderPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await NavigateAsync(state.FolderPath, cancellationToken).ConfigureAwait(true);
            }

            return;
        }

        if (state.ContentKind == PaneContentKind.ConnectionsHome || state.ProfileId is not { } id)
        {
            await PointAtAsync(home, cancellationToken).ConfigureAwait(true);
            return;
        }

        if (Connections.FirstOrDefault(candidate => candidate.Id == id) is not { } choice)
        {
            await PointAtAsync(home, cancellationToken).ConfigureAwait(true);
            Status = Ui.Format(Ui.Pane.SavedProfileUnavailableFormat, state.DisplayNameHint ?? id.ToString("D"));
            _failed = true;
            RaiseConnectionState();
            RaiseListingState();
            return;
        }

        if (!reconnectRemote)
        {
            await StandByAsync(choice).ConfigureAwait(true);
            return;
        }

        await PointAtAsync(choice, cancellationToken).ConfigureAwait(true);
        if (choice.Kind == PaneContentKind.SavedStorage && _source is RemotePaneSource &&
            !string.IsNullOrEmpty(state.FolderPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await NavigateAsync(state.FolderPath, cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Chooses a connection without opening it, and says that a click on the banner will.
    /// </summary>
    /// <remarks>
    /// Whatever the pane showed before is let go, so the banner's retry -- which opens the choice
    /// when there is no source -- is what connects.
    /// </remarks>
    private async Task StandByAsync(PaneConnection choice)
    {
        // Whatever was on its way -- This PC, opened when the list arrived -- is for a pane that
        // is not being shown, and nothing is loading now.
        _navigation++;
        IsBusy = false;
        await CloseTerminalAsync().ConfigureAwait(true);
        if (_source is LocalPaneSource local) await local.DisposeAsync().ConfigureAwait(true);
        _source = null;
        Tree.Clear();
        Rows.Clear();
        SelectedRows.Clear();
        Path = "/";
        _connection = choice;
        Raise(nameof(Connection));
        Raise(nameof(Title));
        Raise(nameof(ConnectionIcon));
        Status = Ui.Pane.AutoReconnectDisabled;
        _failed = false;
        _standingBy = true;
        RaiseConnectionState();
        RaiseListingState();
        RaiseCommands();
    }

    /// <summary>
    /// Asks for a name and makes an empty folder or file in this pane's folder.
    /// </summary>
    /// <remarks>
    /// The listing is reloaded rather than having the new item added to it, because what a
    /// provider actually stored is its answer: a name can come back normalised, and a folder on a
    /// bucket may not exist as a listable thing at all until something is put in it.
    /// </remarks>
    internal async Task CreateAsync(bool container, CancellationToken cancellationToken = default)
    {
        if (!CanMutateHere || Here() is not { } location) return;

        var name = await _dialogs!.PromptAsync(
            new DialogPromptRequest
            {
                Title = container ? Ui.Shell.NewFolderTitle : Ui.Shell.NewEmptyFile,
                Label = container ? Ui.Shell.FolderName : Ui.Shell.FileName,
                Value = container ? string.Empty : Ui.Shell.DefaultFileName,
                Accept = Ui.Shell.Create,
                Validate = PaneItemNameRules.Validate
            },
            cancellationToken).ConfigureAwait(true);
        if (string.IsNullOrEmpty(name)) return;

        await using var mutations = _mutations!();
        var result = container
            ? await mutations.CreateFolderAsync(location, name, cancellationToken).ConfigureAwait(true)
            : await mutations.CreateFileAsync(location, name, cancellationToken).ConfigureAwait(true);

        if (result.IsFailure)
        {
            await RefuseAsync(Ui.Format(Ui.Shell.ItemCreateFailedFormat, result.Error.Message)).ConfigureAwait(true);
            return;
        }

        await MoveAsync(PaneNavigationKind.Refresh, cancellationToken: cancellationToken)
            .ConfigureAwait(true);
        Say(Ui.Format(container ? Ui.Shell.CreatedFolderFormat : Ui.Shell.CreatedFileFormat, name));
    }

    /// <summary>Asks for a new name for the one selected item, and applies it.</summary>
    internal async Task RenameAsync(CancellationToken cancellationToken = default)
    {
        if (!CanMutateHere || Here() is not { } location) return;
        if (Chosen() is not [var row])
        {
            await RefuseAsync(Ui.Shell.SelectOneToRename).ConfigureAwait(true);
            return;
        }

        var item = PaneTransferSnapshots.ItemFor(row);
        if (item.IsFailure)
        {
            await RefuseAsync(item.Error.Message).ConfigureAwait(true);
            return;
        }

        var name = await _dialogs!.PromptAsync(
            new DialogPromptRequest
            {
                Title = Ui.Shell.RenameItem,
                Label = Ui.Shell.NewNameLabel,
                Value = row.Name,
                Accept = Ui.Shell.RenameWorkspaceAccept,
                Validate = PaneItemNameRules.Validate
            },
            cancellationToken).ConfigureAwait(true);
        if (string.IsNullOrEmpty(name)) return;

        await using var mutations = _mutations!();
        var result = await mutations
            .RenameAsync(location, item.Value, name, cancellationToken)
            .ConfigureAwait(true);

        if (result.IsFailure)
        {
            await RefuseAsync(Ui.Format(Ui.Shell.ItemRenameFailedFormat, result.Error.Message)).ConfigureAwait(true);
            return;
        }

        await MoveAsync(PaneNavigationKind.Refresh, cancellationToken: cancellationToken)
            .ConfigureAwait(true);
        Say(Ui.Format(Ui.Shell.RenamedOneFormat, row.Name, name));
    }

    /// <summary>
    /// Renames several selected items with one find-and-replace, after showing every result.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One at a time, stopping at the first refusal and saying how many went through, as 1.x did:
    /// a provider has no transaction across renames, so a batch that failed half-way is described
    /// rather than pretended away.
    /// </para>
    /// <para>
    /// The preview checks new names against what the pane has loaded. 1.x loaded every page first;
    /// this pane cannot yet fetch more pages than the first, so a name taken on a page it has not
    /// loaded is caught by the rename itself, which the provider refuses, and the batch stops there.
    /// </para>
    /// </remarks>
    internal async Task BatchRenameAsync(CancellationToken cancellationToken = default)
    {
        if (_batchRename is null || !CanMutateHere || Here() is not { } location) return;

        var rows = Chosen();
        if (rows.Count < 2)
        {
            await RefuseAsync(Ui.Shell.SelectTwoToBatchRename).ConfigureAwait(true);
            return;
        }

        var items = new List<PaneTransferItem>(rows.Count);
        foreach (var row in rows)
        {
            var item = PaneTransferSnapshots.ItemFor(row);
            if (item.IsFailure)
            {
                await RefuseAsync(item.Error.Message).ConfigureAwait(true);
                return;
            }

            items.Add(item.Value);
        }

        var occupied = Rows.Where(static row => !row.IsParentNavigation).Select(static row => row.Name).ToArray();
        var lines = await _batchRename([.. items.Select(static item => item.Name)], occupied).ConfigureAwait(true);
        if (lines is null || lines.Count != items.Count) return;

        var renamed = 0;
        await using (var mutations = _mutations!())
        {
            for (var index = 0; index < items.Count; index++)
            {
                if (!lines[index].Changes) continue;

                var result = await mutations
                    .RenameAsync(location, items[index], lines[index].Target, cancellationToken)
                    .ConfigureAwait(true);
                if (result.IsFailure)
                {
                    await MoveAsync(PaneNavigationKind.Refresh, cancellationToken: cancellationToken).ConfigureAwait(true);
                    await RefuseAsync(Ui.Format(
                        Ui.Shell.RenamedThenStoppedFormat, renamed, items[index].Name, result.Error.Message)).ConfigureAwait(true);
                    return;
                }

                renamed++;
            }
        }

        await MoveAsync(PaneNavigationKind.Refresh, cancellationToken: cancellationToken).ConfigureAwait(true);
        Say(Ui.Format(Ui.Shell.RenamedItemsFormat, renamed));
    }

    /// <summary>
    /// Confirms, then removes what is selected.
    /// </summary>
    /// <remarks>
    /// The confirmation says the deletion cannot be undone, because in 2.0 it cannot: the WinForms
    /// shell sent local deletions to the Recycle Bin through a Windows-only API and there is no
    /// cross-platform equivalent, so the honest thing is to say so rather than to offer a recovery
    /// that exists on one platform.
    /// </remarks>
    internal async Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        if (!CanMutateHere || Here() is not { } location) return;

        var selection = PaneTransferSnapshots.SelectionFor(_source, SelectedRows);
        if (selection.IsFailure)
        {
            await RefuseAsync(selection.Error.Message).ConfigureAwait(true);
            return;
        }

        var items = selection.Value.Items;
        if (items.Count == 0) return;

        await using var mutations = _mutations!();
        var local = location.Kind == PaneTransferContextKind.ThisPc;

        // 1.x's review: what is about to go, up to six by name, and where it goes -- the Recycle
        // Bin or Trash for this computer, gone for good on a connection. Skipped when somebody
        // has said not to ask; the setting was saved and never read.
        if (DeleteConfirmation?.Invoke() != false)
        {
            var preview = string.Join(
                Environment.NewLine,
                items.Take(6).Select(static item => Ui.Format(Ui.Dialogs.DeletePreviewItemFormat, item.Name)));
            if (items.Count > 6)
            {
                preview += Environment.NewLine + Ui.Format(Ui.Dialogs.DeletePreviewMoreFormat, items.Count - 6);
            }

            var choice = await _dialogs!.ConfirmAsync(
                new DialogRequest
                {
                    Title = Ui.Dialogs.ReviewDeleteCaption,
                    Message = Ui.Format(Ui.Dialogs.DeleteItemsPromptFormat, items.Count),
                    Detail = preview + Environment.NewLine + Environment.NewLine +
                        (local && mutations.RecyclesLocalDeletes
                            ? Ui.Dialogs.DeleteLocalToRecycleBin
                            : local ? Ui.Shell.DeleteItemsDetail : Ui.Dialogs.DeleteRemotePermanent),
                    Severity = DialogSeverity.Warning,
                    Buttons = DialogButtons.YesNo,
                    Default = DialogChoice.No,
                    CheckBoxLabel = StopDeleteConfirmation is null ? null : Ui.Dialogs.DontShowWarningAgain,
                    CheckBoxAnswered = ticked =>
                    {
                        if (ticked) StopDeleteConfirmation?.Invoke();
                    }
                },
                cancellationToken).ConfigureAwait(true);
            if (choice != DialogChoice.Yes) return;
        }

        var outcome = await mutations
            .DeleteAsync(location, items, cancellationToken)
            .ConfigureAwait(true);

        await MoveAsync(PaneNavigationKind.Refresh, cancellationToken: cancellationToken)
            .ConfigureAwait(true);

        // How far it got matters as much as whether it finished: "three of five were deleted, then
        // this happened" is a different situation from "nothing was deleted". A failure is the
        // "Transfer queue" warning, as it was in 1.x.
        if (outcome.IsSuccess)
        {
            Say(Ui.Format(
                outcome.Recycled ? Ui.Shell.SentToRecycleBinFormat : Ui.Shell.DeletedItemsFormat, outcome.Deleted));
            return;
        }

        await RefuseAsync(outcome.Deleted == 0
            ? Ui.Format(Ui.Shell.ItemsDeleteFailedFormat, outcome.Failure!.Message)
            : Ui.Format(Ui.Shell.DeletedThenStoppedFormat, outcome.Deleted, outcome.Failure!.Message)).ConfigureAwait(true);
    }

    /// <summary>Where this pane is, as a location a mutation can act on.</summary>
    private PaneTransferContext? Here() =>
        PaneTransferSnapshots.ContextFor(_source) is { IsSuccess: true } context ? context.Value : null;

    /// <summary>The selected rows a file operation can act on: never the way back out.</summary>
    private IReadOnlyList<BrowserListItem> Chosen() =>
        [.. SelectedRows.Where(static row => !row.IsParentNavigation)];

    /// <summary>Goes to a path, or says why it could not.</summary>
    internal async Task NavigateAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        if (_source is null) return;

        var navigation = ++_navigation;
        IsBusy = true;
        try
        {
            Report(navigation, await _source
                .MoveAsync(PaneNavigationKind.Navigate, relativePath, cancellationToken)
                .ConfigureAwait(true));
        }
        finally
        {
            EndNavigation(navigation);
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
        // A file opens in the external editor, as a double-click did in 1.x; a folder is entered.
        if (Selected is { IsContainer: false, IsParentNavigation: false })
        {
            return CanEdit ? EditAsync(cancellationToken) : Task.CompletedTask;
        }

        if (Selected is not { IsContainer: true } row) return Task.CompletedTask;

        // A row of Connections Home is a connection, not a folder: it opens in this pane.
        if (ConnectionsHomeSource.ConnectionOf(row) is { } connection) return OpenConnectionAsync(connection, cancellationToken);

        return row.IsParentNavigation
            ? UpAsync(cancellationToken)
            : NavigateAsync(row.Location ?? row.Name, cancellationToken);
    }

    /// <summary>Reads the current folder again.</summary>
    internal Task RefreshAsync(CancellationToken cancellationToken = default) =>
        MoveAsync(PaneNavigationKind.Refresh, cancellationToken);

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

        var navigation = ++_navigation;
        IsBusy = true;
        try
        {
            Report(navigation, await _source
                .MoveAsync(kind, cancellationToken: cancellationToken)
                .ConfigureAwait(true));
        }
        finally
        {
            EndNavigation(navigation);
        }
    }

    /// <summary>
    /// The shell this pane is showing, or nothing if it is showing files.
    /// </summary>
    /// <remarks>
    /// Handed straight to the control that draws it. The pane owns its lifetime because the pane is
    /// what gets closed, re-pointed or swapped out from under it.
    /// </remarks>
    public ITerminalSession? Terminal
    {
        get => _terminal;
        private set
        {
            if (ReferenceEquals(_terminal, value)) return;
            _terminal = value as SshTerminalSession;
            Raise(nameof(Terminal));
            Raise(nameof(HasTerminal));
            Raise(nameof(TerminalFontFamily));
            Raise(nameof(TerminalFontSize));
            Raise(nameof(TerminalRenderBoldText));
        }
    }

    /// <summary>Whether there is a live session, as opposed to a pane waiting to be given one.</summary>
    public bool HasTerminal => _terminal is not null;

    private SshTerminalPreferences TerminalLook => _terminal?.Preferences ?? SshTerminalPreferences.Defaults;

    /// <summary>The family chosen under Settings, by name; the painter puts the fallbacks behind it.</summary>
    public string TerminalFontFamily => TerminalLook.FontFamily;

    /// <summary>
    /// The size chosen under Settings, which counts in points as 1.4's did, in the pixels
    /// Avalonia draws with: a 10-point terminal is a 13.3-pixel one.
    /// </summary>
    public double TerminalFontSize => TerminalLook.FontSize * 96.0 / 72.0;

    /// <summary>Whether bold text is drawn in a bold face, or only brighter.</summary>
    public bool TerminalRenderBoldText => TerminalLook.RenderBoldText;

    /// <summary>
    /// Opens a shell on the connection this pane was just pointed at.
    /// </summary>
    /// <remarks>
    /// The grid is left at the session's default until the control has been arranged and can say
    /// how large it really is; the first arrange sends the true size. Opening at a guess and
    /// correcting is what every terminal does -- the alternative is waiting for a layout pass
    /// before connecting, which shows an empty pane for no gain.
    /// </remarks>
    private async Task OpenTerminalAsync(PaneConnection choice, CancellationToken cancellationToken)
    {
        if (_terminals is null || choice.Id is not { } connectionId) return;

        var session = new SshTerminalSession(
            connectionId,
            _terminals(),
            TerminalPreferences?.Invoke(),
            _agentLifecycle?.Invoke(),
            ownsClient: true);
        session.StatusChanged += OnTerminalStatusChanged;
        Terminal = session;
        Status = session.Status;
        await session.OpenAsync(80, 24, cancellationToken).ConfigureAwait(true);
    }

    private void OnTerminalStatusChanged(object? sender, EventArgs e)
    {
        if (sender is SshTerminalSession session && ReferenceEquals(session, _terminal))
        {
            Status = session.Status;
        }
    }

    /// <summary>Ends the session this pane was showing, if it was showing one.</summary>
    private async Task CloseTerminalAsync()
    {
        if (_terminal is not { } session) return;
        session.StatusChanged -= OnTerminalStatusChanged;
        Terminal = null;
        await session.DisposeAsync().ConfigureAwait(true);
    }

    public async ValueTask DisposeAsync()
    {
        // A listing still on its way lands nowhere, and a connection list still being read opens
        // nothing: either would otherwise build a new index, and its SQLite file, for a pane that
        // is gone.
        _navigation++;
        await CloseTerminalAsync().ConfigureAwait(false);
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
    /// Shows a navigation result, whatever it turned out to be, if the pane is still on the
    /// navigation that asked for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A failed navigation leaves the rows alone. The old pane cleared them, which meant a typo in
    /// the address bar lost the listing somebody was looking at and cost another round trip to get
    /// back to it.
    /// </para>
    /// <para>
    /// A result for a navigation the pane has left says nothing, not even that it failed: a
    /// browser answers a navigation it gave up on as superseded, which reads here as a failure and
    /// would put "Disconnected" over the listing that replaced it.
    /// </para>
    /// </remarks>
    private void Report(long navigation, PaneNavigationResult result)
    {
        if (navigation != _navigation) return;

        if (result.Listing is not { } listing)
        {
            Status = result.Error ?? Ui.Pane.Disconnected;
            _failed = true;
            RaiseConnectionState();
            RaiseListingState();
            RaiseCommands();
            return;
        }

        _failed = false;
        RaiseConnectionState();

        Path = listing.DisplayPath;
        _hasMore = listing.HasMore;
        Raise(nameof(HasMorePages));
        (LoadMoreCommand as RelayCommand)?.RaiseCanExecuteChanged();
        _isAtRoot = listing.IsAtRoot;
        _note = listing.Note;

        // The filter stays from folder to folder, as it did in 1.x: "*.pdf" is usually a question
        // about a tree, not one folder. It used to be cleared here, because a folder it emptied
        // looked empty with the reason two controls away; the list now says "No items match the
        // current filter" where the rows would be.

        Index.Reset(listing.Rows);
        ApplyView();
        Raise(nameof(Title));
        FollowTree(listing.Rows, append: false);
    }

    /// <summary>
    /// Takes "Fetching folder" off the list, if this is still the navigation the pane is on.
    /// </summary>
    /// <remarks>
    /// Only that one: an earlier navigation finishing late would uncover a list that is still on
    /// its way, as 1.x's pane only let the latest navigation clear its busy state.
    /// </remarks>
    private void EndNavigation(long navigation)
    {
        if (navigation == _navigation) IsBusy = false;
    }

    /// <summary>
    /// Puts the listing into the tree: where the pane is, opened, with the folders it holds.
    /// </summary>
    private void FollowTree(IReadOnlyList<BrowserListItem> rows, bool append)
    {
        // Connections Home is one node, "Connections", as 1.x's tree showed it.
        if (_source is ConnectionsHomeSource)
        {
            Tree.ShowConnections();
            return;
        }

        if (_source is null || IsTerminal ||
            PaneTransferSnapshots.ContextFor(_source) is not { IsSuccess: true, Value: var here })
        {
            Tree.Clear();
            return;
        }

        var local = _source is LocalPaneSource;
        Tree.Follow(
            local ? "this-pc" : here.ConnectionId?.ToString() ?? Title,
            local ? Ui.Pane.ThisPc : Title,
            local,
            here.RelativePath,
            rows,
            append);
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
        Status = _note ?? string.Empty;
        RaiseListingState();
        RaiseHeaders();
        Raise(nameof(ItemCount));
        RaiseCommands();
    }

    /// <summary>The column headings, whose arrow shows the order.</summary>
    private void RaiseHeaders()
    {
        Raise(nameof(NameHeader));
        Raise(nameof(SizeHeader));
        Raise(nameof(TypeHeader));
        Raise(nameof(ModifiedHeader));
        Raise(nameof(StatusHeader));
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
