namespace StorageHub.Security;

/// <summary>
/// Materializes a secret into a short-lived, access-restricted file for provider APIs that
/// only accept filesystem paths (for example, SSH private keys and client PFX files).
/// </summary>
public interface IRuntimeSecretFileMaterializer
{
    ValueTask<IRuntimeSecretFile> MaterializeAsync(
        ReadOnlyMemory<byte> secret,
        string fileExtension,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes material left behind by a process that did not get to clean up, and reports how many.
    /// </summary>
    /// <remarks>
    /// On the contract rather than on one implementation because the host sweeps at startup and
    /// should not have to know which platform it is sweeping for.
    /// </remarks>
    int ScavengeOrphans(TimeSpan minimumAge);
}

public interface IRuntimeSecretFile : IAsyncDisposable
{
    string FullPath { get; }
}
