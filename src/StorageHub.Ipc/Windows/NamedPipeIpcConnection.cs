using System.IO.Pipes;

namespace StorageHub.Ipc.Windows;

/// <summary>One connected named pipe.</summary>
/// <remarks>
/// <see cref="Peer"/> is deliberately not reported. This account is already the only one that
/// could have connected - Windows answers the question before the accept returns, and naming the
/// peer would need impersonation for a decision that is no longer open. The Unix transport is the
/// other way round: nothing enforces the socket path on its own, so the uid it reports is the
/// whole boundary.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class NamedPipeIpcConnection(PipeStream stream) : IIpcConnection
{
    public Stream Stream { get; } = stream;

    public bool IsConnected => stream.IsConnected;

    public IpcPeerIdentity Peer => IpcPeerIdentity.Unknown;

    public ValueTask DisposeAsync() => stream.DisposeAsync();
}
