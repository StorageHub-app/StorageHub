using System.Collections.Concurrent;
using StorageHub.Ipc;
using StorageHub.Application.Connections;
using StorageHub.Contracts.Ipc;
using StorageHub.Contracts.Results;
using StorageHub.Domain.Identifiers;
using StorageHub.Domain.Storage;
using StorageHub.Infrastructure.Windows;
using StorageHub.Persistence;
using StorageHub.Persistence.Connections;
using StorageHub.Persistence.Trust;
using StorageHub.Security;
using StorageHub.Storage.Abstractions;
using StorageHub.Storage.CodeLogic;
using StorageHub.Storage.Models;

namespace StorageHub.Agent.Host;

/// <summary>A disposable, root-scoped storage session opened for one IPC request.</summary>
public interface IStorageIpcSessionLease : IAsyncDisposable
{
    IStorageEndpointSession Session { get; }

    /// <summary>What the connection and its server look like now, where the session can say.</summary>
    ValueTask<StorageResult<StorageConnectionDetails>> GetDetailsAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(StorageResult<StorageConnectionDetails>.Fail(new StorageFailure(
            "storage.connection.details_unsupported",
            StorageFailureKind.Unsupported,
            "This connection cannot describe itself.")));
}

/// <summary>Opens a saved profile without persisting its resolved runtime credentials.</summary>
public interface IStorageIpcSessionOpener
{
    ValueTask<StorageResult<IStorageIpcSessionLease>> OpenAsync(
        ConnectionProfile profile,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a profile that is being tested rather than used: on a registration of its own, closed
    /// with the lease, and trusting <paramref name="typedIdentity"/> as well as what is saved.
    /// </summary>
    /// <remarks>
    /// An opener with no trust of its own to add to, such as a test's, opens it as any other.
    /// </remarks>
    ValueTask<StorageResult<IStorageIpcSessionLease>> OpenForTestAsync(
        ConnectionProfile profile,
        TrustRecord? typedIdentity,
        CancellationToken cancellationToken = default) => OpenAsync(profile, cancellationToken);
}

/// <summary>
/// Implements only saved-connection discovery, connection health checks, and bounded storage
/// listing. It intentionally has no upload, mutation, delete, or secret-returning command.
/// </summary>
public sealed class StorageIpcCommandService : IAgentIpcCommandHandler, IAsyncDisposable
{
    private const int MaximumCachedHealthSnapshots = 256;
    private readonly IConnectionProfileRepository _profiles;
    private readonly IStorageIpcSessionOpener _sessionOpener;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<Guid, CachedConnectionHealth> _health = new();
    private readonly object _healthGate = new();

    public StorageIpcCommandService(
        IConnectionProfileRepository profiles,
        IStorageIpcSessionOpener sessionOpener,
        TimeProvider? timeProvider = null)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _sessionOpener = sessionOpener ?? throw new ArgumentNullException(nameof(sessionOpener));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Creates the production service over the authoritative SQLite stores, current-user vault,
    /// and CL.Storage's runtime-only registration adapter.
    /// </summary>
    public StorageIpcCommandService(
        SqliteDatabaseOptions databaseOptions,
        Func<ISecretVault> vaultProvider,
        IRuntimeSecretFileMaterializer secretFileMaterializer,
        CodeLogicStorageSessionFactory sessionFactory,
        TimeProvider? timeProvider = null)
        : this(
            databaseOptions,
            vaultProvider,
            secretFileMaterializer,
            () => sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory)),
            timeProvider)
    {
    }

    /// <summary>
    /// Defers CL.Storage lookup until a command runs. This supports CodeLogic hosts where the
    /// loaded library becomes retrievable only after the configure phase has completed.
    /// </summary>
    public StorageIpcCommandService(
        SqliteDatabaseOptions databaseOptions,
        Func<ISecretVault> vaultProvider,
        IRuntimeSecretFileMaterializer secretFileMaterializer,
        Func<CodeLogicStorageSessionFactory> sessionFactoryProvider,
        TimeProvider? timeProvider = null)
        : this(
            new SqliteConnectionProfileRepository(databaseOptions, timeProvider),
            CreateCodeLogicSessionOpener(
                databaseOptions,
                vaultProvider,
                secretFileMaterializer,
                sessionFactoryProvider,
                timeProvider),
            timeProvider)
    {
    }

    /// <summary>
    /// Releases the shared connector, which is what closes any registration still open and deletes
    /// the secret files materialised for it. Process exit would do it eventually; a graceful stop
    /// should not wait for that.
    /// </summary>
    public ValueTask DisposeAsync() =>
        _sessionOpener is IAsyncDisposable disposable ? disposable.DisposeAsync() : ValueTask.CompletedTask;

    public bool CanHandle(string messageType) => messageType is
        StorageIpcMessageTypes.ConnectionListRequest or
        StorageIpcMessageTypes.ConnectionTestRequest or
        StorageIpcMessageTypes.ConnectionDraftTestRequest or
        StorageIpcMessageTypes.ConnectionDetailsRequest or
        StorageIpcMessageTypes.StorageListRequest;

    public ValueTask<AgentIpcCommandResponse> HandleAsync(
        IpcEnvelope request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.MessageType switch
        {
            StorageIpcMessageTypes.ConnectionListRequest =>
                ListConnectionsAsync(request, cancellationToken),
            StorageIpcMessageTypes.ConnectionTestRequest =>
                TestConnectionAsync(request, cancellationToken),
            StorageIpcMessageTypes.ConnectionDraftTestRequest =>
                TestDraftAsync(request, cancellationToken),
            StorageIpcMessageTypes.ConnectionDetailsRequest =>
                DescribeConnectionAsync(request, cancellationToken),
            StorageIpcMessageTypes.StorageListRequest =>
                ListStorageAsync(request, cancellationToken),
            _ => ValueTask.FromResult(AgentIpcCommandResponse.Error(
                "ipc.message.unsupported",
                "The requested IPC operation is not supported by this agent version."))
        };
    }

    private async ValueTask<AgentIpcCommandResponse> ListConnectionsAsync(
        IpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var request = envelope.DeserializePayload<ConnectionListRequest>();
        var validation = ValidateRequest(request.ContractVersion, request.HasValidBounds);
        if (validation is not null)
        {
            return validation;
        }

        try
        {
            var profiles = await _profiles.SearchAsync(
                new ConnectionProfileSearch(
                    Text: NormalizeSearchText(request.SearchText),
                    Provider: request.Provider is { } provider ? MapProvider(provider) : null,
                    IncludeDisabled: request.IncludeDisabled,
                    IncludeDeleted: false,
                    Limit: request.Limit),
                cancellationToken).ConfigureAwait(false);
            if (profiles.Count > request.Limit || profiles.Count > StorageIpcLimits.MaximumConnectionResults)
            {
                return ConnectionListFailure(
                    request.ContractVersion,
                    "storage.ipc.response.invalid",
                    StorageIpcFailureCategory.Integrity,
                    "The saved connection list exceeded the negotiated response limit.");
            }

            var summaries = profiles.Select(profile => MapConnection(
                profile,
                StorageIpcContract.SupportsConnectionHealth(request.ContractVersion))).ToArray();
            return AgentIpcCommandResponse.Create(
                StorageIpcMessageTypes.ConnectionListResponse,
                new ConnectionListResponse(request.ContractVersion, summaries));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ConnectionListFailure(
                request.ContractVersion,
                "storage.connections.unavailable",
                StorageIpcFailureCategory.Unavailable,
                "Saved connections are temporarily unavailable.",
                isTransient: true);
        }
    }

    private async ValueTask<AgentIpcCommandResponse> TestConnectionAsync(
        IpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var request = envelope.DeserializePayload<ConnectionTestRequest>();
        var validation = ValidateRequest(request.ContractVersion, request.HasValidBounds);
        if (validation is not null)
        {
            return validation;
        }

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var profile = await GetProfileAsync(request.ConnectionId, cancellationToken).ConfigureAwait(false);
            if (profile is null)
            {
                return ConnectionTestFailure(
                    request.ContractVersion,
                    request.ConnectionId,
                    started,
                    NotFoundFailure());
            }

            var opened = await _sessionOpener.OpenAsync(profile, cancellationToken).ConfigureAwait(false);
            if (opened.IsFailure)
            {
                return CompleteConnectionTest(
                    profile,
                    request.ContractVersion,
                    started,
                    SanitizeFailure(opened.Error));
            }

            await using var lease = opened.Value;
            var sessionFailure = ValidateSession(lease.Session, profile.Id);
            if (sessionFailure is not null)
            {
                return CompleteConnectionTest(
                    profile,
                    request.ContractVersion,
                    started,
                    sessionFailure);
            }

            var health = await lease.Session.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
            return health.IsSuccess
                ? CompleteConnectionTest(profile, request.ContractVersion, started)
                : CompleteConnectionTest(
                    profile,
                    request.ContractVersion,
                    started,
                    SanitizeFailure(health.Error));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ConnectionTestFailure(
                request.ContractVersion,
                request.ConnectionId,
                started,
                new StorageIpcFailure(
                    "storage.connection.unavailable",
                    StorageIpcFailureCategory.Unavailable,
                    "The connection could not be tested.",
                    IsTransient: true));
        }
    }

    /// <summary>
    /// Tests the connection in the editor, saved or not, and says how far it got: its settings, the
    /// server's identity, the credentials, reaching the server, and listing its starting folder.
    /// </summary>
    /// <remarks>
    /// Nothing is saved and no health is recorded: the profile is built under an id of its own,
    /// opened on a registration of its own and dropped with it, so a saved connection's open
    /// listings and cached health are untouched by a test of edits not saved yet.
    /// </remarks>
    private async ValueTask<AgentIpcCommandResponse> TestDraftAsync(
        IpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var request = envelope.DeserializePayload<ConnectionDraftTestRequest>();
        var validation = ValidateRequest(request.ContractVersion, request.HasValidBounds);
        if (validation is not null)
        {
            return validation;
        }

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var steps = new DraftTestSteps();
        try
        {
            ConnectionProfile profile;
            try
            {
                // Enabled for the test whatever the draft says: testing is how a disabled
                // connection is checked before it is turned back on.
                profile = ConnectionProfileIpcMapper.ToNewProfile(
                    ConnectionProfileId.New(),
                    request.Draft with { IsEnabled = true },
                    _timeProvider.GetUtcNow());
            }
            catch (Exception error) when (error is ArgumentException or FormatException or UriFormatException)
            {
                return CompleteDraftTest(request, started, steps.Fail(
                    ConnectionTestStepKind.Settings,
                    new StorageIpcFailure(
                        "storage.profile.invalid",
                        StorageIpcFailureCategory.Validation,
                        "The connection settings are not valid.",
                        IsTransient: false)));
            }

            steps.Pass(ConnectionTestStepKind.Settings);
            var identity = TypedIdentity(profile, request.Sha256Fingerprint);
            var opened = await _sessionOpener
                .OpenForTestAsync(profile, identity, cancellationToken)
                .ConfigureAwait(false);
            if (opened.IsFailure)
            {
                // The connector checks the server's identity before the credentials, so each
                // failure says which of the two it reached.
                var failure = SanitizeFailure(opened.Error);
                return CompleteDraftTest(request, started, failure.Code switch
                {
                    var code when code.StartsWith("storage.trust.", StringComparison.Ordinal) =>
                        steps.Fail(ConnectionTestStepKind.ServerIdentity, failure),
                    var code when code.StartsWith("storage.credential.", StringComparison.Ordinal) =>
                        steps.Pass(ConnectionTestStepKind.ServerIdentity, HasPinnedIdentity(profile))
                            .Fail(ConnectionTestStepKind.Credentials, failure),
                    _ when failure.Category is StorageIpcFailureCategory.Unsupported or
                        StorageIpcFailureCategory.Validation or StorageIpcFailureCategory.Conflict =>
                        steps.Fail(ConnectionTestStepKind.Settings, failure),
                    _ => steps
                        .Pass(ConnectionTestStepKind.ServerIdentity, HasPinnedIdentity(profile))
                        .Pass(ConnectionTestStepKind.Credentials, HasCredentials(profile))
                        .Fail(ConnectionTestStepKind.Connect, failure)
                });
            }

            steps.Pass(ConnectionTestStepKind.ServerIdentity, HasPinnedIdentity(profile));
            steps.Pass(ConnectionTestStepKind.Credentials, HasCredentials(profile));
            await using var lease = opened.Value;
            var session = lease.Session;
            var sessionFailure = ValidateSession(session, profile.Id);
            if (sessionFailure is not null)
            {
                return CompleteDraftTest(request, started, steps.Fail(ConnectionTestStepKind.Connect, sessionFailure));
            }

            var health = await session.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
            if (health.IsFailure)
            {
                return CompleteDraftTest(request, started, steps.FailAtServer(SanitizeFailure(health.Error)));
            }

            steps.Pass(ConnectionTestStepKind.Connect);
            var root = StorageAddress.Create(profile.Id, session.RootIdentity, string.Empty);
            if (root.IsFailure)
            {
                return CompleteDraftTest(request, started, steps.Fail(
                    ConnectionTestStepKind.Browse, SanitizeFailure(root.Error)));
            }

            var listed = await session.ListAsync(
                root.Value,
                new StorageListRequest(Recursive: false, PageSize: 1, ContinuationToken: null, IncludeVersions: false),
                cancellationToken).ConfigureAwait(false);
            return CompleteDraftTest(request, started, listed.IsSuccess
                ? steps.Pass(ConnectionTestStepKind.Browse)
                : steps.Fail(ConnectionTestStepKind.Browse, SanitizeFailure(listed.Error)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return CompleteDraftTest(request, started, steps.FailAtNext(new StorageIpcFailure(
                "storage.connection.unavailable",
                StorageIpcFailureCategory.Unavailable,
                "The connection could not be tested.",
                IsTransient: true)));
        }
    }

    /// <summary>
    /// Describes a saved connection as it is now: what the server presented, runs and negotiated,
    /// and its sessions. It opens the connection as browsing does, sharing what is open already.
    /// </summary>
    private async ValueTask<AgentIpcCommandResponse> DescribeConnectionAsync(
        IpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var request = envelope.DeserializePayload<ConnectionDetailsRequest>();
        var validation = ValidateRequest(request.ContractVersion, request.HasValidBounds);
        if (validation is not null)
        {
            return validation;
        }

        try
        {
            var profile = await GetProfileAsync(request.ConnectionId, cancellationToken).ConfigureAwait(false);
            if (profile is null)
            {
                return DetailsFailure(request, NotFoundFailure());
            }

            var opened = await _sessionOpener.OpenAsync(profile, cancellationToken).ConfigureAwait(false);
            if (opened.IsFailure)
            {
                return DetailsFailure(request, SanitizeFailure(opened.Error));
            }

            await using var lease = opened.Value;
            var described = await lease.GetDetailsAsync(cancellationToken).ConfigureAwait(false);
            if (described.IsFailure)
            {
                return DetailsFailure(request, SanitizeFailure(described.Error));
            }

            var document = ToDetailsDocument(described.Value);
            return document.HasValidBounds
                ? AgentIpcCommandResponse.Create(
                    StorageIpcMessageTypes.ConnectionDetailsResponse,
                    new ConnectionDetailsResponse(request.ContractVersion, request.ConnectionId, document))
                : DetailsFailure(request, new StorageIpcFailure(
                    "storage.response.invalid",
                    StorageIpcFailureCategory.Integrity,
                    "The provider described the connection in a way that could not be shown.",
                    IsTransient: false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return DetailsFailure(request, new StorageIpcFailure(
                "storage.connection.unavailable",
                StorageIpcFailureCategory.Unavailable,
                "The connection could not be described.",
                IsTransient: true));
        }
    }

    /// <summary>
    /// The details as the contract carries them: text the server chose is cut to the contract's
    /// length with control characters dropped, and lists to its counts, rather than refused.
    /// </summary>
    private static ConnectionDetailsDocument ToDetailsDocument(StorageConnectionDetails details)
    {
        static string? Text(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var clean = new string([.. value.Where(static character => !char.IsControl(character))]).Trim();
            return clean.Length == 0
                ? null
                : clean.Length <= ConnectionDetailsLimits.MaximumTextLength
                    ? clean
                    : clean[..ConnectionDetailsLimits.MaximumTextLength];
        }

        return new ConnectionDetailsDocument(
            Text(details.Provider) ?? "Unknown",
            Text(details.Host),
            details.Port,
            Text(details.TransportSecurity) ?? "Unknown",
            Text(details.ServerSystem),
            Text(details.ServerSoftware),
            [.. details.ServerFeatures
                .Select(Text)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .Take(ConnectionDetailsLimits.MaximumFeatures)],
            [.. details.Negotiated
                .Select(setting => (Name: Text(setting.Key), Value: Text(setting.Value)))
                .Where(static setting => setting.Name is not null)
                .Take(ConnectionDetailsLimits.MaximumNegotiated)
                .Select(static setting => new ConnectionNegotiatedSetting(setting.Name!, setting.Value))],
            details.PresentedIdentity is { } identity && Text(identity.Kind) is { } kind &&
                Text(identity.Fingerprint) is { } fingerprint
                ? new ConnectionPresentedIdentityDocument(
                    kind,
                    fingerprint,
                    Text(identity.Algorithm),
                    Text(identity.Subject),
                    Text(identity.Issuer),
                    identity.NotAfter,
                    identity.Trusted)
                : null,
            details.Sessions is { } sessions
                ? new ConnectionSessionCountsDocument(
                    Math.Max(0, sessions.Idle),
                    Math.Max(0, sessions.InUse),
                    Math.Max(0, sessions.Opened),
                    Math.Max(0, sessions.Closed),
                    Math.Max(0, sessions.FailedProbes))
                : null,
            details.LastHealthCheck is { } health
                ? new ConnectionHealthCheckDocument(
                    health.Healthy,
                    Text(health.ErrorCode),
                    Math.Max(0, (long)health.Latency.TotalMilliseconds),
                    health.CheckedUtc.ToUniversalTime())
                : null);
    }

    private static AgentIpcCommandResponse DetailsFailure(
        ConnectionDetailsRequest request,
        StorageIpcFailure failure) => AgentIpcCommandResponse.Create(
        StorageIpcMessageTypes.ConnectionDetailsResponse,
        new ConnectionDetailsResponse(request.ContractVersion, request.ConnectionId, Details: null, failure));

    /// <summary>
    /// The fingerprint typed in the editor as the record saving would pin, for the identity this
    /// profile checks; nothing when it checks none or none was typed.
    /// </summary>
    private TrustRecord? TypedIdentity(ConnectionProfile profile, string? fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return null;
        }

        var target = profile.Endpoint switch
        {
            FtpsEndpoint { TlsPolicy: TlsCertificatePolicy.Pinned } ftps =>
                (Kind: TrustArtifactKind.TlsCertificate, ftps.Host, ftps.Port),
            SftpEndpoint { HostKeyPolicy: SshHostKeyPolicy.Pinned } sftp =>
                (Kind: TrustArtifactKind.SshHostKey, sftp.Host, sftp.Port),
            SshClientEndpoint { HostKeyPolicy: SshHostKeyPolicy.Pinned } ssh =>
                (Kind: TrustArtifactKind.SshHostKey, ssh.Host, ssh.Port),
            _ => ((TrustArtifactKind Kind, string Host, int Port)?)null
        };
        if (target is not { } pinned)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        return new TrustRecord(
            "draft-test",
            pinned.Kind,
            pinned.Host,
            pinned.Port,
            "SHA256",
            fingerprint.Trim(),
            TrustDecision.Trusted,
            TrustDecisionSource.UserVerified,
            now,
            now,
            ExpiresUtc: null,
            PreviousFingerprint: null,
            Version: 1);
    }

    private static bool HasPinnedIdentity(ConnectionProfile profile) => profile.Endpoint switch
    {
        FtpsEndpoint { TlsPolicy: TlsCertificatePolicy.Pinned } => true,
        SftpEndpoint { HostKeyPolicy: SshHostKeyPolicy.Pinned } => true,
        SftpEndpoint { ServerOptions.JumpConnectionId: not null } => true,
        SshClientEndpoint { HostKeyPolicy: SshHostKeyPolicy.Pinned } => true,
        _ => false
    };

    private static bool HasCredentials(ConnectionProfile profile) =>
        profile.Authentication is not (NoAuthentication or S3DefaultCredentialChainAuthentication) ||
        profile.OperationalOptions.Proxy?.PasswordReference is not null;

    private static AgentIpcCommandResponse CompleteDraftTest(
        ConnectionDraftTestRequest request,
        long started,
        DraftTestSteps steps) => AgentIpcCommandResponse.Create(
        StorageIpcMessageTypes.ConnectionDraftTestResponse,
        new ConnectionDraftTestResponse(
            request.ContractVersion,
            steps.Failure is null,
            ElapsedMilliseconds(started),
            steps.ToArray(),
            steps.Failure));

    /// <summary>
    /// The steps of one test in their order, each passed, failed or not applicable as it is
    /// reached, and every one after a failure not reached.
    /// </summary>
    private sealed class DraftTestSteps
    {
        private static readonly ConnectionTestStepKind[] Order =
        [
            ConnectionTestStepKind.Settings,
            ConnectionTestStepKind.ServerIdentity,
            ConnectionTestStepKind.Credentials,
            ConnectionTestStepKind.Connect,
            ConnectionTestStepKind.Browse
        ];

        private readonly Dictionary<ConnectionTestStepKind, ConnectionTestStep> _taken = [];

        public StorageIpcFailure? Failure { get; private set; }

        public DraftTestSteps Pass(ConnectionTestStepKind kind, bool applies = true)
        {
            _taken[kind] = new ConnectionTestStep(
                kind,
                applies ? ConnectionTestStepOutcome.Passed : ConnectionTestStepOutcome.NotApplicable);
            return this;
        }

        public DraftTestSteps Fail(ConnectionTestStepKind kind, StorageIpcFailure failure)
        {
            _taken[kind] = new ConnectionTestStep(kind, ConnectionTestStepOutcome.Failed, failure);
            Failure = failure;
            return this;
        }

        /// <summary>
        /// A failure from the server itself, put where it belongs: a key or certificate it presented
        /// that is not the trusted one, credentials it refused, or not reaching it at all.
        /// </summary>
        public DraftTestSteps FailAtServer(StorageIpcFailure failure) => failure.Category switch
        {
            StorageIpcFailureCategory.Security => Fail(ConnectionTestStepKind.ServerIdentity, failure),
            StorageIpcFailureCategory.Unauthorized => Fail(ConnectionTestStepKind.Credentials, failure),
            _ => Fail(ConnectionTestStepKind.Connect, failure)
        };

        /// <summary>Fails the first step not yet taken.</summary>
        public DraftTestSteps FailAtNext(StorageIpcFailure failure) =>
            Fail(Order.FirstOrDefault(kind => !_taken.ContainsKey(kind), ConnectionTestStepKind.Browse), failure);

        public ConnectionTestStep[] ToArray()
        {
            var failed = false;
            var steps = new ConnectionTestStep[Order.Length];
            for (var index = 0; index < Order.Length; index++)
            {
                var kind = Order[index];
                if (failed)
                {
                    steps[index] = new ConnectionTestStep(kind, ConnectionTestStepOutcome.NotReached);
                    continue;
                }

                steps[index] = _taken.TryGetValue(kind, out var step)
                    ? step
                    : new ConnectionTestStep(kind, ConnectionTestStepOutcome.NotReached);
                failed = steps[index].Outcome == ConnectionTestStepOutcome.Failed;
            }

            return steps;
        }
    }

    private async ValueTask<AgentIpcCommandResponse> ListStorageAsync(
        IpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var request = envelope.DeserializePayload<StorageListPageRequest>();
        var validation = ValidateRequest(request.ContractVersion, request.HasValidBounds);
        if (validation is not null)
        {
            return validation;
        }

        try
        {
            var profile = await GetProfileAsync(request.ConnectionId, cancellationToken).ConfigureAwait(false);
            if (profile is null)
            {
                return StorageListFailure(request, NotFoundFailure());
            }

            var opened = await _sessionOpener.OpenAsync(profile, cancellationToken).ConfigureAwait(false);
            if (opened.IsFailure)
            {
                return StorageListFailure(request, SanitizeFailure(opened.Error));
            }

            await using var lease = opened.Value;
            var session = lease.Session;
            var sessionFailure = ValidateSession(session, profile.Id);
            if (sessionFailure is not null)
            {
                return StorageListFailure(request, sessionFailure);
            }

            var address = StorageAddress.Create(
                profile.Id,
                session.RootIdentity,
                request.RelativePath);
            if (address.IsFailure)
            {
                return StorageListFailure(request, SanitizeFailure(address.Error));
            }

            var listed = await session.ListAsync(
                address.Value,
                new StorageListRequest(
                    request.Recursive,
                    request.PageSize,
                    request.ContinuationToken,
                    request.IncludeVersions),
                cancellationToken).ConfigureAwait(false);
            if (listed.IsFailure)
            {
                return StorageListFailure(request, SanitizeFailure(listed.Error));
            }

            var pageFailure = ValidatePage(
                listed.Value,
                session,
                address.Value,
                request.PageSize,
                request.Recursive);
            if (pageFailure is not null)
            {
                return StorageListFailure(request, pageFailure);
            }

            var includeStableIdentities = StorageIpcContract.SupportsStableItemIdentities(
                request.ContractVersion);
            var entries = listed.Value.Entries
                .Select(entry => MapEntry(entry, includeStableIdentities))
                .ToArray();
            return AgentIpcCommandResponse.Create(
                StorageIpcMessageTypes.StorageListResponse,
                new StorageListPageResponse(
                    request.ContractVersion,
                    request.ConnectionId,
                    address.Value.CanonicalRelativePath,
                    entries,
                    listed.Value.ContinuationToken,
                    RootIdentity: includeStableIdentities ? session.RootIdentity : null));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return StorageListFailure(
                request,
                new StorageIpcFailure(
                    "storage.list.unavailable",
                    StorageIpcFailureCategory.Unavailable,
                    "The folder could not be listed.",
                    IsTransient: true));
        }
    }

    private async ValueTask<ConnectionProfile?> GetProfileAsync(
        Guid connectionId,
        CancellationToken cancellationToken) => await _profiles.GetAsync(
        new ConnectionProfileId(connectionId),
        includeDeleted: false,
        cancellationToken).ConfigureAwait(false);

    private static AgentIpcCommandResponse? ValidateRequest(int contractVersion, bool hasValidBounds)
    {
        if (!StorageIpcContract.IsSupported(contractVersion))
        {
            return AgentIpcCommandResponse.Error(
                "ipc.contract.unsupported",
                "The requested storage IPC contract version is not supported.");
        }

        return hasValidBounds
            ? null
            : AgentIpcCommandResponse.Error(
                "ipc.payload.invalid",
                "The request payload exceeded a permitted bound or contained an invalid value.");
    }

    private static string? NormalizeSearchText(string? searchText) =>
        string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim();

    private static ConnectionProviderKind MapProvider(StorageConnectionProvider provider) => provider switch
    {
        StorageConnectionProvider.Local => ConnectionProviderKind.Local,
        StorageConnectionProvider.S3 => ConnectionProviderKind.S3,
        StorageConnectionProvider.Ftp => ConnectionProviderKind.Ftp,
        StorageConnectionProvider.Ftps => ConnectionProviderKind.Ftps,
        StorageConnectionProvider.WebDav => ConnectionProviderKind.WebDav,
        StorageConnectionProvider.AzureBlob => ConnectionProviderKind.AzureBlob,
        StorageConnectionProvider.GoogleCloudStorage => ConnectionProviderKind.GoogleCloudStorage,
        StorageConnectionProvider.Sftp => ConnectionProviderKind.Sftp,
        StorageConnectionProvider.Ssh => ConnectionProviderKind.Ssh,
        _ => throw new InvalidDataException("The connection provider is invalid.")
    };

    private static StorageConnectionProvider MapProvider(ConnectionProviderKind provider) => provider switch
    {
        ConnectionProviderKind.Local => StorageConnectionProvider.Local,
        ConnectionProviderKind.S3 => StorageConnectionProvider.S3,
        ConnectionProviderKind.Ftp => StorageConnectionProvider.Ftp,
        ConnectionProviderKind.Ftps => StorageConnectionProvider.Ftps,
        ConnectionProviderKind.WebDav => StorageConnectionProvider.WebDav,
        ConnectionProviderKind.AzureBlob => StorageConnectionProvider.AzureBlob,
        ConnectionProviderKind.GoogleCloudStorage => StorageConnectionProvider.GoogleCloudStorage,
        ConnectionProviderKind.Sftp => StorageConnectionProvider.Sftp,
        ConnectionProviderKind.Ssh => StorageConnectionProvider.Ssh,
        _ => throw new InvalidDataException("The saved connection provider is invalid.")
    };

    private ConnectionSummary MapConnection(ConnectionProfile profile, bool includeHealth)
    {
        ConnectionHealthSnapshot? health = null;
        if (includeHealth &&
            _health.TryGetValue(profile.Id.Value, out var cached) &&
            cached.ProfileVersion == profile.Version)
        {
            health = cached.Snapshot;
        }

        return new ConnectionSummary(
        profile.Id.Value,
        profile.Metadata.DisplayName,
        MapProvider(profile.Provider),
        profile.Metadata.FolderPath,
        profile.Metadata.Tags.ToArray(),
        profile.Metadata.IsFavorite,
        profile.IsEnabled,
        profile.Metadata.IconKey,
        profile.Metadata.AccentColor,
        profile.Version,
        profile.Type == StorageHub.Application.Connections.ConnectionProfileType.Client
            ? StorageHub.Contracts.Ipc.ConnectionProfileType.Client
            : StorageHub.Contracts.Ipc.ConnectionProfileType.Storage,
        health,
        profile.Metadata.GroupId);
    }

    private static StorageListItem MapEntry(StorageEntry entry, bool includeStableIdentities) => new(
        entry.Name,
        entry.Address.CanonicalRelativePath,
        entry.Kind switch
        {
            StorageEntryKind.File => StorageItemKind.File,
            StorageEntryKind.Directory => StorageItemKind.Directory,
            StorageEntryKind.Prefix => StorageItemKind.Prefix,
            StorageEntryKind.SymbolicLink => StorageItemKind.SymbolicLink,
            _ => StorageItemKind.Other
        },
        entry.Size,
        entry.LastModifiedUtc,
        entry.ContentType,
        entry.IsContainer,
        NativeItemId: includeStableIdentities ? entry.Address.NativeItemId : null,
        VersionId: includeStableIdentities ? entry.Address.VersionId : null,
        EntityTag: includeStableIdentities ? entry.Address.EntityTag ?? entry.ETag : null);

    private static StorageIpcFailure? ValidateSession(
        IStorageEndpointSession session,
        ConnectionProfileId expectedProfileId)
    {
        if (session is null ||
            session.ProfileId != expectedProfileId ||
            string.IsNullOrWhiteSpace(session.RootIdentity) ||
            session.RootIdentity.Any(char.IsControl) ||
            session.RootIdentity.Length > 8_192)
        {
            return new StorageIpcFailure(
                "storage.session.invalid",
                StorageIpcFailureCategory.Integrity,
                "The provider returned an invalid root-scoped session.",
                IsTransient: false);
        }

        return null;
    }

    private static StorageIpcFailure? ValidatePage(
        StoragePage page,
        IStorageEndpointSession session,
        StorageAddress listedAddress,
        int requestedPageSize,
        bool recursive)
    {
        if (page.Entries.Count > requestedPageSize ||
            page.Entries.Count > StorageIpcLimits.MaximumStoragePageSize ||
            !IsValidContinuationToken(page.ContinuationToken))
        {
            return InvalidProviderPage();
        }

        foreach (var entry in page.Entries)
        {
            if (entry.Address.ProfileId != session.ProfileId ||
                !string.Equals(entry.Address.RootIdentity, session.RootIdentity, StringComparison.Ordinal) ||
                entry.Name.Length > StorageIpcLimits.MaximumItemNameLength ||
                entry.Address.CanonicalRelativePath.Length > StorageIpcLimits.MaximumRelativePathLength ||
                entry.ContentType?.Length > StorageIpcLimits.MaximumContentTypeLength ||
                entry.ContentType?.Any(char.IsControl) == true ||
                !IsBoundedIdentity(entry.Address.NativeItemId) ||
                !IsBoundedIdentity(entry.Address.VersionId) ||
                !IsBoundedIdentity(entry.Address.EntityTag) ||
                !IsBoundedIdentity(entry.ETag) ||
                entry.Address.EntityTag is not null && entry.ETag is not null &&
                    !string.Equals(entry.Address.EntityTag, entry.ETag, StringComparison.Ordinal) ||
                !IsWithinListing(entry.Address.CanonicalRelativePath, listedAddress.CanonicalRelativePath, recursive))
            {
                return InvalidProviderPage();
            }
        }

        return null;
    }

    private static bool IsValidContinuationToken(string? token) => token is null ||
        token.Length <= StorageIpcLimits.MaximumContinuationTokenLength &&
        !token.Any(char.IsControl);

    private static bool IsBoundedIdentity(string? value) => value is null ||
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= StorageIpcLimits.MaximumOpaqueIdentityLength &&
        !value.Any(char.IsControl);

    private static bool IsWithinListing(string itemPath, string listedPath, bool recursive)
    {
        if (string.Equals(itemPath, listedPath, StringComparison.Ordinal))
        {
            return true;
        }

        var prefix = listedPath.Length == 0 ? string.Empty : listedPath + "/";
        if (!itemPath.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var remainder = itemPath[prefix.Length..];
        return remainder.Length > 0 && (recursive || !remainder.Contains('/', StringComparison.Ordinal));
    }

    private static StorageIpcFailure InvalidProviderPage() => new(
        "storage.ipc.provider_response_invalid",
        StorageIpcFailureCategory.Integrity,
        "The provider returned a page that could not be exposed safely.",
        IsTransient: false);

    private static StorageIpcFailure NotFoundFailure() => new(
        "storage.connection.not_found",
        StorageIpcFailureCategory.NotFound,
        "The saved connection was not found.",
        IsTransient: false);

    private static StorageIpcFailure SanitizeFailure(StorageFailure failure)
    {
        var category = failure.Kind switch
        {
            StorageFailureKind.Validation => StorageIpcFailureCategory.Validation,
            StorageFailureKind.NotFound => StorageIpcFailureCategory.NotFound,
            StorageFailureKind.Conflict => StorageIpcFailureCategory.Conflict,
            StorageFailureKind.Unsupported => StorageIpcFailureCategory.Unsupported,
            StorageFailureKind.Unauthorized => StorageIpcFailureCategory.Unauthorized,
            StorageFailureKind.Unavailable => StorageIpcFailureCategory.Unavailable,
            StorageFailureKind.Timeout => StorageIpcFailureCategory.Timeout,
            StorageFailureKind.Cancelled => StorageIpcFailureCategory.Cancelled,
            StorageFailureKind.Integrity => StorageIpcFailureCategory.Integrity,
            StorageFailureKind.Security => StorageIpcFailureCategory.Security,
            StorageFailureKind.Provider => StorageIpcFailureCategory.Provider,
            _ => StorageIpcFailureCategory.Unexpected
        };
        var code = IsSafeFailureCode(failure.Code) ? failure.Code : "storage.operation.failed";
        return new StorageIpcFailure(code, category, SafeFailureMessage(category), failure.IsTransient);
    }

    private static bool IsSafeFailureCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > StorageIpcLimits.MaximumFailureCodeLength)
        {
            return false;
        }

        return code.All(static character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
    }

    private static string SafeFailureMessage(StorageIpcFailureCategory category) => category switch
    {
        StorageIpcFailureCategory.Validation => "The storage request was invalid.",
        StorageIpcFailureCategory.NotFound => "The requested storage item was not found.",
        StorageIpcFailureCategory.Conflict => "The storage item changed or the connection is disabled.",
        StorageIpcFailureCategory.Unsupported => "The provider does not support this operation safely.",
        StorageIpcFailureCategory.Unauthorized => "The provider rejected the saved credentials.",
        StorageIpcFailureCategory.Unavailable => "The storage provider is temporarily unavailable.",
        StorageIpcFailureCategory.Timeout => "The storage provider did not respond in time.",
        StorageIpcFailureCategory.Cancelled => "The storage operation was cancelled.",
        StorageIpcFailureCategory.Integrity => "The provider response failed an integrity check.",
        StorageIpcFailureCategory.Security => "The connection requires a security or trust decision.",
        StorageIpcFailureCategory.Provider => "The storage provider could not complete the operation.",
        _ => "The storage operation could not be completed."
    };

    private static AgentIpcCommandResponse ConnectionListFailure(
        int contractVersion,
        string code,
        StorageIpcFailureCategory category,
        string message,
        bool isTransient = false) => AgentIpcCommandResponse.Create(
        StorageIpcMessageTypes.ConnectionListResponse,
        new ConnectionListResponse(
            contractVersion,
            [],
            new StorageIpcFailure(code, category, message, isTransient)));

    private static AgentIpcCommandResponse ConnectionTestFailure(
        int contractVersion,
        Guid connectionId,
        long started,
        StorageIpcFailure failure) => AgentIpcCommandResponse.Create(
        StorageIpcMessageTypes.ConnectionTestResponse,
        new ConnectionTestResponse(
            contractVersion,
            connectionId,
            Succeeded: false,
            ElapsedMilliseconds: ElapsedMilliseconds(started),
            failure));

    private AgentIpcCommandResponse CompleteConnectionTest(
        ConnectionProfile profile,
        int contractVersion,
        long started,
        StorageIpcFailure? failure = null)
    {
        var elapsed = ElapsedMilliseconds(started);
        var snapshot = failure is null
            ? new ConnectionHealthSnapshot(
                ConnectionHealthState.Healthy,
                _timeProvider.GetUtcNow().ToUniversalTime(),
                elapsed,
                "Connection healthy")
            : new ConnectionHealthSnapshot(
                failure.Category is StorageIpcFailureCategory.Unauthorized or StorageIpcFailureCategory.Security
                    ? ConnectionHealthState.NeedsAttention
                    : ConnectionHealthState.Unavailable,
                _timeProvider.GetUtcNow().ToUniversalTime(),
                elapsed,
                failure.Message,
                RequiresCredentialAction: failure.Category == StorageIpcFailureCategory.Unauthorized,
                RequiresTrustAction: failure.Category == StorageIpcFailureCategory.Security);
        CacheHealth(profile, snapshot);
        return AgentIpcCommandResponse.Create(
            StorageIpcMessageTypes.ConnectionTestResponse,
            new ConnectionTestResponse(
                contractVersion,
                profile.Id.Value,
                Succeeded: failure is null,
                elapsed,
                failure));
    }

    private void CacheHealth(ConnectionProfile profile, ConnectionHealthSnapshot snapshot)
    {
        lock (_healthGate)
        {
            while (_health.Count >= MaximumCachedHealthSnapshots &&
                !_health.ContainsKey(profile.Id.Value))
            {
                var oldest = _health.OrderBy(static item => item.Value.Snapshot.CheckedUtc).FirstOrDefault();
                if (oldest.Key == Guid.Empty || !_health.TryRemove(oldest.Key, out _))
                {
                    break;
                }
            }

            _health[profile.Id.Value] = new CachedConnectionHealth(profile.Version, snapshot);
        }
    }

    private static AgentIpcCommandResponse StorageListFailure(
        StorageListPageRequest request,
        StorageIpcFailure failure) => AgentIpcCommandResponse.Create(
        StorageIpcMessageTypes.StorageListResponse,
        new StorageListPageResponse(
            request.ContractVersion,
            request.ConnectionId,
            request.RelativePath ?? string.Empty,
            [],
            ContinuationToken: null,
            failure));

    private static long ElapsedMilliseconds(long started)
    {
        var milliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return (long)Math.Clamp(milliseconds, 0, long.MaxValue);
    }

    private static CodeLogicStorageIpcSessionOpener CreateCodeLogicSessionOpener(
        SqliteDatabaseOptions databaseOptions,
        Func<ISecretVault> vaultProvider,
        IRuntimeSecretFileMaterializer secretFileMaterializer,
        Func<CodeLogicStorageSessionFactory> sessionFactoryProvider,
        TimeProvider? timeProvider)
    {
        ArgumentNullException.ThrowIfNull(databaseOptions);
        ArgumentNullException.ThrowIfNull(vaultProvider);
        ArgumentNullException.ThrowIfNull(secretFileMaterializer);
        ArgumentNullException.ThrowIfNull(sessionFactoryProvider);
        var trustStore = new SqliteTrustStore(new SingleWriterSqliteDatabase(databaseOptions));
        var profiles = new SqliteConnectionProfileRepository(databaseOptions, timeProvider);
        CodeLogicConnectionProfileConnector Create(ITrustStore trust) => new(
            sessionFactoryProvider(),
            vaultProvider(),
            trust,
            secretFileMaterializer,
            timeProvider,
            profileLookup: (id, cancellationToken) => profiles.GetAsync(id, cancellationToken: cancellationToken));

        // Shared, so the registration a listing opens is still there for the next page of it.
        return new CodeLogicStorageIpcSessionOpener(
            new SharedConnectionProfileConnector(() => Create(trustStore)),
            typed => Create(typed is null ? trustStore : new DraftTrustStore(trustStore, typed)));
    }

    private sealed record CachedConnectionHealth(
        long ProfileVersion,
        ConnectionHealthSnapshot Snapshot);

    private sealed class CodeLogicStorageIpcSessionOpener(
        SharedConnectionProfileConnector connector,
        Func<TrustRecord?, CodeLogicConnectionProfileConnector> createTestConnector)
        : IStorageIpcSessionOpener, IAsyncDisposable
    {
        private readonly SharedConnectionProfileConnector _connector =
            connector ?? throw new ArgumentNullException(nameof(connector));
        private readonly Func<TrustRecord?, CodeLogicConnectionProfileConnector> _createTestConnector =
            createTestConnector ?? throw new ArgumentNullException(nameof(createTestConnector));

        /// <summary>
        /// On a connector of its own, so the test's registration, credentials and typed trust go
        /// when its lease does rather than waiting in the shared one for an idle timeout.
        /// </summary>
        public async ValueTask<StorageResult<IStorageIpcSessionLease>> OpenForTestAsync(
            ConnectionProfile profile,
            TrustRecord? typedIdentity,
            CancellationToken cancellationToken = default)
        {
            var testConnector = _createTestConnector(typedIdentity);
            try
            {
                var opened = await testConnector.OpenAsync(profile, cancellationToken).ConfigureAwait(false);
                if (opened.IsFailure)
                {
                    await testConnector.DisposeAsync().ConfigureAwait(false);
                    return StorageResult<IStorageIpcSessionLease>.Fail(opened.Error);
                }

                return StorageResult<IStorageIpcSessionLease>.Success(
                    new RuntimeSessionLease(opened.Value, testConnector));
            }
            catch
            {
                await testConnector.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        public ValueTask DisposeAsync() => _connector.DisposeAsync();

        public async ValueTask<StorageResult<IStorageIpcSessionLease>> OpenAsync(
            ConnectionProfile profile,
            CancellationToken cancellationToken = default)
        {
            var opened = await _connector.Get().OpenAsync(profile, cancellationToken).ConfigureAwait(false);
            return opened.IsSuccess
                ? StorageResult<IStorageIpcSessionLease>.Success(new RuntimeSessionLease(opened.Value))
                : StorageResult<IStorageIpcSessionLease>.Fail(opened.Error);
        }
    }

    /// <param name="owner">The connector to close with it, for one opened only for this lease.</param>
    private sealed class RuntimeSessionLease(
        RuntimeStorageConnection connection,
        CodeLogicConnectionProfileConnector? owner = null) : IStorageIpcSessionLease
    {
        private readonly RuntimeStorageConnection _connection =
            connection ?? throw new ArgumentNullException(nameof(connection));

        public IStorageEndpointSession Session => _connection.Session;

        public ValueTask<StorageResult<StorageConnectionDetails>> GetDetailsAsync(
            CancellationToken cancellationToken = default) => _connection.GetDetailsAsync(cancellationToken);

        public async ValueTask DisposeAsync()
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            if (owner is not null)
            {
                await owner.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
