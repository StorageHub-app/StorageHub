using System.Security.AccessControl;
using System.Security.Principal;
using StorageHub.Agent;

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

    [Fact]
    public void A_user_writable_directory_is_refused()
    {
        var directory = Path.Combine(_root, "portable");
        _ = Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "StorageHub.Agent.Windows.exe");
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
    [Fact]
    public void An_install_under_the_user_profile_is_refused()
    {
        var directory = Path.Combine(_root, "localappdata-like");
        _ = Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "StorageHub.Agent.Windows.exe");
        File.WriteAllText(executable, "not a real agent");

        _ = Assert.Throws<UnauthorizedAccessException>(
            () => AgentServiceStaging.EnsureSafeForService(executable));
    }

    [Fact]
    public void A_missing_directory_is_refused_rather_than_assumed_safe()
    {
        _ = Assert.Throws<DirectoryNotFoundException>(
            () => AgentServiceStaging.EnsureSafeForService(
                Path.Combine(_root, "absent", "StorageHub.Agent.Windows.exe")));
    }

    [Fact]
    public void The_staging_directory_is_inside_the_machine_data_root()
    {
        var staging = AgentServiceStaging.ResolveDirectory();
        var machineRoot = AgentHostLayout.ResolveDataRoot(AgentHostMode.WindowsService);

        Assert.StartsWith(machineRoot, staging, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(machineRoot, staging);
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
