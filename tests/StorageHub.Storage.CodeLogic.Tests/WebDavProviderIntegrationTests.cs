using System.Security.Cryptography;
using System.Text;
using CL.Storage;
using CL.Storage.Configuration;
using StorageHub.Application.Connections;
using StorageHub.Contracts.Results;
using StorageHub.Domain.Identifiers;
using StorageHub.Domain.Storage;
using StorageHub.Security;
using StorageHub.Storage.Models;

namespace StorageHub.Storage.CodeLogic.Tests;

/// <summary>
/// WebDAV against a real server: the lab's <c>webdav</c> (plain HTTP) and <c>webdav-tls</c> (HTTPS
/// with the lab's certificate), or anything else the STORAGEHUB_WEBDAV_* variables point at.
/// </summary>
/// <remarks>
/// The plain endpoint gets the conformance pass; the TLS one is where pinning is proved, both ways
/// round, as the FTPS fixture proves it. One pass goes through StorageHub's own connector from a
/// saved profile, with the password in a vault and the pin in a trust store, so what is tested is
/// the path a real connection takes and not only CL.Storage's configuration.
/// </remarks>
[Collection(ProviderIntegrationFixtureGroup.Name)]
public sealed class WebDavProviderIntegrationTests : IAsyncLifetime
{
    private WebDavFixtureSettings? _settings;
    private StorageLibrary? _library;
    private string? _testRoot;

    // The plain and TLS servers may share one folder, and a lab is reused between runs, so every
    // phase writes below its own run folder instead of colliding with what an earlier one left.
    private readonly string _runFolder = $"mounted/run-{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        _settings = WebDavFixtureSettings.Load();
        if (_settings is null)
        {
            return;
        }

        _testRoot = Path.Combine(Path.GetTempPath(), $"storagehub-webdav-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(_testRoot);
        _library = await CodeLogicTestFramework.EnsureStartedAsync();
    }

    [Fact]
    [Trait("Category", "WebDavProviderIntegration")]
    public async Task WebDavEndpoints_ConformAndFailClosedOnTrustAndCredentials()
    {
        if (_settings is null)
        {
            return;
        }

        await CreateRunFoldersAsync(_settings);
        await AssertPlainHttpConformanceAsync(_settings);
        await AssertPinnedTlsConformanceAsync(_settings);
        await AssertSavedProfileConnectsThroughTheConnectorAsync(_settings);
        await AssertHostileFailuresAsync(_settings);
    }

    public Task DisposeAsync()
    {
        if (_testRoot is not null && Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }

        return Task.CompletedTask;
    }

    /// <summary>A WebDAV root has to exist, as a mount point does, so the run's folders are made first.</summary>
    private async Task CreateRunFoldersAsync(WebDavFixtureSettings settings)
    {
        var profileId = ConnectionProfileId.New();
        var rootIdentity = $"webdav-setup-{Guid.NewGuid():N}";
        await using var connection = await RegisterRequiredAsync(
            profileId,
            rootIdentity,
            CreateConfiguration(settings, settings.PlainUrl));
        var run = _runFolder["mounted/".Length..];
        foreach (var path in new[] { run, $"{run}/plain", $"{run}/tls", $"{run}/connector" })
        {
            var address = StorageAddress.Create(profileId, rootIdentity, path);
            Assert.True(address.IsSuccess, Failure(address.Error));
            var created = await connection.Session.CreateDirectoryAsync(address.Value);
            Assert.True(created.IsSuccess, Failure(created.Error));
        }
    }

    private async Task AssertPlainHttpConformanceAsync(WebDavFixtureSettings settings)
    {
        var profileId = ConnectionProfileId.New();
        var rootIdentity = $"webdav-root-{Guid.NewGuid():N}";
        await using var connection = await RegisterRequiredAsync(
            profileId,
            rootIdentity,
            CreateConfiguration(settings, settings.PlainUrl, root: $"{_runFolder}/plain"));

        await ProviderSessionConformance.AssertBoundedRoundTripAsync(
            connection.Session,
            profileId,
            rootIdentity);
        await ProviderSessionConformance.AssertCreateNewCollisionPreservesOriginalAsync(
            connection.Session,
            profileId,
            rootIdentity);
        await ProviderSessionConformance.AssertAbortDoesNotPublishAsync(
            connection.Session,
            profileId,
            rootIdentity);
        await ProviderSessionConformance.AssertAddressSubstitutionFailsBeforeProviderIoAsync(
            connection.Session,
            profileId,
            rootIdentity);
        await ProviderSessionConformance.AssertTransfersToAndFromLocalAsync(
            new CodeLogicStorageSessionFactory(Assert.IsType<StorageLibrary>(_library)),
            connection.Session,
            profileId,
            rootIdentity,
            Assert.IsType<string>(_testRoot));
    }

    private async Task AssertPinnedTlsConformanceAsync(WebDavFixtureSettings settings)
    {
        var profileId = ConnectionProfileId.New();
        var rootIdentity = $"webdavs-root-{Guid.NewGuid():N}";
        await using (var connection = await RegisterRequiredAsync(
            profileId,
            rootIdentity,
            CreateConfiguration(settings, settings.TlsUrl, [settings.ServerFingerprint], root: $"{_runFolder}/tls")))
        {
            await ProviderSessionConformance.AssertBoundedRoundTripAsync(
                connection.Session,
                profileId,
                rootIdentity);
        }

        var wrongPin = new string('A', 64);
        if (StringComparer.OrdinalIgnoreCase.Equals(wrongPin, settings.ServerFingerprint))
        {
            wrongPin = new string('B', 64);
        }

        var pinFailure = await RejectAsync(CreateConfiguration(settings, settings.TlsUrl, [wrongPin]));
        Assert.Equal(StorageFailureKind.Security, pinFailure.Kind);
        Assert.Equal("storage.tls_failure", pinFailure.Code);

        // The lab's CA is not one this machine trusts, so without the pin the certificate is refused.
        var systemTrustFailure = await RejectAsync(CreateConfiguration(settings, settings.TlsUrl));
        Assert.Equal(StorageFailureKind.Security, systemTrustFailure.Kind);
        Assert.Equal("storage.tls_failure", systemTrustFailure.Code);
    }

    private async Task AssertSavedProfileConnectsThroughTheConnectorAsync(WebDavFixtureSettings settings)
    {
        var vaultRoot = Path.Combine(Assert.IsType<string>(_testRoot), "vault");
        using var vault = new VersionedFileSecretVault(vaultRoot, new FixtureSecretProtector());
        var password = (await vault.CreateAsync(Encoding.UTF8.GetBytes(settings.Password))).Reference;
        var endpoint = new WebDavEndpoint(
            settings.TlsUrl,
            TlsCertificatePolicy.Pinned,
            credentialScheme: HttpCredentialScheme.Basic,
            rootPath: $"{_runFolder}/connector");
        var trust = new FixtureTrustStore(new TrustRecord(
            $"trust-{Guid.NewGuid():N}",
            TrustArtifactKind.TlsCertificate,
            endpoint.Host,
            endpoint.Port,
            "SHA256",
            settings.ServerFingerprint,
            TrustDecision.Trusted,
            TrustDecisionSource.UserVerified,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            null,
            1));
        var profile = ConnectionProfile.Create(
            ConnectionProfileId.New(),
            new ConnectionProfileMetadata("Lab WebDAV"),
            endpoint,
            new UsernamePasswordAuthentication(settings.Username, password),
            new ConnectionOperationalOptions(
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(60),
                new ConnectionRetryPolicy(0, TimeSpan.Zero, TimeSpan.Zero),
                proxy: null,
                new ConnectionBandwidthLimits(null, null),
                "utf-8"),
            DateTimeOffset.UtcNow);

        await using var connector = new CodeLogicConnectionProfileConnector(
            new CodeLogicStorageSessionFactory(Assert.IsType<StorageLibrary>(_library)),
            vault,
            trust,
            new NoSecretFiles());
        var opened = await connector.OpenAsync(profile);
        Assert.True(opened.IsSuccess, Failure(opened.Error));
        await using var connection = opened.Value;
        var session = connection.Session;
        await ProviderSessionConformance.AssertBoundedRoundTripAsync(
            session,
            session.ProfileId,
            session.RootIdentity);
    }

    private async Task AssertHostileFailuresAsync(WebDavFixtureSettings settings)
    {
        var wrongPassword = "invalid-" + Guid.NewGuid().ToString("N");
        var wrongPasswordFailure = await RejectAsync(CreateConfiguration(
            settings,
            settings.PlainUrl,
            password: wrongPassword));
        Assert.Equal(StorageFailureKind.Unauthorized, wrongPasswordFailure.Kind);
        AssertDoesNotDisclose(wrongPasswordFailure, settings, wrongPassword);

        // Plain HTTP that nobody acknowledged is refused before anything is sent.
        var unacknowledged = CreateConfiguration(settings, settings.PlainUrl);
        unacknowledged.AllowInsecureHttp = false;
        var registration = await new CodeLogicStorageSessionFactory(Assert.IsType<StorageLibrary>(_library))
            .RegisterAsync(ConnectionProfileId.New(), $"refused-webdav-{Guid.NewGuid():N}", unacknowledged);
        Assert.True(registration.IsFailure);
        AssertDoesNotDisclose(registration.Error, settings);

        // TLS spoken to the plain-HTTP port fails as a TLS failure, not as anything that could be retried in clear.
        var tlsAgainstPlain = await RejectAsync(CreateConfiguration(
            settings,
            new UriBuilder(settings.PlainUrl) { Scheme = Uri.UriSchemeHttps, Port = settings.PlainUrl.Port }.Uri,
            [settings.ServerFingerprint]));
        AssertDoesNotDisclose(tlsAgainstPlain, settings);
    }

    private async Task<RuntimeStorageConnection> RegisterRequiredAsync(
        ConnectionProfileId profileId,
        string rootIdentity,
        WebDavConnectionConfig configuration)
    {
        var factory = new CodeLogicStorageSessionFactory(Assert.IsType<StorageLibrary>(_library));
        var registration = await factory.RegisterAsync(profileId, rootIdentity, configuration);
        Assert.True(registration.IsSuccess, Failure(registration.Error));
        return registration.Value;
    }

    private async Task<StorageFailure> RejectAsync(WebDavConnectionConfig configuration)
    {
        var factory = new CodeLogicStorageSessionFactory(Assert.IsType<StorageLibrary>(_library));
        var registration = await factory.RegisterAsync(
            ConnectionProfileId.New(),
            $"rejected-webdav-{Guid.NewGuid():N}",
            configuration);
        if (registration.IsFailure)
        {
            AssertDoesNotDisclose(registration.Error, Assert.IsType<WebDavFixtureSettings>(_settings));
            return registration.Error;
        }

        await using var connection = registration.Value;
        var health = await connection.Session.CheckHealthAsync();
        Assert.True(health.IsFailure);
        AssertDoesNotDisclose(health.Error, Assert.IsType<WebDavFixtureSettings>(_settings));
        return health.Error;
    }

    private static WebDavConnectionConfig CreateConfiguration(
        WebDavFixtureSettings settings,
        Uri url,
        List<string>? fingerprints = null,
        string? password = null,
        string root = "mounted") => new()
        {
            Enabled = true,
            Endpoint = url.AbsoluteUri,
            Root = root,
            AllowInsecureHttp = url.Scheme == Uri.UriSchemeHttp,
            AuthenticationMode = WebDavAuthenticationMode.Basic,
            Username = settings.Username,
            Password = password ?? settings.Password,
            TrustedCertificateSha256 = fingerprints ?? [],
            TimeoutSeconds = 15
        };

    private static void AssertDoesNotDisclose(
        StorageFailure failure,
        WebDavFixtureSettings settings,
        params string[] additionalSecrets)
    {
        var disclosed = string.Join(
            "|",
            failure.Code,
            failure.Message,
            failure.ProviderCode,
            failure.DiagnosticId);
        foreach (var secret in new[] { settings.Password }.Concat(additionalSecrets))
        {
            Assert.False(disclosed.Contains(secret, StringComparison.Ordinal));
        }
    }

    private static string Failure(StorageFailure? failure) => failure is null
        ? "The operation failed without a structured failure."
        : $"{failure.Code}: {failure.Message}";

    private sealed record WebDavFixtureSettings(
        Uri PlainUrl,
        Uri TlsUrl,
        string Username,
        string Password,
        string ServerFingerprint)
    {
        public static WebDavFixtureSettings? Load()
        {
            var required = string.Equals(
                Environment.GetEnvironmentVariable("STORAGEHUB_REQUIRE_WEBDAV"),
                "1",
                StringComparison.Ordinal);
            var values = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["URL"] = Environment.GetEnvironmentVariable("STORAGEHUB_WEBDAV_URL"),
                ["TLS URL"] = Environment.GetEnvironmentVariable("STORAGEHUB_WEBDAV_TLS_URL"),
                ["username"] = Environment.GetEnvironmentVariable("STORAGEHUB_WEBDAV_USERNAME"),
                ["password"] = Environment.GetEnvironmentVariable("STORAGEHUB_WEBDAV_PASSWORD"),
                ["server fingerprint"] = Environment.GetEnvironmentVariable("STORAGEHUB_WEBDAV_SERVER_SHA256")
            };
            if (!required && values.Values.All(value => value is null))
            {
                return null;
            }

            var plain = RequiredUrl(values["URL"], "URL", Uri.UriSchemeHttp);
            var tls = RequiredUrl(values["TLS URL"], "TLS URL", Uri.UriSchemeHttps);
            var fingerprint = values["server fingerprint"];
            if (fingerprint is null ||
                fingerprint.Length != 64 ||
                fingerprint.Any(character => !Uri.IsHexDigit(character)))
            {
                throw new InvalidOperationException("The WebDAV fixture server fingerprint is missing or invalid.");
            }

            return new WebDavFixtureSettings(
                plain,
                tls,
                RequiredBounded(values["username"], "username"),
                RequiredBounded(values["password"], "password"),
                fingerprint.ToUpperInvariant());
        }

        private static Uri RequiredUrl(string? value, string description, string scheme) =>
            Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == scheme && url.Host.Length > 0
                ? url
                : throw new InvalidOperationException($"The WebDAV fixture {description} is missing or not {scheme}.");

        private static string RequiredBounded(string? value, string description)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl))
            {
                throw new InvalidOperationException($"The WebDAV fixture {description} is missing or invalid.");
            }

            return value;
        }
    }

    private sealed class FixtureTrustStore(TrustRecord record) : ITrustStore
    {
        public ValueTask<IReadOnlyList<TrustRecord>> FindAsync(
            TrustArtifactKind artifactKind,
            string canonicalHost,
            int port,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<TrustRecord> found = record.ArtifactKind == artifactKind &&
                string.Equals(record.CanonicalHost, canonicalHost, StringComparison.OrdinalIgnoreCase) &&
                record.Port == port
                    ? [record]
                    : [];
            return ValueTask.FromResult(found);
        }

        public ValueTask UpsertAsync(TrustRecord record, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<bool> RemoveAsync(string trustId, int expectedVersion, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>WebDAV takes its client certificate as bytes, so a file being asked for is a failure.</summary>
    private sealed class NoSecretFiles : IRuntimeSecretFileMaterializer
    {
        public ValueTask<IRuntimeSecretFile> MaterializeAsync(
            ReadOnlyMemory<byte> secret,
            string fileExtension,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("WebDAV must not write a secret to disk.");

        public int ScavengeOrphans(TimeSpan minimumAge) => 0;
    }

    private sealed class FixtureSecretProtector : ISecretProtector
    {
        private static readonly byte[] Key = SHA256.HashData("storagehub-webdav-fixture"u8.ToArray());

        public string Scheme => "test-xor-v1";

        public byte[] Protect(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> entropy) => Transform(plaintext);

        public byte[] Unprotect(ReadOnlySpan<byte> protectedData, ReadOnlySpan<byte> entropy) => Transform(protectedData);

        private static byte[] Transform(ReadOnlySpan<byte> input)
        {
            var output = new byte[input.Length];
            for (var index = 0; index < input.Length; index++)
            {
                output[index] = (byte)(input[index] ^ Key[index % Key.Length]);
            }

            return output;
        }
    }
}
