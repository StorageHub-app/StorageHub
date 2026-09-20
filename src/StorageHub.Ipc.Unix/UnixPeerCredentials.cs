using System.Globalization;
using System.Net.Sockets;
using System.Runtime.InteropServices;

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

    private const int AtFdCwd = -100;
    private const int AtSymlinkNoFollow = 0x100;
    private const uint StatxUid = 0x00000008;

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

    [LibraryImport("libc")]
    private static partial uint geteuid();

    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int statx(int dirfd, string pathname, int flags, uint mask, byte[] buffer);

    /// <summary>This process's effective user.</summary>
    public static uint EffectiveUserId() => geteuid();

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

    /// <summary>
    /// The owner of a filesystem entry.
    /// </summary>
    /// <remarks>
    /// Read through statx rather than stat. .NET exposes a Unix file's mode but not its owner, and
    /// struct stat's layout differs between architectures - st_uid sits at a different offset on
    /// x86-64 than on aarch64, which is exactly the kind of difference that works on the machine it
    /// was written on. struct statx is defined by the kernel with fixed offsets on every
    /// architecture, so stx_uid is at 20 everywhere.
    /// </remarks>
    public static uint OwnerUserId(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // Generously sized: the kernel writes at most sizeof(struct statx), which is 256 bytes, and
        // only the first 24 are read here.
        var buffer = new byte[512];
        if (statx(AtFdCwd, path, AtSymlinkNoFollow, StatxUid, buffer) != 0)
        {
            throw new IOException(
                $"Could not read the owner of '{path}' (errno {Marshal.GetLastPInvokeError()}).");
        }

        const int StxUidOffset = 20;
        return BitConverter.ToUInt32(buffer, StxUidOffset);
    }

    /// <summary>Whether an identity names this process's own user.</summary>
    public static bool IsSelf(IpcPeerIdentity peer) =>
        peer.Kind == PeerKind &&
        uint.TryParse(peer.Value, CultureInfo.InvariantCulture, out var uid) &&
        uid == EffectiveUserId();
}
