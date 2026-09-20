namespace StorageHub.Agent.Transfers;

/// <summary>
/// What a local transfer path may be, on this operating system.
/// </summary>
/// <remarks>
/// The local user-path endpoint is the only place a filesystem destination crosses the IPC boundary
/// inward: the desktop nominates a folder and the agent must not take its word for it. Three of the
/// checks that guards it are not portable, and each is wrong in a different way off Windows.
///
/// Whether two paths are the same path. Windows compares case-insensitively; Linux does not, and
/// using the Windows rule there both over-blocks (refusing /home/user/Docs for /home/user/docs) and
/// under-blocks, because a containment check that ignores case can be satisfied by a path that the
/// filesystem will resolve somewhere else entirely.
///
/// Which directories are off limits. %WINDIR% and %PROGRAMFILES% have no counterpart; /proc, /sys
/// and /dev are not directories a transfer should ever reach, and they are not files in the sense
/// the endpoint assumes.
///
/// Which path syntaxes are refused outright. Extended-length and device paths bypass normalisation
/// on Windows; Linux has no equivalent, so refusing them there would be theatre.
/// </remarks>
public interface ILocalPathPolicy
{
    /// <summary>
    /// How to tell whether two paths name the same place.
    /// </summary>
    /// <remarks>
    /// The single most consequential line in this interface: it decides whether a containment check
    /// holds. macOS would answer OrdinalIgnoreCase, like Windows, because its default filesystem is
    /// case-insensitive - which is why this is a property of the platform rather than of Unix.
    /// </remarks>
    StringComparison PathComparison { get; }

    /// <summary>
    /// Refuses a path whose syntax defeats normalisation, before anything is resolved.
    /// </summary>
    bool IsRejectedSyntax(string root, out string reason);

    /// <summary>
    /// Locations a transfer must never write into: StorageHub's own data tree, which holds the
    /// vault and the durable queue, and the operating system's own directories.
    /// </summary>
    IEnumerable<(string Folder, string Reason)> ProtectedRoots();

    /// <summary>How to name the account in a message about permissions.</summary>
    string AccountDescription { get; }
}

/// <summary>
/// The policy in force for this process.
/// </summary>
/// <remarks>
/// A static because the answer is a property of the operating system rather than of any one
/// transfer, and the endpoints that ask are static themselves. The host sets it from the platform it
/// composed; anything else - a test, or the desktop reading a path for display - gets the right
/// answer from the default without having to be wired up.
/// </remarks>
public static class LocalPathPolicy
{
    private static ILocalPathPolicy? _current;

    public static ILocalPathPolicy Current => _current ??= CreateDefault();

    /// <summary>Installs the policy the host's platform supplies.</summary>
    public static void Use(ILocalPathPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _current = policy;
    }

    private static ILocalPathPolicy CreateDefault() =>
        OperatingSystem.IsWindows()
            ? new Windows.WindowsLocalPathPolicy()
            : OperatingSystem.IsLinux()
                ? new Linux.LinuxLocalPathPolicy()
                : throw new PlatformNotSupportedException(
                    "StorageHub has no local path policy for this operating system.");
}
