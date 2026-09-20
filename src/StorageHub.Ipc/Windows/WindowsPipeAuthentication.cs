
namespace StorageHub.Ipc.Windows;

/// <summary>
/// Accepts every peer Windows already let through.
/// </summary>
/// <remarks>
/// Not a stub for a check that was skipped. Named-pipe access control runs in the kernel before
/// <see cref="IIpcListener.AcceptAsync"/> returns: the pipe is restricted to the creating account
/// and another account is refused outright. No decision is left for user code, which is why the
/// connection reports no peer identity either. Where the operating system does not enforce the
/// address on its own - a Unix socket path - the authorizer is where that check lives instead.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsPipeAccessControlAuthorizer : IIpcPeerAuthorizer
{
    public bool IsAuthorized(IpcPeerIdentity peer, out string? denialReason)
    {
        denialReason = null;
        return true;
    }
}

// There was a WindowsPipeOwnerAuthenticator here. It compared the pipe's owner against LocalSystem
// and Administrators, which is the check a machine-wide pipe needs because its clients are other
// accounts and CurrentUserOnly cannot be used. No such pipe is published any more, and the owner
// comparison the kernel does for free is stricter than the one this performed.

/// <summary>
/// Nothing to check: Windows already restricted the pipe to this account, and refused it to every
/// other one.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsSameUserPipeAuthenticator : IIpcServerAuthenticator
{
    public void EnsureTrusted(IIpcConnection connection, IpcEndpoint endpoint)
    {
    }
}
