using System.IO.Pipelines;
using System.Security.Cryptography;
using System.Text;
using CL.Storage.Abstractions;
using CL.Storage.Models;
using StorageHub.Contracts.Results;
using StorageHub.Domain.Identifiers;
using StorageHub.Domain.Storage;
using StorageHub.Storage.Abstractions;
using StorageHub.Storage.Models;

namespace StorageHub.Storage.CodeLogic;

/// <summary>
/// A write staged in a partial object that an interrupted transfer can continue, then moved into
/// place: StorageHub's resume, built on CL.Storage's append rather than its resume token.
/// </summary>
/// <remarks>
/// <para>
/// CL.Storage resumes its own copies, but a StorageHub transfer streams through a pipe the library
/// cannot replay, and the library's resume token describes a part file it names itself. What the
/// library does offer on Local, FTP and SFTP is <c>AppendAsync</c>, which writes in place, so an
/// interrupted append leaves exactly the bytes that reached the server. This handle appends to
/// a partial object whose name is derived from the caller's resume key and the destination, and
/// moves it onto the destination on commit; a later write with the same key asks the server how
/// long the partial object is and continues from there.
/// </para>
/// <para>
/// On the Local provider the partial object lives in the reserved staging namespace, named as the
/// orphan scavenger expects, so one abandoned for a day is removed. Elsewhere it sits beside the
/// destination as <c>.storagehub-{digest}.partial</c>, which is never listed as anything but itself.
/// </para>
/// </remarks>
internal sealed class CodeLogicResumableWriteHandle : IResumableStorageWriteHandle
{
    private const long PauseWriterThreshold = 1024 * 1024;
    private const long ResumeWriterThreshold = 512 * 1024;
    private static readonly TimeSpan SuspendDrainTimeout = TimeSpan.FromSeconds(30);
    private readonly object _gate = new();
    private readonly IStorageService _storage;
    private readonly StorageWriteRequest _request;
    private readonly ConnectionProfileId _profileId;
    private readonly string _rootIdentity;
    private readonly string _partialPath;
    private readonly Pipe _pipe;
    private readonly CancellationTokenSource _abortSource = new();
    private Task<global::CodeLogic.Core.Results.Result<StorageItem>>? _append;
    private long _bytesWritten;
    private StorageWriteHandleState _state = StorageWriteHandleState.Open;

    private CodeLogicResumableWriteHandle(
        IStorageService storage,
        StorageWriteRequest request,
        ConnectionProfileId profileId,
        string rootIdentity,
        string partialPath,
        long acceptedOffset)
    {
        _storage = storage;
        _request = request;
        _profileId = profileId;
        _rootIdentity = rootIdentity;
        _partialPath = partialPath;
        AcceptedOffset = acceptedOffset;
        _pipe = new Pipe(new PipeOptions(
            pauseWriterThreshold: PauseWriterThreshold,
            resumeWriterThreshold: ResumeWriterThreshold,
            useSynchronizationContext: false));
        Content = new StartOnWriteStream(this);
    }

    public StorageAddress Destination => _request.Destination;

    public Stream Content { get; }

    public long AcceptedOffset { get; }

    public string? ResumeToken => _partialPath;

    public StorageWriteHandleState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>The partial object a resume key stages to, for one destination.</summary>
    internal static string PartialPath(StorageProvider provider, string destinationPath, string resumeKey)
    {
        var digest = Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes("storagehub.partial.v1\n" + resumeKey + "\n" + destinationPath)));
        if (provider == StorageProvider.Local)
        {
            return CodeLogicLocalStaging.StagingRootPath + "/" + digest + ".partial";
        }

        var separator = destinationPath.LastIndexOf('/');
        var name = ".storagehub-" + digest + ".partial";
        return separator < 0 ? name : destinationPath[..(separator + 1)] + name;
    }

    /// <summary>
    /// Finds what an earlier write with the same key staged and opens a handle that continues it.
    /// </summary>
    internal static async ValueTask<StorageResult<IStorageWriteHandle>> OpenAsync(
        IStorageService storage,
        StorageWriteRequest request,
        ConnectionProfileId profileId,
        string rootIdentity,
        CancellationToken cancellationToken)
    {
        var partialPath = PartialPath(storage.Provider, request.Destination.CanonicalRelativePath, request.ResumeKey!);
        var existing = await storage.GetInfoAsync(partialPath, cancellationToken).ConfigureAwait(false);
        long accepted = 0;
        if (existing.IsSuccess)
        {
            if (existing.Value!.ItemType != StorageItemType.File || existing.Value.Size is not long size)
            {
                return StorageResult<IStorageWriteHandle>.Fail(new StorageFailure(
                    "storage.write.resume_unusable",
                    StorageFailureKind.Conflict,
                    "Something other than a staged file has the partial object's name."));
            }

            accepted = size;
        }
        else if (CodeLogicStorageMapper.MapFailure(
                     existing.Error,
                     "storage.write.resume_lookup",
                     "The bytes staged by an earlier attempt could not be looked up.") is { Kind: not StorageFailureKind.NotFound } lookupFailure)
        {
            return StorageResult<IStorageWriteHandle>.Fail(lookupFailure);
        }
        else
        {
            var separator = partialPath.LastIndexOf('/');
            if (separator > 0)
            {
                // The append creates the file but not its folder. A folder that is already there
                // is not a failure, and one that cannot be made shows up as the append failing.
                _ = await storage.CreateDirectoryAsync(partialPath[..separator], cancellationToken).ConfigureAwait(false);
            }
        }

        return StorageResult<IStorageWriteHandle>.Success(
            new CodeLogicResumableWriteHandle(storage, request, profileId, rootIdentity, partialPath, accepted));
    }

    public async ValueTask<StorageResult<Stream>> OpenAcceptedContentAsync(CancellationToken cancellationToken = default)
    {
        if (AcceptedOffset == 0)
        {
            return StorageResult<Stream>.Success(new MemoryStream([], writable: false));
        }

        try
        {
            var download = await _storage.DownloadAsync(
                _partialPath,
                new StorageDownloadOptions { Offset = 0, Length = AcceptedOffset },
                cancellationToken).ConfigureAwait(false);
            return download.IsSuccess
                ? StorageResult<Stream>.Success(download.Value!)
                : StorageResult<Stream>.Fail(CodeLogicStorageMapper.MapFailure(
                    download.Error,
                    "storage.write.resume_read",
                    "The bytes staged by an earlier attempt could not be read back."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return StorageResult<Stream>.Fail(CodeLogicStorageMapper.Unexpected("read staged bytes"));
        }
    }

    public async ValueTask<StorageResult<StorageEntry>> CommitAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryTransition(StorageWriteHandleState.Open, StorageWriteHandleState.Committing))
        {
            return StorageResult<StorageEntry>.Fail(InvalidState("commit"));
        }

        var total = AcceptedOffset + Interlocked.Read(ref _bytesWritten);
        if (_request.ExpectedLength is { } expected && expected != total)
        {
            await StopAppendAsync().ConfigureAwait(false);
            await DeletePartialAsync().ConfigureAwait(false);
            SetState(StorageWriteHandleState.Faulted);
            return StorageResult<StorageEntry>.Fail(new StorageFailure(
                "storage.write.length_mismatch",
                StorageFailureKind.Integrity,
                "The supplied content length did not match the declared length."));
        }

        try
        {
            EnsureAppendStarted();
            await _pipe.Writer.CompleteAsync().ConfigureAwait(false);
            var appended = await _append!.ConfigureAwait(false);
            if (appended.IsFailure)
            {
                // What reached the server is still a prefix; the next attempt measures it.
                SetState(StorageWriteHandleState.Faulted);
                return StorageResult<StorageEntry>.Fail(CodeLogicStorageMapper.MapFailure(
                    appended.Error,
                    "storage.write.failed",
                    "The item could not be written."));
            }

            var staged = await _storage.GetInfoAsync(_partialPath, CancellationToken.None).ConfigureAwait(false);
            if (staged.IsFailure || staged.Value!.Size is not long stagedLength ||
                _request.ExpectedLength is { } length && stagedLength != length)
            {
                await DeletePartialAsync().ConfigureAwait(false);
                SetState(StorageWriteHandleState.Faulted);
                return StorageResult<StorageEntry>.Fail(new StorageFailure(
                    "storage.write.length_mismatch",
                    StorageFailureKind.Integrity,
                    "The staged content did not have the declared length, so it was not published."));
            }

            var publish = await _storage.MoveAsync(
                    _partialPath,
                    _request.Destination.CanonicalRelativePath,
                    new StorageTransferOptions
                    {
                        Overwrite = _request.Mode == StorageWriteMode.Overwrite,
                        CreateParents = true,
                        DestinationCondition = _request.ExpectedDestinationVersionId is null &&
                                               _request.ExpectedDestinationEntityTag is null
                            ? null
                            : new StorageMutationCondition
                            {
                                ExpectedETag = _request.ExpectedDestinationEntityTag,
                                ExpectedVersionId = _request.ExpectedDestinationVersionId
                            }
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (publish.IsFailure)
            {
                var failure = CodeLogicStorageMapper.MapFailure(
                    publish.Error,
                    "storage.write.publish_failed",
                    "The completed item could not be published.");
                if (failure.Kind == StorageFailureKind.Conflict)
                {
                    // Something is in the way that nobody agreed to replace: the staged bytes
                    // will never be published, so they are not kept.
                    await DeletePartialAsync().ConfigureAwait(false);
                }

                SetState(StorageWriteHandleState.Faulted);
                return StorageResult<StorageEntry>.Fail(failure);
            }

            var published = await _storage
                .GetInfoAsync(_request.Destination.CanonicalRelativePath, CancellationToken.None)
                .ConfigureAwait(false);
            if (published.IsFailure)
            {
                SetState(StorageWriteHandleState.Faulted);
                return StorageResult<StorageEntry>.Fail(CodeLogicStorageMapper.MapFailure(
                    published.Error,
                    "storage.write.publish_unconfirmed",
                    "The published item could not be read back."));
            }

            var entry = CodeLogicStorageMapper.MapEntry(published.Value!, _profileId, _rootIdentity);
            SetState(entry.IsSuccess ? StorageWriteHandleState.Committed : StorageWriteHandleState.Faulted);
            return entry;
        }
        catch (Exception)
        {
            await StopAppendAsync().ConfigureAwait(false);
            SetState(StorageWriteHandleState.Faulted);
            return StorageResult<StorageEntry>.Fail(new StorageFailure(
                "storage.write.provider_indeterminate",
                StorageFailureKind.Integrity,
                "The provider outcome could not be confirmed."));
        }
    }

    public async ValueTask<StorageResult> AbortAsync(CancellationToken cancellationToken = default)
    {
        if (State == StorageWriteHandleState.Aborted)
        {
            return StorageResult.Success();
        }

        if (!TryTransition(StorageWriteHandleState.Open, StorageWriteHandleState.Aborting))
        {
            return StorageResult.Fail(InvalidState("abort"));
        }

        await StopAppendAsync().ConfigureAwait(false);
        var deleted = await DeletePartialAsync().ConfigureAwait(false);
        SetState(deleted ? StorageWriteHandleState.Aborted : StorageWriteHandleState.Faulted);
        return deleted
            ? StorageResult.Success()
            : StorageResult.Fail(new StorageFailure(
                "storage.write.staging_cleanup_failed",
                StorageFailureKind.Integrity,
                "The staged bytes could not be removed; they are never published."));
    }

    public async ValueTask<StorageResult> SuspendAsync(CancellationToken cancellationToken = default)
    {
        if (!TryTransition(StorageWriteHandleState.Open, StorageWriteHandleState.Suspended))
        {
            return StorageResult.Fail(InvalidState("suspend"));
        }

        // Let what is already in the pipe reach the server: it is the source's next bytes, so it
        // is as good a prefix as anything before it. A server that will not take it in time is
        // cut off, and the next attempt measures what it kept.
        try
        {
            await _pipe.Writer.CompleteAsync().ConfigureAwait(false);
            if (_append is { } append)
            {
                try
                {
                    _ = await append.WaitAsync(SuspendDrainTimeout, CancellationToken.None).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    TryCancelProvider();
                    await ObserveAppendAsync().ConfigureAwait(false);
                }
            }
        }
        catch (Exception)
        {
            TryCancelProvider();
            await ObserveAppendAsync().ConfigureAwait(false);
        }

        return StorageResult.Success();
    }

    public async ValueTask DisposeAsync()
    {
        if (State == StorageWriteHandleState.Open)
        {
            _ = await AbortAsync().ConfigureAwait(false);
        }

        await ObserveAppendAsync().ConfigureAwait(false);
        _abortSource.Dispose();
    }

    private void EnsureAppendStarted()
    {
        lock (_gate)
        {
            _append ??= AppendAndDisposeReaderAsync();
        }
    }

    private async Task<global::CodeLogic.Core.Results.Result<StorageItem>> AppendAndDisposeReaderAsync()
    {
        await Task.Yield();
        var reader = _pipe.Reader.AsStream();
        try
        {
            return await _storage.AppendAsync(_partialPath, reader, _abortSource.Token).ConfigureAwait(false);
        }
        finally
        {
            await reader.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task StopAppendAsync()
    {
        TryCancelProvider();
        try
        {
            await _pipe.Writer.CompleteAsync(new OperationCanceledException("The write was stopped."))
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Already completed.
        }

        await ObserveAppendAsync().ConfigureAwait(false);
    }

    private async Task ObserveAppendAsync()
    {
        if (_append is null)
        {
            return;
        }

        try
        {
            _ = await _append.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The handle's own outcome is already decided; this only lets the provider finish.
        }
    }

    private async Task<bool> DeletePartialAsync()
    {
        try
        {
            var deleted = await _storage.DeleteAsync(
                    _partialPath,
                    new StorageDeleteOptions { IgnoreMissing = true },
                    CancellationToken.None)
                .ConfigureAwait(false);
            return deleted.IsSuccess;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void TryCancelProvider()
    {
        try
        {
            _abortSource.Cancel();
        }
        catch (Exception)
        {
            // Provider cancellation callbacks are outside StorageHub's control.
        }
    }

    private bool TryTransition(StorageWriteHandleState from, StorageWriteHandleState to)
    {
        lock (_gate)
        {
            if (_state != from)
            {
                return false;
            }

            _state = to;
            return true;
        }
    }

    private void SetState(StorageWriteHandleState state)
    {
        lock (_gate)
        {
            _state = state;
        }
    }

    private static StorageFailure InvalidState(string operation) => new(
        "storage.write.invalid_state",
        StorageFailureKind.Conflict,
        $"The write handle cannot {operation} from its current state.");

    /// <summary>
    /// The handle's content: bytes go into the pipe, and the first of them starts the append, so
    /// nothing is sent to the server while the caller is still reading the staged bytes back.
    /// </summary>
    private sealed class StartOnWriteStream(CodeLogicResumableWriteHandle owner) : Stream
    {
        private const int MaximumWriteChunk = 64 * 1024;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => owner.State == StorageWriteHandleState.Open;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(owner.State != StorageWriteHandleState.Open, this);

            owner.EnsureAppendStarted();
            var writer = owner._pipe.Writer;
            while (!buffer.IsEmpty)
            {
                var chunkLength = Math.Min(buffer.Length, MaximumWriteChunk);
                buffer[..chunkLength].CopyTo(writer.GetMemory(chunkLength));
                writer.Advance(chunkLength);
                var flushed = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (flushed.IsCanceled)
                {
                    throw new OperationCanceledException("The write was canceled before the provider accepted it.");
                }

                if (flushed.IsCompleted)
                {
                    throw new IOException("The provider stopped accepting content before the write completed.");
                }

                _ = Interlocked.Add(ref owner._bytesWritten, chunkLength);
                buffer = buffer[chunkLength..];
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }
}
