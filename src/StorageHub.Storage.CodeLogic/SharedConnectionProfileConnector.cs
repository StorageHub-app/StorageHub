namespace StorageHub.Storage.CodeLogic;

/// <summary>
/// Hands out one <see cref="CodeLogicConnectionProfileConnector"/> for a subsystem, built on first
/// use.
///
/// The connector is what keeps a profile's runtime registration open between calls, so there has to
/// be one of it: a factory that returns a new connector per call gives every call its own empty
/// cache, which is what the agent did before. Construction stays lazy because the vault has to be
/// open and CL.Storage configured before a connector can be built, and neither is true at wire-up.
///
/// Subsystems keep separate instances on purpose. Each carries its own trust store and vault
/// accessor, and sharing one across them would silently widen what a registration is built from.
/// </summary>
public sealed class SharedConnectionProfileConnector : IAsyncDisposable
{
    private readonly Func<CodeLogicConnectionProfileConnector> _create;
    private readonly Lock _gate = new();
    private CodeLogicConnectionProfileConnector? _instance;
    private bool _disposed;

    /// <summary>Wraps a factory, which is called at most once.</summary>
    /// <param name="create">Builds the connector when it is first needed.</param>
    public SharedConnectionProfileConnector(Func<CodeLogicConnectionProfileConnector> create) =>
        _create = create ?? throw new ArgumentNullException(nameof(create));

    /// <summary>Gets the shared connector, building it if this is the first call.</summary>
    public CodeLogicConnectionProfileConnector Get()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _instance ??= _create();
        }
    }

    /// <summary>
    /// Retires the connector and everything it holds open. Worth doing on a graceful stop rather
    /// than leaving it to process exit: it is what releases resolved credentials and deletes the
    /// secret files materialised for them.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        CodeLogicConnectionProfileConnector? instance;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            instance = _instance;
            _instance = null;
        }

        if (instance is not null)
        {
            await instance.DisposeAsync().ConfigureAwait(false);
        }
    }
}
