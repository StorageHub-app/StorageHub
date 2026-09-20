using System.Security.AccessControl;
using System.Security.Principal;
using StorageHub.Agent;
using StorageHub.Testing;

namespace StorageHub.Agent.IntegrationTests;

/// <summary>
/// A service that runs as LocalSystem must not run from a directory its own user can write to:
/// replacing the binary and waiting for a restart is a privilege escalation. StorageHub installs
/// per user and portable builds run from anywhere, so this is the rule that decides whether the
/// service can be registered at all.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class AgentServiceStagingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"storagehub-staging-{Guid.NewGuid():N}");

    [WindowsOnlyFact]
    public void A_user_writable_directory_is_refused()
    {
        var directory = Path.Combine(_root, "portable");
        _ = Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "StorageHub.Agent.Host.exe");
        File.WriteAllText(executable, "not a real agent");
        GrantCurrentUserWrite(directory);

        var error = Assert.Throws<UnauthorizedAccessException>(
            () => AgentServiceStaging.EnsureSafeForService(executable));

        Assert.Contains("LocalSystem", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The realistic shape of the bug: an install under the user's own profile. Temp sits under
    /// the profile too, so a plain directory there reproduces exactly what %LOCALAPPDATA% grants.
    /// </summary>
    [WindowsOnlyFact]
    public void An_install_under_the_user_profile_is_refused()
    {
        var directory = Path.Combine(_root, "localappdata-like");
        _ = Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "StorageHub.Agent.Host.exe");
        File.WriteAllText(executable, "not a real agent");

        _ = Assert.Throws<UnauthorizedAccessException>(
            () => AgentServiceStaging.EnsureSafeForService(executable));
    }

    [WindowsOnlyFact]
    public void A_missing_directory_is_refused_rather_than_assumed_safe()
    {
        _ = Assert.Throws<DirectoryNotFoundException>(
            () => AgentServiceStaging.EnsureSafeForService(
                Path.Combine(_root, "absent", "StorageHub.Agent.Host.exe")));
    }

    /// <summary>
    /// The regression that made the service unusable: the binaries were staged into
    /// <c>%ProgramData%\StorageHub\bin</c>, inside the very data root the service resolves. The
    /// agent rejects an overlapping data root and application directory at startup, so the service
    /// was registered and then exited immediately, every time, on every machine.
    ///
    /// Neither direction may overlap, and the check is written the way the agent writes it -- a
    /// path prefix on a separator boundary -- so a sibling like <c>StorageHubAgent</c> beside
    /// <c>StorageHub</c> is correctly read as separate rather than as a parent.
    /// </summary>
    [WindowsOnlyTheory]
    [InlineData(AgentHostMode.WindowsService)]
    [InlineData(AgentHostMode.UserSession)]
    public void The_staging_directory_never_overlaps_a_data_root(AgentHostMode mode)
    {
        var staging = AgentServiceStaging.ResolveDirectory();
        var dataRoot = AgentHostLayout.ResolveDataRoot(mode);

        Assert.False(IsSamePathOrAncestor(dataRoot, staging), $"{dataRoot} contains {staging}");
        Assert.False(IsSamePathOrAncestor(staging, dataRoot), $"{staging} contains {dataRoot}");
    }

    /// <summary>
    /// The binaries still live under the staging root, so protecting that root protects them.
    /// </summary>
    [WindowsOnlyFact]
    public void The_staging_directory_sits_inside_the_protected_staging_root()
    {
        var root = AgentServiceStaging.ResolveRootDirectory();
        var staging = AgentServiceStaging.ResolveDirectory();

        Assert.True(IsSamePathOrAncestor(root, staging));
        Assert.NotEqual(root, staging);
    }

    /// <summary>Mirrors the agent's own overlap rule, so the assertion tests what production does.</summary>
    private static bool IsSamePathOrAncestor(string candidateAncestor, string candidateDescendant)
    {
        var ancestor = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidateAncestor));
        var descendant = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidateDescendant));
        return string.Equals(ancestor, descendant, StringComparison.OrdinalIgnoreCase) ||
            descendant.StartsWith(ancestor + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void GrantCurrentUserWrite(string directory)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var info = new DirectoryInfo(directory);
        var security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            identity.User!,
            FileSystemRights.Modify,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        info.SetAccessControl(security);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
