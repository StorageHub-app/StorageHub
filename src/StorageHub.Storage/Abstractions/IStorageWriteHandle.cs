using StorageHub.Contracts.Results;
using StorageHub.Domain.Storage;

namespace StorageHub.Storage.Abstractions;

public enum StorageWriteHandleState
{
    Open,
    Committing,
    Committed,
    Aborting,
    Aborted,
    Faulted,

    /// <summary>Closed with its staged bytes kept for a later write with the same resume key.</summary>
    Suspended
}

/// <summary>
/// Owns a provider write stream and its commit/abort lifecycle. Disposing an uncommitted
/// handle must perform best-effort abort and must never silently commit partial content.
/// </summary>
public interface IStorageWriteHandle : IAsyncDisposable
{
    StorageAddress Destination { get; }

    /// <summary>
    /// The handle-owned writable stream, valid until commit, abort, or handle disposal.
    /// Consumers dispose the handle rather than disposing this stream independently.
    /// </summary>
    Stream Content { get; }

    /// <summary>
    /// The provider-confirmed starting offset for this handle: how many bytes an earlier write
    /// with the same resume key had already staged, which the caller must not write again.
    /// </summary>
    long AcceptedOffset { get; }

    /// <summary>
    /// A non-secret name for the staged bytes, which may be persisted with a checkpoint when the
    /// write was opened with a resume key; null otherwise.
    /// </summary>
    string? ResumeToken { get; }

    StorageWriteHandleState State { get; }

    /// <summary>Closes content and makes it visible. Implementations permit at most one terminal transition.</summary>
    ValueTask<StorageResult<StorageEntry>> CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes staged content when possible. Implementations permit at most one terminal transition.</summary>
    ValueTask<StorageResult> AbortAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// A write opened with a resume key, whose staged bytes can outlive the handle.
/// </summary>
/// <remarks>
/// <see cref="IStorageWriteHandle.AbortAsync"/> still discards everything: it is what a failure
/// that makes the bytes worthless calls, such as a source that changed or failed verification.
/// <see cref="SuspendAsync"/> is for an interruption -- a cancelled worker, a dropped connection --
/// and keeps what was written so far for the next write with the same key to continue.
/// </remarks>
public interface IResumableStorageWriteHandle : IStorageWriteHandle
{
    /// <summary>
    /// Reads back the <see cref="IStorageWriteHandle.AcceptedOffset"/> bytes already staged, so a
    /// caller that verifies a digest can account for the bytes it is not sending again.
    /// </summary>
    ValueTask<StorageResult<Stream>> OpenAcceptedContentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the content and keeps the bytes written so far staged, unpublished, for a later
    /// write with the same resume key. Implementations permit at most one terminal transition.
    /// </summary>
    ValueTask<StorageResult> SuspendAsync(CancellationToken cancellationToken = default);
}
