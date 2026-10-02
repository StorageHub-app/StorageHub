using System.Text.Json.Serialization;

namespace StorageHub.Contracts.Ipc;

/// <summary>
/// The connections panel's groups, which the agent keeps beside the connections. Part of the
/// saved-connection management contract, at its version: a connection names its group in its
/// metadata (<see cref="ConnectionProfileMetadataDocument.GroupId"/>).
/// </summary>
public static class ConnectionGroupIpcMessageTypes
{
    public const string ListRequest = "connection.group.list.request";
    public const string ListResponse = "connection.group.list.response";
    public const string CreateRequest = "connection.group.create.request";
    public const string CreateResponse = "connection.group.create.response";
    public const string UpdateRequest = "connection.group.update.request";
    public const string UpdateResponse = "connection.group.update.response";
    public const string MoveRequest = "connection.group.move.request";
    public const string MoveResponse = "connection.group.move.response";
    public const string DeleteRequest = "connection.group.delete.request";
    public const string DeleteResponse = "connection.group.delete.response";
    public const string AssignRequest = "connection.group.assign.request";
    public const string AssignResponse = "connection.group.assign.response";
    public const string ImportRequest = "connection.group.import.request";
    public const string ImportResponse = "connection.group.import.response";
}

public static class ConnectionGroupIpcLimits
{
    public const int MaximumNameLength = 128;
    public const int MaximumGroups = 500;
    public const int MaximumMembersPerImportedGroup = 1_000;
}

[JsonConverter(typeof(JsonStringEnumConverter<ConnectionGroupWriteStatus>))]
public enum ConnectionGroupWriteStatus
{
    Succeeded = 1,
    NotFound = 2,
    NameConflict = 3,
    ValidationFailed = 4,
    Unavailable = 5,

    /// <summary>An import found an arrangement already brought in, and changed nothing.</summary>
    AlreadyImported = 6
}

/// <summary>One group, as the panel and the connection editor show it.</summary>
/// <param name="ColorKey">The group's colour as #RRGGBB, or null for none.</param>
public sealed record ConnectionGroupDocument(
    Guid GroupId,
    string Name,
    int SortOrder,
    string? IconKey,
    string? ColorKey,
    long Version,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc)
{
    public bool HasValidBounds =>
        GroupId != Guid.Empty &&
        ConnectionGroupRequests.IsValidName(Name) &&
        ConnectionGroupRequests.IsValidAppearance(IconKey, ColorKey) &&
        Version > 0;
}

public sealed record ConnectionGroupListRequest(int ContractVersion)
{
    public bool HasValidBounds => ContractVersion > 0;
}

/// <summary>Every group, in the order the panel shows them.</summary>
public sealed record ConnectionGroupListResponse(
    int ContractVersion,
    ConnectionGroupDocument[] Groups,
    StorageIpcFailure? Failure = null);

public sealed record ConnectionGroupCreateRequest(
    int ContractVersion,
    string Name,
    string? IconKey = null,
    string? ColorKey = null)
{
    public bool HasValidBounds =>
        ContractVersion > 0 &&
        ConnectionGroupRequests.IsValidName(Name) &&
        ConnectionGroupRequests.IsValidAppearance(IconKey, ColorKey);
}

/// <summary>Renames a group and sets its icon and colour; each is written as given.</summary>
public sealed record ConnectionGroupUpdateRequest(
    int ContractVersion,
    Guid GroupId,
    string Name,
    string? IconKey,
    string? ColorKey)
{
    public bool HasValidBounds =>
        ContractVersion > 0 &&
        GroupId != Guid.Empty &&
        ConnectionGroupRequests.IsValidName(Name) &&
        ConnectionGroupRequests.IsValidAppearance(IconKey, ColorKey);
}

/// <summary>Moves a group to a position among the others, counted from nought.</summary>
public sealed record ConnectionGroupMoveRequest(int ContractVersion, Guid GroupId, int Index)
{
    public bool HasValidBounds => ContractVersion > 0 && GroupId != Guid.Empty && Index is >= 0 and < ConnectionGroupIpcLimits.MaximumGroups;
}

/// <summary>Removes a group; its connections go to Ungrouped.</summary>
public sealed record ConnectionGroupDeleteRequest(int ContractVersion, Guid GroupId)
{
    public bool HasValidBounds => ContractVersion > 0 && GroupId != Guid.Empty;
}

/// <summary>Files a connection in a group, or in Ungrouped when the group is null.</summary>
public sealed record ConnectionGroupAssignRequest(int ContractVersion, Guid ConnectionId, Guid? GroupId)
{
    public bool HasValidBounds => ContractVersion > 0 && ConnectionId != Guid.Empty && GroupId != Guid.Empty;
}

/// <summary>One group of the arrangement the desktop kept before groups were the agent's.</summary>
public sealed record ConnectionGroupImportEntry(
    string Name,
    string? IconKey,
    string? ColorKey,
    Guid[] Members)
{
    public bool HasValidBounds =>
        ConnectionGroupRequests.IsValidName(Name) &&
        ConnectionGroupRequests.IsValidAppearance(IconKey, ColorKey) &&
        Members is { Length: <= ConnectionGroupIpcLimits.MaximumMembersPerImportedGroup } &&
        Members.All(static member => member != Guid.Empty);
}

/// <summary>
/// Brings in the desktop's old arrangement, once: a second import, from this desktop or another,
/// is answered with <see cref="ConnectionGroupWriteStatus.AlreadyImported"/> and changes nothing.
/// </summary>
public sealed record ConnectionGroupImportRequest(int ContractVersion, ConnectionGroupImportEntry[] Groups)
{
    public bool HasValidBounds =>
        ContractVersion > 0 &&
        Groups is { Length: <= ConnectionGroupIpcLimits.MaximumGroups } &&
        Groups.All(static group => group is { HasValidBounds: true });
}

/// <summary>
/// What a write did, with every group as it is afterwards, so the caller can draw them without
/// asking again.
/// </summary>
/// <param name="ConnectionVersion">An assignment's connection, at the version it was moved to.</param>
public sealed record ConnectionGroupWriteResponse(
    int ContractVersion,
    ConnectionGroupWriteStatus Status,
    ConnectionGroupDocument? Group = null,
    ConnectionGroupDocument[]? Groups = null,
    long? ConnectionVersion = null,
    StorageIpcFailure? Failure = null);

internal static class ConnectionGroupRequests
{
    internal static bool IsValidName(string? name) =>
        ConnectionProfileMetadataDocument.IsSafeText(name, ConnectionGroupIpcLimits.MaximumNameLength, required: true);

    internal static bool IsValidAppearance(string? iconKey, string? colorKey) =>
        ConnectionProfileMetadataDocument.IsSafeText(iconKey, ConnectionProfileIpcLimits.MaximumIconKeyLength) &&
        ConnectionProfileMetadataDocument.IsValidAccentColor(colorKey);
}
