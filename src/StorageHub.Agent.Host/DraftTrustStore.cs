using StorageHub.Security;

namespace StorageHub.Agent.Host;

/// <summary>
/// The saved trust, with the fingerprint typed in the editor added as trusted, for testing a
/// connection before it is saved. Saving pins that fingerprint; until then it is trusted by this
/// store only, which is dropped with the test.
/// </summary>
/// <remarks>
/// A fingerprint the user rejected or revoked stays refused: the typed one is added only when no
/// saved record names the same key. Nothing is written through it.
/// </remarks>
internal sealed class DraftTrustStore(ITrustStore saved, TrustRecord typed) : ITrustStore
{
    private readonly ITrustStore _saved = saved ?? throw new ArgumentNullException(nameof(saved));
    private readonly TrustRecord _typed = typed ?? throw new ArgumentNullException(nameof(typed));

    public async ValueTask<IReadOnlyList<TrustRecord>> FindAsync(
        TrustArtifactKind artifactKind,
        string canonicalHost,
        int port,
        CancellationToken cancellationToken = default)
    {
        var records = await _saved.FindAsync(artifactKind, canonicalHost, port, cancellationToken)
            .ConfigureAwait(false);
        if (artifactKind != _typed.ArtifactKind ||
            port != _typed.Port ||
            !string.Equals(canonicalHost, _typed.CanonicalHost, StringComparison.OrdinalIgnoreCase) ||
            records.Any(record => SshFingerprints.Equivalent(record.Sha256Fingerprint, _typed.Sha256Fingerprint)))
        {
            return records;
        }

        return [.. records, _typed];
    }

    public ValueTask UpsertAsync(TrustRecord record, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("A connection test does not change what is trusted.");

    public ValueTask<bool> RemoveAsync(
        string trustId,
        int expectedVersion,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("A connection test does not change what is trusted.");
}
