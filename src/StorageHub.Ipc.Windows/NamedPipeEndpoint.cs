namespace StorageHub.Ipc.Windows;

/// <summary>A named pipe on the local machine.</summary>
/// <remarks>
/// The name is the address and, for a same-user endpoint, also the security: it is derived from
/// the account SID, so another account cannot guess it and Windows would refuse it anyway. A
/// machine-service endpoint drops that second property deliberately - the name is machine-wide and
/// guessable, and the access list takes over.
/// </remarks>
public sealed record NamedPipeEndpoint(string PipeName) : IpcEndpoint
{
    public override string Moniker => $@"\.\pipe\{PipeName}";
}
