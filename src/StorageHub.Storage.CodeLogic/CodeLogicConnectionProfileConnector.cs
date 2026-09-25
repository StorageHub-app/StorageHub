using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CL.Storage.Configuration;
using StorageHub.Application.Connections;
using StorageHub.Domain.Identifiers;
using StorageHub.Contracts.Results;
using StorageHub.Security;

namespace StorageHub.Storage.CodeLogic;

/// <summary>
/// Resolves a saved profile immediately before use and registers it only in CL.Storage's
/// in-memory runtime registry. Resolved credentials are never written back to profile JSON.
///
/// One registration is shared by everything using a profile, and outlives the call that opened it
/// by <see cref="IdleLifetime"/>. Every call used to build its own: open the vault, decrypt the
/// credentials, materialise any client certificate to disk, register a connection under a fresh id,
/// connect, work, and tear all of it down again. Paging a listing did that per page, and because the
/// registration id changed every time, the storage library saw a different connection on each page
/// and could reuse neither its listing snapshot nor its pooled session.
///
/// The cost of sharing is that resolved credentials and materialised secret files live for the idle
/// window rather than for one call. The window is deliberately short, a profile edit retires the
/// registration at once rather than at expiry, and nothing is written anywhere durable.
/// </summary>
public sealed class CodeLogicConnectionProfileConnector : IAsyncDisposable
{
    /// <summary>How long an unused registration is kept before its secrets are released.</summary>
    internal static readonly TimeSpan DefaultIdleLifetime = TimeSpan.FromSeconds(30);

    private readonly TimeSpan _idleLifetime;

    private readonly CodeLogicStorageSessionFactory _sessionFactory;
    private readonly CodeLogicConnectionConfigurationBuilder _builder;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<ConnectionProfileId, Entry> _open = [];
    private readonly Lock _gate = new();
    private bool _disposed;

    public CodeLogicConnectionProfileConnector(
        CodeLogicStorageSessionFactory sessionFactory,
        ISecretVault secretVault,
        ITrustStore trustStore,
        IRuntimeSecretFileMaterializer secretFileMaterializer,
        TimeProvider? timeProvider = null,
        TimeSpan? idleLifetime = null)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _idleLifetime = idleLifetime ?? DefaultIdleLifetime;
        _builder = new CodeLogicConnectionConfigurationBuilder(
            secretVault,
            trustStore,
            secretFileMaterializer,
            _timeProvider);
    }

    /// <summary>
    /// Borrows the runtime connection for a profile, building one when none is open. The caller
    /// disposes what it gets back exactly as before; that returns the borrow rather than closing the
    /// connection.
    /// </summary>
    public async ValueTask<StorageResult<RuntimeStorageConnection>> OpenAsync(
        ConnectionProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (TryBorrow(profile, out var shared))
        {
            return StorageResult<RuntimeStorageConnection>.Success(shared);
        }

        // Built outside the lock: it opens the vault and can touch the disk. Two callers racing on
        // one profile therefore both build, and the loser's connection is retired below rather than
        // leaked -- the same trade as any build-then-publish cache, and self-correcting.
        var build = await _builder.BuildAsync(profile, cancellationToken).ConfigureAwait(false);
        if (build.IsFailure)
        {
            return StorageResult<RuntimeStorageConnection>.Fail(build.Error);
        }

        await using var prepared = build.Value;
        var registration = await _sessionFactory.RegisterPreparedAsync(
            profile.Id,
            prepared.RootIdentity,
            prepared.Configuration,
            prepared.RuntimeResources,
            cancellationToken).ConfigureAwait(false);
        if (registration.IsFailure)
        {
            return registration;
        }

        prepared.TransferResourceOwnership();
        return await PublishAsync(profile, registration.Value).ConfigureAwait(false);
    }

    /// <summary>Retires every open registration, releasing its credentials and secret files.</summary>
    public async ValueTask DisposeAsync()
    {
        Entry[] open;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            open = [.. _open.Values];
            _open.Clear();
        }

        foreach (var entry in open)
        {
            entry.Idle?.Dispose();
            await entry.Connection.TearDownAsync().ConfigureAwait(false);
        }
    }

    private bool TryBorrow(ConnectionProfile profile, out RuntimeStorageConnection connection)
    {
        lock (_gate)
        {
            if (!_disposed &&
                _open.TryGetValue(profile.Id, out var entry) &&
                entry.Version == profile.Version)
            {
                // Borrowed while idle: cancel the retirement that was already scheduled.
                entry.Idle?.Dispose();
                entry.Idle = null;
                entry.Connection.Retain();
                connection = entry.Connection;
                return true;
            }
        }

        connection = null!;
        return false;
    }

    private async ValueTask<StorageResult<RuntimeStorageConnection>> PublishAsync(
        ConnectionProfile profile,
        RuntimeStorageConnection connection)
    {
        RuntimeStorageConnection? retire = null;
        RuntimeStorageConnection? stale = null;
        lock (_gate)
        {
            if (_disposed)
            {
                retire = connection;
            }
            else if (_open.TryGetValue(profile.Id, out var existing) && existing.Version == profile.Version)
            {
                // Another caller published first. Theirs wins, so one profile keeps one
                // registration; ours is retired without ever having been handed out.
                existing.Idle?.Dispose();
                existing.Idle = null;
                existing.Connection.Retain();
                retire = connection;
                connection = existing.Connection;
            }
            else
            {
                // A registration for an older version of this profile must not be reused: its
                // credentials, host or root may all have changed.
                if (existing is not null)
                {
                    existing.Idle?.Dispose();
                    existing.Idle = null;
                    stale = existing.Connection;
                }

                connection.BindOwner(ReleaseAsync);
                _open[profile.Id] = new Entry(connection, profile.Version);
            }
        }

        // Outside the lock: both close sessions and delete materialised secret files.
        if (stale is not null)
        {
            await stale.DisposeAsync().ConfigureAwait(false);
        }

        if (retire is not null)
        {
            await retire.TearDownAsync().ConfigureAwait(false);
        }

        return StorageResult<RuntimeStorageConnection>.Success(connection);
    }

    /// <summary>
    /// Called when the last borrow is returned. The registration is kept for a short idle window,
    /// because the next call on the same profile is usually the next page of the same listing.
    /// </summary>
    private ValueTask ReleaseAsync(RuntimeStorageConnection connection)
    {
        var orphaned = true;
        lock (_gate)
        {
            if (!_disposed && TryFindKey(connection, out var key))
            {
                orphaned = false;
                var entry = _open[key];
                entry.Idle?.Dispose();
                entry.Idle = _timeProvider.CreateTimer(
                    _ => RetireIfStillIdle(connection),
                    state: null,
                    _idleLifetime,
                    Timeout.InfiniteTimeSpan);
            }
        }

        // Already replaced by an edit, or the connector is shutting down: really close it.
        return orphaned ? connection.TearDownAsync() : ValueTask.CompletedTask;
    }

    private void RetireIfStillIdle(RuntimeStorageConnection connection)
    {
        var retire = false;
        lock (_gate)
        {
            if (TryFindKey(connection, out var key) && _open[key].Idle is { } idle)
            {
                idle.Dispose();
                _open.Remove(key);
                retire = true;
            }
        }

        if (!retire)
        {
            // Borrowed again between the timer firing and this lock, so it is in use.
            return;
        }

        // The timer owns no caller to report to, so the teardown carries its own failure handling.
        _ = Task.Run(async () =>
        {
            try
            {
                await connection.TearDownAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A registration that will not close must not take the agent down with it; the
                // CodeLogic host disposes what is left when it stops.
            }
        });
    }

    private bool TryFindKey(RuntimeStorageConnection connection, out ConnectionProfileId key)
    {
        foreach (var pair in _open)
        {
            if (ReferenceEquals(pair.Value.Connection, connection))
            {
                key = pair.Key;
                return true;
            }
        }

        key = default;
        return false;
    }

    private sealed class Entry(RuntimeStorageConnection connection, long version)
    {
        public RuntimeStorageConnection Connection { get; } = connection;

        public long Version { get; } = version;

        public ITimer? Idle { get; set; }
    }
}

internal sealed class CodeLogicConnectionConfigurationBuilder
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private readonly ISecretVault _secretVault;
    private readonly ITrustStore _trustStore;
    private readonly IRuntimeSecretFileMaterializer _secretFileMaterializer;
    private readonly TimeProvider _timeProvider;

    internal CodeLogicConnectionConfigurationBuilder(
        ISecretVault secretVault,
        ITrustStore trustStore,
        IRuntimeSecretFileMaterializer secretFileMaterializer,
        TimeProvider timeProvider)
    {
        _secretVault = secretVault ?? throw new ArgumentNullException(nameof(secretVault));
        _trustStore = trustStore ?? throw new ArgumentNullException(nameof(trustStore));
        _secretFileMaterializer = secretFileMaterializer ??
            throw new ArgumentNullException(nameof(secretFileMaterializer));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    internal async ValueTask<StorageResult<PreparedCodeLogicConnection>> BuildAsync(
        ConnectionProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!profile.IsEnabled || profile.DeletedUtc is not null)
        {
            return Fail("storage.profile.disabled", StorageFailureKind.Conflict,
                "The connection profile is disabled or deleted.");
        }

        // FTP takes a connect and a read timeout of its own; S3 and SFTP have one timeout for both.
        var usesHistoricalEditorDefaults = UsesHistoricalEditorDefaults(profile.OperationalOptions);
        if (profile.Provider is ConnectionProviderKind.S3 or ConnectionProviderKind.Sftp &&
            profile.OperationalOptions.ConnectTimeout != profile.OperationalOptions.OperationTimeout &&
            !usesHistoricalEditorDefaults)
        {
            return Unsupported("storage.timeout.unsupported",
                "The selected CL.Storage provider exposes one timeout, so connect and operation timeouts must match.");
        }

        if (!IsUtf8(profile.OperationalOptions.EncodingName))
        {
            if (profile.Provider is not (ConnectionProviderKind.Ftp or ConnectionProviderKind.Ftps or ConnectionProviderKind.Sftp))
            {
                return Unsupported("storage.encoding.unsupported",
                    "Only FTP and SFTP servers have a filename encoding; this provider's names are always UTF-8.");
            }

            if (!IsKnownEncoding(profile.OperationalOptions.EncodingName))
            {
                return Unsupported("storage.encoding.unknown",
                    "The filename encoding is not one this system knows, such as utf-8 or windows-1252.");
            }
        }

        var runtimeResources = new List<IAsyncDisposable>();
        var rootIdentityEvidence = new RootIdentityEvidence();
        try
        {
            object configuration = profile.Endpoint switch
            {
                LocalEndpoint local => BuildLocal(local),
                S3Endpoint s3 => await BuildS3Async(
                    profile, s3, rootIdentityEvidence, cancellationToken).ConfigureAwait(false),
                FtpEndpoint ftp => await BuildFtpAsync(
                    profile, ftp, rootIdentityEvidence, cancellationToken).ConfigureAwait(false),
                FtpsEndpoint ftps => await BuildFtpsAsync(
                    profile, ftps, runtimeResources, rootIdentityEvidence, cancellationToken)
                    .ConfigureAwait(false),
                SftpEndpoint sftp => await BuildSftpAsync(
                    profile, sftp, runtimeResources, rootIdentityEvidence, cancellationToken)
                    .ConfigureAwait(false),
                _ => throw new UnsupportedProfileException("The connection provider is not supported by this adapter.")
            };

            ApplySpeedLimits(configuration, profile.OperationalOptions.Bandwidth);
            if (profile.OperationalOptions.Proxy is { } proxy)
            {
                await ApplyProxyAsync(configuration, proxy, cancellationToken).ConfigureAwait(false);
            }

            return StorageResult<PreparedCodeLogicConnection>.Success(new PreparedCodeLogicConnection(
                configuration,
                CreateRootIdentity(profile, rootIdentityEvidence.Items),
                runtimeResources));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await DisposeResourcesAsync(runtimeResources).ConfigureAwait(false);
            throw;
        }
        catch (TrustApprovalRequiredException)
        {
            await DisposeResourcesAsync(runtimeResources).ConfigureAwait(false);
            return Fail("storage.trust.approval_required", StorageFailureKind.Security,
                "A verified certificate or SSH host-key fingerprint is required before connecting.");
        }
        catch (UnsupportedProfileException error)
        {
            await DisposeResourcesAsync(runtimeResources).ConfigureAwait(false);
            return Unsupported("storage.profile.unsupported", error.Message);
        }
        catch (SecretNotFoundException)
        {
            await DisposeResourcesAsync(runtimeResources).ConfigureAwait(false);
            return Fail("storage.credential.unavailable", StorageFailureKind.Security,
                "A required credential is unavailable in the current-user vault.");
        }
        catch (SecretVaultException)
        {
            await DisposeResourcesAsync(runtimeResources).ConfigureAwait(false);
            return Fail("storage.credential.invalid", StorageFailureKind.Security,
                "A required credential could not be authenticated by the current-user vault.");
        }
        catch (DecoderFallbackException)
        {
            await DisposeResourcesAsync(runtimeResources).ConfigureAwait(false);
            return Fail("storage.credential.encoding_invalid", StorageFailureKind.Security,
                "A text credential is not valid UTF-8.");
        }
        catch (UnprotectedPrivateKeyException)
        {
            await DisposeResourcesAsync(runtimeResources).ConfigureAwait(false);
            return Fail("storage.credential.private_key_unprotected", StorageFailureKind.Security,
                "The SSH private key is not encrypted with a supported passphrase-protected format.");
        }
        catch (InvalidPrivateKeyException)
        {
            await DisposeResourcesAsync(runtimeResources).ConfigureAwait(false);
            return Fail("storage.credential.private_key_invalid", StorageFailureKind.Security,
                "The SSH private key is malformed or could not be decrypted with its vault passphrase.");
        }
        catch (IOException)
        {
            await DisposeResourcesAsync(runtimeResources).ConfigureAwait(false);
            return Fail("storage.credential.materialization_failed", StorageFailureKind.Security,
                "A private credential could not be prepared for the provider.");
        }
        catch (UnauthorizedAccessException)
        {
            await DisposeResourcesAsync(runtimeResources).ConfigureAwait(false);
            return Fail("storage.credential.materialization_denied", StorageFailureKind.Security,
                "The private runtime credential directory is unavailable.");
        }
    }

    private static LocalConnectionConfig BuildLocal(LocalEndpoint endpoint) => new()
    {
        Enabled = true,
        RootPath = endpoint.RootPath,
        FollowLinks = false
    };

    private async ValueTask<S3ConnectionConfig> BuildS3Async(
        ConnectionProfile profile,
        S3Endpoint endpoint,
        RootIdentityEvidence rootIdentityEvidence,
        CancellationToken cancellationToken)
    {
        if (endpoint.TlsPolicy is TlsCertificatePolicy.Pinned or TlsCertificatePolicy.TrustOnFirstUse)
        {
            throw new UnsupportedProfileException(
                "The current CL.Storage S3 provider cannot enforce per-connection TLS certificate pins.");
        }

        var configuration = new S3ConnectionConfig
        {
            Enabled = true,
            Bucket = endpoint.Bucket,
            Prefix = endpoint.RootPrefix,
            Region = endpoint.Region,
            ServiceUrl = endpoint.ServiceEndpoint?.AbsoluteUri,
            ForcePathStyle = endpoint.ForcePathStyle,
            AllowInsecureHttp = endpoint.AllowInsecureHttp,
            DisablePayloadSigning = false,
            DisableDefaultChecksumValidation = false,
            TimeoutSeconds = TimeoutSeconds(profile),
            MaxRetries = Math.Max(0, profile.OperationalOptions.Retry.MaximumAttempts - 1)
        };

        switch (profile.Authentication)
        {
            case S3DefaultCredentialChainAuthentication:
                configuration.AuthenticationMode = S3AuthenticationMode.DefaultCredentialChain;
                break;
            case S3AccessKeyAuthentication accessKey:
                configuration.AuthenticationMode = S3AuthenticationMode.StaticCredentials;
                configuration.AccessKey = await OpenTextSecretAsync(
                    accessKey.AccessKeyReference,
                    "s3.access-key",
                    rootIdentityEvidence,
                    cancellationToken).ConfigureAwait(false);
                configuration.SecretKey = await OpenTextSecretAsync(
                    accessKey.SecretKeyReference,
                    "s3.secret-key",
                    rootIdentityEvidence,
                    cancellationToken).ConfigureAwait(false);
                configuration.SessionToken = accessKey.SessionTokenReference is { } token
                    ? await OpenTextSecretAsync(
                        token,
                        "s3.session-token",
                        rootIdentityEvidence,
                        cancellationToken).ConfigureAwait(false)
                    : null;
                break;
            case CredentialReferenceAuthentication:
                throw new UnsupportedProfileException(
                    "This generic credential record does not expose typed S3 access-key slots.");
            default:
                throw new UnsupportedProfileException("The selected S3 authentication mode is unsupported.");
        }

        return configuration;
    }

    private async ValueTask<FtpConnectionConfig> BuildFtpAsync(
        ConnectionProfile profile,
        FtpEndpoint endpoint,
        RootIdentityEvidence rootIdentityEvidence,
        CancellationToken cancellationToken)
    {
        var configuration = BuildFtpBase(profile, endpoint.Host, endpoint.Port);
        configuration.EncryptionMode = StorageFtpEncryptionMode.None;
        await ApplyFtpAuthenticationAsync(
            profile,
            configuration,
            rootIdentityEvidence,
            cancellationToken).ConfigureAwait(false);
        return configuration;
    }

    private async ValueTask<FtpConnectionConfig> BuildFtpsAsync(
        ConnectionProfile profile,
        FtpsEndpoint endpoint,
        List<IAsyncDisposable> runtimeResources,
        RootIdentityEvidence rootIdentityEvidence,
        CancellationToken cancellationToken)
    {
        if (endpoint.TlsPolicy == TlsCertificatePolicy.TrustOnFirstUse)
        {
            throw new UnsupportedProfileException(
                "Trust-on-first-use certificate capture is unavailable; verify and pin the certificate explicitly.");
        }

        var configuration = BuildFtpBase(profile, endpoint.Host, endpoint.Port);
        configuration.EncryptionMode = endpoint.TlsMode == FtpsTlsMode.Implicit
            ? StorageFtpEncryptionMode.Implicit
            : StorageFtpEncryptionMode.Explicit;
        configuration.TrustedCertificateSha256 = endpoint.TlsPolicy == TlsCertificatePolicy.SystemTrust
            ? []
            : await GetTrustedFingerprintsAsync(
                TrustArtifactKind.TlsCertificate,
                endpoint.Host,
                endpoint.Port,
                "ftps.server-certificate",
                rootIdentityEvidence,
                cancellationToken).ConfigureAwait(false);
        await ApplyFtpAuthenticationAsync(
            profile,
            configuration,
            rootIdentityEvidence,
            cancellationToken).ConfigureAwait(false);

        if (endpoint.ClientCertificatePfxReference is { } pfxReference)
        {
            await using var lease = await _secretVault.OpenAsync(pfxReference, cancellationToken).ConfigureAwait(false);
            rootIdentityEvidence.AddSecret("ftps.client-certificate", pfxReference, lease.Version);
            var material = await _secretFileMaterializer.MaterializeAsync(
                lease.Memory,
                ".pfx",
                cancellationToken).ConfigureAwait(false);
            runtimeResources.Add(material);
            configuration.ClientCertificatePath = material.FullPath;
            configuration.ClientCertificatePassword = endpoint.ClientCertificatePasswordReference is { } passwordReference
                ? await OpenTextSecretAsync(
                    passwordReference,
                    "ftps.client-certificate-password",
                    rootIdentityEvidence,
                    cancellationToken).ConfigureAwait(false)
                : null;
        }

        return configuration;
    }

    private async ValueTask<SftpConnectionConfig> BuildSftpAsync(
        ConnectionProfile profile,
        SftpEndpoint endpoint,
        List<IAsyncDisposable> runtimeResources,
        RootIdentityEvidence rootIdentityEvidence,
        CancellationToken cancellationToken)
    {
        if (endpoint.HostKeyPolicy == SshHostKeyPolicy.TrustOnFirstUse)
        {
            throw new UnsupportedProfileException(
                "Trust-on-first-use host-key capture is unavailable; verify and pin the SSH host key explicitly.");
        }

        var configuration = new SftpConnectionConfig
        {
            Enabled = true,
            Host = endpoint.Host,
            Port = endpoint.Port,
            Root = endpoint.RootPath,
            TimeoutSeconds = TimeoutSeconds(profile),
            Encoding = profile.OperationalOptions.EncodingName,
            Retry = LibraryRetry(profile),
            HostKeyFingerprints = await GetTrustedFingerprintsAsync(
                TrustArtifactKind.SshHostKey,
                endpoint.Host,
                endpoint.Port,
                "sftp.host-key",
                rootIdentityEvidence,
                cancellationToken).ConfigureAwait(false)
        };

        switch (profile.Authentication)
        {
            case UsernamePasswordAuthentication password:
                configuration.Username = password.Username;
                configuration.AuthenticationMode = SftpAuthenticationMode.Password;
                configuration.Password = await OpenTextSecretAsync(
                    password.PasswordReference,
                    "sftp.password",
                    rootIdentityEvidence,
                    cancellationToken).ConfigureAwait(false);
                break;
            case SftpPrivateKeyAuthentication privateKey:
                configuration.Username = privateKey.Username;
                configuration.AuthenticationMode = SftpAuthenticationMode.PrivateKey;
                var passphraseReference = privateKey.PassphraseReference ??
                    throw new UnprotectedPrivateKeyException();
                var privateKeyPassphrase = await OpenTextSecretAsync(
                    passphraseReference,
                    "sftp.private-key-passphrase",
                    rootIdentityEvidence,
                    cancellationToken).ConfigureAwait(false);
                await using (var lease = await _secretVault
                    .OpenAsync(privateKey.PrivateKeyReference, cancellationToken)
                    .ConfigureAwait(false))
                {
                    rootIdentityEvidence.AddSecret(
                        "sftp.private-key",
                        privateKey.PrivateKeyReference,
                        lease.Version);
                    switch (PrivateKeyEncryptionValidator.Validate(
                        lease.Memory.Span,
                        privateKeyPassphrase,
                        privateKey.KeyFormat))
                    {
                        case PrivateKeyValidationResult.Unencrypted:
                            throw new UnprotectedPrivateKeyException();
                        case PrivateKeyValidationResult.Invalid:
                            throw new InvalidPrivateKeyException();
                    }

                    var material = await _secretFileMaterializer.MaterializeAsync(
                        lease.Memory,
                        privateKey.KeyFormat == SftpPrivateKeyFormat.OpenSsh ? ".key" : ".pem",
                        cancellationToken).ConfigureAwait(false);
                    runtimeResources.Add(material);
                    configuration.PrivateKeyPath = material.FullPath;
                }

                configuration.PrivateKeyPassphrase = privateKeyPassphrase;
                break;
            default:
                throw new UnsupportedProfileException("The selected SFTP authentication mode is unsupported.");
        }

        return configuration;
    }

    private static FtpConnectionConfig BuildFtpBase(
        ConnectionProfile profile,
        string host,
        int port) => new()
        {
            Enabled = true,
            Host = host,
            Port = port,
            Root = profile.Endpoint switch
            {
                FtpEndpoint ftp => ftp.RootPath,
                FtpsEndpoint ftps => ftps.RootPath,
                _ => string.Empty
            },
            // Connect and read are separate on FTP; a data connection waits as long as a read.
            TimeoutSeconds = OperationTimeoutSeconds(profile),
            ConnectTimeoutSeconds = TimeoutSeconds(profile),
            ReadTimeoutSeconds = OperationTimeoutSeconds(profile),
            DataConnectionTimeoutSeconds = OperationTimeoutSeconds(profile),
            Encoding = profile.OperationalOptions.EncodingName,
            ServerTimeZone = ServerOptionsOf(profile).ServerTimeZone,
            ListingParser = (StorageFtpListingParser)(int)ServerOptionsOf(profile).ListingFormat,
            DataConnectionMode = ServerOptionsOf(profile).DataConnectionMode == FtpDataConnectionMode.Active
                ? StorageFtpDataConnectionMode.AutoActive
                : StorageFtpDataConnectionMode.AutoPassive,
            ActivePortMin = ServerOptionsOf(profile).ActivePortMinimum,
            ActivePortMax = ServerOptionsOf(profile).ActivePortMaximum,
            ActiveExternalIp = ServerOptionsOf(profile).ActiveExternalAddress?.ToString(),
            Retry = LibraryRetry(profile)
        };

    private static FtpServerOptions ServerOptionsOf(ConnectionProfile profile) => profile.Endpoint switch
    {
        FtpEndpoint ftp => ftp.ServerOptions,
        FtpsEndpoint ftps => ftps.ServerOptions,
        _ => FtpServerOptions.Default
    };

    private static int OperationTimeoutSeconds(ConnectionProfile profile) =>
        checked((int)Math.Ceiling(profile.OperationalOptions.OperationTimeout.TotalSeconds));

    private static bool IsUtf8(string name) =>
        string.Equals(name, "utf-8", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "utf8", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether .NET knows the encoding, legacy code pages included, which is the same table CL.Storage
    /// looks names up in. Checked here so an unknown name is refused with a reason rather than by the
    /// library's configuration validation.
    /// </summary>
    private static bool IsKnownEncoding(string name)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            _ = Encoding.GetEncoding(name);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// The profile's retry policy, for the retries CL.Storage makes itself on FTP and SFTP since
    /// 4.8.93, clamped to the bounds it accepts. Attempts count the first try, as S3's MaxRetries
    /// does not. Deletes and moves are left unretried: a retry after an unseen success acts twice.
    /// </summary>
    internal static StorageRetryConfig LibraryRetry(ConnectionProfile profile)
    {
        var retry = profile.OperationalOptions.Retry;
        var baseDelay = (int)Math.Clamp(retry.InitialDelay.TotalMilliseconds, 1, 60_000);
        return new StorageRetryConfig
        {
            RetryCount = Math.Clamp(retry.MaximumAttempts - 1, 0, 10),
            BaseDelayMs = baseDelay,
            MaxDelayMs = (int)Math.Clamp(retry.MaximumDelay.TotalMilliseconds, baseDelay, 300_000),
            RetryNonIdempotent = false
        };
    }

    private async ValueTask ApplyFtpAuthenticationAsync(
        ConnectionProfile profile,
        FtpConnectionConfig configuration,
        RootIdentityEvidence rootIdentityEvidence,
        CancellationToken cancellationToken)
    {
        switch (profile.Authentication)
        {
            case NoAuthentication:
                configuration.Username = "anonymous";
                configuration.Password = "anonymous@";
                break;
            case UsernamePasswordAuthentication password:
                configuration.Username = password.Username;
                configuration.Password = await OpenTextSecretAsync(
                    password.PasswordReference,
                    "ftp.password",
                    rootIdentityEvidence,
                    cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new UnsupportedProfileException("The selected FTP authentication mode is unsupported.");
        }
    }

    private async ValueTask<List<string>> GetTrustedFingerprintsAsync(
        TrustArtifactKind artifactKind,
        string host,
        int port,
        string role,
        RootIdentityEvidence rootIdentityEvidence,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var records = await _trustStore.FindAsync(artifactKind, host, port, cancellationToken)
            .ConfigureAwait(false);
        var fingerprints = records
            .Where(record => record.Decision == TrustDecision.Trusted &&
                (record.ExpiresUtc is null || record.ExpiresUtc > now))
            .Select(record =>
            {
                rootIdentityEvidence.AddTrust(role, record);
                return record;
            })
            .Select(record => record.Sha256Fingerprint)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return fingerprints.Count == 0
            ? throw new TrustApprovalRequiredException()
            : fingerprints;
    }

    /// <param name="rootIdentityEvidence">
    /// Where the secret's version is recorded when it decides what the connection sees, or null for
    /// one that does not, such as a proxy's password: changing it must not look like a new root.
    /// </param>
    private async ValueTask<string> OpenTextSecretAsync(
        SecretReference reference,
        string role,
        RootIdentityEvidence? rootIdentityEvidence,
        CancellationToken cancellationToken)
    {
        await using var lease = await _secretVault.OpenAsync(reference, cancellationToken).ConfigureAwait(false);
        rootIdentityEvidence?.AddSecret(role, reference, lease.Version);
        var value = StrictUtf8.GetString(lease.Memory.Span);
        if (value.Length == 0 || value.Contains('\0', StringComparison.Ordinal))
        {
            throw new DecoderFallbackException("The credential is empty or contains a null character.");
        }

        return value;
    }

    private static int TimeoutSeconds(ConnectionProfile profile) => checked((int)Math.Ceiling(
        profile.Provider == ConnectionProviderKind.Local
            ? profile.OperationalOptions.OperationTimeout.TotalSeconds
            : profile.OperationalOptions.ConnectTimeout.TotalSeconds));

    /// <summary>
    /// Routes the connection through its proxy: HTTP CONNECT, SOCKS4 or SOCKS5, which CL.Storage
    /// applies to every provider's control and data connections alike.
    /// </summary>
    /// <remarks>
    /// An HTTPS proxy is refused rather than downgraded: no provider speaks TLS to the proxy, and
    /// connecting to it in the clear would send the proxy password unprotected. A local connection
    /// has no network to route.
    /// </remarks>
    private async ValueTask ApplyProxyAsync(
        object configuration,
        ConnectionProxy proxy,
        CancellationToken cancellationToken)
    {
        var target = configuration switch
        {
            S3ConnectionConfig s3 => s3.Proxy,
            // Only passive data connections go through a proxy; an active one would bypass it.
            FtpConnectionConfig { DataConnectionMode: StorageFtpDataConnectionMode.AutoActive } =>
                throw new UnsupportedProfileException(
                    "Active FTP cannot go through a proxy: the server would connect back around it. Use passive mode."),
            FtpConnectionConfig ftp => ftp.Proxy,
            SftpConnectionConfig sftp => sftp.Proxy,
            _ => throw new UnsupportedProfileException("A local connection cannot use a proxy.")
        };

        target.Type = proxy.Endpoint.Scheme switch
        {
            "http" => StorageProxyType.Http,
            "socks4" => StorageProxyType.Socks4,
            "socks5" => StorageProxyType.Socks5,
            _ => throw new UnsupportedProfileException(
                "An HTTPS proxy is not supported: StorageHub cannot use TLS to reach a proxy. Use an HTTP or SOCKS5 proxy.")
        };
        target.Host = proxy.Endpoint.IdnHost;
        target.Port = proxy.Endpoint.Port;
        target.Username = proxy.Username;
        target.Password = proxy.PasswordReference is { } reference
            ? await OpenTextSecretAsync(reference, "proxy.password", rootIdentityEvidence: null, cancellationToken)
                .ConfigureAwait(false)
            : null;
    }

    /// <summary>
    /// Hands the connection's speed limits to CL.Storage, which enforces them on every download and
    /// upload through the connection, its own checksum reads included, and shares each limit between
    /// all of the connection's concurrent transfers.
    /// </summary>
    private static void ApplySpeedLimits(object configuration, ConnectionBandwidthLimits bandwidth)
    {
        var limits = configuration switch
        {
            StorageConnectionConfigBase provider => provider.TransferLimits,
            LocalConnectionConfig local => local.TransferLimits,
            _ => throw new UnsupportedProfileException("The connection provider does not accept speed limits.")
        };
        limits.MaxUploadBytesPerSecond = bandwidth.UploadBytesPerSecond;
        limits.MaxDownloadBytesPerSecond = bandwidth.DownloadBytesPerSecond;
    }

    // Speed limits and the proxy are left out: they have nothing to do with which timeouts the editor wrote.
    private static bool UsesHistoricalEditorDefaults(ConnectionOperationalOptions options) =>
        options.ConnectTimeout == TimeSpan.FromSeconds(30) &&
        options.OperationTimeout == TimeSpan.FromSeconds(60) &&
        options.Retry.MaximumAttempts == 3 &&
        options.Retry.InitialDelay == TimeSpan.FromMilliseconds(250) &&
        options.Retry.MaximumDelay == TimeSpan.FromSeconds(5) &&
        string.Equals(options.EncodingName, "utf-8", StringComparison.OrdinalIgnoreCase);

    private static string CreateRootIdentity(
        ConnectionProfile profile,
        IReadOnlyCollection<RootIdentityEvidenceItem> revisionEvidence)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendIdentityValue(hash, "storagehub-root-identity-v2");
        AppendIdentityValue(hash, profile.Id.Value.ToString("N"));
        AppendIdentityValue(hash, profile.Version.ToString(CultureInfo.InvariantCulture));
        AppendIdentityValue(hash, profile.Provider.ToString());

        switch (profile.Endpoint)
        {
            case LocalEndpoint local:
                AppendIdentityValue(hash, local.RootPath);
                break;
            case S3Endpoint s3:
                AppendIdentityValue(hash, s3.ServiceEndpoint?.AbsoluteUri);
                AppendIdentityValue(hash, s3.Region);
                AppendIdentityValue(hash, s3.Bucket);
                AppendIdentityValue(hash, s3.RootPrefix);
                AppendIdentityValue(hash, s3.ForcePathStyle ? "path-style" : "virtual-host-style");
                break;
            case FtpEndpoint ftp:
                AppendNetworkNamespace(hash, ftp.Host, ftp.Port, ftp.RootPath);
                break;
            case FtpsEndpoint ftps:
                AppendNetworkNamespace(hash, ftps.Host, ftps.Port, ftps.RootPath);
                break;
            case SftpEndpoint sftp:
                AppendNetworkNamespace(hash, sftp.Host, sftp.Port, sftp.RootPath);
                break;
        }

        switch (profile.Authentication)
        {
            case NoAuthentication:
                AppendIdentityValue(hash, "anonymous-or-none");
                break;
            case S3DefaultCredentialChainAuthentication:
                AppendIdentityValue(hash, "s3-default-credential-chain");
                break;
            case CredentialReferenceAuthentication credential:
                AppendIdentityValue(hash, "credential-reference");
                AppendIdentityValue(hash, credential.CredentialId.Value.ToString("N"));
                break;
            case UsernamePasswordAuthentication password:
                AppendIdentityValue(hash, "username-password");
                AppendIdentityValue(hash, password.Username);
                break;
            case S3AccessKeyAuthentication:
                AppendIdentityValue(hash, "s3-static-credentials");
                break;
            case SftpPrivateKeyAuthentication privateKey:
                AppendIdentityValue(hash, "sftp-private-key");
                AppendIdentityValue(hash, privateKey.Username);
                AppendIdentityValue(hash, privateKey.KeyFormat.ToString());
                break;
        }

        foreach (var evidence in revisionEvidence
            .OrderBy(static item => item.Category, StringComparer.Ordinal)
            .ThenBy(static item => item.Role, StringComparer.Ordinal)
            .ThenBy(static item => item.Reference, StringComparer.Ordinal)
            .ThenBy(static item => item.Revision)
            .ThenBy(static item => item.Binding, StringComparer.Ordinal))
        {
            AppendIdentityValue(hash, evidence.Category);
            AppendIdentityValue(hash, evidence.Role);
            AppendIdentityValue(hash, evidence.Reference);
            AppendIdentityValue(hash, evidence.Revision.ToString(CultureInfo.InvariantCulture));
            AppendIdentityValue(hash, evidence.Binding);
        }

        var digest = hash.GetHashAndReset();
        return $"profile:{profile.Id.Value:N}:version:{profile.Version}:root:{Convert.ToHexString(digest)}";
    }

    private static void AppendNetworkNamespace(
        IncrementalHash hash,
        string host,
        int port,
        string rootPath)
    {
        AppendIdentityValue(hash, host);
        AppendIdentityValue(hash, port.ToString(CultureInfo.InvariantCulture));
        AppendIdentityValue(hash, rootPath);
    }

    private static void AppendIdentityValue(IncrementalHash hash, string? value)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        if (value is null)
        {
            BinaryPrimitives.WriteInt32LittleEndian(length, -1);
            hash.AppendData(length);
            return;
        }

        var bytes = StrictUtf8.GetBytes(value);
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static StorageResult<PreparedCodeLogicConnection> Fail(
        string code,
        StorageFailureKind kind,
        string message) => StorageResult<PreparedCodeLogicConnection>.Fail(new StorageFailure(code, kind, message));

    private static StorageResult<PreparedCodeLogicConnection> Unsupported(string code, string message) =>
        Fail(code, StorageFailureKind.Unsupported, message);

    private static async ValueTask DisposeResourcesAsync(List<IAsyncDisposable> resources)
    {
        for (var index = resources.Count - 1; index >= 0; index--)
        {
            await resources[index].DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class UnsupportedProfileException(string message) : Exception(message);
    private sealed class TrustApprovalRequiredException : Exception;
    private sealed class UnprotectedPrivateKeyException : Exception;
    private sealed class InvalidPrivateKeyException : Exception;

    private sealed class RootIdentityEvidence
    {
        private readonly List<RootIdentityEvidenceItem> _items = [];

        internal IReadOnlyCollection<RootIdentityEvidenceItem> Items => _items.AsReadOnly();

        internal void AddSecret(string role, SecretReference reference, int version) =>
            _items.Add(new RootIdentityEvidenceItem(
                "vault-secret-revision",
                role,
                reference.Value,
                version,
                Binding: null));

        internal void AddTrust(string role, TrustRecord record) =>
            _items.Add(new RootIdentityEvidenceItem(
                "trust-record-revision",
                role,
                record.TrustId,
                record.Version,
                $"{record.ArtifactKind}|{record.Algorithm}|{record.Sha256Fingerprint}|" +
                $"{record.Decision}|{record.ExpiresUtc?.ToUniversalTime():O}"));
    }

    private sealed record RootIdentityEvidenceItem(
        string Category,
        string Role,
        string Reference,
        int Revision,
        string? Binding);
}

internal sealed class PreparedCodeLogicConnection(
    object configuration,
    string rootIdentity,
    List<IAsyncDisposable> runtimeResources) : IAsyncDisposable
{
    private bool _resourcesTransferred;

    internal object Configuration { get; } = configuration;
    internal string RootIdentity { get; } = rootIdentity;
    internal IReadOnlyList<IAsyncDisposable> RuntimeResources { get; } = runtimeResources.AsReadOnly();

    internal void TransferResourceOwnership() => _resourcesTransferred = true;

    public async ValueTask DisposeAsync()
    {
        ClearSecretStrings();
        if (_resourcesTransferred)
        {
            return;
        }

        for (var index = RuntimeResources.Count - 1; index >= 0; index--)
        {
            await RuntimeResources[index].DisposeAsync().ConfigureAwait(false);
        }
    }

    private void ClearSecretStrings()
    {
        switch (Configuration)
        {
            case S3ConnectionConfig s3:
                s3.AccessKey = null;
                s3.SecretKey = null;
                s3.SessionToken = null;
                break;
            case FtpConnectionConfig ftp:
                ftp.Password = string.Empty;
                ftp.ClientCertificatePassword = null;
                break;
            case SftpConnectionConfig sftp:
                sftp.Password = null;
                sftp.PrivateKeyPassphrase = null;
                break;
        }
    }
}
