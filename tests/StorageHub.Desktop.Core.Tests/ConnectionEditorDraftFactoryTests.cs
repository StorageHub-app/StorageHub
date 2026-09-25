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
        Assert.DoesNotContain(
            "Private key + password (MFA)",
            ConnectionProviderCatalog.Get(StorageProviderKind.Sftp)
                .AuthenticationFields.Single(field => field.Key == "authenticationMode").Choices!);
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
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };
}
