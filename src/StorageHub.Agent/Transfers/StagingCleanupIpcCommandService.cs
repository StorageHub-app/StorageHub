using System.Text.Json;
using StorageHub.Contracts.Ipc;
using StorageHub.Contracts.Results;
using StorageHub.Domain.Identifiers;
using StorageHub.Domain.Storage;
using StorageHub.Ipc;
using StorageHub.Storage.Abstractions;
using StorageHub.Storage.Models;
using StorageHub.Sync;
using StorageHub.Transfers;

namespace StorageHub.Agent.Transfers;

/// <summary>
/// Finds the working files transfers left beside their destinations, and removes the ones somebody
/// chose: partial copies kept to resume, staged copies never renamed into place, and the probes and
/// stand-ins a write or a rename passes through.
/// </summary>
/// <remarks>
/// <para>
/// Where to look is what the queue remembers: the folders its transfers wrote into, on this PC and
/// on saved connections, most recent first. Nothing is searched for beyond them -- a scan of a whole
/// disk or a whole bucket for names that start with a dot would be slow, and would find what is not
/// StorageHub's to remove.
/// </para>
/// <para>
/// A file is in use, and is shown but never removed, when a transfer that has not finished writes
/// into its folder, or when a sync is running at all: a sync's transfers are not in the queue, so
/// there is no telling which folders they write to. A failed transfer has finished for this purpose.
/// It can be retried, and then starts its copy again rather than resuming it, which is the cost of
/// the space somebody asked to have back. The removal looks at all of this again rather than
/// trusting the scan, and a partial on this PC is held the way a write holds it while it goes, so no
/// transfer can start writing it in between.
/// </para>
/// <para>
/// The Local provider's own staging folder and the Explorer export and drop folders are not here:
/// each is already cleared on its own timer by what writes to it.
/// </para>
/// </remarks>
public sealed class StagingCleanupIpcCommandService : IAgentIpcCommandHandler, IDisposable
{
    private const int JobPages = 20;
    private const int FolderListPages = 10;
    private static readonly TimeSpan ScanBudget = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan FolderBudget = TimeSpan.FromSeconds(20);
    private static readonly TransferState[] AllStates = Enum.GetValues<TransferState>();
    private readonly ITransferQueueQueryStore _jobs;
    private readonly ISyncEndpointConnector _connector;
    private readonly Func<int> _runningSyncs;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public StagingCleanupIpcCommandService(
        ITransferQueueQueryStore jobs,
        ISyncEndpointConnector connector,
        Func<int> runningSyncs)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _connector = connector ?? throw new ArgumentNullException(nameof(connector));
        _runningSyncs = runningSyncs ?? throw new ArgumentNullException(nameof(runningSyncs));
    }

    public void Dispose() => _gate.Dispose();

    public bool CanHandle(string messageType) => messageType is
        StagingCleanupIpcMessageTypes.ScanRequest or
        StagingCleanupIpcMessageTypes.RemoveRequest;

    public async ValueTask<AgentIpcCommandResponse> HandleAsync(
        IpcEnvelope request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            switch (request.MessageType)
            {
                case StagingCleanupIpcMessageTypes.ScanRequest:
                    var scan = request.DeserializePayload<StagingCleanupScanRequest>();
                    if (scan is null || !scan.HasValidBounds || !StagingCleanupIpcContract.IsSupported(scan.ContractVersion))
                    {
                        return InvalidRequest(scan?.ContractVersion ?? 0);
                    }

                    return AgentIpcCommandResponse.Create(
                        StagingCleanupIpcMessageTypes.ScanResponse,
                        await ScanAsync(scan.IncludeRemote, cancellationToken).ConfigureAwait(false));
                case StagingCleanupIpcMessageTypes.RemoveRequest:
                    var remove = request.DeserializePayload<StagingCleanupRemoveRequest>();
                    if (remove is null || !remove.HasValidBounds || !StagingCleanupIpcContract.IsSupported(remove.ContractVersion))
                    {
                        return InvalidRequest(remove?.ContractVersion ?? 0);
                    }

                    return AgentIpcCommandResponse.Create(
                        StagingCleanupIpcMessageTypes.RemoveResponse,
                        await RemoveAsync(remove.Items, cancellationToken).ConfigureAwait(false));
                default:
                    return AgentIpcCommandResponse.Error(
                        "ipc.message.unsupported",
                        "The requested IPC operation is not supported by this agent version.");
            }
        }
        catch (JsonException)
        {
            return AgentIpcCommandResponse.Error(
                "ipc.payload.invalid",
                "The leftover cleanup request payload was invalid.");
        }
    }

    /// <summary>Looks in each folder the queue has written to, as far as the time allows.</summary>
    internal async Task<StagingCleanupScanResponse> ScanAsync(bool includeRemote, CancellationToken cancellationToken)
    {
        // One at a time: two scans of the same remote folders would only race each other.
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(ScanBudget);
            var known = await KnownFoldersAsync(cancellationToken).ConfigureAwait(false);
            var syncRunning = _runningSyncs() > 0;
            var items = new List<StagingLeftover>();
            int searched = 0, unreachable = 0;
            var truncated = known.Truncated;
            foreach (var folder in known.Folders)
            {
                if (!includeRemote && folder.LocalPath is null)
                {
                    continue;
                }

                if (budget.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    unreachable++;
                    continue;
                }

                var found = folder.LocalPath is { } local
                    ? FindLocal(local)
                    : await FindRemoteAsync(folder, budget.Token).ConfigureAwait(false);
                if (found is null)
                {
                    unreachable++;
                    continue;
                }

                searched++;
                foreach (var leftover in found)
                {
                    if (items.Count == StagingCleanupIpcLimits.MaximumItems)
                    {
                        truncated = true;
                        break;
                    }

                    var inUse = syncRunning || known.IsInUse(folder, leftover.Name);
                    var item = leftover with { InUse = inUse };
                    if (item.HasValidBounds) items.Add(item);
                }
            }

            return new StagingCleanupScanResponse(
                StagingCleanupIpcContract.CurrentVersion,
                [.. items],
                searched,
                unreachable,
                truncated,
                syncRunning);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Removes each named leftover that is still one, still in a folder the queue knows, and still unused.</summary>
    internal async Task<StagingCleanupRemoveResponse> RemoveAsync(
        IReadOnlyList<TransferQueueAddress> addresses,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var known = await KnownFoldersAsync(cancellationToken).ConfigureAwait(false);
            var syncRunning = _runningSyncs() > 0;
            var results = new List<StagingCleanupRemoveResult>(addresses.Count);
            foreach (var group in addresses.GroupBy(static address => (address.ConnectionId, address.RootIdentity)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (LocalTransferFolder.IsLocalFolder(group.Key.RootIdentity))
                {
                    results.AddRange(group.Select(address => RemoveLocal(address, known, syncRunning)));
                    continue;
                }

                results.AddRange(await RemoveRemoteAsync([.. group], known, syncRunning, cancellationToken).ConfigureAwait(false));
            }

            return new StagingCleanupRemoveResponse(StagingCleanupIpcContract.CurrentVersion, [.. results]);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static StagingCleanupRemoveResult RemoveLocal(
        TransferQueueAddress address,
        KnownFolders known,
        bool syncRunning)
    {
        // The folder is the address's root and the file its only name, as the scan reported it.
        if (LocalTransferFolder.TryReadFolder(address.RootIdentity) is not { } folder ||
            !Path.IsPathFullyQualified(folder) ||
            address.ConnectionId != LocalTransferFolder.CreateConnectionId(folder) ||
            address.RelativePath.Contains('/') ||
            !StorageHubStagingNames.TryClassifyFile(address.RelativePath, out _) ||
            known.FindLocal(folder) is not { } owner)
        {
            return Refused(address, "cleanup.not_leftover", "This is not a leftover in a folder StorageHub transferred to.");
        }

        if (syncRunning || known.IsInUse(owner, address.RelativePath))
        {
            return InUse(address);
        }

        var path = Path.Combine(folder, address.RelativePath);
        if (!LocalStagedFileWriteHandle.TryClaim(path))
        {
            return InUse(address);
        }

        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                return new StagingCleanupRemoveResult(address, Removed: true, Size: 0);
            }

            if (file.LinkTarget is not null || (file.Attributes & FileAttributes.ReparsePoint) != 0 ||
                new DirectoryInfo(folder).LinkTarget is not null)
            {
                return Refused(address, "cleanup.link", "A link is not removed.");
            }

            var size = file.Length;
            file.Delete();
            return new StagingCleanupRemoveResult(address, Removed: true, Size: size);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Open in another program, or not this account's to remove.
            return Refused(address, "cleanup.remove_failed", "The file could not be removed. It may be open in another program.");
        }
        finally
        {
            LocalStagedFileWriteHandle.ReleaseClaim(path);
        }
    }

    private async Task<IEnumerable<StagingCleanupRemoveResult>> RemoveRemoteAsync(
        TransferQueueAddress[] addresses,
        KnownFolders known,
        bool syncRunning,
        CancellationToken cancellationToken)
    {
        var first = addresses[0];
        var accepted = new List<(TransferQueueAddress Address, KnownFolder Folder)>();
        var results = new List<StagingCleanupRemoveResult>();
        foreach (var address in addresses)
        {
            var slash = address.RelativePath.LastIndexOf('/');
            var name = address.RelativePath[(slash + 1)..];
            var parent = slash < 0 ? string.Empty : address.RelativePath[..slash];
            if (!StorageHubStagingNames.TryClassifyFile(name, out _) ||
                known.FindRemote(address.ConnectionId, address.RootIdentity, parent) is not { } folder)
            {
                results.Add(Refused(address, "cleanup.not_leftover", "This is not a leftover in a folder StorageHub transferred to."));
            }
            else if (syncRunning || known.IsInUse(folder, name))
            {
                results.Add(InUse(address));
            }
            else
            {
                accepted.Add((address, folder));
            }
        }

        if (accepted.Count == 0)
        {
            return results;
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(FolderBudget * 2);
        var opened = await OpenAsync(first.ConnectionId, first.RootIdentity, budget.Token).ConfigureAwait(false);
        if (opened.IsFailure)
        {
            results.AddRange(accepted.Select(item => Refused(item.Address, opened.Error!)));
            return results;
        }

        await using var connection = opened.Value;
        foreach (var (address, _) in accepted)
        {
            var target = StorageAddress.Create(new ConnectionProfileId(address.ConnectionId), address.RootIdentity, address.RelativePath);
            if (target.IsFailure)
            {
                results.Add(Refused(address, target.Error!));
                continue;
            }

            try
            {
                var entry = await connection.Session.GetEntryAsync(target.Value, budget.Token).ConfigureAwait(false);
                if (entry.IsFailure)
                {
                    results.Add(entry.Error!.Kind == StorageFailureKind.NotFound
                        ? new StagingCleanupRemoveResult(address, Removed: true, Size: 0)
                        : Refused(address, entry.Error));
                    continue;
                }

                if (entry.Value.Kind != StorageEntryKind.File)
                {
                    results.Add(Refused(address, "cleanup.not_leftover", "Only a file is removed."));
                    continue;
                }

                var deleted = await connection.Session.DeleteAsync(
                    new StorageDeleteRequest(target.Value, IgnoreMissing: true), budget.Token).ConfigureAwait(false);
                results.Add(deleted.IsSuccess
                    ? new StagingCleanupRemoveResult(address, Removed: true, Size: entry.Value.Size)
                    : Refused(address, deleted.Error!));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                results.Add(Refused(address, "cleanup.timeout", "The connection took too long to answer."));
            }
        }

        return results;
    }

    /// <summary>
    /// The folders the queue has written to, newest first and at most as many as one scan looks in,
    /// with what in them is spoken for.
    /// </summary>
    private async Task<KnownFolders> KnownFoldersAsync(CancellationToken cancellationToken)
    {
        var known = new KnownFolders();
        TransferQueueCursor? cursor = null;
        for (var page = 0; page < JobPages; page++)
        {
            var jobs = await _jobs.ListAsync(new TransferQueueQuery(AllStates, 100, cursor), cancellationToken)
                .ConfigureAwait(false);
            foreach (var job in jobs.Jobs)
            {
                known.Add(job);
            }

            cursor = jobs.Continuation;
            if (cursor is null) return known;
        }

        known.Truncated = true;
        return known;
    }

    /// <summary>The leftovers directly in a folder on this PC, or null when it cannot be read.</summary>
    private static List<StagingLeftover>? FindLocal(string folder)
    {
        try
        {
            var directory = new DirectoryInfo(folder);
            if (!directory.Exists || directory.LinkTarget is not null)
            {
                return null;
            }

            var found = new List<StagingLeftover>();
            foreach (var file in directory.EnumerateFiles(StorageHubStagingNames.FilePrefix + "*"))
            {
                if (file.LinkTarget is not null ||
                    !StorageHubStagingNames.TryClassifyFile(file.Name, out var kind))
                {
                    continue;
                }

                found.Add(new StagingLeftover(
                    new TransferQueueAddress(
                        LocalTransferFolder.CreateConnectionId(folder),
                        LocalTransferFolder.CreateRootIdentity(folder),
                        file.Name),
                    (StagingLeftoverKind)kind,
                    file.Name,
                    folder,
                    file.Length,
                    new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
                    InUse: false));
            }

            return found;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The leftovers directly in a folder on a saved connection, or null when it cannot be read.</summary>
    private async Task<List<StagingLeftover>?> FindRemoteAsync(KnownFolder folder, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(FolderBudget);
        try
        {
            var opened = await OpenAsync(folder.ConnectionId, folder.RootIdentity, budget.Token).ConfigureAwait(false);
            if (opened.IsFailure)
            {
                return null;
            }

            await using var connection = opened.Value;
            var address = StorageAddress.Create(new ConnectionProfileId(folder.ConnectionId), folder.RootIdentity, folder.Path);
            if (address.IsFailure)
            {
                return null;
            }

            var found = new List<StagingLeftover>();
            string? token = null;
            for (var page = 0; page < FolderListPages; page++)
            {
                var listed = await connection.Session.ListAsync(
                    address.Value, new StorageListRequest(ContinuationToken: token), budget.Token).ConfigureAwait(false);
                if (listed.IsFailure)
                {
                    return null;
                }

                foreach (var entry in listed.Value.Entries)
                {
                    if (entry.Kind != StorageEntryKind.File ||
                        !StorageHubStagingNames.TryClassifyFile(entry.Name, out var kind))
                    {
                        continue;
                    }

                    found.Add(new StagingLeftover(
                        new TransferQueueAddress(folder.ConnectionId, folder.RootIdentity, entry.Address.CanonicalRelativePath),
                        (StagingLeftoverKind)kind,
                        entry.Name,
                        "/" + folder.Path,
                        entry.Size,
                        entry.LastModifiedUtc,
                        InUse: false));
                }

                token = listed.Value.ContinuationToken;
                if (token is null) break;
            }

            return found;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>
    /// A saved connection, refused when it now opens somewhere other than the root its transfers
    /// wrote under: a name found there would be somebody else's file.
    /// </summary>
    private async Task<StorageResult<ISyncEndpointConnection>> OpenAsync(
        Guid connectionId,
        string rootIdentity,
        CancellationToken cancellationToken)
    {
        var opened = await _connector.OpenAsync(new ConnectionProfileId(connectionId), cancellationToken).ConfigureAwait(false);
        if (opened.IsFailure)
        {
            return opened;
        }

        if (string.Equals(opened.Value.Session.RootIdentity, rootIdentity, StringComparison.Ordinal))
        {
            return opened;
        }

        await opened.Value.DisposeAsync().ConfigureAwait(false);
        return StorageResult<ISyncEndpointConnection>.Fail(new StorageFailure(
            "cleanup.root_changed",
            StorageFailureKind.Conflict,
            "The connection now opens a different location from the one its transfers wrote to."));
    }

    private static StagingCleanupRemoveResult InUse(TransferQueueAddress address) =>
        Refused(address, "cleanup.in_use", "A transfer or a sync may still use this file.");

    private static StagingCleanupRemoveResult Refused(TransferQueueAddress address, string code, string message) =>
        new(address, Removed: false, Failure: new StorageIpcFailure(code, StorageIpcFailureCategory.Conflict, message, false));

    private static StagingCleanupRemoveResult Refused(TransferQueueAddress address, StorageFailure failure)
    {
        var message = new string([.. failure.Message.Where(static character => !char.IsControl(character))]);
        if (message.Length is 0 or > StorageIpcLimits.MaximumFailureMessageLength)
        {
            message = "The file could not be removed.";
        }

        var code = failure.Code.Length <= StorageIpcLimits.MaximumFailureCodeLength && !failure.Code.Any(char.IsControl)
            ? failure.Code
            : "cleanup.remove_failed";
        return new(address, Removed: false, Failure: new StorageIpcFailure(
            code,
            failure.Kind == StorageFailureKind.NotFound ? StorageIpcFailureCategory.NotFound : StorageIpcFailureCategory.Unavailable,
            message,
            failure.IsTransient));
    }

    private static AgentIpcCommandResponse InvalidRequest(int contractVersion) =>
        AgentIpcCommandResponse.Error(
            StagingCleanupIpcContract.IsSupported(contractVersion) ? "ipc.payload.invalid" : "ipc.contract.unsupported",
            StagingCleanupIpcContract.IsSupported(contractVersion)
                ? "The leftover cleanup request exceeded a permitted bound or contained an invalid value."
                : "The requested leftover cleanup contract version is not supported.");

    /// <summary>
    /// One folder transfers wrote into: a full path on this PC, or a path under a saved connection's
    /// root. Busy while a transfer that has not finished writes into it.
    /// </summary>
    private sealed class KnownFolder(Guid connectionId, string rootIdentity, string path, string? localPath)
    {
        public Guid ConnectionId { get; } = connectionId;
        public string RootIdentity { get; } = rootIdentity;
        public string Path { get; } = path;
        public string? LocalPath { get; } = localPath;
        public bool Busy { get; set; }
    }

    private sealed class KnownFolders
    {
        private static readonly StringComparer LocalComparer =
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        private readonly Dictionary<string, KnownFolder> _local = new(LocalComparer);
        private readonly Dictionary<(Guid, string, string), KnownFolder> _remote = [];
        private readonly HashSet<Guid> _unfinished = [];

        public List<KnownFolder> Folders { get; } = [];

        public bool Truncated { get; set; }

        public void Add(DurableTransferJob job)
        {
            var unfinished = job.State.State is not (TransferState.Completed or TransferState.Cancelled or TransferState.Failed);
            if (unfinished) _unfinished.Add(job.Intent.TransferJobId.Value);
            var parent = job.Intent.Destination.Parent;
            var folder = Local(parent) is { } local
                ? Find(_local, local, () => new KnownFolder(
                    LocalTransferFolder.CreateConnectionId(local), LocalTransferFolder.CreateRootIdentity(local), string.Empty, local))
                : LocalStagingTransferEndpoint.IsLocalDestination(parent)
                    ? null
                    : Find(
                        _remote,
                        (parent.ProfileId.Value, parent.RootIdentity, parent.CanonicalRelativePath),
                        () => new KnownFolder(parent.ProfileId.Value, parent.RootIdentity, parent.CanonicalRelativePath, null));
            if (folder is not null && unfinished) folder.Busy = true;
        }

        public KnownFolder? FindLocal(string folder)
        {
            try
            {
                return _local.GetValueOrDefault(System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(folder)));
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }

        public KnownFolder? FindRemote(Guid connectionId, string rootIdentity, string path) =>
            _remote.GetValueOrDefault((connectionId, rootIdentity, path));

        /// <summary>Whether a file in <paramref name="folder"/> may still be wanted by a transfer.</summary>
        public bool IsInUse(KnownFolder folder, string name) =>
            folder.Busy ||
            StorageHubStagingNames.TryReadStagingJobId(name, out var job) && _unfinished.Contains(job);

        /// <summary>
        /// A folder already known, or a new one while there is room for it. The queue lists newest
        /// first, so the folders kept are the ones written to most recently.
        /// </summary>
        private KnownFolder? Find<TKey>(Dictionary<TKey, KnownFolder> folders, TKey key, Func<KnownFolder> create)
            where TKey : notnull
        {
            if (folders.TryGetValue(key, out var found)) return found;
            if (Folders.Count >= StagingCleanupIpcLimits.MaximumFolders)
            {
                Truncated = true;
                return null;
            }

            var folder = create();
            folders.Add(key, folder);
            Folders.Add(folder);
            return folder;
        }

        /// <summary>The full path of a folder on this PC an address names, or null for a saved connection's.</summary>
        private static string? Local(StorageAddress folder)
        {
            string root;
            if (LocalUserPathTransferEndpoint.TryDecodeRoot(folder, out var user))
            {
                root = user;
            }
            else if (!LocalStagingTransferEndpoint.TryReadDestinationRoot(folder, out root))
            {
                return null;
            }

            try
            {
                var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                    root, folder.CanonicalRelativePath.Replace('/', System.IO.Path.DirectorySeparatorChar)));
                return System.IO.Path.TrimEndingDirectorySeparator(full);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }
    }
}
