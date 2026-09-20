using StorageHub.Security;

namespace StorageHub.Infrastructure.Unix;

/// <summary>
/// Writes a provider's key material to a file for the life of one connection, and removes it.
/// </summary>
/// <remarks>
/// This is what hands an SSH private key to SSH.NET, which needs a path rather than bytes. On
/// Windows the equivalent writes under %LOCALAPPDATA%, so the plaintext reaches a disk and is only
/// removed afterwards. Here the root is the runtime root, which is tmpfs under XDG_RUNTIME_DIR - the
/// material never touches durable storage and is gone when the session ends, even if the process
/// does not get to clean up. That is a strict improvement rather than a port.
///
/// It also happens to be the mode ssh itself insists on: a key file that is not 0600 is refused
/// outright, so this is the correct implementation for any future shell-out as well.
/// </remarks>
public sealed class UnixRuntimeSecretFileMaterializer : IRuntimeSecretFileMaterializer
{
    private const int MaximumSecretLength = 16 * 1024 * 1024;
    private readonly string _rootDirectory;

    public UnixRuntimeSecretFileMaterializer(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        if (!Path.IsPathFullyQualified(rootDirectory))
        {
            throw new ArgumentException(
                "The runtime-secret directory must be an absolute path.",
                nameof(rootDirectory));
        }

        // /proc and /sys are not storage, and a path under them is a sign something is badly
        // misconfigured rather than a directory to create.
        if (rootDirectory.StartsWith("/proc/", StringComparison.Ordinal) ||
            rootDirectory.StartsWith("/sys/", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Runtime secrets cannot be materialized under a kernel filesystem.",
                nameof(rootDirectory));
        }

        _rootDirectory = Path.GetFullPath(rootDirectory);
    }

    public async ValueTask<IRuntimeSecretFile> MaterializeAsync(
        ReadOnlyMemory<byte> secret,
        string fileExtension,
        CancellationToken cancellationToken = default)
    {
        if (secret.IsEmpty || secret.Length > MaximumSecretLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(secret),
                "Runtime secret files must contain 1 byte to 16 MiB.");
        }

        var extension = ValidateExtension(fileExtension);
        UnixFileSystem.EnsurePrivateDirectory(_rootDirectory);

        // A leading dot is the Unix analogue of the hidden attribute the Windows version sets. It
        // is cosmetic on both, and the mode is what actually protects the file.
        var path = Path.Combine(_rootDirectory, $".material-{Guid.NewGuid():N}{extension}");
        try
        {
            await using (var stream = UnixFileSystem.CreatePrivateFile(path))
            {
                await stream.WriteAsync(secret, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            var lifetimeLease = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1,
                FileOptions.None);
            return new RuntimeSecretFile(path, lifetimeLease);
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    /// <summary>
    /// Removes material left behind by a process that did not get to clean up.
    /// </summary>
    /// <remarks>
    /// Less load-bearing here than on Windows, because tmpfs clears at the end of the session
    /// anyway. It still matters for a long-lived lingering agent, which may not see one for weeks.
    /// </remarks>
    public int ScavengeOrphans(TimeSpan minimumAge)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumAge, TimeSpan.FromMinutes(1));

        UnixFileSystem.EnsurePrivateDirectory(_rootDirectory);
        var threshold = DateTime.UtcNow - minimumAge;
        var removed = 0;
        foreach (var path in Directory.EnumerateFiles(_rootDirectory, ".material-*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) <= threshold)
                {
                    File.Delete(path);
                    removed++;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }

        return removed;
    }

    private static string ValidateExtension(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 12 || value[0] != '.' || !IsAlphanumeric(value.AsSpan(1)))
        {
            throw new ArgumentException("A simple alphanumeric file extension is required.", nameof(value));
        }

        return value.ToLowerInvariant();
    }

    private static bool IsAlphanumeric(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class RuntimeSecretFile(string fullPath, FileStream lifetimeLease) : IRuntimeSecretFile
    {
        private string? _path = fullPath;
        private FileStream? _lease = lifetimeLease;

        public string FullPath => Volatile.Read(ref _path)
            ?? throw new ObjectDisposedException(nameof(RuntimeSecretFile));

        public async ValueTask DisposeAsync()
        {
            var lease = Interlocked.Exchange(ref _lease, null);
            var path = Interlocked.Exchange(ref _path, null);
            if (lease is not null)
            {
                await lease.DisposeAsync().ConfigureAwait(false);
            }

            if (path is not null)
            {
                TryDelete(path);
            }
        }
    }
}
