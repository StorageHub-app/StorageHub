using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using StorageHub.Testing;

namespace StorageHub.Infrastructure.Windows.Tests;

public sealed class WindowsAgentDataDirectoryLeaseTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(),
        $"storagehub-agent-root-{Guid.NewGuid():N}");

    [WindowsOnlyFact]
    public void Acquire_protects_agent_tree_without_rewriting_siblings_and_holds_per_user_lock()
    {
        var dataRoot = Path.Combine(_testRoot, "data");
        var lockRoot = Path.Combine(_testRoot, "instance");
        var existingDirectory = Path.Combine(dataRoot, "Agent", "existing");
        var existingFile = Path.Combine(existingDirectory, "state.bin");
        var desktopCache = Path.Combine(dataRoot, "Desktop", "listing-cache.db");
        Directory.CreateDirectory(existingDirectory);
        File.WriteAllBytes(existingFile, [1, 2, 3]);
        Directory.CreateDirectory(Path.GetDirectoryName(desktopCache)!);
        File.WriteAllBytes(desktopCache, [4, 5, 6]);
        var siblingSecurityBefore = new FileInfo(desktopCache).GetAccessControl(
            AccessControlSections.Owner | AccessControlSections.Access);

        using var first = WindowsAgentDataDirectoryLease.Acquire(dataRoot, lockRoot);

        Assert.Equal(Path.GetFullPath(dataRoot), first.RootDirectory);
        Assert.Equal(Path.Combine(Path.GetFullPath(dataRoot), "Agent"), first.AgentDirectory);
        Assert.Equal(Path.Combine(first.AgentDirectory, "CodeLogic"), first.FrameworkDirectory);
        AssertProtectedForCurrentUser(new DirectoryInfo(dataRoot).GetAccessControl(
            AccessControlSections.Owner | AccessControlSections.Access));
        AssertProtectedForCurrentUser(new DirectoryInfo(existingDirectory).GetAccessControl(
            AccessControlSections.Owner | AccessControlSections.Access));
        AssertProtectedForCurrentUser(new FileInfo(existingFile).GetAccessControl(
            AccessControlSections.Owner | AccessControlSections.Access));
        var siblingSecurityAfter = new FileInfo(desktopCache).GetAccessControl(
            AccessControlSections.Owner | AccessControlSections.Access);
        Assert.Equal(siblingSecurityBefore.AreAccessRulesProtected, siblingSecurityAfter.AreAccessRulesProtected);

        var error = Assert.Throws<WindowsAgentDataDirectoryException>(
            () => WindowsAgentDataDirectoryLease.Acquire(dataRoot, lockRoot));
        Assert.Equal(WindowsAgentDataDirectoryFailure.InUse, error.Failure);
    }

    [WindowsOnlyFact]
    public void Different_data_roots_still_share_one_user_instance_lock()
    {
        var lockRoot = Path.Combine(_testRoot, "instance");
        using var first = WindowsAgentDataDirectoryLease.Acquire(
            Path.Combine(_testRoot, "first"),
            lockRoot);

        var error = Assert.Throws<WindowsAgentDataDirectoryException>(() =>
            WindowsAgentDataDirectoryLease.Acquire(Path.Combine(_testRoot, "second"), lockRoot));

        Assert.Equal(WindowsAgentDataDirectoryFailure.InUse, error.Failure);
    }

    [WindowsOnlyFact]
    public void Instance_lock_can_live_inside_default_data_root()
    {
        var dataRoot = Path.Combine(_testRoot, "data");
        var lockRoot = Path.Combine(dataRoot, "AgentInstance");

        using var lease = WindowsAgentDataDirectoryLease.Acquire(dataRoot, lockRoot);

        Assert.True(Directory.Exists(lease.FrameworkDirectory));
        AssertProtectedForCurrentUser(new DirectoryInfo(lockRoot).GetAccessControl(
            AccessControlSections.Owner | AccessControlSections.Access));
    }

    [WindowsOnlyFact]
    public async Task Lease_can_be_released_on_another_thread_and_reacquired()
    {
        var dataRoot = Path.Combine(_testRoot, "data");
        var lockRoot = Path.Combine(_testRoot, "instance");
        var first = WindowsAgentDataDirectoryLease.Acquire(dataRoot, lockRoot);

        await Task.Run(first.Dispose);
        using var second = WindowsAgentDataDirectoryLease.Acquire(dataRoot, lockRoot);

        Assert.Equal(Path.GetFullPath(dataRoot), second.RootDirectory);
    }

    [WindowsOnlyFact]
    public void Relative_unc_and_volume_root_paths_are_rejected()
    {
        var lockRoot = Path.Combine(_testRoot, "instance");
        AssertFailure("relative\\StorageHub", lockRoot, WindowsAgentDataDirectoryFailure.InvalidPath);
        AssertFailure("\\\\server\\share\\StorageHub", lockRoot, WindowsAgentDataDirectoryFailure.RemoteVolume);
        AssertFailure(
            Path.GetPathRoot(Path.GetFullPath(_testRoot))!,
            lockRoot,
            WindowsAgentDataDirectoryFailure.VolumeRoot);
    }

    [WindowsOnlyFact]
    public void Data_and_application_trees_must_be_disjoint()
    {
        var applicationRoot = Path.Combine(_testRoot, "StorageHub.Desktop");
        var siblingDataRoot = Path.Combine(_testRoot, "StorageHub");

        WindowsAgentDataDirectoryLease.EnsureDataRootIsSeparateFromApplication(
            siblingDataRoot,
            applicationRoot);

        _ = Assert.Throws<WindowsAgentDataDirectoryException>(() =>
            WindowsAgentDataDirectoryLease.EnsureDataRootIsSeparateFromApplication(
                applicationRoot,
                applicationRoot));
        _ = Assert.Throws<WindowsAgentDataDirectoryException>(() =>
            WindowsAgentDataDirectoryLease.EnsureDataRootIsSeparateFromApplication(
                Path.Combine(applicationRoot, "Data"),
                applicationRoot));
        _ = Assert.Throws<WindowsAgentDataDirectoryException>(() =>
            WindowsAgentDataDirectoryLease.EnsureDataRootIsSeparateFromApplication(
                _testRoot,
                applicationRoot));
    }

    [WindowsOnlyFact]
    public void Packaged_agent_resolves_the_complete_velopack_owned_tree()
    {
        var applicationRoot = Path.Combine(_testRoot, "StorageHub.Desktop");
        var agentDirectory = Path.Combine(applicationRoot, "current", "Agent");

        var resolvedRoot = WindowsAgentDataDirectoryLease.ResolveApplicationOwnedTreeRoot(
            agentDirectory);

        Assert.Equal(Path.GetFullPath(applicationRoot), resolvedRoot);
        _ = Assert.Throws<WindowsAgentDataDirectoryException>(() =>
            WindowsAgentDataDirectoryLease.EnsureDataRootIsSeparateFromApplication(
                Path.Combine(applicationRoot, "Data"),
                resolvedRoot));
        _ = Assert.Throws<WindowsAgentDataDirectoryException>(() =>
            WindowsAgentDataDirectoryLease.EnsureDataRootIsSeparateFromApplication(
                Path.Combine(applicationRoot, "current", "Data"),
                resolvedRoot));
    }

    [WindowsOnlyFact]
    public void Non_packaged_agent_keeps_its_exact_application_directory()
    {
        var applicationDirectory = Path.Combine(_testRoot, "bin", "Release", "net10.0-windows");

        var resolvedRoot = WindowsAgentDataDirectoryLease.ResolveApplicationOwnedTreeRoot(
            applicationDirectory);

        Assert.Equal(Path.GetFullPath(applicationDirectory), resolvedRoot);
    }

    [WindowsOnlyFact]
    public void Application_tree_must_not_contain_the_fixed_instance_lock()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var defaultDataRoot = Path.Combine(localAppData, "StorageHub");

        _ = Assert.Throws<WindowsAgentDataDirectoryException>(() =>
            WindowsAgentDataDirectoryLease.EnsureApplicationTreeIsSeparateFromInstanceLock(
                defaultDataRoot));
        WindowsAgentDataDirectoryLease.EnsureApplicationTreeIsSeparateFromInstanceLock(
            Path.Combine(localAppData, "StorageHub.Desktop"));
    }

    [WindowsOnlyFact]
    public void Reparse_point_in_ancestor_path_is_rejected()
    {
        Directory.CreateDirectory(_testRoot);
        var target = Path.Combine(_testRoot, "target");
        var junction = Path.Combine(_testRoot, "junction");
        Directory.CreateDirectory(target);
        CreateJunction(junction, target);

        try
        {
            var error = Assert.Throws<WindowsAgentDataDirectoryException>(() =>
                WindowsAgentDataDirectoryLease.Acquire(
                    Path.Combine(junction, "StorageHub"),
                    Path.Combine(_testRoot, "instance")));
            Assert.Equal(WindowsAgentDataDirectoryFailure.ReparsePoint, error.Failure);
        }
        finally
        {
            if (Directory.Exists(junction))
            {
                Directory.Delete(junction);
            }
        }
    }

    [WindowsOnlyFact]
    public void Reparse_point_anywhere_below_existing_data_root_is_rejected()
    {
        Directory.CreateDirectory(_testRoot);
        var dataRoot = Path.Combine(_testRoot, "data");
        var target = Path.Combine(_testRoot, "target");
        var junction = Path.Combine(dataRoot, "Agent", "vault");
        Directory.CreateDirectory(Path.GetDirectoryName(junction)!);
        Directory.CreateDirectory(target);
        CreateJunction(junction, target);

        try
        {
            var error = Assert.Throws<WindowsAgentDataDirectoryException>(() =>
                WindowsAgentDataDirectoryLease.Acquire(
                    dataRoot,
                    Path.Combine(_testRoot, "instance")));
            Assert.Equal(WindowsAgentDataDirectoryFailure.ReparsePoint, error.Failure);
        }
        finally
        {
            if (Directory.Exists(junction))
            {
                Directory.Delete(junction);
            }
        }
    }

    [WindowsOnlyFact]
    public void Reparse_point_below_sibling_data_does_not_block_agent_lease()
    {
        Directory.CreateDirectory(_testRoot);
        var dataRoot = Path.Combine(_testRoot, "data");
        var target = Path.Combine(_testRoot, "target");
        var junction = Path.Combine(dataRoot, "Desktop", "external-cache");
        Directory.CreateDirectory(Path.GetDirectoryName(junction)!);
        Directory.CreateDirectory(target);
        CreateJunction(junction, target);

        try
        {
            using var lease = WindowsAgentDataDirectoryLease.Acquire(
                dataRoot,
                Path.Combine(_testRoot, "instance"));

            Assert.True(Directory.Exists(lease.FrameworkDirectory));
        }
        finally
        {
            if (Directory.Exists(junction))
            {
                Directory.Delete(junction);
            }
        }
    }

    /// <summary>
    /// The service's tree stays reachable by administrators. Its secrets are sealed with the machine
    /// key, which any administrator can already use, so a LocalSystem-only tree buys no secrecy --
    /// and it breaks the elevated switch back to a session mode, which runs as the signed-in user
    /// and has to read the database and vault it is bringing home.
    /// </summary>
    [WindowsOnlyFact]
    public void A_machine_scoped_tree_also_grants_administrators()
    {
        var dataRoot = Path.Combine(_testRoot, "data");
        var agentFile = Path.Combine(dataRoot, "Agent", "storagehub.db");
        Directory.CreateDirectory(Path.GetDirectoryName(agentFile)!);
        File.WriteAllBytes(agentFile, [1, 2, 3]);

        using var lease = WindowsAgentDataDirectoryLease.Acquire(
            dataRoot,
            Path.Combine(_testRoot, "instance"),
            AgentDataTreeScope.Machine);

        AssertProtectedForCurrentUserAndAdministrators(new DirectoryInfo(lease.AgentDirectory)
            .GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access));
        AssertProtectedForCurrentUserAndAdministrators(new FileInfo(agentFile)
            .GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access));
    }

    /// <summary>
    /// The instance lock is per-user by definition and lives in that user's own profile, so the
    /// tree's scope must not widen it.
    /// </summary>
    [WindowsOnlyFact]
    public void A_machine_scoped_tree_leaves_the_instance_lock_to_its_own_user()
    {
        var lockRoot = Path.Combine(_testRoot, "instance");

        using var lease = WindowsAgentDataDirectoryLease.Acquire(
            Path.Combine(_testRoot, "data"),
            lockRoot,
            AgentDataTreeScope.Machine);

        AssertProtectedForCurrentUser(new DirectoryInfo(lockRoot).GetAccessControl(
            AccessControlSections.Owner | AccessControlSections.Access));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    private static void AssertProtectedForCurrentUserAndAdministrators(FileSystemSecurity security)
    {
        var currentUser = WindowsIdentity.GetCurrent().User;
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        Assert.Equal(currentUser, security.GetOwner(typeof(SecurityIdentifier)));
        Assert.True(security.AreAccessRulesProtected);
        var rules = security
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();

        // Exactly these two, so an inherited or leftover grant cannot hide among them.
        Assert.Equal(2, rules.Length);
        foreach (var expected in new[] { currentUser!, administrators })
        {
            var rule = Assert.Single(rules, candidate => expected.Equals(candidate.IdentityReference));
            Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
            Assert.Equal(
                FileSystemRights.FullControl,
                rule.FileSystemRights & FileSystemRights.FullControl);
        }
    }

    private static void AssertProtectedForCurrentUser(FileSystemSecurity security)
    {
        var currentUser = WindowsIdentity.GetCurrent().User;
        Assert.Equal(currentUser, security.GetOwner(typeof(SecurityIdentifier)));
        Assert.True(security.AreAccessRulesProtected);
        var rule = Assert.Single(security
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>());
        Assert.Equal(currentUser, rule.IdentityReference);
        Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
        Assert.Equal(FileSystemRights.FullControl, rule.FileSystemRights & FileSystemRights.FullControl);
    }

    private static void AssertFailure(
        string path,
        string lockRoot,
        WindowsAgentDataDirectoryFailure expected)
    {
        var error = Assert.Throws<WindowsAgentDataDirectoryException>(
            () => WindowsAgentDataDirectoryLease.Acquire(path, lockRoot));
        Assert.Equal(expected, error.Failure);
    }

    private static void CreateJunction(string junction, string target)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            ArgumentList = { "/d", "/c", "mklink", "/J", junction, target }
        }) ?? throw new InvalidOperationException("Could not start the junction helper.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(
            process.ExitCode == 0,
            $"Could not create test junction. {standardOutput} {standardError}");
    }
}
