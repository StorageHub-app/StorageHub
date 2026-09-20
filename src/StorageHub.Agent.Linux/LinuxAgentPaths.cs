using StorageHub.Agent;

namespace StorageHub.Agent.Linux;

/// <summary>
/// Where a Linux agent keeps its files.
/// </summary>
/// <remarks>
/// Environment.SpecialFolder does not carry over. LocalApplicationData maps to $XDG_CONFIG_HOME,
/// which is where configuration belongs and durable data does not; CommonApplicationData maps to
/// /usr/share, which is root-owned, so the machine-wide branch would have produced an unwritable
/// root that only fails at the first write. The XDG variables are read directly instead, with the
/// specification's own defaults when they are unset.
/// </remarks>
public static class LinuxAgentPaths
{
    private const string ApplicationDirectoryName = "storagehub";

    /// <summary>Durable state: the database, the vault, anything that must survive a reboot.</summary>
    public static string ResolveDataRoot()
    {
        var overridden = Environment.GetEnvironmentVariable(AgentHostLayout.DataRootVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return overridden;
        }

        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataHome))
        {
            dataHome = Path.Combine(HomeDirectory(), ".local", "share");
        }

        return Path.Combine(dataHome, ApplicationDirectoryName);
    }

    /// <summary>
    /// Runtime state: sockets, and key material materialised for one connection.
    /// </summary>
    /// <remarks>
    /// XDG_RUNTIME_DIR is per-user, 0700 and on tmpfs, so a socket left behind cannot outlive a
    /// reboot and a plaintext key never reaches a disk. It is unset often enough to need a fallback
    /// - plain SSH sessions, containers, cron - and the fallback is a per-uid directory under the
    /// temp path, which is weaker only in that it survives a reboot.
    /// </remarks>
    public static string ResolveRuntimeRoot() => Ipc.Unix.UnixIpcSocketDirectory.Resolve();

    private static string HomeDirectory()
    {
        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return !string.IsNullOrWhiteSpace(home)
            ? home
            : throw new InvalidOperationException("This account has no home directory.");
    }
}
