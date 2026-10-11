using System.Collections.Concurrent;
using StorageHub.Contracts.Results;
using StorageHub.Domain.Capabilities;
using StorageHub.Domain.Storage;
using StorageHub.Storage.Abstractions;

namespace StorageHub.Sync;

/// <summary>How a path found under either of two compared folders relates to the other side.</summary>
public enum FolderComparisonKind
{
    /// <summary>Only the left folder has it.</summary>
    OnlyLeft,

    /// <summary>Only the right folder has it.</summary>
    OnlyRight,

    /// <summary>Both have it, and something known about the two tells them apart.</summary>
    Different,

    /// <summary>Both have it, and nothing known about the two tells them apart.</summary>
    Same
}

/// <summary>What told two items apart. CL.Storage's <c>StorageDiffReason</c>, in left and right.</summary>
[Flags]
public enum FolderComparisonReasons
{
    None = 0,

    /// <summary>The two sizes differ.</summary>
    Size = 1,

    /// <summary>The left one was changed later than the right, by more than the tolerance.</summary>
    LeftNewer = 2,

    /// <summary>The right one was changed later than the left, by more than the tolerance.</summary>
    RightNewer = 4,

    /// <summary>The two SHA-256 checksums differ.</summary>
    Checksum = 8,

    /// <summary>One is a file and the other a folder, or a link.</summary>
    Type = 16
}

/// <summary>What to compare by, and how much a comparison may read.</summary>
/// <param name="CompareContents">
/// Whether two files of the same size are also told apart by their SHA-256. A server that keeps
/// one says it without the file being read; otherwise it is read, which on a remote connection is
/// a download, so it is asked for rather than assumed.
/// </param>
/// <param name="TimeTolerance">
/// Two times this close are the same time: FAT keeps two seconds, and many FTP servers list to the
/// minute. Two seconds when not given, as CL.Storage's own comparison does.
/// </param>
/// <param name="MaximumEntries">The most items either folder may hold, its subfolders included.</param>
/// <param name="MaximumHashedPairs">The most pairs whose checksums are taken.</param>
/// <param name="MaximumHashBytesPerFile">A file bigger than this is not checksummed.</param>
/// <param name="MaximumTotalHashBytes">The most bytes checksummed on each side altogether.</param>
public sealed record FolderComparisonOptions(
    bool CompareContents = false,
    TimeSpan? TimeTolerance = null,
    int MaximumEntries = 20_000,
    int MaximumHashedPairs = 2_000,
    long MaximumHashBytesPerFile = 1024L * 1024 * 1024,
    long MaximumTotalHashBytes = 4L * 1024 * 1024 * 1024)
{
    internal TimeSpan Tolerance => TimeTolerance ?? TimeSpan.FromSeconds(2);
}

/// <summary>One path, as each side has it.</summary>
/// <param name="RelativePath">The path below the compared folders, spelled as the left side spells it when it has it.</param>
/// <param name="LeftDigest">The left file's SHA-256, when one was taken.</param>
/// <param name="RightDigest">The right file's SHA-256, when one was taken.</param>
/// <param name="RightRelativePath">
/// How the right side spells the path, when names were matched ignoring case and it spells it
/// otherwise: a copy onto the right has to land on "report.txt", not beside it as "Report.TXT".
/// </param>
public sealed record FolderComparisonEntry(
    string RelativePath,
    FolderComparisonKind Kind,
    FolderComparisonReasons Reasons,
    StorageEntry? Left,
    StorageEntry? Right,
    PortableContentDigest? LeftDigest = null,
    PortableContentDigest? RightDigest = null,
    string? RightRelativePath = null);

/// <summary>Every path under either folder, in the order a tree reads.</summary>
/// <param name="UncheckedPairs">
/// Pairs whose contents were to be compared and could not be: a side that cannot checksum, a file
/// over the limit, a read that failed. Those are judged by size and time alone.
/// </param>
public sealed record FolderComparisonResult(
    IReadOnlyList<FolderComparisonEntry> Entries,
    bool CaseInsensitive,
    int UncheckedPairs);

/// <summary>
/// Compares two folders, on any two endpoints, for the "Compare folders" window.
/// </summary>
/// <remarks>
/// <para>
/// CL.Storage has a comparison of its own, <c>StorageCompare.CompareAsync</c>, and its rules are
/// the ones kept here: a file is told apart by size and by modification time beyond a tolerance,
/// and by checksum when asked; one side being a file and the other a folder is a difference of
/// type; and two folders match, leaving what is inside them to be compared item by item. It is not
/// called, because it compares two connections registered in the library, and one side of a pane
/// comparison is as often a folder on this PC, which the library has never heard of.
/// </para>
/// <para>
/// Both sides are read with <see cref="SyncSnapshotScanner"/> instead, the scanner sync plans
/// from: every page and every folder bounded, nothing followed through a link, and every path it
/// reports checked to be inside the folder asked for. StorageHub's own staging files -- a partial
/// download, the Local provider's reserved folder -- are left out on both sides, because they are
/// not a difference between two folders anybody made.
/// </para>
/// </remarks>
public static class FolderComparison
{
    /// <summary>Lists both folders and compares them.</summary>
    public static async ValueTask<StorageResult<FolderComparisonResult>> CompareAsync(
        IStorageEndpointSession left,
        StorageAddress leftRoot,
        IStorageEndpointSession right,
        StorageAddress rightRoot,
        FolderComparisonOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(leftRoot);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(rightRoot);
        options ??= new FolderComparisonOptions();

        var scanOptions = new SyncSnapshotScanOptions(
            maximumEntries: options.MaximumEntries,
            maximumDirectories: options.MaximumEntries,
            portableHashMode: SyncPortableHashMode.Disabled);
        var leftSnapshot = await SyncSnapshotScanner.ScanAsync(left, leftRoot, scanOptions, cancellationToken)
            .ConfigureAwait(false);
        if (leftSnapshot.IsFailure)
        {
            return StorageResult<FolderComparisonResult>.Fail(leftSnapshot.Error);
        }

        var rightSnapshot = await SyncSnapshotScanner.ScanAsync(right, rightRoot, scanOptions, cancellationToken)
            .ConfigureAwait(false);
        if (rightSnapshot.IsFailure)
        {
            return StorageResult<FolderComparisonResult>.Fail(rightSnapshot.Error);
        }

        var compared = Compare(leftSnapshot.Value, rightSnapshot.Value, options);
        if (!options.CompareContents)
        {
            return StorageResult<FolderComparisonResult>.Success(compared);
        }

        return await CompareContentsAsync(left, right, compared, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Compares two snapshots already taken, by what their listings say: kind, size and time.
    /// </summary>
    public static FolderComparisonResult Compare(
        SyncEndpointSnapshot left,
        SyncEndpointSnapshot right,
        FolderComparisonOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        options ??= new FolderComparisonOptions();

        // One case-insensitive side makes the pair insensitive, as CL.Storage decides it: "A.txt"
        // on Windows and "a.txt" on a Linux server are the same file to anybody copying between them.
        var insensitive = left.CaseSensitivity != StorageCaseSensitivity.Sensitive ||
            right.CaseSensitivity != StorageCaseSensitivity.Sensitive;
        var comparer = insensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var leftItems = Visible(left, comparer);
        var rightItems = Visible(right, comparer);

        // An item only one side has, in a folder both have under different spellings, is filed under
        // the left's spelling and keeps the right's in RightRelativePath, so "Docs" and everything in
        // it read as one folder and a copy either way lands in the folder that is already there.
        var leftFolders = insensitive ? Folders(leftItems, comparer) : null;
        var rightFolders = insensitive ? Folders(rightItems, comparer) : null;
        var entries = new List<FolderComparisonEntry>(Math.Max(leftItems.Count, rightItems.Count));
        foreach (var (key, (path, leftEntry)) in leftItems)
        {
            if (!rightItems.TryGetValue(key, out var match))
            {
                var there = Respell(path, rightFolders);
                entries.Add(new FolderComparisonEntry(
                    path,
                    FolderComparisonKind.OnlyLeft,
                    FolderComparisonReasons.None,
                    leftEntry,
                    null,
                    RightRelativePath: string.Equals(there, path, StringComparison.Ordinal) ? null : there));
                continue;
            }

            var reasons = Reasons(leftEntry, match.Entry, options.Tolerance);
            entries.Add(new FolderComparisonEntry(
                path,
                reasons == FolderComparisonReasons.None ? FolderComparisonKind.Same : FolderComparisonKind.Different,
                reasons,
                leftEntry,
                match.Entry,
                RightRelativePath: string.Equals(path, match.Path, StringComparison.Ordinal) ? null : match.Path));
        }

        foreach (var (key, (path, rightEntry)) in rightItems)
        {
            if (!leftItems.ContainsKey(key))
            {
                var shown = Respell(path, leftFolders);
                entries.Add(new FolderComparisonEntry(
                    shown,
                    FolderComparisonKind.OnlyRight,
                    FolderComparisonReasons.None,
                    null,
                    rightEntry,
                    RightRelativePath: string.Equals(shown, path, StringComparison.Ordinal) ? null : path));
            }
        }

        entries.Sort(static (x, y) => ComparePaths(x.RelativePath, y.RelativePath));
        return new FolderComparisonResult(entries, insensitive, UncheckedPairs: 0);
    }

    /// <summary>Orders paths so a folder comes right before everything inside it ("a", "a/b", "a-b").</summary>
    public static int ComparePaths(string x, string y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        var length = Math.Min(x.Length, y.Length);
        for (var index = 0; index < length; index++)
        {
            if (x[index] == y[index])
            {
                continue;
            }

            if (x[index] == '/')
            {
                return -1;
            }

            return y[index] == '/' ? 1 : x[index].CompareTo(y[index]);
        }

        return x.Length.CompareTo(y.Length);
    }

    /// <summary>
    /// A side's items by path, StorageHub's own staging left out. Each keeps the side's own
    /// spelling beside the key, which under a case-insensitive comparer is the first one seen.
    /// </summary>
    private static Dictionary<string, (string Path, StorageEntry Entry)> Visible(
        SyncEndpointSnapshot snapshot,
        StringComparer comparer)
    {
        var items = new Dictionary<string, (string Path, StorageEntry Entry)>(comparer);
        foreach (var (path, entry) in snapshot.Entries)
        {
            if (!StorageHubStagingNames.IsWithinStaging(path))
            {
                items.TryAdd(path, (path, entry));
            }
        }

        return items;
    }

    /// <summary>Every folder a side has, by key, as that side spells it: each one listed and each above an item.</summary>
    private static Dictionary<string, string> Folders(
        Dictionary<string, (string Path, StorageEntry Entry)> items,
        StringComparer comparer)
    {
        var folders = new Dictionary<string, string>(comparer);
        foreach (var (path, entry) in items.Values)
        {
            var folder = entry.IsContainer ? path : Parent(path);
            while (folder.Length > 0 && folders.TryAdd(folder, folder))
            {
                folder = Parent(folder);
            }
        }

        return folders;
    }

    /// <summary>A path with its deepest folder the other side also has spelled the other side's way.</summary>
    private static string Respell(string path, Dictionary<string, string>? folders)
    {
        if (folders is null)
        {
            return path;
        }

        for (var slash = path.LastIndexOf('/'); slash > 0; slash = path.LastIndexOf('/', slash - 1))
        {
            if (folders.TryGetValue(path[..slash], out var spelled))
            {
                return spelled + path[slash..];
            }
        }

        return path;
    }

    private static string Parent(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }

    private static FolderComparisonReasons Reasons(StorageEntry left, StorageEntry right, TimeSpan tolerance)
    {
        if (left.IsContainer || right.IsContainer || left.Kind != right.Kind)
        {
            return left.IsContainer && right.IsContainer ? FolderComparisonReasons.None : FolderComparisonReasons.Type;
        }

        var reasons = FolderComparisonReasons.None;
        if (left.Size is { } leftSize && right.Size is { } rightSize && leftSize != rightSize)
        {
            reasons |= FolderComparisonReasons.Size;
        }

        if (left.LastModifiedUtc is { } leftTime && right.LastModifiedUtc is { } rightTime)
        {
            var difference = leftTime - rightTime;
            if (difference > tolerance)
            {
                reasons |= FolderComparisonReasons.LeftNewer;
            }
            else if (-difference > tolerance)
            {
                reasons |= FolderComparisonReasons.RightNewer;
            }
        }

        return reasons;
    }

    /// <summary>
    /// Takes the SHA-256 of both files of each pair the same size and the same kind, and decides by
    /// it: equal checksums are the same file whatever the times say, as a copy that kept no time is.
    /// </summary>
    /// <remarks>
    /// Pairs whose sizes already differ are not read, since nothing a checksum says would change
    /// the answer. A failure on one pair leaves that pair as size and time judged it and is
    /// counted, rather than failing a comparison of thousands for one locked file.
    /// </remarks>
    private static async ValueTask<StorageResult<FolderComparisonResult>> CompareContentsAsync(
        IStorageEndpointSession left,
        IStorageEndpointSession right,
        FolderComparisonResult compared,
        FolderComparisonOptions options,
        CancellationToken cancellationToken)
    {
        var candidates = compared.Entries
            .Select(static (entry, index) => (Entry: entry, Index: index))
            .Where(static pair =>
                pair.Entry.Left is { Kind: StorageEntryKind.File, Size: { } size } &&
                pair.Entry.Right is { Kind: StorageEntryKind.File } &&
                pair.Entry.Right.Size == size)
            .ToArray();
        if (candidates.Length == 0)
        {
            return StorageResult<FolderComparisonResult>.Success(compared);
        }

        if (left is not IStoragePortableChecksumSession leftChecksums ||
            right is not IStoragePortableChecksumSession rightChecksums)
        {
            return StorageResult<FolderComparisonResult>.Success(compared with { UncheckedPairs = candidates.Length });
        }

        var skipped = 0;
        long budget = 0;
        var chosen = new List<(FolderComparisonEntry Entry, int Index)>();
        foreach (var candidate in candidates)
        {
            var size = candidate.Entry.Left!.Size!.Value;
            if (chosen.Count >= options.MaximumHashedPairs ||
                size > options.MaximumHashBytesPerFile ||
                budget + size > options.MaximumTotalHashBytes)
            {
                skipped++;
                continue;
            }

            budget += size;
            chosen.Add(candidate);
        }

        var digests = new ConcurrentDictionary<int, (PortableContentDigest Left, PortableContentDigest Right)>();
        var failed = 0;
        try
        {
            await Parallel.ForEachAsync(
                chosen,
                new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = 2 },
                async (pair, token) =>
                {
                    var leftDigest = await leftChecksums.ComputePortableChecksumAsync(
                        new PortableChecksumRequest(pair.Entry.Left!, options.MaximumHashBytesPerFile),
                        token).ConfigureAwait(false);
                    var rightDigest = leftDigest.IsFailure
                        ? null
                        : await rightChecksums.ComputePortableChecksumAsync(
                            new PortableChecksumRequest(pair.Entry.Right!, options.MaximumHashBytesPerFile),
                            token).ConfigureAwait(false);
                    if (leftDigest.IsFailure || rightDigest is null || rightDigest.IsFailure)
                    {
                        Interlocked.Increment(ref failed);
                        return;
                    }

                    digests[pair.Index] = (leftDigest.Value.Digest, rightDigest.Value.Digest);
                }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StorageResult<FolderComparisonResult>.Fail(new StorageFailure(
                "compare.cancelled",
                StorageFailureKind.Cancelled,
                "The comparison was cancelled."));
        }

        var entries = compared.Entries.ToArray();
        foreach (var (index, (leftDigest, rightDigest)) in digests)
        {
            var entry = entries[index];
            var reasons = string.Equals(leftDigest.Value, rightDigest.Value, StringComparison.Ordinal)
                ? FolderComparisonReasons.None
                : entry.Reasons | FolderComparisonReasons.Checksum;
            entries[index] = entry with
            {
                Kind = reasons == FolderComparisonReasons.None ? FolderComparisonKind.Same : FolderComparisonKind.Different,
                Reasons = reasons,
                LeftDigest = leftDigest,
                RightDigest = rightDigest
            };
        }

        return StorageResult<FolderComparisonResult>.Success(new FolderComparisonResult(
            entries,
            compared.CaseInsensitive,
            skipped + failed));
    }
}
