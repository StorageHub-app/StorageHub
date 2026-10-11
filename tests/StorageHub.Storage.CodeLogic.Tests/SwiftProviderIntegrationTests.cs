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
/// OpenStack Swift against a real Swift: the lab's all-in-one <c>swift</c> with TempAuth, or
/// anything else the STORAGEHUB_SWIFT_* variables point at.
/// </summary>
/// <remarks>
/// The lab has no Keystone, so the fixture signs in with TempAuth, and then with a token it was
/// issued, which is the pre-issued token sign-in. Both go through StorageHub's own connector from a
/// saved profile, with the password and the token in a vault, so what is tested is the path a real
/// connection takes. A run writes below its own prefix, because a reused lab still holds what the
/// last run wrote.
/// </remarks>
[Collection(ProviderIntegrationFixtureGroup.Name)]
public sealed class SwiftProviderIntegrationTests : IAsyncLifetime
{
    private SwiftFixtureSettings? _settings;
    private StorageLibrary? _library;
    private string? _testRoot;
    private readonly string _runPrefix = $"mounted/run-{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        _settings = SwiftFixtureSettings.Load();
        if (_settings is null)
        {
            return;
        }

        _testRoot = Path.Combine(Path.GetTempPath(), $"storagehub-swift-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(_testRoot);
        _library = await CodeLogicTestFramework.EnsureStartedAsync();

        // The container is the lab's to provide, as a bucket is MinIO's, but a fresh SAIO has none:
        // make it, and leave a sentinel outside the run's prefix.
        var (token, storageUrl) = await SignInAsync(_settings);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        using var container = new HttpRequestMessage(HttpMethod.Put, $"{storageUrl}/{_settings.Container}");
        container.Headers.Add("X-Auth-Token", token);
        using var made = await http.SendAsync(container);
        Assert.True(made.IsSuccessStatusCode, $"The container could not be made: {(int)made.StatusCode}.");
        using var sentinel = new HttpRequestMessage(
            HttpMethod.Put, $"{storageUrl}/{_settings.Container}/outside/sentinel.txt")
        {
            Content = new StringContent("must-not-be-mounted")
        };
        sentinel.Headers.Add("X-Auth-Token", token);
        using var written = await http.SendAsync(sentinel);
        Assert.True(written.IsSuccessStatusCode, $"The sentinel could not be written: {(int)written.StatusCode}.");
    }

    [Fact]
    [Trait("Category", "SwiftProviderIntegration")]
    public async Task SwiftContainer_ConformsAndFailsClosedOnCredentialsAndPlainHttp()
    {
        if (_settings is null)
        {
            return;
        }

        await AssertConformanceAsync(_settings);
        await AssertSavedProfilesConnectThroughTheConnectorAsync(_settings);
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

    private async Task AssertConformanceAsync(SwiftFixtureSettings settings)
    {
        var profileId = ConnectionProfileId.New();
        var rootIdentity = $"swift-root-{Guid.NewGuid():N}";
        await using var connection = await RegisterRequiredAsync(
            profileId,
            rootIdentity,
            CreateConfiguration(settings, $"{_runPrefix}/direct", settings.Password));

        await ProviderSessionConformance.AssertBoundedRoundTripAsync(connection.Session, profileId, rootIdentity);
        await ProviderSessionConformance.AssertCreateNewCollisionPreservesOriginalAsync(
            connection.Session, profileId, rootIdentity);
        await ProviderSessionConformance.AssertAbortDoesNotPublishAsync(connection.Session, profileId, rootIdentity);
        await ProviderSessionConformance.AssertAddressSubstitutionFailsBeforeProviderIoAsync(
            connection.Session, profileId, rootIdentity);
        await ProviderSessionConformance.AssertTransfersToAndFromLocalAsync(
            new CodeLogicStorageSessionFactory(Assert.IsType<StorageLibrary>(_library)),
            connection.Session,
            profileId,
            rootIdentity,
            Assert.IsType<string>(_testRoot));

        // The prefix is the root: the sentinel beside it is not reachable as "outside".
        var outside = StorageAddress.Create(profileId, rootIdentity, "outside/sentinel.txt");
        Assert.True(outside.IsSuccess, Failure(outside.Error));
        var info = await connection.Session.GetEntryAsync(outside.Value);
        Assert.True(info.IsFailure);
        Assert.Equal(StorageFailureKind.NotFound, info.Error.Kind);
    }

    private async Task AssertSavedProfilesConnectThroughTheConnectorAsync(SwiftFixtureSettings settings)
    {
        var vaultRoot = Path.Combine(Assert.IsType<string>(_testRoot), "vault");
        using var vault = new VersionedFileSecretVault(vaultRoot, new FixtureSecretProtector());
        var password = (await vault.CreateAsync(Encoding.UTF8.GetBytes(settings.Password))).Reference;
        var plainHttp = settings.AuthenticationUrl.Scheme == Uri.UriSchemeHttp;

        await using (var connector = Connector(vault))
        {
            var opened = await connector.OpenAsync(Profile(
                new SwiftEndpoint(
                    settings.Container,
                    settings.AuthenticationUrl,
                    allowInsecureHttp: plainHttp,
                    rootPrefix: $"{_runPrefix}/tempauth"),
                new UsernamePasswordAuthentication(settings.Username, password)));
            Assert.True(opened.IsSuccess, Failure(opened.Error));
            await using var connection = opened.Value;
            await ProviderSessionConformance.AssertBoundedRoundTripAsync(
                connection.Session,
                connection.Session.ProfileId,
                connection.Session.RootIdentity);
        }

        // A token issued elsewhere goes straight to the storage URL, with nowhere to sign in.
        var (issued, storageUrl) = await SignInAsync(settings);
        var token = (await vault.CreateAsync(Encoding.UTF8.GetBytes(issued))).Reference;
        await using (var connector = Connector(vault))
        {
            var opened = await connector.OpenAsync(Profile(
                new SwiftEndpoint(
                    settings.Container,
                    authenticationUrl: null,
                    storageUrl: new Uri(storageUrl),
                    allowInsecureHttp: new Uri(storageUrl).Scheme == Uri.UriSchemeHttp,
                    rootPrefix: $"{_runPrefix}/token"),
                new BearerTokenAuthentication(token)));
            Assert.True(opened.IsSuccess, Failure(opened.Error));
            await using var connection = opened.Value;
            await ProviderSessionConformance.AssertBoundedRoundTripAsync(
                connection.Session,
                connection.Session.ProfileId,
                connection.Session.RootIdentity);
        }
    }

    private async Task AssertHostileFailuresAsync(SwiftFixtureSettings settings)
    {
        // The right user with a password that is not theirs: refused as unauthorized, unrepeated.
        var wrongPassword = $"wrong-{Guid.NewGuid():N}";
        var failure = await RejectAsync(CreateConfiguration(settings, $"{_runPrefix}/refused", wrongPassword));
        Assert.Equal(StorageFailureKind.Unauthorized, failure.Kind);
        AssertDoesNotDisclose(failure, settings, wrongPassword);

        // A token nobody issued is refused at the storage URL the same way.
        var (_, storageUrl) = await SignInAsync(settings);
        var forged = $"AUTH_tk{Guid.NewGuid():N}";
        var forgedFailure = await RejectAsync(new SwiftConnectionConfig
        {
            Enabled = true,
            Container = settings.Container,
            Prefix = $"{_runPrefix}/refused",
            AuthenticationMode = SwiftAuthenticationMode.StaticToken,
            StorageUrl = storageUrl,
            Token = forged,
            AllowInsecureHttp = new Uri(storageUrl).Scheme == Uri.UriSchemeHttp,
            TimeoutSeconds = 15
        });
        Assert.Equal(StorageFailureKind.Unauthorized, forgedFailure.Kind);
        AssertDoesNotDisclose(forgedFailure, settings, forged);

        if (settings.AuthenticationUrl.Scheme == Uri.UriSchemeHttp)
        {
            // Plain HTTP nobody acknowledged: the profile cannot be made, and the library will not
            // register one either, so the password is never sent.
            Assert.Throws<ArgumentException>(() => new SwiftEndpoint(settings.Container, settings.AuthenticationUrl));
            var unacknowledged = CreateConfiguration(settings, $"{_runPrefix}/refused", settings.Password);
            unacknowledged.AllowInsecureHttp = false;
            var registration = await new CodeLogicStorageSessionFactory(Assert.IsType<StorageLibrary>(_library))
                .RegisterAsync(ConnectionProfileId.New(), $"refused-swift-{Guid.NewGuid():N}", unacknowledged);
            Assert.True(registration.IsFailure);
            AssertDoesNotDisclose(registration.Error, settings);
        }
    }

    /// <summary>A TempAuth sign-in by hand, for the token and the storage URL it returns.</summary>
    private static async Task<(string Token, string StorageUrl)> SignInAsync(SwiftFixtureSettings settings)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        using var request = new HttpRequestMessage(HttpMethod.Get, settings.AuthenticationUrl);
        request.Headers.Add("X-Auth-User", settings.Username);
        request.Headers.Add("X-Auth-Key", settings.Password);
        using var response = await http.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode, $"TempAuth refused the lab's user: {(int)response.StatusCode}.");
        return (
            response.Headers.GetValues("X-Auth-Token").Single(),
            response.Headers.GetValues("X-Storage-Url").Single().TrimEnd('/'));
    }

    private CodeLogicConnectionProfileConnector Connector(ISecretVault vault) => new(
        new CodeLogicStorageSessionFactory(Assert.IsType<StorageLibrary>(_library)),
        vault,
        new NoTrust(),
        new NoSecretFiles());

    private static ConnectionProfile Profile(SwiftEndpoint endpoint, ConnectionAuthentication authentication) =>
        ConnectionProfile.Create(
            ConnectionProfileId.New(),
            new ConnectionProfileMetadata("Lab Swift"),
            endpoint,
            authentication,
            new ConnectionOperationalOptions(
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(60),
                new ConnectionRetryPolicy(0, TimeSpan.Zero, TimeSpan.Zero),
                proxy: null,
                new ConnectionBandwidthLimits(null, null),
                "utf-8"),
            DateTimeOffset.UtcNow);

    private async Task<RuntimeStorageConnection> RegisterRequiredAsync(
        ConnectionProfileId profileId,
        string rootIdentity,
        SwiftConnectionConfig configuration)
    {
        var factory = new CodeLogicStorageSessionFactory(Assert.IsType<StorageLibrary>(_library));
        var registration = await factory.RegisterAsync(profileId, rootIdentity, configuration);
        Assert.True(registration.IsSuccess, Failure(registration.Error));
        return registration.Value;
    }

    private async Task<StorageFailure> RejectAsync(SwiftConnectionConfig configuration)
    {
        var factory = new CodeLogicStorageSessionFactory(Assert.IsType<StorageLibrary>(_library));
        var registration = await factory.RegisterAsync(
            ConnectionProfileId.New(),
            $"rejected-swift-{Guid.NewGuid():N}",
            configuration);
        if (registration.IsFailure)
        {
            return registration.Error;
        }

        await using var connection = registration.Value;
        var health = await connection.Session.CheckHealthAsync();
        Assert.True(health.IsFailure);
        return health.Error;
    }

    private static SwiftConnectionConfig CreateConfiguration(
        SwiftFixtureSettings settings,
        string prefix,
        string password) => new()
        {
            Enabled = true,
            Container = settings.Container,
            Prefix = prefix,
            AuthenticationMode = SwiftAuthenticationMode.TempAuthV1,
            AuthenticationUrl = settings.AuthenticationUrl.AbsoluteUri,
            Username = settings.Username,
            Password = password,
            AllowInsecureHttp = settings.AuthenticationUrl.Scheme == Uri.UriSchemeHttp,
            TimeoutSeconds = 30
        };

    private static void AssertDoesNotDisclose(
        StorageFailure failure,
        SwiftFixtureSettings settings,
        params string[] additionalSecrets)
    {
        var disclosed = string.Join("|", failure.Code, failure.Message, failure.ProviderCode, failure.DiagnosticId);
        foreach (var secret in additionalSecrets.Append(settings.Password))
        {
            Assert.False(disclosed.Contains(secret, StringComparison.Ordinal));
        }
    }

    private static string Failure(StorageFailure? failure) => failure is null
        ? "The operation failed without a structured failure."
        : $"{failure.Code}: {failure.Message}";

    private sealed record SwiftFixtureSettings(Uri AuthenticationUrl, string Username, string Password, string Container)
    {
        public static SwiftFixtureSettings? Load()
        {
            var required = string.Equals(
                Environment.GetEnvironmentVariable("STORAGEHUB_REQUIRE_SWIFT"),
                "1",
                StringComparison.Ordinal);
            var url = Environment.GetEnvironmentVariable("STORAGEHUB_SWIFT_AUTH_URL");
            var username = Environment.GetEnvironmentVariable("STORAGEHUB_SWIFT_USERNAME");
            var password = Environment.GetEnvironmentVariable("STORAGEHUB_SWIFT_PASSWORD");
            var container = Environment.GetEnvironmentVariable("STORAGEHUB_SWIFT_CONTAINER");
            if (!required && url is null && username is null && password is null && container is null)
            {
                return null;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var authenticationUrl) ||
                (authenticationUrl.Scheme != Uri.UriSchemeHttp && authenticationUrl.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("STORAGEHUB_SWIFT_AUTH_URL is missing or is not an http(s) URL.");
            }

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException("STORAGEHUB_SWIFT_USERNAME or STORAGEHUB_SWIFT_PASSWORD is missing.");
            }

            if (string.IsNullOrWhiteSpace(container) || container.Contains('/', StringComparison.Ordinal))
            {
                throw new InvalidOperationException("STORAGEHUB_SWIFT_CONTAINER is missing or is not a container name.");
            }

            return new SwiftFixtureSettings(authenticationUrl, username.Trim(), password, container.Trim());
        }
    }

    private sealed class NoTrust : ITrustStore
    {
        public ValueTask<IReadOnlyList<TrustRecord>> FindAsync(
            TrustArtifactKind artifactKind,
            string canonicalHost,
            int port,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Swift has no certificate pin to look up.");

        public ValueTask UpsertAsync(TrustRecord record, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<bool> RemoveAsync(string trustId, int expectedVersion, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoSecretFiles : IRuntimeSecretFileMaterializer
    {
        public ValueTask<IRuntimeSecretFile> MaterializeAsync(
            ReadOnlyMemory<byte> secret,
            string fileExtension,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Swift must not write a secret to disk.");

        public int ScavengeOrphans(TimeSpan minimumAge) => 0;
    }

    private sealed class FixtureSecretProtector : ISecretProtector
    {
        private static readonly byte[] Key = SHA256.HashData("storagehub-swift-fixture"u8.ToArray());

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
