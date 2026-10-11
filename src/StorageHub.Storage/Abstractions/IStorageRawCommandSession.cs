using StorageHub.Contracts.Results;

namespace StorageHub.Storage.Abstractions;

/// <summary>A protocol command's reply: the server's code, whether it means success, and its lines.</summary>
/// <param name="Lines">Every line of the reply, a multi-line one's leading lines included, without line ends.</param>
public sealed record StorageRawCommandReply(string Code, bool Succeeded, IReadOnlyList<string> Lines);

/// <summary>
/// Optional endpoint capability for sending one raw protocol command, which today means an FTP
/// or FTPS command for the command console.
/// </summary>
/// <remarks>
/// <para>
/// Its presence says only that the session can ask; a provider with no command channel fails
/// with an unsupported failure. The caller decides what may be sent: the session checks only that
/// the command is one line, and sends it as it is.
/// </para>
/// <para>
/// The command runs on a control connection of its own that is closed afterwards, never on one the
/// session will use again, so whatever it changes on the server's side of the session is gone with
/// it. A rejected command is a reply with a failure code, not a failed result.
/// </para>
/// </remarks>
public interface IStorageRawCommandSession
{
    ValueTask<StorageResult<StorageRawCommandReply>> ExecuteRawCommandAsync(
        string command,
        CancellationToken cancellationToken = default);
}
