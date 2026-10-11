using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using StorageHub.Contracts.Results;
using StorageHub.Domain.Capabilities;
using StorageHub.Domain.Identifiers;
using StorageHub.Domain.Storage;
using StorageHub.Storage.Abstractions;
using StorageHub.Storage.Models;

namespace StorageHub.Agent.Transfers;

/// <summary>
/// A read-only view of a folder on this PC that can be listed and checksummed, so a This PC pane
/// can be one side of a folder comparison.
/// </summary>
/// <remarks>
/// <para>
/// The transfer endpoint for the same folders (<see cref="LocalUserPathTransferEndpoint"/>)
/// deliberately does not list: a transfer names its files, and the worker has no business walking
/// a folder the desktop nominated. A comparison does have to walk it, so walking lives here, on its
/// own, with nothing that writes. The folder is approved by the same gate a transfer's is -- a
/// comparison can see no more than a copy from it could read -- and it is approved again on every
/// open, because the address may have been built before the folder was replaced with a link.
/// </para>
/// <para>
/// A link is listed as a link and never followed, and a path that resolves outside the approved
/// folder is refused, as the transfer endpoint refuses one. A file's address carries the same
/// time-and-length identity the transfer endpoint gives it, so a difference found here can be
/// copied over under the same check a paste gets.
/// </para>
/// </remarks>
public static class LocalUserPathScanEndpoint
{
    /// <summary>Opens the folder an address names for listing, re-approving it first.</summary>
    public static StorageResult<IStorageEndpointSession> Open(StorageAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!LocalUserPathTransferEndpoint.TryDecodeRoot(address, out var root))
        {
            return Fail<IStorageEndpointSession>("local-scan.invalid", "The local folder address is invalid.");
        }

        var approved = LocalUserPathTransferEndpoint.ApproveRoot(root);
        if (approved.IsFailure)
        {
            return StorageResult<IStorageEndpointSession>.Fail(approved.Error);
        }

        return StorageResult<IStorageEndpointSession>.Success(
            new Session(address.ProfileId, approved.Value, address.RootIdentity));
    }

    private static StorageResult<T> Fail<T>(
        string code,
        string message,
        StorageFailureKind kind = StorageFailureKind.Validation,
        bool transient = false) =>
        StorageResult<T>.Fail(new StorageFailure(code, kind, message, transient));

    private sealed class Session(ConnectionProfileId profileId, string root, string rootIdentity)
        : IStorageEndpointSession, IStoragePortableChecksumSession
    {
        public ConnectionProfileId ProfileId { get; } = profileId;

        public string RootIdentity { get; } = rootIdentity;

        public EffectiveStorageCapabilities Capabilities { get; } = new(
            [
                new KeyValuePair<StorageFeature, FeatureSupport>(StorageFeature.List, FeatureSupport.Native()),
                new KeyValuePair<StorageFeature, FeatureSupport>(StorageFeature.PaginatedList, FeatureSupport.Native()),
                new KeyValuePair<StorageFeature, FeatureSupport>(StorageFeature.ReadStream, FeatureSupport.Native())
            ],
            LocalPathPolicy.Current.PathComparison == StringComparison.Ordinal
                ? StorageCaseSensitivity.Sensitive
                : StorageCaseSensitivity.Insensitive);

        public ValueTask<StorageResult> CheckHealthAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Directory.Exists(root)
                ? StorageResult.Success()
                : StorageResult.Fail(new StorageFailure(
                    "local-scan.unavailable", StorageFailureKind.NotFound, "The local folder no longer exists.")));

        public ValueTask<StorageResult<StorageEntry>> GetEntryAsync(
            StorageAddress address,
            CancellationToken cancellationToken = default)
        {
            var resolved = Resolve(address);
            if (resolved.IsFailure)
            {
                return ValueTask.FromResult(StorageResult<StorageEntry>.Fail(resolved.Error));
            }

            var info = new FileInfo(resolved.Value);
            if (info.Exists)
            {
                return ValueTask.FromResult(Describe(address, info));
            }

            var folder = new DirectoryInfo(resolved.Value);
            return ValueTask.FromResult(folder.Exists
                ? Describe(address, folder)
                : Fail<StorageEntry>("local-scan.not-found", "The local item does not exist.", StorageFailureKind.NotFound));
        }

        /// <summary>
        /// One folder's children, a page at a time. The continuation token is the index of the
        /// next child in name order, which is stable for a folder nobody is changing and merely
        /// skips or repeats an entry for one that is; the scanner notices a repeat.
        /// </summary>
        public ValueTask<StorageResult<StoragePage>> ListAsync(
            StorageAddress address,
            StorageListRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            request ??= new StorageListRequest();
            var validation = request.Validate();
            if (validation.IsFailure)
            {
                return ValueTask.FromResult(StorageResult<StoragePage>.Fail(validation.Error));
            }

            if (request.Recursive)
            {
                return ValueTask.FromResult(Fail<StoragePage>(
                    "local-scan.recursive", "A local folder is listed one level at a time.", StorageFailureKind.Unsupported));
            }

            var resolved = address.IsRoot ? StorageResult<string>.Success(root) : Resolve(address);
            if (resolved.IsFailure)
            {
                return ValueTask.FromResult(StorageResult<StoragePage>.Fail(resolved.Error));
            }

            var offset = 0;
            if (request.ContinuationToken is { } token &&
                (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out offset) || offset < 0))
            {
                return ValueTask.FromResult(Fail<StoragePage>("local-scan.token", "The listing continuation is invalid."));
            }

            try
            {
                var folder = new DirectoryInfo(resolved.Value);
                if (!folder.Exists)
                {
                    return ValueTask.FromResult(Fail<StoragePage>(
                        "local-scan.not-found", "The local folder does not exist.", StorageFailureKind.NotFound));
                }

                if (!address.IsRoot && folder.LinkTarget is not null)
                {
                    return ValueTask.FromResult(Fail<StoragePage>(
                        "local-scan.reparse-point", "The local folder is a link or reparse point."));
                }

                var children = folder.EnumerateFileSystemInfos()
                    .OrderBy(static child => child.Name, StringComparer.Ordinal)
                    .ToArray();
                var entries = new List<StorageEntry>(Math.Min(request.PageSize, Math.Max(0, children.Length - offset)));
                foreach (var child in children.Skip(offset).Take(request.PageSize))
                {
                    var childAddress = address.Append(child.Name);
                    if (childAddress.IsFailure)
                    {
                        // A name no address can hold -- a control character, say -- is left out
                        // rather than failing the whole folder.
                        continue;
                    }

                    var described = Describe(childAddress.Value, child);
                    if (described.IsSuccess)
                    {
                        entries.Add(described.Value);
                    }
                }

                var next = offset + request.PageSize < children.Length
                    ? (offset + request.PageSize).ToString(CultureInfo.InvariantCulture)
                    : null;
                return ValueTask.FromResult(StorageResult<StoragePage>.Success(new StoragePage(entries, next)));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
            {
                return ValueTask.FromResult(Fail<StoragePage>(
                    "local-scan.list", "The local folder could not be read.", StorageFailureKind.Unavailable, transient: true));
            }
        }

        /// <summary>Reads the file to its end and hashes it, refusing one that changed size since it was listed.</summary>
        public async ValueTask<StorageResult<PortableChecksumResult>> ComputePortableChecksumAsync(
            PortableChecksumRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            var validation = request.Validate();
            if (validation.IsFailure)
            {
                return StorageResult<PortableChecksumResult>.Fail(validation.Error);
            }

            var resolved = Resolve(request.ExpectedEntry.Address);
            if (resolved.IsFailure)
            {
                return StorageResult<PortableChecksumResult>.Fail(resolved.Error);
            }

            try
            {
                var info = new FileInfo(resolved.Value);
                if (!info.Exists || info.LinkTarget is not null || info.Length != request.ExpectedEntry.Size)
                {
                    return Fail<PortableChecksumResult>(
                        "local-scan.changed", "The local file changed after it was listed.", StorageFailureKind.Conflict);
                }

                await using var stream = new FileStream(
                    resolved.Value, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
                return StorageResult<PortableChecksumResult>.Success(new PortableChecksumResult(
                    new PortableContentDigest(PortableChecksumAlgorithm.Sha256, Convert.ToHexStringLower(hash)),
                    stream.Length));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
            {
                return Fail<PortableChecksumResult>(
                    "local-scan.read", "The local file could not be read.", StorageFailureKind.Unavailable, transient: true);
            }
        }

        public ValueTask<StorageResult<Stream>> OpenReadAsync(
            StorageReadRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ReadOnly<Stream>());

        public ValueTask<StorageResult<IStorageWriteHandle>> OpenWriteAsync(
            StorageWriteRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ReadOnly<IStorageWriteHandle>());

        public ValueTask<StorageResult<StorageEntry>> CreateDirectoryAsync(
            StorageAddress address,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ReadOnly<StorageEntry>());

        public ValueTask<StorageResult> DeleteAsync(
            StorageDeleteRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(StorageResult.Fail(ReadOnlyFailure));

        public ValueTask<StorageResult<StorageEntry>> CopyAsync(
            StorageCopyRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ReadOnly<StorageEntry>());

        public ValueTask<StorageResult<StorageEntry>> MoveAsync(
            StorageMoveRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ReadOnly<StorageEntry>());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static readonly StorageFailure ReadOnlyFailure = new(
            "local-scan.read-only", StorageFailureKind.Unsupported, "A folder opened for comparison is only read.");

        private static StorageResult<T> ReadOnly<T>() => StorageResult<T>.Fail(ReadOnlyFailure);

        private static StorageResult<StorageEntry> Describe(StorageAddress address, FileSystemInfo item)
        {
            if (item.LinkTarget is not null || (item.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return StorageEntry.Create(address, StorageEntryKind.SymbolicLink, lastModifiedUtc: item.LastWriteTimeUtc);
            }

            return item is FileInfo file
                ? StorageEntry.Create(
                    LocalStagedFileWriteHandle.WithIdentity(address, file),
                    StorageEntryKind.File,
                    file.Length,
                    file.LastWriteTimeUtc)
                : StorageEntry.Create(address, StorageEntryKind.Directory, lastModifiedUtc: item.LastWriteTimeUtc);
        }

        private StorageResult<string> Resolve(StorageAddress address)
        {
            if (address.ProfileId != ProfileId ||
                !string.Equals(address.RootIdentity, RootIdentity, StringComparison.Ordinal))
            {
                return Fail<string>("local-scan.address", "The local address is outside its approved folder.");
            }

            try
            {
                var candidate = Path.GetFullPath(Path.Combine(
                    root, address.CanonicalRelativePath.Replace('/', Path.DirectorySeparatorChar)));
                var prefix = LocalPathPolicy.ContainmentPrefix(root);
                return candidate.StartsWith(prefix, LocalPathPolicy.Current.PathComparison)
                    ? StorageResult<string>.Success(candidate)
                    : Fail<string>("local-scan.escape", "The local path escapes its approved folder.");
            }
            catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException)
            {
                return Fail<string>("local-scan.address", "The local path is invalid.");
            }
        }
    }
}
