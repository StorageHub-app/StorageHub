using StorageHub.Domain.Storage;
using StorageHub.Storage.Abstractions;

namespace StorageHub.Sync;

public sealed record ContentDigest
{
    public ContentDigest(string algorithm, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithm);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        Algorithm = algorithm.ToUpperInvariant();
        Value = value;
    }

    public string Algorithm { get; }

    public string Value { get; }
}

public sealed record SyncItemObservation
{
    private SyncItemObservation(
        bool exists,
        long length,
        ContentDigest? digest,
        string? versionId)
    {
        Exists = exists;
        Length = length;
        Digest = digest;
        VersionId = versionId;
    }

    public static SyncItemObservation Missing { get; } = new(false, 0, null, null);

    public bool Exists { get; }

    public long Length { get; }

    public ContentDigest? Digest { get; }

    public string? VersionId { get; }

    public static SyncItemObservation Present(
        long length,
        ContentDigest? digest,
        string? versionId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return new SyncItemObservation(true, length, digest, Normalize(versionId));
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

public sealed record SyncBaselineObservation
{
    private SyncBaselineObservation(
        bool exists,
        long length,
        ContentDigest? digest,
        string? leftVersionId,
        string? rightVersionId)
    {
        Exists = exists;
        Length = length;
        Digest = digest;
        LeftVersionId = leftVersionId;
        RightVersionId = rightVersionId;
    }

    public static SyncBaselineObservation Missing { get; } =
        new(false, 0, null, null, null);

    public bool Exists { get; }

    public long Length { get; }

    public ContentDigest? Digest { get; }

    public string? LeftVersionId { get; }

    public string? RightVersionId { get; }

    public static SyncBaselineObservation Present(
        long length,
        ContentDigest? digest,
        string? leftVersionId,
        string? rightVersionId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        return new SyncBaselineObservation(
            true,
            length,
            digest,
            Normalize(leftVersionId),
            Normalize(rightVersionId));
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>
/// Whether one side of a synchronized pair is still what the last verified baseline recorded.
/// </summary>
/// <remarks>
/// A side's evidence is compared only with that same side's evidence from the baseline: its own
/// version ID or ETag, or a portable SHA-256 of its content. An ETag is never compared with the
/// other side's, and never stands in for a hash. Contradicting evidence wins: a matching tag beside
/// a hash that differs from the baseline's is a change.
/// </remarks>
internal static class SyncBaselineEvidence
{
    internal static string? Version(StorageEntry entry) =>
        entry.Address.VersionId ?? entry.Address.EntityTag ?? entry.ETag;

    internal static ContentDigest? Digest(PortableContentDigest? digest) =>
        digest is null ? null : new ContentDigest(digest.AlgorithmName, digest.Value);

    /// <summary>True only when this side is proven unchanged since <paramref name="baseline"/>.</summary>
    internal static bool SideUnchanged(
        SyncBaselineObservation baseline,
        string? baselineVersion,
        StorageEntry current,
        PortableContentDigest? currentDigest)
    {
        if (!baseline.Exists || current.Size is not long size || size != baseline.Length)
        {
            return false;
        }

        var digest = Digest(currentDigest);
        if (baseline.Digest is not null && digest is not null)
        {
            return baseline.Digest == digest;
        }

        var version = Version(current);
        return baselineVersion is not null &&
            version is not null &&
            StringComparer.Ordinal.Equals(baselineVersion, version);
    }
}
