using StorageHub.Application.Connections;
using StorageHub.Contracts.Ipc;
using StorageHub.Ipc;
using StorageHub.Persistence;
using StorageHub.Persistence.Connections;
using ContractStatus = StorageHub.Contracts.Ipc.ConnectionGroupWriteStatus;
using DomainStatus = StorageHub.Application.Connections.ConnectionGroupWriteStatus;

namespace StorageHub.Agent.Host;

/// <summary>
/// The connections panel's groups over the ordinary pipe: list, create, rename and recolour, move,
/// remove, file a connection, and bring in the desktop's old arrangement once.
/// </summary>
/// <remarks>
/// Every write answers with all the groups as they are afterwards, so the panel redraws from what
/// the agent holds rather than from what it guessed a write would do.
/// </remarks>
public sealed class ConnectionGroupIpcCommandService : IAgentIpcCommandHandler
{
    private readonly IConnectionGroupRepository _groups;

    public ConnectionGroupIpcCommandService(IConnectionGroupRepository groups)
    {
        _groups = groups ?? throw new ArgumentNullException(nameof(groups));
    }

    public ConnectionGroupIpcCommandService(SqliteDatabaseOptions databaseOptions)
        : this(new SqliteConnectionGroupRepository(databaseOptions))
    {
    }

    public bool CanHandle(string messageType) => messageType is
        ConnectionGroupIpcMessageTypes.ListRequest or
        ConnectionGroupIpcMessageTypes.CreateRequest or
        ConnectionGroupIpcMessageTypes.UpdateRequest or
        ConnectionGroupIpcMessageTypes.MoveRequest or
        ConnectionGroupIpcMessageTypes.DeleteRequest or
        ConnectionGroupIpcMessageTypes.AssignRequest or
        ConnectionGroupIpcMessageTypes.ImportRequest;

    public ValueTask<AgentIpcCommandResponse> HandleAsync(
        IpcEnvelope request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.MessageType switch
        {
            ConnectionGroupIpcMessageTypes.ListRequest => ListAsync(request, cancellationToken),
            ConnectionGroupIpcMessageTypes.CreateRequest => WriteAsync<ConnectionGroupCreateRequest>(
                request,
                ConnectionGroupIpcMessageTypes.CreateResponse,
                static request => request.ContractVersion,
                static request => request.HasValidBounds,
                async (groups, create, token) => Group(await groups
                    .CreateAsync(create.Name, create.IconKey, create.ColorKey, token)
                    .ConfigureAwait(false)),
                cancellationToken),
            ConnectionGroupIpcMessageTypes.UpdateRequest => WriteAsync<ConnectionGroupUpdateRequest>(
                request,
                ConnectionGroupIpcMessageTypes.UpdateResponse,
                static request => request.ContractVersion,
                static request => request.HasValidBounds,
                async (groups, update, token) => Group(await groups
                    .UpdateAsync(update.GroupId, update.Name, update.IconKey, update.ColorKey, token)
                    .ConfigureAwait(false)),
                cancellationToken),
            ConnectionGroupIpcMessageTypes.MoveRequest => WriteAsync<ConnectionGroupMoveRequest>(
                request,
                ConnectionGroupIpcMessageTypes.MoveResponse,
                static request => request.ContractVersion,
                static request => request.HasValidBounds,
                async (groups, move, token) => Group(await groups
                    .MoveAsync(move.GroupId, move.Index, token)
                    .ConfigureAwait(false)),
                cancellationToken),
            ConnectionGroupIpcMessageTypes.DeleteRequest => WriteAsync<ConnectionGroupDeleteRequest>(
                request,
                ConnectionGroupIpcMessageTypes.DeleteResponse,
                static request => request.ContractVersion,
                static request => request.HasValidBounds,
                async (groups, delete, token) => Group(await groups
                    .DeleteAsync(delete.GroupId, token)
                    .ConfigureAwait(false)),
                cancellationToken),
            ConnectionGroupIpcMessageTypes.AssignRequest => WriteAsync<ConnectionGroupAssignRequest>(
                request,
                ConnectionGroupIpcMessageTypes.AssignResponse,
                static request => request.ContractVersion,
                static request => request.HasValidBounds,
                async (groups, assign, token) =>
                {
                    var version = await groups
                        .AssignAsync(assign.ConnectionId, assign.GroupId, token)
                        .ConfigureAwait(false);
                    return version is null
                        ? (ContractStatus.NotFound, null, null)
                        : (ContractStatus.Succeeded, null, version);
                },
                cancellationToken),
            ConnectionGroupIpcMessageTypes.ImportRequest => WriteAsync<ConnectionGroupImportRequest>(
                request,
                ConnectionGroupIpcMessageTypes.ImportResponse,
                static request => request.ContractVersion,
                static request => request.HasValidBounds,
                async (groups, import, token) =>
                {
                    var applied = await groups
                        .ImportOnceAsync(
                            [.. import.Groups.Select(static group => new ConnectionGroupImport(
                                group.Name, group.IconKey, group.ColorKey, group.Members))],
                            token)
                        .ConfigureAwait(false);
                    return (applied ? ContractStatus.Succeeded : ContractStatus.AlreadyImported, null, null);
                },
                cancellationToken),
            _ => ValueTask.FromResult(AgentIpcCommandResponse.Error(
                "ipc.message.unsupported",
                "The requested IPC operation is not supported by this agent version."))
        };
    }

    private async ValueTask<AgentIpcCommandResponse> ListAsync(
        IpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var request = envelope.DeserializePayload<ConnectionGroupListRequest>();
        if (!ConnectionProfileIpcContract.IsSupported(request.ContractVersion) || !request.HasValidBounds)
        {
            return ListResponse([], Failure(
                "connection.group.request.invalid",
                StorageIpcFailureCategory.Validation,
                "The group request was invalid or outside the negotiated bounds."));
        }

        try
        {
            var groups = await _groups.ListAsync(cancellationToken).ConfigureAwait(false);
            return ListResponse([.. groups.Select(Map)], null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ListResponse([], Failure(
                "connection.group.unavailable",
                StorageIpcFailureCategory.Unavailable,
                "Connection groups are temporarily unavailable.",
                isTransient: true));
        }
    }

    private async ValueTask<AgentIpcCommandResponse> WriteAsync<TRequest>(
        IpcEnvelope envelope,
        string responseType,
        Func<TRequest, int> contractVersion,
        Func<TRequest, bool> withinBounds,
        Func<IConnectionGroupRepository, TRequest, CancellationToken,
            Task<(ContractStatus Status, ConnectionGroup? Group, long? ConnectionVersion)>> write,
        CancellationToken cancellationToken)
    {
        var request = envelope.DeserializePayload<TRequest>();
        if (request is null || !ConnectionProfileIpcContract.IsSupported(contractVersion(request)) || !withinBounds(request))
        {
            return WriteResponse(responseType, ContractStatus.ValidationFailed, failure: Failure(
                "connection.group.request.invalid",
                StorageIpcFailureCategory.Validation,
                "The group request was invalid or outside the negotiated bounds."));
        }

        try
        {
            var (status, group, connectionVersion) = await write(_groups, request, cancellationToken).ConfigureAwait(false);
            var groups = await _groups.ListAsync(cancellationToken).ConfigureAwait(false);
            return WriteResponse(
                responseType,
                status,
                group is null ? null : Map(group),
                [.. groups.Select(Map)],
                connectionVersion,
                status switch
                {
                    ContractStatus.NameConflict => Failure(
                        "connection.group.name_conflict",
                        StorageIpcFailureCategory.Conflict,
                        "A group with that name already exists."),
                    ContractStatus.NotFound => Failure(
                        "connection.group.not_found",
                        StorageIpcFailureCategory.NotFound,
                        "The group or connection was not found."),
                    _ => null
                });
        }
        catch (ArgumentException)
        {
            return WriteResponse(responseType, ContractStatus.ValidationFailed, failure: Failure(
                "connection.group.validation_failed",
                StorageIpcFailureCategory.Validation,
                "The group's name, icon or colour is not valid."));
        }
        catch (DatabaseRecoveryRequiredException)
        {
            return WriteResponse(responseType, ContractStatus.Unavailable, failure: Failure(
                "connection.group.database_recovery_required",
                StorageIpcFailureCategory.Unavailable,
                "The profile database requires recovery before groups can be changed. Nothing was changed."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return WriteResponse(responseType, ContractStatus.Unavailable, failure: Failure(
                "connection.group.unavailable",
                StorageIpcFailureCategory.Unavailable,
                "The group could not be saved.",
                isTransient: true));
        }
    }

    private static (ContractStatus, ConnectionGroup?, long?) Group(ConnectionGroupWriteResult result) =>
        (result.Status switch
        {
            DomainStatus.Succeeded => ContractStatus.Succeeded,
            DomainStatus.NotFound => ContractStatus.NotFound,
            DomainStatus.NameConflict => ContractStatus.NameConflict,
            _ => ContractStatus.Unavailable
        }, result.Group, null);

    internal static ConnectionGroupDocument Map(ConnectionGroup group) => new(
        group.Id,
        group.Name,
        group.SortOrder,
        group.IconKey,
        group.ColorKey,
        group.Version,
        group.CreatedUtc,
        group.UpdatedUtc);

    private static AgentIpcCommandResponse ListResponse(ConnectionGroupDocument[] groups, StorageIpcFailure? failure) =>
        AgentIpcCommandResponse.Create(
            ConnectionGroupIpcMessageTypes.ListResponse,
            new ConnectionGroupListResponse(ConnectionProfileIpcContract.CurrentVersion, groups, failure));

    private static AgentIpcCommandResponse WriteResponse(
        string responseType,
        ContractStatus status,
        ConnectionGroupDocument? group = null,
        ConnectionGroupDocument[]? groups = null,
        long? connectionVersion = null,
        StorageIpcFailure? failure = null) =>
        AgentIpcCommandResponse.Create(
            responseType,
            new ConnectionGroupWriteResponse(
                ConnectionProfileIpcContract.CurrentVersion,
                status,
                group,
                groups,
                connectionVersion,
                failure));

    private static StorageIpcFailure Failure(
        string code,
        StorageIpcFailureCategory category,
        string message,
        bool isTransient = false) => new(code, category, message, isTransient);
}
