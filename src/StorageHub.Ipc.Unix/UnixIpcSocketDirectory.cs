namespace StorageHub.Ipc.Unix;

/// <summary>
/// Where StorageHub's sockets live, and the checks that make the location the security.
/// </summary>
/// <remarks>
/// A named pipe on Windows carries its own access control. A Unix socket does not: the file's mode
/// is advisory on some systems, and what reliably keeps another user out is that they cannot
/// traverse the directory holding it. So the directory is the boundary, and it is verified on every
/// listen and every connect rather than trusted because this process created it once.
/// </remarks>
public static class UnixIpcSocketDirectory
{
    /// <summary>
    /// sun_path is 108 bytes including its terminator, so a path has to fit in 107.
    /// </summary>
    /// <remarks>
    /// Checked where the endpoint is configured rather than left to bind, which reports a path that
    /// is silently truncated or an opaque SocketException. The paths in use are far shorter -
    /// /run/user/1000/storagehub/agent-secret.sock is 43 - but STORAGEHUB_DATA_ROOT can move them.
    /// </remarks>
    public const int MaximumSocketPathLength = 107;

    private const UnixFileMode PrivateDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>
    /// The directory StorageHub's sockets belong in for this user.
    /// </summary>
    /// <remarks>
    /// XDG_RUNTIME_DIR is the right answer: it is per-user, mode 0700, on tmpfs, and cleared when
    /// the session ends - so a stale socket cannot outlive a reboot. It is absent often enough to
    /// need a fallback, though: plain SSH sessions, containers, and cron jobs frequently have no
    /// login session at all. The fallback is a per-uid directory under the temp path, created and
    /// verified with the same rules, which is weaker only in that it survives a reboot.
    /// </remarks>
    public static string Resolve()
    {
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        return !string.IsNullOrWhiteSpace(runtimeDir)
            ? Path.Combine(runtimeDir, "storagehub")
            : Path.Combine(Path.GetTempPath(), $"storagehub-{UnixPeerCredentials.EffectiveUserId()}");
    }

    /// <summary>Creates the directory private if it is missing, then proves it is private.</summary>
    public static void EnsurePrivate(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
        {
            // Created with the mode rather than chmod-ed afterwards, so there is no window in
            // which the directory exists and is readable by anyone else.
            Directory.CreateDirectory(directory, PrivateDirectoryMode);
        }

        Verify(directory);
    }

    /// <summary>
    /// Refuses a directory that is not exactly 0700, not owned by this user, or a symlink.
    /// </summary>
    /// <remarks>
    /// The symlink case is the one worth spelling out: without it, another user who can create the
    /// parent entry first could point it at a directory they control, and the agent would publish
    /// its socket - including the secret channel - somewhere they can reach.
    /// </remarks>
    public static void Verify(string directory)
    {
        var info = new DirectoryInfo(directory);
        if (!info.Exists)
        {
            throw new IOException($"The StorageHub socket directory '{directory}' does not exist.");
        }

        if (info.LinkTarget is not null)
        {
            throw new IOException(
                $"The StorageHub socket directory '{directory}' is a symbolic link, which is not trusted.");
        }

        var mode = File.GetUnixFileMode(directory);
        if (mode != PrivateDirectoryMode)
        {
            throw new IOException(
                $"The StorageHub socket directory '{directory}' must be accessible only by its owner "
                    + $"(expected {PrivateDirectoryMode}, found {mode}).");
        }

        var owner = UnixPeerCredentials.OwnerUserId(directory);
        var self = UnixPeerCredentials.EffectiveUserId();
        if (owner != self)
        {
            throw new IOException(
                $"The StorageHub socket directory '{directory}' is owned by uid {owner}, not {self}.");
        }
    }

    /// <summary>Rejects a socket path this transport will not address.</summary>
    public static void ValidateSocketPath(string socketPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(socketPath);

        // A leading NUL is Linux's abstract namespace. It has no filesystem entry and therefore no
        // permissions, so any process in the network namespace could connect to it - which is the
        // one way to get this transport's trust model silently wrong.
        if (socketPath[0] == '\0')
        {
            throw new ArgumentException(
                "An abstract-namespace socket has no filesystem permissions and cannot be trusted.",
                nameof(socketPath));
        }

        if (!Path.IsPathRooted(socketPath))
        {
            throw new ArgumentException("A socket path must be absolute.", nameof(socketPath));
        }

        var byteLength = System.Text.Encoding.UTF8.GetByteCount(socketPath);
        if (byteLength > MaximumSocketPathLength)
        {
            throw new ArgumentException(
                $"The socket path is {byteLength} bytes, over the {MaximumSocketPathLength} byte limit "
                    + "for a Unix domain socket address.",
                nameof(socketPath));
        }
    }
}
