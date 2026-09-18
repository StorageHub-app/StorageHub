using CodeLogic.Core.Localization;

namespace StorageHub.Desktop.Localization;

/// <summary>
/// The key store: stored private keys and certificates.
/// </summary>
/// <remarks>
/// Flat string properties only, and no property may be named <c>Culture</c>. See
/// <see cref="CommandStrings"/> for why.
/// </remarks>
[LocalizationSection("keystore")]
internal sealed class KeyStoreStrings : LocalizationModelBase
{
    public string AnotherEntryAlreadyUsesThatName { get; set; } = "Another entry already uses that name.";

    public string Cancel { get; set; } = "Cancel";

    public string Certificate { get; set; } = "Certificate";

    public string CertificatePassword { get; set; } = "Certificate password";

    public string ChooseFromKeyStore { get; set; } = "Choose from Key Store";

    public string Delete { get; set; } = "Delete";

    public string Deleted { get; set; } = "Deleted.";

    public string Expired { get; set; } = "Expired";

    public string Expires { get; set; } = "Expires";

    public string Identity { get; set; } = "Identity";

    public string ImportSSHKey { get; set; } = "Import SSH key...";

    public string ImportCertificate { get; set; } = "Import certificate...";

    public string KeyStore { get; set; } = "Key Store";

    public string KeyPassphrase { get; set; } = "Key passphrase";

    public string KeyStoreSearch { get; set; } = "Key store search";

    public string LegacyPEM { get; set; } = "Legacy PEM";

    public string Loading { get; set; } = "Loading...";

    public string NameThisEntry { get; set; } = "Name this entry";

    public string NoKeysOrCertificatesAreStoredYet { get; set; } = "No keys or certificates are stored yet.";

    public string OpenSSHOpensshKeyV1 { get; set; } = "OpenSSH (openssh-key-v1)";

    public string PKCS12CertificatesPfxP12PfxP12 { get; set; } = "PKCS#12 certificates (*.pfx;*.p12)|*.pfx;*.p12";

    public string PKCS8 { get; set; } = "PKCS#8";

    /// <summary>
    /// Named ...Label because a name ending in Format means a composite format string everywhere
    /// else in these models, and this is an ordinary field caption.
    /// </summary>
    public string PrivateKeyFormatLabel { get; set; } = "Private key format";

    public string PrivateKeysKeyPemKeyPem { get; set; } = "Private keys (*.key;*.pem;*)|*.key;*.pem;*";

    public string RenameEntry { get; set; } = "Rename entry";

    public string Rename { get; set; } = "Rename...";

    public string Renamed { get; set; } = "Renamed.";

    public string SSHKey { get; set; } = "SSH key";

    public string SearchNameOrDescription { get; set; } = "Search name or description";

    public string SelectMaterial { get; set; } = "Select material";

    public string StorageHubCannotStoreAnUnprotectedPrivateKey { get; set; } = "StorageHub cannot store an unprotected private key. Add a passphrase to the key, then import it.";

    public string StoredKeysAndCertificates { get; set; } = "Stored keys and certificates";

    public string TheEntryChangedElsewhereReopenTheKey { get; set; } = "The entry changed elsewhere. Reopen the key store and try again.";

    public string TheEntryIsStillUsedByA { get; set; } = "The entry is still used by a saved connection.";

    public string TheEntryNoLongerExists { get; set; } = "The entry no longer exists.";

    public string TheKeyStoreRejectedTheRequest { get; set; } = "The key store rejected the request.";

    public string TheMaterialCouldNotBeEnrolled { get; set; } = "The material could not be enrolled.";

    public string ThePassphraseCouldNotBeEnrolled { get; set; } = "The passphrase could not be enrolled.";

    public string TheSelectedFileIsEmptyOrLarger { get; set; } = "The selected file is empty or larger than 16 MB.";

    public string UsedBy { get; set; } = "Used by";

    public string WhichEnvelopeDoesTheKeyUse { get; set; } = "Which envelope does the key use?";
}
