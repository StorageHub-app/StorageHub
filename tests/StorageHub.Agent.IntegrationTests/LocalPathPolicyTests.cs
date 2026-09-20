using StorageHub.Agent.Transfers;
using StorageHub.Testing;

namespace StorageHub.Agent.IntegrationTests;

/// <summary>
/// The parts of the policy that hold wherever StorageHub runs.
/// </summary>
/// <remarks>
/// The local user-path endpoint is the only place a filesystem destination crosses the IPC boundary
/// inward - the desktop nominates a folder and the agent must not take its word for it. These assert
/// the policy that guards it.
///
/// Split by platform rather than gated per method, because a skip attribute is a runtime decision
/// and CA1416 is a compile-time one: a class that names both platforms cannot satisfy the analyzer
/// however its cases are attributed.
/// </remarks>
public sealed class LocalPathPolicyTests
{
    [Fact]
    public void EveryProtectedRootExplainsItself()
    {
        // The reason reaches the user as the refusal message, so a blank one would leave a failed
        // transfer with nothing to act on.
        Assert.All(
            LocalPathPolicy.Current.ProtectedRoots(),
            entry => Assert.False(string.IsNullOrWhiteSpace(entry.Reason)));
    }

    [Fact]
    public void ThePolicyInForceMatchesThisPlatform()
    {
        Assert.Equal(
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal,
            LocalPathPolicy.Current.PathComparison);
    }

    [Fact]
    public void StorageHubsOwnDataFolderIsNeverATransferDestination()
    {
        // It holds the vault and the durable queue. This is the one entry the list exists for on
        // every platform, whatever the rest of it names.
        Assert.Contains(
            LocalPathPolicy.Current.ProtectedRoots(),
            entry => entry.Reason.Contains("StorageHub's own data folder", StringComparison.Ordinal));
    }
}

/// <summary>Where a transfer may write on Windows.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsLocalPathPolicyTests
{
    [WindowsOnlyFact]
    public void PathsAreComparedWithoutRegardToCase()
    {
        Assert.Equal(StringComparison.OrdinalIgnoreCase, new Agent.Windows.WindowsLocalPathPolicy().PathComparison);
    }

    [WindowsOnlyFact]
    public void DeviceAndExtendedLengthPathsAreRefused()
    {
        // They bypass the normalisation every later check depends on.
        var policy = new Agent.Windows.WindowsLocalPathPolicy();

        Assert.True(policy.IsRejectedSyntax(@"\\?\C:\Data", out _));
        Assert.True(policy.IsRejectedSyntax(@"\\.\PhysicalDrive0", out _));
        Assert.False(policy.IsRejectedSyntax(@"C:\Data", out _));
    }

    [WindowsOnlyFact]
    public void TheSystemTreesAreProtected()
    {
        var guarded = new Agent.Windows.WindowsLocalPathPolicy().ProtectedRoots().Select(r => r.Folder).ToList();

        Assert.Contains(
            guarded,
            folder => folder.Equals(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            guarded,
            folder => folder.Equals(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Where a transfer may write on Linux.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class LinuxLocalPathPolicyTests
{
    [LinuxOnlyFact]
    public void PathsAreComparedExactly()
    {
        // Using the Windows rule here is wrong in both directions: it refuses /home/user/Docs when
        // /home/user/docs was approved, and it lets a containment check pass for a path the
        // filesystem resolves somewhere else.
        Assert.Equal(StringComparison.Ordinal, new Agent.Linux.LinuxLocalPathPolicy().PathComparison);
    }

    [LinuxOnlyFact]
    public void ThereIsNoPathSyntaxToRefuse()
    {
        // No extended-length or device syntax defeats Path.GetFullPath here, so refusing anything
        // would be theatre rather than a check.
        Assert.False(new Agent.Linux.LinuxLocalPathPolicy().IsRejectedSyntax("/home/someone/data", out _));
    }

    [LinuxOnlyFact]
    public void TheKernelFilesystemsAreProtected()
    {
        // Not storage. These present kernel and device state as files, and writing into them
        // reconfigures the running system rather than saving anything.
        var guarded = Guarded();

        Assert.Contains("/proc", guarded);
        Assert.Contains("/sys", guarded);
        Assert.Contains("/dev", guarded);
    }

    [LinuxOnlyFact]
    public void TheSystemTreesAndTheRuntimeTreeAreProtected()
    {
        var guarded = Guarded();

        foreach (var system in new[] { "/boot", "/etc", "/usr", "/bin", "/sbin", "/lib" })
        {
            Assert.Contains(system, guarded);
        }

        // Holds this agent's own sockets and its materialised key material.
        Assert.Contains("/run", guarded);
    }

    [LinuxOnlyFact]
    public void TheDirectoriesPeopleKeepFilesInAreLeftAlone()
    {
        // A file manager that refused /home or /mnt would be useless. The list blocks what the
        // system owns, not what the user does.
        var guarded = Guarded();

        foreach (var usable in new[] { "/home", "/media", "/mnt", "/srv", "/opt", "/var" })
        {
            Assert.DoesNotContain(usable, guarded);
        }
    }

    private static List<string> Guarded() =>
        [.. new Agent.Linux.LinuxLocalPathPolicy().ProtectedRoots().Select(r => r.Folder)];
}
