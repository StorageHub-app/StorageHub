namespace StorageHub.Ipc.Windows;

/// <summary>A named pipe on the local machine.</summary>
/// <remarks>
/// The name is the address and also the security: it is derived from the account SID, so another
/// account cannot guess it and Windows would refuse it anyway.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed record NamedPipeEndpoint(string PipeName) : IpcEndpoint
{
    public override string Moniker => $@"\.\pipe\{PipeName}";
}
