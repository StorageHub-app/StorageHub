namespace StorageHub.Contracts.Ipc;

/// <summary>
/// The item-attribute contract: reading an item's permissions, owner, link target and times, and
/// changing its permission bits or modified time. Versioned on its own, beside the read-only
/// inspector contract, because it changes things.
/// </summary>
public static class ItemAttributeIpcContract
{
    public const int CurrentVersion = 1;

    public static bool IsSupported(int version) => version == CurrentVersion;
}

/// <summary>Bounds for item attributes, both ways.</summary>
/// <remarks>
/// The mode is the twelve bits CL.Storage accepts: read, write and execute for owner, group and
/// others, plus set-user-ID, set-group-ID and sticky (octal 7777). The modified time is bounded by
/// what the narrowest provider can store, SFTP's unsigned 32-bit seconds since 1970, so a time this
/// accepts is one every connection that can set times can also keep.
/// </remarks>
public static class ItemAttributeIpcLimits
{
    public const int MaximumUnixMode = 0xFFF;
    public const int MaximumPrincipalNameLength = 256;
    public const int MaximumLinkTargetLength = 4_096;

    public static DateTimeOffset EarliestModifiedTimeUtc { get; } = DateTimeOffset.UnixEpoch;

    public static DateTimeOffset LatestModifiedTimeUtc { get; } =
        DateTimeOffset.FromUnixTimeSeconds(uint.MaxValue);

    /// <summary>Whether a modified time can be sent: in UTC, and within what every provider stores.</summary>
    public static bool IsSettableModifiedTime(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero &&
        value >= EarliestModifiedTimeUtc &&
        value <= LatestModifiedTimeUtc;
}

public static class ItemAttributeIpcMessageTypes
{
    public const string GetRequest = "item-attributes.get.request";
    public const string GetResponse = "item-attributes.get.response";
    public const string PermissionsSetRequest = "item-attributes.permissions.set.request";
    public const string PermissionsSetResponse = "item-attributes.permissions.set.response";
    public const string ModifiedTimeSetRequest = "item-attributes.modified-time.set.request";
    public const string ModifiedTimeSetResponse = "item-attributes.modified-time.set.response";
}

/// <summary>
/// One item's attributes as the connection reports them, and which of them it can change.
/// </summary>
/// <remarks>
/// Every value but the kind is optional: a provider reports the subset it has, and what it does
/// not report is null rather than a guess. Owner and group come as names (FTP), as numeric IDs
/// (SFTP), or not at all. The two flags say what the connection advertises; a server can still
/// refuse one change for one item, and the response to that change says so.
/// </remarks>
public sealed record ItemAttributes(
    StorageItemKind Kind,
    long? Size = null,
    DateTimeOffset? ModifiedUtc = null,
    DateTimeOffset? CreatedUtc = null,
    DateTimeOffset? AccessedUtc = null,
    int? UnixMode = null,
    string? Owner = null,
    string? Group = null,
    long? OwnerId = null,
    long? GroupId = null,
    string? LinkTarget = null,
    bool CanSetPermissions = false,
    bool CanSetModifiedTime = false)
{
    public bool HasValidBounds =>
        Enum.IsDefined(Kind) &&
        Size is null or >= 0 &&
        IsUtc(ModifiedUtc) && IsUtc(CreatedUtc) && IsUtc(AccessedUtc) &&
        UnixMode is null or >= 0 and <= ItemAttributeIpcLimits.MaximumUnixMode &&
        IsPrincipalName(Owner) && IsPrincipalName(Group) &&
        OwnerId is null or >= 0 && GroupId is null or >= 0 &&
        IsLinkTarget(LinkTarget);

    public static bool IsPrincipalName(string? value) => value is null ||
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= ItemAttributeIpcLimits.MaximumPrincipalNameLength &&
        !value.Any(char.IsControl);

    public static bool IsLinkTarget(string? value) => value is null ||
        value.Length is > 0 and <= ItemAttributeIpcLimits.MaximumLinkTargetLength &&
        !value.Any(char.IsControl);

    private static bool IsUtc(DateTimeOffset? value) => value is not { Offset: var offset } || offset == TimeSpan.Zero;
}

public sealed record ItemAttributesGetRequest(
    int ContractVersion,
    ObjectInspectorAddress Address)
{
    public bool HasValidBounds =>
        ItemAttributeIpcContract.IsSupported(ContractVersion) &&
        Address?.HasValidBounds == true;
}

public sealed record ItemAttributesGetResponse(
    int ContractVersion,
    ObjectInspectorAddress Address,
    ItemAttributes? Attributes,
    StorageIpcFailure? Failure = null);

public sealed record ItemPermissionsSetRequest(
    int ContractVersion,
    ObjectInspectorAddress Address,
    int UnixMode)
{
    public bool HasValidBounds =>
        ItemAttributeIpcContract.IsSupported(ContractVersion) &&
        Address?.HasValidBounds == true &&
        UnixMode is >= 0 and <= ItemAttributeIpcLimits.MaximumUnixMode;
}

public sealed record ItemPermissionsSetResponse(
    int ContractVersion,
    ObjectInspectorAddress Address,
    bool Changed,
    StorageIpcFailure? Failure = null);

public sealed record ItemModifiedTimeSetRequest(
    int ContractVersion,
    ObjectInspectorAddress Address,
    DateTimeOffset ModifiedUtc)
{
    public bool HasValidBounds =>
        ItemAttributeIpcContract.IsSupported(ContractVersion) &&
        Address?.HasValidBounds == true &&
        ItemAttributeIpcLimits.IsSettableModifiedTime(ModifiedUtc);
}

public sealed record ItemModifiedTimeSetResponse(
    int ContractVersion,
    ObjectInspectorAddress Address,
    bool Changed,
    StorageIpcFailure? Failure = null);
