using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using StorageHub.Contracts.Ipc;
using StorageHub.Contracts.Results;
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
    private string _name;
    private string? _filePath;
    private bool _isDirty = true;

    /// <summary>What the file said when it was last saved or opened, or null if it never was.</summary>
    private string? _saved;

    /// <summary>Set while a file is being put back, so the panes arriving do not count as changes.</summary>
    private bool _restoring;

    /// <summary>Cancelled when the workspace closes, so a file still being opened stops with it.</summary>
    private readonly CancellationTokenSource _lifetime = new();

    /// <param name="paneFactory">
    /// How a new pane is built. The workspace makes and drops panes as the arrangement changes, so
    /// it needs to be able to produce one rather than be handed a fixed set.
    /// </param>
    /// <param name="dialogs">
    /// Where the paste confirmation and the "Transfer queue" warning for a refused paste or drop
    /// go. Null runs transfers unconfirmed and says a refusal in <see cref="Message"/> instead,
    /// which is what a headless test wants; the shell always passes one.
    /// </param>
    /// <param name="name">What the tab says. The shell numbers new workspaces; a file names its own.</param>
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
        IDialogService? dialogs = null,
        string? name = null)
    {
        _paneFactory = paneFactory ?? throw new ArgumentNullException(nameof(paneFactory));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _mutations = mutations ?? throw new ArgumentNullException(nameof(mutations));
        _queueChanged = queueChanged;
        _dialogs = dialogs;
        _name = string.IsNullOrWhiteSpace(name) ? Ui.Format(Ui.Shell.WorkspaceTabFormat, 1) : name.Trim();

        _preset = preset ?? WorkspacePreset.All[1];
        _layout = WorkspaceLayoutModel.CreatePreset(_preset.PaneCount, _preset.Layout);

        StageCopyCommand = new RelayCommand(
            _ => Stage(TransferQueueOperation.Copy), _ => CanStage(TransferQueueOperation.Copy));
        StageMoveCommand = new RelayCommand(
            _ => Stage(TransferQueueOperation.Move), _ => CanStage(TransferQueueOperation.Move));
        PasteCommand = new RelayCommand(_ => _ = PasteAsync(), _ => CanPaste);
        ClearClipboardCommand = new RelayCommand(_ => ClearClipboard(), _ => _clipboard is not null);
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

    /// <summary>
    /// Raised with a sentence for the status bar: what was just staged, that it was cleared, or
    /// what a paste or drop came to (<see cref="Message"/>).
    /// </summary>
    /// <remarks>
    /// An event rather than a property, so staging three items twice says so twice.
    /// </remarks>
    internal event EventHandler<string>? Announced;

    /// <summary>The six arrangements offered: one, two either way, three either way, and a grid.</summary>
    /// <remarks>
    /// The list itself is in Desktop.Core, so the chooser offers what a saved workspace can hold.
    /// </remarks>
    public static IReadOnlyList<WorkspacePreset> Presets => WorkspacePreset.All;

    public static string ClearStagedLabel => Ui.Pane.ClearStaged;

    /// <summary>What is staged, or "Empty", as the strip above 1.x's panes always said.</summary>
    public string ClipboardText => _clipboard?.Describe() ?? Ui.Shell.ClipboardEmpty;

    public static string DragPaneHeadersHint => Ui.Shell.DragPaneHeadersHint;

    public static string PasteToActivePaneLabel => Ui.Shell.PasteToActivePane;

    public static string ClearLabel => Ui.Shell.ClipboardClear;

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

    /// <summary>
    /// Where a folder being read for a transfer is shown until its files are queued, which is how
    /// the queue shows it and stops it. Null leaves the reading unseen, as a workspace with no
    /// queue beside it wants.
    /// </summary>
    internal PendingDropRegistry? PendingDrops { get; init; }

    /// <summary>
    /// What lets a connection's rows be dragged out to File Explorer through the drop broker, as
    /// 1.x's could. Null drags nothing out, as a workspace with no agent behind it wants.
    /// </summary>
    internal ExplorerDragOut? DragOut { get; init; }

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
            Raise(nameof(ClipboardText));
            RaiseCommands();
        }
    }

    public bool HasClipboard => _clipboard is not null;

    /// <summary>"Ready to copy 3 items from Studio Assets", or nothing.</summary>
    public string ClipboardSummary => _clipboard?.Describe() ?? string.Empty;

    /// <summary>What the last transfer attempt said, or nothing.</summary>
    /// <remarks>
    /// Said in the status bar as well, each time, even when it is what was said last: pasting twice
    /// queues twice. Nothing else shows it. A refusal leaves this empty and is shown in a warning
    /// instead (<see cref="RefuseAsync"/>), unless there is nowhere to show one.
    /// </remarks>
    public string Message
    {
        get => _message;
        private set
        {
            if (!string.IsNullOrEmpty(value)) Announced?.Invoke(this, value);
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

    /// <summary>What the tab says, and what a saved file is called inside.</summary>
    public string Name => _name;

    /// <summary>
    /// Whether somebody chose the name: by renaming, or by opening a file that carried one. A
    /// workspace still on its generated "Workspace 1" has not, and saving names it after the file.
    /// </summary>
    internal bool HasExplicitName { get; private set; }

    /// <summary>The file this workspace was saved to or opened from, or null if neither.</summary>
    public string? FilePath
    {
        get => _filePath;
        private set
        {
            if (string.Equals(_filePath, value, StringComparison.Ordinal)) return;
            _filePath = value;
            Raise(nameof(FilePath));
        }
    }

    /// <summary>
    /// Whether the workspace differs from its file: the "*" on the tab, and what is asked about on
    /// close. Always true for one that was never saved, as it was in 1.x.
    /// </summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (_isDirty == value) return;
            _isDirty = value;
            Raise(nameof(IsDirty));
        }
    }

    /// <summary>Gives the workspace a name somebody chose, as Workspace > Rename does.</summary>
    internal void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        name = name.Trim();
        HasExplicitName = true;
        if (string.Equals(name, _name, StringComparison.Ordinal)) return;
        _name = name;
        Raise(nameof(Name));
        Recheck();
    }

    /// <summary>
    /// The workspace as a <c>.shw</c> file holds it: the name, the arrangement, the active pane and
    /// each pane's state, keyed by the pane's leaf.
    /// </summary>
    internal WorkspaceFileDocument CaptureDocument() => WorkspaceFileStore.Capture(
        _name,
        _byId.FirstOrDefault(pair => ReferenceEquals(pair.Value, Active)).Key,
        _layout.Root,
        _layout.PaneIds.ToDictionary(static id => id, id => _byId[id].CaptureState()));

    /// <summary>
    /// Writes the workspace to a file, and makes that file the one it is compared against.
    /// </summary>
    /// <remarks>
    /// Choosing a file name names the workspace, unless somebody already named it -- done before
    /// the capture, so the new name is what lands in the file. The path is the file's own after
    /// the store forces the <c>.shw</c> extension, which is the one to remember.
    /// </remarks>
    internal void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // Halfway through opening, a pane still connecting has no folder yet, and the file would
        // lose it and then look saved. The caller waits for Opening instead.
        if (_restoring) throw new InvalidOperationException("The workspace is still being opened.");

        var candidate = System.IO.Path.GetFileNameWithoutExtension(path).Trim();
        if (!HasExplicitName && candidate.Length > 0)
        {
            _name = candidate.Length > MaximumNameLength ? candidate[..MaximumNameLength] : candidate;
            HasExplicitName = true;
            Raise(nameof(Name));
        }

        var document = CaptureDocument();
        WorkspaceFileStore.Save(path, document);
        FilePath = System.IO.Path.GetFullPath(System.IO.Path.ChangeExtension(path, ".shw"));
        _saved = WorkspaceFileStore.Fingerprint(document);
        Recheck();
    }

    /// <summary>
    /// Takes up a workspace read from a file: its arrangement, its name, and every pane where it
    /// was.
    /// </summary>
    /// <remarks>
    /// The restore is kept in <see cref="Opening"/>, so a save or a close that comes while a pane
    /// is still connecting can wait for it rather than capture it halfway.
    /// </remarks>
    internal Task OpenAsync(
        WorkspaceFileDocument document,
        string path,
        bool reconnectRemote,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Opening = TakeUpAsync(document, path, reconnectRemote, cancellationToken);
    }

    /// <summary>
    /// The file being opened, until every pane is back where it was; complete when there is none.
    /// </summary>
    internal Task Opening { get; private set; } = Task.CompletedTask;

    /// <summary>The restore itself, which <see cref="OpenAsync"/> keeps hold of.</summary>
    /// <remarks>
    /// <para>
    /// The panes this workspace already made are reused for the file's leaves, as switching
    /// arrangement does, so only a shortfall is built. They are restored together rather than one
    /// after another, since each waits on its own connection. The tab shows no "*" meanwhile:
    /// what it shows is the file.
    /// </para>
    /// <para>
    /// The baseline for the "*" is the file as each pane took it up, captured the moment that
    /// pane was back, rather than the whole workspace once the slowest one is. A connection
    /// renamed since the file was written is not a change anybody made; a filter typed into a
    /// pane that was back early, or a rename while another pane connected, is.
    /// </para>
    /// </remarks>
    private async Task TakeUpAsync(
        WorkspaceFileDocument document,
        string path,
        bool reconnectRemote,
        CancellationToken cancellationToken)
    {
        var layout = new WorkspaceLayoutModel(WorkspaceFileStore.ToLayout(document.Layout));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var name = document.Name.Trim();
        BrowserPaneModel active;
        (Guid Id, BrowserPaneState State)[] restored;
        _restoring = true;
        try
        {
            _name = name;
            HasExplicitName = true;

            // Set before anything is awaited, so opening the same file again meanwhile finds
            // this tab rather than making a second one.
            FilePath = System.IO.Path.GetFullPath(path);
            IsDirty = false;
            Raise(nameof(Name));

            _layout = layout;
            var orientation = layout.Root is WorkspaceSplitNode { Orientation: WorkspaceSplitOrientation.Horizontal }
                ? WorkspaceLayout.TopAndBottom
                : WorkspaceLayout.SideBySide;
            _preset = WorkspacePreset.Find(layout.PaneCount, orientation) ?? _preset;
            Rebuild();
            Raise(nameof(Preset));

            restored = await Task.WhenAll(layout.PaneIds.Select(async id =>
            {
                var pane = _byId[id];
                await pane.RestoreAsync(document.Panes[id], reconnectRemote, linked.Token).ConfigureAwait(true);
                return (id, pane.CaptureState());
            })).ConfigureAwait(true);
            linked.Token.ThrowIfCancellationRequested();

            active = _byId.GetValueOrDefault(document.ActivePaneId) ?? Panes[0];
            active.IsActive = true;
        }
        finally
        {
            _restoring = false;
        }

        _saved = WorkspaceFileStore.Fingerprint(WorkspaceFileStore.Capture(
            name,
            _byId.FirstOrDefault(pair => ReferenceEquals(pair.Value, active)).Key,
            layout.Root,
            restored.ToDictionary(static pane => pane.Id, static pane => pane.State)));
        Recheck();
    }

    /// <summary>The longest name a file can carry, which is what 1.x's rename box allowed.</summary>
    internal const int MaximumNameLength = 128;

    /// <summary>
    /// Compares the workspace with what was last saved, and moves the "*" to match.
    /// </summary>
    /// <remarks>
    /// Compared rather than flagged on every change, so changing a filter and changing it back
    /// leaves nothing to save, and a listing that reloads without moving is not a change either.
    /// </remarks>
    private void Recheck()
    {
        if (_restoring) return;
        IsDirty = _saved is null ||
            !string.Equals(_saved, WorkspaceFileStore.Fingerprint(CaptureDocument()), StringComparison.Ordinal);
    }

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
        Panes.Count > 0 && CanStageFrom(Active, operation);

    private static bool CanStageFrom(BrowserPaneModel pane, TransferQueueOperation operation) =>
        pane is { IsTerminal: false, Source: not null } &&
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
            _ = RefuseAsync(selection.Error.Message, CancellationToken.None);
            return;
        }

        Message = string.Empty;
        var staged = new PaneClipboard(selection.Value, operation, pane.Title);
        Clipboard = staged;
        Announced?.Invoke(this, Ui.Format(
            Ui.Shell.StagedSelectionFormat,
            staged.IsMove ? Ui.Shell.StagedCut : Ui.Shell.StagedCopied,
            staged.Count));
    }

    /// <summary>Drops what is staged, and says so, as 1.x's Clear did.</summary>
    private void ClearClipboard()
    {
        Clipboard = null;
        Announced?.Invoke(this, Ui.Shell.StatusClipboardCleared);
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
        if (await TransferAsync(clipboard, Active, cancellationToken).ConfigureAwait(true) && clipboard.IsMove)
        {
            // A move is spent once it is queued; a copy can reasonably be pasted into a second
            // destination, which is most of the point of staging it separately.
            Clipboard = null;
        }
    }

    /// <summary>
    /// Queues what was dropped on a pane, into that pane.
    /// </summary>
    /// <remarks>
    /// The same transfer a paste is -- same snapshots, same confirmation, same queue -- with the
    /// destination named by where the pointer let go rather than by which pane is active. What is
    /// staged for a paste is left alone: dragging one thing does not lose another.
    /// </remarks>
    internal Task DropAsync(
        PaneClipboard clipboard,
        BrowserPaneModel destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(destination);
        return TransferAsync(clipboard, destination, cancellationToken);
    }

    /// <summary>
    /// Brings files dropped from Explorer, Nautilus or Dolphin into a pane.
    /// </summary>
    /// <remarks>
    /// Into a saved connection they go as 1.x's did: the agent reads what was dropped, folders
    /// and all, says which files are already there, and only then is anything queued, after
    /// asking: Replace, Skip or Cancel when some are there, OK or Cancel when none are. Into a
    /// This PC folder, which 1.x refused, they are queued as a paste from This PC would be.
    /// </remarks>
    /// <param name="folder">
    /// The folder they were dropped on in the pane's tree, as 1.x took it; null for where the pane is.
    /// </param>
    internal async Task DropFilesAsync(
        IReadOnlyList<string> paths,
        BrowserPaneModel destination,
        string? folder = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(destination);
        if (paths.Count == 0) return;

        if (PaneTransferSnapshots.ContextFor(destination.Source) is
            {
                IsSuccess: true,
                Value: { Kind: PaneTransferContextKind.SavedConnection, ConnectionId: { } id } here
            } &&
            !string.IsNullOrWhiteSpace(here.RootIdentity))
        {
            await ImportAsync(
                [.. paths],
                new TransferQueueAddress(id, here.RootIdentity, folder ?? here.RelativePath),
                cancellationToken).ConfigureAwait(true);
            return;
        }

        var selections = LocalDrops.From(paths);
        if (selections.IsFailure)
        {
            await RefuseAsync(selections.Error.Message, cancellationToken).ConfigureAwait(true);
            return;
        }

        foreach (var selection in selections.Value)
        {
            await TransferAsync(
                new PaneClipboard(selection, TransferQueueOperation.Copy, Ui.Pane.ThisPc),
                destination,
                cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// 1.x's <c>ReviewShellImportAsync</c>: the agent's plan, the question, and the commit.
    /// </summary>
    /// <remarks>
    /// A client for each call rather than one held across the question, so nothing is held open
    /// while a dialog is up; the plan is the agent's, kept by its token for five minutes. Cancel is
    /// committed too, so the agent lets the plan go at once. With no dialogs, as in a headless
    /// test, nothing already there is replaced.
    /// </remarks>
    private async Task ImportAsync(
        string[] paths,
        TransferQueueAddress destination,
        CancellationToken cancellationToken)
    {
        try
        {
            ShellImportPlanResponse plan;
            await using (var agent = _queue())
            {
                plan = await agent.PlanShellImportAsync(
                    new ShellImportPlanRequest(ShellTransferIpcContract.CurrentVersion, paths, destination),
                    cancellationToken).ConfigureAwait(true);
            }

            if (plan.Failure is not null || string.IsNullOrWhiteSpace(plan.ReviewToken))
            {
                await RefuseAsync(plan.Failure?.Message ?? Ui.Shell.CouldNotReviewDrop, cancellationToken)
                    .ConfigureAwait(true);
                return;
            }

            var conflicts = plan.Items.Count(static item => item.DestinationConflict);
            var choice = await AskImportAsync(plan.Items.Length, conflicts, cancellationToken).ConfigureAwait(true);

            ShellImportCommitResponse committed;
            await using (var agent = _queue())
            {
                committed = await agent.CommitShellImportAsync(
                    new ShellImportCommitRequest(ShellTransferIpcContract.CurrentVersion, plan.ReviewToken, choice),
                    cancellationToken).ConfigureAwait(true);
            }

            if (committed.Failure is not null)
            {
                await RefuseAsync(committed.Failure.Message, cancellationToken).ConfigureAwait(true);
            }
            else if (committed.Accepted)
            {
                Message = Ui.Format(Ui.Shell.QueuedExplorerImportFormat, committed.TransferIds.Length);
                if (_queueChanged is not null) await _queueChanged().ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested || _lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidDataException or InvalidOperationException or TimeoutException or
            System.Text.Json.JsonException or NotSupportedException or ArgumentException or
            ObjectDisposedException)
        {
            await RefuseAsync(Ui.Shell.AgentCannotReviewDrop, cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>1.x's two questions: OK or Cancel with nothing in the way, Yes, No or Cancel with.</summary>
    private async Task<ShellImportDisposition> AskImportAsync(
        int items,
        int conflicts,
        CancellationToken cancellationToken)
    {
        if (_dialogs is null)
        {
            return conflicts == 0 ? ShellImportDisposition.ReplaceFiles : ShellImportDisposition.SkipConflictingFiles;
        }

        if (conflicts == 0)
        {
            var go = await _dialogs.ConfirmAsync(
                new DialogRequest
                {
                    Title = Ui.Dialogs.ImportFromExplorerCaption,
                    Message = Ui.Format(Ui.Dialogs.ImportFromExplorerPromptFormat, items),
                    Severity = DialogSeverity.Question,
                    Buttons = DialogButtons.OkCancel
                },
                cancellationToken).ConfigureAwait(true);
            return go == DialogChoice.Ok ? ShellImportDisposition.ReplaceFiles : ShellImportDisposition.Cancel;
        }

        var choice = await _dialogs.ConfirmAsync(
            new DialogRequest
            {
                Title = Ui.Dialogs.ImportConflictsCaption,
                Message = Ui.Format(Ui.Dialogs.ImportConflictsPromptFormat, conflicts),
                Severity = DialogSeverity.Warning,
                Buttons = DialogButtons.YesNoCancel
            },
            cancellationToken).ConfigureAwait(true);
        return choice switch
        {
            DialogChoice.Yes => ShellImportDisposition.ReplaceFiles,
            DialogChoice.No => ShellImportDisposition.SkipConflictingFiles,
            _ => ShellImportDisposition.Cancel
        };
    }

    /// <summary>Queues a selection into a pane, after confirming it. True when it was queued.</summary>
    private async Task<bool> TransferAsync(
        PaneClipboard clipboard,
        BrowserPaneModel destination,
        CancellationToken cancellationToken)
    {
        if (!await ConfirmAsync(clipboard, destination, cancellationToken).ConfigureAwait(true))
        {
            return false;
        }

        // A folder cannot be queued until it has been read, and reading a big tree takes minutes.
        // From here the queue's Active tab and its log show a row for the reading, as 1.x's did,
        // the destination's own reading below included, since that is part of the same wait.
        // Cancel on it stops the reading. Plain files are queued in one go and need none.
        using var gathering = PendingDrops is { } drops &&
            clipboard.Selection.Items.Any(static item => item.IsContainer)
                ? PendingGathering.Begin(
                    drops,
                    clipboard.Selection,
                    PaneTransferSnapshots.ContextFor(destination.Source) is { IsSuccess: true } location
                        ? location.Value.RelativePath
                        : null)
                : null;

        // The whole destination, as 1.x read it: a folder bigger than one page used to be refused
        // outright ("finish indexing first"), and nothing ever finished it. One that could not be
        // read to the end is refused in 1.x's words.
        if (!await destination.LoadAllAsync(cancellationToken).ConfigureAwait(true))
        {
            gathering?.Fail(Ui.Shell.CouldNotFinishIndexing);
            await RefuseAsync(Ui.Shell.CouldNotFinishIndexing, cancellationToken).ConfigureAwait(true);
            return false;
        }

        var target = PaneTransferSnapshots.DestinationFor(
            destination.Source, destination.AllRows, destination.HasMorePages);
        if (target.IsFailure)
        {
            gathering?.Fail(target.Error.Message);
            await RefuseAsync(target.Error.Message, cancellationToken).ConfigureAwait(true);
            return false;
        }

        ManualTransferEnqueueResult? result;
        try
        {
            result = await EnqueueAsync(clipboard, target.Value, gathering, cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        if (result is null)
        {
            gathering?.Fail(Ui.Shell.AgentCannotEnqueue);
            await RefuseAsync(Ui.Shell.AgentCannotEnqueue, cancellationToken).ConfigureAwait(true);
            return false;
        }

        gathering?.Settle(result.Failure);
        if (result.Failure is { } failure)
        {
            // A folder read stopped from the queue is what somebody asked for rather than a
            // failure, so it is only said, as 1.x only said it, and in 1.x's words: how many were
            // queued before it, when some were, then that the reading was stopped.
            var refusal = RefusalFor(result, clipboard.Selection.Items.Count);
            if (gathering is not null && failure.Kind == StorageFailureKind.Cancelled && !result.HasAmbiguity)
            {
                Message = refusal;
            }
            else
            {
                await RefuseAsync(refusal, cancellationToken).ConfigureAwait(true);
            }

            return false;
        }

        Message = Ui.Format(Ui.Transfer.QueuedFormat, result.Accepted.Count);
        if (_queueChanged is not null)
        {
            await _queueChanged().ConfigureAwait(true);
        }

        return true;
    }

    /// <summary>
    /// Queues a selection through controllers opened for it alone. Null when the agent went away
    /// part way.
    /// </summary>
    /// <remarks>
    /// Its own method so the controllers, and the agent connections behind them, are closed
    /// before anything is shown: a warning left up would otherwise hold them open with it.
    /// </remarks>
    private async Task<ManualTransferEnqueueResult?> EnqueueAsync(
        PaneClipboard clipboard,
        PaneDestinationSnapshot target,
        PendingGathering? gathering,
        CancellationToken cancellationToken)
    {
        // Always the recursive controller, even for a selection of plain files: it delegates to
        // ManualTransferController for those and is the only one that can expand a folder. Choosing
        // between them here would mean this deciding what a container is, which is the storage
        // layer's answer and not the shell's.
        await using var transfers = new ManualTransferController(_queue(), ownsClient: true);
        await using var recursive = new RecursiveTransferController(
            transfers, _storage(), _mutations(), ownsClients: true);
        try
        {
            return await recursive
                .EnqueueAsync(
                    clipboard.Selection,
                    target,
                    clipboard.Operation,
                    progress: gathering is null ? null : gathering.Report,
                    stop: gathering?.Stopped ?? default,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(true);
        }
        catch (Exception error) when (error is IOException or TimeoutException or
            InvalidOperationException or ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Shows why a transfer was refused, in the "Transfer queue" warning 1.x's
    /// <c>ShowManualTransferFailure</c> put up.
    /// </summary>
    /// <remarks>
    /// A warning rather than a sentence in the status bar: a paste or drop that was refused did
    /// nothing anybody could see, and eight seconds in the corner of the window is easy to miss.
    /// It is not said in the bar as well, as 1.x did not say it there. With no dialogs, as in a
    /// headless test, it is said in the bar instead. Once the workspace is closing nothing is
    /// shown or said, as 1.x showed nothing once its window was: a paste still under way when
    /// StorageHub exits fails as the agent goes, and a warning then would open over a window
    /// that is closing.
    /// </remarks>
    private async Task RefuseAsync(string reason, CancellationToken cancellationToken)
    {
        if (_lifetime.IsCancellationRequested) return;
        if (_dialogs is null)
        {
            Message = reason;
            return;
        }

        Message = string.Empty;
        await _dialogs.ShowAsync(
            new DialogRequest
            {
                Title = Ui.Dialogs.TransferQueueCaption,
                Message = reason,
                Severity = DialogSeverity.Warning
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// What a refused enqueue says, in 1.x's words: the failure, after how many were queued before
    /// it when some were, or, when the agent never said whether it took one, which ids to look for.
    /// </summary>
    /// <param name="selected">How many items were pasted or dropped.</param>
    private static string RefusalFor(ManualTransferEnqueueResult result, int selected)
    {
        if (!result.HasAmbiguity)
        {
            var failure = result.Failure?.Message ?? string.Empty;
            return result.IsPartial
                ? Ui.Format(Ui.Shell.PartiallyQueuedFormat, result.Accepted.Count, failure)
                : failure;
        }

        var sentences = new List<string>
        {
            Ui.Format(Ui.Shell.AcknowledgedTransfersFormat, result.Accepted.Count),
            Ui.Format(
                Ui.Shell.AmbiguousTransfersFormat,
                string.Join(", ", result.AmbiguousTransferIds.Select(static id => id.ToString("D"))))
        };
        var unsubmitted = Math.Max(0, selected - result.Accepted.Count - result.AmbiguousTransferIds.Count);
        if (unsubmitted > 0) sentences.Add(Ui.Format(Ui.Shell.UnsubmittedTransfersFormat, unsubmitted));
        sentences.Add(Ui.Shell.CheckQueueForAmbiguousTransfers);
        return string.Join(" ", sentences);
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
    internal void SetRatio(WorkspaceSplitNode node, double ratio)
    {
        _layout.SetRatio(node, ratio);
        Recheck();
    }

    public async ValueTask DisposeAsync()
    {
        // First, so a file still being opened stops putting panes back into a workspace that is
        // letting them go.
        if (!_lifetime.IsCancellationRequested) _lifetime.Cancel();
        _lifetime.Dispose();

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
        Recheck();
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

    /// <summary>
    /// Splits the active pane, putting a new one on the given edge, and returns the new one; null
    /// when the workspace already holds as many panes as it can.
    /// </summary>
    internal BrowserPaneModel? SplitActive(WorkspaceDockEdge edge)
    {
        var active = Panes.FirstOrDefault(static pane => pane.IsActive) ?? Panes.FirstOrDefault();
        var activeId = _byId.FirstOrDefault(pair => ReferenceEquals(pair.Value, active)).Key;
        if (activeId == Guid.Empty || _layout.PaneCount >= WorkspaceLayoutModel.MaximumPanes) return null;

        var newId = Guid.NewGuid();
        if (!_layout.Split(activeId, edge, newId)) return null;
        AdoptLayout();
        return _byId.GetValueOrDefault(newId);
    }

    /// <summary>
    /// Swaps two panes, or docks one on an edge of another: what dragging a pane's header does.
    /// </summary>
    /// <returns>Whether the arrangement changed.</returns>
    internal bool Rearrange(BrowserPaneModel moving, BrowserPaneModel target, WorkspaceDockEdge? edge)
    {
        var movingId = _byId.FirstOrDefault(pair => ReferenceEquals(pair.Value, moving)).Key;
        var targetId = _byId.FirstOrDefault(pair => ReferenceEquals(pair.Value, target)).Key;
        if (movingId == Guid.Empty || targetId == Guid.Empty || movingId == targetId) return false;

        var changed = edge is { } side
            ? _layout.MoveBeside(movingId, targetId, side)
            : _layout.Swap(movingId, targetId);
        if (changed) AdoptLayout();
        return changed;
    }

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

        // Every pane shows the same three buttons, and they act on the pane they are in: pressing
        // one makes that pane the active one, then runs the workspace's own command. They were the
        // workspace's commands themselves, so they were enabled by the active pane's selection,
        // and a pane with a file selected showed Copy dimmed until it had been clicked once.
        pane.CopyCommand = new RelayCommand(
            _ => { pane.IsActive = true; Stage(TransferQueueOperation.Copy); },
            _ => CanStageFrom(pane, TransferQueueOperation.Copy));
        pane.MoveCommand = new RelayCommand(
            _ => { pane.IsActive = true; Stage(TransferQueueOperation.Move); },
            _ => CanStageFrom(pane, TransferQueueOperation.Move));
        pane.PasteCommand = new RelayCommand(
            _ => { pane.IsActive = true; _ = PasteAsync(); },
            _ => _clipboard is not null && pane is { IsTerminal: false, Source: not null });

        // A drop lands in the pane it was dropped on, whichever pane is active, and one that
        // cannot be used is refused as a paste would be.
        pane.DropReceiver = clipboard => DropAsync(clipboard, pane);
        pane.Refused = reason => RefuseAsync(reason, CancellationToken.None);
        pane.Announce = news => Announced?.Invoke(this, news);
        pane.FilesDropReceiver = (paths, folder) => DropFilesAsync(paths, pane, folder);
        pane.DragOut = selection => DragOut?.Start(selection) ?? ExplorerDrag.Nothing;
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

        // What a saved workspace holds about a pane, and so what can make it differ from its file.
        if (e.PropertyName is nameof(BrowserPaneModel.Connection)
            or nameof(BrowserPaneModel.Path)
            or nameof(BrowserPaneModel.ContentKind)
            or nameof(BrowserPaneModel.Filter)
            or nameof(BrowserPaneModel.SortColumn)
            or nameof(BrowserPaneModel.SortAscending)
            or nameof(BrowserPaneModel.ShowConnectionBar)
            or nameof(BrowserPaneModel.ShowFilesBar)
            or nameof(BrowserPaneModel.IsActive))
        {
            Recheck();
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
        foreach (var pane in Panes)
        {
            (pane.CopyCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (pane.MoveCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (pane.PasteCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
