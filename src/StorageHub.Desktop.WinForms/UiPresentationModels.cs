using System.Collections.ObjectModel;
using System.Globalization;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

public enum StorageProviderKind
{
    Local,
    S3,
    Ftp,
    Ftps,
    Sftp,
    Ssh
}

public enum ConnectionFieldKind
{
    Text,
    Path,
    Number,
    Choice,
    Toggle,
    SecretReference,
    CertificateReference,
    Fingerprint
}

public sealed record ConnectionFieldDescriptor(
    string Key,
    string Label,
    ConnectionFieldKind Kind,
    bool Required = false,
    string DefaultValue = "",
    string Placeholder = "",
    string HelpText = "",
    IReadOnlyList<string>? Choices = null);

public sealed record ConnectionProviderDescriptor(
    StorageProviderKind Kind,
    string DisplayName,
    string ShortName,
    string AccentHex,
    int? DefaultPort,
    bool EncryptedByDefault,
    string Summary,
    string EndpointExample,
    string TrustNotice,
    IReadOnlyList<ConnectionFieldDescriptor> GeneralFields,
    IReadOnlyList<ConnectionFieldDescriptor> AuthenticationFields,
    IReadOnlyList<ConnectionFieldDescriptor> SecurityFields,
    ConnectionProfileType Type = ConnectionProfileType.Storage)
{
    public override string ToString() => DisplayName;
}

public static class ConnectionProviderCatalog
{
    // The choice values below are deliberately not translated. A Choice field persists its selected
    // text into the profile's connection defaults, and ConnectionEditorDraftFactory switches on
    // exactly that text to decide the authentication kind, addressing style, TLS mode and trust
    // mode. Translating one would rewrite what is already saved in users' settings and stop the
    // switch matching. They can follow the language once a Choice carries a key separately from
    // its label, which is a change to the stored format rather than a change of wording.
    //
    // "Password reference" and "Private key reference" stay English for the same reason even
    // where they appear as field labels: the same literal cannot be both translated and matched.
    private static readonly ReadOnlyCollection<string> AddressingStyles =
        Array.AsReadOnly(new[] { "Virtual-hosted (recommended)", "Path-style" });

    private static readonly ReadOnlyCollection<string> S3ServiceTypes =
        Array.AsReadOnly(new[]
        {
            ConnectionEditorDraftFactory.AmazonS3ServiceType,
            ConnectionEditorDraftFactory.CloudflareR2ServiceType,
            ConnectionEditorDraftFactory.OtherS3ServiceType
        });

    private static readonly ReadOnlyCollection<string> FtpTlsModes =
        Array.AsReadOnly(new[] { "Explicit TLS (recommended)", "Implicit TLS" });

    private static readonly ReadOnlyCollection<string> TrustModes =
        Array.AsReadOnly(new[] { "System trust + hostname", "System trust + certificate pin" });

    private static readonly ReadOnlyCollection<string> SshAuthenticationModes =
        Array.AsReadOnly(new[] { "Private key reference", "Password reference" });

    private static readonly ReadOnlyCollection<string> SshClientAuthenticationModes =
        Array.AsReadOnly(new[]
        {
            "Private key reference",
            "Password reference",
            "Private key + password (MFA)"
        });

    private static readonly ReadOnlyCollection<ConnectionProviderDescriptor> Providers = Array.AsReadOnly(
    new ConnectionProviderDescriptor[]
    {
        new(
            StorageProviderKind.Local,
            Ui.Providers.LocalUNC,
            "LOCAL",
            "#4C8BF5",
            null,
            true,
            Ui.Providers.LocalDisksMappedDrivesAndWindowsNetwork,
            @"C:\Data or \\server\share",
            Ui.Providers.TheCurrentWindowsIdentityIsUsedOperations,
            [
                Field("rootPath", Ui.Providers.RootPath, ConnectionFieldKind.Path, required: true, placeholder: @"C:\Data or \\server\share")
            ],
            [],
            []),
        new(
            StorageProviderKind.S3,
            Ui.Providers.S3ObjectStorage,
            "S3",
            "#F59E0B",
            443,
            true,
            Ui.Providers.AmazonS3AndS3CompatibleObjectStores,
            "https://s3.example.com",
            Ui.Providers.TheCurrentCLStorageS3ProviderUses,
            [
                Field("s3ServiceType", Ui.Providers.ObjectStoreService, ConnectionFieldKind.Choice, defaultValue: S3ServiceTypes[0], choices: S3ServiceTypes),
                Field("endpoint", Ui.Providers.ServiceEndpoint, ConnectionFieldKind.Text, required: true, defaultValue: "https://s3.amazonaws.com", placeholder: "account-id.r2.cloudflarestorage.com", help: Ui.Providers.AHostnameIsAcceptedAndUpgradedTo),
                Field("region", Ui.Providers.SigningRegion, ConnectionFieldKind.Text, defaultValue: "us-east-1", help: Ui.Providers.AmazonAndMostCompatibleServicesRequireA),
                Field("bucket", "Bucket", ConnectionFieldKind.Text, required: true),
                Field("prefix", Ui.Providers.InitialPrefix, ConnectionFieldKind.Text, placeholder: "team/archive/"),
                Field("addressingStyle", Ui.Providers.AddressingStyle, ConnectionFieldKind.Choice, defaultValue: AddressingStyles[0], choices: AddressingStyles, help: Ui.Providers.CloudflareR2AndManyCompatibleServicesUse)
            ],
            [
                Field("accessKeyReference", Ui.Providers.AccessKeyReference, ConnectionFieldKind.SecretReference, placeholder: Ui.Providers.OptionalWhenUsingAProviderCredentialChain),
                Field("secretAccessKeyReference", Ui.Providers.SecretAccessKeyReference, ConnectionFieldKind.SecretReference, placeholder: Ui.Providers.SelectAVaultEntry),
                Field("sessionTokenReference", Ui.Providers.SessionTokenReference, ConnectionFieldKind.SecretReference, placeholder: Ui.Providers.OptionalVaultEntry)
            ],
            []),
        new(
            StorageProviderKind.Ftp,
            "FTP",
            "FTP",
            "#64748B",
            21,
            false,
            Ui.Providers.LegacyFTPForCompatibleServersPreferFTPS,
            "ftp.example.com",
            Ui.Providers.FTPSendsCredentialsAndDataWithoutTransport,
            [
                Field("host", "Host", ConnectionFieldKind.Text, required: true, placeholder: "ftp.example.com"),
                Field("port", "Port", ConnectionFieldKind.Number, required: true, defaultValue: "21"),
                Field("initialPath", Ui.Providers.InitialPath, ConnectionFieldKind.Text, defaultValue: "/")
            ],
            [
                Field("username", "Username", ConnectionFieldKind.Text, required: true),
                Field("passwordReference", "Password reference", ConnectionFieldKind.SecretReference, required: true, placeholder: Ui.Providers.SelectAVaultEntry)
            ],
            [
                Field("acknowledgePlaintext", Ui.Providers.AcknowledgePlaintextTransport, ConnectionFieldKind.Toggle, defaultValue: "false", help: Ui.Providers.RequiredBeforeThisConnectionCanBeEnabled)
            ]),
        new(
            StorageProviderKind.Ftps,
            "FTPS",
            "FTPS",
            "#10B981",
            21,
            true,
            Ui.Providers.FTPSecuredWithExplicitOrImplicitTLS,
            "ftps.example.com",
            Ui.Providers.TheServerCertificateMustPassHostnameAnd,
            [
                Field("host", "Host", ConnectionFieldKind.Text, required: true, placeholder: "ftps.example.com"),
                Field("port", "Port", ConnectionFieldKind.Number, required: true, defaultValue: "21"),
                Field("initialPath", Ui.Providers.InitialPath, ConnectionFieldKind.Text, defaultValue: "/"),
                Field("tlsMode", "TLS mode", ConnectionFieldKind.Choice, defaultValue: FtpTlsModes[0], choices: FtpTlsModes)
            ],
            [
                Field("username", "Username", ConnectionFieldKind.Text, required: true),
                Field("passwordReference", "Password reference", ConnectionFieldKind.SecretReference, required: true, placeholder: Ui.Providers.SelectAVaultEntry)
            ],
            [
                Field("trustMode", Ui.Providers.ServerTrust, ConnectionFieldKind.Choice, defaultValue: TrustModes[0], choices: TrustModes),
                Field("certificatePin", Ui.Providers.CertificateSHA256Pin, ConnectionFieldKind.Fingerprint, placeholder: Ui.Providers.OptionalVerifyOutOfBand),
                Field("clientCertificateReference", Ui.Providers.ClientPFXCertificateReference, ConnectionFieldKind.CertificateReference, placeholder: Ui.Providers.SelectAnImportedCertificate),
                Field("clientCertificatePasswordReference", Ui.Providers.PFXPasswordReference, ConnectionFieldKind.SecretReference, placeholder: Ui.Providers.RequiredForImportedPFX, help: Ui.Providers.StorageHubDoesNotMaterializeUnprotectedClientPrivate)
            ]),
        new(
            StorageProviderKind.Sftp,
            "SFTP",
            "SFTP",
            "#8B5CF6",
            22,
            true,
            Ui.Providers.SSHFileTransferProtocolUsingOpenSource,
            "sftp.example.com",
            Ui.Providers.SSHHostKeysAreNeverAcceptedSilently2,
            [
                Field("host", "Host", ConnectionFieldKind.Text, required: true, placeholder: "sftp.example.com"),
                Field("port", "Port", ConnectionFieldKind.Number, required: true, defaultValue: "22"),
                Field("initialPath", Ui.Providers.InitialPath, ConnectionFieldKind.Text, defaultValue: "/")
            ],
            [
                Field("username", "Username", ConnectionFieldKind.Text, required: true),
                Field("authenticationMode", Ui.Providers.Authentication, ConnectionFieldKind.Choice, defaultValue: SshAuthenticationModes[0], choices: SshAuthenticationModes),
                Field("passwordReference", "Password reference", ConnectionFieldKind.SecretReference, placeholder: Ui.Providers.OptionalVaultEntry),
                Field("privateKeyReference", Ui.Providers.OpenSSHPEMPrivateKeyReference, ConnectionFieldKind.SecretReference, placeholder: Ui.Providers.SelectAVaultEntry),
                Field("privateKeyPassphraseReference", Ui.Providers.PrivateKeyPassphraseReference, ConnectionFieldKind.SecretReference, required: true, placeholder: Ui.Providers.RequiredVaultEntry, help: Ui.Providers.OnlyEncryptedPrivateKeysAreAccepted)
            ],
            [
                Field("hostKeyFingerprint", Ui.Providers.SSHHostKeySHA256Fingerprint, ConnectionFieldKind.Fingerprint, required: true, placeholder: "SHA256:...")
            ]),
        new(
            StorageProviderKind.Ssh,
            Ui.Providers.SSHTerminal,
            "SSH",
            "#06B6D4",
            22,
            true,
            Ui.Providers.InteractiveSecureShellTerminalUsingManagedSSH,
            "ssh.example.com",
            Ui.Providers.SSHHostKeysAreNeverAcceptedSilently,
            [
                Field("host", "Host", ConnectionFieldKind.Text, required: true, placeholder: "ssh.example.com"),
                Field("port", "Port", ConnectionFieldKind.Number, required: true, defaultValue: "22")
            ],
            [
                Field("username", "Username", ConnectionFieldKind.Text, required: true),
                Field("authenticationMode", Ui.Providers.Authentication, ConnectionFieldKind.Choice, defaultValue: SshClientAuthenticationModes[0], choices: SshClientAuthenticationModes),
                Field("passwordReference", "Password reference", ConnectionFieldKind.SecretReference, placeholder: Ui.Providers.OptionalVaultEntry),
                Field("privateKeyReference", Ui.Providers.OpenSSHPEMPrivateKeyReference, ConnectionFieldKind.SecretReference, placeholder: Ui.Providers.SelectAVaultEntry),
                Field("privateKeyPassphraseReference", Ui.Providers.PrivateKeyPassphraseReference, ConnectionFieldKind.SecretReference, required: true, placeholder: Ui.Providers.RequiredVaultEntry)
            ],
            [
                Field("hostKeyFingerprint", Ui.Providers.SSHHostKeySHA256Fingerprint, ConnectionFieldKind.Fingerprint, required: true, placeholder: "SHA256:...")
            ],
            ConnectionProfileType.Client)
    });

    public static IReadOnlyList<ConnectionProviderDescriptor> All => Providers;

    public static ConnectionProviderDescriptor Get(StorageProviderKind kind) =>
        Providers.First(provider => provider.Kind == kind);

    private static ConnectionFieldDescriptor Field(
        string key,
        string label,
        ConnectionFieldKind kind,
        bool required = false,
        string defaultValue = "",
        string placeholder = "",
        string help = "",
        IReadOnlyList<string>? choices = null) =>
        new(key, label, kind, required, defaultValue, placeholder, help, choices);
}

public enum AgentConnectionState
{
    Starting,
    Connected,
    RecoveryOnly,
    Disconnected
}

public sealed record ShellStatusSnapshot(
    string Location,
    int SelectedItems,
    long SelectedBytes,
    long TransferBytesPerSecond,
    int QueuedJobs,
    int ActiveJobs,
    AgentConnectionState AgentState)
{
    /// <summary>
    /// The state before anything has been observed. Its location is resolved on each call rather
    /// than captured, because a static initializer would fix the text in whichever language
    /// happened to be loaded when this type was first touched.
    /// </summary>
    public static ShellStatusSnapshot Initial =>
        new(Ui.Shell.StatusNoConnection, 0, 0, 0, 0, 0, AgentConnectionState.Starting);

    public string SelectionText => SelectedItems == 0
        ? Ui.Shell.StatusNoSelection
        : Ui.Format(Ui.Shell.StatusSelectionFormat, SelectedItems, UiFormatting.FormatBytes(SelectedBytes));

    public string TransferRateText =>
        Ui.Format(Ui.Shell.StatusTransferRateFormat, UiFormatting.FormatBytes(TransferBytesPerSecond));

    public string QueueText => ActiveJobs == 0
        ? Ui.Format(Ui.Shell.StatusQueueFormat, QueuedJobs)
        : Ui.Format(Ui.Shell.StatusQueueActiveFormat, QueuedJobs, ActiveJobs);

    public string AgentText => AgentState switch
    {
        AgentConnectionState.Starting => Ui.Shell.AgentStarting,
        AgentConnectionState.Connected => Ui.Shell.AgentConnected,
        AgentConnectionState.RecoveryOnly => Ui.Shell.AgentRecoveryMode,
        _ => Ui.Shell.AgentNotConnected
    };
}

public sealed record ConnectionCardModel(
    string Name,
    StorageProviderKind Provider,
    string Endpoint,
    string State,
    bool IsFavorite = false,
    Guid? ConnectionId = null,
    bool IsEnabled = true,
    string? AccentColor = null,
    string? FolderPath = null,
    string[]? Tags = null,
    // Appended, so every existing positional construction is untouched.
    string? IconKey = null)
{
    public ConnectionProviderDescriptor Descriptor => ConnectionProviderCatalog.Get(Provider);

    public ConnectionProfileType Type => Descriptor.Type;

    public string AccentHex => string.IsNullOrWhiteSpace(AccentColor)
        ? Descriptor.AccentHex
        : AccentColor;

    public IReadOnlyList<string> DisplayTags => Tags ?? [];

    public override string ToString() => Name;
}

public sealed record QuickConnectDraft(
    StorageProviderKind Provider,
    string HostOrPath,
    int? Port,
    string UserName,
    bool UseSecureTransport)
{
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(HostOrPath))
        {
            errors.Add(Provider == StorageProviderKind.Local ? Ui.Providers.ALocalOrUNCPathIsRequired : Ui.Providers.AHostOrEndpointIsRequired);
        }

        if (Provider != StorageProviderKind.Local && (Port is < 1 or > 65535))
        {
            errors.Add(Ui.Providers.PortMustBeBetween1And65535);
        }

        if (Provider == StorageProviderKind.Ftp && !UseSecureTransport)
        {
            errors.Add(Ui.Providers.PlainFTPIsUnencryptedUseFTPSOr);
        }

        return errors.AsReadOnly();
    }
}

public enum SyncModeKind
{
    BackupLeftToRight,
    UpdateLeftToRight,
    ExactMirror,
    TwoWay,
    CompareOnly
}

public sealed record SyncModeDescriptor(
    SyncModeKind Kind,
    string DisplayName,
    string Summary,
    bool CanPropagateDeletes,
    bool RequiresPreview);

public static class SyncPresentationCatalog
{
    private static readonly ReadOnlyCollection<SyncModeDescriptor> Modes = Array.AsReadOnly(
    new SyncModeDescriptor[]
    {
        new(SyncModeKind.BackupLeftToRight, Ui.Providers.BackupLeftRight, Ui.Providers.CopyNewAndChangedItemsNeverDelete, false, true),
        new(SyncModeKind.UpdateLeftToRight, Ui.Providers.UpdateLeftRight, Ui.Providers.CopyNewAndChangedItemsDeletionPropagation, true, true),
        new(SyncModeKind.ExactMirror, Ui.Providers.ExactMirror, Ui.Providers.MakeTheDestinationMatchTheSourceIncluding, true, true),
        new(SyncModeKind.TwoWay, "Two-way", Ui.Providers.MergeChangesUsingTheLastCompleteBaseline, true, true),
        new(SyncModeKind.CompareOnly, Ui.Providers.CompareOnly, Ui.Providers.BuildAPlanWithoutChangingEitherEndpoint, false, false)
    });

    public static IReadOnlyList<SyncModeDescriptor> AllModes => Modes;

    public static int DefaultMassDeleteItemLimit => 100;

    public static decimal DefaultMassDeletePercentageLimit => 10m;

    public static bool DeletePropagationEnabledByDefault => false;
}

public static class UiFormatting
{
    private static readonly string[] SizeSuffixes = ["B", "KiB", "MiB", "GiB", "TiB", "PiB"];

    public static string FormatBytes(long bytes)
    {
        var negative = bytes < 0;
        var value = Math.Abs((double)bytes);
        var suffix = 0;
        while (value >= 1024 && suffix < SizeSuffixes.Length - 1)
        {
            value /= 1024;
            suffix++;
        }

        var prefix = negative ? "−" : string.Empty;
        return string.Create(CultureInfo.CurrentCulture, $"{prefix}{value:0.#} {SizeSuffixes[suffix]}");
    }
}
