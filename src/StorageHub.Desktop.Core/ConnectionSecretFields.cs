using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop;

/// <summary>
/// What the connection editor's secret fields mean to the vault and to the key store.
/// </summary>
/// <remarks>
/// A field holds an opaque reference; these say what that reference is a reference to. Keyed on
/// the descriptor keys in <see cref="ConnectionProviderCatalog"/>, exactly as
/// <see cref="ConnectionEditorDraftFactory"/> reads them, because the purpose a secret is
/// enrolled under is what the agent checks when the profile later asks for it back.
/// </remarks>
internal static class ConnectionSecretFields
{
    /// <summary>The vault purpose a field's secret is enrolled under.</summary>
    internal static SecretMaterialPurpose Purpose(string fieldKey) => fieldKey switch
    {
        "accessKeyReference" => SecretMaterialPurpose.AccessKey,
        "secretAccessKeyReference" => SecretMaterialPurpose.SecretAccessKey,
        "sessionTokenReference" => SecretMaterialPurpose.SessionToken,
        "privateKeyReference" => SecretMaterialPurpose.SshPrivateKey,
        "privateKeyPassphraseReference" => SecretMaterialPurpose.SshPrivateKeyPassphrase,
        "clientCertificateReference" => SecretMaterialPurpose.ClientCertificatePfx,
        "clientCertificatePasswordReference" => SecretMaterialPurpose.ClientCertificatePassword,
        "credentialReference" => SecretMaterialPurpose.ProxyCredential,
        ConnectionEditorDraftFactory.ProxyPasswordKey => SecretMaterialPurpose.ProxyCredential,
        _ => SecretMaterialPurpose.Password
    };

    /// <summary>
    /// The key material a field can borrow from the key store, or null when it cannot.
    /// </summary>
    /// <remarks>
    /// Only material fields qualify: a passphrase is filled from whichever entry is chosen, never
    /// picked on its own, because the two are only meaningful together.
    /// </remarks>
    internal static KeyStoreMaterialKind? KeyStoreSlot(string fieldKey) => fieldKey switch
    {
        "clientCertificateReference" => KeyStoreMaterialKind.Pkcs12Certificate,
        "privateKeyReference" => KeyStoreMaterialKind.SshPrivateKey,
        _ => null
    };

    /// <summary>The passphrase field unlocked by a given material field.</summary>
    internal static string? CompanionPassphrase(string fieldKey) => fieldKey switch
    {
        "clientCertificateReference" => "clientCertificatePasswordReference",
        "privateKeyReference" => "privateKeyPassphraseReference",
        _ => null
    };

    /// <summary>
    /// Whether a field's secret comes from a file rather than from typing.
    /// </summary>
    /// <remarks>
    /// A certificate and a private key are files; a password, an access key and a passphrase are
    /// typed. The distinction decides which prompt enrolling opens.
    /// </remarks>
    internal static bool IsFile(string fieldKey) => KeyStoreSlot(fieldKey) is not null;
}
