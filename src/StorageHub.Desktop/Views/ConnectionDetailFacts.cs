using System.Globalization;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>
/// The details panel's rows for one connection, in 1.4's sections: Server, Authentication,
/// Security, Transfer, Organisation and Status.
/// </summary>
/// <remarks>
/// Ported from 1.4's ConnectionDetailView. The listing says only what a card needs, so Server,
/// Authentication, Security and Transfer come from the saved profile, read once a connection is
/// selected; until it has been read they say "Loading…", as 1.4's did. A row with nothing to say is
/// left out, and secrets are never shown: a stored one reads as the connection editor's field does,
/// [Key Store] and the entry's name, or [Vault] Stored for this connection.
/// </remarks>
internal static class ConnectionDetailFacts
{
    internal static IReadOnlyList<ConnectionDetailRow> Build(
        ConnectionCardModel card,
        ConnectionSummary? summary,
        ConnectionProfileDocument? profile,
        SecretReferenceNames? keyNames = null)
    {
        ArgumentNullException.ThrowIfNull(card);
        var rows = new List<ConnectionDetailRow>();
        void Section(string title) => rows.Add(new ConnectionDetailRow(title, string.Empty) { IsSection = true });
        void Fact(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) rows.Add(new ConnectionDetailRow(key, value));
        }

        void Secret(string key, string? reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return;
            var display = keyNames?.Describe(reference, Ui.KeyStore.StoredForThisConnection)
                ?? SecretReferenceNames.Vault(reference, Ui.KeyStore.StoredForThisConnection);
            rows.Add(new ConnectionDetailRow(key, display.Name) { Secret = display });
        }

        var endpoint = profile?.Draft.Endpoint;
        var auth = profile?.Draft.Authentication;
        var options = profile?.Draft.OperationalOptions;

        Section(Ui.Connections.SectionServer);
        Fact(Ui.Connections.Provider, card.Descriptor.DisplayName);
        if (endpoint is null)
        {
            Fact(Ui.Connections.FieldAddress, Ui.Connections.DetailLoading);
        }
        else
        {
            Fact(Ui.Connections.FieldHost, endpoint.Host);
            Fact(Ui.Connections.FieldPort, endpoint.Port?.ToString(CultureInfo.CurrentCulture));
            Fact(Ui.Connections.FieldBucket, endpoint.Bucket);
            Fact(Ui.Connections.FieldRegion, endpoint.Region);
            Fact(Ui.Connections.FieldService, endpoint.ServiceEndpoint);
            Fact(Ui.Connections.FieldPathStyle, endpoint.ForcePathStyle ? Ui.Connections.DetailForced : null);
            Fact(Ui.Connections.FieldPath, endpoint.RootPath);
        }

        Section(Ui.Connections.SectionAuthentication);
        if (auth is null)
        {
            Fact(Ui.Connections.FieldMethod, Ui.Connections.DetailLoading);
        }
        else
        {
            Fact(Ui.Connections.FieldMethod, DescribeAuthentication(auth.Kind));
            Fact(Ui.Connections.FieldUsername, auth.Username);
            Fact(
                Ui.Connections.FieldKeyFormatLabel,
                auth.Kind is ConnectionAuthenticationKind.SftpPrivateKey or ConnectionAuthenticationKind.SshPrivateKeyPassword
                    ? auth.PrivateKeyFormat.ToString()
                    : null);
            Secret(Ui.Connections.FieldPassword, auth.PasswordReference);
            Secret(Ui.Connections.FieldAccessKey, auth.AccessKeyReference);
            Secret(Ui.Connections.FieldSecretKey, auth.SecretKeyReference);
            Secret(Ui.Connections.FieldSessionToken, auth.SessionTokenReference);
            Secret(Ui.Connections.FieldPrivateKey, auth.PrivateKeyReference);
            Secret(Ui.Connections.FieldKeyPassphrase, auth.PrivateKeyPassphraseReference);
        }

        if (endpoint is not null && DescribeSecurity(endpoint) is { Count: > 0 } security)
        {
            Section(Ui.Connections.SectionSecurity);
            foreach (var (key, value) in security) Fact(key, value);
            if (endpoint.Provider == StorageConnectionProvider.Ftps)
            {
                Secret(Ui.Connections.FieldClientCert, endpoint.ClientCertificatePfxReference);
            }
        }

        if (options is not null)
        {
            Section(Ui.Connections.SectionTransfer);
            Fact(Ui.Connections.FieldConnectTimeout, Seconds(options.ConnectTimeoutSeconds));
            Fact(Ui.Connections.FieldOperationTimeout, Seconds(options.OperationTimeoutSeconds));
            Fact(Ui.Connections.FieldRetries, options.MaximumRetryAttempts.ToString(CultureInfo.CurrentCulture));
            Fact(Ui.Connections.FieldProxy, options.ProxyEndpoint);
            Fact(Ui.Connections.FieldUploadLimit, DescribeRate(options.UploadBytesPerSecond));
            Fact(Ui.Connections.FieldDownloadLimit, DescribeRate(options.DownloadBytesPerSecond));
            Fact(Ui.Connections.FieldEncoding, options.EncodingName);
        }

        Section(Ui.Connections.SectionOrganisation);
        Fact(Ui.Connections.FieldFolder, card.FolderPath);
        Fact(Ui.Connections.FieldTags, string.Join(", ", card.DisplayTags));
        Fact(Ui.Connections.FieldFavorite, card.IsFavorite ? Ui.Connections.DetailYes : Ui.Connections.DetailNo);

        Section(Ui.Connections.SectionStatus);
        Fact(Ui.Connections.FieldState, card.State);
        if (summary is { IsEnabled: true, Health: { } health })
        {
            Fact(Ui.Connections.FieldChecked, health.CheckedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
            Fact(Ui.Connections.FieldRoundTrip, $"{health.ElapsedMilliseconds.ToString("N0", CultureInfo.CurrentCulture)} ms");
            Fact(Ui.Connections.FieldDetail, health.Status);
        }

        return rows;
    }

    /// <summary>
    /// How the connection is protected on the wire, as 1.4 listed it: the TLS policy, the SSH host
    /// key policy, a client certificate, and "Transport: Unencrypted" for plain FTP.
    /// </summary>
    private static List<(string Key, string Value)> DescribeSecurity(ConnectionEndpointDocument endpoint)
    {
        var rows = new List<(string, string)>();
        switch (endpoint.Provider)
        {
            case StorageConnectionProvider.Ftps:
                rows.Add((Ui.Connections.FieldTls, DescribeTls(endpoint.TlsPolicy)));
                rows.Add((Ui.Connections.FieldFtpsMode, endpoint.FtpsTlsMode.ToString()));
                break;
            case StorageConnectionProvider.Sftp:
            case StorageConnectionProvider.Ssh:
                rows.Add((
                    Ui.Connections.FieldHostKey,
                    endpoint.SshHostKeyPolicy == ConnectionSshHostKeyPolicy.Pinned
                        ? Ui.Connections.DetailPinned
                        : Ui.Connections.DetailTrustOnFirstUse));
                break;
            case StorageConnectionProvider.S3:
                rows.Add((Ui.Connections.FieldTls, DescribeTls(endpoint.TlsPolicy)));
                break;
        }

        // Plain FTP has no TLS to describe, and is the one that most needs saying.
        if (endpoint.AllowInsecureTransport || endpoint.Provider == StorageConnectionProvider.Ftp)
        {
            rows.Add((Ui.Connections.FieldTransport, Ui.Connections.TransportUnencrypted));
        }

        return rows;
    }

    private static string DescribeTls(ConnectionTlsCertificatePolicy policy) => policy switch
    {
        ConnectionTlsCertificatePolicy.SystemTrust => Ui.Connections.TlsSystemTrust,
        ConnectionTlsCertificatePolicy.Pinned => Ui.Connections.TlsPinnedCertificate,
        ConnectionTlsCertificatePolicy.TrustOnFirstUse => Ui.Connections.DetailTrustOnFirstUse,
        _ => Ui.Connections.TlsUnspecified
    };

    private static string DescribeAuthentication(ConnectionAuthenticationKind kind) => kind switch
    {
        ConnectionAuthenticationKind.None => Ui.Connections.AuthAnonymous,
        ConnectionAuthenticationKind.S3DefaultCredentialChain => Ui.Connections.AuthDefaultCredentialChain,
        ConnectionAuthenticationKind.CredentialReference => Ui.Connections.AuthStoredCredential,
        ConnectionAuthenticationKind.UsernamePassword => Ui.Connections.AuthUsernamePassword,
        ConnectionAuthenticationKind.S3AccessKey => Ui.Connections.AuthAccessKeyAndSecret,
        ConnectionAuthenticationKind.SftpPrivateKey => Ui.Connections.AuthPrivateKey,
        ConnectionAuthenticationKind.SshPrivateKeyPassword => Ui.Connections.AuthPrivateKeyAndPassword,
        _ => kind.ToString()
    };

    private static string Seconds(int seconds) => $"{seconds.ToString(CultureInfo.CurrentCulture)} s";

    private static string? DescribeRate(long? bytesPerSecond) => bytesPerSecond switch
    {
        null or <= 0 => null,
        < 1024 => $"{bytesPerSecond.Value.ToString(CultureInfo.CurrentCulture)} B/s",
        < 1024 * 1024 => $"{(bytesPerSecond.Value / 1024d).ToString("N1", CultureInfo.CurrentCulture)} KB/s",
        _ => $"{(bytesPerSecond.Value / (1024d * 1024d)).ToString("N1", CultureInfo.CurrentCulture)} MB/s"
    };
}
