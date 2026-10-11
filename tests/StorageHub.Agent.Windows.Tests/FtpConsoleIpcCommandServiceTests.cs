using System.Text.Json;
using StorageHub.Agent.Host;
using StorageHub.Contracts.Ipc;
using StorageHub.Contracts.Results;
using StorageHub.Domain.Capabilities;
using StorageHub.Domain.Identifiers;
using StorageHub.Domain.Storage;
using StorageHub.Ipc;
using StorageHub.Storage.Abstractions;
using StorageHub.Storage.Models;
using StorageHub.Sync;

namespace StorageHub.Agent.Windows.Tests;

/// <summary>
/// The agent's side of the FTP command console: the command is checked again here, sent to the
/// saved connection the desktop named, and the server's reply returned as it came, within bounds.
/// </summary>
public sealed class FtpConsoleIpcCommandServiceTests
{
    [Fact]
    public async Task ACommandIsSentToTheNamedConnectionAndItsReplyReturned()
    {
        var profileId = ConnectionProfileId.New();
        var session = new FakeRawSession(profileId)
        {
            Reply = new StorageRawCommandReply("211", true, ["211-Features:", " MDTM", " SIZE", "211 End"])
        };
        var (service, connector, connection) = Service(session);

        var response = await Send(service, new FtpConsoleExecuteRequest(
            FtpConsoleIpcContract.CurrentVersion, profileId.Value, "FEAT"));

        Assert.True(response.HasValidBounds);
        Assert.Null(response.Failure);
        var reply = Assert.IsType<FtpConsoleReply>(response.Reply);
        Assert.Equal("211", reply.Code);
        Assert.True(reply.Succeeded);
        Assert.Equal("211-Features:\n MDTM\n SIZE\n211 End", reply.Text);
        Assert.False(reply.Truncated);
        Assert.Equal(["FEAT"], session.Sent);
        Assert.Equal(profileId, connector.LastProfileId);
        Assert.True(connection.IsDisposed);
    }

    [Fact]
    public async Task AServerRefusalIsAReplyNotAFailure()
    {
        var profileId = ConnectionProfileId.New();
        var session = new FakeRawSession(profileId)
        {
            Reply = new StorageRawCommandReply("550", false, ["550 No such file or directory."])
        };
        var (service, _, _) = Service(session);

        var response = await Send(service, new FtpConsoleExecuteRequest(
            FtpConsoleIpcContract.CurrentVersion, profileId.Value, "MDTM /missing"));

        Assert.Null(response.Failure);
        Assert.False(response.Reply!.Succeeded);
        Assert.Equal("550", response.Reply.Code);
    }

    /// <summary>The desktop is not trusted to have checked: the agent refuses before opening anything.</summary>
    [Theory]
    [InlineData("PASV", "ftp.console.refused.data_connection")]
    [InlineData("RETR secrets.txt", "ftp.console.refused.data_connection")]
    [InlineData("USER admin", "ftp.console.refused.login")]
    [InlineData("PASS hunter2", "ftp.console.refused.login")]
    [InlineData("AUTH TLS", "ftp.console.refused.security")]
    [InlineData("CWD /", "ftp.console.refused.session_setting")]
    [InlineData("quit", "ftp.console.refused.login")]
    public async Task RefusedCommandsNeverReachTheConnection(string command, string code)
    {
        var profileId = ConnectionProfileId.New();
        var session = new FakeRawSession(profileId);
        var (service, connector, _) = Service(session);

        var response = await Send(service, new FtpConsoleExecuteRequest(
            FtpConsoleIpcContract.CurrentVersion, profileId.Value, command));

        Assert.True(response.HasValidBounds);
        Assert.Null(response.Reply);
        Assert.Equal(code, response.Failure?.Code);
        Assert.Equal(StorageIpcFailureCategory.Validation, response.Failure?.Category);
        Assert.Null(connector.LastProfileId);
        Assert.Empty(session.Sent);
        Assert.DoesNotContain("hunter2", JsonSerializer.Serialize(response), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NOOP\r\nDELE important.db")]
    [InlineData("")]
    [InlineData(" SYST")]
    public async Task AMalformedCommandIsAnInvalidRequest(string command)
    {
        var session = new FakeRawSession(ConnectionProfileId.New());
        var (service, connector, _) = Service(session);

        var response = await service.HandleAsync(IpcEnvelope.Create(
            FtpConsoleIpcMessageTypes.ExecuteRequest,
            Guid.NewGuid(),
            1,
            new FtpConsoleExecuteRequest(FtpConsoleIpcContract.CurrentVersion, session.ProfileId.Value, command)));

        Assert.Equal("ipc.payload.invalid", ErrorCode(response));
        Assert.Null(connector.LastProfileId);
    }

    [Fact]
    public async Task AnotherContractVersionIsSaidToBeUnsupported()
    {
        var session = new FakeRawSession(ConnectionProfileId.New());
        var (service, _, _) = Service(session);

        var response = await service.HandleAsync(IpcEnvelope.Create(
            FtpConsoleIpcMessageTypes.ExecuteRequest,
            Guid.NewGuid(),
            1,
            new FtpConsoleExecuteRequest(FtpConsoleIpcContract.CurrentVersion + 1, session.ProfileId.Value, "SYST")));

        Assert.Equal("ipc.contract.unsupported", ErrorCode(response));
    }

    /// <summary>
    /// What the server says is shown as it said it, except what could move a terminal's cursor,
    /// and cut at the limit with the cut said.
    /// </summary>
    [Fact]
    public async Task RepliesAreCutAtTheLimitAndCleanedOfControlCharacters()
    {
        var profileId = ConnectionProfileId.New();
        var session = new FakeRawSession(profileId)
        {
            Reply = new StorageRawCommandReply(
                "211",
                true,
                ["211-\u001b[31mred\u0007", .. Enumerable.Repeat(new string('x', 1_000), 40), "211 End"])
        };
        var (service, _, _) = Service(session);

        var response = await Send(service, new FtpConsoleExecuteRequest(
            FtpConsoleIpcContract.CurrentVersion, profileId.Value, "STAT"));

        var reply = Assert.IsType<FtpConsoleReply>(response.Reply);
        Assert.True(reply.HasValidBounds);
        Assert.True(reply.Truncated);
        Assert.Equal(FtpConsoleIpcLimits.MaximumReplyLength, reply.Text.Length);
        Assert.StartsWith("211-�[31mred�\n", reply.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReplyWithoutACodeIsNotPassedOn()
    {
        var profileId = ConnectionProfileId.New();
        var session = new FakeRawSession(profileId) { Reply = new StorageRawCommandReply("", false, ["garbage"]) };
        var (service, _, _) = Service(session);

        var response = await Send(service, new FtpConsoleExecuteRequest(
            FtpConsoleIpcContract.CurrentVersion, profileId.Value, "SYST"));

        Assert.Null(response.Reply);
        Assert.Equal("ftp.console.reply.invalid", response.Failure?.Code);
    }

    [Fact]
    public async Task ACommandThatTakesTooLongIsGivenUp()
    {
        var profileId = ConnectionProfileId.New();
        var session = new FakeRawSession(profileId) { Hang = true };
        var connection = new FakeConnection(session);
        var service = new FtpConsoleIpcCommandService(
            new FakeConnector(StorageResult<ISyncEndpointConnection>.Success(connection)),
            TimeSpan.FromMilliseconds(100));

        var response = await Send(service, new FtpConsoleExecuteRequest(
            FtpConsoleIpcContract.CurrentVersion, profileId.Value, "STAT"));

        Assert.True(response.HasValidBounds);
        Assert.Equal("ftp.console.timeout", response.Failure?.Code);
        Assert.Equal(StorageIpcFailureCategory.Timeout, response.Failure?.Category);
        Assert.True(connection.IsDisposed);
    }

    [Fact]
    public async Task AConnectionThatIsNotFtpSaysSo()
    {
        var profileId = ConnectionProfileId.New();
        var plain = new FakePlainSession(profileId);
        var (service, _, _) = Service(plain);

        var response = await Send(service, new FtpConsoleExecuteRequest(
            FtpConsoleIpcContract.CurrentVersion, profileId.Value, "SYST"));

        Assert.Equal("ftp.console.unsupported", response.Failure?.Code);
    }

    /// <summary>The library's words are not passed on: they can name a host or an account.</summary>
    [Fact]
    public async Task FailuresAreInTheConsolesOwnWords()
    {
        var connector = new FakeConnector(StorageResult<ISyncEndpointConnection>.Fail(new StorageFailure(
            "storage.unauthorized",
            StorageFailureKind.Unauthorized,
            "530 Login incorrect for user deploy@ftp.example.com")));
        var service = new FtpConsoleIpcCommandService(connector);

        var response = await Send(service, new FtpConsoleExecuteRequest(
            FtpConsoleIpcContract.CurrentVersion, Guid.NewGuid(), "SYST"));

        Assert.Equal("ftp.console.unauthorized", response.Failure?.Code);
        Assert.DoesNotContain("deploy", response.Failure!.Message, StringComparison.Ordinal);
    }

    private static (FtpConsoleIpcCommandService, FakeConnector, FakeConnection) Service(IStorageEndpointSession session)
    {
        var connection = new FakeConnection(session);
        var connector = new FakeConnector(StorageResult<ISyncEndpointConnection>.Success(connection));
        return (new FtpConsoleIpcCommandService(connector), connector, connection);
    }

    private static async Task<FtpConsoleExecuteResponse> Send(FtpConsoleIpcCommandService service, FtpConsoleExecuteRequest request)
    {
        var response = await service.HandleAsync(IpcEnvelope.Create(
            FtpConsoleIpcMessageTypes.ExecuteRequest, Guid.NewGuid(), 1, request));
        Assert.Equal(FtpConsoleIpcMessageTypes.ExecuteResponse, response.MessageType);
        return response.Payload.Deserialize<FtpConsoleExecuteResponse>() ?? throw new InvalidOperationException("No payload.");
    }

    private static string? ErrorCode(AgentIpcCommandResponse response) =>
        response.Payload.TryGetProperty("Code", out var code) ? code.GetString() : null;

    private sealed class FakeConnector(StorageResult<ISyncEndpointConnection> result) : ISyncEndpointConnector
    {
        public ConnectionProfileId? LastProfileId { get; private set; }

        public ValueTask<StorageResult<ISyncEndpointConnection>> OpenAsync(
            ConnectionProfileId profileId,
            CancellationToken cancellationToken = default)
        {
            LastProfileId = profileId;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FakeConnection(IStorageEndpointSession session) : ISyncEndpointConnection
    {
        public IStorageEndpointSession Session { get; } = session;

        public bool IsDisposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeRawSession(ConnectionProfileId profileId) : FakePlainSession(profileId), IStorageRawCommandSession
    {
        public StorageRawCommandReply Reply { get; init; } = new("200", true, ["200 OK"]);

        public bool Hang { get; init; }

        public List<string> Sent { get; } = [];

        public async ValueTask<StorageResult<StorageRawCommandReply>> ExecuteRawCommandAsync(
            string command,
            CancellationToken cancellationToken = default)
        {
            Sent.Add(command);
            if (Hang) await Task.Delay(Timeout.Infinite, cancellationToken);
            return StorageResult<StorageRawCommandReply>.Success(Reply);
        }
    }

    private class FakePlainSession(ConnectionProfileId profileId) : IStorageEndpointSession
    {
        public ConnectionProfileId ProfileId { get; } = profileId;

        public string RootIdentity => "ftp:root";

        public EffectiveStorageCapabilities Capabilities => EffectiveStorageCapabilities.None;

        public ValueTask<StorageResult<StorageEntry>> GetEntryAsync(
            StorageAddress address,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<StorageResult> CheckHealthAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<StorageResult<StoragePage>> ListAsync(
            StorageAddress address,
            StorageListRequest? request = null,
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

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
