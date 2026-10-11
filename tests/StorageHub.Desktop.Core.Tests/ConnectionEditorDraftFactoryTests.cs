using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Tests;

public sealed class ConnectionEditorDraftFactoryTests
{
    [Theory]
    [InlineData(StorageProviderKind.S3, 30, 3)]
    [InlineData(StorageProviderKind.Ftp, 30, 0)]
    [InlineData(StorageProviderKind.Ftps, 30, 0)]
    [InlineData(StorageProviderKind.Sftp, 30, 0)]
    [InlineData(StorageProviderKind.Ssh, 30, 0)]
    [InlineData(StorageProviderKind.WebDav, 600, 0)]
    [InlineData(StorageProviderKind.AzureBlob, 30, 3)]
    [InlineData(StorageProviderKind.OpenStackSwift, 600, 0)]
    public void RemoteDraftsUseOnlyOperationalDefaultsTheirProviderCanEnforce(
        StorageProviderKind provider,
        int operationTimeoutSeconds,
        int maximumRetryAttempts)
    {
        var draft = ConnectionEditorDraftFactory.Build(provider, ValidValues(provider));

        Assert.Equal(30, draft.OperationalOptions.ConnectTimeoutSeconds);
        Assert.Equal(operationTimeoutSeconds, draft.OperationalOptions.OperationTimeoutSeconds);
        Assert.Equal(maximumRetryAttempts, draft.OperationalOptions.MaximumRetryAttempts);
    }

    [Fact]
    public void SpeedLimitsAreKibibytesPerSecondAndSurviveAnEdit()
    {
        var values = ValidValues(StorageProviderKind.Sftp);
        values[ConnectionEditorDraftFactory.UploadLimitKey] = "512";
        values[ConnectionEditorDraftFactory.DownloadLimitKey] = " ";

        var draft = ConnectionEditorDraftFactory.Build(StorageProviderKind.Sftp, values);
        var reopened = ConnectionEditorDraftFactory.ToEditorValues(
            new ConnectionProfileDocument(Guid.NewGuid(), 1, draft, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));

        Assert.True(draft.HasValidBounds);
        Assert.Equal(512 * 1024, draft.OperationalOptions.UploadBytesPerSecond);
        Assert.Null(draft.OperationalOptions.DownloadBytesPerSecond);
        Assert.Equal("512", reopened[ConnectionEditorDraftFactory.UploadLimitKey]);
        Assert.Equal(string.Empty, reopened[ConnectionEditorDraftFactory.DownloadLimitKey]);
    }

    [Fact]
    public void AProxyAndItsSignInSurviveAnEdit()
    {
        var values = ValidValues(StorageProviderKind.Sftp);
        values[ConnectionEditorDraftFactory.ProxyAddressKey] = " socks5://proxy.example.com:1080/ ";
        values[ConnectionEditorDraftFactory.ProxyUsernameKey] = "relay";
        values[ConnectionEditorDraftFactory.ProxyPasswordKey] = "shs_CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";

        var draft = ConnectionEditorDraftFactory.Build(StorageProviderKind.Sftp, values);
        var reopened = ConnectionEditorDraftFactory.ToEditorValues(
            new ConnectionProfileDocument(Guid.NewGuid(), 1, draft, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));

        Assert.True(draft.HasValidBounds);
        Assert.Equal("socks5://proxy.example.com:1080", draft.OperationalOptions.ProxyEndpoint);
        Assert.Equal("relay", draft.OperationalOptions.ProxyUsername);
        Assert.Equal(values[ConnectionEditorDraftFactory.ProxyPasswordKey], draft.OperationalOptions.ProxyPasswordReference);
        Assert.Equal("socks5://proxy.example.com:1080", reopened[ConnectionEditorDraftFactory.ProxyAddressKey]);
        Assert.Equal("relay", reopened[ConnectionEditorDraftFactory.ProxyUsernameKey]);
    }

    [Fact]
    public void FtpAdvancedSettingsSurviveAnEdit()
    {
        var values = ValidValues(StorageProviderKind.Ftps);
        values[ConnectionEditorDraftFactory.EncodingKey] = "Windows-1252";
        values[ConnectionEditorDraftFactory.ConnectTimeoutKey] = "15";
        values[ConnectionEditorDraftFactory.ReadTimeoutKey] = "300";
        values[ConnectionEditorDraftFactory.ServerTimeZoneKey] = "Europe/Copenhagen";
        values[ConnectionEditorDraftFactory.ListingFormatKey] = "Windows / IIS";
        values[ConnectionEditorDraftFactory.DataConnectionKey] = "Active";
        values[ConnectionEditorDraftFactory.ActivePortsKey] = "50000 - 50100";
        values[ConnectionEditorDraftFactory.ActiveAddressKey] = "203.0.113.7";

        var draft = ConnectionEditorDraftFactory.Build(StorageProviderKind.Ftps, values);
        var reopened = ConnectionEditorDraftFactory.ToEditorValues(
            new ConnectionProfileDocument(Guid.NewGuid(), 1, draft, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));

        Assert.True(draft.HasValidBounds);
        Assert.Equal("windows-1252", draft.OperationalOptions.EncodingName);
        Assert.Equal(15, draft.OperationalOptions.ConnectTimeoutSeconds);
        Assert.Equal(300, draft.OperationalOptions.OperationTimeoutSeconds);
        var ftp = draft.Endpoint.Ftp!;
        Assert.Equal("Europe/Copenhagen", ftp.ServerTimeZone);
        Assert.Equal(ConnectionFtpListingFormat.Windows, ftp.ListingFormat);
        Assert.Equal(ConnectionFtpDataConnectionMode.Active, ftp.DataConnectionMode);
        Assert.Equal((50_000, 50_100), (ftp.ActivePortMinimum, ftp.ActivePortMaximum));
        Assert.Equal("203.0.113.7", ftp.ActiveExternalAddress);

        Assert.Equal("300", reopened[ConnectionEditorDraftFactory.ReadTimeoutKey]);
        Assert.Equal("Windows / IIS", reopened[ConnectionEditorDraftFactory.ListingFormatKey]);
        Assert.Equal("Active", reopened[ConnectionEditorDraftFactory.DataConnectionKey]);
        Assert.Equal("50000-50100", reopened[ConnectionEditorDraftFactory.ActivePortsKey]);
    }

    /// <summary>An FTP server left as it comes carries no options block, so the stored row is as before.</summary>
    [Fact]
    public void AnFtpServerLeftAsItComesHasNoOptions()
    {
        var values = ValidValues(StorageProviderKind.Ftp);
        values[ConnectionEditorDraftFactory.ListingFormatKey] = "Auto";
        values[ConnectionEditorDraftFactory.DataConnectionKey] = "Passive (recommended)";

        var draft = ConnectionEditorDraftFactory.Build(StorageProviderKind.Ftp, values);

        Assert.Null(draft.Endpoint.Ftp);
        Assert.Equal("utf-8", draft.OperationalOptions.EncodingName);
    }

    [Theory]
    [InlineData(nameof(ConnectionEditorDraftFactory.EncodingKey), "klingon-8", nameof(Ui.Validation.EncodingIsUnknown))]
    [InlineData(nameof(ConnectionEditorDraftFactory.ConnectTimeoutKey), "0", nameof(Ui.Validation.TimeoutMustBeSeconds))]
    [InlineData(nameof(ConnectionEditorDraftFactory.ReadTimeoutKey), "soon", nameof(Ui.Validation.TimeoutMustBeSeconds))]
    [InlineData(nameof(ConnectionEditorDraftFactory.ServerTimeZoneKey), "Mars/Olympus_Mons", nameof(Ui.Validation.TimeZoneIsUnknown))]
    [InlineData(nameof(ConnectionEditorDraftFactory.ActiveAddressKey), "203.0.113.7", nameof(Ui.Validation.ActiveSettingsNeedActiveMode))]
    public void AnFtpSettingStorageHubCannotUseIsRefusedBesideTheField(string field, string value, string message)
    {
        var values = ValidValues(StorageProviderKind.Ftp);
        var key = (string)typeof(ConnectionEditorDraftFactory)
            .GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;
        values[key] = value;

        var error = Assert.Throws<ArgumentException>(() => ConnectionEditorDraftFactory.Build(StorageProviderKind.Ftp, values));

        var expected = (string)typeof(ValidationStrings).GetProperty(message)!.GetValue(Ui.Validation)!;
        Assert.StartsWith(expected, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("50000")]
    [InlineData("80-90")]
    [InlineData("50100-50000")]
    public void ActivePortsMustBeARangeAboveTheReservedPorts(string range)
    {
        var values = ValidValues(StorageProviderKind.Ftp);
        values[ConnectionEditorDraftFactory.DataConnectionKey] = "Active";
        values[ConnectionEditorDraftFactory.ActivePortsKey] = range;

        var error = Assert.Throws<ArgumentException>(() => ConnectionEditorDraftFactory.Build(StorageProviderKind.Ftp, values));

        Assert.StartsWith(Ui.Validation.ActivePortsAreInvalid, error.Message, StringComparison.Ordinal);
    }

    /// <summary>The IPC and domain enums are cast between by number, so they must list the same values.</summary>
    [Fact]
    public void TheWireListingFormatsMatchTheOffer()
    {
        Assert.Equal(Enum.GetValues<ConnectionFtpListingFormat>().Length, ConnectionEditorDraftFactory.ListingFormats.Count);
    }

    /// <summary>With no proxy address, a sign-in left in the other two fields is not saved.</summary>
    [Fact]
    public void NoProxyAddressIsADirectConnection()
    {
        var values = ValidValues(StorageProviderKind.Ftps);
        values[ConnectionEditorDraftFactory.ProxyUsernameKey] = "leftover";

        var draft = ConnectionEditorDraftFactory.Build(StorageProviderKind.Ftps, values);

        Assert.Null(draft.OperationalOptions.ProxyEndpoint);
        Assert.Null(draft.OperationalOptions.ProxyUsername);
        Assert.True(draft.HasValidBounds);
    }

    [Theory]
    [InlineData("https://proxy.example.com:8443", nameof(Ui.Validation.HttpsProxiesAreNotSupported))]
    [InlineData("socks5://proxy.example.com", nameof(Ui.Validation.ProxyAddressIsInvalid))]
    [InlineData("ftp://proxy.example.com:21", nameof(Ui.Validation.ProxyAddressIsInvalid))]
    [InlineData("proxy.example.com:3128", nameof(Ui.Validation.ProxyAddressIsInvalid))]
    [InlineData("http://user:pw@proxy.example.com:3128", nameof(Ui.Validation.ProxyAddressIsInvalid))]
    public void AProxyAddressStorageHubCannotUseIsRefusedBesideTheField(string address, string message)
    {
        var values = ValidValues(StorageProviderKind.S3);
        values[ConnectionEditorDraftFactory.ProxyAddressKey] = address;

        var error = Assert.Throws<ArgumentException>(() => ConnectionEditorDraftFactory.Build(StorageProviderKind.S3, values));

        var expected = (string)typeof(ValidationStrings).GetProperty(message)!.GetValue(Ui.Validation)!;
        Assert.StartsWith(expected, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("socks5://proxy.example.com:1080", "")]
    [InlineData("socks4://proxy.example.com:1080", "relay")]
    public void AProxyPasswordNeedsAUserNameAndSocks5(string address, string username)
    {
        var values = ValidValues(StorageProviderKind.Sftp);
        values[ConnectionEditorDraftFactory.ProxyAddressKey] = address;
        values[ConnectionEditorDraftFactory.ProxyUsernameKey] = username;
        values[ConnectionEditorDraftFactory.ProxyPasswordKey] = "shs_CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";

        var error = Assert.Throws<ArgumentException>(() => ConnectionEditorDraftFactory.Build(StorageProviderKind.Sftp, values));

        Assert.StartsWith(Ui.Validation.ProxyPasswordNeedsAUserName, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0", null)]
    [InlineData("1", 1024L)]
    public void ZeroIsNoLimit(string kib, long? bytesPerSecond)
    {
        var values = ValidValues(StorageProviderKind.Ftps);
        values[ConnectionEditorDraftFactory.DownloadLimitKey] = kib;

        var draft = ConnectionEditorDraftFactory.Build(StorageProviderKind.Ftps, values);

        Assert.Equal(bytesPerSecond, draft.OperationalOptions.DownloadBytesPerSecond);
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("1.5")]
    [InlineData("fast")]
    [InlineData("99999999999")]
    public void ASpeedLimitThatIsNotAWholeNumberIsRefused(string kib)
    {
        var values = ValidValues(StorageProviderKind.S3);
        values[ConnectionEditorDraftFactory.UploadLimitKey] = kib;

        var error = Assert.Throws<ArgumentException>(() => ConnectionEditorDraftFactory.Build(StorageProviderKind.S3, values));

        Assert.StartsWith(Ui.Validation.SpeedLimitMustBeAWholeNumber, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildsSftpPrivateKeyDraftUsingOnlyOpaqueReferences()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["profileName"] = "Production SFTP",
            ["host"] = "sftp.example.com",
            ["port"] = "22",
            ["initialPath"] = "/exports",
            ["username"] = "backup",
            ["authenticationMode"] = "Private key reference",
            ["privateKeyReference"] = "shs_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            ["privateKeyPassphraseReference"] = "shs_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
            ["hostKeyFingerprint"] = new string('A', 64)
        };

        var draft = ConnectionEditorDraftFactory.Build(StorageProviderKind.Sftp, values);

        Assert.True(draft.HasValidBounds);
        Assert.Equal(StorageConnectionProvider.Sftp, draft.Endpoint.Provider);
        Assert.Equal(ConnectionAuthenticationKind.SftpPrivateKey, draft.Authentication.Kind);
        Assert.Equal(values["privateKeyReference"], draft.Authentication.PrivateKeyReference);
        Assert.Equal(values["privateKeyPassphraseReference"], draft.Authentication.PrivateKeyPassphraseReference);
    }

    [Fact]
    public void BuildsSshClientDraftWithLabelsAndClientType()
    {
        var values = ValidValues(StorageProviderKind.Ssh);
        values["folder"] = "Operations";
        values["labels"] = "production, linux, production";

        var draft = ConnectionEditorDraftFactory.Build(StorageProviderKind.Ssh, values);

        Assert.Equal(ConnectionProfileType.Client, draft.Type);
        Assert.Equal(StorageConnectionProvider.Ssh, draft.Endpoint.Provider);
        Assert.Equal("Operations", draft.Metadata.FolderPath);
        Assert.Equal(["production", "linux"], Assert.IsType<string[]>(draft.Metadata.Tags));
    }

    [Fact]
    public void BuildsSshPrivateKeyAndPasswordMfaDraft()
    {
        var values = ValidValues(StorageProviderKind.Ssh);
        values["authenticationMode"] = "Private key + password (MFA)";
        values["privateKeyReference"] = "shs_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        values["privateKeyPassphraseReference"] = "shs_CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";

        var draft = ConnectionEditorDraftFactory.Build(StorageProviderKind.Ssh, values);

        Assert.True(draft.HasValidBounds);
        Assert.Equal(ConnectionAuthenticationKind.SshPrivateKeyPassword, draft.Authentication.Kind);
        Assert.NotNull(draft.Authentication.PasswordReference);
        Assert.NotNull(draft.Authentication.PrivateKeyReference);
        Assert.NotNull(draft.Authentication.PrivateKeyPassphraseReference);
    }

    /// <summary>
    /// An SFTP server that wants a key and then the account password takes both, as an SSH
    /// terminal's does, now that the storage library offers them together.
    /// </summary>
    [Fact]
    public void BuildsSftpPrivateKeyAndPasswordDraft()
    {
        var values = ValidValues(StorageProviderKind.Sftp);
        values["authenticationMode"] = "Private key + password (MFA)";
        values["privateKeyReference"] = "shs_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        values["passwordReference"] = "shs_DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD";

        var draft = ConnectionEditorDraftFactory.Build(StorageProviderKind.Sftp, values);

        Assert.True(draft.HasValidBounds);
        Assert.Equal(ConnectionAuthenticationKind.SshPrivateKeyPassword, draft.Authentication.Kind);
        Assert.Contains(
            "Private key + password (MFA)",
            ConnectionProviderCatalog.Get(StorageProviderKind.Sftp)
                .AuthenticationFields.Single(field => field.Key == "authenticationMode").Choices!);
    }

    /// <summary>
    /// A WebDAV address typed without a scheme is HTTPS; its sign-in scheme, pin, client
    /// certificate, folder and request timeout all come back when the connection is edited, and
    /// saving it again unchanged gives the same draft.
    /// </summary>
    [Fact]
    public void APinnedWebDavConnectionSurvivesAnEdit()
    {
        var values = ValidValues(StorageProviderKind.WebDav);
        values["url"] = " dav.example.test:8443/remote.php/dav/files/me/ ";
        values["initialPath"] = "Projects";
        values["authenticationMode"] = "Digest";
        values["trustMode"] = "System trust + certificate pin";
        values["certificatePin"] = new string('A', 64);
        values["clientCertificateReference"] = "shs_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        values["clientCertificatePasswordReference"] = "shs_CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";
        values[ConnectionEditorDraftFactory.ReadTimeoutKey] = "1800";

        var draft = ConnectionEditorDraftFactory.Build(StorageProviderKind.WebDav, values);
        var reopened = ConnectionEditorDraftFactory.ToEditorValues(
            new ConnectionProfileDocument(Guid.NewGuid(), 1, draft, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));

        Assert.True(draft.HasValidBounds);
        Assert.Equal("https://dav.example.test:8443/remote.php/dav/files/me/", draft.Endpoint.ServiceEndpoint);
        Assert.Equal("Projects", draft.Endpoint.RootPath);
        Assert.Equal(ConnectionHttpCredentialScheme.Digest, draft.Endpoint.HttpCredentialScheme);
        Assert.Equal(ConnectionTlsCertificatePolicy.Pinned, draft.Endpoint.TlsPolicy);
        Assert.False(draft.Endpoint.AllowInsecureTransport);
        Assert.Null(draft.Endpoint.Host);
        Assert.Equal(1800, draft.OperationalOptions.OperationTimeoutSeconds);
        Assert.Equal(ConnectionAuthenticationKind.UsernamePassword, draft.Authentication.Kind);

        Assert.Equal("https://dav.example.test:8443/remote.php/dav/files/me/", reopened["url"]);
        Assert.Equal("Digest", reopened["authenticationMode"]);
        Assert.Equal("System trust + certificate pin", reopened["trustMode"]);
        Assert.Equal("1800", reopened[ConnectionEditorDraftFactory.ReadTimeoutKey]);
        // The pin itself lives in the trust store, so the editor is handed it again as it would be.
        var resaved = new Dictionary<string, string>(reopened, StringComparer.Ordinal)
        {
            ["certificatePin"] = new string('A', 64)
        };
        Assert.Equal(draft, ConnectionEditorDraftFactory.Build(StorageProviderKind.WebDav, resaved) with
        {
            Metadata = draft.Metadata
        });
    }

    [Theory]
    [InlineData("Bearer token", ConnectionAuthenticationKind.BearerToken)]
    [InlineData("Anonymous", ConnectionAuthenticationKind.None)]
    [InlineData("NTLM", ConnectionAuthenticationKind.UsernamePassword)]
    public void EachWebDavSignInBuildsOnlyItsOwnFieldsAndReopensAsItself(
        string mode,
        ConnectionAuthenticationKind kind)
    {
        var values = ValidValues(StorageProviderKind.WebDav);
        values["authenticationMode"] = mode;
        values["bearerTokenReference"] = "shs_DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD";

        var draft = ConnectionEditorDraftFactory.Build(StorageProviderKind.WebDav, values);
        var reopened = ConnectionEditorDraftFactory.ToEditorValues(
            new ConnectionProfileDocument(Guid.NewGuid(), 1, draft, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));

        Assert.True(draft.HasValidBounds);
        Assert.Equal(kind, draft.Authentication.Kind);
        Assert.Equal(kind == ConnectionAuthenticationKind.BearerToken, draft.Authentication.SessionTokenReference is not null);
        Assert.Equal(kind == ConnectionAuthenticationKind.UsernamePassword, draft.Authentication.PasswordReference is not null);
        Assert.Equal(mode, reopened["authenticationMode"]);
    }

    [Fact]
    public void WebDavRefusesUnacknowledgedHttpAndProtectionsHttpCannotGive()
    {
        var values = ValidValues(StorageProviderKind.WebDav);
        values["url"] = "http://nas.local:8080/";

        var unacknowledged = Assert.Throws<ArgumentException>(() =>
            ConnectionEditorDraftFactory.Build(StorageProviderKind.WebDav, values));
        Assert.StartsWith(Ui.Validation.WebDavPlainHttpNeedsAcknowledgement, unacknowledged.Message, StringComparison.Ordinal);

        values["acknowledgePlaintext"] = "true";
        Assert.True(ConnectionEditorDraftFactory.Build(StorageProviderKind.WebDav, values).Endpoint.AllowInsecureTransport);

        values["trustMode"] = "System trust + certificate pin";
        values["certificatePin"] = new string('A', 64);
        var pinned = Assert.Throws<ArgumentException>(() =>
            ConnectionEditorDraftFactory.Build(StorageProviderKind.WebDav, values));
        Assert.StartsWith(Ui.Validation.WebDavTlsNeedsHttps, pinned.Message, StringComparison.Ordinal);

        foreach (var url in new[] { "ftp://nas.local/", "https://user:secret@nas.local/", "https://nas.local/?a=b", "" })
        {
            values["url"] = url;
            Assert.Throws<ArgumentException>(() => ConnectionEditorDraftFactory.Build(StorageProviderKind.WebDav, values));
        }
    }

    /// <summary>
    /// A shared key with no service URL gets the account's public one; each Azure sign-in carries
    /// only its own secret, reopens as itself, and saving it again unchanged gives the same draft.
    /// </summary>
    [Theory]
    [InlineData("Shared key", ConnectionAuthenticationKind.AzureSharedKey)]
    [InlineData("SAS token", ConnectionAuthenticationKind.AzureSasToken)]
    [InlineData("Connection string", ConnectionAuthenticationKind.AzureConnectionString)]
    [InlineData("Azure identity", ConnectionAuthenticationKind.AmbientCredential)]
    public void EachAzureSignInBuildsOnlyItsOwnFieldsAndSurvivesAnEdit(string mode, ConnectionAuthenticationKind kind)
    {
        var values = ValidValues(StorageProviderKind.AzureBlob);
        values["authenticationMode"] = mode;
        values["prefix"] = "/team/reports/";
        values["sasTokenReference"] = "shs_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        values["connectionStringReference"] = "shs_CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";
        if (mode is "SAS token" or "Azure identity")
        {
            values["endpoint"] = "account.blob.core.windows.net";
        }

        var draft = ConnectionEditorDraftFactory.Build(StorageProviderKind.AzureBlob, values);
        var reopened = ConnectionEditorDraftFactory.ToEditorValues(
            new ConnectionProfileDocument(Guid.NewGuid(), 1, draft, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));

        Assert.True(draft.HasValidBounds);
        Assert.Equal(kind, draft.Authentication.Kind);
        Assert.Equal("archive", draft.Endpoint.Bucket);
        Assert.Equal("team/reports", draft.Endpoint.RootPath);
        Assert.Equal(
            kind == ConnectionAuthenticationKind.AzureConnectionString ? null : "https://account.blob.core.windows.net/",
            draft.Endpoint.ServiceEndpoint);
        Assert.Equal(kind == ConnectionAuthenticationKind.AzureSharedKey ? "account" : null, draft.Authentication.Username);
        Assert.Equal(
            kind switch
            {
                ConnectionAuthenticationKind.AzureSharedKey => "shs_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                ConnectionAuthenticationKind.AzureConnectionString => "shs_CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC",
                _ => null
            },
            draft.Authentication.SecretKeyReference);
        Assert.Equal(
            kind == ConnectionAuthenticationKind.AzureSasToken ? "shs_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB" : null,
            draft.Authentication.SessionTokenReference);

        Assert.Equal(mode, reopened["authenticationMode"]);
        Assert.Equal("archive", reopened["container"]);
        Assert.False(reopened.ContainsKey("bucket"));
        Assert.Equal(draft, ConnectionEditorDraftFactory.Build(StorageProviderKind.AzureBlob, reopened) with
        {
            Metadata = draft.Metadata
        });
    }

    /// <summary>Plain HTTP comes only with a connection string, and a service URL is only ever HTTPS.</summary>
    [Fact]
    public void AzureAllowsPlainHttpOnlyForAConnectionStringAndChecksNamesBesideTheFields()
    {
        var values = ValidValues(StorageProviderKind.AzureBlob);
        values["acknowledgePlaintext"] = "true";
        var sharedKey = Assert.Throws<ArgumentException>(() =>
            ConnectionEditorDraftFactory.Build(StorageProviderKind.AzureBlob, values));
        Assert.StartsWith(Ui.Validation.AzurePlainHttpNeedsConnectionString, sharedKey.Message, StringComparison.Ordinal);

        values["authenticationMode"] = "Connection string";
        values["connectionStringReference"] = "shs_CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";
        values["endpoint"] = "http://127.0.0.1:10000/devstoreaccount1";
        var emulator = ConnectionEditorDraftFactory.Build(StorageProviderKind.AzureBlob, values);
        Assert.True(emulator.Endpoint.AllowInsecureTransport);
        Assert.Null(emulator.Endpoint.ServiceEndpoint);

        values = ValidValues(StorageProviderKind.AzureBlob);
        foreach (var (key, value, message) in new[]
        {
            ("endpoint", "http://account.blob.core.windows.net/", Ui.Validation.AzurePlainHttpNeedsConnectionString),
            ("endpoint", "https://account.blob.core.windows.net/?sv=1&sig=secret", Ui.Validation.TheAzureServiceUrlIsInvalid),
            ("container", "Archive", Ui.Validation.TheAzureContainerNameIsInvalid),
            ("container", "two--hyphens", Ui.Validation.TheAzureContainerNameIsInvalid),
            ("accountName", "Account_1", Ui.Validation.TheAzureAccountNameIsInvalid),
            ("accountKeyReference", "", Ui.Validation.AnAzureAccountKeyIsRequired)
        })
        {
            var attempt = new Dictionary<string, string>(values, StringComparer.Ordinal) { [key] = value };
            var error = Assert.Throws<ArgumentException>(() =>
                ConnectionEditorDraftFactory.Build(StorageProviderKind.AzureBlob, attempt));
            Assert.StartsWith(message, error.Message, StringComparison.Ordinal);
        }

        values["authenticationMode"] = "SAS token";
        values["sasTokenReference"] = "shs_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        var noUrl = Assert.Throws<ArgumentException>(() =>
            ConnectionEditorDraftFactory.Build(StorageProviderKind.AzureBlob, values));
        Assert.StartsWith(Ui.Validation.AnAzureServiceUrlIsRequired, noUrl.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Google's client keeps its own request timeout and retries, which nothing in StorageHub can
    /// change, so a Cloud Storage draft says what the client does rather than a value it would ignore.
    /// </summary>
    [Fact]
    public void CloudStorageDraftsSayWhatGooglesClientDoes()
    {
        var stored = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GoogleCloudStorage.connectTimeoutSeconds"] = "5"
        };

        var draft = ConnectionEditorDraftFactory.Build(
            StorageProviderKind.GoogleCloudStorage,
            ValidValues(StorageProviderKind.GoogleCloudStorage),
            ConnectionDefaultSettings.Get(StorageProviderKind.GoogleCloudStorage, stored));

        Assert.Equal(100, draft.OperationalOptions.ConnectTimeoutSeconds);
        Assert.Equal(100, draft.OperationalOptions.OperationTimeoutSeconds);
        Assert.Equal(0, draft.OperationalOptions.MaximumRetryAttempts);
        Assert.False(ConnectionDefaultSettings.HasConfigurableTimeout(StorageProviderKind.GoogleCloudStorage));
        Assert.True(ConnectionDefaultSettings.HasConfigurableTimeout(StorageProviderKind.AzureBlob));
    }

    /// <summary>
    /// Each Cloud Storage sign-in carries only its own field, reopens as itself, and saving it
    /// again unchanged gives the same draft; a service URL is kept, Google's own is no URL at all.
    /// </summary>
    [Theory]
    [InlineData("Service-account key", ConnectionAuthenticationKind.GoogleServiceAccount, "")]
    [InlineData("Application Default Credentials", ConnectionAuthenticationKind.AmbientCredential, "storage.example.test")]
    [InlineData("Anonymous", ConnectionAuthenticationKind.None, "http://127.0.0.1:4443")]
    public void EachCloudStorageSignInBuildsOnlyItsOwnFieldsAndSurvivesAnEdit(
        string mode,
        ConnectionAuthenticationKind kind,
        string serviceUrl)
    {
        var values = ValidValues(StorageProviderKind.GoogleCloudStorage);
        values["authenticationMode"] = mode;
        values["prefix"] = "/team/reports/";
        values["endpoint"] = serviceUrl;
        values["acknowledgePlaintext"] = serviceUrl.StartsWith("http://", StringComparison.Ordinal) ? "true" : "false";

        var draft = ConnectionEditorDraftFactory.Build(StorageProviderKind.GoogleCloudStorage, values);
        var reopened = ConnectionEditorDraftFactory.ToEditorValues(
            new ConnectionProfileDocument(Guid.NewGuid(), 1, draft, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));

        Assert.True(draft.HasValidBounds);
        Assert.Equal(kind, draft.Authentication.Kind);
        Assert.Equal("archive", draft.Endpoint.Bucket);
        Assert.Equal("team/reports", draft.Endpoint.RootPath);
        Assert.Equal(
            serviceUrl switch
            {
                "" => null,
                "storage.example.test" => "https://storage.example.test/",
                _ => "http://127.0.0.1:4443/"
            },
            draft.Endpoint.ServiceEndpoint);
        Assert.Equal(serviceUrl.StartsWith("http://", StringComparison.Ordinal), draft.Endpoint.AllowInsecureTransport);
        Assert.Equal(
            kind == ConnectionAuthenticationKind.GoogleServiceAccount ? "shs_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" : null,
            draft.Authentication.SecretKeyReference);

        Assert.Equal(mode, reopened["authenticationMode"]);
        Assert.False(reopened.ContainsKey("secretAccessKeyReference"));
        Assert.Equal(draft, ConnectionEditorDraftFactory.Build(StorageProviderKind.GoogleCloudStorage, reopened) with
        {
            Metadata = draft.Metadata
        });
    }

    /// <summary>A bucket is named as Google names one, and plain HTTP has to be allowed before it is used.</summary>
    [Fact]
    public void CloudStorageChecksTheBucketAndPlainHttpBesideTheFields()
    {
        var values = ValidValues(StorageProviderKind.GoogleCloudStorage);
        foreach (var (key, value, message) in new[]
        {
            ("endpoint", "http://127.0.0.1:4443", Ui.Validation.ServiceUrlPlainHttpNotAllowed),
            ("endpoint", "https://storage.example.test/?key=secret", Ui.Validation.TheCloudStorageServiceUrlIsInvalid),
            ("endpoint", "ftp://storage.example.test/", Ui.Validation.TheCloudStorageServiceUrlIsInvalid),
            ("bucket", "", Ui.Validation.ACloudStorageBucketIsRequired),
            ("bucket", "Archive", Ui.Validation.TheCloudStorageBucketNameIsInvalid),
            ("bucket", "goog-archive", Ui.Validation.TheCloudStorageBucketNameIsInvalid),
            ("serviceAccountKeyReference", "", Ui.Validation.AServiceAccountKeyIsRequired)
        })
        {
            var attempt = new Dictionary<string, string>(values, StringComparer.Ordinal) { [key] = value };
            var error = Assert.Throws<ArgumentException>(() =>
                ConnectionEditorDraftFactory.Build(StorageProviderKind.GoogleCloudStorage, attempt));
            Assert.StartsWith(message, error.Message, StringComparison.Ordinal);
        }

        // Allowing plain HTTP with nothing plain to apply it to changes nothing.
        values["acknowledgePlaintext"] = "true";
        Assert.False(ConnectionEditorDraftFactory.Build(StorageProviderKind.GoogleCloudStorage, values)
            .Endpoint.AllowInsecureTransport);
    }

    /// <summary>
    /// Each Swift sign-in carries only the fields it uses, reopens as itself, and saving it again
    /// unchanged gives the same draft: Keystone with its scope and region, TempAuth without either,
    /// and a token with the storage URL alone.
    /// </summary>
    [Theory]
    [InlineData("Keystone v3", ConnectionAuthenticationKind.KeystonePassword)]
    [InlineData("TempAuth", ConnectionAuthenticationKind.UsernamePassword)]
    [InlineData("Pre-issued token", ConnectionAuthenticationKind.BearerToken)]
    public void EachSwiftSignInBuildsOnlyItsOwnFieldsAndSurvivesAnEdit(string mode, ConnectionAuthenticationKind kind)
    {
        var values = ValidValues(StorageProviderKind.OpenStackSwift);
        values["authenticationMode"] = mode;
        values["storageUrl"] = "swift.example.test/v1/AUTH_project";
        values["region"] = "RegionOne";
        values["prefix"] = "/team/";
        values["userDomainName"] = "Users";
        values["bearerTokenReference"] = "shs_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        values[ConnectionEditorDraftFactory.ReadTimeoutKey] = "1200";

        var draft = ConnectionEditorDraftFactory.Build(StorageProviderKind.OpenStackSwift, values);
        var reopened = ConnectionEditorDraftFactory.ToEditorValues(
            new ConnectionProfileDocument(Guid.NewGuid(), 1, draft, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));

        Assert.True(draft.HasValidBounds);
        Assert.Equal(kind, draft.Authentication.Kind);
        Assert.Equal("archive", draft.Endpoint.Bucket);
        Assert.Equal("team", draft.Endpoint.RootPath);
        Assert.Equal("https://swift.example.test/v1/AUTH_project", draft.Endpoint.StorageUrl);
        Assert.Equal(1200, draft.OperationalOptions.OperationTimeoutSeconds);
        Assert.Equal(
            kind == ConnectionAuthenticationKind.BearerToken ? null : "https://keystone.example.test/v3",
            draft.Endpoint.ServiceEndpoint);
        Assert.Equal(kind == ConnectionAuthenticationKind.KeystonePassword ? "RegionOne" : null, draft.Endpoint.Region);
        Assert.Equal(
            kind == ConnectionAuthenticationKind.KeystonePassword
                ? new ConnectionKeystoneDocument("project", "Users", "Default")
                : null,
            draft.Authentication.Keystone);
        Assert.Equal(kind == ConnectionAuthenticationKind.BearerToken ? null : "operator", draft.Authentication.Username);
        Assert.Equal(
            kind == ConnectionAuthenticationKind.BearerToken ? "shs_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB" : null,
            draft.Authentication.SessionTokenReference);

        Assert.Equal(mode, reopened["authenticationMode"]);
        Assert.Equal("archive", reopened["container"]);
        Assert.False(reopened.ContainsKey("bucket"));
        Assert.Equal(draft, ConnectionEditorDraftFactory.Build(StorageProviderKind.OpenStackSwift, reopened) with
        {
            Metadata = draft.Metadata
        });
    }

    /// <summary>What Swift needs is said beside the field: the container, the URL each sign-in uses, and plain HTTP allowed.</summary>
    [Fact]
    public void SwiftChecksItsUrlsAndContainerBesideTheFields()
    {
        var values = ValidValues(StorageProviderKind.OpenStackSwift);
        foreach (var (key, value, message) in new[]
        {
            ("container", "", Ui.Validation.ASwiftContainerIsRequired),
            ("container", "a/b", Ui.Validation.TheSwiftContainerNameIsInvalid),
            ("authenticationUrl", "", Ui.Validation.AnAuthenticationUrlIsRequired),
            ("authenticationUrl", "http://127.0.0.1:8080/auth/v1.0", Ui.Validation.ServiceUrlPlainHttpNotAllowed),
            ("authenticationUrl", "https://keystone.example.test/v3?token=1", Ui.Validation.TheSwiftUrlIsInvalid),
            ("storageUrl", "http://swift.example.test/v1/AUTH_p", Ui.Validation.ServiceUrlPlainHttpNotAllowed),
            ("projectName", "", Ui.Validation.AKeystoneProjectIsRequired),
            ("username", "", Ui.Validation.AUsernameIsRequired)
        })
        {
            var attempt = new Dictionary<string, string>(values, StringComparer.Ordinal) { [key] = value };
            var error = Assert.Throws<ArgumentException>(() =>
                ConnectionEditorDraftFactory.Build(StorageProviderKind.OpenStackSwift, attempt));
            Assert.StartsWith(message, error.Message, StringComparison.Ordinal);
        }

        values["authenticationMode"] = "Pre-issued token";
        values["bearerTokenReference"] = "shs_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        var noStorage = Assert.Throws<ArgumentException>(() =>
            ConnectionEditorDraftFactory.Build(StorageProviderKind.OpenStackSwift, values));
        Assert.StartsWith(Ui.Validation.AStorageUrlIsRequiredForAToken, noStorage.Message, StringComparison.Ordinal);

        values["authenticationMode"] = "TempAuth";
        values["authenticationUrl"] = "http://127.0.0.1:8080/auth/v1.0";
        values["acknowledgePlaintext"] = "true";
        var saio = ConnectionEditorDraftFactory.Build(StorageProviderKind.OpenStackSwift, values);
        Assert.True(saio.Endpoint.AllowInsecureTransport);
    }

    /// <summary>A pinned FTPS connection reopened for editing keeps its pin, rather than reverting to system trust.</summary>
    [Fact]
    public void APinnedFtpsConnectionKeepsItsTrustModeWhenEdited()
    {
        var values = ValidValues(StorageProviderKind.Ftps);
        values["trustMode"] = "System trust + certificate pin";
        values["certificatePin"] = new string('A', 64);

        var draft = ConnectionEditorDraftFactory.Build(StorageProviderKind.Ftps, values);
        var reopened = ConnectionEditorDraftFactory.ToEditorValues(
            new ConnectionProfileDocument(Guid.NewGuid(), 1, draft, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));

        Assert.Equal(ConnectionTlsCertificatePolicy.Pinned, draft.Endpoint.TlsPolicy);
        Assert.Equal("System trust + certificate pin", reopened["trustMode"]);
    }

    [Fact]
    public void SftpAdvancedSettingsSurviveAnEdit()
    {
        var jump = Guid.NewGuid();
        var values = ValidValues(StorageProviderKind.Sftp);
        values[ConnectionEditorDraftFactory.JumpConnectionIdKey] = jump.ToString();
        values[ConnectionEditorDraftFactory.KeyExchangeAlgorithmsKey] = " curve25519-sha256 , diffie-hellman-group14-sha256 ";
        values[ConnectionEditorDraftFactory.CiphersKey] = "aes256-gcm@openssh.com";
        values[ConnectionEditorDraftFactory.MacAlgorithmsKey] = "hmac-sha2-256";
        values[ConnectionEditorDraftFactory.HostKeyAlgorithmsKey] = "ssh-ed25519";
        values[ConnectionEditorDraftFactory.MaximumSessionsKey] = "2";
        values[ConnectionEditorDraftFactory.KeepAliveKey] = "30";

        var draft = ConnectionEditorDraftFactory.Build(StorageProviderKind.Sftp, values);
        var reopened = ConnectionEditorDraftFactory.ToEditorValues(
            new ConnectionProfileDocument(Guid.NewGuid(), 1, draft, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));

        Assert.True(draft.HasValidBounds);
        var sftp = draft.Endpoint.Sftp!;
        Assert.Equal(jump, sftp.JumpConnectionId);
        Assert.Equal("curve25519-sha256,diffie-hellman-group14-sha256", sftp.KeyExchangeAlgorithms);
        Assert.Equal("aes256-gcm@openssh.com", sftp.Ciphers);
        Assert.Equal("hmac-sha2-256", sftp.MacAlgorithms);
        Assert.Equal("ssh-ed25519", sftp.HostKeyAlgorithms);
        Assert.Equal(2, sftp.MaximumSessions);
        Assert.Equal(30, sftp.KeepAliveSeconds);
        Assert.Equal(jump.ToString(), reopened[ConnectionEditorDraftFactory.JumpConnectionIdKey]);
        Assert.Equal("curve25519-sha256,diffie-hellman-group14-sha256", reopened[ConnectionEditorDraftFactory.KeyExchangeAlgorithmsKey]);
        Assert.Equal("2", reopened[ConnectionEditorDraftFactory.MaximumSessionsKey]);
        Assert.Equal("30", reopened[ConnectionEditorDraftFactory.KeepAliveKey]);
    }

    [Fact]
    public void SftpWithNoAdvancedSettingsCarriesNone()
    {
        var draft = ConnectionEditorDraftFactory.Build(StorageProviderKind.Sftp, ValidValues(StorageProviderKind.Sftp));

        Assert.Null(draft.Endpoint.Sftp);
    }

    [Theory]
    [InlineData(ConnectionEditorDraftFactory.KeyExchangeAlgorithmsKey, "curve25519-sha256; rm -rf")]
    [InlineData(ConnectionEditorDraftFactory.MaximumSessionsKey, "0")]
    [InlineData(ConnectionEditorDraftFactory.MaximumSessionsKey, "65")]
    [InlineData(ConnectionEditorDraftFactory.KeepAliveKey, "3601")]
    [InlineData(ConnectionEditorDraftFactory.JumpConnectionIdKey, "not-a-connection")]
    public void SftpAdvancedSettingsOutsideTheirBoundsAreRefused(string key, string value)
    {
        var values = ValidValues(StorageProviderKind.Sftp);
        values[key] = value;

        Assert.Throws<ArgumentException>(() => ConnectionEditorDraftFactory.Build(StorageProviderKind.Sftp, values));
    }

    [Fact]
    public void PlainFtpRequiresExplicitAcknowledgement()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["profileName"] = "Legacy FTP",
            ["host"] = "ftp.example.com",
            ["port"] = "21",
            ["username"] = "backup",
            ["passwordReference"] = "shs_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            ["acknowledgePlaintext"] = "false"
        };

        var error = Assert.Throws<ArgumentException>(() =>
            ConnectionEditorDraftFactory.Build(StorageProviderKind.Ftp, values));

        Assert.Contains("acknowledgement", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void APlainHttpS3EndpointIsAcceptedOnlyOnceAllowedOnTheTrustTab()
    {
        var values = ValidValues(StorageProviderKind.S3);
        values["endpoint"] = "http://127.0.0.1:9000";

        var error = Assert.Throws<ArgumentException>(() => ConnectionEditorDraftFactory.Build(StorageProviderKind.S3, values));
        Assert.StartsWith(Ui.Validation.TheS3ServiceEndpointMustUseHTTPS, error.Message, StringComparison.Ordinal);

        values["acknowledgePlaintext"] = "true";
        var endpoint = ConnectionEditorDraftFactory.Build(StorageProviderKind.S3, values).Endpoint;
        Assert.Equal("http://127.0.0.1:9000", endpoint.ServiceEndpoint);
        Assert.True(endpoint.AllowInsecureTransport);

        // The allowance means nothing for an HTTPS endpoint, so none is stored for one.
        values["endpoint"] = "https://s3.amazonaws.com";
        Assert.False(ConnectionEditorDraftFactory.Build(StorageProviderKind.S3, values).Endpoint.AllowInsecureTransport);
    }

    [Theory]
    [InlineData("")]
    [InlineData("MD5:unsafe")]
    [InlineData("AAAAAAAA AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void PinnedSftpRequiresStrictVerifiedSha256Fingerprint(string fingerprint)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["profileName"] = "SFTP",
            ["host"] = "sftp.example.test",
            ["port"] = "22",
            ["username"] = "operator",
            ["authenticationMode"] = "Password reference",
            ["passwordReference"] = "shs_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            ["hostKeyFingerprint"] = fingerprint
        };

        Assert.Throws<ArgumentException>(() =>
            ConnectionEditorDraftFactory.Build(StorageProviderKind.Sftp, values));
    }

    [Fact]
    public void PinnedFtpsRequiresCertificateFingerprintBeforeProfileCanBeSaved()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["profileName"] = "FTPS",
            ["host"] = "ftps.example.test",
            ["port"] = "21",
            ["username"] = "operator",
            ["passwordReference"] = "shs_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            ["trustMode"] = "System trust + certificate pin"
        };

        Assert.Throws<ArgumentException>(() =>
            ConnectionEditorDraftFactory.Build(StorageProviderKind.Ftps, values));
    }

    private static Dictionary<string, string> ValidValues(StorageProviderKind provider) => provider switch
    {
        StorageProviderKind.S3 => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["profileName"] = "S3",
            ["endpoint"] = "https://s3.amazonaws.com",
            ["bucket"] = "archive",
            ["region"] = "eu-north-1",
            ["authenticationMode"] = "Default credential chain"
        },
        StorageProviderKind.Ftp => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["profileName"] = "FTP",
            ["host"] = "ftp.example.test",
            ["port"] = "21",
            ["username"] = "operator",
            ["passwordReference"] = "shs_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            ["acknowledgePlaintext"] = "true"
        },
        StorageProviderKind.Ftps => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["profileName"] = "FTPS",
            ["host"] = "ftps.example.test",
            ["port"] = "21",
            ["username"] = "operator",
            ["passwordReference"] = "shs_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            ["trustMode"] = "System trust"
        },
        StorageProviderKind.Sftp => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["profileName"] = "SFTP",
            ["host"] = "sftp.example.test",
            ["port"] = "22",
            ["username"] = "operator",
            ["authenticationMode"] = "Password reference",
            ["passwordReference"] = "shs_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            ["hostKeyFingerprint"] = new string('A', 64)
        },
        StorageProviderKind.Ssh => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["profileName"] = "SSH shell",
            ["host"] = "ssh.example.test",
            ["port"] = "22",
            ["username"] = "operator",
            ["authenticationMode"] = "Password reference",
            ["passwordReference"] = "shs_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            ["hostKeyFingerprint"] = new string('A', 64)
        },
        StorageProviderKind.WebDav => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["profileName"] = "WebDAV",
            ["url"] = "https://dav.example.test/",
            ["username"] = "operator",
            ["passwordReference"] = "shs_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            ["trustMode"] = "System trust + hostname"
        },
        StorageProviderKind.AzureBlob => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["profileName"] = "Azure",
            ["container"] = "archive",
            ["authenticationMode"] = "Shared key",
            ["accountName"] = "account",
            ["accountKeyReference"] = "shs_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
        },
        StorageProviderKind.GoogleCloudStorage => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["profileName"] = "Cloud Storage",
            ["bucket"] = "archive",
            ["authenticationMode"] = "Service-account key",
            ["serviceAccountKeyReference"] = "shs_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
        },
        StorageProviderKind.OpenStackSwift => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["profileName"] = "Swift",
            ["container"] = "archive",
            ["authenticationUrl"] = "https://keystone.example.test/v3",
            ["authenticationMode"] = "Keystone v3",
            ["username"] = "operator",
            ["passwordReference"] = "shs_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            ["projectName"] = "project"
        },
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };
}
