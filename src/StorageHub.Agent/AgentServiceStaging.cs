using System.Security.AccessControl;
using System.Security.Principal;

namespace StorageHub.Agent;

/// <summary>
/// Places the agent somewhere a LocalSystem service may safely run from.
///
/// StorageHub installs per user, under <c>%LOCALAPPDATA%</c>, and a portable or developer build
/// runs from wherever it was unpacked. Every one of those directories is writable by the user --
/// and a service binary that its own user can replace is a privilege escalation: swap the file,
/// wait for the service to restart, and your code runs as SYSTEM. Registering the service against
/// the install directory would hand that out on any machine where the user is not already an
/// administrator.
///
/// So the elevated install copies the agent into a machine-owned directory whose ACL denies
/// non-administrators any write at all, and the service runs from there. That also settles two
/// unrelated problems for free: Velopack replaces <c>current\</c> wholesale on update, which would
/// invalidate a path pointing into it, and a portable build may be moved or unplugged entirely.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class AgentServiceStaging
{
    /// <summary>
    /// The machine-owned tree the service runs from.
    ///
    /// It is a sibling of the service's data root, never a directory inside it. The agent refuses
    /// to start when its data root and its application directory overlap, because an update
    /// replacing the application tree would then be replacing durable state, and the ownership the
    /// agent takes of durable state would be taking ownership of packaged files. Staging into the
    /// data root tripped that check on every single start: the service could be registered, but
    /// exited immediately with "the StorageHub data directory must not overlap the installed
    /// application directory" -- and because a session agent refuses to run beside an installed
    /// service, the machine was then left with no agent at all.
    /// </summary>
    public static string ResolveRootDirectory()
    {
        var machineFolder = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return string.IsNullOrWhiteSpace(machineFolder)
            ? throw new InvalidOperationException("Windows did not report a machine data folder.")
            : Path.Combine(machineFolder, AgentHostLayout.ServiceName);
    }

    /// <summary>The machine-owned directory the service's binaries are staged into.</summary>
    public static string ResolveDirectory() => Path.Combine(ResolveRootDirectory(), "bin");

    /// <summary>The staged executable, whether or not it exists yet.</summary>
    public static string ResolveStagedExecutable(string executableName) =>
        Path.Combine(ResolveDirectory(), executableName);

    /// <summary>
    /// The version currently staged, or null when nothing is staged or it cannot be read.
    ///
    /// Updating the app does not update this copy: re-staging needs an elevated token the desktop
    /// does not have, and the service must not fetch it itself -- SYSTEM copying a binary out of a
    /// user-writable directory is the very escalation the staging exists to prevent. So the
    /// mismatch is detected and the operator is asked to re-apply.
    /// </summary>
    public static string? ReadStagedVersion(string executableName)
    {
        try
        {
            var staged = ResolveStagedExecutable(executableName);
            if (!File.Exists(staged))
            {
                return null;
            }

            var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(staged).ProductVersion;
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Copies the agent beside its dependencies into the protected directory and returns the
    /// executable to register. Replaces whatever was there, so an update re-stages cleanly.
    /// </summary>
    public static string Stage(string sourceExecutablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceExecutablePath);
        if (!File.Exists(sourceExecutablePath))
        {
            throw new FileNotFoundException("The agent executable was not found.", sourceExecutablePath);
        }

        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourceExecutablePath)) ??
            throw new ArgumentException("The agent executable has no directory.", nameof(sourceExecutablePath));
        var target = ResolveDirectory();
        // The parent is protected too. ProgramData lets any authenticated user create a directory
        // in it, so leaving the staging root unprotected would let somebody create it first and
        // hold it as its owner -- and an owner of the parent can replace the directory the service
        // runs from, which is the escalation this whole class exists to prevent.
        CreateProtectedDirectory(ResolveRootDirectory());
        CreateProtectedDirectory(target);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, file);
            var destination = Path.Combine(target, relative);
            var destinationDirectory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destinationDirectory))
            {
                _ = Directory.CreateDirectory(destinationDirectory);
            }

            File.Copy(file, destination, overwrite: true);
        }

        var staged = Path.Combine(target, Path.GetFileName(sourceExecutablePath));
        if (!File.Exists(staged))
        {
            throw new InvalidOperationException("The agent could not be staged for the service.");
        }

        RemoveLegacyStagingDirectory();
        return staged;
    }

    /// <summary>
    /// Deletes the copy left inside the data root by a build that staged there.
    ///
    /// Only reached once the new copy exists and the caller is about to register the service
    /// against it, so the worst case is a stale directory nobody points at. Best effort for the
    /// same reason: a few hundred orphaned files in ProgramData are untidy, not a reason to fail an
    /// install that has otherwise succeeded.
    /// </summary>
    private static void RemoveLegacyStagingDirectory()
    {
        var legacy = Path.Combine(
            AgentHostLayout.ResolveDataRoot(AgentHostMode.WindowsService), "bin");
        try
        {
            if (Directory.Exists(legacy))
            {
                Directory.Delete(legacy, recursive: true);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Still in use by the service being replaced, or already gone.
        }
    }

    /// <summary>
    /// Rejects a directory any non-administrator can write to. Belt and braces behind
    /// <see cref="Stage"/>: a service registered against a writable path is an escalation waiting
    /// to happen, so it is refused rather than merely discouraged.
    /// </summary>
    public static void EnsureSafeForService(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(executablePath));
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("The agent directory could not be resolved.");
        }

        var security = new DirectoryInfo(directory).GetAccessControl();
        foreach (FileSystemAccessRule rule in security.GetAccessRules(
            true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow ||
                (rule.FileSystemRights & WritingRights) == 0)
            {
                continue;
            }

            var sid = (SecurityIdentifier)rule.IdentityReference;
            if (!IsTrustedWriter(sid))
            {
                throw new UnauthorizedAccessException(
                    $"'{directory}' is writable by '{sid.Value}', so it cannot host a service that runs as LocalSystem.");
            }
        }
    }

    private const FileSystemRights WritingRights =
        FileSystemRights.WriteData |
        FileSystemRights.AppendData |
        FileSystemRights.Delete |
        FileSystemRights.ChangePermissions |
        FileSystemRights.TakeOwnership;

    private static bool IsTrustedWriter(SecurityIdentifier sid) =>
        sid.IsWellKnown(WellKnownSidType.LocalSystemSid) ||
        sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) ||
        sid.IsWellKnown(WellKnownSidType.CreatorOwnerSid);

    private static void CreateProtectedDirectory(string path)
    {
        var security = new DirectorySecurity();

        // Detached from inheritance: ProgramData grants authenticated users a create/append right
        // that would otherwise flow down here and defeat the whole exercise.
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var owner in new[]
        {
            WellKnownSidType.LocalSystemSid,
            WellKnownSidType.BuiltinAdministratorsSid
        })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(owner, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        // Everyone else may run it and read it, and change nothing.
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        if (Directory.Exists(path))
        {
            new DirectoryInfo(path).SetAccessControl(security);
            return;
        }

        _ = Directory.CreateDirectory(path);
        new DirectoryInfo(path).SetAccessControl(security);
    }
}
