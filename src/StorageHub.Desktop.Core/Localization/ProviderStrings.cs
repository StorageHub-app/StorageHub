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

    public string LeaveEmptyForAKeyWithNoPassphrase { get; set; } = "Leave empty for a key that has no passphrase. A passphrase is still recommended.";

    public string OpenSSHPEMPrivateKeyReference { get; set; } = "OpenSSH / PEM private-key reference";

    public string OptionalVaultEntry { get; set; } = "Optional vault entry";

    public string OptionalWhenUsingAProviderCredentialChain { get; set; } = "Optional when using a provider credential chain";

    public string OptionalVerifyOutOfBand { get; set; } = "Optional; verify out of band";

    public string PFXPasswordReference { get; set; } = "PFX password reference";

    public string PlainFTPIsUnencryptedUseFTPSOr { get; set; } = "Plain FTP is unencrypted; use FTPS or SFTP unless compatibility requires it.";

    public string PortMustBeBetween1And65535 { get; set; } = "Port must be between 1 and 65535.";

    public string PrivateKeyPassphraseReference { get; set; } = "Private-key passphrase reference";

    public string AllowPlainHTTPEndpoint { get; set; } = "Allow a plain HTTP endpoint";

    public string ForAnEndpointYouRunYourselfSuchAs { get; set; } = "For an endpoint you run yourself, such as a local MinIO. Requests are signed, but data is not encrypted on the way.";

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

    public string WebDavSummary { get; set; } =
        "Files on a WebDAV server, such as Nextcloud, ownCloud, SharePoint or a NAS.";

    public string WebDavTrustNotice { get; set; } =
        "The server certificate must pass system validation or match a verified SHA-256 pin. Basic sign-in sends the password with every request, so use HTTPS.";

    public string ServerUrl { get; set; } = "Server URL";

    public string WebDavUrlHelp { get; set; } =
        "The address of the WebDAV folder, path included. A host name alone is taken as https://.";

    public string WebDavAuthenticationHelp { get; set; } =
        "Basic, Digest, NTLM and Negotiate sign in with the user name and password; a bearer token is sent as it is.";

    public string BearerTokenReference { get; set; } = "Bearer token reference";

    public string OnlyForBearerTokenSignIn { get; set; } = "Only for bearer-token sign-in";

    public string WebDavPlainHttpHelp { get; set; } =
        "For a server you run yourself on your own network. The password and the files travel unencrypted.";

    public string AzureBlobStorage { get; set; } = "Azure Blob Storage";

    public string AzureBlobSummary { get; set; } =
        "Files in an Azure Blob Storage container, or in the Azurite emulator.";

    public string AzureBlobTrustNotice { get; set; } =
        "The Blob service's certificate must pass system validation. A shared key, a SAS token or an Azure identity is only ever sent over HTTPS.";

    public string Container { get; set; } = "Container";

    public string AzureContainerHelp { get; set; } =
        "3 to 63 lower-case letters, digits and single hyphens, as Azure names it.";

    public string BlobServiceUrl { get; set; } = "Blob service URL";

    public string AzureServiceUrlHelp { get; set; } =
        "For a shared key it defaults to https://account.blob.core.windows.net/. A connection string names its own, so this is left out with one.";

    public string AzureAuthenticationHelp { get; set; } =
        "A shared key needs the account name; Azure identity uses the agent's own sign-in (an environment service principal, a managed identity or the Azure CLI).";

    public string StorageAccountName { get; set; } = "Storage account name";

    public string AccountKeyReference { get; set; } = "Account key reference";

    public string SasTokenReference { get; set; } = "SAS token reference";

    public string ConnectionStringReference { get; set; } = "Connection string reference";

    public string OnlyForSharedKeySignIn { get; set; } = "Only for shared-key sign-in";

    public string OnlyForSasSignIn { get; set; } = "Only for SAS-token sign-in";

    public string OnlyForConnectionStringSignIn { get; set; } = "Only for connection-string sign-in";

    public string AllowPlainHttpConnectionString { get; set; } = "Allow a plain-HTTP connection string";

    public string AzurePlainHttpHelp { get; set; } =
        "For the Azurite emulator on your own computer. Requests are still signed, but the files travel unencrypted.";

    public string GoogleCloudStorage { get; set; } = "Google Cloud Storage";

    public string GoogleCloudStorageSummary { get; set; } =
        "Files in a Google Cloud Storage bucket, or in an emulator such as fake-gcs-server.";

    public string GoogleCloudStorageTrustNotice { get; set; } =
        "Google's certificate must pass system validation. A service-account key or Application Default Credentials are only sent over HTTPS unless you allow plain HTTP for an emulator.";

    public string GcsBucketHelp { get; set; } =
        "3 to 63 lower-case letters, digits, hyphens and underscores, as Google names it; up to 222 with dots.";

    public string GcsServiceUrl { get; set; } = "Service URL";

    public string GcsServiceUrlHelp { get; set; } =
        "Leave empty for Google itself. For a private endpoint or an emulator, give its address; /storage/v1/ is added for you.";

    public string GcsAuthenticationHelp { get; set; } =
        "A service-account key is the JSON file Google gives you, kept in the vault. Application Default Credentials use the agent's own sign-in (GOOGLE_APPLICATION_CREDENTIALS, gcloud or the metadata server). Anonymous reads public buckets.";

    public string ServiceAccountKeyReference { get; set; } = "Service-account key reference";

    public string OnlyForServiceAccountSignIn { get; set; } = "Only for service-account sign-in";

    public string GcsPlainHttpHelp { get; set; } =
        "For an emulator on your own computer. Nothing is encrypted, a service-account sign-in included.";
}
