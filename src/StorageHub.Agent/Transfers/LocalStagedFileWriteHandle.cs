using System.Collections.Concurrent;
using StorageHub.Contracts.Ipc;
using StorageHub.Contracts.Results;
using StorageHub.Domain.Storage;
using StorageHub.Storage.Abstractions;
using StorageHub.Storage.Models;

namespace StorageHub.Agent.Transfers;

/// <summary>
/// A local file written beside its destination, under a reserved name, and published by a rename.
/// </summary>
/// <remarks>
/// <para>
/// The two local destinations used to write straight into the file they were making and delete
/// it again on failure, so an interrupted download left nothing to continue from and a file that
/// was there before could not be replaced without first being destroyed. Writing to
/// <c>.storagehub-{key}.partial</c> in the same folder and renaming it into place gives both: the
/// destination is either untouched or complete, never half-written, and the partial file is what
/// a resumed transfer continues.
/// </para>
/// <para>
/// A write with a resume key names its partial file after the key, so the next write with the
/// same key finds it and reports its length as <see cref="AcceptedOffset"/>; a write without one
/// gets a random name and leaves nothing behind. Create-new publishes with a rename that refuses
/// an existing destination, so it stays atomic; overwrite renames over it, after the transfer
/// executor has checked that the destination is still the one somebody agreed to replace.
/// </para>
/// <para>
/// Only the agent writes these files, and it may run two transfers to the same place at once, so
/// a partial file being written is held for this process: a second write with the same key is
/// refused as transient rather than appending into the first one's bytes.
/// </para>
/// </remarks>
internal sealed class LocalStagedFileWriteHandle : IResumableStorageWriteHandle
{
    internal const string PartialPrefix = ".storagehub-";
    internal const string PartialSuffix = ".partial";
    private static readonly ConcurrentDictionary<string, byte> PartialsInUse = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private readonly string _finalPath;
    private readonly string _partialPath;
    private readonly long? _expectedLength;
    private readonly bool _overwrite;
    private readonly bool _resumable;
    private readonly string _codePrefix;
    private FileStream? _stream;
    private bool _released;

    private LocalStagedFileWriteHandle(
        StorageAddress destination,
        string finalPath,
        string partialPath,
        StorageWriteRequest request,
        string codePrefix,
        FileStream stream)
    {
        Destination = destination;
        _finalPath = finalPath;
        _partialPath = partialPath;
        _expectedLength = request.ExpectedLength;
        _overwrite = request.Mode == StorageWriteMode.Overwrite;
        _resumable = request.ResumeKey is not null;
        _codePrefix = codePrefix;
        _stream = stream;
        AcceptedOffset = stream.Length;
        ResumeToken = _resumable ? Path.GetFileName(partialPath) : null;
    }

    public StorageAddress Destination { get; }

    public Stream Content => _stream ?? throw new ObjectDisposedException(nameof(LocalStagedFileWriteHandle));

    public long AcceptedOffset { get; }

    public string? ResumeToken { get; }

    public StorageWriteHandleState State { get; private set; } = StorageWriteHandleState.Open;

    /// <summary>Whether a file name is one of these partial files, which nobody else should touch.</summary>
    internal static bool IsPartialName(string name) =>
        name.StartsWith(PartialPrefix, StringComparison.OrdinalIgnoreCase) &&
        name.EndsWith(PartialSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Opens the write. The caller has already resolved <paramref name="finalPath"/> inside its
    /// approved root and created the folder it goes in.
    /// </summary>
    internal static StorageResult<IStorageWriteHandle> Open(
        StorageAddress destination,
        string finalPath,
        StorageWriteRequest request,
        string codePrefix)
    {
        var folder = Path.GetDirectoryName(finalPath)!;
        var name = PartialPrefix + (request.ResumeKey ?? Guid.NewGuid().ToString("N")) + PartialSuffix;
        var partialPath = Path.Combine(folder, name);
        if (request.Mode == StorageWriteMode.CreateNew && File.Exists(finalPath))
        {
            return Fail(
                codePrefix + ".exists",
                StorageFailureKind.Conflict,
                "A file with that name already exists in the local folder.");
        }

        if (!PartialsInUse.TryAdd(partialPath, 0))
        {
            return Fail(
                codePrefix + ".busy",
                StorageFailureKind.Unavailable,
                "Another transfer is writing the same file.",
                transient: true);
        }

        try
        {
            var stream = request.ResumeKey is null
                ? CreatePartial(partialPath)
                : OpenResumablePartial(partialPath);
            return StorageResult<IStorageWriteHandle>.Success(new LocalStagedFileWriteHandle(
                destination, finalPath, partialPath, request, codePrefix, stream));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _ = PartialsInUse.TryRemove(partialPath, out _);
            return Fail(
                codePrefix + ".open",
                StorageFailureKind.Unavailable,
                "The local file could not be created.",
                transient: true);
        }
    }

    public ValueTask<StorageResult<Stream>> OpenAcceptedContentAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Stream stream = new FileStream(
                _partialPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return ValueTask.FromResult(StorageResult<Stream>.Success(stream));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return ValueTask.FromResult(StorageResult<Stream>.Fail(new StorageFailure(
                _codePrefix + ".resume-read",
                StorageFailureKind.Unavailable,
                "The bytes staged by an earlier attempt could not be read back.",
                isTransient: true)));
        }
    }

    public async ValueTask<StorageResult<StorageEntry>> CommitAsync(CancellationToken cancellationToken = default)
    {
        if (State != StorageWriteHandleState.Open)
        {
            return StorageResult<StorageEntry>.Fail(InvalidState());
        }

        State = StorageWriteHandleState.Committing;
        try
        {
            await _stream!.FlushAsync(cancellationToken).ConfigureAwait(false);
            await _stream.DisposeAsync().ConfigureAwait(false);
            _stream = null;
            if (_expectedLength is { } length && new FileInfo(_partialPath).Length != length)
            {
                State = StorageWriteHandleState.Faulted;
                TryDeletePartial();
                return StorageResult<StorageEntry>.Fail(new StorageFailure(
                    _codePrefix + ".commit",
                    StorageFailureKind.Integrity,
                    "The local file length did not match the transfer plan."));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            State = StorageWriteHandleState.Faulted;
            TryDeletePartial();
            return StorageResult<StorageEntry>.Fail(new StorageFailure(
                _codePrefix + ".commit",
                StorageFailureKind.Unavailable,
                "The local file could not be committed.",
                isTransient: true));
        }

        try
        {
            File.Move(_partialPath, _finalPath, overwrite: _overwrite);
        }
        catch (IOException) when (!_overwrite && File.Exists(_finalPath))
        {
            State = StorageWriteHandleState.Faulted;
            TryDeletePartial();
            return StorageResult<StorageEntry>.Fail(new StorageFailure(
                _codePrefix + ".exists",
                StorageFailureKind.Conflict,
                "A file with that name appeared in the local folder while it was being written."));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The bytes are complete and verified by length; a resumable write keeps them, so the
            // retry only has to publish.
            State = StorageWriteHandleState.Faulted;
            if (!_resumable)
            {
                TryDeletePartial();
            }

            Release();
            return StorageResult<StorageEntry>.Fail(new StorageFailure(
                _codePrefix + ".publish",
                StorageFailureKind.Unavailable,
                "The local file could not be put in place.",
                isTransient: true));
        }

        Release();
        State = StorageWriteHandleState.Committed;
        var file = new FileInfo(_finalPath);
        return StorageEntry.Create(
            WithIdentity(Destination, file),
            StorageEntryKind.File,
            file.Length,
            file.LastWriteTimeUtc);
    }

    public async ValueTask<StorageResult> AbortAsync(CancellationToken cancellationToken = default)
    {
        if (State is StorageWriteHandleState.Committed or StorageWriteHandleState.Aborted)
        {
            return StorageResult.Success();
        }

        if (State != StorageWriteHandleState.Open)
        {
            return StorageResult.Fail(InvalidState());
        }

        State = StorageWriteHandleState.Aborting;
        await CloseStreamAsync().ConfigureAwait(false);
        TryDeletePartial();
        State = StorageWriteHandleState.Aborted;
        return StorageResult.Success();
    }

    public async ValueTask<StorageResult> SuspendAsync(CancellationToken cancellationToken = default)
    {
        if (State != StorageWriteHandleState.Open)
        {
            return StorageResult.Fail(InvalidState());
        }

        if (!_resumable)
        {
            return await AbortAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await _stream!.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Whatever reached the file is still a prefix of the source; the next attempt reads
            // its length rather than trusting a count.
        }

        await CloseStreamAsync().ConfigureAwait(false);
        Release();
        State = StorageWriteHandleState.Suspended;
        return StorageResult.Success();
    }

    public async ValueTask DisposeAsync()
    {
        if (State == StorageWriteHandleState.Open)
        {
            _ = await AbortAsync().ConfigureAwait(false);
        }

        await CloseStreamAsync().ConfigureAwait(false);
        Release();
    }

    /// <summary>
    /// A local file's address with the identity a transfer can hold it to: its modification time
    /// and length, as the desktop computes them for the same file.
    /// </summary>
    internal static StorageAddress WithIdentity(StorageAddress address, FileInfo file)
    {
        var identified = StorageAddress.Create(
            address.ProfileId,
            address.RootIdentity,
            address.CanonicalRelativePath,
            address.NativeItemId,
            versionId: null,
            entityTag: LocalTransferFolder.FileIdentity(file.LastWriteTimeUtc, file.Length));
        return identified.IsSuccess ? identified.Value : address;
    }

    private static FileStream CreatePartial(string partialPath) => new(
        partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 128 * 1024,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static FileStream OpenResumablePartial(string partialPath)
    {
        var existing = new FileInfo(partialPath);
        if (existing.Exists && existing.LinkTarget is not null)
        {
            // Never written through: a link here was not made by StorageHub.
            existing.Delete();
            return CreatePartial(partialPath);
        }

        if (!existing.Exists)
        {
            return CreatePartial(partialPath);
        }

        var stream = new FileStream(
            partialPath, FileMode.Open, FileAccess.Write, FileShare.Read, 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        stream.Seek(0, SeekOrigin.End);
        return stream;
    }

    private async ValueTask CloseStreamAsync()
    {
        if (_stream is null)
        {
            return;
        }

        try
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The partial file's own length is what the next attempt believes.
        }

        _stream = null;
    }

    private void TryDeletePartial()
    {
        try
        {
            if (File.Exists(_partialPath))
            {
                File.Delete(_partialPath);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A partial file left behind is never published; the maintenance clean-up finds it.
        }

        Release();
    }

    private void Release()
    {
        if (!_released)
        {
            _released = true;
            _ = PartialsInUse.TryRemove(_partialPath, out _);
        }
    }

    private StorageFailure InvalidState() => new(
        _codePrefix + ".state",
        StorageFailureKind.Conflict,
        "The local write is no longer open.");

    private static StorageResult<IStorageWriteHandle> Fail(
        string code,
        StorageFailureKind kind,
        string message,
        bool transient = false) =>
        StorageResult<IStorageWriteHandle>.Fail(new StorageFailure(code, kind, message, transient));
}
