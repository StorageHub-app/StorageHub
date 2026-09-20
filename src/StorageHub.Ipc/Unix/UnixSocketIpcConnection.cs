using System.Net.Sockets;

namespace StorageHub.Ipc.Unix;

/// <summary>One connected Unix socket, and the uid the kernel says is behind it.</summary>
/// <remarks>
/// The peer is read once, at construction, while the socket is known to be connected. Reading it
/// lazily would mean the answer could change - or become unavailable - between the check and the
/// use, which is the shape of a time-of-check bug in the one place that cannot afford one.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
internal sealed class UnixSocketIpcConnection : IIpcConnection
{
    private readonly Socket _socket;
    private readonly NetworkStream _stream;

    internal UnixSocketIpcConnection(Socket socket)
    {
        _socket = socket;
        Peer = UnixPeerCredentials.ReadPeer(socket);
        _stream = new NetworkStream(socket, ownsSocket: true);
    }

    public Stream Stream => _stream;

    public bool IsConnected => _socket.Connected;

    public IpcPeerIdentity Peer { get; }

    public ValueTask DisposeAsync() => _stream.DisposeAsync();
}
