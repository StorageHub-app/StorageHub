using StorageHub.Contracts.Ipc;
using StorageHub.Ipc;
using StorageHub.Ipc.Unix;
using StorageHub.Testing;

namespace StorageHub.Ipc.Unix.Tests;

/// <summary>
/// The same protocol as the named-pipe transport carries, over AF_UNIX.
/// </summary>
/// <remarks>
/// The framing, handshake and session lifetime are shared code, so these do not re-test them. What
/// is specific to this transport is the address and who is allowed to reach it, and that is what
/// each case here is about.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class UnixSocketIpcTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"storagehub-ipc-tests-{Guid.NewGuid():N}");

    public UnixSocketIpcTests()
    {
        if (OperatingSystem.IsLinux())
        {
            Directory.CreateDirectory(
                _directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    [LinuxOnlyFact]
    public async Task NegotiatesProtocolAndRoundTripsNormalEnvelope()
    {
        var options = ServerOptions();
        await using var server = UnixDomainSocketIpc.CreateServer(
            options,
            static async (session, cancellationToken) =>
            {
                var request = await session.ReceiveAsync(cancellationToken);
                var payload = request.DeserializePayload<TestPayload>();
                await session.SendAsync(
                    IpcEnvelope.Create(
                        "test.response",
                        request.RequestId,
                        request.Sequence + 1,
                        new TestPayload(payload.Value + "-received")),
                    cancellationToken);
            });
        await StartAsync(server);

        await using var client = UnixDomainSocketIpc.CreateClient(ClientOptions(options.Endpoint));
        var hello = await client.ConnectAsync();
        var requestId = Guid.NewGuid();
        await client.SendAsync(IpcEnvelope.Create("test.request", requestId, 1, new TestPayload("hello")));
        var response = await client.ReceiveAsync();

        Assert.True(hello.Accepted);
        Assert.Equal(ProtocolVersion.Current, hello.ProtocolVersion);
        Assert.Equal(options.AgentInstanceId, hello.AgentInstanceId);
        Assert.Equal(new TestPayload("hello-received"), response.DeserializePayload<TestPayload>());
    }

    /// <summary>
    /// The channel that used to refuse to exist off Windows.
    /// </summary>
    /// <remarks>
    /// It was refused because the check asked whether the host was Windows. What a secret channel
    /// actually needs is that it reaches one account and cannot be observed by another, which a
    /// socket in a 0700 directory provides, so this is the case that proves the requirement was
    /// re-derived rather than waived.
    /// </remarks>
    [LinuxOnlyFact]
    public async Task TheSecretChannelRoundTripsOnASocketInAPrivateDirectory()
    {
        var options = ServerOptions("agent-secret.sock") with { FrameKind = IpcFrameKind.Secret };
        Assert.True(UnixDomainSocketIpc.Transport.SupportsConfidentialChannel(options.Endpoint));

        await using var server = UnixDomainSocketIpc.CreateServer(
            options,
            static async (session, cancellationToken) =>
            {
                var request = await session.ReceiveSecretAsync(cancellationToken);
                Assert.Equal([1, 2, 3], request.Payload.SecretMaterial);
                await session.SendSecretAsync(
                    new SecretIpcResponseEnvelope(
                        SecretVaultIpcMessageTypes.EnrollResponse,
                        request.RequestId,
                        1,
                        new SecretVaultResponse(
                            SecretVaultIpcContract.CurrentVersion,
                            SecretVaultOperation.Enroll,
                            Succeeded: true,
                            "shs_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                            Version: 1)),
                    cancellationToken);
            });
        await StartAsync(server);

        await using var client = UnixDomainSocketIpc.CreateClient(
            ClientOptions(options.Endpoint) with { FrameKind = IpcFrameKind.Secret });
        _ = await client.ConnectAsync();
        var requestId = Guid.NewGuid();
        await client.SendSecretAsync(new SecretIpcRequestEnvelope(
            SecretVaultIpcMessageTypes.EnrollRequest,
            requestId,
            1,
            new SecretVaultRequest(
                SecretVaultIpcContract.CurrentVersion,
                SecretVaultOperation.Enroll,
                SecretMaterialPurpose.Password,
                Reference: null,
                SecretMaterial: [1, 2, 3])));
        var response = await client.ReceiveSecretAsync();

        Assert.Equal(requestId, response.RequestId);
        Assert.Equal(SecretVaultIpcMessageTypes.EnrollResponse, response.MessageType);
        Assert.True(response.Payload.Succeeded);
    }

    [LinuxOnlyFact]
    public void AnAbstractNamespaceSocketIsRefused()
    {
        // No filesystem entry means no permissions, so any process in the network namespace could
        // connect. This is the one way to lose the boundary silently on Linux.
        var error = Assert.Throws<ArgumentException>(() =>
            UnixDomainSocketIpc.Transport.ValidateEndpoint(
                new UnixSocketEndpoint("\0storagehub-abstract")));

        Assert.Contains("abstract-namespace", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [LinuxOnlyFact]
    public void APathOverTheAddressLimitIsRefusedWhereItIsConfigured()
    {
        var tooLong = "/tmp/" + new string('p', UnixIpcSocketDirectory.MaximumSocketPathLength);
        var error = Assert.Throws<ArgumentException>(() =>
            UnixDomainSocketIpc.Transport.ValidateEndpoint(new UnixSocketEndpoint(tooLong)));

        Assert.Contains("byte limit", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [LinuxOnlyFact]
    public void ARelativePathIsRefused()
    {
        _ = Assert.Throws<ArgumentException>(() =>
            UnixDomainSocketIpc.Transport.ValidateEndpoint(new UnixSocketEndpoint("agent.sock")));
    }

    [LinuxOnlyFact]
    public void ADirectoryOtherUsersCanReadIsRefused()
    {
        var loose = Path.Combine(_directory, "loose");
        Directory.CreateDirectory(
            loose,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute);

        var error = Assert.Throws<IOException>(() => UnixIpcSocketDirectory.Verify(loose));
        Assert.Contains("only by its owner", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [LinuxOnlyFact]
    public void ASymlinkedDirectoryIsRefused()
    {
        // Without this, a user who can create the entry first could point it at a directory they
        // control and collect whatever the agent publishes there - the secret channel included.
        var real = Path.Combine(_directory, "real");
        Directory.CreateDirectory(
            real,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var link = Path.Combine(_directory, "link");
        Directory.CreateSymbolicLink(link, real);

        var error = Assert.Throws<IOException>(() => UnixIpcSocketDirectory.Verify(link));
        Assert.Contains("symbolic link", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [LinuxOnlyFact]
    public void ASecretChannelIsRefusedOnADirectoryThatIsNotPrivate()
    {
        var loose = Path.Combine(_directory, "loose-secret");
        Directory.CreateDirectory(
            loose,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        Assert.False(UnixDomainSocketIpc.Transport.SupportsConfidentialChannel(
            new UnixSocketEndpoint(Path.Combine(loose, "agent-secret.sock"))));
    }

    [LinuxOnlyFact]
    public async Task ASocketLeftBehindByADeadAgentIsReplaced()
    {
        // bind() reports EADDRINUSE for any existing file, and a Unix socket file outlives the
        // process that bound it, so a crashed agent would otherwise block every later start.
        var options = ServerOptions();
        var path = ((UnixSocketEndpoint)options.Endpoint).SocketPath;
        await File.WriteAllTextAsync(path, string.Empty);
        Assert.True(File.Exists(path));

        await using var server = UnixDomainSocketIpc.CreateServer(options);
        await StartAsync(server);

        await using var client = UnixDomainSocketIpc.CreateClient(ClientOptions(options.Endpoint));
        Assert.True((await client.ConnectAsync()).Accepted);
    }

    [LinuxOnlyFact]
    public async Task ALiveAgentIsNotEvictedByASecondOne()
    {
        var options = ServerOptions();
        await using var first = UnixDomainSocketIpc.CreateServer(options);
        await StartAsync(first);

        await using var second = UnixDomainSocketIpc.CreateServer(options);
        _ = await second.InitializeAsync(CancellationToken.None);
        var error = await Assert.ThrowsAsync<IOException>(
            () => second.StartAsync(CancellationToken.None));

        Assert.Contains("already listening", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An agent that is not running is an <see cref="IOException"/>, as it is on Windows.
    /// </summary>
    /// <remarks>
    /// The transport used to let the raw <c>SocketException</c> out, and nothing in the desktop
    /// catches one: both <c>RemoteBrowserErrors.IsExpected</c> and
    /// <c>DesktopAgentAvailability.IsTransportFault</c> decide "the agent is unavailable" from a
    /// list that names IOException, which a SocketException is not. The same missing agent that
    /// showed an offline banner on Windows therefore reached the screen on Linux as an unhandled
    /// exception.
    ///
    /// This is the shape, not the message, so the assertion is on the type. It would have caught
    /// the original, and it is the sort of thing only running on Linux can catch.
    /// </remarks>
    [LinuxOnlyFact]
    public async Task ConnectingWithNoAgentListeningFailsTheWayWindowsDoes()
    {
        var endpoint = new UnixSocketEndpoint(Path.Combine(_directory, "nobody-is-home.sock"));

        await using var client = UnixDomainSocketIpc.CreateClient(ClientOptions(endpoint));

        await Assert.ThrowsAnyAsync<IOException>(() => client.ConnectAsync());
    }

    private IpcServerOptions ServerOptions(string name = "agent.sock") => new()
    {
        Endpoint = new UnixSocketEndpoint(Path.Combine(_directory, name)),
        AgentInstanceId = Guid.NewGuid(),
        AgentVersion = "1.0.0-tests",
        HandshakeTimeout = TimeSpan.FromSeconds(2),
    };

    private static IpcClientOptions ClientOptions(IpcEndpoint endpoint) => new()
    {
        Endpoint = endpoint,
        ClientName = "StorageHub.Tests",
        ClientVersion = "1.0.0-tests",
        ClientInstanceId = Guid.NewGuid(),
        ConnectTimeout = TimeSpan.FromSeconds(2),
        MaxConnectAttempts = 3,
    };

    private static async Task StartAsync(IpcServerSubsystem server)
    {
        _ = await server.InitializeAsync(CancellationToken.None);
        await server.StartAsync(CancellationToken.None);
    }

    private sealed record TestPayload(string Value);
}
