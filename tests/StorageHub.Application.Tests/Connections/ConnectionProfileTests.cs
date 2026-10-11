using StorageHub.Application.Connections;
using StorageHub.Domain.Identifiers;
using StorageHub.Security;
using StorageHub.Testing;

namespace StorageHub.Application.Tests.Connections;

public sealed class ConnectionProfileTests
{
    private static readonly SecretReference Password = SecretReference.Create();

    [Fact]
    public void Metadata_normalizes_tags_and_keeps_an_immutable_snapshot()
    {
        var tags = new[] { " Production ", "backup", "PRODUCTION" };

        var metadata = new ConnectionProfileMetadata(
            "Archive",
            folderPath: "Cloud/Production",
            tags: tags,
            isFavorite: true,
            defaultPaths: new ConnectionDefaultPaths("/", "/incoming", "/downloads"),
            iconKey: "cloud-lock",
            accentColor: "#3366CC",
            notes: "Off-site archive");
        tags[0] = "mutated";

        Assert.Collection(
            metadata.Tags,
            tag => Assert.Equal("backup", tag),
            tag => Assert.Equal("Production", tag));
        Assert.Equal("Cloud/Production", metadata.FolderPath);
        Assert.Equal("#3366CC", metadata.AccentColor);
    }

    [Theory]
    [MemberData(nameof(ValidProfiles))]
    public void Supported_provider_profiles_are_valid(ConnectionProfile profile)
    {
        Assert.False(profile.Id.IsEmpty);
        Assert.Equal(profile.Provider, profile.Endpoint.Provider);
        Assert.Equal(1, profile.Version);
    }

    [Fact]
    public void Plain_ftp_requires_an_explicit_insecure_transport_acknowledgement()
    {
        var error = Assert.Throws<ArgumentException>(() => new FtpEndpoint(
            "ftp.example.test", 21, allowInsecurePlainText: false));

        Assert.Contains("insecure", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ftp_server_options_keep_active_settings_to_active_mode_and_know_their_time_zone()
    {
        var active = new FtpServerOptions(
            " UTC ", FtpListingFormat.Unix, FtpDataConnectionMode.Active, 50_000, 50_100);
        Assert.Equal("UTC", active.ServerTimeZone);
        Assert.Equal(FtpServerOptions.Default, new FtpEndpoint("ftp.example.test", 21, true).ServerOptions);

        Assert.Throws<ArgumentException>(() => new FtpServerOptions("Mars/Olympus_Mons"));
        Assert.Throws<ArgumentException>(() => new FtpServerOptions(activePortMinimum: 50_000, activePortMaximum: 50_100));
        Assert.Throws<ArgumentException>(() => new FtpServerOptions(
            dataConnectionMode: FtpDataConnectionMode.Active, activePortMinimum: 50_000));
        Assert.Throws<ArgumentException>(() => new FtpServerOptions(
            dataConnectionMode: FtpDataConnectionMode.Active, activePortMinimum: 80, activePortMaximum: 90));
        Assert.Throws<ArgumentException>(() => new FtpServerOptions(
            dataConnectionMode: FtpDataConnectionMode.Active, activePortMinimum: 60_000, activePortMaximum: 50_000));
    }

    [Fact]
    public void Sftp_server_options_normalize_algorithm_lists_and_bound_their_limits()
    {
        var options = new SftpServerOptions(
            keyExchangeAlgorithms: " curve25519-sha256 ,, curve25519-sha256, diffie-hellman-group14-sha256 ",
            ciphers: "  ",
            maximumSessions: 4,
            keepAliveSeconds: 60);
        Assert.Equal("curve25519-sha256,diffie-hellman-group14-sha256", options.KeyExchangeAlgorithms);
        Assert.Null(options.Ciphers);
        Assert.Equal(["curve25519-sha256", "diffie-hellman-group14-sha256"], SftpServerOptions.Split(options.KeyExchangeAlgorithms));
        Assert.Equal(SftpServerOptions.Default, new SftpEndpoint("sftp.example.test", 22, SshHostKeyPolicy.Pinned).ServerOptions);
        Assert.Equal(SftpServerOptions.Default, new SftpServerOptions(ciphers: " , "));

        Assert.Throws<ArgumentException>(() => new SftpServerOptions(ciphers: "aes256-ctr;ls"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SftpServerOptions(maximumSessions: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SftpServerOptions(maximumSessions: 65));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SftpServerOptions(keepAliveSeconds: 3_601));
    }

    [Fact]
    public void An_sftp_connection_takes_a_key_and_password_but_cannot_be_its_own_jump_host()
    {
        var id = ConnectionProfileId.New();
        var now = DateTimeOffset.UtcNow;
        var keyAndPassword = new SshPrivateKeyPasswordAuthentication(
            "operator", SecretReference.Create(), SecretReference.Create(), null, SftpPrivateKeyFormat.OpenSsh);
        var options = new ConnectionOperationalOptions(
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30),
            new ConnectionRetryPolicy(1, TimeSpan.Zero, TimeSpan.Zero),
            null,
            new ConnectionBandwidthLimits(null, null),
            "utf-8");

        var profile = ConnectionProfile.Create(
            id,
            new ConnectionProfileMetadata("Inner"),
            new SftpEndpoint("sftp.example.test", 22, SshHostKeyPolicy.Pinned),
            keyAndPassword,
            options,
            now);
        Assert.IsType<SshPrivateKeyPasswordAuthentication>(profile.Authentication);

        Assert.Throws<ArgumentException>(() => ConnectionProfile.Create(
            id,
            new ConnectionProfileMetadata("Loop"),
            new SftpEndpoint(
                "sftp.example.test",
                22,
                SshHostKeyPolicy.Pinned,
                serverOptions: new SftpServerOptions(jumpConnectionId: id)),
            keyAndPassword,
            options,
            now));
    }

    [Fact]
    public void A_proxy_signs_in_with_a_user_name_and_a_vault_password_but_socks4_has_none()
    {
        var password = SecretReference.Create();

        var socks5 = new ConnectionProxy(new Uri("socks5://proxy.example.test:1080"), " relay ", password);
        Assert.Equal("relay", socks5.Username);
        Assert.Equal(password, socks5.PasswordReference);

        Assert.Throws<ArgumentException>(() => new ConnectionProxy(
            new Uri("socks5://proxy.example.test:1080"), username: null, password));
        Assert.Throws<ArgumentException>(() => new ConnectionProxy(
            new Uri("socks4://proxy.example.test:1080"), "relay", password));
        Assert.Throws<ArgumentException>(() => new ConnectionProxy(new Uri("socks5://proxy.example.test")));
        Assert.Throws<ArgumentException>(() => new ConnectionProxy(new Uri("ftp://proxy.example.test:21")));
    }

    [Fact]
    public void Endpoints_reject_embedded_credentials_and_insecure_s3_without_acknowledgement()
    {
        Assert.Throws<ArgumentException>(() => new ConnectionProxy(
            new Uri("https://user:password@proxy.example.test:8443")));
        Assert.Throws<ArgumentException>(() => new S3Endpoint(
            "archive",
            "local",
            new Uri("https://access:secret@s3.example.test")));
        Assert.Throws<ArgumentException>(() => new S3Endpoint(
            "archive",
            "local",
            new Uri("http://minio.example.test:9000")));

        var acknowledged = new S3Endpoint(
            "archive",
            "local",
            new Uri("http://minio.example.test:9000"),
            forcePathStyle: true,
            allowInsecureHttp: true);

        Assert.True(acknowledged.AllowInsecureHttp);
    }

    [Fact]
    public void Ftps_requires_a_tls_policy_and_accepts_only_opaque_pfx_references()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FtpsEndpoint(
            "ftps.example.test",
            21,
            FtpsTlsMode.Explicit,
            TlsCertificatePolicy.Unspecified));

        var pfx = SecretReference.Create();
        var endpoint = new FtpsEndpoint(
            "ftps.example.test",
            21,
            FtpsTlsMode.Explicit,
            TlsCertificatePolicy.SystemTrust,
            pfx,
            SecretReference.Create());

        Assert.Equal(pfx, endpoint.ClientCertificatePfxReference);

        // A PKCS#12 bundle may carry no password, so a PFX without one is valid.
        var passwordLess = new FtpsEndpoint(
            "ftps.example.test",
            21,
            FtpsTlsMode.Explicit,
            TlsCertificatePolicy.SystemTrust,
            pfx);
        Assert.Equal(pfx, passwordLess.ClientCertificatePfxReference);
        Assert.Null(passwordLess.ClientCertificatePasswordReference);

        // The reverse remains nonsense: a password with no material to unlock.
        Assert.Throws<ArgumentException>(() => new FtpsEndpoint(
            "ftps.example.test",
            21,
            FtpsTlsMode.Explicit,
            TlsCertificatePolicy.SystemTrust,
            clientCertificatePasswordReference: SecretReference.Create()));
    }

    [Fact]
    public void Sftp_requires_a_host_key_policy_and_rejects_unknown_private_key_formats()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SftpEndpoint(
            "sftp.example.test", 22, SshHostKeyPolicy.Unspecified));

        Assert.Throws<ArgumentOutOfRangeException>(() => new SftpPrivateKeyAuthentication(
            "operator",
            SecretReference.Create(),
            passphraseReference: null,
            (SftpPrivateKeyFormat)999));
        // A key without a passphrase is accepted since 2.0, where 1.4 refused it.
        Assert.Null(new SftpPrivateKeyAuthentication(
            "operator",
            SecretReference.Create(),
            passphraseReference: null,
            SftpPrivateKeyFormat.OpenSsh).PassphraseReference);
    }

    [Fact]
    public void Ssh_is_a_client_profile_and_uses_managed_reference_authentication()
    {
        var profile = ConnectionProfile.Create(
            ConnectionProfileId.New(),
            Metadata("SSH shell"),
            new SshClientEndpoint("ssh.example.test", 22, SshHostKeyPolicy.Pinned),
            new UsernamePasswordAuthentication("operator", Password),
            OperationalOptions(),
            DateTimeOffset.UtcNow);

        Assert.Equal(ConnectionProfileType.Client, profile.Type);
        Assert.Equal(ConnectionProviderKind.Ssh, profile.Provider);
    }

    [Fact]
    public void Ssh_and_sftp_support_private_key_and_account_password_mfa()
    {
        var authentication = new SshPrivateKeyPasswordAuthentication(
            "operator",
            Password,
            SecretReference.Create(),
            SecretReference.Create(),
            SftpPrivateKeyFormat.OpenSsh);
        var profile = ConnectionProfile.Create(
            ConnectionProfileId.New(),
            Metadata("MFA shell"),
            new SshClientEndpoint("ssh.example.test", 22, SshHostKeyPolicy.Pinned),
            authentication,
            OperationalOptions(),
            DateTimeOffset.UtcNow);

        Assert.Same(authentication, profile.Authentication);
        var sftp = ConnectionProfile.Create(
            ConnectionProfileId.New(),
            Metadata("SFTP MFA"),
            new SftpEndpoint("sftp.example.test", 22, SshHostKeyPolicy.Pinned),
            authentication,
            OperationalOptions(),
            DateTimeOffset.UtcNow);
        Assert.Same(authentication, sftp.Authentication);
    }

    [Fact]
    public void Authentication_contracts_expose_references_not_secret_strings()
    {
        var passwordProperty = typeof(UsernamePasswordAuthentication)
            .GetProperty(nameof(UsernamePasswordAuthentication.PasswordReference));
        var keyProperty = typeof(SftpPrivateKeyAuthentication)
            .GetProperty(nameof(SftpPrivateKeyAuthentication.PrivateKeyReference));
        var s3SecretProperty = typeof(S3AccessKeyAuthentication)
            .GetProperty(nameof(S3AccessKeyAuthentication.SecretKeyReference));
        var mfaPasswordProperty = typeof(SshPrivateKeyPasswordAuthentication)
            .GetProperty(nameof(SshPrivateKeyPasswordAuthentication.PasswordReference));

        Assert.Equal(typeof(SecretReference), passwordProperty!.PropertyType);
        Assert.Equal(typeof(SecretReference), keyProperty!.PropertyType);
        Assert.Equal(typeof(SecretReference), s3SecretProperty!.PropertyType);
        Assert.Equal(typeof(SecretReference), mfaPasswordProperty!.PropertyType);
        Assert.DoesNotContain(
            typeof(UsernamePasswordAuthentication).GetProperties(),
            property => property.PropertyType == typeof(string) &&
                property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void S3_default_credential_chain_requires_an_explicit_authentication_choice()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() => ConnectionProfile.Create(
            ConnectionProfileId.New(),
            Metadata("S3"),
            new S3Endpoint("archive", "eu-north-1"),
            new NoAuthentication(),
            OperationalOptions(),
            now));

        var profile = ConnectionProfile.Create(
            ConnectionProfileId.New(),
            Metadata("S3"),
            new S3Endpoint("archive", "eu-north-1"),
            new S3DefaultCredentialChainAuthentication(),
            OperationalOptions(),
            now);
        Assert.IsType<S3DefaultCredentialChainAuthentication>(profile.Authentication);
    }

    [Fact]
    public void WebDav_keeps_its_url_path_and_pins_by_the_urls_host_and_port()
    {
        var endpoint = new WebDavEndpoint(
            new Uri("https://DAV.Example.test/remote.php/dav/files/me/"),
            TlsCertificatePolicy.Pinned,
            credentialScheme: HttpCredentialScheme.Digest,
            clientCertificatePfxReference: SecretReference.Create(),
            clientCertificatePasswordReference: SecretReference.Create(),
            rootPath: "Projects");

        Assert.Equal("dav.example.test", endpoint.Host);
        Assert.Equal(443, endpoint.Port);
        Assert.Equal("/remote.php/dav/files/me/", endpoint.ServerUrl.AbsolutePath);
        Assert.False(endpoint.AllowInsecureHttp);
        Assert.Equal("Projects", endpoint.RootPath);
        Assert.Equal(8080, new WebDavEndpoint(
            new Uri("http://nas.local:8080/"), TlsCertificatePolicy.SystemTrust, allowInsecureHttp: true).Port);
    }

    [Fact]
    public void WebDav_refuses_unacknowledged_http_and_protections_that_http_cannot_give()
    {
        var http = new Uri("http://nas.local/dav/");
        Assert.ThrowsAny<ArgumentException>(() => new WebDavEndpoint(http, TlsCertificatePolicy.SystemTrust));
        Assert.ThrowsAny<ArgumentException>(() => new WebDavEndpoint(
            http, TlsCertificatePolicy.Pinned, allowInsecureHttp: true));
        Assert.ThrowsAny<ArgumentException>(() => new WebDavEndpoint(
            http, TlsCertificatePolicy.SystemTrust, allowInsecureHttp: true,
            clientCertificatePfxReference: SecretReference.Create()));
        Assert.ThrowsAny<ArgumentException>(() => new WebDavEndpoint(
            new Uri("ftp://nas.local/"), TlsCertificatePolicy.SystemTrust));
        Assert.ThrowsAny<ArgumentException>(() => new WebDavEndpoint(
            new Uri("https://user:secret@nas.local/"), TlsCertificatePolicy.SystemTrust));
        Assert.ThrowsAny<ArgumentException>(() => new WebDavEndpoint(
            new Uri("https://nas.local/?token=secret"), TlsCertificatePolicy.SystemTrust));
        Assert.ThrowsAny<ArgumentException>(() => new WebDavEndpoint(
            new Uri("https://nas.local/"), TlsCertificatePolicy.Unspecified));
        Assert.ThrowsAny<ArgumentException>(() => new WebDavEndpoint(
            new Uri("https://nas.local/"), TlsCertificatePolicy.SystemTrust,
            clientCertificatePasswordReference: SecretReference.Create()));
        Assert.ThrowsAny<ArgumentException>(() => new WebDavEndpoint(
            new Uri("https://nas.local/" + new string('a', 2_100)), TlsCertificatePolicy.SystemTrust));
    }

    [Fact]
    public void WebDav_signs_in_anonymously_or_with_a_token_only_over_basic_and_otherwise_with_a_password()
    {
        var basic = new WebDavEndpoint(new Uri("https://dav.example.test/"), TlsCertificatePolicy.SystemTrust);
        var negotiate = new WebDavEndpoint(
            new Uri("https://dav.example.test/"), TlsCertificatePolicy.SystemTrust,
            credentialScheme: HttpCredentialScheme.Negotiate);
        var token = new BearerTokenAuthentication(SecretReference.Create());

        Assert.Equal(ConnectionProviderKind.WebDav, Create(basic, new NoAuthentication()).Provider);
        Assert.IsType<BearerTokenAuthentication>(Create(basic, token).Authentication);
        Assert.IsType<UsernamePasswordAuthentication>(
            Create(negotiate, new UsernamePasswordAuthentication("operator", Password)).Authentication);
        Assert.Throws<ArgumentException>(() => Create(negotiate, new NoAuthentication()));
        Assert.Throws<ArgumentException>(() => Create(negotiate, token));
        Assert.Throws<ArgumentException>(() => Create(basic, new S3DefaultCredentialChainAuthentication()));
        Assert.ThrowsAny<ArgumentException>(() => new BearerTokenAuthentication(default));

        static ConnectionProfile Create(WebDavEndpoint endpoint, ConnectionAuthentication authentication) =>
            ConnectionProfile.Create(ConnectionProfileId.New(), Metadata("WebDAV"), endpoint, authentication,
                OperationalOptions(), DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Azure_names_its_container_as_azure_does_and_sends_secrets_only_to_https()
    {
        var endpoint = new AzureBlobEndpoint(
            " archive-2026 ",
            new Uri("https://account.blob.core.windows.net/"),
            rootPrefix: "/team/reports/");

        Assert.Equal(ConnectionProviderKind.AzureBlob, endpoint.Provider);
        Assert.Equal("archive-2026", endpoint.Container);
        Assert.Equal("team/reports", endpoint.RootPrefix);
        Assert.False(endpoint.AllowInsecureHttp);
        Assert.True(new AzureBlobEndpoint("emulated", allowInsecureHttp: true).AllowInsecureHttp);

        foreach (var name in new[] { "ab", "Archive", "two--hyphens", "-edge", "edge-", "under_score", new string('a', 64) })
        {
            Assert.ThrowsAny<ArgumentException>(() => new AzureBlobEndpoint(name));
        }

        // A key or a token is only ever sent to HTTPS, so a plain-HTTP service URL is refused, and
        // plain HTTP can only be acknowledged for a connection string, which has no service URL.
        Assert.ThrowsAny<ArgumentException>(() => new AzureBlobEndpoint(
            "archive", new Uri("http://127.0.0.1:10000/devstoreaccount1")));
        Assert.ThrowsAny<ArgumentException>(() => new AzureBlobEndpoint(
            "archive", new Uri("https://account.blob.core.windows.net/"), allowInsecureHttp: true));
        Assert.ThrowsAny<ArgumentException>(() => new AzureBlobEndpoint(
            "archive", new Uri("https://account.blob.core.windows.net/?sv=2024&sig=secret")));
        Assert.ThrowsAny<ArgumentException>(() => new AzureBlobEndpoint(
            "archive", rootPrefix: "team/../other"));
    }

    [Fact]
    public void Azure_takes_a_connection_string_without_a_service_url_and_every_other_sign_in_with_one()
    {
        var service = new AzureBlobEndpoint("archive", new Uri("https://account.blob.core.windows.net/"));
        var emulator = new AzureBlobEndpoint("archive", allowInsecureHttp: true);
        var connectionString = new AzureConnectionStringAuthentication(SecretReference.Create());

        Assert.IsType<AzureConnectionStringAuthentication>(Create(emulator, connectionString).Authentication);
        Assert.IsType<AzureSharedKeyAuthentication>(
            Create(service, new AzureSharedKeyAuthentication("account", SecretReference.Create())).Authentication);
        Assert.IsType<AzureSasTokenAuthentication>(
            Create(service, new AzureSasTokenAuthentication(SecretReference.Create())).Authentication);
        Assert.IsType<AmbientCredentialAuthentication>(
            Create(service, new AmbientCredentialAuthentication()).Authentication);

        Assert.Throws<ArgumentException>(() => Create(service, connectionString));
        Assert.Throws<ArgumentException>(() => Create(emulator, new AmbientCredentialAuthentication()));
        Assert.Throws<ArgumentException>(() => Create(service, new NoAuthentication()));
        Assert.Throws<ArgumentException>(() => Create(service, new S3DefaultCredentialChainAuthentication()));
        Assert.Throws<ArgumentException>(() => Create(service, new UsernamePasswordAuthentication("account", Password)));
        Assert.ThrowsAny<ArgumentException>(() => new AzureSharedKeyAuthentication("Account", SecretReference.Create()));
        Assert.ThrowsAny<ArgumentException>(() => new AzureSharedKeyAuthentication("ab", SecretReference.Create()));
        Assert.ThrowsAny<ArgumentException>(() => new AzureSasTokenAuthentication(default));
        Assert.ThrowsAny<ArgumentException>(() => new AzureConnectionStringAuthentication(default));

        static ConnectionProfile Create(AzureBlobEndpoint endpoint, ConnectionAuthentication authentication) =>
            ConnectionProfile.Create(ConnectionProfileId.New(), Metadata("Azure"), endpoint, authentication,
                OperationalOptions(), DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Cloud_storage_names_its_bucket_as_google_does_and_takes_plain_http_only_when_acknowledged()
    {
        var google = new GoogleCloudStorageEndpoint(" archive_2026 ", rootPrefix: "/team/reports/");
        Assert.Equal(ConnectionProviderKind.GoogleCloudStorage, google.Provider);
        Assert.Equal("archive_2026", google.Bucket);
        Assert.Null(google.ServiceUri);
        Assert.Equal("team/reports", google.RootPrefix);
        Assert.False(google.AllowInsecureHttp);

        var emulator = new GoogleCloudStorageEndpoint(
            "archive", new Uri("http://127.0.0.1:4443/"), allowInsecureHttp: true);
        Assert.True(emulator.AllowInsecureHttp);
        // An acknowledgement with nothing plain to apply to is not kept.
        Assert.False(new GoogleCloudStorageEndpoint("archive", allowInsecureHttp: true).AllowInsecureHttp);
        Assert.Equal(
            "www.example.com",
            new GoogleCloudStorageEndpoint("www.example.com").Bucket);

        foreach (var name in new[]
        {
            "ab", "Archive", "-edge", "edge-", "goog-bucket", "has space", new string('a', 64),
            "a..b", string.Join('.', Enumerable.Repeat(new string('a', 63), 4))
        })
        {
            Assert.ThrowsAny<ArgumentException>(() => new GoogleCloudStorageEndpoint(name));
        }

        Assert.ThrowsAny<ArgumentException>(() => new GoogleCloudStorageEndpoint(
            "archive", new Uri("http://127.0.0.1:4443/")));
        Assert.ThrowsAny<ArgumentException>(() => new GoogleCloudStorageEndpoint(
            "archive", new Uri("https://user:secret@storage.example.test/")));
        Assert.ThrowsAny<ArgumentException>(() => new GoogleCloudStorageEndpoint(
            "archive", new Uri("ftp://storage.example.test/")));
        Assert.ThrowsAny<ArgumentException>(() => new GoogleCloudStorageEndpoint(
            "archive", rootPrefix: "team/../other"));
    }

    [Fact]
    public void Cloud_storage_signs_in_with_a_key_the_agents_identity_or_not_at_all()
    {
        var endpoint = new GoogleCloudStorageEndpoint("archive");

        Assert.IsType<GoogleServiceAccountAuthentication>(
            Create(new GoogleServiceAccountAuthentication(SecretReference.Create())).Authentication);
        Assert.IsType<AmbientCredentialAuthentication>(Create(new AmbientCredentialAuthentication()).Authentication);
        Assert.IsType<NoAuthentication>(Create(new NoAuthentication()).Authentication);

        Assert.Throws<ArgumentException>(() => Create(new S3DefaultCredentialChainAuthentication()));
        Assert.Throws<ArgumentException>(() => Create(new UsernamePasswordAuthentication("account", Password)));
        Assert.Throws<ArgumentException>(() => Create(new AzureSasTokenAuthentication(SecretReference.Create())));
        Assert.ThrowsAny<ArgumentException>(() => new GoogleServiceAccountAuthentication(default));

        ConnectionProfile Create(ConnectionAuthentication authentication) =>
            ConnectionProfile.Create(ConnectionProfileId.New(), Metadata("Cloud Storage"), endpoint, authentication,
                OperationalOptions(), DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Provider_and_endpoint_must_match()
    {
        Assert.Throws<ArgumentException>(() => ConnectionProfile.Create(
            ConnectionProfileId.New(),
            Metadata("Mismatch"),
            new LocalEndpoint(TestPaths.LocalRoot),
            new NoAuthentication(),
            OperationalOptions(),
            DateTimeOffset.UtcNow,
            provider: ConnectionProviderKind.S3));
    }

    public static TheoryData<ConnectionProfile> ValidProfiles()
    {
        var now = DateTimeOffset.UtcNow;
        var data = new TheoryData<ConnectionProfile>
        {
            ConnectionProfile.Create(ConnectionProfileId.New(), Metadata("Local"),
                new LocalEndpoint(TestPaths.LocalRoot), new NoAuthentication(), OperationalOptions(), now),
            ConnectionProfile.Create(ConnectionProfileId.New(), Metadata("S3"),
                new S3Endpoint("archive", "eu-north-1"),
                new S3AccessKeyAuthentication(
                    SecretReference.Create(),
                    SecretReference.Create(),
                    SecretReference.Create()), OperationalOptions(), now),
            ConnectionProfile.Create(ConnectionProfileId.New(), Metadata("FTP"),
                new FtpEndpoint("ftp.example.test", 21, allowInsecurePlainText: true),
                new UsernamePasswordAuthentication("operator", Password), OperationalOptions(), now),
            ConnectionProfile.Create(ConnectionProfileId.New(), Metadata("FTPS"),
                new FtpsEndpoint("ftps.example.test", 21, FtpsTlsMode.Explicit, TlsCertificatePolicy.SystemTrust),
                new UsernamePasswordAuthentication("operator", Password), OperationalOptions(), now),
            ConnectionProfile.Create(ConnectionProfileId.New(), Metadata("SFTP"),
                new SftpEndpoint("sftp.example.test", 22, SshHostKeyPolicy.Pinned),
                new SftpPrivateKeyAuthentication("operator", SecretReference.Create(), SecretReference.Create(),
                    SftpPrivateKeyFormat.OpenSsh), OperationalOptions(), now),
            ConnectionProfile.Create(ConnectionProfileId.New(), Metadata("WebDAV"),
                new WebDavEndpoint(new Uri("https://dav.example.test/files/"), TlsCertificatePolicy.SystemTrust),
                new UsernamePasswordAuthentication("operator", Password), OperationalOptions(), now),
            ConnectionProfile.Create(ConnectionProfileId.New(), Metadata("Azure"),
                new AzureBlobEndpoint("archive", new Uri("https://account.blob.core.windows.net/")),
                new AzureSharedKeyAuthentication("account", SecretReference.Create()), OperationalOptions(), now),
            ConnectionProfile.Create(ConnectionProfileId.New(), Metadata("Cloud Storage"),
                new GoogleCloudStorageEndpoint("archive"),
                new GoogleServiceAccountAuthentication(SecretReference.Create()), OperationalOptions(), now),
            ConnectionProfile.Create(ConnectionProfileId.New(), Metadata("SSH"),
                new SshClientEndpoint("ssh.example.test", 22, SshHostKeyPolicy.Pinned),
                new UsernamePasswordAuthentication("operator", Password), OperationalOptions(), now)
        };
        return data;
    }

    private static ConnectionProfileMetadata Metadata(string name) => new(name);

    private static ConnectionOperationalOptions OperationalOptions() => new(
        TimeSpan.FromSeconds(15),
        TimeSpan.FromMinutes(5),
        new ConnectionRetryPolicy(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10)),
        proxy: null,
        new ConnectionBandwidthLimits(null, null),
        "utf-8");
}
