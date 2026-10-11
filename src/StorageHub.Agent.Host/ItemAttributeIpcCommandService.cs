using System.Text.Json;
using StorageHub.Contracts.Ipc;
using StorageHub.Contracts.Results;
using StorageHub.Domain.Capabilities;
using StorageHub.Domain.Identifiers;
using StorageHub.Domain.Storage;
using StorageHub.Ipc;
using StorageHub.Storage.Abstractions;
using StorageHub.Storage.Models;
using StorageHub.Sync;

namespace StorageHub.Agent.Host;

/// <summary>
/// Reads an item's attributes (permissions, owner, link target, times) and changes its permission
/// bits or modified time, for the Properties window.
/// </summary>
/// <remarks>
/// <para>
/// The same saved-profile connector and exact-address check as the object inspector, so the item
/// changed is the one the pane listed, under the root the agent identified. Each change is refused
/// with a sentence before the provider is asked when the connection does not advertise it.
/// </para>
/// <para>
/// What the provider reports is cleaned rather than trusted: a name or link target with control
/// characters or past the contract's length, a mode outside twelve bits or a negative ID is left
/// out as not reported, so one odd field on one server does not cost the window everything else.
/// </para>
/// </remarks>
public sealed class ItemAttributeIpcCommandService : IAgentIpcCommandHandler
{
    private readonly ISyncEndpointConnector _connector;

    public ItemAttributeIpcCommandService(ISyncEndpointConnector connector)
    {
        _connector = connector ?? throw new ArgumentNullException(nameof(connector));
    }

    public bool CanHandle(string messageType) => messageType is
        ItemAttributeIpcMessageTypes.GetRequest or
        ItemAttributeIpcMessageTypes.PermissionsSetRequest or
        ItemAttributeIpcMessageTypes.ModifiedTimeSetRequest;

    public ValueTask<AgentIpcCommandResponse> HandleAsync(
        IpcEnvelope request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.MessageType switch
        {
            ItemAttributeIpcMessageTypes.GetRequest => GetAsync(request, cancellationToken),
            ItemAttributeIpcMessageTypes.PermissionsSetRequest => SetPermissionsAsync(request, cancellationToken),
            ItemAttributeIpcMessageTypes.ModifiedTimeSetRequest => SetModifiedTimeAsync(request, cancellationToken),
            _ => ValueTask.FromResult(AgentIpcCommandResponse.Error(
                "ipc.message.unsupported",
                "The requested IPC operation is not supported by this agent version."))
        };
    }

    private async ValueTask<AgentIpcCommandResponse> GetAsync(
        IpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ItemAttributesGetRequest request;
        try { request = envelope.DeserializePayload<ItemAttributesGetRequest>(); }
        catch (JsonException) { return ObjectInspectorIpcCommandService.InvalidPayload(); }
        if (!request.HasValidBounds) return InvalidRequest(request.ContractVersion);

        try
        {
            var opened = await _connector.OpenAsync(
                new ConnectionProfileId(request.Address.ConnectionId), cancellationToken).ConfigureAwait(false);
            if (opened.IsFailure) return GetFailure(request, Sanitize(opened.Error));
            await using var connection = opened.Value;
            var session = connection.Session;
            var validated = ObjectInspectorIpcCommandService.ValidateAndCreateAddress(session, request.Address);
            if (validated.Failure is not null) return GetFailure(request, validated.Failure);

            ItemAttributes attributes;
            if (session is IStorageAttributeEndpointSession attributeSession)
            {
                var read = await attributeSession.GetAttributesAsync(validated.Address!, cancellationToken)
                    .ConfigureAwait(false);
                if (read.IsFailure) return GetFailure(request, Sanitize(read.Error));
                attributes = Clean(read.Value, session.Capabilities, canChange: true);
            }
            else
            {
                // A session without attributes still has the entry, which is the kind, size and
                // modified time; the window shows those and offers no change.
                var entry = await session.GetEntryAsync(validated.Address!, cancellationToken).ConfigureAwait(false);
                if (entry.IsFailure) return GetFailure(request, Sanitize(entry.Error));
                attributes = Clean(new StorageItemAttributes(entry.Value), session.Capabilities, canChange: false);
            }

            return AgentIpcCommandResponse.Create(
                ItemAttributeIpcMessageTypes.GetResponse,
                new ItemAttributesGetResponse(request.ContractVersion, request.Address, attributes));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return GetFailure(request, Unavailable()); }
    }

    private async ValueTask<AgentIpcCommandResponse> SetPermissionsAsync(
        IpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ItemPermissionsSetRequest request;
        try { request = envelope.DeserializePayload<ItemPermissionsSetRequest>(); }
        catch (JsonException) { return ObjectInspectorIpcCommandService.InvalidPayload(); }
        if (!request.HasValidBounds) return InvalidRequest(request.ContractVersion);

        try
        {
            var opened = await _connector.OpenAsync(
                new ConnectionProfileId(request.Address.ConnectionId), cancellationToken).ConfigureAwait(false);
            if (opened.IsFailure) return PermissionsResult(request, Sanitize(opened.Error));
            await using var connection = opened.Value;
            var session = connection.Session;
            var validated = ObjectInspectorIpcCommandService.ValidateAndCreateAddress(session, request.Address);
            if (validated.Failure is not null) return PermissionsResult(request, validated.Failure);

            if (!session.Capabilities.Supports(StorageFeature.Permissions) ||
                session is not IStorageAttributeEndpointSession attributeSession)
            {
                return PermissionsResult(request, Unsupported("This connection cannot change permissions."));
            }

            var changed = await attributeSession
                .SetPermissionsAsync(validated.Address!, request.UnixMode, cancellationToken)
                .ConfigureAwait(false);
            return PermissionsResult(request, changed.IsSuccess ? null : Sanitize(changed.Error));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return PermissionsResult(request, Unavailable()); }
    }

    private async ValueTask<AgentIpcCommandResponse> SetModifiedTimeAsync(
        IpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ItemModifiedTimeSetRequest request;
        try { request = envelope.DeserializePayload<ItemModifiedTimeSetRequest>(); }
        catch (JsonException) { return ObjectInspectorIpcCommandService.InvalidPayload(); }
        if (!request.HasValidBounds) return InvalidRequest(request.ContractVersion);

        try
        {
            var opened = await _connector.OpenAsync(
                new ConnectionProfileId(request.Address.ConnectionId), cancellationToken).ConfigureAwait(false);
            if (opened.IsFailure) return ModifiedTimeResult(request, Sanitize(opened.Error));
            await using var connection = opened.Value;
            var session = connection.Session;
            var validated = ObjectInspectorIpcCommandService.ValidateAndCreateAddress(session, request.Address);
            if (validated.Failure is not null) return ModifiedTimeResult(request, validated.Failure);

            if (!session.Capabilities.Supports(StorageFeature.SetModifiedTime) ||
                session is not IStorageAttributeEndpointSession attributeSession)
            {
                return ModifiedTimeResult(request, Unsupported("This connection cannot set the modified time."));
            }

            var changed = await attributeSession
                .SetModifiedTimeAsync(validated.Address!, request.ModifiedUtc, cancellationToken)
                .ConfigureAwait(false);
            return ModifiedTimeResult(request, changed.IsSuccess ? null : Sanitize(changed.Error));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return ModifiedTimeResult(request, Unavailable()); }
    }

    /// <summary>The provider's attributes, with anything outside the contract left out.</summary>
    internal static ItemAttributes Clean(
        StorageItemAttributes attributes,
        EffectiveStorageCapabilities capabilities,
        bool canChange)
    {
        var entry = attributes.Entry;
        var kind = entry.Kind switch
        {
            StorageEntryKind.File => StorageItemKind.File,
            StorageEntryKind.Directory => StorageItemKind.Directory,
            StorageEntryKind.Prefix => StorageItemKind.Prefix,
            StorageEntryKind.SymbolicLink => StorageItemKind.SymbolicLink,
            _ => StorageItemKind.Other
        };

        return new ItemAttributes(
            kind,
            entry.Size is >= 0 ? entry.Size : null,
            Reported(entry.LastModifiedUtc),
            Reported(attributes.CreatedUtc),
            Reported(attributes.LastAccessedUtc),
            attributes.UnixMode is >= 0 and <= ItemAttributeIpcLimits.MaximumUnixMode ? attributes.UnixMode : null,
            ItemAttributes.IsPrincipalName(attributes.Owner) ? attributes.Owner : null,
            ItemAttributes.IsPrincipalName(attributes.Group) ? attributes.Group : null,
            attributes.OwnerId is >= 0 ? attributes.OwnerId : null,
            attributes.GroupId is >= 0 ? attributes.GroupId : null,
            ItemAttributes.IsLinkTarget(attributes.LinkTarget) ? attributes.LinkTarget : null,
            canChange && capabilities.Supports(StorageFeature.Permissions),
            canChange && capabilities.Supports(StorageFeature.SetModifiedTime));
    }

    /// <summary>A time in UTC, or nothing for the year-one default a library leaves unset.</summary>
    private static DateTimeOffset? Reported(DateTimeOffset? value) =>
        value is { } time && time.UtcDateTime.Year > 1 ? time.ToUniversalTime() : null;

    /// <summary>
    /// A provider failure in words about the change, never the provider's own text.
    /// </summary>
    /// <remarks>
    /// The inspector's sentences are about reading; a chmod a server refuses because the account
    /// does not own the file is not "the provider rejected the saved credentials", which is what
    /// an unauthorised read means.
    /// </remarks>
    private static StorageIpcFailure Sanitize(StorageFailure failure)
    {
        var (category, code, message) = failure.Kind switch
        {
            StorageFailureKind.Validation => (StorageIpcFailureCategory.Validation, "storage.attributes.validation",
                "The server did not accept that value."),
            StorageFailureKind.NotFound => (StorageIpcFailureCategory.NotFound, "storage.attributes.not_found",
                "The item was not found; it may have been moved or deleted."),
            StorageFailureKind.Conflict => (StorageIpcFailureCategory.Conflict, "storage.attributes.conflict",
                "The item changed while it was being read or changed."),
            StorageFailureKind.Unsupported => (StorageIpcFailureCategory.Unsupported, "storage.attributes.unsupported",
                "The server does not support this change."),
            StorageFailureKind.Unauthorized => (StorageIpcFailureCategory.Unauthorized, "storage.attributes.unauthorized",
                "The server refused; the account may not own this item or may lack the right to change it."),
            StorageFailureKind.Unavailable => (StorageIpcFailureCategory.Unavailable, "storage.attributes.unavailable",
                "The storage provider is temporarily unavailable."),
            StorageFailureKind.Timeout => (StorageIpcFailureCategory.Timeout, "storage.attributes.timeout",
                "The storage provider did not respond in time."),
            StorageFailureKind.Cancelled => (StorageIpcFailureCategory.Cancelled, "storage.attributes.cancelled",
                "The request was cancelled."),
            StorageFailureKind.Integrity => (StorageIpcFailureCategory.Integrity, "storage.attributes.integrity",
                "The provider response failed an integrity check."),
            StorageFailureKind.Security => (StorageIpcFailureCategory.Security, "storage.attributes.security",
                "The connection requires a security or trust decision."),
            StorageFailureKind.Provider => (StorageIpcFailureCategory.Provider, "storage.attributes.provider",
                "The storage provider could not complete the request."),
            _ => (StorageIpcFailureCategory.Unexpected, "storage.attributes.failed",
                "The item's attributes could not be read or changed.")
        };
        return new StorageIpcFailure(code, category, message, failure.IsTransient);
    }

    private static StorageIpcFailure Unsupported(string message) =>
        new("storage.attributes.unsupported", StorageIpcFailureCategory.Unsupported, message, IsTransient: false);

    private static StorageIpcFailure Unavailable() => new(
        "storage.attributes.unavailable",
        StorageIpcFailureCategory.Unavailable,
        "The item's attributes are temporarily unavailable.",
        IsTransient: true);

    private static AgentIpcCommandResponse InvalidRequest(int contractVersion) =>
        AgentIpcCommandResponse.Error(
            ItemAttributeIpcContract.IsSupported(contractVersion) ? "ipc.payload.invalid" : "ipc.contract.unsupported",
            ItemAttributeIpcContract.IsSupported(contractVersion)
                ? "The item attribute request exceeded a permitted bound or contained an invalid value."
                : "The requested item attribute contract version is not supported.");

    private static AgentIpcCommandResponse GetFailure(ItemAttributesGetRequest request, StorageIpcFailure failure) =>
        AgentIpcCommandResponse.Create(
            ItemAttributeIpcMessageTypes.GetResponse,
            new ItemAttributesGetResponse(request.ContractVersion, request.Address, Attributes: null, failure));

    private static AgentIpcCommandResponse PermissionsResult(ItemPermissionsSetRequest request, StorageIpcFailure? failure) =>
        AgentIpcCommandResponse.Create(
            ItemAttributeIpcMessageTypes.PermissionsSetResponse,
            new ItemPermissionsSetResponse(request.ContractVersion, request.Address, Changed: failure is null, failure));

    private static AgentIpcCommandResponse ModifiedTimeResult(ItemModifiedTimeSetRequest request, StorageIpcFailure? failure) =>
        AgentIpcCommandResponse.Create(
            ItemAttributeIpcMessageTypes.ModifiedTimeSetResponse,
            new ItemModifiedTimeSetResponse(request.ContractVersion, request.Address, Changed: failure is null, failure));
}
