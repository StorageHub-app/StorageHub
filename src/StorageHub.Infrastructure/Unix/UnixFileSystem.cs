using System.Runtime.InteropServices;

namespace StorageHub.Infrastructure.Unix;

/// <summary>
/// The filesystem primitives StorageHub's Unix security rests on.
/// </summary>
/// <remarks>
/// Both the socket directory and the vault answer the same three questions - who owns this, what is
/// its mode, and is it really the thing it appears to be rather than a link to somewhere else - so
/// they ask them here. Duplicating a syscall wrapper that decides whether another user can read a
/// vault is the kind of duplication that only diverges in one direction.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public static partial class UnixFileSystem
{
    private const int AtFdCwd = -100;
    private const int AtSymlinkNoFollow = 0x100;
    private const uint StatxUid = 0x00000008;

    /// <summary>Owner read and write, and nothing else. What a private file must be.</summary>
    public const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>Owner read, write and traverse. What a private directory must be.</summary>
    public const UnixFileMode PrivateDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    [LibraryImport("libc")]
    private static partial uint geteuid();

    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int statx(int dirfd, string pathname, int flags, uint mask, byte[] buffer);

    /// <summary>This process's effective user.</summary>
    public static uint EffectiveUserId() => geteuid();

    /// <summary>
    /// The owner of a filesystem entry.
    /// </summary>
    /// <remarks>
    /// Read through statx rather than stat. .NET exposes a Unix file's mode but not its owner, and
    /// struct stat's layout differs between architectures - st_uid sits at a different offset on
    /// x86-64 than on aarch64, which is exactly the kind of difference that works on the machine it
    /// was written on. struct statx has fixed offsets on every architecture, so stx_uid is at 20
    /// everywhere. The link is not followed, because the question is about this entry.
    /// </remarks>
    public static uint OwnerUserId(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // Generously sized: the kernel writes at most sizeof(struct statx), which is 256 bytes, and
        // only the first 24 are read here.
        var buffer = new byte[512];
        if (statx(AtFdCwd, path, AtSymlinkNoFollow, StatxUid, buffer) != 0)
        {
            throw new IOException(
                $"Could not read the owner of '{path}' (errno {Marshal.GetLastPInvokeError()}).");
        }

        const int StxUidOffset = 20;
        return BitConverter.ToUInt32(buffer, StxUidOffset);
    }

    /// <summary>
    /// Creates a directory private if it is missing, then proves it is.
    /// </summary>
    /// <remarks>
    /// Every missing level is created separately, because Directory.CreateDirectory applies its
    /// unix mode to the final directory only. Creating .../storagehub/runtime-secrets in one call
    /// left .../storagehub at the umask default of 0755 - world-readable, holding the socket the
    /// secret channel was about to be published on. The confidential-channel check caught it, which
    /// is the only reason it is a comment here rather than a shipped hole.
    /// </remarks>
    public static void EnsurePrivateDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
        {
            var missing = new Stack<string>();
            for (var level = directory; !string.IsNullOrEmpty(level) && !Directory.Exists(level);
                 level = Path.GetDirectoryName(level) ?? string.Empty)
            {
                missing.Push(level);
            }

            // Created with the mode rather than chmod-ed afterwards, so there is no window in which
            // a level exists and is readable by anyone else.
            while (missing.Count > 0)
            {
                Directory.CreateDirectory(missing.Pop(), PrivateDirectoryMode);
            }
        }

        VerifyPrivate(directory, PrivateDirectoryMode, "directory");
    }

    /// <summary>Refuses a path that is not exactly the expected mode, not ours, or a symlink.</summary>
    public static void VerifyPrivate(string path, UnixFileMode expected, string what)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new IOException($"The StorageHub {what} '{path}' does not exist.");
        }

        // A symlink is the case worth spelling out: without this, whoever can create the entry
        // first could point it at something they control, and everything written through it would
        // be theirs to read.
        var linkTarget = Directory.Exists(path)
            ? new DirectoryInfo(path).LinkTarget
            : new FileInfo(path).LinkTarget;
        if (linkTarget is not null)
        {
            throw new IOException(
                $"The StorageHub {what} '{path}' is a symbolic link, which is not trusted.");
        }

        var mode = File.GetUnixFileMode(path);
        if (mode != expected)
        {
            throw new IOException(
                $"The StorageHub {what} '{path}' must be accessible only by its owner "
                    + $"(expected {expected}, found {mode}).");
        }

        var owner = OwnerUserId(path);
        var self = EffectiveUserId();
        if (owner != self)
        {
            throw new IOException(
                $"The StorageHub {what} '{path}' is owned by uid {owner}, not {self}.");
        }
    }

    /// <summary>
    /// Opens a new file that is private from the moment it exists.
    /// </summary>
    /// <remarks>
    /// UnixCreateMode applies the mode at creation. The alternative - create, then
    /// File.SetUnixFileMode - leaves a window in which the file exists with the default mode, and
    /// for a file holding a vault's master key that window is the whole problem.
    /// </remarks>
    public static FileStream CreatePrivateFile(string path) =>
        new(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            UnixCreateMode = PrivateFileMode,
            Options = FileOptions.WriteThrough,
        });
}
