using System.Text.Json.Serialization;

namespace StorageHub.Contracts.Ipc;

/// <summary>The independently versioned, read-only storage browsing contract.</summary>
public static class StorageIpcContract
{
    public const int LegacyVersion = 1;
    public const int StableIdentityVersion = 2;
    public const int CurrentVersion = 3;

    public static bool IsSupported(int version) => version is >= LegacyVersion and <= CurrentVersion;

    public static bool SupportsStableItemIdentities(int version) => version >= StableIdentityVersion;

    public static bool SupportsConnectionHealth(int version) => version >= CurrentVersion;
}

/// <summary>
/// Limits keep every read-only response comfortably below the normal IPC frame ceiling,
/// including JSON escaping of provider-controlled text.
/// </summary>
public static class StorageIpcLimits
{
    public const int MaximumConnectionResults = 100;
    public const int MaximumSearchTextLength = 256;
    public const int MaximumStoragePageSize = 250;
    public const int MaximumStableIdentityPageSize = 40;
    public const int MaximumRelativePathLength = 4_096;
    public const int MaximumItemNameLength = 512;
    public const int MaximumContentTypeLength = 128;
    public const int MaximumContinuationTokenLength = 8_192;
    public const int MaximumOpaqueIdentityLength = 8_192;
    public const int MaximumFailureCodeLength = 128;
    public const int MaximumFailureMessageLength = 256;

    public static int GetMaximumStoragePageSize(int contractVersion) =>
        StorageIpcContract.SupportsStableItemIdentities(contractVersion)
            ? MaximumStableIdentityPageSize
            : MaximumStoragePageSize;
}

public static class StorageIpcMessageTypes
{
    public const string ConnectionListRequest = "connection.list.request";
    public const string ConnectionListResponse = "connection.list.response";
    public const string ConnectionTestRequest = "connection.test.request";
    public const string ConnectionTestResponse = "connection.test.response";
    public const string ConnectionDraftTestRequest = "connection.test-draft.request";
    public const string ConnectionDraftTestResponse = "connection.test-draft.response";
    public const string ConnectionDetailsRequest = "connection.details.request";
    public const string ConnectionDetailsResponse = "connection.details.response";
    public const string StorageListRequest = "storage.list.request";
    public const string StorageListResponse = "storage.list.response";
}

[JsonConverter(typeof(JsonStringEnumConverter<StorageConnectionProvider>))]
public enum StorageConnectionProvider
{
    Local = 1,
    S3 = 2,
    Ftp = 3,
    Ftps = 4,
    Sftp = 5,
    Ssh = 6,
    WebDav = 7,
    AzureBlob = 8
}

[JsonConverter(typeof(JsonStringEnumConverter<StorageIpcFailureCategory>))]
public enum StorageIpcFailureCategory
{
    Validation,
    NotFound,
    Conflict,
    Unsupported,
    Unauthorized,
    Unavailable,
    Timeout,
    Cancelled,
    Integrity,
    Security,
    Provider,
    Unexpected
}

[JsonConverter(typeof(JsonStringEnumConverter<StorageItemKind>))]
public enum StorageItemKind
{
    File,
    Directory,
    Prefix,
    SymbolicLink,
    Other
}

public sealed record StorageIpcFailure(
    string Code,
    StorageIpcFailureCategory Category,
    string Message,
    bool IsTransient);

public sealed record ConnectionListRequest(
    int ContractVersion = StorageIpcContract.CurrentVersion,
    string? SearchText = null,
    StorageConnectionProvider? Provider = null,
    bool IncludeDisabled = false,
    int Limit = 50)
{
    public bool HasValidBounds =>
        ContractVersion > 0 &&
        Limit is >= 1 and <= StorageIpcLimits.MaximumConnectionResults &&
        (SearchText is null ||
            SearchText.Length <= StorageIpcLimits.MaximumSearchTextLength &&
            !SearchText.Any(char.IsControl)) &&
        (Provider is null || Enum.IsDefined(Provider.Value));
}

public sealed record ConnectionSummary(
    Guid ConnectionId,
    string DisplayName,
    StorageConnectionProvider Provider,
    string? FolderPath,
    string[] Tags,
    bool IsFavorite,
    bool IsEnabled,
    string? IconKey,
    string? AccentColor,
    long Version,
    ConnectionProfileType Type = ConnectionProfileType.Storage,
    ConnectionHealthSnapshot? Health = null,
    Guid? GroupId = null);

[JsonConverter(typeof(JsonStringEnumConverter<ConnectionHealthState>))]
public enum ConnectionHealthState
{
    Healthy = 1,
    NeedsAttention = 2,
    Unavailable = 3
}

public sealed record ConnectionHealthSnapshot(
    ConnectionHealthState State,
    DateTimeOffset CheckedUtc,
    long ElapsedMilliseconds,
    string Status,
    bool RequiresCredentialAction = false,
    bool RequiresTrustAction = false)
{
    public bool HasValidBounds =>
        Enum.IsDefined(State) &&
        CheckedUtc != default &&
        CheckedUtc.Offset == TimeSpan.Zero &&
        ElapsedMilliseconds >= 0 &&
        ConnectionProfileMetadataDocument.IsSafeText(
            Status,
            StorageIpcLimits.MaximumFailureMessageLength,
            required: true) &&
        !(RequiresCredentialAction && RequiresTrustAction) &&
        (State == ConnectionHealthState.NeedsAttention ||
            !RequiresCredentialAction && !RequiresTrustAction);
}

public sealed record ConnectionListResponse(
    int ContractVersion,
    ConnectionSummary[] Connections,
    StorageIpcFailure? Failure = null);

public sealed record ConnectionTestRequest(
    int ContractVersion,
    Guid ConnectionId)
{
    public bool HasValidBounds => ContractVersion > 0 && ConnectionId != Guid.Empty;
}

public sealed record ConnectionTestResponse(
    int ContractVersion,
    Guid ConnectionId,
    bool Succeeded,
    long ElapsedMilliseconds,
    StorageIpcFailure? Failure = null);

/// <summary>
/// Asks the agent to reach a connection as it stands in the editor, saved or not. Nothing is
/// written: the profile is built for the test and dropped after it, and a fingerprint typed in the
/// editor is trusted for this test only, as saving would pin it.
/// </summary>
/// <param name="ConnectionId">The saved connection being edited, if it is one.</param>
/// <param name="Sha256Fingerprint">
/// The host key or certificate fingerprint in the editor, not pinned yet.
/// </param>
public sealed record ConnectionDraftTestRequest(
    int ContractVersion,
    ConnectionProfileDraft Draft,
    Guid? ConnectionId = null,
    string? Sha256Fingerprint = null)
{
    public bool HasValidBounds =>
        ContractVersion > 0 &&
        Draft is { HasValidBounds: true } &&
        ConnectionId != Guid.Empty &&
        (Sha256Fingerprint is null || ConnectionTrustIpcLimits.IsValidFingerprint(Sha256Fingerprint));
}

/// <summary>The steps of a connection test, in the order they are taken.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ConnectionTestStepKind>))]
public enum ConnectionTestStepKind
{
    /// <summary>The settings make a connection the agent can use.</summary>
    Settings,

    /// <summary>The server's host key or certificate is one the user trusts.</summary>
    ServerIdentity,

    /// <summary>The passwords and keys it names open from the vault.</summary>
    Credentials,

    /// <summary>The server answers and accepts the credentials.</summary>
    Connect,

    /// <summary>The starting folder can be listed.</summary>
    Browse
}

[JsonConverter(typeof(JsonStringEnumConverter<ConnectionTestStepOutcome>))]
public enum ConnectionTestStepOutcome
{
    Passed,
    Failed,

    /// <summary>An earlier step failed, so this one was not tried.</summary>
    NotReached,

    /// <summary>The connection has nothing to check here, such as a key for a local folder.</summary>
    NotApplicable
}

public sealed record ConnectionTestStep(
    ConnectionTestStepKind Kind,
    ConnectionTestStepOutcome Outcome,
    StorageIpcFailure? Failure = null);

public sealed record ConnectionDraftTestResponse(
    int ContractVersion,
    bool Succeeded,
    long ElapsedMilliseconds,
    ConnectionTestStep[] Steps,
    StorageIpcFailure? Failure = null);

/// <summary>Asks what a saved connection and the server behind it look like now.</summary>
public sealed record ConnectionDetailsRequest(
    int ContractVersion,
    Guid ConnectionId)
{
    public bool HasValidBounds => ContractVersion > 0 && ConnectionId != Guid.Empty;
}

public static class ConnectionDetailsLimits
{
    public const int MaximumTextLength = 256;
    public const int MaximumFeatures = 64;
    public const int MaximumNegotiated = 32;
}

/// <summary>
/// A live description of a connection: never a secret, only what the server presented, runs and
/// negotiated, and how the connection's sessions are doing.
/// </summary>
public sealed record ConnectionDetailsDocument(
    string Provider,
    string? Host,
    int? Port,
    string TransportSecurity,
    string? ServerSystem,
    string? ServerSoftware,
    string[] ServerFeatures,
    ConnectionNegotiatedSetting[] Negotiated,
    ConnectionPresentedIdentityDocument? PresentedIdentity = null,
    ConnectionSessionCountsDocument? Sessions = null,
    ConnectionHealthCheckDocument? LastHealthCheck = null)
{
    public bool HasValidBounds =>
        IsSafe(Provider, required: true) &&
        IsSafe(Host) &&
        Port is null or >= 0 and <= 65_535 &&
        IsSafe(TransportSecurity, required: true) &&
        IsSafe(ServerSystem) &&
        IsSafe(ServerSoftware) &&
        ServerFeatures is not null &&
        ServerFeatures.Length <= ConnectionDetailsLimits.MaximumFeatures &&
        ServerFeatures.All(static feature => IsSafe(feature, required: true)) &&
        Negotiated is not null &&
        Negotiated.Length <= ConnectionDetailsLimits.MaximumNegotiated &&
        Negotiated.All(static setting => setting is not null &&
            IsSafe(setting.Name, required: true) && IsSafe(setting.Value)) &&
        (PresentedIdentity is null ||
            IsSafe(PresentedIdentity.Kind, required: true) &&
            IsSafe(PresentedIdentity.Fingerprint, required: true) &&
            IsSafe(PresentedIdentity.Algorithm) &&
            IsSafe(PresentedIdentity.Subject) &&
            IsSafe(PresentedIdentity.Issuer)) &&
        (Sessions is null ||
            Sessions.Idle >= 0 && Sessions.InUse >= 0 && Sessions.Opened >= 0 &&
            Sessions.Closed >= 0 && Sessions.FailedProbes >= 0) &&
        (LastHealthCheck is null ||
            LastHealthCheck.LatencyMilliseconds >= 0 && IsSafe(LastHealthCheck.ErrorCode));

    internal static bool IsSafe(string? value, bool required = false) => value is null
        ? !required
        : value.Length is > 0 and <= ConnectionDetailsLimits.MaximumTextLength && !value.Any(char.IsControl);
}

public sealed record ConnectionNegotiatedSetting(string Name, string? Value);

/// <param name="Kind"><c>ssh-host-key</c> or <c>tls-certificate</c>.</param>
public sealed record ConnectionPresentedIdentityDocument(
    string Kind,
    string Fingerprint,
    string? Algorithm,
    string? Subject,
    string? Issuer,
    DateTimeOffset? NotAfter,
    bool Trusted);

public sealed record ConnectionSessionCountsDocument(int Idle, int InUse, int Opened, int Closed, int FailedProbes);

public sealed record ConnectionHealthCheckDocument(
    bool Healthy,
    string? ErrorCode,
    long LatencyMilliseconds,
    DateTimeOffset CheckedUtc);

public sealed record ConnectionDetailsResponse(
    int ContractVersion,
    Guid ConnectionId,
    ConnectionDetailsDocument? Details,
    StorageIpcFailure? Failure = null);

public sealed record StorageListPageRequest(
    int ContractVersion,
    Guid ConnectionId,
    string RelativePath,
    int PageSize = StorageIpcLimits.MaximumStableIdentityPageSize,
    string? ContinuationToken = null,
    bool IncludeVersions = false,
    bool Recursive = false)
{
    public bool HasValidBounds =>
        ContractVersion > 0 &&
        ConnectionId != Guid.Empty &&
        RelativePath is not null &&
        RelativePath.Length <= StorageIpcLimits.MaximumRelativePathLength &&
        !RelativePath.Any(char.IsControl) &&
        PageSize >= 1 && PageSize <= StorageIpcLimits.GetMaximumStoragePageSize(ContractVersion) &&
        (ContinuationToken is null ||
            ContinuationToken.Length <= StorageIpcLimits.MaximumContinuationTokenLength &&
            !ContinuationToken.Any(char.IsControl));
}

public sealed record StorageListItem(
    string Name,
    string RelativePath,
    StorageItemKind Kind,
    long? Size,
    DateTimeOffset? LastModifiedUtc,
    string? ContentType,
    bool IsContainer,
    string? NativeItemId = null,
    string? VersionId = null,
    string? EntityTag = null)
{
    public bool HasValidIdentityBounds =>
        IsBoundedIdentity(NativeItemId) &&
        IsBoundedIdentity(VersionId) &&
        IsBoundedIdentity(EntityTag);

    private static bool IsBoundedIdentity(string? value) => value is null ||
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= StorageIpcLimits.MaximumOpaqueIdentityLength &&
        !value.Any(char.IsControl);
}

public sealed record StorageListPageResponse(
    int ContractVersion,
    Guid ConnectionId,
    string RelativePath,
    StorageListItem[] Entries,
    string? ContinuationToken,
    StorageIpcFailure? Failure = null,
    string? RootIdentity = null)
{
    public bool HasValidRootIdentity =>
        !string.IsNullOrWhiteSpace(RootIdentity) &&
        RootIdentity.Length <= StorageIpcLimits.MaximumOpaqueIdentityLength &&
        !RootIdentity.Any(char.IsControl);
}
