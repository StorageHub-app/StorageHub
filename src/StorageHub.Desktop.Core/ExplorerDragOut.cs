using System.Security.Cryptography;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// Dragging a saved connection's rows out to File Explorer, as 1.x's <c>BrowserPaneControl</c> did.
/// </summary>
/// <remarks>
/// <para>
/// Explorer only takes paths, and a remote file has none until it has been downloaded, which is
/// minutes too late for a drag. So the drag carries an empty marker folder instead. Explorer
/// copies it to wherever it is dropped, the drop broker (the copy hook registered at launch) sees
/// the copy, writes down the destination and refuses it, and the agent then downloads the real
/// selection there as ordinary queued transfers.
/// </para>
/// <para>
/// The marker has to be on the drag before the drag starts, and waiting for the agent first loses
/// the mouse gesture, so it is made here and registered with the agent while the drag is under
/// way. The agent derives the marker's path itself and looks at the sources again when the drop
/// is committed, so the token handed to it only says which drag is which.
/// </para>
/// <para>
/// Each drag is a row on the queue's Active tab from the moment it starts ("Waiting for
/// destination"), through <see cref="PendingDropRegistry"/>, until the agent takes it or it comes
/// to nothing. A drag that lands on a StorageHub pane instead is that pane's transfer, and its
/// row is taken away rather than left reading as cancelled.
/// </para>
/// </remarks>
internal sealed class ExplorerDragOut
{
    /// <summary>How long the agent has to answer each half of the handshake, as in 1.x.</summary>
    internal static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);

    private readonly Func<ITransferQueueAgentClient> _agent;
    private readonly PendingDropRegistry? _drops;
    private readonly Func<bool> _brokerRegistered;
    private readonly string _markerRoot;

    /// <param name="agent">A client per drag, closed once the drag has settled.</param>
    /// <param name="drops">Where the drag shows while it waits; null shows nothing.</param>
    /// <param name="brokerRegistered">
    /// Whether the copy hook was registered at launch. Without it Explorer would copy the empty
    /// marker folder for real, so a drag carries nothing for Explorer and says why once it ends.
    /// </param>
    /// <param name="markerRoot">Where markers are made; the agent's own folder unless a test says.</param>
    internal ExplorerDragOut(
        Func<ITransferQueueAgentClient> agent,
        PendingDropRegistry? drops,
        Func<bool> brokerRegistered,
        string? markerRoot = null)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _drops = drops;
        _brokerRegistered = brokerRegistered ?? throw new ArgumentNullException(nameof(brokerRegistered));
        _markerRoot = markerRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StorageHub",
            "DragMarkers");
    }

    /// <summary>
    /// Whether the broker was registered when StorageHub started. Set once, by the boot; read at
    /// each drag, so a shell built before the boot got that far still sees it.
    /// </summary>
    internal static bool BrokerRegistered { get; set; }

    /// <summary>
    /// Readies a drag of these rows. Nothing for Explorer when they are not a saved connection's:
    /// This PC's rows are dragged as their own paths.
    /// </summary>
    internal ExplorerDrag Start(PaneSelectionSnapshot selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.Context.Kind != PaneTransferContextKind.SavedConnection) return ExplorerDrag.Nothing;

        // There is no Explorer, and no copy hook, anywhere else. A file manager on Linux takes
        // file URIs and nothing that could stand in for a file not yet downloaded.
        if (!OperatingSystem.IsWindows()) return ExplorerDrag.Unavailable(Ui.Pane.DragOutNeedsExplorer);
        if (!_brokerRegistered()) return ExplorerDrag.Unavailable(Ui.Shell.ExplorerIntegrationUnavailable);
        if (selection.Context.ConnectionId is not { } connectionId ||
            string.IsNullOrWhiteSpace(selection.Context.RootIdentity))
        {
            return ExplorerDrag.Unavailable(Ui.Shell.ExplorerExportRequiresConnection);
        }

        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var marker = StageMarker(token);
        if (marker is null) return ExplorerDrag.Refused(Ui.Pane.DropCouldNotInitialize);

        _drops?.Begin(token, PendingGathering.DescribeSource(selection), selection.Items.Count);
        var sources = selection.Items.Select(item => new ShellExportSource(
            new TransferQueueAddress(
                connectionId,
                selection.Context.RootIdentity,
                item.RelativePath,
                item.NativeItemId,
                item.VersionId,
                item.EntityTag),
            item.IsContainer,
            item.Name)).ToArray();
        return new ExplorerDrag(this, token, marker, sources);
    }

    /// <summary>
    /// Makes the empty folder Explorer will appear to copy. It carries nothing: the copy hook
    /// refuses the copy and the agent does the real transfer.
    /// </summary>
    private string? StageMarker(string token)
    {
        try
        {
            var staged = Path.Combine(_markerRoot, "StorageHubDrop-" + token);
            Directory.CreateDirectory(staged);
            File.WriteAllText(Path.Combine(staged, ".storagehub-drop"), token);
            return staged;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    internal static void DiscardMarker(string? markerPath)
    {
        if (string.IsNullOrWhiteSpace(markerPath)) return;
        try
        {
            if (Directory.Exists(markerPath)) Directory.Delete(markerPath, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The agent purges abandoned markers on its own timer, so this is best effort.
        }
    }

    internal ITransferQueueAgentClient OpenAgent() => _agent();

    internal PendingDropRegistry? Drops => _drops;
}

/// <summary>One drag of a connection's rows, from the moment it starts until it has settled.</summary>
internal sealed class ExplorerDrag
{
    internal static readonly ExplorerDrag Nothing = new(null, null);

    private readonly ExplorerDragOut? _owner;
    private readonly string? _token;
    private readonly ITransferQueueAgentClient? _agent;
    private readonly Task<ExplorerDropBeginResponse>? _registration;
    private bool _finished;

    private ExplorerDrag(string? unavailable, string? refusal)
    {
        UnavailableReason = unavailable;
        Refusal = refusal;
    }

    internal ExplorerDrag(ExplorerDragOut owner, string token, string markerPath, ShellExportSource[] sources)
    {
        _owner = owner;
        _token = token;
        MarkerPath = markerPath;
        try
        {
            _agent = owner.OpenAgent();
            _registration = RegisterAsync(_agent, sources, token);
        }
        catch (Exception error) when (IsAgentFailure(error))
        {
            _registration = Task.FromException<ExplorerDropBeginResponse>(error);
        }
    }

    private static async Task<ExplorerDropBeginResponse> RegisterAsync(
        ITransferQueueAgentClient agent,
        ShellExportSource[] sources,
        string token)
    {
        using var timeout = new CancellationTokenSource(ExplorerDragOut.HandshakeTimeout);
        return await agent.BeginExplorerDropAsync(
            new ExplorerDropBeginRequest(ShellTransferIpcContract.CurrentVersion, sources, token),
            timeout.Token).ConfigureAwait(false);
    }

    internal static ExplorerDrag Unavailable(string reason) => new(reason, null);

    internal static ExplorerDrag Refused(string reason) => new(null, reason);

    /// <summary>The folder the drag carries for Explorer, or null when it carries nothing for it.</summary>
    internal string? MarkerPath { get; }

    /// <summary>Why the drag should not start at all, said in the pane; null when it can.</summary>
    internal string? Refusal { get; }

    /// <summary>
    /// Why nothing can land outside StorageHub, said once the drag is over if it did not land on a
    /// pane, as 1.x said it.
    /// </summary>
    internal string? UnavailableReason { get; }

    /// <summary>The drag could not be started, so the marker is spent.</summary>
    /// <returns>What the pane says.</returns>
    internal async Task<string> AbandonAsync(string error)
    {
        _finished = true;
        ExplorerDragOut.DiscardMarker(MarkerPath);
        if (_token is not null) _owner?.Drops?.MarkFailed(_token, Ui.Pane.DragCouldNotStart);
        await CloseAsync().ConfigureAwait(false);
        return Ui.Format(Ui.Pane.DragCouldNotStartFormat, error);
    }

    /// <summary>
    /// Settles the drag once it has ended: the agent is asked where Explorer put the marker, and
    /// queues the download there.
    /// </summary>
    /// <param name="landedInStorageHub">Whether a StorageHub pane took the drop.</param>
    /// <returns>What the pane says about it, or null for nothing.</returns>
    internal async Task<string?> FinishAsync(bool landedInStorageHub)
    {
        if (_finished) return null;
        _finished = true;
        if (_token is null || _registration is null)
        {
            return landedInStorageHub ? null : UnavailableReason;
        }

        var drops = _owner!.Drops;
        if (landedInStorageHub)
        {
            // Another pane took it and posts a row of its own, so the marker never became work.
            drops?.Discard(_token);
        }

        try
        {
            var registered = await _registration.ConfigureAwait(true);
            if (registered.Failure is not null || string.IsNullOrWhiteSpace(registered.DropToken))
            {
                ExplorerDragOut.DiscardMarker(MarkerPath);
                drops?.MarkFailed(_token, registered.Failure?.Message ?? Ui.Pane.DropNotInitialized);
                return landedInStorageHub ? null : registered.Failure?.Message ?? Ui.Pane.DropCouldNotInitialize;
            }

            using var commitTimeout = new CancellationTokenSource(ExplorerDragOut.HandshakeTimeout);
            var committed = await _agent!.CommitExplorerDropAsync(
                new ExplorerDropCommitRequest(ShellTransferIpcContract.CurrentVersion, _token),
                commitTimeout.Token).ConfigureAwait(true);
            if (committed.Accepted)
            {
                // The agent owns it now; the transfers it queues take over from this row.
                drops?.MarkQueued(_token, committed.DestinationPath);
                return landedInStorageHub ? null : Ui.Format(Ui.Pane.QueuedToFormat, committed.DestinationPath);
            }

            drops?.MarkCancelled(_token, committed.Failure?.Message ?? Ui.Pane.NoDestinationReported);
            return null;
        }
        catch (Exception error) when (IsAgentFailure(error))
        {
            drops?.MarkFailed(_token, error.Message);
            return landedInStorageHub ? null : Ui.Format(Ui.Pane.DropCouldNotQueueFormat, error.Message);
        }
        finally
        {
            await CloseAsync().ConfigureAwait(true);
        }
    }

    private async Task CloseAsync()
    {
        if (_agent is not null) await _agent.DisposeAsync().ConfigureAwait(false);
    }

    private static bool IsAgentFailure(Exception error) =>
        error is IOException or UnauthorizedAccessException or InvalidDataException or
            InvalidOperationException or TimeoutException or System.Text.Json.JsonException or
            OperationCanceledException or NotSupportedException or ArgumentException;
}
