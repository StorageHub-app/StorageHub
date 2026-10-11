using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
/// Google Cloud Storage against an emulator: the lab's <c>fake-gcs-server</c>, or anything else the
/// STORAGEHUB_GCS_* variables point at.
/// </summary>
/// <remarks>
/// fake-gcs-server checks no credentials, so the conformance pass signs in anonymously. The pass
/// through StorageHub's own connector signs in with a service-account key from a vault instead: the
/// key is minted here, and the connection goes through a forwarder in this fixture that keeps the
/// Authorization header of each request (and answers the key's token endpoint, should the client
/// exchange its assertion rather than sign its own token). What is shown is that the key in the
/// vault is what the Google client signed with, without anything reaching Google. A run writes
/// below its own prefix, because a reused lab still holds what the last run wrote.
/// </remarks>
[Collection(ProviderIntegrationFixtureGroup.Name)]
public sealed class GoogleCloudStorageProviderIntegrationTests : IAsyncLifetime
{
    private GcsFixtureSettings? _settings;
    private StorageLibrary? _library;
    private string? _testRoot;
    private readonly string _runPrefix = $"mounted/run-{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        _settings = GcsFixtureSettings.Load();
        if (_settings is null)
        {
            return;
        }

        _testRoot = Path.Combine(Path.GetTempPath(), $"storagehub-gcs-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(_testRoot);
        _library = await CodeLogicTestFramework.EnsureStartedAsync();

        // The bucket is the lab's to provide, as MinIO's is, but an emulator started without a
        // data folder has none: make it if it is missing, and leave a sentinel outside the prefix.
        using var http = new HttpClient { BaseAddress = _settings.ServiceUrl, Timeout = TimeSpan.FromSeconds(15) };
        using var created = await http.PostAsJsonAsync("storage/v1/b?project=storagehub", new { name = _settings.Bucket });
        Assert.True(
            created.IsSuccessStatusCode || created.StatusCode == HttpStatusCode.Conflict,
            $"The bucket could not be made: {(int)created.StatusCode}.");
        using var sentinel = await http.PostAsync(
            $"upload/storage/v1/b/{_settings.Bucket}/o?uploadType=media&name=outside%2Fsentinel.txt",
            new StringContent("must-not-be-mounted", Encoding.UTF8, "text/plain"));
        Assert.True(sentinel.IsSuccessStatusCode, $"The sentinel could not be written: {(int)sentinel.StatusCode}.");
    }

    [Fact]
    [Trait("Category", "GoogleCloudStorageProviderIntegration")]
    public async Task CloudStorageBucket_ConformsAndSignsInWithTheVaultedKeyAndRefusesUnacknowledgedHttp()
    {
        if (_settings is null)
        {
            return;
        }

        await AssertConformanceAsync(_settings);
        await AssertSavedProfileSignsInWithItsVaultedKeyAsync(_settings);
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

    private async Task AssertConformanceAsync(GcsFixtureSettings settings)
    {
        var profileId = ConnectionProfileId.New();
        var rootIdentity = $"gcs-root-{Guid.NewGuid():N}";
        await using var connection = await RegisterRequiredAsync(
            profileId,
            rootIdentity,
            CreateConfiguration(settings, $"{_runPrefix}/direct"));

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

    private async Task AssertSavedProfileSignsInWithItsVaultedKeyAsync(GcsFixtureSettings settings)
    {
        await using var forwarder = RecordingForwarder.Start(settings.ServiceUrl);
        var vaultRoot = Path.Combine(Assert.IsType<string>(_testRoot), "vault");
        using var vault = new VersionedFileSecretVault(vaultRoot, new FixtureSecretProtector());
        using var rsa = RSA.Create(2048);
        var key = ServiceAccountJson(rsa, new Uri(forwarder.Uri, "token"));
        var reference = (await vault.CreateAsync(Encoding.UTF8.GetBytes(key))).Reference;

        await using var connector = Connector(vault);
        var opened = await connector.OpenAsync(Profile(
            new GoogleCloudStorageEndpoint(
                settings.Bucket,
                forwarder.Uri,
                allowInsecureHttp: true,
                rootPrefix: $"{_runPrefix}/connector"),
            new GoogleServiceAccountAuthentication(reference)));
        Assert.True(opened.IsSuccess, Failure(opened.Error));
        await using var connection = opened.Value;
        await ProviderSessionConformance.AssertBoundedRoundTripAsync(
            connection.Session,
            connection.Session.ProfileId,
            connection.Session.RootIdentity);

        // Every request was signed in, either with a token the key's assertion was exchanged for,
        // or with a token the key signed itself.
        var authorizations = forwarder.Authorizations;
        Assert.NotEmpty(authorizations);
        Assert.All(authorizations, authorization => Assert.True(
            authorization == "Bearer fixture-token" || IsSignedBy(authorization, rsa),
            "A request was not signed in with the vaulted key."));
    }

    /// <summary>Whether a bearer token is a JWT whose RS256 signature is this key's.</summary>
    private static bool IsSignedBy(string authorization, RSA key)
    {
        const string bearer = "Bearer ";
        if (!authorization.StartsWith(bearer, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = authorization[bearer.Length..].Split('.');
        return parts.Length == 3 && key.VerifyData(
            Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]),
            Base64UrlDecode(parts[2]),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var text = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(text.PadRight(text.Length + ((4 - (text.Length % 4)) % 4), '='));
    }

    private async Task AssertHostileFailuresAsync(GcsFixtureSettings settings)
    {
        if (settings.ServiceUrl.Scheme == Uri.UriSchemeHttp)
        {
            // A plain-HTTP endpoint nobody acknowledged: the profile cannot be made, and the
            // library will not register one either.
            Assert.Throws<ArgumentException>(() => new GoogleCloudStorageEndpoint(settings.Bucket, settings.ServiceUrl));
            var unacknowledged = CreateConfiguration(settings, $"{_runPrefix}/refused");
            unacknowledged.AllowInsecureHttp = false;
            var registration = await new CodeLogicStorageSessionFactory(Assert.IsType<StorageLibrary>(_library))
                .RegisterAsync(ConnectionProfileId.New(), $"refused-gcs-{Guid.NewGuid():N}", unacknowledged);
            Assert.True(registration.IsFailure);
        }

        // A key that is not JSON at all is refused without being repeated back.
        var vaultRoot = Path.Combine(Assert.IsType<string>(_testRoot), "vault-hostile");
        using var vault = new VersionedFileSecretVault(vaultRoot, new FixtureSecretProtector());
        const string notAKey = "not-a-service-account-key-4f9c";
        var reference = (await vault.CreateAsync(Encoding.UTF8.GetBytes(notAKey))).Reference;
        await using var connector = Connector(vault);
        var refused = await connector.OpenAsync(Profile(
            new GoogleCloudStorageEndpoint(
                settings.Bucket,
                settings.ServiceUrl,
                allowInsecureHttp: settings.ServiceUrl.Scheme == Uri.UriSchemeHttp,
                rootPrefix: $"{_runPrefix}/refused"),
            new GoogleServiceAccountAuthentication(reference)));
        Assert.True(refused.IsFailure);
        var disclosed = string.Join(
            "|", refused.Error.Code, refused.Error.Message, refused.Error.ProviderCode, refused.Error.DiagnosticId);
        Assert.DoesNotContain(notAKey, disclosed, StringComparison.Ordinal);
    }

    private CodeLogicConnectionProfileConnector Connector(ISecretVault vault) => new(
        new CodeLogicStorageSessionFactory(Assert.IsType<StorageLibrary>(_library)),
        vault,
        new NoTrust(),
        new NoSecretFiles());

    private static ConnectionProfile Profile(GoogleCloudStorageEndpoint endpoint, ConnectionAuthentication authentication) =>
        ConnectionProfile.Create(
            ConnectionProfileId.New(),
            new ConnectionProfileMetadata("Lab Cloud Storage"),
            endpoint,
            authentication,
            new ConnectionOperationalOptions(
                TimeSpan.FromSeconds(100),
                TimeSpan.FromSeconds(100),
                new ConnectionRetryPolicy(1, TimeSpan.Zero, TimeSpan.Zero),
                proxy: null,
                new ConnectionBandwidthLimits(null, null),
                "utf-8"),
            DateTimeOffset.UtcNow);

    /// <summary>A service-account key file for <paramref name="rsa"/>, its token endpoint <paramref name="tokenUri"/>.</summary>
    private static string ServiceAccountJson(RSA rsa, Uri tokenUri) => JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["type"] = "service_account",
            ["project_id"] = "storagehub-lab",
            ["private_key_id"] = Guid.NewGuid().ToString("N"),
            ["private_key"] = rsa.ExportPkcs8PrivateKeyPem(),
            ["client_email"] = "fixture@storagehub-lab.iam.gserviceaccount.com",
            ["client_id"] = "1",
            ["token_uri"] = tokenUri.AbsoluteUri
        });

    private async Task<RuntimeStorageConnection> RegisterRequiredAsync(
        ConnectionProfileId profileId,
        string rootIdentity,
        GoogleCloudConnectionConfig configuration)
    {
        var factory = new CodeLogicStorageSessionFactory(Assert.IsType<StorageLibrary>(_library));
        var registration = await factory.RegisterAsync(profileId, rootIdentity, configuration);
        Assert.True(registration.IsSuccess, Failure(registration.Error));
        return registration.Value;
    }

    private static GoogleCloudConnectionConfig CreateConfiguration(GcsFixtureSettings settings, string prefix) => new()
    {
        Enabled = true,
        Bucket = settings.Bucket,
        Prefix = prefix,
        AuthenticationMode = GoogleCloudAuthenticationMode.Anonymous,
        ServiceUrl = settings.ServiceUrl.AbsoluteUri,
        AllowInsecureHttp = settings.ServiceUrl.Scheme == Uri.UriSchemeHttp
    };

    private static string Failure(StorageFailure? failure) => failure is null
        ? "The operation failed without a structured failure."
        : $"{failure.Code}: {failure.Message}";

    private sealed record GcsFixtureSettings(Uri ServiceUrl, string Bucket)
    {
        public static GcsFixtureSettings? Load()
        {
            var required = string.Equals(
                Environment.GetEnvironmentVariable("STORAGEHUB_REQUIRE_FAKE_GCS"),
                "1",
                StringComparison.Ordinal);
            var serviceUrl = Environment.GetEnvironmentVariable("STORAGEHUB_GCS_SERVICE_URL");
            var bucket = Environment.GetEnvironmentVariable("STORAGEHUB_GCS_BUCKET");
            if (!required && serviceUrl is null && bucket is null)
            {
                return null;
            }

            if (!Uri.TryCreate(serviceUrl, UriKind.Absolute, out var url) ||
                (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("STORAGEHUB_GCS_SERVICE_URL is missing or is not an http(s) URL.");
            }

            if (string.IsNullOrWhiteSpace(bucket) || !GoogleCloudStorageEndpoint.IsValidBucketName(bucket.Trim()))
            {
                throw new InvalidOperationException("STORAGEHUB_GCS_BUCKET is missing or is not a bucket name.");
            }

            var root = url.AbsoluteUri.EndsWith('/') ? url : new Uri(url.AbsoluteUri + "/");
            return new GcsFixtureSettings(root, bucket.Trim());
        }
    }

    /// <summary>
    /// A forwarder on loopback in front of the emulator that keeps each request's Authorization
    /// header, and answers a token exchange at <c>/token</c> itself.
    /// </summary>
    private sealed class RecordingForwarder : IAsyncDisposable
    {
        private static readonly HashSet<string> HopHeaders = new(StringComparer.OrdinalIgnoreCase)
        {
            "Connection", "Content-Length", "Transfer-Encoding", "Keep-Alive", "Host"
        };

        private readonly HttpListener _listener;
        private readonly HttpClient _upstream;
        private readonly Task _loop;
        private readonly List<string> _authorizations = [];

        private RecordingForwarder(HttpListener listener, Uri uri, Uri upstream)
        {
            _listener = listener;
            _upstream = new HttpClient { BaseAddress = upstream, Timeout = TimeSpan.FromSeconds(30) };
            Uri = uri;
            _loop = Task.Run(ServeAsync);
        }

        public Uri Uri { get; }

        public IReadOnlyList<string> Authorizations
        {
            get
            {
                lock (_authorizations)
                {
                    return [.. _authorizations];
                }
            }
        }

        public static RecordingForwarder Start(Uri upstream)
        {
            for (var attempt = 0; ; attempt++)
            {
                var prefix = $"http://127.0.0.1:{Random.Shared.Next(20_000, 60_000)}/";
                var listener = new HttpListener();
                listener.Prefixes.Add(prefix);
                try
                {
                    listener.Start();
                    return new RecordingForwarder(listener, new Uri(prefix), upstream);
                }
                catch (HttpListenerException) when (attempt < 10)
                {
                    listener.Close();
                }
            }
        }

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
                {
                    return;
                }

                try
                {
                    await HandleAsync(context);
                }
                catch (Exception exception) when (exception is HttpListenerException or HttpRequestException or IOException)
                {
                    context.Response.Abort();
                }
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            if (request.Url!.AbsolutePath == "/token")
            {
                var body = Encoding.UTF8.GetBytes(
                    """{"access_token":"fixture-token","expires_in":3600,"token_type":"Bearer"}""");
                response.ContentType = "application/json";
                response.ContentLength64 = body.Length;
                await response.OutputStream.WriteAsync(body);
                response.Close();
                return;
            }

            lock (_authorizations)
            {
                _authorizations.Add(request.Headers["Authorization"] ?? string.Empty);
            }

            using var forwarded = new HttpRequestMessage(
                new HttpMethod(request.HttpMethod),
                request.Url.PathAndQuery.TrimStart('/'));
            using var content = new MemoryStream();
            await request.InputStream.CopyToAsync(content);
            if (content.Length > 0 || request.HttpMethod is "POST" or "PUT" or "PATCH")
            {
                forwarded.Content = new ByteArrayContent(content.ToArray());
            }

            foreach (var name in request.Headers.AllKeys.OfType<string>())
            {
                if (HopHeaders.Contains(name) || name.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = request.Headers[name];
                if (!forwarded.Headers.TryAddWithoutValidation(name, value))
                {
                    _ = forwarded.Content?.Headers.TryAddWithoutValidation(name, value);
                }
            }

            using var answer = await _upstream.SendAsync(forwarded);
            response.StatusCode = (int)answer.StatusCode;
            foreach (var header in answer.Headers.Concat(answer.Content.Headers))
            {
                if (!HopHeaders.Contains(header.Key))
                {
                    response.Headers[header.Key] = string.Join(",", header.Value);
                }
            }

            var payload = await answer.Content.ReadAsByteArrayAsync();
            response.ContentLength64 = payload.Length;
            await response.OutputStream.WriteAsync(payload);
            response.Close();
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            _listener.Close();
            await _loop;
            _upstream.Dispose();
        }
    }

    private sealed class NoTrust : ITrustStore
    {
        public ValueTask<IReadOnlyList<TrustRecord>> FindAsync(
            TrustArtifactKind artifactKind,
            string canonicalHost,
            int port,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Cloud Storage has no certificate pin to look up.");

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
            throw new InvalidOperationException("Cloud Storage must not write a secret to disk.");

        public int ScavengeOrphans(TimeSpan minimumAge) => 0;
    }

    private sealed class FixtureSecretProtector : ISecretProtector
    {
        private static readonly byte[] Key = SHA256.HashData("storagehub-gcs-fixture"u8.ToArray());

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
