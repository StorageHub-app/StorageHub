using System.Text;
using System.Text.Json;
using StorageHub.Contracts.Ipc;
using StorageHub.Contracts.Results;
using StorageHub.Domain.Identifiers;
using StorageHub.Ipc;
using StorageHub.Storage.Abstractions;
using StorageHub.Sync;

namespace StorageHub.Agent.Host;

/// <summary>
/// The FTP command console: one raw command, typed in the desktop, sent to a saved FTP or FTPS
/// connection, and the server's reply sent back.
/// </summary>
/// <remarks>
/// <para>
/// The saved connection is opened as the object inspector and Properties open it, through the
/// shared connector, so its secrets are read from the vault here and never reach the desktop. The
/// command itself goes out on a control connection the library leases for it and closes after the
/// reply (<see cref="IStorageRawCommandSession"/>), so a command cannot leave a pooled session in a
/// state the panes and the queue do not expect, and a command cut off by the timeout cannot leave
/// half a reply waiting to be read as the answer to the next one.
/// </para>
/// <para>
/// The desktop is not trusted to have checked anything. The bounds, and
/// <see cref="FtpConsoleCommandPolicy"/>'s refusals, are applied again here before the connection
/// is opened, and the whole exchange is bounded by <see cref="FtpConsoleIpcLimits.CommandTimeout"/>.
/// What the server says is returned as it said it, cut at the reply limit and with any control
/// character but the line break replaced; what the library says when it fails is not, because its
/// text can name a host or an account.
/// </para>
/// </remarks>
public sealed class FtpConsoleIpcCommandService : IAgentIpcCommandHandler
{
    private readonly ISyncEndpointConnector _connector;
    private readonly TimeSpan _timeout;

    public FtpConsoleIpcCommandService(ISyncEndpointConnector connector, TimeSpan? timeout = null)
    {
        _connector = connector ?? throw new ArgumentNullException(nameof(connector));
        _timeout = timeout ?? FtpConsoleIpcLimits.CommandTimeout;
        if (_timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public bool CanHandle(string messageType) => messageType is FtpConsoleIpcMessageTypes.ExecuteRequest;

    public async ValueTask<AgentIpcCommandResponse> HandleAsync(
        IpcEnvelope request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MessageType != FtpConsoleIpcMessageTypes.ExecuteRequest)
        {
            return AgentIpcCommandResponse.Error(
                "ipc.message.unsupported",
                "The requested IPC operation is not supported by this agent version.");
        }

        FtpConsoleExecuteRequest execute;
        try { execute = request.DeserializePayload<FtpConsoleExecuteRequest>(); }
        catch (JsonException) { return InvalidPayload(); }
        if (execute is null) return InvalidPayload();

        if (!FtpConsoleIpcContract.IsSupported(execute.ContractVersion))
        {
            return AgentIpcCommandResponse.Error(
                "ipc.contract.unsupported",
                "The requested FTP console contract version is not supported.");
        }

        if (!execute.HasValidBounds)
        {
            return AgentIpcCommandResponse.Error(
                "ipc.payload.invalid",
                "The FTP console request exceeded a permitted bound or contained an invalid value.");
        }

        var refusal = FtpConsoleCommandPolicy.Check(execute.Command);
        if (refusal != FtpConsoleRefusal.None) return Result(execute, Refused(refusal));

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        try
        {
            var opened = await _connector.OpenAsync(new ConnectionProfileId(execute.ConnectionId), deadline.Token)
                .ConfigureAwait(false);
            if (opened.IsFailure) return Result(execute, Sanitize(opened.Error));
            await using var connection = opened.Value;
            if (connection.Session is not IStorageRawCommandSession raw)
            {
                return Result(execute, NotFtp());
            }

            var replied = await raw.ExecuteRawCommandAsync(execute.Command, deadline.Token).ConfigureAwait(false);
            if (replied.IsFailure) return Result(execute, Sanitize(replied.Error));
            return Clean(replied.Value) is { } reply
                ? AgentIpcCommandResponse.Create(
                    FtpConsoleIpcMessageTypes.ExecuteResponse,
                    new FtpConsoleExecuteResponse(execute.ContractVersion, execute.ConnectionId, reply))
                : Result(execute, new StorageIpcFailure(
                    "ftp.console.reply.invalid",
                    StorageIpcFailureCategory.Provider,
                    "The server's reply had no reply code.",
                    IsTransient: false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested) { return Result(execute, TimedOut()); }
        catch (Exception) { return Result(execute, Unavailable()); }
    }

    /// <summary>
    /// The reply as the contract carries it: the code, success by its first digit, and the lines
    /// joined, with control characters but the line break replaced and the whole cut at the limit.
    /// Null when the code is not three digits, which is not a reply.
    /// </summary>
    internal static FtpConsoleReply? Clean(StorageRawCommandReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        var code = reply.Code?.Trim() ?? string.Empty;
        if (code.Length != FtpConsoleIpcLimits.ReplyCodeLength || !code.All(char.IsAsciiDigit)) return null;

        var text = new StringBuilder();
        var truncated = false;
        foreach (var line in reply.Lines ?? [])
        {
            if (text.Length > 0) text.Append('\n');
            foreach (var character in line ?? string.Empty)
            {
                text.Append(char.IsControl(character) ? '�' : character);
            }

            if (text.Length > FtpConsoleIpcLimits.MaximumReplyLength)
            {
                truncated = true;
                break;
            }
        }

        if (text.Length > FtpConsoleIpcLimits.MaximumReplyLength)
        {
            text.Length = FtpConsoleIpcLimits.MaximumReplyLength;
            truncated = true;
        }

        return new FtpConsoleReply(code, code[0] is '1' or '2' or '3', text.ToString(), truncated);
    }

    /// <summary>Why a command was not sent, in the words the desktop also has.</summary>
    internal static StorageIpcFailure Refused(FtpConsoleRefusal refusal)
    {
        var (code, message) = refusal switch
        {
            FtpConsoleRefusal.DataConnection => ("ftp.console.refused.data_connection",
                "Commands that open a data connection are not sent; use the panes for listings and transfers."),
            FtpConsoleRefusal.Login => ("ftp.console.refused.login",
                "The connection logs in with its saved account; login and logout commands are not sent."),
            FtpConsoleRefusal.Security => ("ftp.console.refused.security",
                "TLS is set by the connection's settings; security commands are not sent."),
            FtpConsoleRefusal.SessionSetting => ("ftp.console.refused.session_setting",
                "Each command runs on its own short session, so settings such as CWD or TYPE would not last."),
            _ => ("ftp.console.refused.malformed",
                "A command must be one line of at most 512 characters, starting with its verb.")
        };
        return new StorageIpcFailure(code, StorageIpcFailureCategory.Validation, message, IsTransient: false);
    }

    /// <summary>A failure in fixed words, never the library's or the server's own text.</summary>
    private static StorageIpcFailure Sanitize(StorageFailure failure)
    {
        if (failure.Code == "storage.raw_command.unsupported") return NotFtp();

        var (category, code, message) = failure.Kind switch
        {
            StorageFailureKind.NotFound => (StorageIpcFailureCategory.NotFound, "ftp.console.not_found",
                "The saved connection was not found."),
            StorageFailureKind.Validation => (StorageIpcFailureCategory.Validation, "ftp.console.validation",
                "The command could not be sent as typed."),
            StorageFailureKind.Unsupported => (StorageIpcFailureCategory.Unsupported, "ftp.console.unsupported",
                "This connection does not take raw commands."),
            StorageFailureKind.Unauthorized => (StorageIpcFailureCategory.Unauthorized, "ftp.console.unauthorized",
                "The server refused the saved account, or the connection is disabled."),
            StorageFailureKind.Security => (StorageIpcFailureCategory.Security, "ftp.console.security",
                "The connection requires a security or trust decision; test it from the connections panel."),
            StorageFailureKind.Timeout => (StorageIpcFailureCategory.Timeout, "ftp.console.timeout",
                "The server did not answer in time."),
            StorageFailureKind.Cancelled => (StorageIpcFailureCategory.Cancelled, "ftp.console.cancelled",
                "The command was cancelled."),
            StorageFailureKind.Unavailable => (StorageIpcFailureCategory.Unavailable, "ftp.console.unavailable",
                "The server could not be reached, or closed the connection."),
            _ => (StorageIpcFailureCategory.Provider, "ftp.console.failed",
                "The command could not be sent.")
        };
        return new StorageIpcFailure(code, category, message, failure.IsTransient);
    }

    private static StorageIpcFailure NotFtp() => new(
        "ftp.console.unsupported",
        StorageIpcFailureCategory.Unsupported,
        "Only FTP and FTPS connections take raw commands.",
        IsTransient: false);

    private static StorageIpcFailure TimedOut() => new(
        "ftp.console.timeout",
        StorageIpcFailureCategory.Timeout,
        "The server did not answer in time.",
        IsTransient: true);

    private static StorageIpcFailure Unavailable() => new(
        "ftp.console.unavailable",
        StorageIpcFailureCategory.Unavailable,
        "The server could not be reached, or closed the connection.",
        IsTransient: true);

    private static AgentIpcCommandResponse InvalidPayload() => AgentIpcCommandResponse.Error(
        "ipc.payload.invalid",
        "The FTP console request payload was invalid.");

    private static AgentIpcCommandResponse Result(FtpConsoleExecuteRequest request, StorageIpcFailure failure) =>
        AgentIpcCommandResponse.Create(
            FtpConsoleIpcMessageTypes.ExecuteResponse,
            new FtpConsoleExecuteResponse(request.ContractVersion, request.ConnectionId, Reply: null, failure));
}
