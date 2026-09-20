using System.ComponentModel;
using System.Windows.Input;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>
/// Two panes and the transfers between them.
/// </summary>
/// <remarks>
/// <para>
/// This is the thing that makes a file manager out of two file listings: which pane is the source,
/// which is the destination, and what happens when somebody asks to copy. The panes themselves know
/// nothing about each other, which is why the decision lives here rather than in either of them.
/// </para>
/// <para>
/// The source is whichever pane is active and the destination is the other one -- the rule every
/// two-pane manager has used since Norton Commander, and the one the WinForms shell used. It is
/// worth stating because it is the only rule here: there is no separate "choose a destination"
/// step, and adding one would be the thing that made this slower than the tool it replaces.
/// </para>
/// </remarks>
internal sealed class WorkspaceModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly Func<ITransferQueueAgentClient> _queue;
    private readonly Func<IRemoteStorageAgentClient> _storage;
    private readonly Func<IObjectInspectorAgentClient> _mutations;
    private readonly Func<Task>? _queueChanged;
    private string _message = string.Empty;

    /// <remarks>
    /// Three client factories because a transfer touches three agent surfaces: the queue it is
    /// enqueued on, the storage it walks to expand a folder, and the inspector it asks to create
    /// the destination folders. A factory rather than a client each, because every one of these is
    /// opened per operation and closed after it - holding one open means holding one that broke
    /// when the agent restarted.
    /// </remarks>
    internal WorkspaceModel(
        BrowserPaneModel left,
        BrowserPaneModel right,
        Func<ITransferQueueAgentClient> queue,
        Func<IRemoteStorageAgentClient> storage,
        Func<IObjectInspectorAgentClient> mutations,
        Func<Task>? queueChanged = null)
    {
        Left = left ?? throw new ArgumentNullException(nameof(left));
        Right = right ?? throw new ArgumentNullException(nameof(right));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _mutations = mutations ?? throw new ArgumentNullException(nameof(mutations));
        _queueChanged = queueChanged;

        Left.PropertyChanged += OnPaneChanged;
        Right.PropertyChanged += OnPaneChanged;
        Left.SelectedRows.CollectionChanged += (_, _) => RaiseCommands();
        Right.SelectedRows.CollectionChanged += (_, _) => RaiseCommands();

        CopyCommand = new RelayCommand(
            _ => _ = TransferAsync(TransferQueueOperation.Copy),
            _ => CanTransfer);
        MoveCommand = new RelayCommand(
            _ => _ = TransferAsync(TransferQueueOperation.Move),
            _ => CanMove);

        // Both panes show the same two commands. Which pane is the source is decided when one of
        // them runs, from whichever pane is active - not from which button was pressed, because
        // pressing a button in a pane is one of the ways to make it the active one.
        Left.CopyCommand = CopyCommand;
        Left.MoveCommand = MoveCommand;
        Right.CopyCommand = CopyCommand;
        Right.MoveCommand = MoveCommand;
    }

    public BrowserPaneModel Left { get; }

    public BrowserPaneModel Right { get; }

    /// <summary>The pane a transfer takes from: whichever one was last clicked into.</summary>
    internal BrowserPaneModel Source => Right.IsActive ? Right : Left;

    /// <summary>And the one it goes to, which is always the other.</summary>
    internal BrowserPaneModel Destination => Right.IsActive ? Left : Right;

    /// <summary>What the last transfer attempt said, or nothing.</summary>
    public string Message
    {
        get => _message;
        private set
        {
            if (string.Equals(_message, value, StringComparison.Ordinal)) return;
            _message = value;
            Raise(nameof(Message));
            Raise(nameof(HasMessage));
        }
    }

    public bool HasMessage => !string.IsNullOrEmpty(_message);

    public ICommand CopyCommand { get; }

    public ICommand MoveCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Something is selected in the source, and the destination is somewhere to put it.
    /// </summary>
    /// <remarks>
    /// Deliberately not "and they are different panes". Copying a folder into the same folder is a
    /// legitimate request that the queue answers with a collision, and refusing it here would
    /// answer a question the destination is better placed to.
    /// </remarks>
    internal bool CanTransfer => Source.HasSelection && Destination.Snapshot is not null;

    /// <summary>
    /// A move needs more than a copy: files only, and every one carrying a stable identity.
    /// </summary>
    /// <remarks>
    /// Both rules come from the agent refusing the request, and both are worth checking here
    /// because the difference is a button that is dim against one that fails after it is pressed.
    /// A move deletes the source, so it will only run against an object the agent can prove is the
    /// one it listed; and a folder move is not enabled at all, because a half-moved tree is not
    /// something either end can recover from. Providers differ on identity, so the same selection
    /// is movable on one connection and not on another.
    /// </remarks>
    internal bool CanMove =>
        CanTransfer &&
        Source.SelectedRows
            .Where(static row => !row.IsParentNavigation)
            .All(static row => !row.IsContainer &&
                (row.VersionId is not null || row.EntityTag is not null));

    /// <summary>
    /// Queues the selection, and says what happened.
    /// </summary>
    /// <remarks>
    /// Every step can refuse, and each refusal is a sentence rather than an exception: a row that
    /// is not transferable, a destination still paging, a queue that is not accepting. The shell
    /// has to keep working after any of them, because the pane behind this is still perfectly
    /// usable.
    /// </remarks>
    internal async Task TransferAsync(
        TransferQueueOperation operation,
        CancellationToken cancellationToken = default)
    {
        var source = Source;
        var destination = Destination;

        var selection = PaneTransferSnapshots.SelectionFor(source.Snapshot, source.SelectedRows);
        if (selection.IsFailure)
        {
            Message = selection.Error.Message;
            return;
        }

        var target = PaneTransferSnapshots.DestinationFor(destination.Snapshot, destination.Rows);
        if (target.IsFailure)
        {
            Message = target.Error.Message;
            return;
        }

        // Always the recursive controller, even for a selection of plain files: it delegates to
        // ManualTransferController for those and is the only one that can expand a folder. Choosing
        // between them here would mean this deciding what a container is, which is the storage
        // layer's answer and not the shell's.
        await using var transfers = new ManualTransferController(_queue(), ownsClient: true);
        await using var recursive = new RecursiveTransferController(
            transfers, _storage(), _mutations(), ownsClients: true);
        try
        {
            var result = await recursive
                .EnqueueAsync(selection.Value, target.Value, operation, cancellationToken)
                .ConfigureAwait(true);

            Message = result.Failure is { } failure
                ? failure.Message
                : Ui.Format(Ui.Transfer.QueuedFormat, result.Accepted.Count);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception error) when (error is IOException or TimeoutException or
            InvalidOperationException or ObjectDisposedException)
        {
            Message = Ui.Transfer.QueueUnavailable;
            return;
        }

        if (_queueChanged is not null)
        {
            await _queueChanged().ConfigureAwait(true);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Left.PropertyChanged -= OnPaneChanged;
        Right.PropertyChanged -= OnPaneChanged;
        await Left.DisposeAsync().ConfigureAwait(false);
        await Right.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Clicking into one pane makes the other inactive.
    /// </summary>
    /// <remarks>
    /// Two active panes would leave the source ambiguous, and a pane that cannot be made inactive
    /// by clicking elsewhere is one somebody has to un-click.
    /// </remarks>
    private void OnPaneChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BrowserPaneModel.IsActive) &&
            sender is BrowserPaneModel { IsActive: true } activated)
        {
            if (ReferenceEquals(activated, Left)) Right.IsActive = false;
            else Left.IsActive = false;
        }

        RaiseCommands();
    }

    private void RaiseCommands()
    {
        (CopyCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (MoveCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
