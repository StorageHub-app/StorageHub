using System.Security.AccessControl;
using System.Security.Principal;

namespace StorageHub.Infrastructure.Windows;

/// <summary>
/// Who, besides the account running the agent, may reach its durable state.
/// </summary>
public enum AgentDataTreeScope
{
    /// <summary>
    /// The running account and nobody else. Right for a session agent: its vault is sealed with
    /// that user's own DPAPI key, so restricting the files to them matches what the secrets
    /// themselves already guarantee.
    /// </summary>
    CurrentUser,

    /// <summary>
    /// The running account and the machine's administrators. Right for the service, whose vault is
    /// sealed with the machine key -- an administrator can already read those secrets, and StorageHub
    /// says so before installing the service, so locking the files to LocalSystem buys no secrecy.
    ///
    /// What it does buy is a working switch back. Moving the installation out of the machine
    /// location runs elevated as the signed-in user, because that is the only identity that can
    /// write their own user-scoped vault; with a LocalSystem-only tree, that process cannot read
    /// the database or the secrets it is supposed to be bringing home.
    /// </summary>
    Machine
}

/// <summary>
/// Owns the per-user agent instance lock and protects agent-owned durable state.
/// </summary>
public sealed class WindowsAgentDataDirectoryLease : IDisposable, IAsyncDisposable
{
    private const string LockFileName = ".storagehub-agent.v1.lock";
    private FileStream? _instanceLock;

    private WindowsAgentDataDirectoryLease(string rootDirectory, FileStream instanceLock)
    {
        RootDirectory = rootDirectory;
        AgentDirectory = Path.Combine(rootDirectory, "Agent");
        FrameworkDirectory = Path.Combine(AgentDirectory, "CodeLogic");
        _instanceLock = instanceLock;
    }

    /// <summary>Gets the validated and protected StorageHub data root.</summary>
    public string RootDirectory { get; }

    /// <summary>Gets the protected directory containing agent-owned durable state.</summary>
    public string AgentDirectory { get; }

    /// <summary>Gets the protected CodeLogic discovery directory.</summary>
    public string FrameworkDirectory { get; }

    /// <summary>
    /// Rejects layouts where an installer/update could replace durable state, or where
    /// durable-state protection could take ownership of packaged application files.
    /// </summary>
    public static void EnsureDataRootIsSeparateFromApplication(
        string rootDirectory,
        string applicationDirectory)
    {
        var fullRootPath = ValidateLocalPath(rootDirectory, "agent data directory");
        var fullApplicationPath = ValidateLocalPath(applicationDirectory, "application directory");
        if (IsSamePathOrAncestor(fullRootPath, fullApplicationPath) ||
            IsSamePathOrAncestor(fullApplicationPath, fullRootPath))
        {
            throw new WindowsAgentDataDirectoryException(
                WindowsAgentDataDirectoryFailure.InvalidPath,
                "The StorageHub data directory must not overlap the installed application directory.");
        }
    }

    /// <summary>
    /// Resolves the complete Velopack-owned tree for an Agent located at
    /// <c>&lt;package&gt;\current\Agent</c>. Non-packaged hosts retain their exact
    /// application directory so source and test execution are not broadened.
    /// </summary>
    public static string ResolveApplicationOwnedTreeRoot(string applicationDirectory)
    {
        var fullApplicationPath = ValidateLocalPath(applicationDirectory, "application directory");
        var agentDirectory = new DirectoryInfo(fullApplicationPath);
        var currentDirectory = agentDirectory.Parent;
        var packageDirectory = currentDirectory?.Parent;
        return string.Equals(agentDirectory.Name, "Agent", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(currentDirectory?.Name, "current", StringComparison.OrdinalIgnoreCase) &&
            packageDirectory is not null
                ? packageDirectory.FullName
                : fullApplicationPath;
    }

    /// <summary>
    /// Rejects a custom package location that contains the fixed per-user
    /// instance lease, which must remain outside installer-owned directories.
    /// </summary>
    public static void EnsureApplicationTreeIsSeparateFromInstanceLock(
        string applicationDirectory)
    {
        var fullApplicationPath = ValidateLocalPath(applicationDirectory, "application directory");
        var fullLockPath = ValidateLocalPath(
            GetDefaultInstanceLockDirectory(),
            "agent instance lock directory");
        if (IsSamePathOrAncestor(fullApplicationPath, fullLockPath) ||
            IsSamePathOrAncestor(fullLockPath, fullApplicationPath))
        {
            throw new WindowsAgentDataDirectoryException(
                WindowsAgentDataDirectoryFailure.InvalidPath,
                "The installed application directory must not overlap the per-user Agent instance lock.");
        }
    }

    /// <summary>
    /// Acquires the one-agent-per-Windows-user lock and protects the shared root plus the
    /// complete agent-owned subtree. The
    /// optional lock directory exists for isolated tests; production callers must use the fixed
    /// per-user location so different data-root overrides cannot start competing pipe servers.
    /// </summary>
    public static WindowsAgentDataDirectoryLease Acquire(
        string rootDirectory,
        string? instanceLockDirectory = null,
        AgentDataTreeScope scope = AgentDataTreeScope.CurrentUser)
    {
        var fullRootPath = ValidateLocalPath(rootDirectory, "agent data directory");
        var fullInstanceLockDirectory = ValidateLocalPath(
            instanceLockDirectory ?? GetDefaultInstanceLockDirectory(),
            "agent instance lock directory");
        FileStream? instanceLock = null;
        try
        {
            var currentUser = GetCurrentUser();
            // The instance lock stays the running account's alone whatever the tree's scope is: it
            // is per-user by definition, and lives in that user's own profile rather than in the
            // shared data root.
            instanceLock = AcquireInstanceLock(fullInstanceLockDirectory, [currentUser]);
            var trustees = ResolveTrustees(currentUser, scope);

            RejectReparsePointsInPath(fullRootPath, "agent data directory");
            Directory.CreateDirectory(fullRootPath);
            ProtectDirectory(fullRootPath, currentUser, trustees);
            var agentDirectory = Path.Combine(fullRootPath, "Agent");
            var frameworkDirectory = Path.Combine(agentDirectory, "CodeLogic");
            Directory.CreateDirectory(frameworkDirectory);
            // The shared root also contains Desktop caches and developer VM fixtures.
            // They can be open or governed by their own ACL requirements and must not
            // prevent the background Agent from starting. Recursively harden only the
            // Agent subtree, which owns the database, vault, and runtime secrets.
            ProtectOwnedTree(agentDirectory, currentUser, trustees);

            var result = new WindowsAgentDataDirectoryLease(fullRootPath, instanceLock);
            instanceLock = null;
            return result;
        }
        catch (WindowsAgentDataDirectoryException)
        {
            throw;
        }
        catch (UnauthorizedAccessException error)
        {
            throw new WindowsAgentDataDirectoryException(
                WindowsAgentDataDirectoryFailure.AccessDenied,
                "The StorageHub data directory could not be protected for the current user.",
                error);
        }
        catch (IOException error) when (IsSharingViolation(error))
        {
            throw new WindowsAgentDataDirectoryException(
                WindowsAgentDataDirectoryFailure.InUse,
                "Another StorageHub Agent instance is already running for this Windows user.",
                error);
        }
        catch (IOException error)
        {
            throw new WindowsAgentDataDirectoryException(
                WindowsAgentDataDirectoryFailure.Unavailable,
                "The StorageHub data directory is unavailable.",
                error);
        }
        catch (Exception error) when (error is System.Security.SecurityException or InvalidOperationException)
        {
            throw new WindowsAgentDataDirectoryException(
                WindowsAgentDataDirectoryFailure.AccessDenied,
                "The StorageHub data-directory security identity could not be established.",
                error);
        }
        finally
        {
            instanceLock?.Dispose();
        }
    }

    public void Dispose() => Interlocked.Exchange(ref _instanceLock, null)?.Dispose();

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private static string GetDefaultInstanceLockDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new WindowsAgentDataDirectoryException(
                WindowsAgentDataDirectoryFailure.InvalidPath,
                "The current user's local application-data directory is unavailable.");
        }

        return Path.Combine(localAppData, "StorageHub", "AgentInstance");
    }

    /// <summary>
    /// The accounts given full control of the tree. The owner always; the machine's administrators
    /// as well when the tree is machine-scoped, because an elevated switch back out of the service
    /// has to be able to read it.
    /// </summary>
    private static SecurityIdentifier[] ResolveTrustees(
        SecurityIdentifier owner,
        AgentDataTreeScope scope)
    {
        if (scope != AgentDataTreeScope.Machine)
        {
            return [owner];
        }

        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        return owner.Equals(administrators) ? [owner] : [owner, administrators];
    }

    private static FileStream AcquireInstanceLock(
        string lockDirectory,
        SecurityIdentifier[] trustees)
    {
        var currentUser = trustees[0];
        RejectReparsePointsInPath(lockDirectory, "agent instance lock directory");
        Directory.CreateDirectory(lockDirectory);
        RejectReparsePointsInPath(lockDirectory, "agent instance lock directory");
        ProtectDirectory(lockDirectory, currentUser, trustees);

        var lockPath = Path.Combine(lockDirectory, LockFileName);
        RejectReparsePointEntryIfPresent(lockPath, "agent instance lock");
        FileStream? instanceLock = null;
        try
        {
            instanceLock = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.WriteThrough);
            RejectReparsePointEntryIfPresent(lockPath, "agent instance lock");
            ProtectFile(lockPath, currentUser, trustees);
            var attributes = File.GetAttributes(lockPath);
            File.SetAttributes(
                lockPath,
                attributes | FileAttributes.Hidden | FileAttributes.NotContentIndexed);
            var result = instanceLock;
            instanceLock = null;
            return result;
        }
        finally
        {
            instanceLock?.Dispose();
        }
    }

    private static string ValidateLocalPath(string path, string description)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new WindowsAgentDataDirectoryException(
                WindowsAgentDataDirectoryFailure.InvalidPath,
                $"The {description} must be an absolute path.");
        }

        if (path.StartsWith("\\\\", StringComparison.Ordinal))
        {
            throw new WindowsAgentDataDirectoryException(
                WindowsAgentDataDirectoryFailure.RemoteVolume,
                $"The {description} must be on a local Windows volume.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new WindowsAgentDataDirectoryException(
                WindowsAgentDataDirectoryFailure.InvalidPath,
                $"The {description} path is invalid.",
                error);
        }

        var volumeRoot = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(volumeRoot))
        {
            throw new WindowsAgentDataDirectoryException(
                WindowsAgentDataDirectoryFailure.InvalidPath,
                $"The {description} has no volume root.");
        }

        if (string.Equals(
                Path.TrimEndingDirectorySeparator(fullPath),
                Path.TrimEndingDirectorySeparator(volumeRoot),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new WindowsAgentDataDirectoryException(
                WindowsAgentDataDirectoryFailure.VolumeRoot,
                $"A Windows volume root cannot be used as the {description}.");
        }

        DriveType driveType;
        try
        {
            driveType = new DriveInfo(volumeRoot).DriveType;
        }
        catch (Exception error) when (error is ArgumentException or IOException)
        {
            throw new WindowsAgentDataDirectoryException(
                WindowsAgentDataDirectoryFailure.InvalidPath,
                $"The {description} volume could not be resolved.",
                error);
        }

        if (driveType is DriveType.Network or DriveType.NoRootDirectory or DriveType.Unknown)
        {
            throw new WindowsAgentDataDirectoryException(
                WindowsAgentDataDirectoryFailure.RemoteVolume,
                $"The {description} must be on a local Windows volume.");
        }

        return fullPath;
    }

    private static bool IsSamePathOrAncestor(string candidateAncestor, string candidateDescendant)
    {
        var ancestor = Path.TrimEndingDirectorySeparator(candidateAncestor);
        var descendant = Path.TrimEndingDirectorySeparator(candidateDescendant);
        if (string.Equals(ancestor, descendant, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return descendant.StartsWith(
            ancestor + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    private static SecurityIdentifier GetCurrentUser() =>
        WindowsIdentity.GetCurrent().User ??
        throw new InvalidOperationException("The current Windows user SID is unavailable.");

    private static void ProtectOwnedTree(
        string fullPath,
        SecurityIdentifier owner,
        SecurityIdentifier[] trustees)
    {
        ProtectDirectory(fullPath, owner, trustees);
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(fullPath);
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = 0
        };

        while (pendingDirectories.TryPop(out var directoryPath))
        {
            foreach (var entry in new DirectoryInfo(directoryPath)
                .EnumerateFileSystemInfos("*", enumerationOptions))
            {
                var attributes = entry.Attributes;
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new WindowsAgentDataDirectoryException(
                        WindowsAgentDataDirectoryFailure.ReparsePoint,
                        "The StorageHub data tree cannot contain reparse points.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    ProtectDirectory(entry.FullName, owner, trustees);
                    pendingDirectories.Push(entry.FullName);
                }
                else
                {
                    ProtectFile(entry.FullName, owner, trustees);
                }
            }
        }
    }

    private static void RejectReparsePointsInPath(string fullPath, string description)
    {
        for (DirectoryInfo? directory = new(fullPath); directory is not null; directory = directory.Parent)
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(directory.FullName);
            }
            catch (FileNotFoundException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new WindowsAgentDataDirectoryException(
                    WindowsAgentDataDirectoryFailure.ReparsePoint,
                    $"The {description} cannot contain a reparse point in its path.");
            }

            if ((attributes & FileAttributes.Directory) == 0)
            {
                throw new WindowsAgentDataDirectoryException(
                    WindowsAgentDataDirectoryFailure.InvalidPath,
                    $"The {description} path contains a non-directory entry.");
            }
        }
    }

    private static void ProtectDirectory(
        string fullPath,
        SecurityIdentifier owner,
        SecurityIdentifier[] trustees)
    {
        var security = new DirectorySecurity();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var trustee in trustees)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                trustee,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        var directory = new DirectoryInfo(fullPath);
        directory.SetAccessControl(security);
        VerifyAccessRules(
            directory.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access),
            owner,
            trustees,
            "directory");
    }

    private static void ProtectFile(
        string fullPath,
        SecurityIdentifier owner,
        SecurityIdentifier[] trustees)
    {
        var security = new FileSecurity();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var trustee in trustees)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                trustee,
                FileSystemRights.FullControl,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        var file = new FileInfo(fullPath);
        file.SetAccessControl(security);
        VerifyAccessRules(
            file.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access),
            owner,
            trustees,
            "file");
    }

    /// <summary>
    /// Reads the applied security back and insists it says exactly what was asked for: this owner,
    /// no inheritance, and one full-control rule per intended trustee and nothing else. Checking
    /// the count is the point -- a rule that survived is a rule nobody intended.
    /// </summary>
    private static void VerifyAccessRules(
        FileSystemSecurity security,
        SecurityIdentifier owner,
        SecurityIdentifier[] trustees,
        string entryKind)
    {
        if (!owner.Equals(security.GetOwner(typeof(SecurityIdentifier))) ||
            !security.AreAccessRulesProtected)
        {
            throw new IOException($"The StorageHub {entryKind} ownership or inheritance could not be verified.");
        }

        var explicitRules = security
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        if (explicitRules.Length != trustees.Length)
        {
            throw new IOException($"The StorageHub {entryKind} access rules could not be verified.");
        }

        foreach (var trustee in trustees)
        {
            var granted = explicitRules.Any(rule =>
                trustee.Equals(rule.IdentityReference) &&
                rule.AccessControlType == AccessControlType.Allow &&
                (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl);
            if (!granted)
            {
                throw new IOException($"The StorageHub {entryKind} access rules could not be verified.");
            }
        }
    }

    private static void RejectReparsePointEntryIfPresent(string path, string description)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new WindowsAgentDataDirectoryException(
                WindowsAgentDataDirectoryFailure.ReparsePoint,
                $"The {description} cannot be a reparse point.");
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            throw new WindowsAgentDataDirectoryException(
                WindowsAgentDataDirectoryFailure.InvalidPath,
                $"The {description} path is occupied by a directory.");
        }
    }

    private static bool IsSharingViolation(IOException error) =>
        (error.HResult & 0xFFFF) is 32 or 33;
}

public enum WindowsAgentDataDirectoryFailure
{
    InvalidPath,
    RemoteVolume,
    VolumeRoot,
    ReparsePoint,
    InUse,
    AccessDenied,
    Unavailable
}

public sealed class WindowsAgentDataDirectoryException : Exception
{
    public WindowsAgentDataDirectoryException(
        WindowsAgentDataDirectoryFailure failure,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    public WindowsAgentDataDirectoryFailure Failure { get; }
}
