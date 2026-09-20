using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace StorageHub.Ipc.Windows;

/// <summary>StorageHub's IPC over Windows named pipes.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class NamedPipeIpcTransport : IIpcTransport
{
    /// <summary>What a named pipe allows, and what the accept loop must not exceed.</summary>
    public const int MaximumPipeInstances = 254;

    public string Name => "named-pipe";

    /// <summary>
    /// A same-user pipe carries secrets; a machine-service pipe does not.
    /// </summary>
    /// <remarks>
    /// CurrentUserOnly means the pipe is reachable only by the account that created it, and the
    /// client checks the owner matches itself, so neither observation nor impersonation is open to
    /// another account. A machine-service pipe is reachable by every permitted account and named
    /// machine-wide, which is the opposite of what a secret channel needs.
    /// </remarks>
    public bool SupportsConfidentialChannel(IpcEndpoint endpoint, IpcTrustModel trustModel)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return endpoint is NamedPipeEndpoint && trustModel == IpcTrustModel.SameUser;
    }

    public void ValidateEndpoint(IpcEndpoint endpoint, IpcTrustModel trustModel)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint is not NamedPipeEndpoint pipe)
        {
            throw new ArgumentException(
                $"The named-pipe transport cannot address a {endpoint.GetType().Name}.",
                nameof(endpoint));
        }

        ValidatePipeName(pipe.PipeName);
        if (!Enum.IsDefined(trustModel))
        {
            throw new ArgumentOutOfRangeException(nameof(trustModel), "The IPC trust model is invalid.");
        }
    }

    public void ValidateListenOptions(IpcListenOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateEndpoint(options.Endpoint, options.TrustModel);

        if (options.MaxConcurrentClients is < 1 or > MaximumPipeInstances)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaxConcurrentClients,
                $"The maximum concurrent client count must be between 1 and {MaximumPipeInstances}.");
        }

        if (options.TrustModel != IpcTrustModel.MachineService)
        {
            return;
        }

        // Refuse rather than publish a pipe the desktop cannot open: an empty list would leave
        // only LocalSystem and Administrators on the ACL, which looks like a working agent and
        // fails at every connect.
        if (options.PermittedPrincipals.Count == 0)
        {
            throw new ArgumentException(
                "Machine-service pipe security requires at least one permitted account.",
                nameof(options));
        }

        foreach (var sid in options.PermittedPrincipals)
        {
            if (string.IsNullOrWhiteSpace(sid) || !IsResolvableSid(sid))
            {
                throw new ArgumentException(
                    "A permitted account SID is not a valid security identifier.",
                    nameof(options));
            }
        }
    }

    public IIpcListener Listen(IpcListenOptions options)
    {
        ValidateListenOptions(options);
        var pipe = (NamedPipeEndpoint)options.Endpoint;
        var security = options.TrustModel == IpcTrustModel.MachineService
            ? CreateMachineServicePipeSecurity(options.PermittedPrincipals)
            : null;
        return new NamedPipeIpcListener(pipe, options.MaxConcurrentClients, security);
    }

    public async ValueTask<IIpcConnection> ConnectAsync(
        IpcConnectOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateEndpoint(options.Endpoint, options.TrustModel);
        var pipe = (NamedPipeEndpoint)options.Endpoint;

        var pipeOptions = PipeOptions.Asynchronous;
        if (options.TrustModel != IpcTrustModel.MachineService)
        {
            pipeOptions |= PipeOptions.CurrentUserOnly;
        }

        var stream = new NamedPipeClientStream(".", pipe.PipeName, PipeDirection.InOut, pipeOptions);
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

    internal static bool IsResolvableSid(string sid)
    {
        try
        {
            _ = new SecurityIdentifier(sid);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static PipeSecurity CreateMachineServicePipeSecurity(IReadOnlyList<string> permittedUserSids)
    {
        var security = new PipeSecurity();
        foreach (var wellKnown in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(wellKnown, null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));
        }

        foreach (var sid in permittedUserSids)
        {
            // ReadWrite plus Synchronize is everything a client needs and nothing more; it cannot
            // change the pipe's own security the way FullControl would.
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(sid),
                PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize,
                AccessControlType.Allow));
        }

        return security;
    }
}
