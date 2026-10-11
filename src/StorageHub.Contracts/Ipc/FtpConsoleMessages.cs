using System.Text.Json.Serialization;

namespace StorageHub.Contracts.Ipc;

/// <summary>
/// The FTP command console's contract: one raw command, typed by somebody, sent to a saved FTP or
/// FTPS connection, and the server's reply to it.
/// </summary>
/// <remarks>
/// <para>
/// One command per request, never a script, so every line the server sees is one somebody typed
/// and saw answered. The agent runs it on a control connection the library hands out for the
/// purpose and then throws away, so nothing the command changes on the server's side of the
/// session -- working directory, transfer type, a half-finished rename -- reaches the sessions the
/// panes and the queue use.
/// </para>
/// <para>
/// Commands that open a data connection, log in, negotiate TLS or end the session are refused on
/// both sides by <see cref="FtpConsoleCommandPolicy"/>: the desktop to say why before anything is
/// sent, the agent because it never trusts the desktop to have asked.
/// </para>
/// </remarks>
public static class FtpConsoleIpcContract
{
    public const int CurrentVersion = 1;

    public static bool IsSupported(int version) => version == CurrentVersion;
}

/// <summary>Bounds for the console, both ways.</summary>
public static class FtpConsoleIpcLimits
{
    /// <summary>
    /// The longest command accepted, in characters. RFC 959 sets none, but 512 is what common
    /// servers (vsftpd, ProFTPD, FileZilla Server) read into a line before they refuse it, and
    /// a path a pane can reach fits well inside it.
    /// </summary>
    public const int MaximumCommandLength = 512;

    /// <summary>The longest verb, so <c>XSHA256</c> and <c>HASH</c> fit and a run of letters does not.</summary>
    public const int MaximumVerbLength = 12;

    /// <summary>
    /// The most reply text returned, in characters. A <c>STAT</c> or <c>HELP</c> fits; a server that
    /// answers with a directory's worth of text is cut here and the reply says so.
    /// </summary>
    public const int MaximumReplyLength = 16_384;

    /// <summary>A reply code: three digits, as RFC 959 has it.</summary>
    public const int ReplyCodeLength = 3;

    /// <summary>How long the agent waits for the server's reply before giving up on the command.</summary>
    public static TimeSpan CommandTimeout { get; } = TimeSpan.FromSeconds(30);
}

public static class FtpConsoleIpcMessageTypes
{
    public const string ExecuteRequest = "ftp-console.execute.request";
    public const string ExecuteResponse = "ftp-console.execute.response";
}

/// <summary>Why a command is not sent.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FtpConsoleRefusal>))]
public enum FtpConsoleRefusal
{
    /// <summary>Sent as typed.</summary>
    None,

    /// <summary>Empty, too long, carrying a line break or another control character, or with no verb.</summary>
    Malformed,

    /// <summary>Opens or depends on a data connection (PASV, PORT, LIST, RETR, STOR...): the panes and the queue do transfers.</summary>
    DataConnection,

    /// <summary>Logs in or out, or ends the session (USER, PASS, ACCT, REIN, QUIT).</summary>
    Login,

    /// <summary>Negotiates or drops TLS on the control or data channel (AUTH, PROT, PBSZ, CCC).</summary>
    Security,

    /// <summary>
    /// Sets something that lasts for the session (CWD, TYPE, MODE, OPTS, REST, RNFR...), which would
    /// be lost: each command runs on a control connection of its own.
    /// </summary>
    SessionSetting
}

/// <summary>
/// What may be sent from the console, decided by the command's verb.
/// </summary>
/// <remarks>
/// <para>
/// A deny list rather than an allow list, because servers add their own verbs (SITE subcommands,
/// HASH, XCRC, AVBL, CSID...) and the point of a console is to reach them. What is denied is what
/// would hang, mislead or break: a command that waits on a data connection nobody opened, a login
/// that would replace the saved one, a TLS change the library did not make, and settings that last
/// for a session the console throws away after one reply.
/// </para>
/// <para>
/// The verb is the first word, compared without regard to case, as servers do.
/// </para>
/// </remarks>
public static class FtpConsoleCommandPolicy
{
    private static readonly Dictionary<string, FtpConsoleRefusal> Refused = new(StringComparer.OrdinalIgnoreCase)
    {
        // A data connection: the reply to these comes over a second socket, or sets one up.
        ["PASV"] = FtpConsoleRefusal.DataConnection,
        ["EPSV"] = FtpConsoleRefusal.DataConnection,
        ["SPSV"] = FtpConsoleRefusal.DataConnection,
        ["LPSV"] = FtpConsoleRefusal.DataConnection,
        ["PORT"] = FtpConsoleRefusal.DataConnection,
        ["EPRT"] = FtpConsoleRefusal.DataConnection,
        ["LPRT"] = FtpConsoleRefusal.DataConnection,
        ["LIST"] = FtpConsoleRefusal.DataConnection,
        ["NLST"] = FtpConsoleRefusal.DataConnection,
        ["MLSD"] = FtpConsoleRefusal.DataConnection,
        ["RETR"] = FtpConsoleRefusal.DataConnection,
        ["STOR"] = FtpConsoleRefusal.DataConnection,
        ["STOU"] = FtpConsoleRefusal.DataConnection,
        ["APPE"] = FtpConsoleRefusal.DataConnection,
        ["ABOR"] = FtpConsoleRefusal.DataConnection,

        // The login the connection was saved with is the one it uses.
        ["USER"] = FtpConsoleRefusal.Login,
        ["PASS"] = FtpConsoleRefusal.Login,
        ["ACCT"] = FtpConsoleRefusal.Login,
        ["REIN"] = FtpConsoleRefusal.Login,
        ["QUIT"] = FtpConsoleRefusal.Login,
        ["HOST"] = FtpConsoleRefusal.Login,

        // TLS is negotiated as the connection's settings say, and only by the library.
        ["AUTH"] = FtpConsoleRefusal.Security,
        ["PROT"] = FtpConsoleRefusal.Security,
        ["PBSZ"] = FtpConsoleRefusal.Security,
        ["CCC"] = FtpConsoleRefusal.Security,
        ["ADAT"] = FtpConsoleRefusal.Security,
        ["MIC"] = FtpConsoleRefusal.Security,
        ["CONF"] = FtpConsoleRefusal.Security,
        ["ENC"] = FtpConsoleRefusal.Security,

        // Settings that last for a session; this one ends with the reply.
        ["CWD"] = FtpConsoleRefusal.SessionSetting,
        ["XCWD"] = FtpConsoleRefusal.SessionSetting,
        ["CDUP"] = FtpConsoleRefusal.SessionSetting,
        ["XCUP"] = FtpConsoleRefusal.SessionSetting,
        ["TYPE"] = FtpConsoleRefusal.SessionSetting,
        ["MODE"] = FtpConsoleRefusal.SessionSetting,
        ["STRU"] = FtpConsoleRefusal.SessionSetting,
        ["OPTS"] = FtpConsoleRefusal.SessionSetting,
        ["REST"] = FtpConsoleRefusal.SessionSetting,
        ["RNFR"] = FtpConsoleRefusal.SessionSetting,
        ["RNTO"] = FtpConsoleRefusal.SessionSetting,
    };

    /// <summary>The verbs refused, for a test or a help text to list.</summary>
    public static IReadOnlyCollection<string> RefusedVerbs => Refused.Keys;

    /// <summary>Whether a command may be sent, and if not, why.</summary>
    public static FtpConsoleRefusal Check(string? command)
    {
        if (!IsWellFormed(command)) return FtpConsoleRefusal.Malformed;
        return Refused.TryGetValue(VerbOf(command!), out var refusal) ? refusal : FtpConsoleRefusal.None;
    }

    /// <summary>
    /// One line, within the length, with no control character anywhere -- a CR or LF would let one
    /// request carry a second command -- starting with a verb of letters and digits.
    /// </summary>
    public static bool IsWellFormed(string? command)
    {
        if (string.IsNullOrWhiteSpace(command) ||
            command.Length > FtpConsoleIpcLimits.MaximumCommandLength ||
            command.Any(char.IsControl) ||
            char.IsWhiteSpace(command[0]) ||
            char.IsWhiteSpace(command[^1]))
        {
            return false;
        }

        var verb = VerbOf(command);
        return verb.Length is > 0 and <= FtpConsoleIpcLimits.MaximumVerbLength &&
            char.IsAsciiLetter(verb[0]) &&
            verb.All(char.IsAsciiLetterOrDigit);
    }

    /// <summary>The first word, as typed: up to the first space, tab or other white space.</summary>
    public static string VerbOf(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var end = 0;
        while (end < command.Length && !char.IsWhiteSpace(command[end])) end++;
        return command[..end];
    }

    /// <summary>
    /// Whether the command carries a password, so it is neither shown back nor kept in the history.
    /// </summary>
    /// <remarks>
    /// PASS and ACCT are refused anyway; this is what makes a refused one leave no trace. Looked
    /// for past leading white space and before any other check, so a malformed one is caught too.
    /// </remarks>
    public static bool CarriesSecret(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        var verb = VerbOf(command.TrimStart());
        return verb.Equals("PASS", StringComparison.OrdinalIgnoreCase) ||
            verb.Equals("ACCT", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Sends one command to a saved FTP or FTPS connection.</summary>
public sealed record FtpConsoleExecuteRequest(
    int ContractVersion,
    Guid ConnectionId,
    string Command)
{
    public bool HasValidBounds =>
        FtpConsoleIpcContract.IsSupported(ContractVersion) &&
        ConnectionId != Guid.Empty &&
        FtpConsoleCommandPolicy.IsWellFormed(Command);
}

/// <summary>
/// The server's reply: its three-digit code, whether that code is a success (1xx to 3xx), and its
/// text, every line of a multi-line reply included, as the server sent it.
/// </summary>
/// <param name="Text">
/// The lines, joined with <c>\n</c>; any other control character the server sent is replaced, so
/// nothing in a reply can move the transcript's cursor or ring a bell.
/// </param>
/// <param name="Truncated">Whether the text was cut at <see cref="FtpConsoleIpcLimits.MaximumReplyLength"/>.</param>
public sealed record FtpConsoleReply(
    string Code,
    bool Succeeded,
    string Text,
    bool Truncated = false)
{
    public bool HasValidBounds =>
        Code is { Length: FtpConsoleIpcLimits.ReplyCodeLength } &&
        Code.All(char.IsAsciiDigit) &&
        Text is not null &&
        Text.Length <= FtpConsoleIpcLimits.MaximumReplyLength &&
        !Text.Any(static character => character != '\n' && char.IsControl(character));
}

/// <summary>
/// What came of one command: the server's reply, or a failure that kept it from being sent or
/// answered. A reply with a 4xx or 5xx code is still a reply, not a failure.
/// </summary>
/// <remarks>The command is not echoed back; the desktop already has it, and it never leaves the desktop twice.</remarks>
public sealed record FtpConsoleExecuteResponse(
    int ContractVersion,
    Guid ConnectionId,
    FtpConsoleReply? Reply,
    StorageIpcFailure? Failure = null)
{
    public bool HasValidBounds =>
        FtpConsoleIpcContract.IsSupported(ContractVersion) &&
        ConnectionId != Guid.Empty &&
        (Reply is null) != (Failure is null) &&
        (Reply is null || Reply.HasValidBounds) &&
        (Failure is null || IsValidFailure(Failure));

    private static bool IsValidFailure(StorageIpcFailure failure) =>
        !string.IsNullOrWhiteSpace(failure.Code) &&
        failure.Code.Length <= StorageIpcLimits.MaximumFailureCodeLength &&
        !string.IsNullOrWhiteSpace(failure.Message) &&
        failure.Message.Length <= StorageIpcLimits.MaximumFailureMessageLength &&
        !failure.Message.Any(char.IsControl) &&
        Enum.IsDefined(failure.Category);
}
