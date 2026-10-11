using System.Text.Json;
using StorageHub.Agent.Host;
using StorageHub.Ipc;
using StorageHub.Application.Connections;
using StorageHub.Contracts.Ipc;
using StorageHub.Contracts.Results;
using StorageHub.Domain.Capabilities;
using StorageHub.Domain.Identifiers;
using StorageHub.Domain.Storage;
using StorageHub.Security;
using StorageHub.Storage.Abstractions;
using StorageHub.Storage.CodeLogic;
using StorageHub.Storage.Models;

namespace StorageHub.Agent.Windows.Tests;

/// <summary>
/// Testing a connection as it stands in the editor: each step is reported, a typed fingerprint is
/// trusted for the test only, and nothing is saved.
/// </summary>
public sealed class StorageIpcDraftTestTests
{
    private const string Password = "shs_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Fingerprint = "SHA256:47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU";

    [Fact]
    public async Task ADraftThatIsReachedPassesEveryStepItHas()
    {
        var opener = new DraftOpener();
        var repository = new EmptyRepository();
        var service = new StorageIpcCommandService(repository, opener);

        var response = await TestAsync(service, LocalDraft());

        Assert.True(response.Succeeded);
        Assert.Null(response.Failure);
        Assert.Equal(
            [
                (ConnectionTestStepKind.Settings, ConnectionTestStepOutcome.Passed),
                (ConnectionTestStepKind.ServerIdentity, ConnectionTestStepOutcome.NotApplicable),
                (ConnectionTestStepKind.Credentials, ConnectionTestStepOutcome.NotApplicable),
                (ConnectionTestStepKind.Connect, ConnectionTestStepOutcome.Passed),
                (ConnectionTestStepKind.Browse, ConnectionTestStepOutcome.Passed)
            ],
            Outcomes(response));
        Assert.Equal(1, opener.Session?.ListCount);
        Assert.True(opener.Lease?.IsDisposed);
        Assert.Null(opener.TypedIdentity);
        Assert.Equal(0, repository.Reads);
    }

    [Fact]
    public async Task AFingerprintInTheEditorIsTrustedForTheTestAsSavingWouldPinIt()
    {
        var opener = new DraftOpener();
        var service = new StorageIpcCommandService(new EmptyRepository(), opener);

        var response = await TestAsync(service, SftpDraft(), Fingerprint);

        Assert.True(response.Succeeded);
        var identity = Assert.IsType<TrustRecord>(opener.TypedIdentity);
        Assert.Equal(TrustArtifactKind.SshHostKey, identity.ArtifactKind);
        Assert.Equal("sftp.example.test", identity.CanonicalHost);
        Assert.Equal(2222, identity.Port);
        Assert.Equal(Fingerprint, identity.Sha256Fingerprint);
        Assert.Equal(TrustDecision.Trusted, identity.Decision);
        Assert.Equal(ConnectionTestStepOutcome.Passed, Outcome(response, ConnectionTestStepKind.ServerIdentity));
        Assert.Equal(ConnectionTestStepOutcome.Passed, Outcome(response, ConnectionTestStepKind.Credentials));
    }

    [Fact]
    public async Task EachFailureIsPutAtTheStepItBelongsToAndLaterStepsAreNotReached()
    {
        var cases = new (string Code, StorageFailureKind Kind, bool AtHealth, ConnectionTestStepKind Step)[]
        {
            ("storage.trust.approval_required", StorageFailureKind.Security, false, ConnectionTestStepKind.ServerIdentity),
            ("storage.credential.unavailable", StorageFailureKind.Security, false, ConnectionTestStepKind.Credentials),
            ("storage.timeout.unsupported", StorageFailureKind.Unsupported, false, ConnectionTestStepKind.Settings),
            ("storage.provider.unreachable", StorageFailureKind.Unavailable, false, ConnectionTestStepKind.Connect),
            ("storage.provider.host_key", StorageFailureKind.Security, true, ConnectionTestStepKind.ServerIdentity),
            ("storage.provider.denied", StorageFailureKind.Unauthorized, true, ConnectionTestStepKind.Credentials),
            ("storage.provider.timeout", StorageFailureKind.Timeout, true, ConnectionTestStepKind.Connect)
        };

        foreach (var (code, kind, atHealth, step) in cases)
        {
            var failure = new StorageFailure(code, kind, "password=hunter2 was refused");
            var opener = atHealth ? new DraftOpener(healthFailure: failure) : new DraftOpener(openFailure: failure);
            var service = new StorageIpcCommandService(new EmptyRepository(), opener);

            var response = await TestAsync(service, SftpDraft(), Fingerprint);

            Assert.False(response.Succeeded);
            Assert.Equal(code, response.Failure?.Code);
            Assert.DoesNotContain("hunter2", JsonSerializer.Serialize(response), StringComparison.Ordinal);
            var failed = Assert.Single(response.Steps, candidate => candidate.Outcome == ConnectionTestStepOutcome.Failed);
            Assert.Equal(step, failed.Kind);
            Assert.Equal(code, failed.Failure?.Code);
            Assert.All(
                response.Steps.SkipWhile(candidate => candidate.Kind != step).Skip(1),
                later => Assert.Equal(ConnectionTestStepOutcome.NotReached, later.Outcome));
            Assert.All(
                response.Steps.TakeWhile(candidate => candidate.Kind != step),
                earlier => Assert.Contains(
                    earlier.Outcome,
                    new[] { ConnectionTestStepOutcome.Passed, ConnectionTestStepOutcome.NotApplicable }));
        }
    }

    [Fact]
    public async Task AFolderThatCannotBeListedFailsOnlyTheLastStep()
    {
        var opener = new DraftOpener(listFailure: new StorageFailure(
            "storage.list.denied", StorageFailureKind.Unauthorized, "Denied."));
        var service = new StorageIpcCommandService(new EmptyRepository(), opener);

        var response = await TestAsync(service, LocalDraft());

        Assert.False(response.Succeeded);
        Assert.Equal(ConnectionTestStepOutcome.Passed, Outcome(response, ConnectionTestStepKind.Connect));
        Assert.Equal(ConnectionTestStepOutcome.Failed, Outcome(response, ConnectionTestStepKind.Browse));
    }

    [Fact]
    public async Task ADisabledDraftIsStillTestedAndAnInvalidRequestIsRefused()
    {
        var opener = new DraftOpener();
        var service = new StorageIpcCommandService(new EmptyRepository(), opener);

        var response = await TestAsync(service, LocalDraft() with { IsEnabled = false });
        Assert.True(response.Succeeded);
        Assert.True(opener.Profile?.IsEnabled);

        var refused = await service.HandleAsync(IpcEnvelope.Create(
            StorageIpcMessageTypes.ConnectionDraftTestRequest,
            Guid.NewGuid(),
            1,
            new ConnectionDraftTestRequest(StorageIpcContract.CurrentVersion, LocalDraft(), Sha256Fingerprint: "not a key")));
        Assert.Equal(IpcProtocol.ErrorResponseMessageType, refused.MessageType);
        Assert.Equal("ipc.payload.invalid", refused.Payload.Deserialize<IpcErrorResponse>()?.Code);
    }

    private static async Task<ConnectionDraftTestResponse> TestAsync(
        StorageIpcCommandService service,
        ConnectionProfileDraft draft,
        string? fingerprint = null)
    {
        var response = await service.HandleAsync(IpcEnvelope.Create(
            StorageIpcMessageTypes.ConnectionDraftTestRequest,
            Guid.NewGuid(),
            1,
            new ConnectionDraftTestRequest(StorageIpcContract.CurrentVersion, draft, Sha256Fingerprint: fingerprint)));
        Assert.Equal(StorageIpcMessageTypes.ConnectionDraftTestResponse, response.MessageType);
        return Assert.IsType<ConnectionDraftTestResponse>(
            response.Payload.Deserialize<ConnectionDraftTestResponse>());
    }

    private static (ConnectionTestStepKind, ConnectionTestStepOutcome)[] Outcomes(ConnectionDraftTestResponse response) =>
        [.. response.Steps.Select(static step => (step.Kind, step.Outcome))];

    private static ConnectionTestStepOutcome Outcome(ConnectionDraftTestResponse response, ConnectionTestStepKind kind) =>
        response.Steps.Single(step => step.Kind == kind).Outcome;

    private static ConnectionProfileDraft LocalDraft() => new(
        new ConnectionProfileMetadataDocument("Local", Tags: []),
        new ConnectionEndpointDocument(StorageConnectionProvider.Local, RootPath: Path.GetTempPath()),
        new ConnectionAuthenticationDocument(ConnectionAuthenticationKind.None),
        new ConnectionOperationalOptionsDocument());

    private static ConnectionProfileDraft SftpDraft() => new(
        new ConnectionProfileMetadataDocument("Files", Tags: []),
        new ConnectionEndpointDocument(
            StorageConnectionProvider.Sftp,
            Host: "sftp.example.test",
            Port: 2222,
            SshHostKeyPolicy: ConnectionSshHostKeyPolicy.Pinned),
        new ConnectionAuthenticationDocument(
            ConnectionAuthenticationKind.UsernamePassword,
            Username: "operator",
            PasswordReference: Password),
        new ConnectionOperationalOptionsDocument());

    private sealed class DraftOpener(
        StorageFailure? openFailure = null,
        StorageFailure? healthFailure = null,
        StorageFailure? listFailure = null) : IStorageIpcSessionOpener
    {
        public ConnectionProfile? Profile { get; private set; }
        public TrustRecord? TypedIdentity { get; private set; }
        public DraftSession? Session { get; private set; }
        public DraftLease? Lease { get; private set; }

        public ValueTask<StorageResult<IStorageIpcSessionLease>> OpenAsync(
            ConnectionProfile profile,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A draft is never opened as a saved connection.");

        public ValueTask<StorageResult<IStorageIpcSessionLease>> OpenForTestAsync(
            ConnectionProfile profile,
            TrustRecord? typedIdentity,
            CancellationToken cancellationToken = default)
        {
            Profile = profile;
            TypedIdentity = typedIdentity;
            if (openFailure is not null)
            {
                return ValueTask.FromResult(StorageResult<IStorageIpcSessionLease>.Fail(openFailure));
            }

            Session = new DraftSession(profile.Id, healthFailure, listFailure);
            Lease = new DraftLease(Session);
            return ValueTask.FromResult(StorageResult<IStorageIpcSessionLease>.Success(Lease));
        }
    }

    private sealed class DraftLease(IStorageEndpointSession session) : IStorageIpcSessionLease
    {
        public IStorageEndpointSession Session { get; } = session;
        public bool IsDisposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DraftSession(
        ConnectionProfileId profileId,
        StorageFailure? healthFailure,
        StorageFailure? listFailure) : IStorageEndpointSession
    {
        public ConnectionProfileId ProfileId { get; } = profileId;
        public string RootIdentity => "root-draft";
        public EffectiveStorageCapabilities Capabilities => EffectiveStorageCapabilities.None;
        public int ListCount { get; private set; }

        public ValueTask<StorageResult> CheckHealthAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(healthFailure is null ? StorageResult.Success() : StorageResult.Fail(healthFailure));

        public ValueTask<StorageResult<StoragePage>> ListAsync(
            StorageAddress address,
            StorageListRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            ListCount++;
            Assert.Equal(1, request?.PageSize);
            return ValueTask.FromResult(listFailure is null
                ? StorageResult<StoragePage>.Success(new StoragePage([]))
                : StorageResult<StoragePage>.Fail(listFailure));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public ValueTask<StorageResult<StorageEntry>> GetEntryAsync(
            StorageAddress address,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<StorageResult<Stream>> OpenReadAsync(
            StorageReadRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<StorageResult<IStorageWriteHandle>> OpenWriteAsync(
            StorageWriteRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<StorageResult<StorageEntry>> CreateDirectoryAsync(
            StorageAddress address,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<StorageResult> DeleteAsync(
            StorageDeleteRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<StorageResult<StorageEntry>> CopyAsync(
            StorageCopyRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<StorageResult<StorageEntry>> MoveAsync(
            StorageMoveRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>Holds nothing: a test of a draft reads no saved connection.</summary>
    private sealed class EmptyRepository : IConnectionProfileRepository
    {
        public int Reads { get; private set; }

        public ValueTask<ConnectionProfile?> GetAsync(
            ConnectionProfileId id,
            bool includeDeleted = false,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            return ValueTask.FromResult<ConnectionProfile?>(null);
        }

        public ValueTask<IReadOnlyList<ConnectionProfile>> SearchAsync(
            ConnectionProfileSearch search,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            return ValueTask.FromResult<IReadOnlyList<ConnectionProfile>>([]);
        }

        public ValueTask<ConnectionProfileWriteResult> CreateAsync(
            ConnectionProfile profile,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<ConnectionProfileWriteResult> UpdateAsync(
            ConnectionProfile profile,
            long expectedVersion,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<ConnectionProfileWriteResult> SetEnabledAsync(
            ConnectionProfileId id,
            bool enabled,
            long expectedVersion,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<ConnectionProfileWriteResult> SoftDeleteAsync(
            ConnectionProfileId id,
            long expectedVersion,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

/// <summary>Describing a saved connection: what the server says is cut to the contract, never refused.</summary>
public sealed class StorageIpcConnectionDetailsTests
{
    [Fact]
    public async Task ASavedConnectionIsDescribedWithTheServersTextMadeSafe()
    {
        var profile = ConnectionProfile.Create(
            ConnectionProfileId.New(),
            new ConnectionProfileMetadata("Local"),
            new LocalEndpoint(Path.GetTempPath()),
            new NoAuthentication(),
            new ConnectionOperationalOptions(
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(15),
                new ConnectionRetryPolicy(0, TimeSpan.Zero, TimeSpan.Zero),
                proxy: null,
                new ConnectionBandwidthLimits(null, null),
                "utf-8"),
            DateTimeOffset.UnixEpoch);
        var details = new StorageConnectionDetails(
            "Sftp",
            "files.example.test",
            22,
            "Ssh",
            ServerSystem: null,
            ServerSoftware: "OpenSSH_9.6\r\nInjected" + new string('x', 400),
            ServerFeatures: [.. Enumerable.Range(0, 100).Select(static index => $"feature-{index}"), "  "],
            Negotiated: [new("cipher", "aes256-gcm@openssh.com"), new("\u0007", "dropped")],
            new StoragePresentedIdentity("ssh-host-key", "SHA256:abc", "ssh-ed25519", null, null, null, Trusted: true),
            new StorageSessionCounts(1, 2, 3, 0, 0),
            new StorageHealthCheck(true, null, TimeSpan.FromMilliseconds(42), DateTimeOffset.UnixEpoch));
        var opener = new DescribingOpener(StorageResult<StorageConnectionDetails>.Success(details));
        var service = new StorageIpcCommandService(new SavedRepository(profile), opener);

        var response = await DescribeAsync(service, profile.Id.Value);

        Assert.Null(response.Failure);
        var document = Assert.IsType<ConnectionDetailsDocument>(response.Details);
        Assert.True(document.HasValidBounds);
        Assert.StartsWith("OpenSSH_9.6Injected", document.ServerSoftware, StringComparison.Ordinal);
        Assert.Equal(ConnectionDetailsLimits.MaximumTextLength, document.ServerSoftware!.Length);
        Assert.Equal(ConnectionDetailsLimits.MaximumFeatures, document.ServerFeatures.Length);
        var negotiated = Assert.Single(document.Negotiated);
        Assert.Equal(("cipher", "aes256-gcm@openssh.com"), (negotiated.Name, negotiated.Value));
        Assert.Equal("SHA256:abc", document.PresentedIdentity?.Fingerprint);
        Assert.Equal(2, document.Sessions?.InUse);
        Assert.Equal(42, document.LastHealthCheck?.LatencyMilliseconds);
        Assert.True(opener.Lease?.IsDisposed);
    }

    [Fact]
    public async Task AConnectionThatCannotBeDescribedSaysWhyAndAnUnknownOneIsNotFound()
    {
        var profile = ConnectionProfile.Create(
            ConnectionProfileId.New(),
            new ConnectionProfileMetadata("Local"),
            new LocalEndpoint(Path.GetTempPath()),
            new NoAuthentication(),
            new ConnectionOperationalOptions(
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(15),
                new ConnectionRetryPolicy(0, TimeSpan.Zero, TimeSpan.Zero),
                proxy: null,
                new ConnectionBandwidthLimits(null, null),
                "utf-8"),
            DateTimeOffset.UnixEpoch);
        var service = new StorageIpcCommandService(
            new SavedRepository(profile),
            new DescribingOpener(StorageResult<StorageConnectionDetails>.Fail(new StorageFailure(
                "storage.provider.unreachable", StorageFailureKind.Unavailable, "secret host 10.0.0.1"))));

        var failed = await DescribeAsync(service, profile.Id.Value);
        Assert.Null(failed.Details);
        Assert.Equal("storage.provider.unreachable", failed.Failure?.Code);
        Assert.DoesNotContain("10.0.0.1", failed.Failure!.Message, StringComparison.Ordinal);

        var missing = await DescribeAsync(service, Guid.NewGuid());
        Assert.Equal("storage.connection.not_found", missing.Failure?.Code);
    }

    private static async Task<ConnectionDetailsResponse> DescribeAsync(StorageIpcCommandService service, Guid id)
    {
        var response = await service.HandleAsync(IpcEnvelope.Create(
            StorageIpcMessageTypes.ConnectionDetailsRequest,
            Guid.NewGuid(),
            1,
            new ConnectionDetailsRequest(StorageIpcContract.CurrentVersion, id)));
        Assert.Equal(StorageIpcMessageTypes.ConnectionDetailsResponse, response.MessageType);
        return Assert.IsType<ConnectionDetailsResponse>(response.Payload.Deserialize<ConnectionDetailsResponse>());
    }

    private sealed class DescribingOpener(StorageResult<StorageConnectionDetails> details) : IStorageIpcSessionOpener
    {
        public DescribingLease? Lease { get; private set; }

        public ValueTask<StorageResult<IStorageIpcSessionLease>> OpenAsync(
            ConnectionProfile profile,
            CancellationToken cancellationToken = default)
        {
            Lease = new DescribingLease(details);
            return ValueTask.FromResult(StorageResult<IStorageIpcSessionLease>.Success(Lease));
        }
    }

    private sealed class DescribingLease(StorageResult<StorageConnectionDetails> details) : IStorageIpcSessionLease
    {
        public IStorageEndpointSession Session => throw new NotSupportedException();
        public bool IsDisposed { get; private set; }

        public ValueTask<StorageResult<StorageConnectionDetails>> GetDetailsAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(details);

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SavedRepository(ConnectionProfile saved) : IConnectionProfileRepository
    {
        public ValueTask<ConnectionProfile?> GetAsync(
            ConnectionProfileId id,
            bool includeDeleted = false,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(id == saved.Id ? saved : null);

        public ValueTask<IReadOnlyList<ConnectionProfile>> SearchAsync(
            ConnectionProfileSearch search,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ConnectionProfile>>([saved]);

        public ValueTask<ConnectionProfileWriteResult> CreateAsync(
            ConnectionProfile profile,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<ConnectionProfileWriteResult> UpdateAsync(
            ConnectionProfile profile,
            long expectedVersion,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<ConnectionProfileWriteResult> SetEnabledAsync(
            ConnectionProfileId id,
            bool enabled,
            long expectedVersion,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<ConnectionProfileWriteResult> SoftDeleteAsync(
            ConnectionProfileId id,
            long expectedVersion,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

/// <summary>The typed fingerprint is added to what is trusted, never over a decision already made.</summary>
public sealed class DraftTrustStoreTests
{
    private const string Typed = "SHA256:47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU";

    [Fact]
    public async Task TheTypedFingerprintIsAddedForItsOwnEndpointOnly()
    {
        var store = new DraftTrustStore(new SavedTrust(), Record("files.example.test", 22, Typed, TrustDecision.Trusted));

        Assert.Single(await store.FindAsync(TrustArtifactKind.SshHostKey, "FILES.example.test", 22));
        Assert.Empty(await store.FindAsync(TrustArtifactKind.SshHostKey, "files.example.test", 2222));
        Assert.Empty(await store.FindAsync(TrustArtifactKind.TlsCertificate, "files.example.test", 22));
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await store.UpsertAsync(Record("files.example.test", 22, Typed, TrustDecision.Trusted)));
    }

    [Fact]
    public async Task AKeyTheUserRejectedStaysRejectedWhicheverWayItIsWritten()
    {
        // The same key in hexadecimal, as the editor's fetch writes it.
        var hex = Convert.ToHexString(Convert.FromBase64String("47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU="));
        var rejected = Record("files.example.test", 22, hex, TrustDecision.Rejected);
        var store = new DraftTrustStore(
            new SavedTrust(rejected),
            Record("files.example.test", 22, Typed, TrustDecision.Trusted));

        var found = Assert.Single(await store.FindAsync(TrustArtifactKind.SshHostKey, "files.example.test", 22));
        Assert.Equal(TrustDecision.Rejected, found.Decision);
    }

    private static TrustRecord Record(string host, int port, string fingerprint, TrustDecision decision) => new(
        Guid.NewGuid().ToString("N"),
        TrustArtifactKind.SshHostKey,
        host,
        port,
        "SHA256",
        fingerprint,
        decision,
        TrustDecisionSource.UserVerified,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        ExpiresUtc: null,
        PreviousFingerprint: null,
        Version: 1);

    private sealed class SavedTrust(params TrustRecord[] records) : ITrustStore
    {
        public ValueTask<IReadOnlyList<TrustRecord>> FindAsync(
            TrustArtifactKind artifactKind,
            string canonicalHost,
            int port,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<TrustRecord>>(
                [.. records.Where(record => record.ArtifactKind == artifactKind &&
                    record.Port == port &&
                    string.Equals(record.CanonicalHost, canonicalHost, StringComparison.OrdinalIgnoreCase))]);

        public ValueTask UpsertAsync(TrustRecord record, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<bool> RemoveAsync(
            string trustId,
            int expectedVersion,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
