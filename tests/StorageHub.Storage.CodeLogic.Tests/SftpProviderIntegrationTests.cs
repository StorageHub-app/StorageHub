using CL.Storage;
using CL.Storage.Configuration;
using CodeLogic;
using StorageHub.Contracts.Results;
using StorageHub.Domain.Identifiers;
using StorageHub.Domain.Storage;
using StorageHub.Storage.Models;

namespace StorageHub.Storage.CodeLogic.Tests;

[Collection(ProviderIntegrationFixtureGroup.Name)]
public sealed class SftpProviderIntegrationTests : IAsyncLifetime
{
    private SftpFixtureSettings? _settings;
    private StorageLibrary? _library;
    private string? _testRoot;

    public async Task InitializeAsync()
    {
        _settings = SftpFixtureSettings.Load();
        if (_settings is null)
        {
            return;
        }

        _testRoot = Path.Combine(Path.GetTempPath(), $"storagehub-sftp-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(_testRoot);
        _library = await CodeLogicTestFramework.EnsureStartedAsync();
    }

    [Fact]
    [Trait("Category", "SftpProviderIntegration")]
    public async Task SftpEndpoints_ConformAndFailClosedAcrossAuthenticationAndHostKeyChanges()
    {
        if (_settings is null)
        {
            return;
        }

        await AssertPasswordConformanceAsync(_settings);
        await AssertPrivateKeyConformanceAsync(_settings);
        await AssertHostKeyFailuresAsync(_settings);
        await AssertAuthenticationFailuresAsync(_settings);
        await AssertMalformedPinFailuresAsync(_settings);
    }

    public async Task DisposeAsync()
    {
        if (_testRoot is not null && Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    private async Task AssertPasswordConformanceAsync(SftpFixtureSettings settings)
    {
        var profileId = ConnectionProfileId.New();
        var rootIdentity = $"sftp-password-root-{Guid.NewGuid():N}";
        await using var connection = await RegisterRequiredAsync(
            profileId,
            rootIdentity,
            CreatePasswordConfiguration(settings, settings.PasswordPort));

        await ProviderSessionConformance.AssertBoundedRoundTripAsync(
            connection.Session,
            profileId,
            rootIdentity,
            StorageWriteMode.Overwrite);
        await ProviderSessionConformance.AssertAbortDoesNotPublishAsync(
            connection.Session,
            profileId,
            rootIdentity,
            StorageWriteMode.Overwrite);
        await ProviderSessionConformance.AssertAddressSubstitutionFailsBeforeProviderIoAsync(
            connection.Session,
            profileId,
            rootIdentity);
        await ProviderSessionConformance.AssertTransfersToAndFromLocalAsync(
            new CodeLogicStorageSessionFactory(Assert.IsType<StorageLibrary>(_library)),
            connection.Session,
            profileId,
            rootIdentity,
            Assert.IsType<string>(_testRoot),
            StorageWriteMode.Overwrite,
            supportsSafeRemoteCreate: false);

        var createOnlyAddress = StorageAddress.Create(
            profileId,
            rootIdentity,
            "create-only-must-not-start.bin").Value;
        var createOnly = await connection.Session.OpenWriteAsync(new StorageWriteRequest(
            createOnlyAddress,
            StorageWriteMode.CreateNew));
        Assert.True(createOnly.IsFailure);
        Assert.Equal("storage.create_new.atomicity_unsupported", createOnly.Error.Code);
    }

    private async Task AssertPrivateKeyConformanceAsync(SftpFixtureSettings settings)
    {
        var profileId = ConnectionProfileId.New();
        var rootIdentity = $"sftp-key-root-{Guid.NewGuid():N}";
        await using var connection = await RegisterRequiredAsync(
            profileId,
            rootIdentity,
            CreatePrivateKeyConfiguration(
                settings,
                settings.PrivateKeyPort,
                settings.ClientKeyPath,
                settings.ClientKeyPassphrase));

        await ProviderSessionConformance.AssertBoundedRoundTripAsync(
            connection.Session,
            profileId,
            rootIdentity,
            StorageWriteMode.Overwrite);
        await ProviderSessionConformance.AssertAddressSubstitutionFailsBeforeProviderIoAsync(
            connection.Session,
            profileId,
            rootIdentity);
    }

    private async Task AssertHostKeyFailuresAsync(SftpFixtureSettings settings)
    {
        var wrongPin = await RejectAsync(CreatePasswordConfiguration(
            settings,
            settings.PasswordPort,
            settings.RotatedHostFingerprint));
        AssertDoesNotDisclose(wrongPin, settings);
        AssertTrustRequired(wrongPin);

        var changedHostKey = await RejectAsync(CreatePasswordConfiguration(
            settings,
            settings.RotatedPort,
            settings.HostFingerprint));
        AssertDoesNotDisclose(changedHostKey, settings);
        AssertTrustRequired(changedHostKey);

        var rotatedAccepted = await RegisterRequiredAsync(
            ConnectionProfileId.New(),
            $"sftp-rotated-root-{Guid.NewGuid():N}",
            CreatePasswordConfiguration(
                settings,
                settings.RotatedPort,
                settings.RotatedHostFingerprint));
        await using (rotatedAccepted)
        {
            var health = await rotatedAccepted.Session.CheckHealthAsync();
            Assert.True(health.IsSuccess, Failure(health.Error));
        }
    }

    /// <summary>
    /// A key the pins refuse is a trust decision, which the queue and the sync outbox block on. Before
    /// CL.Storage 4.8.93 it came back as "unavailable", and a transfer retried it.
    /// </summary>
    private static void AssertTrustRequired(StorageFailure failure)
    {
        Assert.Equal(StorageFailureKind.Security, failure.Kind);
        Assert.Equal("storage.trust.host_key_rejected", failure.Code);
        Assert.False(failure.IsTransient);
    }

    private async Task AssertAuthenticationFailuresAsync(SftpFixtureSettings settings)
    {
        var wrongPassword = "invalid-" + Guid.NewGuid().ToString("N");
        var passwordFailure = await RejectAsync(CreatePasswordConfiguration(
            settings,
            settings.PasswordPort,
            password: wrongPassword));
        Assert.Equal(StorageFailureKind.Unauthorized, passwordFailure.Kind);
        AssertDoesNotDisclose(passwordFailure, settings, wrongPassword);

        var wrongPassphrase = "invalid-" + Guid.NewGuid().ToString("N");
        var passphraseFailure = await RejectAsync(CreatePrivateKeyConfiguration(
            settings,
            settings.PrivateKeyPort,
            settings.ClientKeyPath,
            wrongPassphrase));
        AssertDoesNotDisclose(passphraseFailure, settings, wrongPassphrase);

        var unauthorizedKey = await RejectAsync(CreatePrivateKeyConfiguration(
            settings,
            settings.PrivateKeyPort,
            settings.AlternateClientKeyPath,
            settings.AlternateClientKeyPassphrase));
        Assert.Equal(StorageFailureKind.Unauthorized, unauthorizedKey.Kind);
        AssertDoesNotDisclose(unauthorizedKey, settings);

        var passwordAgainstKeyOnly = await RejectAsync(CreatePasswordConfiguration(
            settings,
            settings.PrivateKeyPort));
        Assert.Equal(StorageFailureKind.Unauthorized, passwordAgainstKeyOnly.Kind);
        AssertDoesNotDisclose(passwordAgainstKeyOnly, settings);

        var keyAgainstPasswordOnly = await RejectAsync(CreatePrivateKeyConfiguration(
            settings,
            settings.PasswordPort,
            settings.ClientKeyPath,
            settings.ClientKeyPassphrase));
        Assert.Equal(StorageFailureKind.Unauthorized, keyAgainstPasswordOnly.Kind);
        AssertDoesNotDisclose(keyAgainstPasswordOnly, settings);
    }

    private async Task AssertMalformedPinFailuresAsync(SftpFixtureSettings settings)
    {
        var malformedPin = "malformed-" + Guid.NewGuid().ToString("N");
        var malformed = await RejectAsync(CreatePasswordConfiguration(
            settings,
            settings.PasswordPort,
            malformedPin));
        Assert.Equal(StorageFailureKind.Provider, malformed.Kind);
        AssertDoesNotDisclose(malformed, settings, malformedPin);

        var missing = CreatePasswordConfiguration(settings, settings.PasswordPort);
        missing.HostKeyFingerprints = [];
        var missingPin = await RejectAsync(missing);
        Assert.Equal(StorageFailureKind.Provider, missingPin.Kind);
        AssertDoesNotDisclose(missingPin, settings);
    }

    private async Task<RuntimeStorageConnection> RegisterRequiredAsync(
        ConnectionProfileId profileId,
        string rootIdentity,
        SftpConnectionConfig configuration)
    {
        var factory = new CodeLogicStorageSessionFactory(Assert.IsType<StorageLibrary>(_library));
        var registration = await factory.RegisterAsync(profileId, rootIdentity, configuration);
        Assert.True(registration.IsSuccess, Failure(registration.Error));
        return registration.Value;
    }

    private async Task<StorageFailure> RejectAsync(SftpConnectionConfig configuration)
    {
        var factory = new CodeLogicStorageSessionFactory(Assert.IsType<StorageLibrary>(_library));
        var registration = await factory.RegisterAsync(
            ConnectionProfileId.New(),
            $"rejected-sftp-{Guid.NewGuid():N}",
            configuration);
        if (registration.IsFailure)
        {
            AssertDoesNotDisclose(registration.Error, Assert.IsType<SftpFixtureSettings>(_settings));
            return registration.Error;
        }

        await using var connection = registration.Value;
        var health = await connection.Session.CheckHealthAsync();
        Assert.True(health.IsFailure);
        AssertDoesNotDisclose(health.Error, Assert.IsType<SftpFixtureSettings>(_settings));
        return health.Error;
    }

    private static SftpConnectionConfig CreatePasswordConfiguration(
        SftpFixtureSettings settings,
        int port,
        string? fingerprint = null,
        string? password = null) => new()
        {
            Enabled = true,
            Host = "127.0.0.1",
            Port = port,
            Root = "mounted",
            Username = settings.Username,
            AuthenticationMode = SftpAuthenticationMode.Password,
            Password = password ?? settings.Password,
            HostKeyFingerprints = [fingerprint ?? settings.HostFingerprint],
            TimeoutSeconds = 10
        };

    private static SftpConnectionConfig CreatePrivateKeyConfiguration(
        SftpFixtureSettings settings,
        int port,
        string privateKeyPath,
        string privateKeyPassphrase) => new()
        {
            Enabled = true,
            Host = "127.0.0.1",
            Port = port,
            Root = "mounted",
            Username = settings.Username,
            AuthenticationMode = SftpAuthenticationMode.PrivateKey,
            PrivateKeyPath = privateKeyPath,
            PrivateKeyPassphrase = privateKeyPassphrase,
            HostKeyFingerprints = [settings.HostFingerprint],
            TimeoutSeconds = 10
        };

    private static void AssertDoesNotDisclose(
        StorageFailure failure,
        SftpFixtureSettings settings,
        params string[] additionalSecrets)
    {
        var disclosed = string.Join(
            "|",
            failure.Code,
            failure.Message,
            failure.ProviderCode,
            failure.DiagnosticId);
        foreach (var secret in new[]
                 {
                     settings.Username,
                     settings.Password,
                     settings.ClientKeyPassphrase,
                     settings.AlternateClientKeyPassphrase,
                     settings.ClientKeyPath,
                     settings.AlternateClientKeyPath
                 }.Concat(additionalSecrets))
        {
            Assert.False(disclosed.Contains(secret, StringComparison.Ordinal));
        }
    }

    private static string Failure(StorageFailure? failure) => failure is null
        ? "The operation failed without a structured failure."
        : $"{failure.Code}: {failure.Message} (provider: {failure.ProviderCode ?? "none"})";

    private sealed record SftpFixtureSettings(
        int PasswordPort,
        int PrivateKeyPort,
        int RotatedPort,
        string Username,
        string Password,
        string HostFingerprint,
        string RotatedHostFingerprint,
        string ClientKeyPath,
        string ClientKeyPassphrase,
        string AlternateClientKeyPath,
        string AlternateClientKeyPassphrase)
    {
        private static readonly string[] PortNames =
            ["password port", "private-key port", "rotated-key port"];

        public static SftpFixtureSettings? Load()
        {
            var required = string.Equals(
                Environment.GetEnvironmentVariable("STORAGEHUB_REQUIRE_SFTP"),
                "1",
                StringComparison.Ordinal);
            var values = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["password port"] = Environment.GetEnvironmentVariable("STORAGEHUB_SFTP_PASSWORD_PORT"),
                ["private-key port"] = Environment.GetEnvironmentVariable("STORAGEHUB_SFTP_PRIVATE_KEY_PORT"),
                ["rotated-key port"] = Environment.GetEnvironmentVariable("STORAGEHUB_SFTP_ROTATED_PORT"),
                ["username"] = Environment.GetEnvironmentVariable("STORAGEHUB_SFTP_USERNAME"),
                ["password"] = Environment.GetEnvironmentVariable("STORAGEHUB_SFTP_PASSWORD"),
                ["host fingerprint"] = Environment.GetEnvironmentVariable("STORAGEHUB_SFTP_HOST_SHA256"),
                ["rotated host fingerprint"] = Environment.GetEnvironmentVariable("STORAGEHUB_SFTP_ROTATED_HOST_SHA256"),
                ["client key path"] = Environment.GetEnvironmentVariable("STORAGEHUB_SFTP_CLIENT_KEY_PATH"),
                ["client key passphrase"] = Environment.GetEnvironmentVariable("STORAGEHUB_SFTP_CLIENT_KEY_PASSPHRASE"),
                ["alternate client key path"] = Environment.GetEnvironmentVariable("STORAGEHUB_SFTP_ALTERNATE_KEY_PATH"),
                ["alternate client key passphrase"] = Environment.GetEnvironmentVariable("STORAGEHUB_SFTP_ALTERNATE_KEY_PASSPHRASE")
            };
            if (!required && values.Values.All(value => value is null))
            {
                return null;
            }

            var ports = PortNames
                .Select(name => int.TryParse(values[name], out var port) && port is >= 1 and <= 65535
                    ? port
                    : throw new InvalidOperationException($"The SFTP fixture {name} is missing or invalid."))
                .ToArray();
            if (ports.Distinct().Count() != ports.Length)
            {
                throw new InvalidOperationException("SFTP fixture ports must be distinct.");
            }

            return new SftpFixtureSettings(
                ports[0],
                ports[1],
                ports[2],
                RequiredBounded(values["username"], "username"),
                RequiredBounded(values["password"], "password"),
                RequiredFingerprint(values["host fingerprint"], "host fingerprint"),
                RequiredFingerprint(values["rotated host fingerprint"], "rotated host fingerprint"),
                RequiredKeyPath(values["client key path"], "client key path"),
                RequiredBounded(values["client key passphrase"], "client key passphrase"),
                RequiredKeyPath(values["alternate client key path"], "alternate client key path"),
                RequiredBounded(values["alternate client key passphrase"], "alternate client key passphrase"));
        }

        private static string RequiredBounded(string? value, string description)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl))
            {
                throw new InvalidOperationException($"The SFTP fixture {description} is missing or invalid.");
            }
            return value;
        }

        private static string RequiredFingerprint(string? value, string description)
        {
            if (value is null || value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            {
                throw new InvalidOperationException($"The SFTP fixture {description} is missing or invalid.");
            }
            return value.ToUpperInvariant();
        }

        private static string RequiredKeyPath(string? value, string description)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !Path.IsPathFullyQualified(value) ||
                !File.Exists(value) ||
                !Path.GetExtension(value).Equals(".key", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"The SFTP fixture {description} is missing or invalid.");
            }
            return Path.GetFullPath(value);
        }
    }
}
