using System.Globalization;
using System.Text;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>Maps visible protocol-aware editor fields to the reference-only IPC draft.</summary>
public static class ConnectionEditorDraftFactory
{
    // Stored values, not display text: these are the choices of the s3ServiceType connection
    // default, persisted into the profile and switched on below. See the note in
    // ConnectionProviderCatalog.
    internal const string AmazonS3ServiceType = "Amazon S3";
    internal const string CloudflareR2ServiceType = "Cloudflare R2";
    internal const string OtherS3ServiceType = "Other S3-compatible";

    public static ConnectionProfileDraft Build(
        StorageProviderKind provider,
        IReadOnlyDictionary<string, string> values,
        ConnectionProviderDefaults? defaults = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        var descriptor = ConnectionProviderCatalog.Get(provider);
        var (folder, tags) = values.ContainsKey("folder") || values.ContainsKey("labels")
            ? (NormalizeFolder(Get(values, "folder")), ParseLabels(Get(values, "labels")))
            : ParseFolderAndTags(Get(values, "folderTags"));
        // Carried from the editor when it offers them, and only then falling back to the
        // provider's own. Overwriting these unconditionally -- which is what used to happen --
        // silently discarded a chosen icon and colour on every save.
        var iconKey = Get(values, "iconKey");
        var accentColor = Get(values, "accentColor");
        var metadata = new ConnectionProfileMetadataDocument(
            Require(values, "profileName", Ui.Validation.AProfileNameIsRequired),
            folder,
            tags,
            IconKey: string.IsNullOrWhiteSpace(iconKey) ? descriptor.ShortName.ToLowerInvariant() : iconKey.Trim(),
            AccentColor: string.IsNullOrWhiteSpace(accentColor) ? descriptor.AccentHex : accentColor.Trim());
        var endpoint = BuildEndpoint(provider, values);
        var authentication = BuildAuthentication(provider, values);
        var draft = new ConnectionProfileDraft(
            metadata,
            endpoint,
            authentication,
            BuildOperationalOptions(provider, defaults, values),
            Type: descriptor.Type);
        if (!draft.HasValidBounds)
        {
            throw new ArgumentException(
                Ui.Validation.OneOrMoreConnectionFieldsAreOutside,
                nameof(values));
        }

        return draft;
    }

    /// <summary>The editor's speed limit fields, in KiB/s; empty is no limit.</summary>
    internal const string UploadLimitKey = "uploadLimitKib";

    internal const string DownloadLimitKey = "downloadLimitKib";

    /// <summary>The editor's proxy fields: an address such as socks5://host:1080, and its sign-in.</summary>
    internal const string ProxyAddressKey = "proxyAddress";

    internal const string ProxyUsernameKey = "proxyUsername";

    internal const string ProxyPasswordKey = "proxyPasswordReference";

    /// <summary>The Advanced section's fields, which FTP and FTPS have.</summary>
    internal const string EncodingKey = "encoding";

    internal const string ConnectTimeoutKey = "connectTimeoutSeconds";

    internal const string ReadTimeoutKey = "readTimeoutSeconds";

    internal const string ServerTimeZoneKey = "serverTimeZone";

    internal const string ListingFormatKey = "listingFormat";

    internal const string DataConnectionKey = "dataConnection";

    internal const string ActivePortsKey = "activePorts";

    internal const string ActiveAddressKey = "activeAddress";

    // Choice values, matched below as written, so they are not translated (see the note in
    // ConnectionProviderCatalog): the technical names are what a server's documentation uses anyway.
    internal static readonly IReadOnlyList<string> ListingFormats =
    [
        "Auto",
        "MLSD (machine-readable)",
        "Unix",
        "Unix (alternative)",
        "Windows / IIS",
        "OpenVMS",
        "IBM z/OS",
        "HP NonStop"
    ];

    internal static readonly IReadOnlyList<string> DataConnections = ["Passive (recommended)", "Active"];

    // 16 GiB/s: far past any link StorageHub will see, and small enough that KiB * 1024 cannot overflow.
    private const long MaximumSpeedLimitKib = 16L * 1024 * 1024;

    private static ConnectionOperationalOptionsDocument BuildOperationalOptions(
        StorageProviderKind provider,
        ConnectionProviderDefaults? defaults,
        IReadOnlyDictionary<string, string> values)
    {
        defaults ??= ConnectionDefaultSettings.Get(provider, stored: null);
        var isFtp = provider is StorageProviderKind.Ftp or StorageProviderKind.Ftps;
        return new ConnectionOperationalOptionsDocument(
            // Only FTP keeps a connect and a read timeout apart, so only its editor offers them.
            ConnectTimeoutSeconds: isFtp ? ParseSeconds(values, ConnectTimeoutKey, 600, defaults.ConnectTimeoutSeconds) : defaults.ConnectTimeoutSeconds,
            OperationTimeoutSeconds: isFtp ? ParseSeconds(values, ReadTimeoutKey, 86_400, defaults.OperationTimeoutSeconds) : defaults.OperationTimeoutSeconds,
            EncodingName: isFtp ? ParseEncoding(values) : "utf-8",
            MaximumRetryAttempts: defaults.MaximumRetryAttempts,
            ProxyEndpoint: ParseProxyAddress(values, out var scheme),
            ProxyUsername: ProxySignIn(values, scheme, out var password),
            ProxyPasswordReference: password,
            UploadBytesPerSecond: ParseSpeedLimit(values, UploadLimitKey),
            DownloadBytesPerSecond: ParseSpeedLimit(values, DownloadLimitKey));
    }

    private static int ParseSeconds(IReadOnlyDictionary<string, string> values, string key, int maximum, int fallback)
    {
        var value = Get(values, key);
        if (value is null)
        {
            return fallback;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) &&
            seconds >= 1 && seconds <= maximum
                ? seconds
                : throw new ArgumentException(Ui.Validation.TimeoutMustBeSeconds, nameof(values));
    }

    /// <summary>The file-name encoding, checked against the same table the agent's FTP client uses.</summary>
    private static string ParseEncoding(IReadOnlyDictionary<string, string> values)
    {
        var name = Get(values, EncodingKey);
        if (name is null)
        {
            return "utf-8";
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            _ = Encoding.GetEncoding(name);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException(Ui.Validation.EncodingIsUnknown, nameof(values));
        }

        return name.ToLowerInvariant();
    }

    /// <summary>An FTP server's Advanced options, or null when every one is left as it comes.</summary>
    private static ConnectionFtpOptionsDocument? BuildFtpOptions(IReadOnlyDictionary<string, string> values)
    {
        var timeZone = Get(values, ServerTimeZoneKey);
        if (timeZone is not null && !TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out _))
        {
            throw new ArgumentException(Ui.Validation.TimeZoneIsUnknown, nameof(values));
        }

        var format = Get(values, ListingFormatKey) is { } chosen
            ? Math.Max(0, ListingFormats.ToList().IndexOf(chosen))
            : 0;
        var active = string.Equals(Get(values, DataConnectionKey), DataConnections[1], StringComparison.Ordinal);

        int? lowest = null;
        int? highest = null;
        if (Get(values, ActivePortsKey) is { } range)
        {
            var ends = range.Split('-', 2, StringSplitOptions.TrimEntries);
            if (ends.Length != 2 ||
                !int.TryParse(ends[0], NumberStyles.None, CultureInfo.InvariantCulture, out var low) ||
                !int.TryParse(ends[1], NumberStyles.None, CultureInfo.InvariantCulture, out var high) ||
                low < 1024 || high > 65_535 || low > high)
            {
                throw new ArgumentException(Ui.Validation.ActivePortsAreInvalid, nameof(values));
            }

            (lowest, highest) = (low, high);
        }

        var address = Get(values, ActiveAddressKey);
        if (address is not null && !System.Net.IPAddress.TryParse(address, out _))
        {
            throw new ArgumentException(Ui.Validation.ActiveAddressIsInvalid, nameof(values));
        }

        if (!active && (lowest is not null || address is not null))
        {
            throw new ArgumentException(Ui.Validation.ActiveSettingsNeedActiveMode, nameof(values));
        }

        var options = new ConnectionFtpOptionsDocument(
            timeZone,
            (ConnectionFtpListingFormat)format,
            active ? ConnectionFtpDataConnectionMode.Active : ConnectionFtpDataConnectionMode.Passive,
            lowest,
            highest,
            address);
        return options == new ConnectionFtpOptionsDocument() ? null : options;
    }

    /// <summary>
    /// The proxy as scheme://host:port, or null to connect directly.
    /// </summary>
    /// <remarks>
    /// HTTPS is refused here, where it can be said beside the field, rather than saved and then
    /// refused on connecting: no provider can speak TLS to a proxy.
    /// </remarks>
    private static string? ParseProxyAddress(IReadOnlyDictionary<string, string> values, out string? scheme)
    {
        scheme = null;
        var value = Get(values, ProxyAddressKey);
        if (value is null)
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var address) ||
            string.IsNullOrEmpty(address.Host) ||
            !string.IsNullOrEmpty(address.UserInfo) ||
            address.AbsolutePath is not ("" or "/") ||
            !string.IsNullOrEmpty(address.Query) ||
            !string.IsNullOrEmpty(address.Fragment))
        {
            throw new ArgumentException(Ui.Validation.ProxyAddressIsInvalid, nameof(values));
        }

        if (address.Scheme == Uri.UriSchemeHttps)
        {
            throw new ArgumentException(Ui.Validation.HttpsProxiesAreNotSupported, nameof(values));
        }

        // A SOCKS address has no default port to fall back on, and "socks5://host" would otherwise
        // parse with port -1.
        if (address.Scheme is not ("http" or "socks4" or "socks5") || address.Port <= 0)
        {
            throw new ArgumentException(Ui.Validation.ProxyAddressIsInvalid, nameof(values));
        }

        scheme = address.Scheme;
        return address.GetLeftPart(UriPartial.Authority);
    }

    private static string? ProxySignIn(
        IReadOnlyDictionary<string, string> values,
        string? scheme,
        out string? passwordReference)
    {
        var username = Get(values, ProxyUsernameKey);
        passwordReference = Get(values, ProxyPasswordKey);
        if (scheme is null)
        {
            // No proxy, so nothing to sign in to; whatever was left in the fields is dropped.
            passwordReference = null;
            return null;
        }

        if (passwordReference is not null && (username is null || scheme == "socks4"))
        {
            throw new ArgumentException(Ui.Validation.ProxyPasswordNeedsAUserName, nameof(values));
        }

        return username;
    }

    /// <summary>A limit in KiB/s as bytes per second; empty or 0 is no limit.</summary>
    private static long? ParseSpeedLimit(IReadOnlyDictionary<string, string> values, string key)
    {
        var value = Get(values, key);
        if (value is null)
        {
            return null;
        }

        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var kib) ||
            kib > MaximumSpeedLimitKib)
        {
            throw new ArgumentException(Ui.Validation.SpeedLimitMustBeAWholeNumber, nameof(values));
        }

        return kib == 0 ? null : kib * 1024;
    }

    private static string FormatSpeedLimit(long? bytesPerSecond) => bytesPerSecond is { } bytes
        ? Math.Max(1, bytes / 1024).ToString(CultureInfo.InvariantCulture)
        : string.Empty;

    public static IReadOnlyDictionary<string, string> ToEditorValues(ConnectionProfileDocument profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["profileName"] = profile.Draft.Metadata.DisplayName,
            ["folder"] = profile.Draft.Metadata.FolderPath ?? string.Empty,
            ["labels"] = profile.Draft.Metadata.Tags is { Length: > 0 }
                ? string.Join(", ", profile.Draft.Metadata.Tags)
                : string.Empty,
            ["folderTags"] = FormatFolderAndTags(profile.Draft.Metadata),
            ["iconKey"] = profile.Draft.Metadata.IconKey ?? string.Empty,
            ["accentColor"] = profile.Draft.Metadata.AccentColor ?? string.Empty
        };
        AddEndpoint(values, profile.Draft.Endpoint);
        AddAuthentication(values, profile.Draft.Authentication);
        if (profile.Draft.Endpoint.Provider is StorageConnectionProvider.Ftp or StorageConnectionProvider.Ftps)
        {
            var options = profile.Draft.OperationalOptions;
            values[EncodingKey] = options.EncodingName;
            values[ConnectTimeoutKey] = options.ConnectTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
            values[ReadTimeoutKey] = options.OperationTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        }

        values[ProxyAddressKey] = profile.Draft.OperationalOptions.ProxyEndpoint?.TrimEnd('/') ?? string.Empty;
        values[ProxyUsernameKey] = profile.Draft.OperationalOptions.ProxyUsername ?? string.Empty;
        values[ProxyPasswordKey] = profile.Draft.OperationalOptions.ProxyPasswordReference ?? string.Empty;
        values[UploadLimitKey] = FormatSpeedLimit(profile.Draft.OperationalOptions.UploadBytesPerSecond);
        values[DownloadLimitKey] = FormatSpeedLimit(profile.Draft.OperationalOptions.DownloadBytesPerSecond);
        return values;
    }

    private static ConnectionEndpointDocument BuildEndpoint(
        StorageProviderKind provider,
        IReadOnlyDictionary<string, string> values) => provider switch
        {
            StorageProviderKind.Local => new ConnectionEndpointDocument(
                StorageConnectionProvider.Local,
                RootPath: Require(values, "rootPath", Ui.Validation.AnAbsoluteLocalOrUNCRootIs)),
            StorageProviderKind.S3 => BuildS3Endpoint(values),
            StorageProviderKind.Ftp => BuildFtpEndpoint(values),
            StorageProviderKind.Ftps => BuildFtpsEndpoint(values),
            StorageProviderKind.Sftp => BuildSftpEndpoint(values),
            StorageProviderKind.Ssh => BuildSshEndpoint(values),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };

    private static ConnectionEndpointDocument BuildSftpEndpoint(IReadOnlyDictionary<string, string> values)
    {
        ValidateFingerprint(Require(
            values,
            "hostKeyFingerprint",
            Ui.Validation.AVerifiedSSHHostKeySHA256));
        return new ConnectionEndpointDocument(
            StorageConnectionProvider.Sftp,
            RootPath: NormalizeProviderRoot(Get(values, "initialPath")),
            Host: Require(values, "host", Ui.Validation.AnSFTPHostIsRequired),
            Port: ParsePort(values, 22),
            SshHostKeyPolicy: ConnectionSshHostKeyPolicy.Pinned);
    }

    private static ConnectionEndpointDocument BuildSshEndpoint(IReadOnlyDictionary<string, string> values)
    {
        ValidateFingerprint(Require(
            values,
            "hostKeyFingerprint",
            Ui.Validation.AVerifiedSSHHostKeySHA256));
        return new ConnectionEndpointDocument(
            StorageConnectionProvider.Ssh,
            Host: Require(values, "host", Ui.Validation.AnSSHHostIsRequired),
            Port: ParsePort(values, 22),
            SshHostKeyPolicy: ConnectionSshHostKeyPolicy.Pinned);
    }

    private static ConnectionEndpointDocument BuildS3Endpoint(IReadOnlyDictionary<string, string> values)
    {
        var endpoint = NormalizeS3ServiceEndpoint(Require(
            values,
            "endpoint",
            Ui.Validation.AnHTTPSS3ServiceEndpointIsRequired));
        var serviceType = Get(values, "s3ServiceType") ?? AmazonS3ServiceType;
        var isCloudflareR2 = string.Equals(
                serviceType,
                CloudflareR2ServiceType,
                StringComparison.Ordinal) ||
            IsCloudflareR2Endpoint(endpoint);
        var region = isCloudflareR2
            ? "auto"
            : Require(values, "region", Ui.Validation.ASigningRegionIsRequiredForThis);
        var bucket = Require(values, "bucket", Ui.Validation.AnS3BucketIsRequired);
        if (bucket.Length is < 3 or > 255 ||
            bucket.Any(static character => char.IsControl(character) ||
                char.IsWhiteSpace(character) || character is '/' or '\\'))
        {
            throw new ArgumentException(Ui.Validation.TheS3BucketNameMustBe3, nameof(values));
        }

        return new ConnectionEndpointDocument(
            StorageConnectionProvider.S3,
            RootPath: NormalizeProviderRoot(Get(values, "prefix")),
            Bucket: bucket,
            Region: region,
            ServiceEndpoint: endpoint,
            ForcePathStyle: isCloudflareR2 || string.Equals(
                Get(values, "addressingStyle"),
                "Path-style",
                StringComparison.Ordinal));
    }

    internal static bool IsCloudflareR2Endpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return false;
        }

        var candidate = endpoint.Contains("://", StringComparison.Ordinal)
            ? endpoint
            : "https://" + endpoint;
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
            (uri.IdnHost.Equals("r2.cloudflarestorage.com", StringComparison.OrdinalIgnoreCase) ||
                uri.IdnHost.EndsWith(".r2.cloudflarestorage.com", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeS3ServiceEndpoint(string value)
    {
        var candidate = value.Contains("://", StringComparison.Ordinal)
            ? value
            : "https://" + value;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ArgumentException(
                Ui.Validation.TheS3ServiceEndpointMustBeA,
                nameof(value));
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                Ui.Validation.TheS3ServiceEndpointMustUseHTTPS,
                nameof(value));
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath is not "" and not "/")
        {
            throw new ArgumentException(
                Ui.Validation.EnterTheS3ServiceEndpointOnlyWithout,
                nameof(value));
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    private static ConnectionEndpointDocument BuildFtpEndpoint(IReadOnlyDictionary<string, string> values)
    {
        if (!ParseBoolean(values, "acknowledgePlaintext"))
        {
            throw new ArgumentException(
                Ui.Validation.PlainFTPRequiresExplicitPlaintextTransportAcknowledgemen,
                nameof(values));
        }

        return new ConnectionEndpointDocument(
            StorageConnectionProvider.Ftp,
            RootPath: NormalizeProviderRoot(Get(values, "initialPath")),
            Host: Require(values, "host", Ui.Validation.AnFTPHostIsRequired),
            Port: ParsePort(values, 21),
            AllowInsecureTransport: true,
            Ftp: BuildFtpOptions(values));
    }

    private static ConnectionEndpointDocument BuildFtpsEndpoint(IReadOnlyDictionary<string, string> values)
    {
        var pinned = string.Equals(
            Get(values, "trustMode"),
            "System trust + certificate pin",
            StringComparison.Ordinal);
        if (pinned)
        {
            ValidateFingerprint(Require(
                values,
                "certificatePin",
                Ui.Validation.AVerifiedCertificateSHA256PinIs));
        }

        return new ConnectionEndpointDocument(
            StorageConnectionProvider.Ftps,
            RootPath: NormalizeProviderRoot(Get(values, "initialPath")),
            Host: Require(values, "host", Ui.Validation.AnFTPSHostIsRequired),
            Port: ParsePort(values, 21),
            TlsPolicy: pinned
                ? ConnectionTlsCertificatePolicy.Pinned
                : ConnectionTlsCertificatePolicy.SystemTrust,
            FtpsTlsMode: string.Equals(
                Get(values, "tlsMode"),
                "Implicit TLS",
                StringComparison.Ordinal)
                    ? ConnectionFtpsTlsMode.Implicit
                    : ConnectionFtpsTlsMode.Explicit,
            ClientCertificatePfxReference: Get(values, "clientCertificateReference"),
            ClientCertificatePasswordReference: Get(values, "clientCertificatePasswordReference"),
            Ftp: BuildFtpOptions(values));
    }

    private static ConnectionAuthenticationDocument BuildAuthentication(
        StorageProviderKind provider,
        IReadOnlyDictionary<string, string> values) => provider switch
        {
            StorageProviderKind.Local => BuildLocalAuthentication(values),
            StorageProviderKind.S3 => BuildS3Authentication(values),
            StorageProviderKind.Ftp or StorageProviderKind.Ftps =>
                new ConnectionAuthenticationDocument(
                    ConnectionAuthenticationKind.UsernamePassword,
                    Username: Require(values, "username", Ui.Validation.AUsernameIsRequired),
                    PasswordReference: Require(values, "passwordReference", Ui.Validation.AVaultPasswordReferenceIsRequired)),
            StorageProviderKind.Sftp => BuildSshAuthentication(values, allowMultiFactor: false),
            StorageProviderKind.Ssh => BuildSshAuthentication(values, allowMultiFactor: true),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };

    private static ConnectionAuthenticationDocument BuildLocalAuthentication(
        IReadOnlyDictionary<string, string> values)
    {
        if (!string.IsNullOrEmpty(Get(values, "credentialReference")))
        {
            throw new ArgumentException(
                Ui.Validation.AlternateWindowsIdentitiesAreNotAvailableUntil,
                nameof(values));
        }

        return new ConnectionAuthenticationDocument(ConnectionAuthenticationKind.None);
    }

    private static ConnectionAuthenticationDocument BuildS3Authentication(
        IReadOnlyDictionary<string, string> values)
    {
        var access = Get(values, "accessKeyReference");
        var secret = Get(values, "secretAccessKeyReference");
        var token = Get(values, "sessionTokenReference");
        if (access is null && secret is null && token is null)
        {
            return new ConnectionAuthenticationDocument(ConnectionAuthenticationKind.S3DefaultCredentialChain);
        }

        if (access is null || secret is null)
        {
            throw new ArgumentException(
                Ui.Validation.S3AccessKeyAuthenticationRequiresBothAccess,
                nameof(values));
        }

        return new ConnectionAuthenticationDocument(
            ConnectionAuthenticationKind.S3AccessKey,
            AccessKeyReference: access,
            SecretKeyReference: secret,
            SessionTokenReference: token);
    }

    private static ConnectionAuthenticationDocument BuildSshAuthentication(
        IReadOnlyDictionary<string, string> values,
        bool allowMultiFactor)
    {
        var mode = Get(values, "authenticationMode") ?? "Private key reference";
        var username = Require(values, "username", Ui.Validation.AnSFTPUsernameIsRequired);
        return mode switch
        {
            "Private key reference" => new ConnectionAuthenticationDocument(
                ConnectionAuthenticationKind.SftpPrivateKey,
                Username: username,
                PrivateKeyReference: Require(
                    values,
                    "privateKeyReference",
                    Ui.Validation.AnEncryptedPrivateKeyVaultReferenceIs2),
                PrivateKeyPassphraseReference: Require(
                    values,
                    "privateKeyPassphraseReference",
                    Ui.Validation.APrivateKeyPassphraseVaultReferenceIs2)),
            "Password reference" => new ConnectionAuthenticationDocument(
                ConnectionAuthenticationKind.UsernamePassword,
                Username: username,
                PasswordReference: Require(
                    values,
                    "passwordReference",
                    Ui.Validation.AVaultPasswordReferenceIsRequired)),
            "Private key + password (MFA)" when allowMultiFactor => new ConnectionAuthenticationDocument(
                ConnectionAuthenticationKind.SshPrivateKeyPassword,
                Username: username,
                PasswordReference: Require(
                    values,
                    "passwordReference",
                    Ui.Validation.AnAccountPasswordVaultReferenceIsRequired),
                PrivateKeyReference: Require(
                    values,
                    "privateKeyReference",
                    Ui.Validation.AnEncryptedPrivateKeyVaultReferenceIs),
                PrivateKeyPassphraseReference: Require(
                    values,
                    "privateKeyPassphraseReference",
                    Ui.Validation.APrivateKeyPassphraseVaultReferenceIs)),
            _ => throw new ArgumentException(
                Ui.Validation.SSHAgentAuthenticationIsNotAvailableIn,
                nameof(values))
        };
    }

    private static void AddEndpoint(
        Dictionary<string, string> values,
        ConnectionEndpointDocument endpoint)
    {
        Add(values, "rootPath", endpoint.Provider == StorageConnectionProvider.Local ? endpoint.RootPath : null);
        Add(values, "initialPath", endpoint.Provider is StorageConnectionProvider.Ftp or
            StorageConnectionProvider.Ftps or StorageConnectionProvider.Sftp ? endpoint.RootPath : null);
        Add(values, "prefix", endpoint.Provider == StorageConnectionProvider.S3 ? endpoint.RootPath : null);
        Add(values, "host", endpoint.Host);
        Add(values, "port", endpoint.Port?.ToString(CultureInfo.InvariantCulture));
        Add(values, "bucket", endpoint.Bucket);
        Add(values, "region", endpoint.Region);
        Add(values, "endpoint", endpoint.ServiceEndpoint);
        Add(values, "s3ServiceType", endpoint.Provider == StorageConnectionProvider.S3
            ? IsCloudflareR2Endpoint(endpoint.ServiceEndpoint)
                ? CloudflareR2ServiceType
                : endpoint.ServiceEndpoint?.Contains("amazonaws.com", StringComparison.OrdinalIgnoreCase) == true
                    ? AmazonS3ServiceType
                    : OtherS3ServiceType
            : null);
        Add(values, "addressingStyle", endpoint.ForcePathStyle
            ? "Path-style"
            : "Virtual-hosted (recommended)");
        Add(values, "acknowledgePlaintext", endpoint.AllowInsecureTransport.ToString(CultureInfo.InvariantCulture));
        Add(values, "tlsMode", endpoint.FtpsTlsMode == ConnectionFtpsTlsMode.Implicit
            ? "Implicit TLS"
            : "Explicit TLS (recommended)");
        Add(values, "clientCertificateReference", endpoint.ClientCertificatePfxReference);
        Add(values, "clientCertificatePasswordReference", endpoint.ClientCertificatePasswordReference);
        if (endpoint.Provider is StorageConnectionProvider.Ftp or StorageConnectionProvider.Ftps)
        {
            var ftp = endpoint.Ftp ?? new ConnectionFtpOptionsDocument();
            values[ServerTimeZoneKey] = ftp.ServerTimeZone ?? string.Empty;
            values[ListingFormatKey] = ListingFormats[(int)ftp.ListingFormat];
            values[DataConnectionKey] = DataConnections[ftp.DataConnectionMode == ConnectionFtpDataConnectionMode.Active ? 1 : 0];
            values[ActivePortsKey] = ftp.ActivePortMinimum is { } low && ftp.ActivePortMaximum is { } high
                ? string.Create(CultureInfo.InvariantCulture, $"{low}-{high}")
                : string.Empty;
            values[ActiveAddressKey] = ftp.ActiveExternalAddress ?? string.Empty;
        }
    }

    private static void AddAuthentication(
        IDictionary<string, string> values,
        ConnectionAuthenticationDocument authentication)
    {
        Add(values, "username", authentication.Username);
        Add(values, "passwordReference", authentication.PasswordReference);
        Add(values, "accessKeyReference", authentication.AccessKeyReference);
        Add(values, "secretAccessKeyReference", authentication.SecretKeyReference);
        Add(values, "sessionTokenReference", authentication.SessionTokenReference);
        Add(values, "privateKeyReference", authentication.PrivateKeyReference);
        Add(values, "privateKeyPassphraseReference", authentication.PrivateKeyPassphraseReference);
        Add(values, "authenticationMode", authentication.Kind switch
        {
            ConnectionAuthenticationKind.SftpPrivateKey => "Private key reference",
            ConnectionAuthenticationKind.UsernamePassword => "Password reference",
            ConnectionAuthenticationKind.SshPrivateKeyPassword => "Private key + password (MFA)",
            _ => null
        });
    }

    private static void Add(IDictionary<string, string> values, string key, string? value)
    {
        if (value is not null)
        {
            values[key] = value;
        }
    }

    private static string FormatFolderAndTags(ConnectionProfileMetadataDocument metadata)
    {
        var tags = metadata.Tags is { Length: > 0 } ? string.Join(", ", metadata.Tags) : string.Empty;
        return (metadata.FolderPath, tags) switch
        {
            (null, "") => string.Empty,
            ({ } folder, "") => folder,
            (null, { } tagText) => $"· {tagText}",
            ({ } folder, { } tagText) => $"{folder} · {tagText}"
        };
    }

    private static (string? Folder, string[] Tags) ParseFolderAndTags(string? value)
    {
        if (value is null)
        {
            return (null, []);
        }

        var separator = value.IndexOf('·');
        if (separator < 0)
        {
            return (value.Trim(), []);
        }

        var folder = value[..separator].Trim();
        var tags = value[(separator + 1)..]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return (folder.Length == 0 ? null : folder, tags);
    }

    private static string? NormalizeFolder(string? value)
    {
        var folder = value?.Trim();
        return string.IsNullOrEmpty(folder) ? null : folder;
    }

    private static string[] ParseLabels(string? value) => string.IsNullOrWhiteSpace(value)
        ? []
        : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? NormalizeProviderRoot(string? value)
    {
        var normalized = value?.Trim().Trim('/');
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static int ParsePort(IReadOnlyDictionary<string, string> values, int fallback)
    {
        var value = Get(values, "port");
        if (value is null)
        {
            return fallback;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) &&
            port is >= 1 and <= 65_535
                ? port
                : throw new ArgumentException(Ui.Validation.ThePortMustBeBetween1And, nameof(values));
    }

    private static bool ParseBoolean(IReadOnlyDictionary<string, string> values, string key) =>
        bool.TryParse(Get(values, key), out var result) && result;

    private static void ValidateFingerprint(string value)
    {
        if (!ConnectionTrustIpcLimits.IsValidFingerprint(value))
        {
            throw new ArgumentException(
                Ui.Validation.TheServerIdentityFingerprintMustBeSHA,
                nameof(value));
        }
    }

    private static string Require(
        IReadOnlyDictionary<string, string> values,
        string key,
        string message) => Get(values, key) ?? throw new ArgumentException(message, nameof(values));

    private static string? Get(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
}
