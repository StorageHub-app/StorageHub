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
    private PagedListingIndex? _index;
    private int _paneNumber = 1;
    private bool _showConnectionBar = true;
    private BrowserSortColumn _sortColumn = BrowserSortColumn.Name;
    private bool _sortAscending = true;
    private string _filter = string.Empty;
    private bool _isAtRoot = true;

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
    /// How a stale agent gets restarted when it turns out to be speaking an older protocol. Nothing
    /// in the shell supplies one yet -- the screen that starts and stops the agent has not been
    /// ported -- so the mismatch is reported and the user is told to restart StorageHub, which is
    /// the same fallback 1.x used when it had no controller to hand.
    /// </param>
    internal BrowserPaneModel(
        IRemoteStorageAgentClient? client = null,
        Func<ILocalFileBrowserDataSource?>? localSource = null,
        Func<PaneMutationController>? mutations = null,
        IDialogService? dialogs = null,
        Func<ISshTerminalAgentClient>? terminals = null,
        Func<IAgentLifecycleController?>? agentLifecycle = null)
    {
        _terminals = terminals;
        _agentLifecycle = agentLifecycle;
        _controller = new RemoteBrowserController(client);
        _localSource = localSource ?? (static () => null);
        _mutations = mutations;
        _dialogs = dialogs;
        SelectedRows.CollectionChanged += (_, _) =>
        {
            Raise(nameof(HasSelection));
            Raise(nameof(SelectionSummary));
        };

        NewFolderCommand = new RelayCommand(
            _ => _ = CreateAsync(container: true), _ => CanMutateHere);
        NewFileCommand = new RelayCommand(
            _ => _ = CreateAsync(container: false), _ => CanMutateHere);
        RenameCommand = new RelayCommand(
            _ => _ = RenameAsync(), _ => CanMutateHere && Chosen().Count == 1);
        DeleteCommand = new RelayCommand(
            _ => _ = DeleteAsync(), _ => CanMutateHere && Chosen().Count > 0);

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
            Raise(nameof(PaneHeading));
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
        PaneTransferSnapshots.ContextFor(_source) is { IsSuccess: true, Value.Kind:
            PaneTransferContextKind.ThisPc or PaneTransferContextKind.SavedConnection };

    public static string NewFolderLabel => Ui.Shell.NewFolderTitle;

    public static string NewFileLabel => Ui.Shell.NewEmptyFile;

    public static string RenameLabel => Ui.Shell.RenameWorkspaceAccept;

    public static string DeleteLabel => Ui.Commands.EditDelete;

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

            // Whatever this pane is about to show, it is no longer showing the old shell. Pointing
            // a terminal pane at a bucket would otherwise leave the session open and unreachable,
            // holding two pipes and a keep-alive for a window nobody can see.
            await CloseTerminalAsync().ConfigureAwait(true);

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

                // Nothing in the status strip yet: the session puts its own state there as soon as
                // it has one, and a pane that says it twice reads as two problems.
                Status = string.Empty;
                await OpenTerminalAsync(choice, cancellationToken).ConfigureAwait(true);
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
            Status = Ui.Format(Ui.Shell.ItemCreateFailedFormat, result.Error.Message);
            return;
        }

        await MoveAsync(PaneNavigationKind.Refresh, cancellationToken: cancellationToken)
            .ConfigureAwait(true);
        Status = Ui.Format(
            container ? Ui.Shell.CreatedFolderFormat : Ui.Shell.CreatedFileFormat, name);
    }

    /// <summary>Asks for a new name for the one selected item, and applies it.</summary>
    internal async Task RenameAsync(CancellationToken cancellationToken = default)
    {
        if (!CanMutateHere || Here() is not { } location) return;
        if (Chosen() is not [var row])
        {
            Status = Ui.Shell.SelectOneToRename;
            return;
        }

        var item = PaneTransferSnapshots.ItemFor(row);
        if (item.IsFailure)
        {
            Status = item.Error.Message;
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
            Status = result.Error.Message;
            return;
        }

        await MoveAsync(PaneNavigationKind.Refresh, cancellationToken: cancellationToken)
            .ConfigureAwait(true);
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
            Status = selection.Error.Message;
            return;
        }

        var items = selection.Value.Items;
        if (items.Count == 0) return;

        var summary = items.Count == 1
            ? items[0].Name
            : Ui.Format(Ui.Dialogs.SelectedItemsFormat, items.Count);
        var choice = await _dialogs!.ConfirmAsync(
            new DialogRequest
            {
                Title = Ui.Shell.DeleteItemsCaption,
                Message = Ui.Format(Ui.Shell.DeleteItemsPromptFormat, summary),
                Detail = Ui.Shell.DeleteItemsDetail,
                Severity = DialogSeverity.Warning,
                Buttons = DialogButtons.YesNo
            },
            cancellationToken).ConfigureAwait(true);
        if (choice != DialogChoice.Yes) return;

        await using var mutations = _mutations!();
        var outcome = await mutations
            .DeleteAsync(location, items, cancellationToken)
            .ConfigureAwait(true);

        await MoveAsync(PaneNavigationKind.Refresh, cancellationToken: cancellationToken)
            .ConfigureAwait(true);

        // How far it got matters as much as whether it finished: "three of five were deleted, then
        // this happened" is a different situation from "nothing was deleted".
        Status = outcome switch
        {
            { IsSuccess: true } => Ui.Format(Ui.Shell.DeletedItemsFormat, outcome.Deleted),
            { Deleted: 0 } => Ui.Format(Ui.Shell.ItemsDeleteFailedFormat, outcome.Failure!.Message),
            _ => Ui.Format(Ui.Shell.DeletedThenStoppedFormat, outcome.Deleted, outcome.Failure!.Message)
        };
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
        }
    }

    /// <summary>Whether there is a live session, as opposed to a pane waiting to be given one.</summary>
    public bool HasTerminal => _terminal is not null;

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
            preferences: null,
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
