using StorageHub.Ipc;

namespace StorageHub.Desktop;

/// <summary>
/// Builds the client options every desktop agent client shares, and the clients themselves.
/// </summary>
/// <remarks>
/// The retry envelope is deliberately short: the agent is a local process, so a connect that does
/// not succeed quickly means it is down, and the caller should surface an offline state instead of
/// stalling the UI.
///
/// Creating the client here rather than at each call site is what makes the endpoint and the
/// authenticator agree. They have to: pairing a machine-wide endpoint with the same-user check would
/// compile, connect, and silently skip the check that stops a squatter collecting secrets.
/// </remarks>
internal static class DesktopAgentIpcOptions
{
    internal const int ConnectAttempts = 3;

    internal static readonly TimeSpan InitialReconnectDelay = TimeSpan.FromMilliseconds(100);

    internal static readonly TimeSpan MaximumReconnectDelay = TimeSpan.FromMilliseconds(400);

    // Pinned to this assembly (not the entry assembly) so the reported version stays the desktop
    // build even when a test host or the agent owns the process.
    private static readonly string ClientVersionValue =
        typeof(DesktopAgentIpcOptions).Assembly.GetName().Version?.ToString() ?? "0.1.0";

    internal static IpcClientOptions Create(
        IpcEndpoint endpoint,
        string clientName,
        TimeSpan connectTimeout) =>
        new()
        {
            Endpoint = endpoint,
            ClientName = clientName,
            ClientVersion = ClientVersionValue,
            ConnectTimeout = connectTimeout,
            MaxConnectAttempts = ConnectAttempts,
            InitialReconnectDelay = InitialReconnectDelay,
            MaximumReconnectDelay = MaximumReconnectDelay
        };

    /// <summary>A client on the agent's normal channel.</summary>
    internal static IpcClient CreateClient(string clientName, TimeSpan connectTimeout) =>
        CreateClient(Create(DesktopAgentHost.NormalEndpoint, clientName, connectTimeout));

    /// <summary>A client for options already built, whichever channel they name.</summary>
    internal static IpcClient CreateClient(IpcClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new IpcClient(
            DesktopAgentHost.Platform.Transport,
            options,
            DesktopAgentHost.Platform.ServerAuthenticator);
    }
}
