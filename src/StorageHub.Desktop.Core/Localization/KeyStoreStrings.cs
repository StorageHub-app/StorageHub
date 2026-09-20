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

    public string PKCS8 { get; set; } = "PKCS#8";

    /// <summary>
    /// Named ...Label because a name ending in Format means a composite format string everywhere
    /// else in these models, and this is an ordinary field caption.
    /// </summary>
    public string PrivateKeyFormatLabel { get; set; } = "Private key format";

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

    // ------------------------------------------------------------- the manager
    /// <summary>{0} = how many entries are stored.</summary>
    public string StoredItemsFormat { get; set; } = "{0} stored item(s).";

    public string NothingMatches { get; set; } = "Nothing matches the search.";

    /// <summary>{0} = the entry's display name.</summary>
    public string ImportedFormat { get; set; } = "Imported '{0}'.";

    /// <summary>{0} = the entry's display name; {1} = the connections that use it.</summary>
    public string StillUsedByFormat { get; set; } = "'{0}' is still used by {1}.";

    /// <summary>{0} = the connections that use the entry.</summary>
    public string EntryStillUsedByFormat { get; set; } = "The entry is still used by {0}.";

    /// <summary>{0} = what went wrong.</summary>
    public string KeyStoreUnavailableFormat { get; set; } = "The key store is unavailable: {0}";

    public string UnknownSubject { get; set; } = "(unknown subject)";

    public string UnknownFingerprint { get; set; } = "(unknown fingerprint)";

    /// <summary>What the Expires column shows for a key, which has none.</summary>
    public string NoExpiry { get; set; } = "-";

    public string Name { get; set; } = "Name";

    public string Kind { get; set; } = "Kind";

    public string Tags { get; set; } = "Tags";

    public string Refresh { get; set; } = "Refresh";

    public string Use { get; set; } = "Use";

    public string PickerHint { get; set; } =
        "Only entries of the kind this field accepts are listed. The material itself never leaves the vault.";

    // ------------------------------------------------------------- importing
    public string ImportCertificateTitle { get; set; } = "Import certificate";

    public string ImportSshKeyTitle { get; set; } = "Import SSH key";

    public string File { get; set; } = "File";

    public string Browse { get; set; } = "Browse…";

    public string Import { get; set; } = "Import";

    public string Importing { get; set; } = "Importing…";

    public string ImportHint { get; set; } =
        "The file is encrypted into the vault as it is imported. Only an opaque reference is kept, and any number of connections can use it.";

    public string CertificatePasswordHint { get; set; } = "Leave empty if the bundle has no password.";

    public string ChooseTheFileToImport { get; set; } = "Choose the file to import.";

    public string NameRequired { get; set; } = "Give the entry a name.";

    public string NameTooLong { get; set; } =
        "The name is too long, or contains characters that cannot be stored.";

    public string CertificateFiles { get; set; } = "PKCS#12 certificates";

    public string PrivateKeyFiles { get; set; } = "Private keys";

    public string AllFiles { get; set; } = "All files";
}
