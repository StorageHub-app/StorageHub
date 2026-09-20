namespace StorageHub.Ipc;

/// <summary>
/// Where an agent listens, and a client connects.
/// </summary>
/// <remarks>
/// A pipe name used to be the whole address, and on Windows it also carried the security: the name
/// is derived from the account SID, so only that account can guess it. That does not survive the
/// move off Windows, where the address is a path and the security is the directory's mode. Naming
/// the endpoint as a type rather than a string is what keeps the two ideas apart.
/// </remarks>
public abstract record IpcEndpoint
{
    /// <summary>How the endpoint is written in a log line or an error.</summary>
    public abstract string Moniker { get; }

    public sealed override string ToString() => Moniker;
}

// There was an IpcTrustModel here, choosing between SameUser and MachineService. Only one agent
// exists now and it belongs to the signed-in account, so every endpoint is same-user and the choice
// decided nothing: Windows restricts the pipe to the creating account and the client checks the
// server's owner matches itself, while a Unix socket sits in a directory only that user can enter
// and both ends compare the peer's uid. That is unconditional, and saying so beats a one-member
// enum threaded through every option record.

/// <summary>
/// Who is on the other end, as the operating system reports it rather than as the peer claims.
/// </summary>
/// <remarks>
/// <c>Kind</c> names the authority that answered - "windows-sid", "unix-uid" - so a value is never
/// compared against one from a different kind of system. Both are supplied by the kernel and
/// neither can be forged by the peer.
/// </remarks>
public readonly record struct IpcPeerIdentity(string Kind, string Value)
{
    /// <summary>A peer the transport cannot identify, which no authorizer may accept.</summary>
    public static IpcPeerIdentity Unknown { get; } = new("unknown", string.Empty);

    public bool IsKnown => !string.IsNullOrEmpty(Value) && Kind != "unknown";

    public override string ToString() => IsKnown ? $"{Kind}:{Value}" : "unknown";
}
