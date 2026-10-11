using System.Text.Json;
using StorageHub.Agent.Transfers;
using StorageHub.Contracts.Ipc;
using StorageHub.Contracts.Results;
using StorageHub.Domain.Identifiers;
using StorageHub.Domain.Storage;
using StorageHub.Ipc;
using StorageHub.Storage.Abstractions;
using StorageHub.Sync;

namespace StorageHub.Agent.Host;

/// <summary>
/// Compares two folders for the desktop's "Compare folders" window: a saved connection's folder
/// or a folder on this PC on each side, in any pairing.
/// </summary>
/// <remarks>
/// <para>
/// The comparison itself is <see cref="FolderComparison"/>, over the sync scanner. This holds the
/// comparisons while they run and after: a start answers at once with an id, the desktop asks how
/// it is going, and once it has finished reads it a page at a time. One reply could not hold the
/// result for a big tree, and one request is given two minutes, which checksumming a few gigabytes
/// on a connection takes longer than.
/// </para>
/// <para>
/// A saved connection is opened through the connector sync uses, and the root it opens must be the
/// one the pane was showing, as every other request a pane makes must; a connection edited to point
/// elsewhere since is refused rather than compared somewhere the person was not looking. A folder
/// on this PC is approved by the gate a transfer from it would pass.
/// </para>
/// </remarks>
public sealed class FolderCompareIpcCommandService : IAgentIpcCommandHandler, IAsyncDisposable
{
    /// <summary>How long a finished comparison is kept after it was last read.</summary>
    internal static readonly TimeSpan RetainedFor = TimeSpan.FromMinutes(15);

    private readonly ISyncEndpointConnector _connector;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<Guid, Job> _jobs = [];
    private readonly Lock _gate = new();

    public FolderCompareIpcCommandService(ISyncEndpointConnector connector, TimeProvider? timeProvider = null)
    {
        _connector = connector ?? throw new ArgumentNullException(nameof(connector));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool CanHandle(string messageType) => messageType is
        FolderCompareIpcMessageTypes.StartRequest or
        FolderCompareIpcMessageTypes.StatusRequest or
        FolderCompareIpcMessageTypes.ReleaseRequest;

    public ValueTask<AgentIpcCommandResponse> HandleAsync(
        IpcEnvelope request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return ValueTask.FromResult(request.MessageType switch
            {
                FolderCompareIpcMessageTypes.StartRequest => Start(request.DeserializePayload<FolderCompareStartRequest>()),
                FolderCompareIpcMessageTypes.StatusRequest => Status(request.DeserializePayload<FolderCompareStatusRequest>()),
                FolderCompareIpcMessageTypes.ReleaseRequest => Release(request.DeserializePayload<FolderCompareReleaseRequest>()),
                _ => AgentIpcCommandResponse.Error(
                    "ipc.message.unsupported",
                    "The requested IPC operation is not supported by this agent version.")
            });
        }
        catch (JsonException)
        {
            return ValueTask.FromResult(AgentIpcCommandResponse.Error(
                "ipc.payload.invalid",
                "The folder comparison request payload was invalid."));
        }
    }

    /// <summary>Stops every comparison still running.</summary>
    public async ValueTask DisposeAsync()
    {
        Job[] jobs;
        lock (_gate)
        {
            jobs = [.. _jobs.Values];
            _jobs.Clear();
        }

        foreach (var job in jobs)
        {
            await job.Cancellation.CancelAsync().ConfigureAwait(false);
        }

        foreach (var job in jobs)
        {
            try
            {
                await job.Work.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            job.Cancellation.Dispose();
        }
    }

    private AgentIpcCommandResponse Start(FolderCompareStartRequest request)
    {
        if (request is null || !request.HasValidBounds || !FolderCompareIpcContract.IsSupported(request.ContractVersion))
        {
            return InvalidRequest(request?.ContractVersion ?? 0);
        }

        var id = Guid.NewGuid();
        Job job;
        lock (_gate)
        {
            Expire();
            if (_jobs.Count >= FolderCompareIpcLimits.MaximumRetained)
            {
                // The oldest finished one makes room; with every slot still running, nothing does.
                var oldest = _jobs.Values
                    .Where(static candidate => candidate.Work.IsCompleted)
                    .OrderBy(static candidate => candidate.TouchedUtc)
                    .FirstOrDefault();
                if (oldest is null)
                {
                    return AgentIpcCommandResponse.Create(
                        FolderCompareIpcMessageTypes.StartResponse,
                        new FolderCompareStartResponse(request.ContractVersion, Guid.Empty, new StorageIpcFailure(
                            "compare.busy",
                            StorageIpcFailureCategory.Unavailable,
                            "Too many folder comparisons are running. Wait for one to finish.",
                            IsTransient: true)));
                }

                _jobs.Remove(oldest.Id);
                oldest.Cancellation.Dispose();
            }

            job = new Job(id, new CancellationTokenSource(), _timeProvider.GetUtcNow());
            job.Work = Task.Run(() => RunAsync(job, request, job.Cancellation.Token));
            _jobs[id] = job;
        }

        return AgentIpcCommandResponse.Create(
            FolderCompareIpcMessageTypes.StartResponse,
            new FolderCompareStartResponse(request.ContractVersion, id));
    }

    private AgentIpcCommandResponse Status(FolderCompareStatusRequest request)
    {
        if (request is null || !request.HasValidBounds || !FolderCompareIpcContract.IsSupported(request.ContractVersion))
        {
            return InvalidRequest(request?.ContractVersion ?? 0);
        }

        Job? job;
        lock (_gate)
        {
            Expire();
            if (_jobs.TryGetValue(request.CompareId, out job))
            {
                job.TouchedUtc = _timeProvider.GetUtcNow();
            }
        }

        FolderCompareStatusResponse response;
        if (job is null)
        {
            response = new FolderCompareStatusResponse(
                request.ContractVersion, request.CompareId, FolderCompareState.Failed, 0, [],
                Failure: new StorageIpcFailure(
                    "compare.not_found",
                    StorageIpcFailureCategory.NotFound,
                    "The comparison is no longer held. Compare the folders again.",
                    IsTransient: false));
        }
        else if (job.Failure is { } failure)
        {
            response = new FolderCompareStatusResponse(
                request.ContractVersion, request.CompareId, FolderCompareState.Failed, 0, [], Failure: failure);
        }
        else if (job.Result is not { } result)
        {
            response = new FolderCompareStatusResponse(
                request.ContractVersion, request.CompareId, FolderCompareState.Running, 0, []);
        }
        else
        {
            response = new FolderCompareStatusResponse(
                request.ContractVersion,
                request.CompareId,
                FolderCompareState.Completed,
                result.Entries.Length,
                [.. result.Entries.Skip(request.Offset).Take(request.PageSize)],
                result.UncheckedPairs,
                result.CaseInsensitive);
        }

        return AgentIpcCommandResponse.Create(FolderCompareIpcMessageTypes.StatusResponse, response);
    }

    private AgentIpcCommandResponse Release(FolderCompareReleaseRequest request)
    {
        if (request is null || !request.HasValidBounds || !FolderCompareIpcContract.IsSupported(request.ContractVersion))
        {
            return InvalidRequest(request?.ContractVersion ?? 0);
        }

        Job? job;
        lock (_gate)
        {
            if (_jobs.Remove(request.CompareId, out job))
            {
                job.Cancellation.Cancel();
            }
        }

        return AgentIpcCommandResponse.Create(
            FolderCompareIpcMessageTypes.ReleaseResponse,
            new FolderCompareReleaseResponse(request.ContractVersion, request.CompareId, job is not null));
    }

    /// <summary>Drops finished comparisons nobody has read for <see cref="RetainedFor"/>. Under the gate.</summary>
    private void Expire()
    {
        var cutoff = _timeProvider.GetUtcNow() - RetainedFor;
        foreach (var stale in _jobs.Values.Where(job => job.Work.IsCompleted && job.TouchedUtc < cutoff).ToArray())
        {
            _jobs.Remove(stale.Id);
            stale.Cancellation.Dispose();
        }
    }

    private async Task RunAsync(Job job, FolderCompareStartRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await using var left = await OpenAsync(request.Left, cancellationToken).ConfigureAwait(false);
            if (left.Failure is not null)
            {
                job.Failure = left.Failure;
                return;
            }

            await using var right = await OpenAsync(request.Right, cancellationToken).ConfigureAwait(false);
            if (right.Failure is not null)
            {
                job.Failure = right.Failure;
                return;
            }

            var compared = await FolderComparison.CompareAsync(
                left.Session!,
                left.Root!,
                right.Session!,
                right.Root!,
                new FolderComparisonOptions(
                    request.CompareContents,
                    MaximumEntries: FolderCompareIpcLimits.MaximumEntries),
                cancellationToken).ConfigureAwait(false);
            if (compared.IsFailure)
            {
                job.Failure = Describe(compared.Error);
                return;
            }

            job.Result = new Finished(
                [.. compared.Value.Entries.Select(Map).Where(static entry => entry.HasValidBounds)],
                compared.Value.UncheckedPairs,
                compared.Value.CaseInsensitive);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            job.Failure = new StorageIpcFailure(
                "compare.cancelled", StorageIpcFailureCategory.Cancelled, "The comparison was cancelled.", false);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            job.Failure = new StorageIpcFailure(
                "compare.unexpected",
                StorageIpcFailureCategory.Unexpected,
                "The folders could not be compared.",
                IsTransient: false);
        }
    }

    /// <summary>Opens one side: a folder on this PC, or a saved connection held to the root the pane showed.</summary>
    private async ValueTask<OpenedSide> OpenAsync(TransferQueueAddress side, CancellationToken cancellationToken)
    {
        var profileId = new ConnectionProfileId(side.ConnectionId);
        if (LocalTransferFolder.IsLocalFolder(side.RootIdentity))
        {
            var root = StorageAddress.Create(profileId, side.RootIdentity, side.RelativePath);
            if (root.IsFailure)
            {
                return OpenedSide.Failed(Describe(root.Error));
            }

            var local = LocalUserPathScanEndpoint.Open(root.Value);
            return local.IsSuccess
                ? new OpenedSide(local.Value, root.Value, null, null)
                : OpenedSide.Failed(Describe(local.Error));
        }

        var opened = await _connector.OpenAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (opened.IsFailure)
        {
            return OpenedSide.Failed(Describe(opened.Error));
        }

        var connection = opened.Value;
        if (connection.Session.ProfileId != profileId ||
            !string.Equals(connection.Session.RootIdentity, side.RootIdentity, StringComparison.Ordinal))
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            return OpenedSide.Failed(new StorageIpcFailure(
                "compare.root_changed",
                StorageIpcFailureCategory.Integrity,
                "The connection now opens somewhere other than the folder the pane is showing. Refresh the pane and compare again.",
                IsTransient: false));
        }

        var address = StorageAddress.Create(profileId, side.RootIdentity, side.RelativePath);
        if (address.IsFailure)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            return OpenedSide.Failed(Describe(address.Error));
        }

        return new OpenedSide(connection.Session, address.Value, connection, null);
    }

    private static FolderCompareEntry Map(FolderComparisonEntry entry) => new(
        entry.RelativePath,
        entry.Kind switch
        {
            FolderComparisonKind.OnlyLeft => FolderCompareKind.OnlyLeft,
            FolderComparisonKind.OnlyRight => FolderCompareKind.OnlyRight,
            FolderComparisonKind.Different => FolderCompareKind.Different,
            _ => FolderCompareKind.Same
        },
        (FolderCompareReasons)(int)entry.Reasons,
        entry.Left is { } left ? Item(left, entry.LeftDigest) : null,
        entry.Right is { } right ? Item(right, entry.RightDigest) : null,
        entry.RightRelativePath);

    private static FolderCompareItem Item(StorageEntry entry, PortableContentDigest? digest)
    {
        var item = new FolderCompareItem(
            entry.Kind switch
            {
                StorageEntryKind.File => StorageItemKind.File,
                StorageEntryKind.Directory => StorageItemKind.Directory,
                StorageEntryKind.Prefix => StorageItemKind.Prefix,
                StorageEntryKind.SymbolicLink => StorageItemKind.SymbolicLink,
                _ => StorageItemKind.Other
            },
            entry.Size,
            entry.LastModifiedUtc,
            digest?.Value,
            entry.Address.NativeItemId,
            entry.Address.VersionId,
            entry.Address.EntityTag ?? entry.ETag);

        // An identity too long to carry is dropped rather than the item: without it a copy over
        // this file is refused for having nothing to hold the replacement to, which is the safe way.
        return item.HasValidBounds ? item : item with { NativeItemId = null, VersionId = null, EntityTag = null };
    }

    /// <summary>
    /// A failure as the desktop sees it. The messages here are StorageHub's own -- the connector's,
    /// the scanner's, the local folder gate's -- so they are passed on, bounded, rather than replaced
    /// with one sentence for every cause.
    /// </summary>
    private static StorageIpcFailure Describe(StorageFailure failure)
    {
        var message = new string([.. failure.Message.Where(static character => !char.IsControl(character))]);
        if (message.Length > StorageIpcLimits.MaximumFailureMessageLength)
        {
            message = message[..(StorageIpcLimits.MaximumFailureMessageLength - 1)] + "…";
        }

        var code = failure.Code.Length <= StorageIpcLimits.MaximumFailureCodeLength && !failure.Code.Any(char.IsControl)
            ? failure.Code
            : "compare.failed";
        return new StorageIpcFailure(
            code,
            failure.Kind switch
            {
                StorageFailureKind.Validation => StorageIpcFailureCategory.Validation,
                StorageFailureKind.NotFound => StorageIpcFailureCategory.NotFound,
                StorageFailureKind.Conflict => StorageIpcFailureCategory.Conflict,
                StorageFailureKind.Unsupported => StorageIpcFailureCategory.Unsupported,
                StorageFailureKind.Unauthorized => StorageIpcFailureCategory.Unauthorized,
                StorageFailureKind.Unavailable => StorageIpcFailureCategory.Unavailable,
                StorageFailureKind.Timeout => StorageIpcFailureCategory.Timeout,
                StorageFailureKind.Cancelled => StorageIpcFailureCategory.Cancelled,
                StorageFailureKind.Integrity => StorageIpcFailureCategory.Integrity,
                StorageFailureKind.Security => StorageIpcFailureCategory.Security,
                StorageFailureKind.Provider => StorageIpcFailureCategory.Provider,
                _ => StorageIpcFailureCategory.Unexpected
            },
            message.Length == 0 ? "The folders could not be compared." : message,
            failure.IsTransient);
    }

    private static AgentIpcCommandResponse InvalidRequest(int contractVersion) =>
        AgentIpcCommandResponse.Error(
            FolderCompareIpcContract.IsSupported(contractVersion) ? "ipc.payload.invalid" : "ipc.contract.unsupported",
            FolderCompareIpcContract.IsSupported(contractVersion)
                ? "The folder comparison request exceeded a permitted bound or contained an invalid value."
                : "The requested folder comparison contract version is not supported.");

    private sealed class Job(Guid id, CancellationTokenSource cancellation, DateTimeOffset startedUtc)
    {
        public Guid Id { get; } = id;

        public CancellationTokenSource Cancellation { get; } = cancellation;

        public Task Work { get; set; } = Task.CompletedTask;

        public DateTimeOffset TouchedUtc { get; set; } = startedUtc;

        public volatile Finished? Result;

        public volatile StorageIpcFailure? Failure;
    }

    private sealed record Finished(FolderCompareEntry[] Entries, int UncheckedPairs, bool CaseInsensitive);

    private sealed record OpenedSide(
        IStorageEndpointSession? Session,
        StorageAddress? Root,
        ISyncEndpointConnection? Connection,
        StorageIpcFailure? Failure) : IAsyncDisposable
    {
        public static OpenedSide Failed(StorageIpcFailure failure) => new(null, null, null, failure);

        public async ValueTask DisposeAsync()
        {
            if (Connection is not null)
            {
                await Connection.DisposeAsync().ConfigureAwait(false);
            }
            else if (Session is not null)
            {
                await Session.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
