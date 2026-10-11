using System.Text.Json;
using StorageHub.Contracts.Ipc;

namespace StorageHub.Domain.Tests;

/// <summary>
/// The FTP console's contract: what a command may be, which verbs are never sent, and what a reply
/// may carry back.
/// </summary>
public sealed class FtpConsoleContractTests
{
    private static readonly Guid Connection = Guid.Parse("6f1e1c55-4f0b-4c39-9a41-2d1c2b7f0e11");

    [Theory]
    [InlineData("SYST")]
    [InlineData("FEAT")]
    [InlineData("STAT")]
    [InlineData("site chmod 644 /var/www/index.html")]
    [InlineData("MDTM /srv/ftp/årsrapport 2026.pdf")]
    [InlineData("XSHA256 /big.iso")]
    [InlineData("HELP SITE")]
    public void OrdinaryCommandsAreSent(string command)
    {
        Assert.Equal(FtpConsoleRefusal.None, FtpConsoleCommandPolicy.Check(command));
        Assert.True(new FtpConsoleExecuteRequest(FtpConsoleIpcContract.CurrentVersion, Connection, command).HasValidBounds);
    }

    [Theory]
    [InlineData("PASV", FtpConsoleRefusal.DataConnection)]
    [InlineData("epsv", FtpConsoleRefusal.DataConnection)]
    [InlineData("PORT 127,0,0,1,4,1", FtpConsoleRefusal.DataConnection)]
    [InlineData("EPRT |1|127.0.0.1|1025|", FtpConsoleRefusal.DataConnection)]
    [InlineData("LIST", FtpConsoleRefusal.DataConnection)]
    [InlineData("NLST /", FtpConsoleRefusal.DataConnection)]
    [InlineData("MLSD", FtpConsoleRefusal.DataConnection)]
    [InlineData("RETR a.txt", FtpConsoleRefusal.DataConnection)]
    [InlineData("STOR a.txt", FtpConsoleRefusal.DataConnection)]
    [InlineData("APPE a.txt", FtpConsoleRefusal.DataConnection)]
    [InlineData("ABOR", FtpConsoleRefusal.DataConnection)]
    [InlineData("USER root", FtpConsoleRefusal.Login)]
    [InlineData("PASS hunter2", FtpConsoleRefusal.Login)]
    [InlineData("ACCT billing", FtpConsoleRefusal.Login)]
    [InlineData("REIN", FtpConsoleRefusal.Login)]
    [InlineData("QUIT", FtpConsoleRefusal.Login)]
    [InlineData("AUTH TLS", FtpConsoleRefusal.Security)]
    [InlineData("PROT C", FtpConsoleRefusal.Security)]
    [InlineData("PBSZ 0", FtpConsoleRefusal.Security)]
    [InlineData("CCC", FtpConsoleRefusal.Security)]
    [InlineData("CWD /tmp", FtpConsoleRefusal.SessionSetting)]
    [InlineData("CDUP", FtpConsoleRefusal.SessionSetting)]
    [InlineData("TYPE I", FtpConsoleRefusal.SessionSetting)]
    [InlineData("MODE Z", FtpConsoleRefusal.SessionSetting)]
    [InlineData("OPTS UTF8 ON", FtpConsoleRefusal.SessionSetting)]
    [InlineData("REST 100", FtpConsoleRefusal.SessionSetting)]
    [InlineData("RNFR a.txt", FtpConsoleRefusal.SessionSetting)]
    public void CommandsThatWouldHangMisleadOrBreakAreRefused(string command, FtpConsoleRefusal expected)
    {
        Assert.Equal(expected, FtpConsoleCommandPolicy.Check(command));

        // Well formed, so the request crosses the pipe; the agent refuses it again on its side.
        Assert.True(new FtpConsoleExecuteRequest(FtpConsoleIpcContract.CurrentVersion, Connection, command).HasValidBounds);
    }

    [Fact]
    public void EveryRefusedVerbHasAReason()
    {
        foreach (var verb in FtpConsoleCommandPolicy.RefusedVerbs)
        {
            Assert.NotEqual(FtpConsoleRefusal.None, FtpConsoleCommandPolicy.Check(verb));
            Assert.NotEqual(FtpConsoleRefusal.Malformed, FtpConsoleCommandPolicy.Check(verb));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" SYST")]
    [InlineData("SYST ")]
    [InlineData("NOOP\r\nDELE important.db")]
    [InlineData("NOOP\nDELE important.db")]
    [InlineData("SITE\tCHMOD 777 x")]
    [InlineData("STAT \u0007")]
    [InlineData("/SYST")]
    [InlineData("1SYST")]
    [InlineData("ABCDEFGHIJKLM x")]
    public void MalformedCommandsAreRefusedAndCannotCrossThePipe(string command)
    {
        Assert.Equal(FtpConsoleRefusal.Malformed, FtpConsoleCommandPolicy.Check(command));
        Assert.False(new FtpConsoleExecuteRequest(FtpConsoleIpcContract.CurrentVersion, Connection, command).HasValidBounds);
    }

    [Fact]
    public void ACommandIsAtMostFiveHundredAndTwelveCharacters()
    {
        var longest = "SITE " + new string('x', FtpConsoleIpcLimits.MaximumCommandLength - 5);
        Assert.Equal(FtpConsoleRefusal.None, FtpConsoleCommandPolicy.Check(longest));
        Assert.Equal(FtpConsoleRefusal.Malformed, FtpConsoleCommandPolicy.Check(longest + "x"));
    }

    [Fact]
    public void RequestsNeedASupportedVersionAndAConnection()
    {
        Assert.False(new FtpConsoleExecuteRequest(FtpConsoleIpcContract.CurrentVersion + 1, Connection, "SYST").HasValidBounds);
        Assert.False(new FtpConsoleExecuteRequest(FtpConsoleIpcContract.CurrentVersion, Guid.Empty, "SYST").HasValidBounds);
        Assert.False(new FtpConsoleExecuteRequest(FtpConsoleIpcContract.CurrentVersion, Connection, null!).HasValidBounds);
    }

    [Theory]
    [InlineData("PASS hunter2", true)]
    [InlineData("pass hunter2", true)]
    [InlineData("  PASS hunter2", true)]
    [InlineData("PASS\thunter2", true)]
    [InlineData("ACCT billing", true)]
    [InlineData("PASSWORD x", false)]
    [InlineData("SITE PASS x", false)]
    [InlineData("SYST", false)]
    public void APasswordIsRecognisedHoweverItIsTyped(string command, bool secret) =>
        Assert.Equal(secret, FtpConsoleCommandPolicy.CarriesSecret(command));

    [Fact]
    public void RepliesAreBoundedAndCarryNoControlCharactersButLineBreaks()
    {
        Assert.True(new FtpConsoleReply("211", true, "211-Features:\n MDTM\n211 End").HasValidBounds);
        Assert.True(new FtpConsoleReply("550", false, string.Empty).HasValidBounds);
        Assert.False(new FtpConsoleReply("21", true, "x").HasValidBounds);
        Assert.False(new FtpConsoleReply("2a1", true, "x").HasValidBounds);
        Assert.False(new FtpConsoleReply("211", true, "a\rb").HasValidBounds);
        Assert.False(new FtpConsoleReply("211", true, "\u001b[31mred").HasValidBounds);
        Assert.False(new FtpConsoleReply("211", true, new string('x', FtpConsoleIpcLimits.MaximumReplyLength + 1)).HasValidBounds);
        Assert.True(new FtpConsoleReply("211", true, new string('x', FtpConsoleIpcLimits.MaximumReplyLength), Truncated: true).HasValidBounds);
    }

    [Fact]
    public void AResponseCarriesAReplyOrAFailureButNotBoth()
    {
        var reply = new FtpConsoleReply("215", true, "215 UNIX Type: L8");
        var failure = new StorageIpcFailure("ftp.console.timeout", StorageIpcFailureCategory.Timeout, "Too slow.", true);
        const int version = FtpConsoleIpcContract.CurrentVersion;

        Assert.True(new FtpConsoleExecuteResponse(version, Connection, reply).HasValidBounds);
        Assert.True(new FtpConsoleExecuteResponse(version, Connection, null, failure).HasValidBounds);
        Assert.False(new FtpConsoleExecuteResponse(version, Connection, null).HasValidBounds);
        Assert.False(new FtpConsoleExecuteResponse(version, Connection, reply, failure).HasValidBounds);
        Assert.False(new FtpConsoleExecuteResponse(version, Guid.Empty, reply).HasValidBounds);
        Assert.False(new FtpConsoleExecuteResponse(version, Connection, null, failure with { Message = "two\nlines" }).HasValidBounds);
    }

    [Fact]
    public void TheResponseRoundTripsThroughAnEnvelope()
    {
        var response = new FtpConsoleExecuteResponse(
            FtpConsoleIpcContract.CurrentVersion,
            Connection,
            new FtpConsoleReply("211", true, "211-Features:\n SIZE\n211 End", Truncated: true));
        var envelope = IpcEnvelope.Create(FtpConsoleIpcMessageTypes.ExecuteResponse, Guid.NewGuid(), 1, response);

        var read = JsonSerializer.Deserialize<IpcEnvelope>(JsonSerializer.Serialize(envelope))!
            .DeserializePayload<FtpConsoleExecuteResponse>();

        Assert.Equal(response.Reply, read.Reply);
        Assert.True(read.HasValidBounds);
    }
}
