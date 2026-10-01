using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using Lucide.Avalonia;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>
/// Which palette role a metric card takes.
/// </summary>
/// <remarks>
/// A class rather than a brush, so the card's colour resolves through the design tokens and follows
/// an appearance change. The WinForms cards each held their own resolved <c>Color</c> and had to be
/// re-tinted by the theme walker.
/// </remarks>
internal enum MetricTone
{
    /// <summary>No colour of its own. A count that is neither good nor bad, such as disabled tasks.</summary>
    Neutral,

    Primary,
    Success,
    Warning,
    Danger,
}

/// <summary>One of the four cards along the top of the overview.</summary>
internal sealed record MetricCard(
    string Value,
    string Caption,
    LucideIconKind Icon,
    MetricTone Tone)
{
    internal bool IsNeutral => Tone == MetricTone.Neutral;

    internal bool IsPrimary => Tone == MetricTone.Primary;

    internal bool IsSuccess => Tone == MetricTone.Success;

    internal bool IsWarning => Tone == MetricTone.Warning;

    internal bool IsDanger => Tone == MetricTone.Danger;
}

/// <summary>
/// The glyph at the start of a row, as 1.x's lists drew one on every line, the empty message's
/// included.
/// </summary>
internal sealed record RowIcon(LucideIconKind Kind, MetricTone Tone)
{
    /// <summary>A workspace or a connection.</summary>
    internal static RowIcon Connection { get; } = new(LucideIconKind.Network, MetricTone.Primary);

    /// <summary>A missing workspace, or a transfer that needs attention.</summary>
    internal static RowIcon Warning { get; } = new(LucideIconKind.TriangleAlert, MetricTone.Warning);

    /// <summary>"Nothing needs attention".</summary>
    internal static RowIcon Ok { get; } = new(LucideIconKind.CircleCheck, MetricTone.Success);

    /// <summary>An empty table's message.</summary>
    internal static RowIcon Empty { get; } = new(LucideIconKind.Info, MetricTone.Neutral);

    /// <summary>An enabled sync task, in the same tick as "Nothing needs attention".</summary>
    internal static RowIcon Enabled { get; } = new(LucideIconKind.CircleCheck, MetricTone.Success);

    /// <summary>A disabled sync task.</summary>
    internal static RowIcon Disabled { get; } = new(LucideIconKind.Pause, MetricTone.Neutral);

    /// <summary>A sync run.</summary>
    internal static RowIcon Run { get; } = new(LucideIconKind.Play, MetricTone.Primary);

    /// <summary>An empty Sync tasks table's message, which 1.x marked with a "…" rather than an "i".</summary>
    internal static RowIcon EmptySync { get; } = new(LucideIconKind.Ellipsis, MetricTone.Neutral);

    internal bool IsPrimary => Tone == MetricTone.Primary;

    internal bool IsSuccess => Tone == MetricTone.Success;

    internal bool IsWarning => Tone == MetricTone.Warning;
}

/// <summary>A row of the workspaces table.</summary>
internal sealed record WorkspaceRow(string Name, string Location, string State) : IPlaceholderRow
{
    public bool IsPlaceholder { get; init; }

    /// <summary>The remembered workspace the row stands for; none for the empty table's message.</summary>
    internal WorkspaceShortcutView? Shortcut { get; init; }

    /// <summary>
    /// A file that was not there when the list was drawn. Dimmed, as 1.x drew it, but not
    /// disabled: opening it is how it offers to leave the list.
    /// </summary>
    internal bool IsMissing => Shortcut is { LooksPresent: false };

    /// <summary>The file's path, which 1.x's row showed as its tooltip.</summary>
    internal string? ToolTip => Shortcut?.Entry.Path;

    internal RowIcon Icon => IsPlaceholder ? RowIcon.Empty : IsMissing ? RowIcon.Warning : RowIcon.Connection;
}

/// <summary>A row of the recent-connections table.</summary>
internal sealed record RecentConnectionRow(string Name, string Provider, string Details) : IPlaceholderRow
{
    public bool IsPlaceholder { get; init; }

    internal RowIcon Icon => IsPlaceholder ? RowIcon.Empty : RowIcon.Connection;
}

/// <summary>A row of the needs-attention table.</summary>
internal sealed record AttentionRow(string Name, string State, string Updated) : IPlaceholderRow
{
    public bool IsPlaceholder { get; init; }

    /// <summary>The message says all is well here, so it is a tick rather than the others' "i".</summary>
    internal RowIcon Icon => IsPlaceholder ? RowIcon.Ok : RowIcon.Warning;
}

/// <summary>
/// The overview, which is what the Welcome tab shows.
/// </summary>
/// <remarks>
/// <para>
/// Every string comes from OverviewStrings, which already had all of them - including the empty
/// states, which this screen spends most of its life showing and which are written out rather than
/// left blank.
/// </para>
/// <para>
/// Live, as 1.x's was: it asks the agent for the saved connections and for the active, queued and
/// failed transfers when it is refreshed, and again whenever the agent connects. It was a record
/// built once from the snapshot at startup, which is how its Agent card came to say "Starting"
/// while the status bar under it said "Agent: connected", and why its buttons did nothing.
/// </para>
/// </remarks>
internal sealed class OverviewModel : INotifyPropertyChanged
{
    private static readonly TransferQueueState[] ActiveStates =
    [
        TransferQueueState.Preparing,
        TransferQueueState.Connecting,
        TransferQueueState.Transferring,
        TransferQueueState.Verifying,
        TransferQueueState.Finalizing
    ];

    private static readonly TransferQueueState[] QueuedStates =
        [TransferQueueState.Pending, TransferQueueState.Retrying];

    private static readonly TransferQueueState[] AttentionStates =
    [
        TransferQueueState.Failed,
        TransferQueueState.Interrupted,
        TransferQueueState.NeedsReconciliation,
        TransferQueueState.BlockedCredential,
        TransferQueueState.BlockedTrust
    ];

    /// <summary>How many rows each table shows, as in 1.x.</summary>
    private const int MaximumListRows = 12;

    private readonly Func<IRemoteStorageAgentClient>? _storage;
    private readonly Func<ITransferQueueAgentClient>? _transfers;
    private ShellStatusSnapshot _status;
    private string _active = "0";
    private string _queued = "0";
    private string _attentionCount = "0";
    private string _statusText = string.Empty;
    private bool _statusIsWarning;
    private int _refreshing;

    private OverviewModel(
        ShellStatusSnapshot status,
        Func<IRemoteStorageAgentClient>? storage,
        Func<ITransferQueueAgentClient>? transfers)
    {
        _status = status;
        _storage = storage;
        _transfers = transfers;
        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync());

        // What 1.x's workspace list offered on a row: open it, pin or unpin it, take it off the
        // list, copy where it is. Each acts on the selected row, which the placeholder never is.
        _openWorkspace = new RelayCommand(
            _ => { if (Chosen() is { } chosen && OpenWorkspace is { } open) _ = open(chosen.Entry.Path); },
            _ => Chosen() is not null);
        _toggleWorkspacePin = new RelayCommand(
            _ => { if (Chosen() is { } chosen) ToggleWorkspacePin?.Invoke(chosen); },
            _ => Chosen() is not null);
        _forgetWorkspace = new RelayCommand(
            _ => { if (Chosen() is { } chosen) ForgetWorkspace?.Invoke(chosen.Entry.Path); },
            _ => Chosen() is not null);
        _copyWorkspacePath = new RelayCommand(
            _ => { if (Chosen() is { } chosen && CopyWorkspacePath is { } copy) _ = copy(chosen.Entry.Path); },
            _ => Chosen() is not null);
        _active = status.ActiveJobs.ToString(CultureInfo.CurrentCulture);
        _queued = status.QueuedJobs.ToString(CultureInfo.CurrentCulture);
        var strings = Ui.Overview;
        Workspaces = [new(strings.WorkspacesEmpty, strings.WorkspacesEmptyHint, string.Empty) { IsPlaceholder = true }];
        RecentConnections = [new(strings.ConnectionsEmpty, string.Empty, string.Empty) { IsPlaceholder = true }];
        Attention = [new(strings.AttentionEmpty, string.Empty, string.Empty) { IsPlaceholder = true }];
    }

    /// <summary>
    /// The overview as it stands with nothing saved and nothing running, and nothing to ask.
    /// </summary>
    /// <remarks>
    /// The empty rows are rows, not an absence of them: the WinForms screen fills each table with a
    /// single line saying what would be there ("No workspaces yet" / "Save a workspace to pin it
    /// here"), which reads better than an empty grid and keeps the column widths honest. Each is
    /// marked a placeholder, so it cannot be selected or opened (<see cref="PlaceholderRows"/>).
    /// </remarks>
    internal static OverviewModel Create(ShellStatusSnapshot status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new(status, null, null);
    }

    /// <summary>The overview the application shows: one that asks the agent what there is.</summary>
    internal static OverviewModel ForAgent(
        ShellStatusSnapshot status,
        Func<IRemoteStorageAgentClient> storage,
        Func<ITransferQueueAgentClient> transfers)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new(status, storage, transfers);
    }

    // A binding target has to be an instance property, and these are resolved per call rather than
    // captured so that they follow a language change. CA1822 sees only that they touch no field.
#pragma warning disable CA1822
    public string Headline => Ui.Overview.Headline;

    public string Subheading => Ui.Overview.Subheading;

    public string NewWorkspaceLabel => Ui.Overview.ActionNewWorkspace;

    // The button opens the New connection dialog since 1.x's Connection Manager became the
    // connections panel, so it says that rather than 1.x's "Connections".
    public string ConnectionsLabel => Ui.Shell.NewConnection;

    public string SyncTasksLabel => Ui.Overview.ActionSyncTasks;

    public string RefreshLabel => Ui.Overview.ActionRefresh;

    public string WorkspacesTitle => Ui.Overview.WorkspacesTitle;

    public string WorkspacesSubtitle => Ui.Overview.WorkspacesSubtitle;

    public string ConnectionsTitle => Ui.Overview.ConnectionsTitle;

    public string ConnectionsSubtitle => Ui.Overview.ConnectionsSubtitle;

    public string AttentionTitle => Ui.Overview.AttentionTitle;

    public string AttentionSubtitle => Ui.Overview.AttentionSubtitle;

    public string ColumnName => Ui.Overview.ColumnName;

    public string ColumnLocation => Ui.Overview.ColumnLocation;

    public string ColumnState => Ui.Overview.ColumnState;

    public string ColumnProvider => Ui.Overview.ColumnProvider;

    public string ColumnDetails => Ui.Overview.ColumnDetails;

    public string ColumnUpdated => Ui.Overview.ColumnUpdated;
#pragma warning restore CA1822

    /// <summary>The four cards: the agent, then active, queued and needing attention.</summary>
    public IReadOnlyList<MetricCard> Metrics =>
    [
        new(
            AgentValue(_status, Ui.Overview),
            Ui.Overview.MetricAgent,
            LucideIconKind.Server,
            _status.AgentIsHealthy ? MetricTone.Success : MetricTone.Primary),
        new(_active, Ui.Overview.MetricActiveTransfers, LucideIconKind.Play, MetricTone.Success),
        new(_queued, Ui.Overview.MetricQueued, LucideIconKind.ListOrdered, MetricTone.Primary),
        new(_attentionCount, Ui.Overview.MetricNeedsAttention, LucideIconKind.TriangleAlert, MetricTone.Warning),
    ];

    public IReadOnlyList<WorkspaceRow> Workspaces { get; private set; }

    /// <summary>The row the workspace menu and a double-click act on.</summary>
    public WorkspaceRow? SelectedWorkspace
    {
        get => _selectedWorkspace;
        set
        {
            if (Equals(_selectedWorkspace, value)) return;
            _selectedWorkspace = value;
            Raise(nameof(SelectedWorkspace));
            Raise(nameof(PinWorkspaceLabel));
            _openWorkspace.RaiseCanExecuteChanged();
            _toggleWorkspacePin.RaiseCanExecuteChanged();
            _forgetWorkspace.RaiseCanExecuteChanged();
            _copyWorkspacePath.RaiseCanExecuteChanged();
        }
    }

    private WorkspaceRow? _selectedWorkspace;
    private readonly RelayCommand _openWorkspace;
    private readonly RelayCommand _toggleWorkspacePin;
    private readonly RelayCommand _forgetWorkspace;
    private readonly RelayCommand _copyWorkspacePath;

    /// <summary>Opens the selected workspace: its menu's Open, a double-click, or Enter.</summary>
    public ICommand OpenWorkspaceCommand => _openWorkspace;

    public ICommand ToggleWorkspacePinCommand => _toggleWorkspacePin;

    /// <summary>Takes the selected workspace off the lists. The file itself is left where it is.</summary>
    public ICommand ForgetWorkspaceCommand => _forgetWorkspace;

    public ICommand CopyWorkspacePathCommand => _copyWorkspacePath;

    /// <summary>"Pin", or "Unpin" for a row that already is, as 1.x's menu said when it opened.</summary>
    public string PinWorkspaceLabel => Chosen() is { IsPinned: true } ? Ui.Overview.ContextUnpin : Ui.Overview.ContextPin;

#pragma warning disable CA1822
    public string OpenWorkspaceLabel => Ui.Overview.ContextOpen;

    public string ForgetWorkspaceLabel => Ui.Overview.ContextRemoveFromList;

    public string CopyWorkspacePathLabel => Ui.Overview.ContextCopyPath;
#pragma warning restore CA1822

    /// <summary>Opens a workspace file. Set by the shell, which owns the files.</summary>
    internal Func<string, Task>? OpenWorkspace { get; set; }

    /// <summary>Pins or unpins a workspace. Set by the shell.</summary>
    internal Action<WorkspaceShortcutView>? ToggleWorkspacePin { get; set; }

    /// <summary>Drops a workspace from the pinned and recent lists. Set by the shell.</summary>
    internal Action<string>? ForgetWorkspace { get; set; }

    /// <summary>Puts a path on the clipboard. Set by the shell, which has the window it belongs to.</summary>
    internal Func<string, Task>? CopyWorkspacePath { get; set; }

    /// <summary>
    /// Lists the remembered workspaces, as the shell has already ordered them: pinned first.
    /// </summary>
    /// <remarks>
    /// This page does no IO of its own, as 1.x's did not: which files look present is decided by
    /// whoever reads the lists. The State column says Pinned, Recent or Missing in words, so the
    /// dimming is not the only thing that tells a screen reader a file is gone.
    /// </remarks>
    internal void ShowWorkspaces(IReadOnlyList<WorkspaceShortcutView> shortcuts)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        var strings = Ui.Overview;
        var selected = Chosen()?.Entry.Path;
        WorkspaceRow[] rows =
        [
            .. shortcuts.Select(shortcut => new WorkspaceRow(
                shortcut.Entry.DisplayName,
                shortcut.Entry.Path,
                !shortcut.LooksPresent ? strings.WorkspaceStateMissing
                    : shortcut.IsPinned ? strings.WorkspaceStatePinned
                    : strings.WorkspaceStateRecent)
            {
                Shortcut = shortcut
            }),
        ];
        Workspaces = rows.Length > 0
            ? rows
            : [new(strings.WorkspacesEmpty, strings.WorkspacesEmptyHint, string.Empty) { IsPlaceholder = true }];
        Raise(nameof(Workspaces));

        // The same file stays selected when the list is drawn again, say after pinning it from
        // here, rather than the selection vanishing under the pointer.
        SelectedWorkspace = rows.FirstOrDefault(row => string.Equals(
            row.Shortcut?.Entry.Path, selected, StringComparison.OrdinalIgnoreCase));
    }

    private WorkspaceShortcutView? Chosen() => _selectedWorkspace?.Shortcut;

    public IReadOnlyList<RecentConnectionRow> RecentConnections { get; private set; }

    public IReadOnlyList<AttentionRow> Attention { get; private set; }

    /// <summary>"Updated 12:26", or why the last refresh could not finish.</summary>
    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (string.Equals(_statusText, value, StringComparison.Ordinal)) return;
            _statusText = value;
            Raise(nameof(StatusText));
            Raise(nameof(HasStatusText));
        }
    }

    public bool HasStatusText => _statusText.Length > 0;

    public bool StatusIsWarning
    {
        get => _statusIsWarning;
        private set
        {
            if (_statusIsWarning == value) return;
            _statusIsWarning = value;
            Raise(nameof(StatusIsWarning));
        }
    }

    /// <summary>New workspace: the arrangement chooser. Set by the shell.</summary>
    public ICommand? NewWorkspaceCommand { get; internal set; }

    /// <summary>Connections: a new connection, as 1.x's opened. Set by the shell, which owns the windows.</summary>
    public ICommand? ConnectionsCommand { get; internal set; }

    /// <summary>Sync tasks: the tab beside this one. Set by the shell.</summary>
    public ICommand? SyncTasksCommand { get; internal set; }

    public ICommand RefreshCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Takes the status bar's snapshot, and refreshes when the agent has just come back.
    /// </summary>
    /// <remarks>
    /// The overview is usually what somebody is looking at when the agent restarts, so it is the
    /// surface most worth not leaving stale - which is what 1.x reloaded it on, too.
    /// </remarks>
    internal void UpdateStatus(ShellStatusSnapshot status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var connected = !_status.AgentIsHealthy && status.AgentIsHealthy;
        _status = status;
        if (_storage is null)
        {
            _active = status.ActiveJobs.ToString(CultureInfo.CurrentCulture);
            _queued = status.QueuedJobs.ToString(CultureInfo.CurrentCulture);
        }

        Raise(nameof(Metrics));
        if (connected) _ = RefreshAsync();
    }

    /// <summary>Asks the agent for the connections and the transfers, and fills the page.</summary>
    internal async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_storage is null || _transfers is null) return;
        if (Interlocked.Exchange(ref _refreshing, 1) != 0) return;

        try
        {
            StatusText = Ui.Overview.StatusRefreshing;
            StatusIsWarning = false;

            await using var storage = _storage();
            await using var transfers = _transfers();
            var connections = await storage
                .ListConnectionsAsync(
                    new ConnectionListRequest(IncludeDisabled: false, Limit: StorageIpcLimits.MaximumConnectionResults),
                    cancellationToken)
                .ConfigureAwait(true);
            ThrowIfFailure(connections.Failure);
            var active = await ListAsync(transfers, ActiveStates, cancellationToken).ConfigureAwait(true);
            var queued = await ListAsync(transfers, QueuedStates, cancellationToken).ConfigureAwait(true);
            var attention = await ListAsync(transfers, AttentionStates, cancellationToken).ConfigureAwait(true);

            _active = Count(active);
            _queued = Count(queued);
            _attentionCount = Count(attention);
            Raise(nameof(Metrics));

            _saved = connections.Connections;
            ComposeRecentConnections();

            var problems = attention.Transfers
                .OrderByDescending(static value => value.UpdatedUtc)
                .Take(MaximumListRows)
                .Select(static value => new AttentionRow(
                    $"{value.Operation}: {(string.IsNullOrWhiteSpace(value.SourcePath) ? value.DestinationPath : value.SourcePath)}",
                    UiEnumNames.Describe(value.State),
                    value.UpdatedUtc.LocalDateTime.ToString("g", CultureInfo.CurrentCulture)))
                .ToArray();
            Attention = problems.Length > 0
                ? problems
                : [new(Ui.Overview.AttentionEmpty, string.Empty, string.Empty) { IsPlaceholder = true }];
            Raise(nameof(Attention));

            StatusText = Ui.Format(Ui.Overview.StatusUpdatedFormat, DateTime.Now);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Said once, in the shell's own words for an agent that did not answer, rather than as
            // the exception's message - which is how "Pipe is broken." used to reach this page.
            StatusText = Ui.Shell.AgentNotConnected;
            StatusIsWarning = true;
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);
        }
    }

    private IReadOnlyList<ConnectionSummary> _saved = [];
    private readonly List<Guid> _recent = [];

    /// <summary>
    /// Puts a connection a pane just opened at the top of Recent connections, as 1.x did:
    /// "Opened this session, followed by saved favorites" is what the table's subtitle says.
    /// </summary>
    internal void RecordRecent(Guid connectionId)
    {
        _recent.Remove(connectionId);
        _recent.Insert(0, connectionId);
        if (_recent.Count > MaximumListRows) _recent.RemoveRange(MaximumListRows, _recent.Count - MaximumListRows);
        ComposeRecentConnections();
    }

    private void ComposeRecentConnections()
    {
        var byId = _saved.ToDictionary(static value => value.ConnectionId);
        var rows = _recent
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .Concat(_saved
                .OrderByDescending(static value => value.IsFavorite)
                .ThenBy(static value => value.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            .DistinctBy(static value => value.ConnectionId)
            .Take(MaximumListRows)
            .Select(static value => new RecentConnectionRow(
                value.DisplayName,
                value.Provider.ToString(),
                value.IsFavorite ? Ui.Overview.ConnectionFavorite : value.FolderPath ?? string.Empty))
            .ToArray();
        RecentConnections = rows.Length > 0
            ? rows
            : [new(Ui.Overview.ConnectionsEmpty, string.Empty, string.Empty) { IsPlaceholder = true }];
        Raise(nameof(RecentConnections));
    }

    private static async Task<TransferListResponse> ListAsync(
        ITransferQueueAgentClient client,
        TransferQueueState[] states,
        CancellationToken cancellationToken)
    {
        var response = await client
            .ListAsync(new TransferListRequest(TransferQueueIpcContract.CurrentVersion, states, PageSize: 25), cancellationToken)
            .ConfigureAwait(true);
        ThrowIfFailure(response.Failure);
        return response;
    }

    /// <summary>A count, with a "+" when there was more than one page of it.</summary>
    private static string Count(TransferListResponse response) =>
        response.ContinuationToken is null
            ? response.Transfers.Length.ToString(CultureInfo.CurrentCulture)
            : $"{response.Transfers.Length}+";

    private static void ThrowIfFailure(StorageIpcFailure? failure)
    {
        if (failure is not null) throw new InvalidOperationException(failure.Message);
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>
    /// The agent's state in the card's own shorter wording.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="ShellStatusSnapshot.AgentText"/>: the status bar says
    /// "Agent: connected" because it has no label beside it, while the card says "Connected" under a
    /// caption that already reads "Agent". OverviewStrings has always carried both sets.
    /// </remarks>
    private static string AgentValue(ShellStatusSnapshot status, OverviewStrings strings) =>
        status.AgentState switch
        {
            AgentConnectionState.Connected => strings.AgentConnected,
            AgentConnectionState.RecoveryOnly => strings.AgentRecoveryMode,
            AgentConnectionState.Disconnected => strings.AgentOffline,
            _ => strings.AgentStarting,
        };
}
