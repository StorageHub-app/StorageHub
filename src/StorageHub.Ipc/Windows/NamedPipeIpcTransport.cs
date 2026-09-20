using System.IO.Pipes;

namespace StorageHub.Ipc.Windows;

/// <summary>StorageHub's IPC over Windows named pipes.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class NamedPipeIpcTransport : IIpcTransport
{
    /// <summary>What a named pipe allows, and what the accept loop must not exceed.</summary>
    public const int MaximumPipeInstances = 254;

    public string Name => "named-pipe";

    /// <summary>
    /// Every pipe this transport publishes carries secrets.
    /// </summary>
    /// <remarks>
    /// CurrentUserOnly means the pipe is reachable only by the account that created it, and the
    /// client checks the owner matches itself, so neither observation nor impersonation is open to
    /// another account. Both ends set it unconditionally, so there is no second kind of pipe to
    /// ask about.
    /// </remarks>
    public bool SupportsConfidentialChannel(IpcEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return endpoint is NamedPipeEndpoint;
    }

    public void ValidateEndpoint(IpcEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint is not NamedPipeEndpoint pipe)
        {
            throw new ArgumentException(
                $"The named-pipe transport cannot address a {endpoint.GetType().Name}.",
                nameof(endpoint));
        }

        ValidatePipeName(pipe.PipeName);
    }

    public void ValidateListenOptions(IpcListenOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateEndpoint(options.Endpoint);

        if (options.MaxConcurrentClients is < 1 or > MaximumPipeInstances)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaxConcurrentClients,
                $"The maximum concurrent client count must be between 1 and {MaximumPipeInstances}.");
        }
    }

    public IIpcListener Listen(IpcListenOptions options)
    {
        ValidateListenOptions(options);
        var pipe = (NamedPipeEndpoint)options.Endpoint;
        return new NamedPipeIpcListener(pipe, options.MaxConcurrentClients);
    }

    public async ValueTask<IIpcConnection> ConnectAsync(
        IpcConnectOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateEndpoint(options.Endpoint);
        var pipe = (NamedPipeEndpoint)options.Endpoint;

        // CurrentUserOnly unconditionally: the agent is this account's, and Windows comparing the
        // server's owner to the caller is the whole boundary.
        const PipeOptions ClientOptions = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;
        var stream = new NamedPipeClientStream(".", pipe.PipeName, PipeDirection.InOut, ClientOptions);
        try
        {
            await stream.ConnectAsync(cancellationToken).ConfigureAwait(false);
            return new NamedPipeIpcConnection(stream);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// What Windows will accept as a pipe name, checked before it reaches the operating system.
    /// </summary>
    /// <remarks>
    /// Lived in the shared protocol validation while named pipes were the only transport. It is a
    /// rule about pipe names rather than about the protocol, and a Unix socket path answers to a
    /// different one entirely - a 108 byte sun_path limit, and a directory that has to be private.
    /// </remarks>
    public static void ValidatePipeName(string pipeName)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            throw new ArgumentException("A named-pipe name is required.", nameof(pipeName));
        }

        if (pipeName.Length > 180)
        {
            throw new ArgumentException("The named-pipe name cannot exceed 180 characters.", nameof(pipeName));
        }

        foreach (var character in pipeName)
        {
            if (!(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
            {
                throw new ArgumentException(
                    "The named-pipe name may contain only ASCII letters, digits, dots, dashes, and underscores.",
                    nameof(pipeName));
            }
        }
    }
}
