using System.Globalization;
using System.Text;
using StorageHub.Application.Connections;
using StorageHub.Domain.Identifiers;
using StorageHub.Domain.Storage;
using StorageHub.Security;
using StorageHub.Storage.Models;

namespace StorageHub.Storage.CodeLogic.Tests;

/// <summary>
/// Covers the sharing the connector does, which is what lets the storage library reuse a listing
/// snapshot and a pooled session between calls. Before this the agent built a registration under a
/// fresh id per call, so the library saw a different connection on every page of a listing.
/// </summary>
[Collection(ProviderIntegrationFixtureGroup.Name)]
public sealed class CodeLogicConnectionProfileConnectorTests : IAsyncLifetime, IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"storagehub-connector-{Guid.NewGuid():N}");

    private CodeLogicStorageSessionFactory _factory = null!;
    private VersionedFileSecretVault _vault = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        var library = await CodeLogicTestFramework.EnsureStartedAsync();
        _factory = new CodeLogicStorageSessionFactory(library);
        _vault = new VersionedFileSecretVault(Path.Combine(_root, "vault"), new XorSecretProtector());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Two_calls_for_one_profile_share_a_single_runtime_registration()
    {
        await using var connector = CreateConnector();
        var profile = CreateLocalProfile(_root);

        var first = await connector.OpenAsync(profile);
        var second = await connector.OpenAsync(profile);

        Assert.True(first.IsSuccess, first.Error?.Message);
        Assert.True(second.IsSuccess, second.Error?.Message);

        // The id is what the storage library keys its listing snapshot and session pool on, so the
        // whole point of sharing is that it does not change between calls.
        Assert.Equal(first.Value.RuntimeConnectionId, second.Value.RuntimeConnectionId);
        Assert.Same(first.Value, second.Value);

        // Returning one borrow leaves the registration usable for the other.
        await first.Value.DisposeAsync();
        var address = StorageAddress.Create(profile.Id, second.Value.Session.RootIdentity, string.Empty).Value;
        var listing = await second.Value.Session.ListAsync(address);
        Assert.True(listing.IsSuccess, listing.Error?.Message);
        await second.Value.DisposeAsync();
    }

    [Fact]
    public async Task An_edited_profile_gets_a_new_registration_rather_than_the_old_one()
    {
        await using var connector = CreateConnector();
        var profile = CreateLocalProfile(_root);

        var before = await connector.OpenAsync(profile);
        Assert.True(before.IsSuccess, before.Error?.Message);
        var firstId = before.Value.RuntimeConnectionId;
        await before.Value.DisposeAsync();

        // A profile edit bumps Version. Its credentials, host or root may all have changed, so the
        // open registration must not be handed out again.
        var edited = profile with { Version = profile.Version + 1 };
        var after = await connector.OpenAsync(edited);

        Assert.True(after.IsSuccess, after.Error?.Message);
        Assert.NotEqual(firstId, after.Value.RuntimeConnectionId);
        await after.Value.DisposeAsync();
    }

    [Fact]
    public async Task An_unused_registration_is_retired_once_its_idle_window_passes()
    {
        await using var connector = CreateConnector(TimeSpan.FromMilliseconds(50));
        var profile = CreateLocalProfile(_root);

        var first = await connector.OpenAsync(profile);
        Assert.True(first.IsSuccess, first.Error?.Message);
        var firstId = first.Value.RuntimeConnectionId;
        await first.Value.DisposeAsync();

        // Nothing holds it now, so the registration and the secrets resolved for it go away rather
        // than lingering for as long as the agent runs. Waited out in one go rather than polled:
        // every open is a borrow, and a borrow cancels the retirement it is waiting for.
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        var rebuilt = await connector.OpenAsync(profile);
        Assert.True(rebuilt.IsSuccess, rebuilt.Error?.Message);
        Assert.NotEqual(firstId, rebuilt.Value.RuntimeConnectionId);
        await rebuilt.Value.DisposeAsync();
    }

    [Fact]
    public async Task Disposing_the_connector_closes_a_registration_that_is_still_open()
    {
        var connector = CreateConnector();
        var profile = CreateLocalProfile(_root);
        var opened = await connector.OpenAsync(profile);
        Assert.True(opened.IsSuccess, opened.Error?.Message);

        await connector.DisposeAsync();

        // The session is closed even though the borrow was never returned, which is what a stopping
        // agent needs: no registration left holding resolved credentials.
        var address = StorageAddress.Create(profile.Id, opened.Value.Session.RootIdentity, string.Empty).Value;
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await opened.Value.Session.ListAsync(address));
    }

    private CodeLogicConnectionProfileConnector CreateConnector(TimeSpan? idleLifetime = null) => new(
        _factory,
        _vault,
        new EmptyTrustStore(),
        new UnusedSecretFileMaterializer(),
        TimeProvider.System,
        idleLifetime);

    /// <summary>
    /// A connection's download limit slows the bytes read through its session, which is what the
    /// queue, sync and the browser all read through.
    /// </summary>
    [Fact]
    public async Task A_download_limit_slows_reads_through_the_session()
    {
        const int limit = 64 * 1024;
        await File.WriteAllBytesAsync(Path.Combine(_root, "big.bin"), new byte[4 * limit]);
        await using var connector = CreateConnector();
        var profile = CreateLocalProfile(_root, new ConnectionBandwidthLimits(null, limit));

        var opened = await connector.OpenAsync(profile);
        Assert.True(opened.IsSuccess, opened.Error?.Message);
        await using var connection = opened.Value;
        var address = StorageAddress.Create(profile.Id, connection.Session.RootIdentity, "big.bin").Value;
        var started = System.Diagnostics.Stopwatch.StartNew();
        var read = await connection.Session.OpenReadAsync(new StorageReadRequest(address));
        Assert.True(read.IsSuccess, read.Error?.Message);
        await using (var stream = read.Value)
        {
            await stream.CopyToAsync(Stream.Null);
        }

        // Four seconds of data at the limit. Allowing for a first second's burst, at least two.
        Assert.True(started.Elapsed >= TimeSpan.FromSeconds(2), $"Read in {started.Elapsed}.");
    }

    /// <summary>
    /// A connection routed through the lab's proxies reaches MinIO by its service name, which only
    /// resolves inside the lab's network: listing it at all is proof the traffic went through the
    /// proxy. SOCKS5 signs in with a password kept in the vault; a wrong one is refused.
    /// </summary>
    /// <remarks>Does nothing without the lab, like the other provider suites; STORAGEHUB_REQUIRE_PROXY makes it required.</remarks>
    [Fact]
    public async Task A_connection_through_the_lab_proxies_reaches_a_server_only_the_proxy_can_see()
    {
        var lab = ProxyLab.Load();
        if (lab is null)
        {
            return;
        }

        var http = new ConnectionProxy(new Uri($"http://127.0.0.1:{lab.HttpPort}"));
        var socks5 = new ConnectionProxy(
            new Uri($"socks5://127.0.0.1:{lab.Socks5Port}"),
            lab.Username,
            (await _vault.CreateAsync(Encoding.UTF8.GetBytes(lab.Password))).Reference);
        var wrongPassword = new ConnectionProxy(
            new Uri($"socks5://127.0.0.1:{lab.Socks5Port}"),
            lab.Username,
            (await _vault.CreateAsync("not-the-password"u8.ToArray())).Reference);

        await using var connector = CreateConnector();
        foreach (var proxy in new[] { http, socks5 })
        {
            var profile = await CreateMinioProfileAsync(lab, proxy);
            var opened = await connector.OpenAsync(profile);
            Assert.True(opened.IsSuccess, $"{proxy.Endpoint}: {opened.Error?.Message}");
            await using var connection = opened.Value;
            var root = StorageAddress.Create(profile.Id, connection.Session.RootIdentity, string.Empty).Value;
            var listing = await connection.Session.ListAsync(root);
            Assert.True(listing.IsSuccess, $"{proxy.Endpoint}: {listing.Error?.Message}");
        }

        var refusedProfile = await CreateMinioProfileAsync(lab, wrongPassword);
        var refusedOpen = await connector.OpenAsync(refusedProfile);
        if (refusedOpen.IsSuccess)
        {
            await using var connection = refusedOpen.Value;
            var root = StorageAddress.Create(refusedProfile.Id, connection.Session.RootIdentity, string.Empty).Value;
            Assert.True((await connection.Session.ListAsync(root)).IsFailure, "A wrong proxy password still listed.");
        }
    }

    private async Task<ConnectionProfile> CreateMinioProfileAsync(ProxyLab lab, ConnectionProxy proxy) =>
        ConnectionProfile.Create(
            ConnectionProfileId.New(),
            new ConnectionProfileMetadata("MinIO through a proxy"),
            new S3Endpoint(lab.Bucket, "us-east-1", lab.MinioBehindProxy, forcePathStyle: true, allowInsecureHttp: true),
            new S3AccessKeyAuthentication(
                (await _vault.CreateAsync(Encoding.UTF8.GetBytes(lab.AccessKey))).Reference,
                (await _vault.CreateAsync(Encoding.UTF8.GetBytes(lab.SecretKey))).Reference),
            new ConnectionOperationalOptions(
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30),
                new ConnectionRetryPolicy(1, TimeSpan.Zero, TimeSpan.Zero),
                proxy,
                new ConnectionBandwidthLimits(null, null),
                "utf-8"),
            DateTimeOffset.UtcNow);

    /// <summary>The lab's proxies and the MinIO behind them, from eng/testlab's settings.</summary>
    private sealed record ProxyLab(
        int HttpPort,
        int Socks5Port,
        string Username,
        string Password,
        Uri MinioBehindProxy,
        string AccessKey,
        string SecretKey,
        string Bucket)
    {
        public static ProxyLab? Load()
        {
            static string? Get(string name) => Environment.GetEnvironmentVariable(name);
            if (Get("STORAGEHUB_PROXY_HTTP_PORT") is null)
            {
                return Get("STORAGEHUB_REQUIRE_PROXY") == "1"
                    ? throw new InvalidOperationException("STORAGEHUB_REQUIRE_PROXY is set but the proxy settings are not.")
                    : null;
            }

            static string Required(string name) => Get(name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"{name} is missing.");
            return new ProxyLab(
                int.Parse(Required("STORAGEHUB_PROXY_HTTP_PORT"), CultureInfo.InvariantCulture),
                int.Parse(Required("STORAGEHUB_PROXY_SOCKS5_PORT"), CultureInfo.InvariantCulture),
                Required("STORAGEHUB_PROXY_USERNAME"),
                Required("STORAGEHUB_PROXY_PASSWORD"),
                new Uri(Required("STORAGEHUB_PROXY_MINIO_ENDPOINT")),
                Required("STORAGEHUB_MINIO_ACCESS_KEY"),
                Required("STORAGEHUB_MINIO_SECRET_KEY"),
                Required("STORAGEHUB_MINIO_BUCKET"));
        }
    }

    private static ConnectionProfile CreateLocalProfile(string rootPath, ConnectionBandwidthLimits? limits = null) => ConnectionProfile.Create(
        ConnectionProfileId.New(),
        new ConnectionProfileMetadata("Local files"),
        new LocalEndpoint(rootPath),
        new NoAuthentication(),
        new ConnectionOperationalOptions(
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(60),
            new ConnectionRetryPolicy(3, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(5)),
            proxy: null,
            limits ?? new ConnectionBandwidthLimits(null, null),
            "utf-8"),
        DateTimeOffset.Parse("2026-09-19T09:00:00Z", CultureInfo.InvariantCulture));

    /// <summary>A local profile pins nothing, so trust lookups only have to be answerable.</summary>
    private sealed class EmptyTrustStore : ITrustStore
    {
        public ValueTask<IReadOnlyList<TrustRecord>> FindAsync(
            TrustArtifactKind artifactKind,
            string canonicalHost,
            int port,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<TrustRecord>>([]);

        public ValueTask UpsertAsync(TrustRecord record, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<bool> RemoveAsync(
            string trustId,
            int expectedVersion,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>A local profile materialises no secret files; being called at all is the failure.</summary>
    private sealed class UnusedSecretFileMaterializer : IRuntimeSecretFileMaterializer
    {
        public int ScavengeOrphans(TimeSpan minimumAge) => 0;

        public ValueTask<IRuntimeSecretFile> MaterializeAsync(
            ReadOnlyMemory<byte> secret,
            string fileExtension,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A local profile has no secret to materialise.");
    }

    private sealed class XorSecretProtector : ISecretProtector
    {
        public string Scheme => "test-xor-v1";

        public byte[] Protect(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> entropy) => Transform(plaintext);

        public byte[] Unprotect(ReadOnlySpan<byte> protectedData, ReadOnlySpan<byte> entropy) =>
            Transform(protectedData);

        private static byte[] Transform(ReadOnlySpan<byte> input)
        {
            var output = new byte[input.Length];
            for (var index = 0; index < input.Length; index++)
            {
                output[index] = (byte)(input[index] ^ 0x5A);
            }

            return output;
        }
    }
}
