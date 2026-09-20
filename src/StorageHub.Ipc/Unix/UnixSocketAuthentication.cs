namespace StorageHub.Ipc.Unix;

/// <summary>
/// Admits only this user, server side.
/// </summary>
/// <remarks>
/// Unlike the named-pipe equivalent this is not a formality. Nothing in the kernel refuses a
/// connection to a Unix socket on the caller's identity - the directory keeps other users from
/// reaching the path at all, and this is the second half of that, checked per connection. If the
/// directory check were ever wrong, or a process in the same user's namespace reached the path some
/// other way, this is what still says no.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class UnixPeerCredentialAuthorizer : IIpcPeerAuthorizer
{
    public bool IsAuthorized(IpcPeerIdentity peer, out string? denialReason)
    {
        if (!peer.IsKnown)
        {
            denialReason = "The peer could not be identified.";
            return false;
        }

        if (!UnixPeerCredentials.IsSelf(peer))
        {
            denialReason = $"The peer is {peer}, not this user.";
            return false;
        }

        denialReason = null;
        return true;
    }
}

/// <summary>
/// Confirms the socket that answered belongs to this user, client side.
/// </summary>
/// <remarks>
/// The mirror of the authorizer, and the reason the trust is mutual. A socket path in a private
/// directory should already be unreachable by anyone else, so this holds only in the case where
/// that has gone wrong - which is exactly when a client is about to hand credentials to whatever
/// answered.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class UnixSocketOwnerAuthenticator : IIpcServerAuthenticator
{
    public void EnsureTrusted(IIpcConnection connection, IpcEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!connection.Peer.IsKnown)
        {
            throw new IOException($"The process listening on {endpoint} could not be identified.");
        }

        if (!UnixPeerCredentials.IsSelf(connection.Peer))
        {
            throw new IOException(
                $"The process listening on {endpoint} runs as {connection.Peer}, not as this user.");
        }
    }
}
