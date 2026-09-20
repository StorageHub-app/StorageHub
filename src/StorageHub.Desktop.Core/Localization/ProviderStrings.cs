using CodeLogic.Core.Localization;

namespace StorageHub.Desktop.Localization;

/// <summary>
/// Provider descriptions, their field labels, and the help text under them.
/// </summary>
/// <remarks>
/// Flat string properties only, and no property may be named <c>Culture</c>. See
/// <see cref="CommandStrings"/> for why.
///
/// Path examples and fingerprint prefixes are absent on purpose: they are syntax a reader
/// copies, not prose a reader interprets.
/// </remarks>
[LocalizationSection("providers")]
internal sealed class ProviderStrings : LocalizationModelBase
{
    public string AHostOrEndpointIsRequired { get; set; } = "A host or endpoint is required.";

    public string AHostnameIsAcceptedAndUpgradedTo { get; set; } = "A hostname is accepted and upgraded to HTTPS automatically. Do not use a public bucket URL.";

    public string ALocalOrUNCPathIsRequired { get; set; } = "A local or UNC path is required.";

    public string AccessKeyReference { get; set; } = "Access key reference";

    public string AcknowledgePlaintextTransport { get; set; } = "Acknowledge plaintext transport";

    public string AddressingStyle { get; set; } = "Addressing style";

    public string AmazonS3AndS3CompatibleObjectStores { get; set; } = "Amazon S3 and S3-compatible object stores through CL.Storage.";

    public string AmazonAndMostCompatibleServicesRequireA { get; set; } = "Amazon and most compatible services require a signing region. Cloudflare R2 uses 'auto', which StorageHub selects automatically.";

    public string Authentication { get; set; } = "Authentication";

    public string BackupLeftRight { get; set; } = "Backup left → right";

    public string BuildAPlanWithoutChangingEitherEndpoint { get; set; } = "Build a plan without changing either endpoint.";

    public string CertificateSHA256Pin { get; set; } = "Certificate SHA-256 pin";

    public string ClientPFXCertificateReference { get; set; } = "Client PFX certificate reference";

    public string CloudflareR2AndManyCompatibleServicesUse { get; set; } = "Cloudflare R2 and many compatible services use path-style addressing; Amazon S3 normally uses virtual-hosted addressing.";

    public string CompareOnly { get; set; } = "Compare only";

    public string CopyNewAndChangedItemsDeletionPropagation { get; set; } = "Copy new and changed items; deletion propagation stays opt-in.";

    public string CopyNewAndChangedItemsNeverDelete { get; set; } = "Copy new and changed items; never delete destination items.";

    public string ExactMirror { get; set; } = "Exact mirror";

    public string FTPSecuredWithExplicitOrImplicitTLS { get; set; } = "FTP secured with explicit or implicit TLS.";

    public string FTPSendsCredentialsAndDataWithoutTransport { get; set; } = "FTP sends credentials and data without transport encryption. StorageHub shows a persistent warning and never silently upgrades or suppresses trust checks.";

    public string InitialPath { get; set; } = "Initial path";

    public string InitialPrefix { get; set; } = "Initial prefix";

    public string InteractiveSecureShellTerminalUsingManagedSSH { get; set; } = "Interactive Secure Shell terminal using managed SSH.NET; no PuTTY dependency.";

    public string LegacyFTPForCompatibleServersPreferFTPS { get; set; } = "Legacy FTP for compatible servers. Prefer FTPS or SFTP.";

    public string LocalUNC { get; set; } = "Local / UNC";

    public string LocalDisksMappedDrivesAndWindowsNetwork { get; set; } = "Local disks, mapped drives, and Windows network shares.";

    public string MakeTheDestinationMatchTheSourceIncluding { get; set; } = "Make the destination match the source, including reviewed deletions.";

    public string MergeChangesUsingTheLastCompleteBaseline { get; set; } = "Merge changes using the last complete baseline and explicit conflict policy.";

    public string ObjectStoreService { get; set; } = "Object-store service";

    public string OnlyEncryptedPrivateKeysAreAccepted { get; set; } = "Only encrypted private keys are accepted.";

    public string OpenSSHPEMPrivateKeyReference { get; set; } = "OpenSSH / PEM private-key reference";

    public string OptionalVaultEntry { get; set; } = "Optional vault entry";

    public string OptionalWhenUsingAProviderCredentialChain { get; set; } = "Optional when using a provider credential chain";

    public string OptionalVerifyOutOfBand { get; set; } = "Optional; verify out of band";

    public string PFXPasswordReference { get; set; } = "PFX password reference";

    public string PlainFTPIsUnencryptedUseFTPSOr { get; set; } = "Plain FTP is unencrypted; use FTPS or SFTP unless compatibility requires it.";

    public string PortMustBeBetween1And65535 { get; set; } = "Port must be between 1 and 65535.";

    public string PrivateKeyPassphraseReference { get; set; } = "Private-key passphrase reference";

    public string RequiredBeforeThisConnectionCanBeEnabled { get; set; } = "Required before this connection can be enabled.";

    public string RequiredForImportedPFX { get; set; } = "Required for imported PFX";

    public string RequiredVaultEntry { get; set; } = "Required vault entry";

    public string RootPath { get; set; } = "Root path";

    public string S3ObjectStorage { get; set; } = "S3 / Object Storage";

    public string SSHFileTransferProtocolUsingOpenSource { get; set; } = "SSH File Transfer Protocol using open-source .NET libraries; no PuTTY dependency.";

    public string SSHTerminal { get; set; } = "SSH Terminal";

    public string SSHHostKeysAreNeverAcceptedSilently { get; set; } = "SSH host keys are never accepted silently. Verify and pin the SHA-256 fingerprint before connecting.";

    public string SSHHostKeysAreNeverAcceptedSilently2 { get; set; } = "SSH host keys are never accepted silently. Verify and pin the SHA-256 fingerprint obtained through a separate trusted channel.";

    public string SSHHostKeySHA256Fingerprint { get; set; } = "SSH host-key SHA-256 fingerprint";

    public string SecretAccessKeyReference { get; set; } = "Secret access key reference";

    public string SelectAVaultEntry { get; set; } = "Select a vault entry";

    public string SelectAnImportedCertificate { get; set; } = "Select an imported certificate";

    public string ServerTrust { get; set; } = "Server trust";

    public string ServiceEndpoint { get; set; } = "Service endpoint";

    public string SessionTokenReference { get; set; } = "Session token reference";

    public string SigningRegion { get; set; } = "Signing region";

    public string StorageHubDoesNotMaterializeUnprotectedClientPrivate { get; set; } = "StorageHub does not materialize unprotected client private keys.";

    public string TheCurrentCLStorageS3ProviderUses { get; set; } = "The current CL.Storage S3 provider uses system TLS and hostname validation. Per-connection S3 certificate pins are not offered until the provider can enforce them.";

    public string TheCurrentWindowsIdentityIsUsedOperations { get; set; } = "The current Windows identity is used. Operations remain restricted to the configured root.";

    public string TheServerCertificateMustPassHostnameAnd { get; set; } = "The server certificate must pass hostname and chain validation. An optional SHA-256 pin adds protection; client PFX material remains in the vault.";

    public string UpdateLeftRight { get; set; } = "Update left → right";
}
