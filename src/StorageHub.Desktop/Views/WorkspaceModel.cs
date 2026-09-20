using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;

namespace StorageHub.Desktop.Views;

/// <summary>
/// One to four panes, the layout holding them, and the transfers between them.
/// </summary>
/// <remarks>
/// <para>
/// This is the thing that makes a file manager out of a set of file listings. The panes themselves
/// know nothing about each other, which is why every decision that needs two of them lives here.
/// </para>
/// <para>
/// The arrangement is <see cref="WorkspaceLayoutModel"/>, which is in Desktop.Core, is already
/// tested, and was already written for four: a tree of splits with a pane at each leaf. This type
/// holds the pane view models that fill those leaves and nothing about how they are drawn --
/// <c>WorkspaceView</c> walks the same tree into nested grids. Two implementations of a layout
/// would be two places for a three-pane arrangement to come out wrong.
/// </para>
/// <para>
/// <b>Transfers are staged, then pasted.</b> With two panes "copy" can mean "into the other one",
/// because there is exactly one other one; with three or four that phrase names nothing. So Copy
/// and Move stage the active pane's selection, and Paste puts it into whichever pane is active
/// then. This is what the 1.x shell did with two panes already -- <c>StageSelection</c> on the
/// pane, <c>PasteIntoPane</c> on the form -- so growing to four costs a layout rather than a
/// redesign, and the rule is one sentence at every pane count instead of one per count.
/// </para>
/// </remarks>
internal sealed class WorkspaceModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly Func<BrowserPaneModel> _paneFactory;
    private readonly Func<ITransferQueueAgentClient> _queue;
    private readonly Func<IRemoteStorageAgentClient> _storage;
    private readonly Func<IObjectInspectorAgentClient> _mutations;
    private readonly Func<Task>? _queueChanged;
    private readonly IDialogService? _dialogs;
    private readonly Dictionary<Guid, BrowserPaneModel> _byId = [];
    private WorkspaceLayoutModel _layout;
    private WorkspacePreset _preset;
    private PaneClipboard? _clipboard;
    private string _message = string.Empty;

    /// <param name="paneFactory">
    /// How a new pane is built. The workspace makes and drops panes as the arrangement changes, so
    /// it needs to be able to produce one rather than be handed a fixed set.
    /// </param>
    /// <param name="dialogs">
    /// Where the paste confirmation goes. Null runs transfers unconfirmed, which is what a headless
    /// test wants; the shell always passes one.
    /// </param>
    /// <remarks>
    /// Three client factories because a transfer touches three agent surfaces: the queue it is
    /// enqueued on, the storage it walks to expand a folder, and the inspector it asks to create
    /// the destination folders. A factory rather than a client each, because every one of these is
    /// opened per operation and closed after it - holding one open means holding one that broke
    /// when the agent restarted.
    /// </remarks>
    internal WorkspaceModel(
        Func<BrowserPaneModel> paneFactory,
        Func<ITransferQueueAgentClient> queue,
        Func<IRemoteStorageAgentClient> storage,
        Func<IObjectInspectorAgentClient> mutations,
        Func<Task>? queueChanged = null,
        WorkspacePreset? preset = null,
        IDialogService? dialogs = null)
    {
        _paneFactory = paneFactory ?? throw new ArgumentNullException(nameof(paneFactory));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _mutations = mutations ?? throw new ArgumentNullException(nameof(mutations));
        _queueChanged = queueChanged;
        _dialogs = dialogs;

        _preset = preset ?? WorkspacePreset.All[1];
        _layout = WorkspaceLayoutModel.CreatePreset(_preset.PaneCount, _preset.Layout);

        StageCopyCommand = new RelayCommand(
            _ => Stage(TransferQueueOperation.Copy), _ => CanStage(TransferQueueOperation.Copy));
        StageMoveCommand = new RelayCommand(
            _ => Stage(TransferQueueOperation.Move), _ => CanStage(TransferQueueOperation.Move));
        PasteCommand = new RelayCommand(_ => _ = PasteAsync(), _ => CanPaste);
        ClearClipboardCommand = new RelayCommand(_ => Clipboard = null, _ => _clipboard is not null);
        ClosePaneCommand = new RelayCommand(
            pane => ClosePane(pane as BrowserPaneModel),
            pane => _layout.PaneCount > 1 && pane is BrowserPaneModel);

        Rebuild();
        Panes[0].IsActive = true;
    }

    /// <summary>The panes, in the order the layout tree visits them.</summary>
    public ObservableCollection<BrowserPaneModel> Panes { get; } = [];

    /// <summary>The arrangement itself, for the view to walk and for a save to serialise.</summary>
    internal WorkspaceLayoutModel Layout => _layout;

    /// <summary>Raised when the tree changed shape and the view has to rebuild its grids.</summary>
    internal event EventHandler? LayoutChanged;

    /// <summary>The six arrangements offered: one, two either way, three either way, and a grid.</summary>
    /// <remarks>
    /// The list itself is in Desktop.Core, so the chooser offers what a saved workspace can hold.
    /// </remarks>
    public static IReadOnlyList<WorkspacePreset> Presets => WorkspacePreset.All;

    public static string ClearStagedLabel => Ui.Pane.ClearStaged;

    /// <summary>
    /// Which arrangement is showing. Setting it grows or shrinks the set of panes.
    /// </summary>
    /// <remarks>
    /// Panes that survive the change keep their connection and their folder: the new layout is
    /// filled from the existing panes in order, and only the shortfall is built. Rebuilding all of
    /// them would make switching from two panes to three cost two reconnections, which is how a
    /// layout button turns into something nobody presses twice.
    /// </remarks>
    public WorkspacePreset Preset
    {
        get => _preset;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_preset == value) return;
            _preset = value;
            _layout = WorkspaceLayoutModel.CreatePreset(value.PaneCount, value.Layout);
            Rebuild();
            Raise(nameof(Preset));
        }
    }

    /// <summary>
    /// The pane a command acts on: whichever one was last clicked into.
    /// </summary>
    /// <remarks>
    /// Never null while a workspace exists, because a workspace has at least one pane and one of
    /// them is always active. Falling back to the first pane rather than returning null means no
    /// call site has to answer "what if nothing is focused", which in practice is a state that only
    /// lasts between construction and the first click.
    /// </remarks>
    internal BrowserPaneModel Active =>
        Panes.FirstOrDefault(static pane => pane.IsActive) ?? Panes[0];

    /// <summary>What is staged and waiting to be pasted, or nothing.</summary>
    internal PaneClipboard? Clipboard
    {
        get => _clipboard;
        private set
        {
            _clipboard = value;
            Raise(nameof(Clipboard));
            Raise(nameof(HasClipboard));
            Raise(nameof(ClipboardSummary));
            RaiseCommands();
        }
    }

    public bool HasClipboard => _clipboard is not null;

    /// <summary>"Ready to copy 3 items from Studio Assets", or nothing.</summary>
    public string ClipboardSummary => _clipboard?.Describe() ?? string.Empty;

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

    public ICommand StageCopyCommand { get; }

    public ICommand StageMoveCommand { get; }

    public ICommand PasteCommand { get; }

    public ICommand ClearClipboardCommand { get; }

    public ICommand ClosePaneCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Whether the active pane could stage this operation.
    /// </summary>
    /// <remarks>
    /// The rules are <see cref="PaneClipboardRules"/> in Desktop.Core, so a pane on Windows and a
    /// pane on Linux refuse the same move for the same reason. A terminal has no selection to
    /// stage, which is the one case decided here rather than there.
    /// </remarks>
    internal bool CanStage(TransferQueueOperation operation) =>
        Panes.Count > 0 &&
        Active is { IsTerminal: false, Source: not null } pane &&
        PaneClipboardRules.CanStage(pane.SelectedRows, operation);

    /// <summary>
    /// Whether the active pane is somewhere the staged items could land.
    /// </summary>
    /// <remarks>
    /// Deliberately not "and it is a different pane". Pasting into the folder something was copied
    /// from is a legitimate request that the queue answers with a collision, and refusing it here
    /// would answer a question the destination is better placed to.
    /// </remarks>
    internal bool CanPaste =>
        _clipboard is not null &&
        Panes.Count > 0 &&
        Active is { IsTerminal: false, Source: not null };

    /// <summary>Stages the active pane's selection, replacing whatever was staged before.</summary>
    internal void Stage(TransferQueueOperation operation)
    {
        var pane = Active;
        var selection = PaneTransferSnapshots.SelectionFor(pane.Source, pane.SelectedRows);
        if (selection.IsFailure)
        {
            Message = selection.Error.Message;
            return;
        }

        Message = string.Empty;
        Clipboard = new PaneClipboard(selection.Value, operation, pane.Title);
    }

    /// <summary>
    /// Queues what is staged into the active pane, after confirming it.
    /// </summary>
    /// <remarks>
    /// Every step can refuse, and each refusal is a sentence rather than an exception: a
    /// destination still paging, a queue that is not accepting, an agent that went away. The shell
    /// has to keep working after any of them, because the panes behind this are still perfectly
    /// usable. A declined confirmation leaves the selection staged, so saying no is not the same as
    /// losing what was staged.
    /// </remarks>
    internal async Task PasteAsync(CancellationToken cancellationToken = default)
    {
        if (_clipboard is not { } clipboard) return;
        var destination = Active;

        var target = PaneTransferSnapshots.DestinationFor(
            destination.Source, destination.Rows, destination.HasMorePages);
        if (target.IsFailure)
        {
            Message = target.Error.Message;
            return;
        }

        if (!await ConfirmAsync(clipboard, destination, cancellationToken).ConfigureAwait(true))
        {
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
                .EnqueueAsync(clipboard.Selection, target.Value, clipboard.Operation, cancellationToken)
                .ConfigureAwait(true);

            Message = result.Failure is { } failure
                ? failure.Message
                : Ui.Format(Ui.Transfer.QueuedFormat, result.Accepted.Count);

            // A move is spent once it is queued; a copy can reasonably be pasted into a second
            // destination, which is most of the point of staging it separately.
            if (result.Failure is null && clipboard.IsMove) Clipboard = null;
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

    /// <summary>Drops a pane from the arrangement, keeping the others as they are.</summary>
    internal void ClosePane(BrowserPaneModel? pane)
    {
        if (pane is null) return;
        var id = _byId.FirstOrDefault(entry => ReferenceEquals(entry.Value, pane)).Key;
        if (id == Guid.Empty || !_layout.Close(id)) return;
        _preset = WorkspacePreset.Find(_layout.PaneCount, _preset.Layout) ?? _preset;
        Rebuild();
        Raise(nameof(Preset));
    }

    /// <summary>The pane view model for a leaf, for the view walking the same tree.</summary>
    internal BrowserPaneModel? PaneFor(Guid id) => _byId.GetValueOrDefault(id);

    /// <summary>Records where a splitter was dragged to, so the arrangement can be saved.</summary>
    /// <remarks>
    /// Does not raise <see cref="LayoutChanged"/>: the grid the person just dragged is already
    /// showing the new ratio, and rebuilding it under their pointer would reset every pane's
    /// scroll position at the end of every drag.
    /// </remarks>
    internal void SetRatio(WorkspaceSplitNode node, double ratio) => _layout.SetRatio(node, ratio);

    public async ValueTask DisposeAsync()
    {
        foreach (var pane in Panes)
        {
            pane.PropertyChanged -= OnPaneChanged;
            await pane.DisposeAsync().ConfigureAwait(false);
        }

        Panes.Clear();
        _byId.Clear();
    }

    /// <summary>
    /// Matches the set of pane view models to the set of leaves in the layout.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pane that still has its own leaf keeps it. That is the case when a pane was closed or a
    /// split moved: the other leaves are the same objects, so every other connection stays exactly
    /// where it was and only the closed one is let go.
    /// </para>
    /// <para>
    /// Switching arrangement is the other case, and there the leaves are all new, so the panes are
    /// reused in order instead -- which keeps as many open connections as the new arrangement has
    /// room for. Only panes left without a leaf either way are disposed, and their clients with
    /// them.
    /// </para>
    /// </remarks>
    private void Rebuild()
    {
        var ids = _layout.PaneIds;
        var surviving = Panes.ToList();
        var assigned = new Dictionary<Guid, BrowserPaneModel>();

        foreach (var id in ids)
        {
            if (_byId.TryGetValue(id, out var existing) && surviving.Remove(existing))
            {
                assigned[id] = existing;
            }
        }

        var spare = new Queue<BrowserPaneModel>(surviving);
        foreach (var id in ids)
        {
            if (assigned.ContainsKey(id)) continue;
            assigned[id] = spare.Count > 0 ? spare.Dequeue() : NewPane();
        }

        foreach (var orphan in spare)
        {
            orphan.PropertyChanged -= OnPaneChanged;
            _ = CloseAsync(orphan);
        }

        _byId.Clear();
        Panes.Clear();
        foreach (var id in ids)
        {
            _byId[id] = assigned[id];
            Panes.Add(assigned[id]);
        }

        if (!Panes.Any(static pane => pane.IsActive)) Panes[0].IsActive = true;

        DescribePanes();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        RaiseCommands();
    }

    /// <summary>
    /// Gives every pane its number and the actions that act on the arrangement.
    /// </summary>
    /// <remarks>
    /// Redone on every rebuild rather than once per pane, because all of it is positional: closing
    /// pane 2 makes the old pane 3 the new pane 2, splitting fills the last free leaf, and a Move
    /// or swap menu built earlier would offer panes that are no longer there. Rebuilding is cheap
    /// and there are at most four.
    /// </remarks>
    private void DescribePanes()
    {
        var ids = _layout.PaneIds;
        for (var index = 0; index < ids.Count; index++)
        {
            var id = ids[index];
            var pane = Panes[index];
            pane.PaneNumber = index + 1;
            pane.SplitRightCommand = SplitCommand(id, WorkspaceDockEdge.Right);
            pane.SplitBelowCommand = SplitCommand(id, WorkspaceDockEdge.Bottom);
            pane.ClosePaneCommand = new RelayCommand(
                _ => ClosePane(pane), _ => _layout.PaneCount > 1);

            pane.MoveTargets.Clear();
            for (var other = 0; other < ids.Count; other++)
            {
                if (other == index) continue;
                var target = ids[other];
                pane.MoveTargets.Add(new PaneMoveTarget(
                    Ui.Format(Ui.Shell.PaneNumberFormat, other + 1),
                    [
                        new PaneMoveOption(Ui.Shell.SwapWithPane, SwapCommand(id, target)),
                        new PaneMoveOption(
                            Ui.Shell.MoveLeftOfPane, MoveCommand(id, target, WorkspaceDockEdge.Left)),
                        new PaneMoveOption(
                            Ui.Shell.MoveAbovePane, MoveCommand(id, target, WorkspaceDockEdge.Top)),
                        new PaneMoveOption(
                            Ui.Shell.MoveRightOfPane, MoveCommand(id, target, WorkspaceDockEdge.Right)),
                        new PaneMoveOption(
                            Ui.Shell.MoveBelowPane, MoveCommand(id, target, WorkspaceDockEdge.Bottom))
                    ]));
            }
        }
    }

    /// <summary>
    /// Splits a pane, putting a new one on the given edge of it.
    /// </summary>
    /// <remarks>
    /// Refused at four, which is what <see cref="WorkspaceLayoutModel.MaximumPanes"/> says and not
    /// a limit invented here. Four panes on one screen is already past what most windows have the
    /// width for; a fifth would be a listing nobody can read.
    /// </remarks>
    private RelayCommand SplitCommand(Guid paneId, WorkspaceDockEdge edge) => new(
        _ =>
        {
            if (!_layout.Split(paneId, edge, Guid.NewGuid())) return;
            AdoptLayout();
        },
        _ => _layout.PaneCount < WorkspaceLayoutModel.MaximumPanes);

    private RelayCommand SwapCommand(Guid moving, Guid target) => new(_ =>
    {
        if (_layout.Swap(moving, target)) AdoptLayout();
    });

    private RelayCommand MoveCommand(Guid moving, Guid target, WorkspaceDockEdge edge) => new(_ =>
    {
        if (_layout.MoveBeside(moving, target, edge)) AdoptLayout();
    });

    /// <summary>Takes up a tree that changed shape, and works out which preset it now matches.</summary>
    /// <remarks>
    /// An arrangement reached by splitting need not be one of the six -- splitting the right pane
    /// of a side-by-side gives the three-pane preset, but splitting it again does not give the
    /// grid. The preset is therefore what the tree happens to match, or the last one if it matches
    /// none, and the panes are what the tree says either way.
    /// </remarks>
    private void AdoptLayout()
    {
        _preset = WorkspacePreset.Find(_layout.PaneCount, _preset.Layout) ?? _preset;
        Rebuild();
        Raise(nameof(Preset));
    }

    /// <summary>
    /// Closes a pane the new arrangement has no room for, without making the change wait.
    /// </summary>
    /// <remarks>
    /// Switching from four panes to two should redraw at once; what is left is closing a socket,
    /// and a person who just pressed a layout button has no reason to watch that happen.
    /// </remarks>
    private static async Task CloseAsync(BrowserPaneModel pane) =>
        await pane.DisposeAsync().ConfigureAwait(false);

    private BrowserPaneModel NewPane()
    {
        var pane = _paneFactory();
        pane.PropertyChanged += OnPaneChanged;
        pane.SelectedRows.CollectionChanged += (_, _) => RaiseCommands();

        // Every pane shows the same three buttons. Which pane they act on is decided when one of
        // them runs, from whichever pane is active - not from which button was pressed, because
        // pressing a button in a pane is one of the ways to make it the active one.
        pane.CopyCommand = StageCopyCommand;
        pane.MoveCommand = StageMoveCommand;
        pane.PasteCommand = PasteCommand;
        return pane;
    }

    private async Task<bool> ConfirmAsync(
        PaneClipboard clipboard,
        BrowserPaneModel destination,
        CancellationToken cancellationToken)
    {
        if (_dialogs is null) return true;

        var operation = clipboard.IsMove
            ? Ui.Dialogs.TransferOperationMove
            : Ui.Dialogs.TransferOperationCopy;
        var choice = await _dialogs.ConfirmAsync(
            new DialogRequest
            {
                Title = Ui.Format(Ui.Dialogs.ReviewTransferCaptionFormat, operation.ToLowerInvariant()),
                Message = Ui.Format(
                    Ui.Dialogs.ReviewTransferBodyFormat,
                    operation,
                    clipboard.ItemSummary,
                    clipboard.SourceName,
                    destination.Title,
                    clipboard.IsMove
                        ? Ui.Dialogs.TransferOriginalsRemoved
                        : Ui.Dialogs.TransferOriginalsRemain),
                Severity = clipboard.IsMove ? DialogSeverity.Warning : DialogSeverity.Question,
                Buttons = DialogButtons.OkCancel
            },
            cancellationToken).ConfigureAwait(true);
        return choice == DialogChoice.Ok;
    }

    /// <summary>
    /// Clicking into one pane makes every other one inactive.
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
            foreach (var other in Panes)
            {
                if (!ReferenceEquals(other, activated)) other.IsActive = false;
            }
        }

        RaiseCommands();
    }

    private void RaiseCommands()
    {
        (StageCopyCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (StageMoveCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (PasteCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ClearClipboardCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ClosePaneCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
