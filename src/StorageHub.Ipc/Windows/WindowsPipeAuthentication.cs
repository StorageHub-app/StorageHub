using System.IO.Pipes;
using System.Security.Principal;

namespace StorageHub.Ipc.Windows;

/// <summary>
/// Accepts every peer Windows already let through.
/// </summary>
/// <remarks>
/// Not a stub for a check that was skipped. Named-pipe access control runs in the kernel before
/// <see cref="IIpcListener.AcceptAsync"/> returns: a same-user pipe refuses another account
/// outright, and a machine-service pipe refuses any account not on its ACL. No decision is left for
/// user code, which is why the connection reports no peer identity either. Where the operating
/// system does not enforce the address on its own - a Unix socket path - the authorizer is where
/// that check lives instead.
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

/// <summary>
/// Confirms a machine-service pipe really belongs to the agent before anything is sent.
/// </summary>
/// <remarks>
/// CurrentUserOnly normally does this for free by comparing the server's owner to the caller. A
/// service runs as LocalSystem, so that comparison has to be replaced rather than dropped: Windows
/// lets any process create a further instance of an existing pipe name, so without this an
/// unprivileged squatter could answer on the machine-wide name and be handed credentials over the
/// secret channel.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsPipeOwnerAuthenticator : IIpcServerAuthenticator
{
    public void EnsureTrusted(IIpcConnection connection, IpcEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(endpoint);
        if (connection.Stream is not PipeStream pipe)
        {
            throw new ArgumentException(
                "The pipe owner check needs a named-pipe connection.",
                nameof(connection));
        }

        var owner = pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (!IsTrustedServerOwner(owner))
        {
            throw new IOException(
                $"Local pipe '{endpoint.Moniker}' is not owned by the StorageHub service account.");
        }
    }

    /// <summary>
    /// Whether a pipe owned by <paramref name="owner"/> may be trusted to be the agent service.
    /// </summary>
    /// <remarks>
    /// Separated from the stream so the rule can be tested for every kind of owner. Deciding it
    /// against a pipe this process creates cannot be: the answer then depends on whether whoever
    /// runs the tests happens to be an administrator, which is how the first version of this passed
    /// locally and failed on a build agent.
    /// </remarks>
    public static bool IsTrustedServerOwner(SecurityIdentifier? owner) =>
        owner is not null &&
        (owner.IsWellKnown(WellKnownSidType.LocalSystemSid) ||
            owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid));
}

/// <summary>
/// Nothing to check: Windows already restricted a same-user pipe to this account, and refused it to
/// every other one.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsSameUserPipeAuthenticator : IIpcServerAuthenticator
{
    public void EnsureTrusted(IIpcConnection connection, IpcEndpoint endpoint)
    {
    }
}
