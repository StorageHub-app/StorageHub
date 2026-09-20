using System.Security.Cryptography;

namespace StorageHub.Desktop.Updates;

/// <summary>Why a download did not produce a package worth installing.</summary>
internal enum UpdateDownloadFailure
{
    None = 0,
    Unreachable,
    WrongSize,
    WrongDigest,
    Cancelled,
}

/// <summary>The outcome of fetching a package.</summary>
internal sealed record UpdateDownloadResult(
    bool Succeeded,
    string? Path,
    UpdateDownloadFailure Failure,
    string? Message)
{
    internal static UpdateDownloadResult Ok(string path) =>
        new(true, path, UpdateDownloadFailure.None, null);

    internal static UpdateDownloadResult Failed(UpdateDownloadFailure failure, string message) =>
        new(false, null, failure, message);
}

/// <summary>
/// Fetches a release package and proves it is the one the feed described.
/// </summary>
/// <remarks>
/// This is the security-critical half of updating. What it downloads is handed to msiexec or to the
/// system package manager, both of which run with administrative rights, so a package that is not
/// exactly what the feed named is a package that must never reach them.
///
/// Three things are checked, and all three are refusals rather than warnings: the URL is HTTPS,
/// which <see cref="UpdateManifest.Validate"/> has already established; the length matches what was
/// claimed, which stops a download being truncated into something else; and the SHA-256 matches. A
/// file that fails any of them is deleted rather than left on disk, because a rejected installer
/// sitting in a temp directory is an invitation to run it by hand.
/// </remarks>
internal static class UpdateDownload
{
    /// <summary>
    /// Downloads to a private file and verifies it.
    /// </summary>
    /// <param name="progress">Percentage complete, so a guided update can show it.</param>
    internal static async Task<UpdateDownloadResult> FetchAsync(
        HttpClient client,
        UpdatePackage package,
        string directory,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileNameFor(package));

        try
        {
            using var response = await client
                .GetAsync(package.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return UpdateDownloadResult.Failed(
                    UpdateDownloadFailure.Unreachable,
                    $"The update could not be downloaded ({(int)response.StatusCode}).");
            }

            // Refused before a byte is written when the server admits to the wrong size, so a
            // mismatched package costs one request rather than a full download.
            var declared = response.Content.Headers.ContentLength;
            if (declared is { } length && length != package.SizeBytes)
            {
                return UpdateDownloadResult.Failed(
                    UpdateDownloadFailure.WrongSize,
                    "The update download is not the size the update feed described.");
            }

            var digest = await WriteAndHashAsync(response, path, package, progress, cancellationToken)
                .ConfigureAwait(false);

            if (digest is null)
            {
                Delete(path);
                return UpdateDownloadResult.Failed(
                    UpdateDownloadFailure.WrongSize,
                    "The update download ended early and is incomplete.");
            }

            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(digest),
                    Convert.FromHexString(package.Sha256)))
            {
                Delete(path);
                return UpdateDownloadResult.Failed(
                    UpdateDownloadFailure.WrongDigest,
                    "The update download does not match its checksum and was discarded.");
            }

            return UpdateDownloadResult.Ok(path);
        }
        catch (OperationCanceledException)
        {
            Delete(path);
            return UpdateDownloadResult.Failed(UpdateDownloadFailure.Cancelled, "The update was cancelled.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            Delete(path);
            return UpdateDownloadResult.Failed(
                UpdateDownloadFailure.Unreachable,
                "The update could not be downloaded.");
        }
    }

    /// <summary>Streams the body to disk, hashing as it goes. Null when the length is wrong.</summary>
    private static async Task<string?> WriteAndHashAsync(
        HttpResponseMessage response,
        string path,
        UpdatePackage package,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        using var hash = SHA256.Create();
        var written = 0L;
        var reported = -1;

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var destination = new FileStream(path, PrivateFile()))
        {
            var buffer = new byte[81920];
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;

                written += read;
                if (written > package.SizeBytes) return null;

                hash.TransformBlock(buffer, 0, read, null, 0);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);

                var percent = (int)(written * 100 / package.SizeBytes);
                if (progress is not null && percent != reported)
                {
                    reported = percent;
                    progress.Report(percent);
                }
            }
        }

        if (written != package.SizeBytes) return null;

        hash.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(hash.Hash!).ToLowerInvariant();
    }

    /// <summary>
    /// Creates the file readable and writable only by this account.
    /// </summary>
    /// <remarks>
    /// The package is about to be handed to an installer running with administrative rights, so
    /// nobody else gets to write to it between here and there. The mode is set at creation rather
    /// than afterwards, which leaves no window in which the file exists with wider permissions.
    /// Windows inherits the temp directory's ACL, which is already per-user, and rejects the
    /// property outright.
    /// </remarks>
    private static FileStreamOptions PrivateFile()
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous,
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return options;
    }

    /// <summary>The package's own name, taken from the feed rather than from the server's reply.</summary>
    /// <remarks>
    /// A filename from a redirect or a Content-Disposition header is attacker-controlled, and a
    /// path separator in one would write outside the directory. This keeps the last segment of the
    /// declared URL and strips anything that is not a plain name.
    /// </remarks>
    private static string FileNameFor(UpdatePackage package)
    {
        var suffix = package.Kind == UpdatePackageKind.Msi ? ".msi" : ".deb";
        var candidate = Path.GetFileName(new Uri(package.Url).AbsolutePath);

        if (string.IsNullOrWhiteSpace(candidate) ||
            candidate.Any(character => Path.GetInvalidFileNameChars().Contains(character)))
        {
            return "storagehub-update" + suffix;
        }

        return candidate.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? candidate
            : candidate + suffix;
    }

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort. A file that cannot be removed is one the installer will refuse anyway,
            // because it never matched its digest.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
