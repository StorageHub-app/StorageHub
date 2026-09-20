using CodeLogic.Core.Localization;

namespace StorageHub.Desktop.Localization;

/// <summary>
/// The connection editor and Quick Connect.
/// </summary>
/// <remarks>
/// Flat string properties only, and no property may be named <c>Culture</c>. See
/// <see cref="CommandStrings"/> for why.
///
/// A file-dialog filter is half prose and half pattern: the descriptions translate, the patterns
/// must not.
/// </remarks>
[LocalizationSection("connectioneditor")]
internal sealed class ConnectionEditorStrings : LocalizationModelBase
{
    // ------------------------------------------------------------------ window
    public string EditTitle { get; set; } = "Edit Connection — StorageHub";

    public string EditAccessibleName { get; set; } = "Edit Connection";

    public string QuickConnectTitle { get; set; } = "Quick Connect — StorageHub";

    public string QuickConnectAccessibleName { get; set; } = "Quick Connect";

    public string WindowAccessibleDescription { get; set; } =
        "Configure provider endpoints, vault credential references, and explicit server trust.";

    public string NewConnection { get; set; } = "New connection";

    public string NewUnsavedProfile { get; set; } = "New unsaved profile";

    public string ConnectionBadge { get; set; } = "Connection badge";

    public string BadgeHint { get; set; } =
        "The provider glyph and accent make concurrent connections easy to distinguish.";

    // -------------------------------------------------------------------- tabs
    public string TabGeneral { get; set; } = "General";

    public string TabEndpoint { get; set; } = "Endpoint";

    public string TabAuthentication { get; set; } = "Authentication";

    public string TabTrust { get; set; } = "Transport and server identity";

    // ------------------------------------------------------------------ fields
    public string ProfileNameRequired { get; set; } = "Profile name *";

    public string ProfileNameHint { get; set; } =
        "The display name shown in connection cards and pane selectors.";

    public string ProviderProtocol { get; set; } = "Provider / protocol";

    public string ProviderHint { get; set; } =
        "Changes the provider-specific endpoint, authentication, and trust fields.";

    public string ConnectionType { get; set; } = "Connection type";

    public string ConnectionTypeHint { get; set; } =
        "Choose Storage for browsable providers or Client for interactive remote clients.";

    public string FolderLabel { get; set; } = "Folder";

    public string FolderHint { get; set; } =
        "Optional organizational path that groups this connection in the connections panel.";

    public string ChooseRootFolder { get; set; } = "Choose the connection root folder";

    public string Appearance { get; set; } = "Icon and colour";

    public string AppearanceHint { get; set; } =
        "How this connection is shown in the list and in a pane's header.";

    public string Labels { get; set; } = "Labels";

    public string LabelsHint { get; set; } =
        "Comma-separated searchable labels; never enter credentials or recovery codes.";

    public string SessionNameHint { get; set; } =
        "Used only to identify this session; the temporary profile is not saved.";

    public string Enabled { get; set; } = "Enabled";

    public string Disabled { get; set; } = "Disabled";

    public string Browse { get; set; } = "Browse…";

    // ---------------------------------------------------------------- commands
    public string SaveProfile { get; set; } = "Save profile";

    public string SaveProfileHint { get; set; } = "Save non-secret profile data and vault references.";

    public string ConnectWithoutSaving { get; set; } = "Connect without saving";

    public string ConnectWithoutSavingHint { get; set; } =
        "Connect for this session; secrets remain vault references.";

    public string OpenClient { get; set; } = "Open client";

    public string KeyStore { get; set; } = "Key Store…";

    public string Delete { get; set; } = "Delete…";

    public string Cancel { get; set; } = "Cancel";

    // ------------------------------------------------------------------ vault
    public string EnrollOrReplace { get; set; } = "Enroll / replace…";

    public string Enroll { get; set; } = "Enroll";

    public string VaultReferenceHint { get; set; } =
        "An opaque vault reference. Secret material is never displayed.";

    public string VaultEncryptionHint { get; set; } =
        "The value is encrypted into the StorageHub vault. Only an opaque reference is saved in the profile.";

    public string NoVaultReference { get; set; } = "No vault reference is selected.";

    public string WritingVaultEntry { get; set; } = "Writing encrypted vault entry…";

    public string VaultEntryFailed { get; set; } = "The vault entry could not be written.";

    public string VaultSecretDeleteFailed { get; set; } = "The vault secret could not be deleted.";

    public string SecretDeleteFailed { get; set; } = "The secret delete operation failed.";

    public string SecretOperationFailed { get; set; } =
        "The secret operation failed without changing the profile.";

    public string SecretFileInvalid { get; set; } =
        "The selected secret file is empty or exceeds 16 MiB.";

    public string VaultSecretDeleted { get; set; } =
        "Vault secret deleted. Save the profile to remove its old reference.";

    public string KeyStoreUnreadable { get; set; } = "The key store could not be read.";

    public string NoStoredKeys { get; set; } =
        "No matching keys are stored yet. Import one from Connections > Key Store.";

    /// <summary>{0} = the field's label.</summary>
    public string EnrollFieldFormat { get; set; } = "Enroll {0}";

    /// <summary>{0} = the vault entry version.</summary>
    public string VaultReferenceReadyFormat { get; set; } = "Vault reference ready (version {0}).";

    /// <summary>{0} = the chosen key's display name.</summary>
    public string UsingStoredKeyFormat { get; set; } = "Using '{0}' from the key store.";

    // ------------------------------------------------------------------ trust
    public string FetchFromHost { get; set; } = "Fetch from host…";

    public string FetchFromHostHint { get; set; } =
        "Retrieve and display the SSH host key without trusting it.";

    public string Reject { get; set; } = "Reject…";

    public string RejectHint { get; set; } =
        "Record this exact fingerprint as rejected for the saved endpoint.";

    public string VerifyFingerprintHint { get; set; } =
        "Verify this SHA-256 fingerprint through a separate trusted channel before saving.";

    public string PlaintextWarning { get; set; } = "Plaintext transport warning";

    public string FetchAlreadyRunning { get; set; } = "An SSH host-key fetch is already running.";

    public string EnterValidSftpEndpoint { get; set; } =
        "Enter a valid SFTP host and port before fetching its host key.";

    public string EnterValidFingerprint { get; set; } =
        "Enter a valid SHA-256 fingerprint before rejecting it.";

    public string HostKeyFetchFailed { get; set; } =
        "The SSH host key could not be fetched from the endpoint.";

    public string HostKeyUnusable { get; set; } = "The SSH endpoint did not return a usable host key.";

    public string HostKeyNotAdded { get; set; } = "The discovered SSH host key was not added.";

    public string HostKeyAdded { get; set; } =
        "SSH host key added to the editor; save only after independent verification.";

    public string SaveBeforeRejecting { get; set; } =
        "Save the connection before recording a rejected fingerprint.";

    public string RecordingRejected { get; set; } = "Recording rejected server identity…";

    public string RejectedRecorded { get; set; } =
        "Rejected server identity recorded; the profile remains fail-closed.";

    public string RejectedRecordFailed { get; set; } = "The rejected identity could not be recorded.";

    public string RejectedRecordFailedThroughAgent { get; set; } =
        "The rejected identity could not be recorded through the background agent.";

    public string TrustStateUnreadable { get; set; } = "The saved server trust state could not be loaded.";

    public string MultipleTrustRecords { get; set; } =
        "Multiple active server trust records require reconciliation before editing this profile.";

    public string PinNotEnrolled { get; set; } =
        "The profile was saved, but its pin was not enrolled. It remains fail-closed.";

    /// <summary>{0} = the host, {1} = the port.</summary>
    public string FetchingHostKeyFormat { get; set; } = "Fetching the SSH host key from {0}:{1}…";

    // ----------------------------------------------------------------- status
    public string LoadingProfile { get; set; } = "Loading profile…";

    public string SavingProfile { get; set; } = "Saving profile…";

    public string TestingConnection { get; set; } = "Testing connection…";

    public string ChangesNotTested { get; set; } = "Changes not tested";

    public string NotTested { get; set; } = "Not tested";

    public string ProfileDeleted { get; set; } = "Profile deleted.";

    public string ConnectionTestFailed { get; set; } = "Connection test failed.";

    public string SshConnectionSucceeded { get; set; } = "SSH connection succeeded.";

    public string AgentCannotTest { get; set; } = "The background agent could not test this connection.";

    public string AgentIncompatible { get; set; } =
        "The background agent is unavailable or incompatible with this desktop build. Restart StorageHub and try again.";

    public string ProfileLoadFailed { get; set; } = "The profile could not be loaded.";

    public string ProfileLoadFailedThroughAgent { get; set; } =
        "The profile could not be loaded from the background agent.";

    public string ProfileSaveFailed { get; set; } = "The profile could not be saved.";

    public string ProfileDeleteFailed { get; set; } = "The profile could not be deleted.";

    public string ProfileDeleteFailedThroughAgent { get; set; } =
        "The profile could not be deleted through the background agent.";

    public string WaitBeforeSaving { get; set; } =
        "Wait for the selected profile to finish loading before saving.";

    public string WaitBeforeDeleting { get; set; } =
        "Wait for the selected profile to finish loading before deleting.";

    public string QuickConnectCannotOpenSaved { get; set; } =
        "Quick Connect always starts a new connection, so it cannot open a saved one.";

    public string SshRequiresPublicKeyThenPassword { get; set; } =
        "The SSH server must accept public-key authentication followed by the account password.";

    /// <summary>{0} = the round-trip time in milliseconds.</summary>
    public string ConnectionSucceededFormat { get; set; } = "Connection succeeded in {0} ms";

    /// <summary>{0} = the profile version.</summary>
    public string LoadedVersionFormat { get; set; } = "Loaded version {0}";

    /// <summary>{0} = the profile version.</summary>
    public string SavedVersionFormat { get; set; } = "Saved version {0}";

    /// <summary>{0} = the provider's display name.</summary>
    public string NewProviderFormat { get; set; } = "New {0}";

    // ------------------------------------------------------------- field values
    //
    // The authentication mode, addressing style, TLS mode and trust mode are absent here on
    // purpose: a Choice field stores its selected text in the profile and this form compares
    // against exactly that text. See the note in ConnectionProviderCatalog.
    public string R2SigningRegion { get; set; } = "Cloudflare R2 signing region; fixed to auto.";

    /// <summary>A file-dialog filter. The descriptions translate; the patterns must not.</summary>
    public string CertificateFilter { get; set; } =
        "PKCS#12 certificates (*.pfx;*.p12)|*.pfx;*.p12|All files (*.*)|*.*";

    /// <summary>A file-dialog filter. The descriptions translate; the patterns must not.</summary>
    public string PrivateKeyFilter { get; set; } =
        "SSH private keys (*.key;*.pem)|*.key;*.pem|All files (*.*)|*.*";

    // ------------------------------------------------- accessible names and prose
    public string ConnectionProvider { get; set; } = "Connection provider";

    public string ConnectionSettings { get; set; } = "Connection settings";

    public string ConnectionTestStatus { get; set; } = "Connection test status";

    public string ProviderSummary { get; set; } = "Provider summary";

    public string QuickConnectCommands { get; set; } = "Quick Connect commands";

    public string SecureTransportPolicy { get; set; } = "Secure transport policy";

    public string TestConnection { get; set; } = "Test connection";

    public string TeamProject { get; set; } = "Team / Project";

    public string SavingVerifiedTrust { get; set; } = "Saving verified server trust…";

    public string SecretSentToAgentOnly { get; set; } =
        "Secret material is sent only to the current-user agent vault.";

    public string SecretsLiveInVault { get; set; } =
        "Secret values live in the encrypted StorageHub vault; this profile stores references only.";

    public string StoresReferenceOnly { get; set; } =
        "Stores a vault or certificate reference, not a secret value.";

    public string SelectPfxCertificate { get; set; } = "Select a password-protected PFX certificate";

    public string SelectPrivateKey { get; set; } = "Select an encrypted SSH private key";

    public string SelectClientProfileFirst { get; set; } =
        "Select a saved SSH client profile before opening a terminal.";

    public string SelectProfileBeforeDeleting { get; set; } = "Select a saved profile before deleting it.";

    public string SelectOneAuthMethod { get; set; } =
        "Select one vault-backed SSH authentication method.";

    public string SigningRegionHint { get; set; } =
        "Signing region used by the selected S3-compatible service.";

    /// <summary>{0} = the provider's short name.</summary>
    public string TemporaryConnectionFormat { get; set; } = "Temporary {0} connection";

    public string TypeLabel { get; set; } = "Type";

    public string TabSecurity { get; set; } = "TLS / SSH Trust";

    /// <summary>{0} = the provider's short name, {1} = its accent colour as a hex value.</summary>
    public string ProviderColorBadgeFormat { get; set; } = "  {0}  \u00b7 provider color {1}";
}
