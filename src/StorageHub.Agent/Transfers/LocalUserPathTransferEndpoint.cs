using System.Security;
using StorageHub.Contracts.Ipc;
using StorageHub.Contracts.Results;
using StorageHub.Domain.Capabilities;
using StorageHub.Domain.Identifiers;
using StorageHub.Domain.Storage;
using StorageHub.Storage.Abstractions;
using StorageHub.Storage.Models;

namespace StorageHub.Agent.Transfers;

/// <summary>
/// A read/write endpoint for a local folder the desktop nominated, so a This PC pane can be one
/// side of a queued transfer.
///
/// Every other local endpoint addresses a path the agent itself produced: the Explorer staging
/// root, or a source a shell drop already approved. This one takes a path chosen in the UI, which
/// makes it the only place where a filesystem destination crosses the IPC boundary inward. The
/// desktop's word is therefore not evidence of anything: the root is re-validated on every open,
/// and every file resolved through it is re-checked for containment, because a directory that
/// passed validation can be replaced with a link while a transfer is still running.
/// </summary>
public static class LocalUserPathTransferEndpoint
{
    public static bool IsUserPath(StorageAddress address) =>
        LocalTransferFolder.IsLocalFolder(address.RootIdentity);

    /// <summary>Builds a durable address for a local folder, rejecting anything unsuitable.</summary>
    public static StorageResult<StorageAddress> CreateAddress(string root, string relativePath)
    {
        var approved = ApproveRoot(root);
        if (approved.IsFailure)
        {
            return StorageResult<StorageAddress>.Fail(approved.Error);
        }

        return StorageAddress.Create(
            new ConnectionProfileId(LocalTransferFolder.CreateConnectionId(approved.Value)),
            LocalTransferFolder.CreateRootIdentity(approved.Value),
            relativePath);
    }

    /// <summary>
    /// Validates a local address at enqueue time, so a rejected folder is reported while the user
    /// is still looking at the dialog rather than as a failed job later. Returns null when the
    /// address is acceptable.
    /// </summary>
    public static StorageFailure? Approve(StorageAddress address)
    {
        if (!TryDecodeRoot(address, out var root))
        {
            return new StorageFailure(
                "local-user.invalid", StorageFailureKind.Validation, "The local folder address is invalid.");
        }

        var approved = ApproveRoot(root);
        return approved.IsFailure ? approved.Error : null;
    }

    public static StorageResult<ITransferEndpointConnection> Open(StorageAddress address)
    {
        if (!TryDecodeRoot(address, out var root))
        {
            return Fail<ITransferEndpointConnection>("local-user.invalid", "The local folder address is invalid.");
        }

        // Re-approved rather than trusted: the address was built earlier, possibly before a
        // restart, and the folder may since have been replaced, removed, or turned into a link.
        var approved = ApproveRoot(root);
        if (approved.IsFailure)
        {
            return StorageResult<ITransferEndpointConnection>.Fail(approved.Error);
        }

        return StorageResult<ITransferEndpointConnection>.Success(
            new Connection(new Session(address.ProfileId, approved.Value, address.RootIdentity)));
    }

    /// <summary>
    /// The single gate for a UI-supplied path. Rejects anything that is not a plain, current-user
    /// writable directory, and in particular anything that would let a transfer reach StorageHub's
    /// own durable state or the operating system.
    /// </summary>
    internal static StorageResult<string> ApproveRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return Fail<string>("local-user.empty", "A local transfer folder is required.");
        }

        string canonical;
        try
        {
            // Extended-length and device paths bypass the normalisation the rest of this method
            // depends on, so they are refused rather than canonicalised.
            if (root.StartsWith(@"\\?\", StringComparison.Ordinal) ||
                root.StartsWith(@"\\.\", StringComparison.Ordinal))
            {
                return Fail<string>("local-user.device-path", "Device and extended-length paths cannot be used for transfers.");
            }

            if (!Path.IsPathFullyQualified(root))
            {
                return Fail<string>("local-user.relative", "The local transfer folder must be a full path.");
            }

            canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return Fail<string>("local-user.invalid", "The local transfer folder path is not valid.");
        }

        try
        {
            var directory = new DirectoryInfo(canonical);
            if (!directory.Exists)
            {
                return Fail<string>("local-user.not-found", "The local transfer folder no longer exists.");
            }

            if (directory.LinkTarget is not null)
            {
                return Fail<string>("local-user.reparse-point", "The local transfer folder is a link or reparse point.");
            }

            if (IsProtected(canonical, out var reason))
            {
                return Fail<string>("local-user.protected", reason);
            }

            if (!IsWritable(canonical))
            {
                return Fail<string>(
                    "local-user.read-only",
                    $"The local transfer folder is not writable by {LocalPathPolicy.Current.AccountDescription}.");
            }

            return StorageResult<string>.Success(canonical);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            return Fail<string>("local-user.unavailable", "The local transfer folder could not be opened.");
        }
    }

    /// <summary>
    /// Locations a transfer must never write into: StorageHub's own data tree, which holds the
    /// vault and the durable queue, and the operating system's own directories.
    /// </summary>
    private static bool IsProtected(string canonical, out string reason)
    {
        var comparison = LocalPathPolicy.Current.PathComparison;
        foreach (var (folder, message) in LocalPathPolicy.Current.ProtectedRoots())
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                continue;
            }

            var guarded = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            if (string.Equals(canonical, guarded, comparison) ||
                canonical.StartsWith(guarded + Path.DirectorySeparatorChar, comparison))
            {
                reason = message;
                return true;
            }
        }

        reason = string.Empty;
        return false;
    }

    /// <summary>
    /// Confirms the current user can create files here, by creating one. An ACL inspection would
    /// have to model inheritance, group membership, and deny rules to reach the same answer.
    /// </summary>
    private static bool IsWritable(string canonical)
    {
        var probe = Path.Combine(canonical, $".storagehub-write-{Guid.NewGuid():N}.tmp");
        try
        {
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
                return true;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(probe))
                {
                    File.Delete(probe);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // DeleteOnClose already covers the normal path; a leftover probe is harmless.
            }
        }
    }

    internal static bool TryDecodeRoot(StorageAddress address, out string root)
    {
        root = string.Empty;
        if (LocalTransferFolder.TryReadFolder(address.RootIdentity) is not { } folder)
        {
            return false;
        }

        try
        {
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

            // The id has to match the one the folder derives, so a caller cannot pair an approved
            // folder's identity with some other connection's id.
            return address.ProfileId == new ConnectionProfileId(LocalTransferFolder.CreateConnectionId(root));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return false;
        }
    }

    private static StorageResult<T> Fail<T>(string code, string message, bool transient = false) =>
        StorageResult<T>.Fail(new StorageFailure(code, StorageFailureKind.Validation, message, transient));

    private sealed class Connection(Session session) : ITransferEndpointConnection
    {
        public IStorageEndpointSession Session { get; } = session;
        public ValueTask DisposeAsync() => session.DisposeAsync();
    }

    private sealed class Session(ConnectionProfileId profileId, string root, string rootIdentity) : IStorageEndpointSession
    {
        public ConnectionProfileId ProfileId { get; } = profileId;
        public string RootIdentity { get; } = rootIdentity;

        // A read can start at an offset, so a download can be resumed from here, and a write is
        // staged beside its destination, so one into here can be.
        public EffectiveStorageCapabilities Capabilities { get; } = new([
            new KeyValuePair<StorageFeature, FeatureSupport>(StorageFeature.ReadStream, FeatureSupport.Native()),
            new KeyValuePair<StorageFeature, FeatureSupport>(StorageFeature.WriteStream, FeatureSupport.Native()),
            new KeyValuePair<StorageFeature, FeatureSupport>(StorageFeature.ConditionalCreate, FeatureSupport.Native()),
            new KeyValuePair<StorageFeature, FeatureSupport>(StorageFeature.ResumeDownload, FeatureSupport.Native()),
            new KeyValuePair<StorageFeature, FeatureSupport>(StorageFeature.ResumeUpload, FeatureSupport.Native(
                "A partial file beside the destination is continued and renamed into place."))]);

        public ValueTask<StorageResult> CheckHealthAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Directory.Exists(root)
                ? StorageResult.Success()
                : StorageResult.Fail(new StorageFailure(
                    "local-user.unavailable",
                    StorageFailureKind.NotFound,
                    "The local transfer folder no longer exists.")));

        public ValueTask<StorageResult<StorageEntry>> GetEntryAsync(StorageAddress address, CancellationToken cancellationToken = default)
        {
            var resolved = Resolve(address);
            if (resolved.IsFailure)
            {
                return ValueTask.FromResult(StorageResult<StorageEntry>.Fail(resolved.Error));
            }

            var file = new FileInfo(resolved.Value);
            if (!file.Exists)
            {
                return ValueTask.FromResult(StorageResult<StorageEntry>.Fail(new StorageFailure(
                    "local-user.not-found", StorageFailureKind.NotFound, "The local file does not exist.")));
            }

            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return ValueTask.FromResult(Fail<StorageEntry>(
                    "local-user.reparse-point", "The local file is a link or reparse point."));
            }

            return ValueTask.FromResult(StorageEntry.Create(
                LocalStagedFileWriteHandle.WithIdentity(address, file),
                StorageEntryKind.File,
                file.Length,
                file.LastWriteTimeUtc));
        }

        public ValueTask<StorageResult<Stream>> OpenReadAsync(StorageReadRequest request, CancellationToken cancellationToken = default)
        {
            var resolved = Resolve(request.Address);
            if (resolved.IsFailure)
            {
                return ValueTask.FromResult(StorageResult<Stream>.Fail(resolved.Error));
            }

            try
            {
                var file = new FileInfo(resolved.Value);
                if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return ValueTask.FromResult(Fail<Stream>(
                        "local-user.read", "The local file is unavailable or is a link."));
                }

                var stream = new FileStream(
                    resolved.Value, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                // The queue resumes an interrupted transfer by asking for a range, so the offset
                // and length have to be honoured rather than the whole file returned.
                if (request.Offset > stream.Length)
                {
                    stream.Dispose();
                    return ValueTask.FromResult(Fail<Stream>(
                        "local-user.range", "The requested local read range is invalid."));
                }

                stream.Position = request.Offset;
                Stream bounded = request.Length is { } length
                    ? new BoundedLocalReadStream(stream, length)
                    : stream;
                return ValueTask.FromResult(StorageResult<Stream>.Success(bounded));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return ValueTask.FromResult(Fail<Stream>(
                    "local-user.read", "The local file could not be opened.", transient: true));
            }
        }

        public ValueTask<StorageResult<IStorageWriteHandle>> OpenWriteAsync(StorageWriteRequest request, CancellationToken cancellationToken = default)
        {
            var validation = request.Validate();
            if (validation.IsFailure)
            {
                return ValueTask.FromResult(StorageResult<IStorageWriteHandle>.Fail(validation.Error));
            }

            // Create-new, or an overwrite somebody chose when the paste met a file already
            // here. The transfer executor holds the overwrite to the file's identity before it
            // writes, and the new content only replaces the old by a rename once it is complete,
            // so the file is either the one that was there or the whole new one.
            if (request.Mode is not (StorageWriteMode.CreateNew or StorageWriteMode.Overwrite) ||
                request.ExpectedDestinationVersionId is not null ||
                request.ExpectedDestinationEntityTag is not null)
            {
                return ValueTask.FromResult(Fail<IStorageWriteHandle>(
                    "local-user.write-mode",
                    "A local destination creates a file or replaces one; it cannot hold the replacement to a version."));
            }

            var resolved = Resolve(request.Destination);
            if (resolved.IsFailure)
            {
                return ValueTask.FromResult(StorageResult<IStorageWriteHandle>.Fail(resolved.Error));
            }

            try
            {
                var parent = Path.GetDirectoryName(resolved.Value)!;
                var created = CreateContainedDirectory(parent);
                if (created.IsFailure)
                {
                    return ValueTask.FromResult(StorageResult<IStorageWriteHandle>.Fail(created.Error));
                }

                if (Directory.Exists(resolved.Value) ||
                    new FileInfo(resolved.Value) is { Exists: true, LinkTarget: not null })
                {
                    return ValueTask.FromResult(Fail<IStorageWriteHandle>(
                        "local-user.not-a-file", "The local destination is a folder or a link."));
                }

                return ValueTask.FromResult(LocalStagedFileWriteHandle.Open(
                    request.Destination, resolved.Value, request, "local-user"));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return ValueTask.FromResult(Fail<IStorageWriteHandle>(
                    "local-user.open", "The local file could not be created.", transient: true));
            }
        }

        public ValueTask<StorageResult<StorageEntry>> CreateDirectoryAsync(StorageAddress address, CancellationToken cancellationToken = default)
        {
            var resolved = Resolve(address);
            if (resolved.IsFailure)
            {
                return ValueTask.FromResult(StorageResult<StorageEntry>.Fail(resolved.Error));
            }

            var created = CreateContainedDirectory(resolved.Value);
            if (created.IsFailure)
            {
                return ValueTask.FromResult(StorageResult<StorageEntry>.Fail(created.Error));
            }

            return ValueTask.FromResult(StorageEntry.Create(
                address, StorageEntryKind.Directory, null, Directory.GetLastWriteTimeUtc(resolved.Value)));
        }

        /// <summary>
        /// Creates a directory one segment at a time, refusing to follow a link. Creating the
        /// whole chain at once would happily walk through a reparse point planted between the
        /// root's approval and this write.
        /// </summary>
        private StorageResult CreateContainedDirectory(string target)
        {
            try
            {
                var prefix = root + Path.DirectorySeparatorChar;
                if (!string.Equals(target, root, LocalPathPolicy.Current.PathComparison) &&
                    !target.StartsWith(prefix, LocalPathPolicy.Current.PathComparison))
                {
                    return StorageResult.Fail(new StorageFailure(
                        "local-user.escape", StorageFailureKind.Validation,
                        "The local path escapes its approved folder."));
                }

                var relative = string.Equals(target, root, LocalPathPolicy.Current.PathComparison)
                    ? string.Empty
                    : target[prefix.Length..];
                var current = root;
                foreach (var segment in relative.Split(
                    Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
                {
                    current = Path.Combine(current, segment);
                    var directory = new DirectoryInfo(current);
                    if (directory.Exists)
                    {
                        if (directory.LinkTarget is not null)
                        {
                            return StorageResult.Fail(new StorageFailure(
                                "local-user.reparse-point", StorageFailureKind.Validation,
                                "A folder on the local destination path is a link or reparse point."));
                        }

                        continue;
                    }

                    Directory.CreateDirectory(current);
                }

                return StorageResult.Success();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return StorageResult.Fail(new StorageFailure(
                    "local-user.create-directory", StorageFailureKind.Unavailable,
                    "The local folder could not be created.", isTransient: true));
            }
        }

        public ValueTask<StorageResult<StoragePage>> ListAsync(StorageAddress address, StorageListRequest? request = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Fail<StoragePage>(
                "local-user.unsupported", "The transfer worker does not browse local folders."));

        public ValueTask<StorageResult> DeleteAsync(StorageDeleteRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(StorageResult.Fail(new StorageFailure(
                "local-user.unsupported", StorageFailureKind.Unsupported,
                "A queued transfer does not delete from a local folder.")));

        public ValueTask<StorageResult<StorageEntry>> CopyAsync(StorageCopyRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Fail<StorageEntry>(
                "local-user.unsupported", "Server-side copy is unavailable for a local folder."));

        public ValueTask<StorageResult<StorageEntry>> MoveAsync(StorageMoveRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Fail<StorageEntry>(
                "local-user.unsupported", "Server-side move is unavailable for a local folder."));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private StorageResult<string> Resolve(StorageAddress address)
        {
            if (address.ProfileId != ProfileId ||
                !string.Equals(address.RootIdentity, RootIdentity, StringComparison.Ordinal))
            {
                return Fail<string>("local-user.address", "The local address is outside its approved folder.");
            }

            try
            {
                var candidate = Path.GetFullPath(Path.Combine(
                    root, address.CanonicalRelativePath.Replace('/', Path.DirectorySeparatorChar)));
                var prefix = root + Path.DirectorySeparatorChar;
                if (!candidate.StartsWith(prefix, LocalPathPolicy.Current.PathComparison))
                {
                    return Fail<string>("local-user.escape", "The local path escapes its approved folder.");
                }

                return StorageResult<string>.Success(candidate);
            }
            catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException)
            {
                return Fail<string>("local-user.address", "The local path is invalid.");
            }
        }
    }

    /// <summary>Caps a read at the requested length so a resumed range cannot over-read.</summary>
    private sealed class BoundedLocalReadStream(Stream inner, long remaining) : Stream
    {
        private long _remaining = remaining;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_remaining <= 0)
            {
                return 0;
            }

            var slice = buffer[..(int)Math.Min(buffer.Length, _remaining)];
            var read = inner.Read(slice);
            _remaining -= read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_remaining <= 0)
            {
                return 0;
            }

            var slice = buffer[..(int)Math.Min(buffer.Length, _remaining)];
            var read = await inner.ReadAsync(slice, cancellationToken).ConfigureAwait(false);
            _remaining -= read;
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() => inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync().ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
