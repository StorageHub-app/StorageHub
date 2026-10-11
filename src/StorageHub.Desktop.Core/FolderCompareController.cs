using StorageHub.Contracts.Ipc;
using StorageHub.Contracts.Results;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>Which way a copy of compared differences goes.</summary>
public enum FolderCompareDirection
{
    LeftToRight,
    RightToLeft
}

/// <summary>Two folders as the agent compared them, every entry read.</summary>
public sealed class FolderCompareOutcome
{
    internal FolderCompareOutcome(
        PaneTransferContext left,
        PaneTransferContext right,
        IReadOnlyList<FolderCompareEntry> entries,
        bool caseInsensitive,
        int uncheckedPairs)
    {
        Left = left;
        Right = right;
        Entries = entries;
        CaseInsensitive = caseInsensitive;
        UncheckedPairs = uncheckedPairs;
    }

    public PaneTransferContext Left { get; }
    public PaneTransferContext Right { get; }

    /// <summary>Every path under either folder, sorted as the agent sorted them.</summary>
    public IReadOnlyList<FolderCompareEntry> Entries { get; }

    public bool CaseInsensitive { get; }

    /// <summary>Pairs whose contents were to be compared and could not be.</summary>
    public int UncheckedPairs { get; }
}

/// <summary>What copying a set of differences came to.</summary>
public sealed class FolderCompareCopyResult
{
    internal FolderCompareCopyResult(
        ManualTransferEnqueueResult? enqueued,
        int foldersCreated,
        int skipped,
        int renamed,
        int leftOut,
        StorageFailure? failure)
    {
        Enqueued = enqueued;
        FoldersCreated = foldersCreated;
        Skipped = skipped;
        Renamed = renamed;
        LeftOut = leftOut;
        Failure = failure ?? enqueued?.Failure;
    }

    /// <summary>The queue's answer, when anything was sent to it.</summary>
    public ManualTransferEnqueueResult? Enqueued { get; }

    public int Queued => Enqueued?.Accepted.Count ?? 0;

    public int FoldersCreated { get; }

    /// <summary>Files already at the destination that the conflict choice left as they were.</summary>
    public int Skipped { get; }

    /// <summary>Files queued under a free name because their own was taken.</summary>
    public int Renamed { get; }

    /// <summary>
    /// Items a copy cannot carry: a file where the other side has a folder of that name, a link,
    /// or a file to be replaced that nobody can say which version of is being replaced.
    /// </summary>
    public int LeftOut { get; }

    public StorageFailure? Failure { get; }
}

/// <summary>
/// Compare Folders: the agent compares the two panes' folders, and the differences somebody picks
/// are copied one way or the other as ordinary queued transfers.
/// </summary>
/// <remarks>
/// <para>
/// The comparison runs in the agent, which is what can open a saved connection, and is read here a
/// page at a time. Nothing about it is cached: the window asks again when somebody wants it fresh.
/// </para>
/// <para>
/// A copy is planned from the comparison rather than from a pane, because a difference can be
/// several folders down and a pane holds one folder. Each file is still one ordinary queued
/// transfer, held to the identity the comparison listed for whatever it replaces, so a file that
/// changed since is refused by the agent rather than overwritten.
/// </para>
/// </remarks>
public sealed class FolderCompareController : IAsyncDisposable
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(250);
    private readonly IFolderCompareAgentClient _compare;
    private readonly bool _ownsClient;
    private readonly TimeSpan _pollInterval;
    private int _disposed;

    public FolderCompareController()
        : this(new NamedPipeFolderCompareAgentClient(), ownsClient: true)
    {
    }

    public FolderCompareController(
        IFolderCompareAgentClient compare,
        bool ownsClient = false,
        TimeSpan? pollInterval = null)
    {
        _compare = compare ?? throw new ArgumentNullException(nameof(compare));
        _ownsClient = ownsClient;
        _pollInterval = pollInterval ?? DefaultPollInterval;
    }

    /// <summary>Compares the folder <paramref name="left"/> shows with the one <paramref name="right"/> shows.</summary>
    public async Task<StorageResult<FolderCompareOutcome>> CompareAsync(
        PaneTransferContext left,
        PaneTransferContext right,
        bool compareContents,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (AddressFor(left) is not { } leftAddress || AddressFor(right) is not { } rightAddress)
        {
            return StorageResult<FolderCompareOutcome>.Fail(new StorageFailure(
                "compare.pane_unsupported",
                StorageFailureKind.Validation,
                Ui.Validation.FolderComparisonNeedsAFolder));
        }

        try
        {
            var started = await _compare.StartAsync(
                new FolderCompareStartRequest(
                    FolderCompareIpcContract.CurrentVersion,
                    leftAddress,
                    rightAddress,
                    compareContents),
                cancellationToken).ConfigureAwait(false);
            if (started.Failure is { } refused)
            {
                return StorageResult<FolderCompareOutcome>.Fail(RecursiveTransferController.MapFailure(refused));
            }

            try
            {
                return await ReadAsync(left, right, started.CompareId, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await ReleaseAsync(started.CompareId).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is IOException or TimeoutException or InvalidDataException or
            InvalidOperationException or ObjectDisposedException)
        {
            return StorageResult<FolderCompareOutcome>.Fail(new StorageFailure(
                "compare.agent_unavailable",
                StorageFailureKind.Unavailable,
                Ui.Validation.TheBackgroundAgentCouldNotCompareTheFolders,
                isTransient: true));
        }
    }

    /// <summary>Waits for the comparison to finish, then reads every page of it.</summary>
    private async Task<StorageResult<FolderCompareOutcome>> ReadAsync(
        PaneTransferContext left,
        PaneTransferContext right,
        Guid compareId,
        CancellationToken cancellationToken)
    {
        var status = await StatusAsync(compareId, 0, cancellationToken).ConfigureAwait(false);
        while (status.State == FolderCompareState.Running)
        {
            await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
            status = await StatusAsync(compareId, 0, cancellationToken).ConfigureAwait(false);
        }

        if (status.State == FolderCompareState.Failed)
        {
            return StorageResult<FolderCompareOutcome>.Fail(RecursiveTransferController.MapFailure(status.Failure!));
        }

        var entries = new List<FolderCompareEntry>(status.TotalEntries);
        entries.AddRange(status.Entries);
        while (entries.Count < status.TotalEntries)
        {
            var page = await StatusAsync(compareId, entries.Count, cancellationToken).ConfigureAwait(false);
            if (page.State != FolderCompareState.Completed ||
                page.TotalEntries != status.TotalEntries ||
                page.Entries.Length == 0)
            {
                throw new InvalidDataException(Ui.Validation.TheAgentReturnedAnInvalidFolderComparison);
            }

            entries.AddRange(page.Entries);
        }

        if (entries.Count != status.TotalEntries ||
            entries.Select(static entry => entry.RelativePath).Distinct(StringComparer.Ordinal).Count() != entries.Count)
        {
            throw new InvalidDataException(Ui.Validation.TheAgentReturnedAnInvalidFolderComparison);
        }

        return StorageResult<FolderCompareOutcome>.Success(new FolderCompareOutcome(
            left,
            right,
            entries.AsReadOnly(),
            status.CaseInsensitive,
            status.UncheckedPairs));
    }

    private Task<FolderCompareStatusResponse> StatusAsync(Guid compareId, int offset, CancellationToken cancellationToken) =>
        _compare.GetStatusAsync(
            new FolderCompareStatusRequest(FolderCompareIpcContract.CurrentVersion, compareId, offset),
            cancellationToken);

    /// <summary>
    /// Lets the agent drop the comparison. Best effort: one it is not told about expires on its own,
    /// and a failure here must not hide the comparison that was read.
    /// </summary>
    private async Task ReleaseAsync(Guid compareId)
    {
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _compare.ReleaseAsync(
                new FolderCompareReleaseRequest(FolderCompareIpcContract.CurrentVersion, compareId),
                deadline.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or TimeoutException or InvalidDataException or
            InvalidOperationException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// A pane's folder as the agent is told it: a saved connection's root and path, or a folder on
    /// this PC encoded as a queued transfer's is. Null for anything else -- the drive list, an
    /// ad-hoc connection, the connections home -- which has no folder to compare.
    /// </summary>
    internal static TransferQueueAddress? AddressFor(PaneTransferContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        TransferQueueAddress? address = context.Kind switch
        {
            PaneTransferContextKind.SavedConnection when context.ConnectionId is { } id && context.RootIdentity is { } root =>
                new TransferQueueAddress(id, root, context.RelativePath),
            PaneTransferContextKind.ThisPc when context.RelativePath.Length > 0 &&
                Path.IsPathFullyQualified(context.RelativePath) =>
                new TransferQueueAddress(
                    LocalTransferFolder.CreateConnectionId(context.RelativePath),
                    LocalTransferFolder.CreateRootIdentity(context.RelativePath),
                    string.Empty),
            _ => null
        };
        return address is { HasValidBounds: true } ? address : null;
    }

    /// <summary>
    /// Copies <paramref name="selected"/> the way <paramref name="direction"/> says: folders the
    /// destination lacks are created, and each file is queued as an ordinary transfer.
    /// </summary>
    /// <param name="ensureFolder">
    /// Creates one folder on the destination, by its path relative to the compared folder there;
    /// a failure is the reason it could not. Folders are created parents first.
    /// </param>
    public static async Task<FolderCompareCopyResult> CopyAsync(
        FolderCompareOutcome compared,
        IEnumerable<FolderCompareEntry> selected,
        FolderCompareDirection direction,
        ManualTransferController transfers,
        Func<string, CancellationToken, Task<StorageFailure?>> ensureFolder,
        TransferConflictPolicy? conflicts = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transfers);
        ArgumentNullException.ThrowIfNull(ensureFolder);
        var plan = await PlanAsync(compared, selected, direction, conflicts ?? TransferConflictPolicy.Replace, cancellationToken)
            .ConfigureAwait(false);
        if (plan.IsFailure)
        {
            return new FolderCompareCopyResult(null, 0, 0, 0, 0, plan.Error);
        }

        var created = 0;
        foreach (var folder in plan.Value.Folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await ensureFolder(folder, cancellationToken).ConfigureAwait(false) is { } failure)
            {
                return new FolderCompareCopyResult(
                    null, created, plan.Value.Skipped, plan.Value.Renamed, plan.Value.LeftOut, failure);
            }

            created++;
        }

        var enqueued = plan.Value.Requests.Count == 0
            ? null
            : await transfers.EnqueuePlanAsync(
                new ManualTransferPlan(plan.Value.Requests, plan.Value.Skipped, plan.Value.Renamed),
                cancellationToken).ConfigureAwait(false);
        return new FolderCompareCopyResult(
            enqueued, created, plan.Value.Skipped, plan.Value.Renamed, plan.Value.LeftOut, failure: null);
    }

    /// <summary>
    /// What a copy will do: the folders to create, parents first, by their path relative to the
    /// compared folder on the destination, and the transfers to queue.
    /// </summary>
    internal sealed record CopyPlan(
        IReadOnlyList<string> Folders,
        IReadOnlyList<TransferEnqueueRequest> Requests,
        int Skipped,
        int Renamed,
        int LeftOut);

    /// <summary>
    /// Plans a copy. A selected folder brings with it everything under it that the destination
    /// lacks or has differently; a selected file brings the folders above it that the destination
    /// lacks. What only the destination has, and what is the same, is never touched.
    /// </summary>
    internal static async Task<StorageResult<CopyPlan>> PlanAsync(
        FolderCompareOutcome compared,
        IEnumerable<FolderCompareEntry> selected,
        FolderCompareDirection direction,
        TransferConflictPolicy conflicts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compared);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(conflicts);
        var toRight = direction == FolderCompareDirection.LeftToRight;
        var source = toRight ? compared.Left : compared.Right;
        var destination = toRight ? compared.Right : compared.Left;
        if (ManualTransferController.RequireQueueableEndpoints(source, destination) is { } refused)
        {
            return StorageResult<CopyPlan>.Fail(refused);
        }

        var comparer = compared.CaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var byPath = compared.Entries.ToDictionary(static entry => entry.RelativePath, StringComparer.Ordinal);
        var included = Expand(compared.Entries, selected, toRight, byPath);

        // Every name already taken at the destination, and every one this plan takes, so a rename
        // finds a name that is free on the destination rather than one free in the selection.
        var taken = new HashSet<string>(
            compared.Entries.Where(entry => To(entry, toRight) is not null).Select(entry => ToPath(entry, toRight)),
            comparer);
        var folders = new List<string>();
        var folderSet = new HashSet<string>(comparer);
        var requests = new List<TransferEnqueueRequest>();
        int skipped = 0, renamed = 0, leftOut = 0;
        foreach (var entry in included)
        {
            var from = From(entry, toRight);
            var to = To(entry, toRight);
            if (from is null || entry.Kind == FolderCompareKind.Same)
            {
                continue;
            }

            if (IsContainer(from.Kind))
            {
                if (to is null)
                {
                    AddFolders(ToPath(entry, toRight), entry.RelativePath);
                }
                else if (!IsContainer(to.Kind))
                {
                    leftOut++;
                }

                continue;
            }

            if (from.Kind != StorageItemKind.File || to is not null && to.Kind != StorageItemKind.File)
            {
                leftOut++;
                continue;
            }

            var target = ToPath(entry, toRight);
            AddParents(entry.RelativePath);
            if (to is not null)
            {
                var existing = Item(target, to);
                var incoming = Item(FromPath(entry, toRight), from);
                if (existing is null || incoming is null)
                {
                    leftOut++;
                    continue;
                }

                if (await conflicts.ResolveAsync(existing, cancellationToken).ConfigureAwait(false) is not { } choice)
                {
                    return StorageResult<CopyPlan>.Fail(ManualTransferController.StoppedAtConflict());
                }

                switch (TransferConflictRules.Decide(choice, incoming, existing))
                {
                    case TransferConflictOutcome.Skip:
                        skipped++;
                        continue;
                    case TransferConflictOutcome.Rename:
                        var parent = Parent(target);
                        if (TransferConflictRules.FreeName(Name(target), name => taken.Contains(Join(parent, name))) is not { } free)
                        {
                            return StorageResult<CopyPlan>.Fail(new StorageFailure(
                                "manual_transfer.rename.no_free_name",
                                StorageFailureKind.Validation,
                                Ui.Format(Ui.Validation.NoFreeNameForRenameFormat, Name(target))));
                        }

                        target = Join(parent, free);
                        taken.Add(target);
                        to = null;
                        renamed++;
                        break;
                }

                // Replacing a file is held to the version listed for it, and the agent can only
                // hold it to one it was given.
                if (to is not null && (to.VersionId is null && to.EntityTag is null ||
                    from.VersionId is null && from.EntityTag is null))
                {
                    leftOut++;
                    continue;
                }
            }

            var request = new TransferEnqueueRequest(
                TransferQueueIpcContract.CurrentVersion,
                Guid.NewGuid(),
                TransferQueueOperation.Copy,
                ManualTransferController.CreateAddress(
                    source, Under(source, FromPath(entry, toRight)), from.NativeItemId, from.VersionId, from.EntityTag),
                ManualTransferController.CreateAddress(
                    destination, Under(destination, target), to?.NativeItemId, to?.VersionId, to?.EntityTag),
                from.Size,
                TransferQueueVerification.StrongHashWhenAvailable,
                0,
                to?.VersionId,
                to?.EntityTag);
            if (!request.HasValidBounds)
            {
                return StorageResult<CopyPlan>.Fail(new StorageFailure(
                    "manual_transfer.request.invalid",
                    StorageFailureKind.Integrity,
                    Ui.Validation.AValidatedPaneSnapshotProducedAnInvalid));
            }

            requests.Add(request);
        }

        conflicts.Count(skipped, renamed);
        return StorageResult<CopyPlan>.Success(new CopyPlan(
            folders.AsReadOnly(), requests.AsReadOnly(), skipped, renamed, leftOut));

        // The folders above a file that the destination lacks, parents first.
        void AddParents(string relativePath)
        {
            var parent = Parent(relativePath);
            if (parent.Length > 0 && byPath.TryGetValue(parent, out var folder) &&
                From(folder, toRight) is not null && To(folder, toRight) is null)
            {
                AddFolders(ToPath(folder, toRight), parent);
            }
        }

        void AddFolders(string target, string relativePath)
        {
            if (folderSet.Contains(target))
            {
                return;
            }

            AddParents(relativePath);
            if (folderSet.Add(target))
            {
                folders.Add(target);
            }
        }
    }

    /// <summary>
    /// The selection, with each selected folder's differences under it, in path order and each
    /// once. A folder whose name is a file on the other side brings nothing: it cannot be entered.
    /// </summary>
    private static List<FolderCompareEntry> Expand(
        IReadOnlyList<FolderCompareEntry> entries,
        IEnumerable<FolderCompareEntry> selected,
        bool toRight,
        Dictionary<string, FolderCompareEntry> byPath)
    {
        var chosen = new HashSet<string>(StringComparer.Ordinal);
        var folders = new List<string>();
        foreach (var entry in selected)
        {
            if (entry is null || !byPath.ContainsKey(entry.RelativePath))
            {
                continue;
            }

            chosen.Add(entry.RelativePath);
            if (From(entry, toRight) is { } from && IsContainer(from.Kind) &&
                (To(entry, toRight) is not { } to || IsContainer(to.Kind)))
            {
                folders.Add(entry.RelativePath + "/");
            }
        }

        // Entries are in path order, so everything under a folder follows it.
        return [.. entries.Where(entry =>
            chosen.Contains(entry.RelativePath) ||
            entry.Kind != FolderCompareKind.Same &&
            folders.Any(folder => entry.RelativePath.StartsWith(folder, StringComparison.Ordinal)))];
    }

    /// <summary>
    /// A path relative to the compared folder, as the queue addresses it: a saved connection's is
    /// relative to its root, and a folder on this PC is its own root.
    /// </summary>
    private static string Under(PaneTransferContext context, string relativePath) =>
        context.Kind == PaneTransferContextKind.ThisPc || context.RelativePath.Length == 0
            ? relativePath
            : context.RelativePath + "/" + relativePath;

    private static FolderCompareItem? From(FolderCompareEntry entry, bool toRight) => toRight ? entry.Left : entry.Right;

    private static FolderCompareItem? To(FolderCompareEntry entry, bool toRight) => toRight ? entry.Right : entry.Left;

    private static string FromPath(FolderCompareEntry entry, bool toRight) =>
        toRight ? entry.RelativePath : entry.RightRelativePath ?? entry.RelativePath;

    private static string ToPath(FolderCompareEntry entry, bool toRight) =>
        toRight ? entry.RightRelativePath ?? entry.RelativePath : entry.RelativePath;

    private static bool IsContainer(StorageItemKind kind) => kind is StorageItemKind.Directory or StorageItemKind.Prefix;

    /// <summary>One side's file as the conflict rules see it. Null when it cannot be described.</summary>
    private static PaneTransferItem? Item(string relativePath, FolderCompareItem item)
    {
        var created = PaneTransferItem.Create(
            Name(relativePath),
            relativePath,
            item.Kind,
            item.Size,
            item.NativeItemId,
            item.VersionId,
            item.EntityTag,
            item.LastModifiedUtc);
        return created.IsSuccess ? created.Value : null;
    }

    private static string Parent(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }

    private static string Name(string path) => path[(path.LastIndexOf('/') + 1)..];

    private static string Join(string parent, string name) => parent.Length == 0 ? name : parent + "/" + name;

    /// <summary>
    /// Creates a folder under a folder on this PC, one name at a time and never through a link,
    /// the way the agent creates one for a transfer. Used for a folder that has nothing in it to
    /// copy: the agent creates the folders above every file it writes.
    /// </summary>
    public static StorageFailure? EnsureLocalFolder(string baseFolder, string relativePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseFolder);
        ArgumentNullException.ThrowIfNull(relativePath);
        var failure = new StorageFailure(
            "compare.folder_create_failed",
            StorageFailureKind.Unavailable,
            Ui.Validation.AFolderCouldNotBeCreatedAtTheDestination);
        if (!Path.IsPathFullyQualified(baseFolder))
        {
            return failure;
        }

        try
        {
            var current = baseFolder;
            foreach (var name in relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    return failure;
                }

                current = Path.Combine(current, name);
                var folder = new DirectoryInfo(current);
                if (folder.Exists)
                {
                    if (folder.LinkTarget is not null)
                    {
                        return failure;
                    }

                    continue;
                }

                if (File.Exists(current))
                {
                    return failure;
                }

                Directory.CreateDirectory(current);
            }

            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return failure;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_ownsClient)
        {
            await _compare.DisposeAsync().ConfigureAwait(false);
        }
    }
}
