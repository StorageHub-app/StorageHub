using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>Where the secret a reference names came from, as far as the desktop can tell.</summary>
internal enum SecretReferenceSource
{
    /// <summary>The field is empty.</summary>
    None,

    /// <summary>A key or certificate imported into the Key Store, which any connection can use.</summary>
    KeyStore,

    /// <summary>Enrolled straight into the vault for this one field.</summary>
    Vault,

    /// <summary>A Key Store entry this session saw, which the Key Store no longer lists.</summary>
    MissingFromKeyStore
}

/// <summary>
/// What a secret-reference field shows in place of its opaque reference: a badge saying where the
/// secret came from, and a name a person can recognise.
/// </summary>
/// <remarks>
/// The reference itself is kept, for the tooltip and for Copy reference: it is what the profile
/// stores and what a support conversation needs, but "shs_" and 43 random characters is not
/// something anybody reads to find out which key a connection uses.
/// </remarks>
internal sealed record SecretReferenceDisplay(
    SecretReferenceSource Source,
    string Badge,
    string Name,
    string Reference,
    string Detail = "")
{
    internal static SecretReferenceDisplay Empty { get; } = new(SecretReferenceSource.None, string.Empty, string.Empty, string.Empty);

    public bool HasBadge => Source != SecretReferenceSource.None;

    public bool IsKeyStore => Source == SecretReferenceSource.KeyStore;

    public bool IsVault => Source == SecretReferenceSource.Vault;

    public bool IsMissing => Source == SecretReferenceSource.MissingFromKeyStore;

    /// <summary>"Key Store: deploy-key", which is what a screen reader says for the field.</summary>
    public string AccessibleText => HasBadge ? Ui.Format(Ui.KeyStore.SourceAccessibleFormat, Badge, Name) : string.Empty;

    /// <summary>What the entry is, when known, then the raw reference.</summary>
    public string ToolTip
    {
        get
        {
            if (Reference.Length == 0) return string.Empty;
            var reference = Ui.Format(Ui.KeyStore.VaultReferenceFormat, Reference);
            return Detail.Length == 0 ? reference : Detail + Environment.NewLine + reference;
        }
    }
}

/// <summary>
/// The Key Store's entries by the vault references they hand out, so a field holding one can be
/// shown by name.
/// </summary>
/// <remarks>
/// <para>
/// A reference does not say where it came from: one the Key Store enrolled and one enrolled for a
/// single connection look the same. What does is whether the Key Store lists an entry with that
/// material or passphrase reference, so this is a lookup over the last listing.
/// </para>
/// <para>
/// Every entry ever listed is remembered for as long as this lives, which is what makes a deleted
/// one recognisable: a reference that was an entry's and is no longer on the listing is said to
/// be missing, under the name it had, rather than quietly turning into an anonymous vault secret.
/// </para>
/// </remarks>
internal sealed class SecretReferenceNames
{
    private readonly Dictionary<string, KeyStoreEntryDocument> _known = new(StringComparer.Ordinal);
    private HashSet<string> _listed = new(StringComparer.Ordinal);

    /// <summary>Takes a complete listing: anything known and not on it has gone.</summary>
    internal void Replace(IEnumerable<KeyStoreEntryDocument> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries) Add(entry);
    }

    /// <summary>Takes one entry, or a partial listing, without forgetting anything.</summary>
    internal void Add(KeyStoreEntryDocument entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        foreach (var reference in References(entry))
        {
            _known[reference] = entry;
            _listed.Add(reference);
        }
    }

    /// <summary>
    /// What a field holding a reference should show.
    /// </summary>
    /// <param name="vaultName">
    /// The name for a secret enrolled for the field itself, such as "Stored for this connection".
    /// </param>
    internal SecretReferenceDisplay Describe(string? reference, string vaultName)
    {
        var value = reference?.Trim() ?? string.Empty;
        if (value.Length == 0) return SecretReferenceDisplay.Empty;
        if (!_known.TryGetValue(value, out var entry)) return Vault(value, vaultName);

        var detail = DescribeEntry(entry);
        return _listed.Contains(value)
            ? new SecretReferenceDisplay(SecretReferenceSource.KeyStore, Ui.KeyStore.KeyStore, entry.DisplayName, value, detail)
            : new SecretReferenceDisplay(SecretReferenceSource.MissingFromKeyStore, Ui.KeyStore.MissingFromKeyStore, entry.DisplayName, value, detail);
    }

    /// <summary>A reference that can only be the field's own, for a screen with no Key Store to ask.</summary>
    internal static SecretReferenceDisplay Vault(string? reference, string vaultName)
    {
        var value = reference?.Trim() ?? string.Empty;
        return value.Length == 0
            ? SecretReferenceDisplay.Empty
            : new SecretReferenceDisplay(SecretReferenceSource.Vault, Ui.KeyStore.VaultBadge, vaultName, value);
    }

    /// <summary>"SSH key · SHA256:…", the same identity the Key Store's table shows.</summary>
    private static string DescribeEntry(KeyStoreEntryDocument entry) =>
        (entry.Kind is KeyStoreMaterialKind.Pkcs12Certificate ? Ui.KeyStore.Certificate : Ui.KeyStore.SSHKey) +
        " · " + KeyStoreRules.DescribeIdentity(entry);

    private static IEnumerable<string> References(KeyStoreEntryDocument entry)
    {
        yield return entry.MaterialReference;
        if (!string.IsNullOrEmpty(entry.PassphraseReference)) yield return entry.PassphraseReference;
    }
}
