using System.Text;

namespace StorageHub.Contracts.Ipc;

/// <summary>The independently versioned, read-only exact-object inspection contract.</summary>
public static class ObjectInspectorIpcContract
{
    public const int CurrentVersion = 1;

    public static bool IsSupported(int version) => version == CurrentVersion;
}

/// <summary>
/// Bounds for provider-controlled inspector data. The version page ceiling accounts for worst-case
/// JSON escaping of opaque provider identities and keeps responses below the normal IPC frame limit.
/// </summary>
public static class ObjectInspectorIpcLimits
{
    public const int MaximumVersionPageSize = 25;
    public const int MaximumRelativePathLength = StorageIpcLimits.MaximumRelativePathLength;
    public const int MaximumOpaqueIdentityLength = StorageIpcLimits.MaximumOpaqueIdentityLength;
    public const int MaximumContinuationTokenLength = StorageIpcLimits.MaximumContinuationTokenLength;
    public const int MaximumMetadataEntries = 1_024;
    public const int MaximumMetadataNameLength = 256;
    public const int MaximumMetadataCombinedBytes = 64 * 1_024;
    public const int MaximumTagEntries = 10;
    public const int MaximumTagNameLength = 128;
    public const int MaximumTagValueLength = 256;

    /// <summary>
    /// The most the inspector reads to compute a checksum the server does not report. Larger files
    /// are answered only by their server.
    /// </summary>
    public const long MaximumComputedChecksumBytes = 256L * 1_024 * 1_024;
}

public static class ObjectInspectorIpcMessageTypes
{
    public const string VersionListRequest = "object-inspector.versions.list.request";
    public const string VersionListResponse = "object-inspector.versions.list.response";
    public const string MetadataGetRequest = "object-inspector.metadata.get.request";
    public const string MetadataGetResponse = "object-inspector.metadata.get.response";
    public const string TagsGetRequest = "object-inspector.tags.get.request";
    public const string TagsGetResponse = "object-inspector.tags.get.response";
    public const string ChecksumGetRequest = "object-inspector.checksum.get.request";
    public const string ChecksumGetResponse = "object-inspector.checksum.get.response";
}

/// <summary>An exact object identity captured from a root-scoped browsing session.</summary>
public sealed record ObjectInspectorAddress(
    Guid ConnectionId,
    string RootIdentity,
    string RelativePath,
    string? NativeItemId = null,
    string? VersionId = null,
    string? EntityTag = null)
{
    public bool HasValidBounds =>
        ConnectionId != Guid.Empty &&
        IsRequiredOpaque(RootIdentity) &&
        !string.IsNullOrWhiteSpace(RelativePath) &&
        RelativePath.Length <= ObjectInspectorIpcLimits.MaximumRelativePathLength &&
        !RelativePath.Any(char.IsControl) &&
        IsOptionalOpaque(NativeItemId) &&
        IsOptionalOpaque(VersionId) &&
        IsOptionalOpaque(EntityTag);

    public static bool IsRequiredOpaque(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= ObjectInspectorIpcLimits.MaximumOpaqueIdentityLength &&
        !value.Any(char.IsControl);

    public static bool IsOptionalOpaque(string? value) => value is null || IsRequiredOpaque(value);
}

public sealed record ObjectVersionListRequest(
    int ContractVersion,
    ObjectInspectorAddress Address,
    int PageSize = ObjectInspectorIpcLimits.MaximumVersionPageSize,
    string? ContinuationToken = null,
    bool IncludeDeleteMarkers = true)
{
    public bool HasValidBounds =>
        ObjectInspectorIpcContract.IsSupported(ContractVersion) &&
        Address?.HasValidBounds == true &&
        Address.VersionId is null &&
        PageSize is >= 1 and <= ObjectInspectorIpcLimits.MaximumVersionPageSize &&
        IsOptionalToken(ContinuationToken);

    public static bool IsOptionalToken(string? value) => value is null ||
        value.Length is > 0 and <= ObjectInspectorIpcLimits.MaximumContinuationTokenLength &&
        !value.Any(char.IsControl);
}

public sealed record ObjectVersionSummary(
    string VersionId,
    string? EntityTag,
    long? Size,
    DateTimeOffset? LastModifiedUtc,
    bool IsLatest,
    bool IsDeleteMarker)
{
    public bool HasValidBounds =>
        ObjectInspectorAddress.IsRequiredOpaque(VersionId) &&
        ObjectInspectorAddress.IsOptionalOpaque(EntityTag) &&
        Size is null or >= 0;
}

public sealed record ObjectVersionListResponse(
    int ContractVersion,
    ObjectInspectorAddress Address,
    ObjectVersionSummary[] Versions,
    string? ContinuationToken,
    StorageIpcFailure? Failure = null);

public sealed record ObjectMetadataGetRequest(
    int ContractVersion,
    ObjectInspectorAddress Address)
{
    public bool HasValidBounds =>
        ObjectInspectorIpcContract.IsSupported(ContractVersion) &&
        Address?.HasValidBounds == true;
}

public sealed record ObjectMetadataEntry(string Name, string Value)
{
    public bool HasValidBounds =>
        IsPortableMetadataName(Name) &&
        Value is not null &&
        !Value.Any(char.IsControl);

    internal static bool IsPortableMetadataName(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= ObjectInspectorIpcLimits.MaximumMetadataNameLength &&
        value.All(static character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
}

public sealed record ObjectMetadataGetResponse(
    int ContractVersion,
    ObjectInspectorAddress Address,
    ObjectMetadataEntry[] Metadata,
    StorageIpcFailure? Failure = null)
{
    public bool HasValidMetadataBounds =>
        Metadata is not null &&
        Metadata.Length <= ObjectInspectorIpcLimits.MaximumMetadataEntries &&
        Metadata.All(static entry => entry?.HasValidBounds == true) &&
        Metadata.Select(static entry => entry.Name).Distinct(StringComparer.Ordinal).Count() == Metadata.Length &&
        GetCombinedUtf8Bytes(Metadata) <= ObjectInspectorIpcLimits.MaximumMetadataCombinedBytes;

    private static long GetCombinedUtf8Bytes(IEnumerable<ObjectMetadataEntry> entries)
    {
        long bytes = 0;
        foreach (var entry in entries)
        {
            bytes += Encoding.UTF8.GetByteCount(entry.Name);
            bytes += Encoding.UTF8.GetByteCount(entry.Value);
        }

        return bytes;
    }
}

public sealed record ObjectTagsGetRequest(
    int ContractVersion,
    ObjectInspectorAddress Address)
{
    public bool HasValidBounds =>
        ObjectInspectorIpcContract.IsSupported(ContractVersion) &&
        Address?.HasValidBounds == true;
}

public sealed record ObjectTagEntry(string Name, string Value)
{
    public bool HasValidBounds =>
        !string.IsNullOrEmpty(Name) &&
        Name.Length <= ObjectInspectorIpcLimits.MaximumTagNameLength &&
        Name.All(IsPortableTagCharacter) &&
        Value is not null &&
        Value.Length <= ObjectInspectorIpcLimits.MaximumTagValueLength &&
        Value.All(IsPortableTagCharacter);

    private static bool IsPortableTagCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is ' ' or '+' or '-' or '.' or '/' or ':' or '=' or '_';
}

public sealed record ObjectTagsGetResponse(
    int ContractVersion,
    ObjectInspectorAddress Address,
    ObjectTagEntry[] Tags,
    StorageIpcFailure? Failure = null)
{
    public bool HasValidTagBounds =>
        Tags is not null &&
        Tags.Length <= ObjectInspectorIpcLimits.MaximumTagEntries &&
        Tags.All(static entry => entry?.HasValidBounds == true) &&
        Tags.Select(static entry => entry.Name).Distinct(StringComparer.Ordinal).Count() == Tags.Length;
}

/// <summary>Where an inspected checksum came from.</summary>
public enum ObjectChecksumSource
{
    /// <summary>Computed by the agent from the file's bytes.</summary>
    Computed = 0,

    /// <summary>Reported by the server for the whole file, without reading it.</summary>
    Server = 1,
}

/// <summary>
/// Asks for the file's SHA-256. With <paramref name="PreferServer"/> a digest the server keeps
/// answers first; otherwise, or when it has none, the agent reads up to
/// <see cref="ObjectInspectorIpcLimits.MaximumComputedChecksumBytes"/> and computes it.
/// </summary>
public sealed record ObjectChecksumGetRequest(
    int ContractVersion,
    ObjectInspectorAddress Address,
    bool PreferServer = true)
{
    public bool HasValidBounds =>
        ObjectInspectorIpcContract.IsSupported(ContractVersion) &&
        Address?.HasValidBounds == true;
}

public sealed record ObjectChecksumGetResponse(
    int ContractVersion,
    ObjectInspectorAddress Address,
    string? Algorithm,
    string? Value,
    ObjectChecksumSource Source,
    long BytesCovered,
    StorageIpcFailure? Failure = null)
{
    public const string Sha256 = "SHA256";

    public bool HasValidResultBounds => Failure is null
        ? Algorithm == Sha256 &&
          Value is { Length: 64 } &&
          Value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f') &&
          Enum.IsDefined(Source) &&
          BytesCovered >= 0
        : Algorithm is null && Value is null && BytesCovered == 0;
}
