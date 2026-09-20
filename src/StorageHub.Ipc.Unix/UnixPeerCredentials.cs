using System.Globalization;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using StorageHub.Infrastructure.Unix;

namespace StorageHub.Ipc.Unix;

/// <summary>
/// Who is on the other end of a Unix socket, and who owns a path, as the kernel reports them.
/// </summary>
/// <remarks>
/// This is the whole of the trust model on Linux, so it is worth being precise about why it can be
/// trusted. SO_PEERCRED is filled in by the kernel at connect time from the peer's own credentials;
/// the peer never sends it and cannot influence it. That makes it stronger than the Windows client's
/// owner check, which reads the ACL of a pipe after connecting and can in principle be raced by a
/// process that replaces the endpoint in between.
/// </remarks>
public static partial class UnixPeerCredentials
{
    /// <summary>The kind stamped on every identity this class produces.</summary>
    public const string PeerKind = "unix-uid";

    private const int SolSocket = 1;
    private const int SoPeerCred = 17;

    /// <summary>
    /// struct ucred from linux/socket.h. Three 32-bit fields, in this order, on every Linux ABI
    /// StorageHub targets.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Ucred
    {
        public int Pid;
        public uint Uid;
        public uint Gid;
    }

    [LibraryImport("libc", SetLastError = true)]
    private static partial int getsockopt(int sockfd, int level, int optname, ref Ucred optval, ref uint optlen);

    /// <summary>This process's effective user.</summary>
    public static uint EffectiveUserId() => UnixFileSystem.EffectiveUserId();

    /// <summary>The peer's user, read from a connected socket.</summary>
    public static IpcPeerIdentity ReadPeer(Socket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);

        var credentials = default(Ucred);
        var size = (uint)Marshal.SizeOf<Ucred>();
        if (getsockopt(socket.Handle.ToInt32(), SolSocket, SoPeerCred, ref credentials, ref size) != 0)
        {
            throw new IOException(
                $"Could not read the peer's credentials from the socket (errno {Marshal.GetLastPInvokeError()}).");
        }

        return new IpcPeerIdentity(
            PeerKind,
            credentials.Uid.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Whether an identity names this process's own user.</summary>
    public static bool IsSelf(IpcPeerIdentity peer) =>
        peer.Kind == PeerKind &&
        uint.TryParse(peer.Value, CultureInfo.InvariantCulture, out var uid) &&
        uid == EffectiveUserId();
}
