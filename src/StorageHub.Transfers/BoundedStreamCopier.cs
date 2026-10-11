using System.Buffers;
using System.Security.Cryptography;
using StorageHub.Storage.Abstractions;

namespace StorageHub.Transfers;

/// <summary>
/// Raised when a source contains more data than the immutable length captured by the plan.
/// The extra byte is deliberately detected before a destination write is committed.
/// </summary>
public sealed class SourceLengthExceededException(long expectedLength) : IOException(
    $"Source contains more than the expected {expectedLength} bytes.")
{
    public long ExpectedLength { get; } = expectedLength;
}

public static class BoundedStreamCopier
{
    public const int DefaultBufferSize = 64 * 1024;

    public const int MaximumBufferSize = 1024 * 1024;

    public static async Task<StreamCopyResult> CopyAsync(
        Stream source,
        Stream destination,
        long? expectedLength = null,
        int bufferSize = DefaultBufferSize,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default) => await CopyCoreAsync(
        source,
        destination,
        expectedLength,
        bufferSize,
        progress,
        computeSha256: false,
        cancellationToken).ConfigureAwait(false);

    /// <summary>Copies with the same fixed buffer while hashing source bytes in the same pass.</summary>
    public static async Task<StreamCopyResult> CopyWithSha256Async(
        Stream source,
        Stream destination,
        long? expectedLength = null,
        int bufferSize = DefaultBufferSize,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default) => await CopyCoreAsync(
        source,
        destination,
        expectedLength,
        bufferSize,
        progress,
        computeSha256: true,
        cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Copies the rest of a transfer whose first <paramref name="resumedBytes"/> are already at
    /// the destination. <paramref name="source"/> starts at that offset; progress and the result
    /// count the whole item, and <paramref name="prefixHash"/>, when given, already holds the
    /// resumed bytes and is finished with the copied ones, so the digest covers the whole item.
    /// </summary>
    /// <remarks>The hash is owned, and disposed, by this call.</remarks>
    public static async Task<StreamCopyResult> CopyRemainderAsync(
        Stream source,
        Stream destination,
        long resumedBytes,
        long totalLength,
        IncrementalHash? prefixHash,
        int bufferSize = DefaultBufferSize,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using (prefixHash)
        {
            if (resumedBytes < 0 || resumedBytes > totalLength)
            {
                throw new ArgumentOutOfRangeException(nameof(resumedBytes));
            }

            return await CopyCoreAsync(
                source,
                destination,
                totalLength - resumedBytes,
                bufferSize,
                progress,
                computeSha256: prefixHash is not null,
                cancellationToken,
                resumedBytes,
                prefixHash).ConfigureAwait(false);
        }
    }

    private static async Task<StreamCopyResult> CopyCoreAsync(
        Stream source,
        Stream destination,
        long? expectedLength,
        int bufferSize,
        IProgress<TransferProgress>? progress,
        bool computeSha256,
        CancellationToken cancellationToken,
        long resumedBytes = 0,
        IncrementalHash? seededHash = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (!source.CanRead)
        {
            throw new ArgumentException("Source stream must be readable.", nameof(source));
        }

        if (!destination.CanWrite)
        {
            throw new ArgumentException("Destination stream must be writable.", nameof(destination));
        }

        if (expectedLength is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedLength));
        }

        if (bufferSize is <= 0 or > MaximumBufferSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bufferSize),
                $"Buffer size must be between 1 and {MaximumBufferSize} bytes.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        long totalCopied = 0;
        using var ownedHash = computeSha256 && seededHash is null
            ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
            : null;
        var hash = seededHash ?? ownedHash;
        long? totalLength = expectedLength is long remainder ? resumedBytes + remainder : null;

        try
        {
            while (expectedLength is null || totalCopied < expectedLength.Value)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var requested = expectedLength is long exactLength
                    ? (int)Math.Min(bufferSize, exactLength - totalCopied)
                    : bufferSize;
                var read = await source
                    .ReadAsync(buffer.AsMemory(0, requested), cancellationToken)
                    .ConfigureAwait(false);

                if (read == 0)
                {
                    if (expectedLength is long requiredLength && totalCopied != requiredLength)
                    {
                        throw new EndOfStreamException(
                            $"Source ended after {totalCopied} bytes; expected {requiredLength} bytes.");
                    }

                    break;
                }

                await destination
                    .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);

                hash?.AppendData(buffer, 0, read);

                totalCopied = checked(totalCopied + read);
                progress?.Report(new TransferProgress(resumedBytes + totalCopied, totalLength));
            }

            // Reading exactly the planned length is not sufficient evidence that the source is
            // unchanged: a source that grew would otherwise be silently truncated. Probe one
            // additional byte and fail before the caller can commit the destination.
            if (expectedLength is long exactExpectedLength)
            {
                var extra = await source
                    .ReadAsync(buffer.AsMemory(0, 1), cancellationToken)
                    .ConfigureAwait(false);
                if (extra != 0)
                {
                    throw new SourceLengthExceededException(exactExpectedLength);
                }
            }

            return new StreamCopyResult(
                resumedBytes + totalCopied,
                hash is null
                    ? null
                    : new PortableContentDigest(
                        PortableChecksumAlgorithm.Sha256,
                        Convert.ToHexStringLower(hash.GetHashAndReset())));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }
}
