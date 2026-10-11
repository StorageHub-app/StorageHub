using StorageHub.Contracts.Ipc;
using StorageHub.Ipc;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The FTP console below its window: what the client lets through each way, and how the command
/// history is walked.
/// </summary>
public sealed class FtpConsoleClientTests
{
    private static readonly Guid Connection = Guid.NewGuid();

    [Fact]
    public async Task TheClientSendsOneCommandAndReturnsTheMatchingReply()
    {
        var transport = new FakeTransport(request => IpcEnvelope.Create(
            FtpConsoleIpcMessageTypes.ExecuteResponse,
            request.RequestId,
            1,
            new FtpConsoleExecuteResponse(
                FtpConsoleIpcContract.CurrentVersion,
                Connection,
                new FtpConsoleReply("215", true, "215 UNIX Type: L8"))));
        await using var client = new NamedPipeFtpConsoleAgentClient(transport);

        var response = await client.ExecuteAsync(
            new FtpConsoleExecuteRequest(FtpConsoleIpcContract.CurrentVersion, Connection, "SYST"));

        Assert.Equal("215", response.Reply?.Code);
        Assert.Equal(FtpConsoleIpcMessageTypes.ExecuteRequest, transport.Sent?.MessageType);
        Assert.Equal("SYST", transport.Sent?.DeserializePayload<FtpConsoleExecuteRequest>().Command);
    }

    [Fact]
    public async Task AnInvalidRequestIsNotSent()
    {
        var transport = new FakeTransport(static _ => throw new InvalidOperationException("Nothing should be sent."));
        await using var client = new NamedPipeFtpConsoleAgentClient(transport);

        await Assert.ThrowsAsync<ArgumentException>(() => client.ExecuteAsync(
            new FtpConsoleExecuteRequest(FtpConsoleIpcContract.CurrentVersion, Connection, "NOOP\r\nDELE x")));
        Assert.Null(transport.Sent);
    }

    /// <summary>A reply for another connection, or one outside the contract, is not believed.</summary>
    [Fact]
    public async Task AReplyThatDoesNotMatchIsRefusedAndThePipeDropped()
    {
        var other = new FakeTransport(request => IpcEnvelope.Create(
            FtpConsoleIpcMessageTypes.ExecuteResponse,
            request.RequestId,
            1,
            new FtpConsoleExecuteResponse(
                FtpConsoleIpcContract.CurrentVersion,
                Guid.NewGuid(),
                new FtpConsoleReply("215", true, "215 UNIX"))));
        await using (var client = new NamedPipeFtpConsoleAgentClient(other))
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => client.ExecuteAsync(
                new FtpConsoleExecuteRequest(FtpConsoleIpcContract.CurrentVersion, Connection, "SYST")));
        }

        Assert.Equal(1, other.DisconnectCount);

        var escaped = new FakeTransport(request => IpcEnvelope.Create(
            FtpConsoleIpcMessageTypes.ExecuteResponse,
            request.RequestId,
            1,
            new FtpConsoleExecuteResponse(
                FtpConsoleIpcContract.CurrentVersion,
                Connection,
                new FtpConsoleReply("215", true, "\u001b]0;owned\u0007"))));
        await using (var client = new NamedPipeFtpConsoleAgentClient(escaped))
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => client.ExecuteAsync(
                new FtpConsoleExecuteRequest(FtpConsoleIpcContract.CurrentVersion, Connection, "SYST")));
        }
    }

    [Fact]
    public void HistoryWalksBackAndForthAndKeepsTheDraft()
    {
        var history = new FtpConsoleHistory();
        history.Add("SYST");
        history.Add("FEAT");
        history.Add("FEAT");

        Assert.Equal(["SYST", "FEAT"], history.Entries);
        Assert.Equal("FEAT", history.Previous("STA"));
        Assert.Equal("SYST", history.Previous("FEAT"));
        Assert.Null(history.Previous("SYST"));
        Assert.Equal("FEAT", history.Next());
        Assert.Equal("STA", history.Next());
        Assert.Null(history.Next());
    }

    [Fact]
    public void HistoryNeverKeepsAPassword()
    {
        var history = new FtpConsoleHistory();
        history.Add("PASS hunter2");
        history.Add("  pass hunter2");
        history.Add("ACCT billing");
        history.Add("NOOP");

        Assert.Equal(["NOOP"], history.Entries);
    }

    [Fact]
    public void HistoryKeepsTheNewestHundred()
    {
        var history = new FtpConsoleHistory();
        for (var i = 0; i < FtpConsoleHistory.Capacity + 5; i++) history.Add($"MDTM file{i}");

        Assert.Equal(FtpConsoleHistory.Capacity, history.Entries.Count);
        Assert.Equal("MDTM file5", history.Entries[0]);
    }

    private sealed class FakeTransport(Func<IpcEnvelope, IpcEnvelope> respond) : IStorageIpcTransport
    {
        public IpcEnvelope? Sent { get; private set; }

        public bool IsConnected { get; private set; }

        public int DisconnectCount { get; private set; }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            return Task.CompletedTask;
        }

        public ValueTask SendAsync(IpcEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Sent = envelope;
            return ValueTask.CompletedTask;
        }

        public ValueTask<IpcEnvelope> ReceiveAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(respond(Sent ?? throw new InvalidOperationException("No request was sent.")));

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            DisconnectCount++;
            IsConnected = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return ValueTask.CompletedTask;
        }
    }
}
