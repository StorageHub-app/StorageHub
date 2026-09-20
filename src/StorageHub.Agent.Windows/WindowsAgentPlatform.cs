using Microsoft.Win32;
using StorageHub.Agent;
using StorageHub.Ipc;
using StorageHub.Ipc.Windows;

namespace StorageHub.Agent.Windows;

/// <summary>
/// Starting the agent again at sign-in, as a value under the current user's Run key.
/// </summary>
/// <remarks>
/// The same decision a systemd user unit expresses on Linux, and the reason AgentHostMode needs no
/// member for either: the mode says whether a registration exists, and the platform says what one
/// is. The desktop has carried its own copy of this since before the agent had a platform; the two
/// converge when the shell's services move into Desktop.Core.
/// </remarks>
public sealed class WindowsRunKeyAutostart : IAutostartRegistration
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _valueName;

    public WindowsRunKeyAutostart(string valueName = "StorageHub")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
        _valueName = valueName;
    }

    /// <summary>
    /// Whether the logon entry exists. This is what separates "start with my sign-in" from "only
    /// while StorageHub is open": both run the agent in this session, and the entry is the only
    /// durable difference between them, so it is read back rather than remembered separately.
    /// </summary>
    public bool IsRegistered
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(_valueName) is not null;
            }
            catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// A Run entry starts at sign-in and stops at sign-out. Only the service mode survives that on
    /// Windows, which is the whole reason the service mode exists.
    /// </summary>
    public bool SurvivesSignOut => false;

    public bool TryRegister(string commandLine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandLine);
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return false;
            }

            key.SetValue(_valueName, commandLine, RegistryValueKind.String);
            return true;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool TryRemove()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(_valueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>Running the StorageHub agent on Windows.</summary>
public sealed class WindowsAgentPlatform : IAgentPlatform
{
    private static readonly HashSet<AgentHostMode> Supported =
        [AgentHostMode.UserSession, AgentHostMode.AppSession, AgentHostMode.WindowsService];

    public string Name => "windows";

    public IReadOnlySet<AgentHostMode> SupportedHostModes => Supported;

    public IIpcTransport Transport => WindowsNamedPipeIpc.Transport;

    public IIpcPeerAuthorizer PeerAuthorizer => WindowsNamedPipeIpc.PeerAuthorizer;

    /// <summary>
    /// Chosen from the mode rather than fixed, because a service pipe is machine-wide and must have
    /// its owner checked while a session pipe is already restricted to this account.
    /// </summary>
    public IIpcServerAuthenticator ServerAuthenticator =>
        WindowsNamedPipeIpc.AuthenticatorFor(IpcTrustModel.SameUser);

    public IAutostartRegistration Autostart { get; } = new WindowsRunKeyAutostart();

    /// <summary>Registering a service is an administrative act, and UAC is how it is asked for.</summary>
    public bool CanElevate => true;

    /// <summary>
    /// A service running as LocalSystem would resolve LocalApplicationData to the system profile,
    /// silently presenting an empty installation rather than the user's saved connections - which
    /// is why the root moves with the mode rather than being read once.
    /// </summary>
    public AgentPaths ResolvePaths(AgentHostMode mode)
    {
        EnsureSupported(mode);
        var dataRoot = AgentHostLayout.ResolveDataRoot(mode);

        // Windows has no tmpfs, so runtime state sits beside durable state rather than somewhere
        // that is cleared for it. The split still matters: it is what lets the Linux host put
        // sockets and key material on tmpfs without every caller knowing which is which.
        return new AgentPaths(dataRoot, Path.Combine(dataRoot, "Runtime"));
    }

    public IpcEndpoint ResolveEndpoint(AgentHostMode mode, AgentIpcChannel channel)
    {
        EnsureSupported(mode);
        if (!Enum.IsDefined(channel))
        {
            throw new ArgumentOutOfRangeException(nameof(channel));
        }

        var (normal, secret) = AgentHostLayout.ResolvePipeNames(mode);
        return new NamedPipeEndpoint(channel == AgentIpcChannel.Secret ? secret : normal);
    }

    public IpcTrustModel ResolveTrustModel(AgentHostMode mode)
    {
        EnsureSupported(mode);
        return mode == AgentHostMode.WindowsService
            ? IpcTrustModel.MachineService
            : IpcTrustModel.SameUser;
    }

    private static void EnsureSupported(AgentHostMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }
}
