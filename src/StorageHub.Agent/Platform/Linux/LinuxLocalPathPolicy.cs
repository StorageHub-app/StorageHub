using StorageHub.Agent.Transfers;

namespace StorageHub.Agent.Linux;

/// <summary>Where a transfer may write on Linux.</summary>
/// <remarks>
/// Deliberately not a translation of the Windows list. That one names three things - StorageHub's
/// own data, the OS directories, and the installed programs - and the Linux answer to each is
/// different in kind, not just in spelling. The kernel filesystems in particular have no Windows
/// counterpart at all: /proc and /sys present files that are not storage, and writing into them is
/// how a process reconfigures the running system.
///
/// What is left alone matters as much. /home, /media, /mnt, /srv, /opt and /var are where a person's
/// files actually live, and a file manager that refused them would be useless. The list blocks what
/// the system owns, not what the user does.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class LinuxLocalPathPolicy : ILocalPathPolicy
{
    /// <summary>
    /// Case-sensitive, because ext4, btrfs and xfs are.
    /// </summary>
    /// <remarks>
    /// Using the Windows rule here would be wrong in both directions: it would refuse
    /// /home/user/Docs when /home/user/docs was approved, and it would let a containment check pass
    /// for a path the filesystem resolves somewhere else.
    /// </remarks>
    public StringComparison PathComparison => StringComparison.Ordinal;

    public string AccountDescription => "your user account";

    /// <summary>
    /// Nothing. Linux has no extended-length or device-path syntax that defeats normalisation, so
    /// there is nothing here to refuse that Path.GetFullPath does not already resolve.
    /// </summary>
    public bool IsRejectedSyntax(string root, out string reason)
    {
        reason = string.Empty;
        return false;
    }

    public IEnumerable<(string Folder, string Reason)> ProtectedRoots()
    {
        yield return (
            LinuxAgentPaths.ResolveDataRoot(),
            "StorageHub's own data folder cannot be a transfer destination.");

        // Not storage. These present kernel and device state as files, and writing into them
        // reconfigures the running system rather than saving anything.
        foreach (var kernel in new[] { "/proc", "/sys", "/dev" })
        {
            yield return (kernel, "Kernel and device filesystems cannot be a transfer destination.");
        }

        // Owned by the system and its package manager. A transfer landing here either fails on
        // permissions or, run with more privilege than it should have, breaks the installation.
        foreach (var system in new[] { "/boot", "/etc", "/usr", "/bin", "/sbin", "/lib", "/lib64" })
        {
            yield return (system, "System folders cannot be a transfer destination.");
        }

        // The runtime tree holds this agent's own sockets and materialised key material.
        yield return ("/run", "Runtime state folders cannot be a transfer destination.");
    }
}
