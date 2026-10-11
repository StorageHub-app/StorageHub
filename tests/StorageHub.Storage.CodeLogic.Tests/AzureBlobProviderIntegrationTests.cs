using System.Security.Cryptography;
using System.Text;
using Azure.Storage.Blobs;
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
/// Azure Blob Storage against a real Blob service: the lab's <c>azurite</c>, or anything else the
/// STORAGEHUB_AZURE_* variables point at.
/// </summary>
/// <remarks>
/// Azurite speaks plain HTTP, and CL.Storage sends a shared key or a SAS token only to an HTTPS
/// service URL, so the fixture signs in with a connection string, the one way an emulator is
/// reached. One pass goes through StorageHub's own connector from a saved profile, with the
/// connection string in a vault, so what is tested is the path a real connection takes. A run
/// writes below its own prefix, because a reused lab still holds what the last run wrote.
/// </remarks>
[Collection(ProviderIntegrationFixtureGroup.Name)]
public sealed class AzureBlobProviderIntegrationTests : IAsyncLifetime
{
    private AzureFixtureSettings? _settings;
    private StorageLibrary? _library;
    private string? _testRoot;
    private readonly string _runPrefix = $"mounted/run-{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        _settings = AzureFixtureSettings.Load();
        if (_settings is null)
        {
            return;
        }

        _testRoot = Path.Combine(Path.GetTempPath(), $"storagehub-azure-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(_testRoot);
        _library = await CodeLogicTestFramework.EnsureStartedAsync();

        // The container is the lab's to provide, as a bucket is MinIO's; the SDK the library
        // already brings makes it, and leaves a sentinel outside the run's prefix.
        var container = new BlobContainerClient(_settings.ConnectionString, _settings.Container);
        _ = await container.CreateIfNotExistsAsync();
        _ = await container.GetBlobClient("outside/sentinel.txt")
            .UploadAsync(BinaryData.FromString("must-not-be-mounted"), overwrite: true);
    }

    [Fact]
    [Trait("Category", "AzureBlobProviderIntegration")]
    public async Task AzureBlobContainer_ConformsAndFailsClosedOnCredentialsAndPlainHttp()
    {
        if (_settings is null)
        {
            return;
        }

        await AssertConformanceAsync(_settings);
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

    private async Task AssertConformanceAsync(AzureFixtureSettings settings)
    {
        var profileId = ConnectionProfileId.New();
        var rootIdentity = $"azure-root-{Guid.NewGuid():N}";
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

    private async Task AssertSavedProfileConnectsThroughTheConnectorAsync(AzureFixtureSettings settings)
    {
        var vaultRoot = Path.Combine(Assert.IsType<string>(_testRoot), "vault");
        using var vault = new VersionedFileSecretVault(vaultRoot, new FixtureSecretProtector());
        var connectionString = (await vault.CreateAsync(Encoding.UTF8.GetBytes(settings.ConnectionString))).Reference;
        var plainHttp = AzureConnectionStringIsPlainHttp(settings.ConnectionString);

        await using var connector = Connector(vault);
        var opened = await connector.OpenAsync(Profile(
            new AzureBlobEndpoint(settings.Container, allowInsecureHttp: plainHttp, rootPrefix: $"{_runPrefix}/connector"),
            new AzureConnectionStringAuthentication(connectionString)));
        Assert.True(opened.IsSuccess, Failure(opened.Error));
        await using var connection = opened.Value;
        await ProviderSessionConformance.AssertBoundedRoundTripAsync(
            connection.Session,
            connection.Session.ProfileId,
            connection.Session.RootIdentity);

        if (plainHttp)
        {
            // The same string without the acknowledgement is refused before anything is sent.
            await using var refusing = Connector(vault);
            var refused = await refusing.OpenAsync(Profile(
                new AzureBlobEndpoint(settings.Container, rootPrefix: $"{_runPrefix}/connector"),
                new AzureConnectionStringAuthentication(connectionString)));
            Assert.True(refused.IsFailure);
            Assert.Equal(StorageFailureKind.Unsupported, refused.Error.Kind);
            AssertDoesNotDisclose(refused.Error, settings);
        }
    }

    private async Task AssertHostileFailuresAsync(AzureFixtureSettings settings)
    {
        // The same account with a key that is not its own: refused as unauthorized, key unrepeated.
        var wrongKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var wrongKeyFailure = await RejectAsync(CreateConfiguration(
            settings,
            $"{_runPrefix}/refused",
            ReplaceAccountKey(settings.ConnectionString, wrongKey)));
        Assert.Equal(StorageFailureKind.Unauthorized, wrongKeyFailure.Kind);
        AssertDoesNotDisclose(wrongKeyFailure, settings, wrongKey);

        // CL.Storage will not send a shared key to a plain-HTTP service URL at all.
        var unsafeSharedKey = new AzureBlobConnectionConfig
        {
            Enabled = true,
            Container = settings.Container,
            AuthenticationMode = AzureBlobAuthenticationMode.SharedKey,
            ServiceUri = "http://127.0.0.1:10000/devstoreaccount1",
            AccountName = "devstoreaccount1",
            AccountKey = wrongKey
        };
        var registration = await new CodeLogicStorageSessionFactory(Assert.IsType<StorageLibrary>(_library))
            .RegisterAsync(ConnectionProfileId.New(), $"refused-azure-{Guid.NewGuid():N}", unsafeSharedKey);
        Assert.True(registration.IsFailure);
        AssertDoesNotDisclose(registration.Error, settings, wrongKey);
    }

    private CodeLogicConnectionProfileConnector Connector(ISecretVault vault) => new(
        new CodeLogicStorageSessionFactory(Assert.IsType<StorageLibrary>(_library)),
        vault,
        new NoTrust(),
        new NoSecretFiles());

    private static ConnectionProfile Profile(AzureBlobEndpoint endpoint, ConnectionAuthentication authentication) =>
        ConnectionProfile.Create(
            ConnectionProfileId.New(),
            new ConnectionProfileMetadata("Lab Azure Blob"),
            endpoint,
            authentication,
            new ConnectionOperationalOptions(
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30),
                new ConnectionRetryPolicy(1, TimeSpan.Zero, TimeSpan.Zero),
                proxy: null,
                new ConnectionBandwidthLimits(null, null),
                "utf-8"),
            DateTimeOffset.UtcNow);

    private async Task<RuntimeStorageConnection> RegisterRequiredAsync(
        ConnectionProfileId profileId,
        string rootIdentity,
        AzureBlobConnectionConfig configuration)
    {
        var factory = new CodeLogicStorageSessionFactory(Assert.IsType<StorageLibrary>(_library));
        var registration = await factory.RegisterAsync(profileId, rootIdentity, configuration);
        Assert.True(registration.IsSuccess, Failure(registration.Error));
        return registration.Value;
    }

    private async Task<StorageFailure> RejectAsync(AzureBlobConnectionConfig configuration)
    {
        var factory = new CodeLogicStorageSessionFactory(Assert.IsType<StorageLibrary>(_library));
        var registration = await factory.RegisterAsync(
            ConnectionProfileId.New(),
            $"rejected-azure-{Guid.NewGuid():N}",
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

    private static AzureBlobConnectionConfig CreateConfiguration(
        AzureFixtureSettings settings,
        string prefix,
        string? connectionString = null) => new()
        {
            Enabled = true,
            Container = settings.Container,
            Prefix = prefix,
            AuthenticationMode = AzureBlobAuthenticationMode.ConnectionString,
            ConnectionString = connectionString ?? settings.ConnectionString,
            TimeoutSeconds = 15,
            MaxRetries = 0
        };

    private static bool AzureConnectionStringIsPlainHttp(string connectionString) =>
        CodeLogicConnectionConfigurationBuilder.AzureConnectionStringNamesPlainHttp(connectionString);

    private static string ReplaceAccountKey(string connectionString, string key) => string.Join(
        ';',
        connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(part =>
            part.TrimStart().StartsWith("AccountKey=", StringComparison.OrdinalIgnoreCase)
                ? "AccountKey=" + key
                : part));

    private static void AssertDoesNotDisclose(
        StorageFailure failure,
        AzureFixtureSettings settings,
        params string[] additionalSecrets)
    {
        var disclosed = string.Join("|", failure.Code, failure.Message, failure.ProviderCode, failure.DiagnosticId);
        foreach (var secret in new[] { settings.ConnectionString, settings.AccountKey }.Concat(additionalSecrets))
        {
            if (secret.Length > 0)
            {
                Assert.False(disclosed.Contains(secret, StringComparison.Ordinal));
            }
        }
    }

    private static string Failure(StorageFailure? failure) => failure is null
        ? "The operation failed without a structured failure."
        : $"{failure.Code}: {failure.Message}";

    private sealed record AzureFixtureSettings(string ConnectionString, string Container, string AccountKey)
    {
        public static AzureFixtureSettings? Load()
        {
            var required = string.Equals(
                Environment.GetEnvironmentVariable("STORAGEHUB_REQUIRE_AZURITE"),
                "1",
                StringComparison.Ordinal);
            var connectionString = Environment.GetEnvironmentVariable("STORAGEHUB_AZURE_CONNECTION_STRING");
            var container = Environment.GetEnvironmentVariable("STORAGEHUB_AZURE_CONTAINER");
            if (!required && connectionString is null && container is null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Length > 4_096 ||
                connectionString.Any(char.IsControl) ||
                !connectionString.Contains("AccountKey=", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "STORAGEHUB_AZURE_CONNECTION_STRING is missing or names no account key.");
            }

            if (string.IsNullOrWhiteSpace(container))
            {
                throw new InvalidOperationException("STORAGEHUB_AZURE_CONTAINER is missing.");
            }

            var key = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .First(part => part.StartsWith("AccountKey=", StringComparison.OrdinalIgnoreCase))["AccountKey=".Length..];
            return new AzureFixtureSettings(connectionString, container.Trim(), key);
        }
    }

    private sealed class NoTrust : ITrustStore
    {
        public ValueTask<IReadOnlyList<TrustRecord>> FindAsync(
            TrustArtifactKind artifactKind,
            string canonicalHost,
            int port,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Azure Blob has no certificate pin to look up.");

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
            throw new InvalidOperationException("Azure Blob must not write a secret to disk.");

        public int ScavengeOrphans(TimeSpan minimumAge) => 0;
    }

    private sealed class FixtureSecretProtector : ISecretProtector
    {
        private static readonly byte[] Key = SHA256.HashData("storagehub-azure-fixture"u8.ToArray());

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
