using System.Runtime.CompilerServices;
using StorageHub.Contracts.Ipc;
using StorageHub.Contracts.Results;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>Expands selected remote folders into a bounded immutable file-transfer plan.</summary>
internal sealed class RecursiveTransferController : IAsyncDisposable
{
    private const int MaximumManifestEntries = 10_000;
    private const int MaximumManifestPages = 1_000;
    private const long MaximumCombinedPathCharacters = 4_000_000;
    private readonly ManualTransferController _transfers;
    private readonly IRemoteStorageAgentClient _storage;
    private readonly IObjectInspectorAgentClient _mutations;
    private readonly bool _ownsClients;
    private bool _disposed;

    internal RecursiveTransferController(ManualTransferController transfers)
        : this(
            transfers,
            new NamedPipeRemoteStorageAgentClient(),
            new NamedPipeObjectInspectorAgentClient(),
            ownsClients: true)
    {
    }

    internal RecursiveTransferController(
        ManualTransferController transfers,
        IRemoteStorageAgentClient storage,
        IObjectInspectorAgentClient mutations,
        bool ownsClients = false)
    {
        _transfers = transfers ?? throw new ArgumentNullException(nameof(transfers));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _mutations = mutations ?? throw new ArgumentNullException(nameof(mutations));
        _ownsClients = ownsClients;
    }

    /// <param name="progress">
    /// Called with the running file and folder counts after every page, so the queue can show the
    /// walk while it runs. Optional: the transfer is identical without it.
    /// </param>
    internal async Task<ManualTransferEnqueueResult> EnqueueAsync(
        PaneSelectionSnapshot source,
        PaneDestinationSnapshot destination,
        TransferQueueOperation operation,
        CancellationToken cancellationToken,
        Action<int, int>? progress = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (operation == TransferQueueOperation.Move && source.Items.Any(static item => item.IsContainer))
        {
            return Failure(
                "manual_transfer.folder_move_not_supported",
                Ui.Validation.FolderMovesAreNotEnabledYetBecause);
        }

        if (source.Context.Kind != PaneTransferContextKind.SavedConnection ||
            destination.Context.Kind != PaneTransferContextKind.SavedConnection ||
            source.Context.ConnectionId is null ||
            destination.Context.ConnectionId is null)
        {
            return Failure(
                "manual_transfer.saved_connections_required",
                Ui.Validation.RecursiveTransfersRequireSavedConnectionsOnBoth);
        }

        var accepted = new List<TransferEnqueueResponse>();
        try
        {
            return await StreamAsync(source, destination, operation, accepted, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // The person stopped the walk. What it already queued is durable and stays queued;
            // stopping the reading of a folder is not a request to undo accepted transfers.
            return new ManualTransferEnqueueResult(accepted, [], new StorageFailure(
                "manual_transfer.gathering_cancelled",
                StorageFailureKind.Cancelled,
                Ui.Validation.ReadingTheFolderWasStopped));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or
            InvalidOperationException or TimeoutException or System.Text.Json.JsonException)
        {
            // One sentence for six unrelated causes told nobody anything, and the exception was
            // dropped rather than logged, so a report of it could not be followed up either. The
            // shell already decides once what a failed agent call means; this asks it.
            return new ManualTransferEnqueueResult(accepted, [], new StorageFailure(
                "manual_transfer.manifest_unavailable",
                StorageFailureKind.Unavailable,
                DesktopAgentAvailability.ReportFailure(error),
                isTransient: true));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsClients)
        {
            await _storage.DisposeAsync().ConfigureAwait(false);
            await _mutations.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Walks the dropped folders and queues what it finds as it finds them, rather than reading the
    /// whole tree first. The checks that used to need the finished manifest -- a duplicate target, a
    /// file and a folder claiming one path, an entry outside the folder that was dropped -- are all
    /// "have I seen this already", so they carry across pages unchanged in the sets below.
    ///
    /// What this gives up is the all-or-nothing guarantee. A collision found on the last page used
    /// to mean nothing had been queued; now the files before it are queued and may already be
    /// moving. That is the trade every file manager that shows a growing queue makes, and the
    /// result already carries <see cref="ManualTransferEnqueueResult.IsPartial"/> to say so.
    /// </summary>
    private async Task<ManualTransferEnqueueResult> StreamAsync(
        PaneSelectionSnapshot source,
        PaneDestinationSnapshot destination,
        TransferQueueOperation operation,
        List<TransferEnqueueResponse> accepted,
        Action<int, int>? progress,
        CancellationToken cancellationToken)
    {
        var sourceEntryKinds = new Dictionary<string, StorageItemKind>(StringComparer.Ordinal);
        var ensuredDirectories = new HashSet<string>(StringComparer.Ordinal);
        var targetPaths = new HashSet<string>(StringComparer.Ordinal);
        var destinationEntries = destination.Entries.ToDictionary(
            static item => item.RelativePath,
            StringComparer.Ordinal);
        var batch = new List<(PaneTransferItem Item, string DestinationPath)>();
        long combinedPathCharacters = 0;
        var entryCount = 0;

        // Loose files first. A mixed drop then puts rows in the queue before any folder is read.
        foreach (var selected in source.Items.Where(static item => !item.IsContainer))
        {
            var destinationPath = Combine(destination.Context.RelativePath, selected.Name);
            combinedPathCharacters += selected.RelativePath.Length + destinationPath.Length;
            entryCount++;
            batch.Add((selected, destinationPath));
        }

        if (batch.Count > 0)
        {
            var flushed = await FlushAsync(
                batch, source, destination, operation, targetPaths, ensuredDirectories,
                destinationEntries, accepted, cancellationToken).ConfigureAwait(false);
            if (flushed is not null)
            {
                return new ManualTransferEnqueueResult(accepted, flushed.AmbiguousTransferIds, flushed.Failure);
            }

            batch.Clear();
            progress?.Invoke(accepted.Count, ensuredDirectories.Count);
        }

        foreach (var selected in source.Items.Where(static item => item.IsContainer))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetRoot = Combine(destination.Context.RelativePath, selected.Name);
            progress?.Invoke(accepted.Count, ensuredDirectories.Count);

            var rootFailure = await EnsureDirectoryChainAsync(
                destination, targetRoot, ensuredDirectories, cancellationToken).ConfigureAwait(false);
            if (rootFailure is not null)
            {
                return new ManualTransferEnqueueResult(accepted, [], rootFailure);
            }

            // What the destination already holds under that root. Drained whole because a conflict
            // has to be known before the first file lands on top of something.
            var existing = await DrainAsync(ListTreePagesAsync(
                destination.Context.ConnectionId!.Value,
                destination.Context.RootIdentity!,
                targetRoot,
                allowNotFound: true,
                cancellationToken)).ConfigureAwait(false);
            if (existing.Failure is not null)
            {
                return new ManualTransferEnqueueResult(accepted, [], existing.Failure);
            }

            foreach (var entry in existing.Entries)
            {
                var mappedExisting = PaneTransferItem.Create(entry);
                if (mappedExisting.IsFailure)
                {
                    return new ManualTransferEnqueueResult(accepted, [], new StorageFailure(
                        "manual_transfer.destination_manifest_invalid",
                        StorageFailureKind.Integrity,
                        Ui.Validation.TheDestinationReturnedAnInvalidRecursiveEntry));
                }

                if (!destinationEntries.TryAdd(entry.RelativePath, mappedExisting.Value) &&
                    !string.Equals(entry.RelativePath, targetRoot, StringComparison.Ordinal))
                {
                    return new ManualTransferEnqueueResult(accepted, [], new StorageFailure(
                        "manual_transfer.destination_manifest_invalid",
                        StorageFailureKind.Integrity,
                        Ui.Validation.TheDestinationReturnedDuplicatedRecursiveEntries));
                }
            }

            await foreach (var page in ListTreePagesAsync(
                source.Context.ConnectionId!.Value,
                source.Context.RootIdentity!,
                selected.RelativePath,
                allowNotFound: false,
                cancellationToken).ConfigureAwait(false))
            {
                if (page.Failure is not null)
                {
                    return new ManualTransferEnqueueResult(accepted, [], page.Failure);
                }

                foreach (var entry in page.Entries)
                {
                    if (sourceEntryKinds.TryGetValue(entry.RelativePath, out var priorKind))
                    {
                        return new ManualTransferEnqueueResult(accepted, [], new StorageFailure(
                            priorKind != entry.Kind
                                ? "manual_transfer.source_kind_collision"
                                : "manual_transfer.source_manifest_duplicate",
                            StorageFailureKind.Integrity,
                            priorKind != entry.Kind
                                ? Ui.Validation.TheSourceReturnedAFileAndFolder
                                : Ui.Validation.TheSourceReturnedADuplicatedRecursiveEntry));
                    }

                    sourceEntryKinds.Add(entry.RelativePath, entry.Kind);
                    if (!TryGetDescendantSuffix(selected.RelativePath, entry.RelativePath, out var suffix))
                    {
                        return new ManualTransferEnqueueResult(accepted, [], new StorageFailure(
                            "manual_transfer.source_manifest_invalid",
                            StorageFailureKind.Integrity,
                            Ui.Validation.TheSourceReturnedAnEntryOutsideThe));
                    }

                    var targetPath = suffix.Length == 0 ? targetRoot : Combine(targetRoot, suffix);
                    combinedPathCharacters += entry.RelativePath.Length + targetPath.Length;
                    entryCount++;
                    if (entryCount > MaximumManifestEntries ||
                        combinedPathCharacters > MaximumCombinedPathCharacters)
                    {
                        return new ManualTransferEnqueueResult(accepted, [], new StorageFailure(
                            "manual_transfer.manifest_limit_exceeded",
                            StorageFailureKind.Validation,
                            $"A recursive transfer is limited to {MaximumManifestEntries:N0} files and folders."));
                    }

                    if (entry.Kind is StorageItemKind.Directory or StorageItemKind.Prefix)
                    {
                        // Created as it is met rather than in one depth-ordered pass at the end,
                        // because the files inside it are queued before the walk has seen the rest.
                        var madeDirectory = await EnsureDirectoryChainAsync(
                            destination, targetPath, ensuredDirectories, cancellationToken).ConfigureAwait(false);
                        if (madeDirectory is not null)
                        {
                            return new ManualTransferEnqueueResult(accepted, [], madeDirectory);
                        }

                        continue;
                    }

                    if (entry.Kind != StorageItemKind.File || entry.IsContainer)
                    {
                        return new ManualTransferEnqueueResult(accepted, [], new StorageFailure(
                            "manual_transfer.recursive_item_unsupported",
                            StorageFailureKind.Unsupported,
                            Ui.Validation.TheSelectedFolderContainsASymbolicLink));
                    }

                    var mapped = PaneTransferItem.Create(entry);
                    if (mapped.IsFailure)
                    {
                        return new ManualTransferEnqueueResult(accepted, [], mapped.Error);
                    }

                    batch.Add((mapped.Value, targetPath));
                }

                if (batch.Count > 0)
                {
                    var flushed = await FlushAsync(
                        batch, source, destination, operation, targetPaths, ensuredDirectories,
                        destinationEntries, accepted, cancellationToken).ConfigureAwait(false);
                    if (flushed is not null)
                    {
                        return new ManualTransferEnqueueResult(accepted, flushed.AmbiguousTransferIds, flushed.Failure);
                    }

                    batch.Clear();
                }

                progress?.Invoke(accepted.Count, ensuredDirectories.Count);
            }
        }

        return new ManualTransferEnqueueResult(accepted, [], failure: null);
    }

    /// <summary>
    /// Turns one page of files into requests and hands them to the queue. Returns the failing result
    /// to stop on, or null when the page was accepted; what it accepted is appended either way.
    /// </summary>
    private async Task<ManualTransferEnqueueResult?> FlushAsync(
        List<(PaneTransferItem Item, string DestinationPath)> batch,
        PaneSelectionSnapshot source,
        PaneDestinationSnapshot destination,
        TransferQueueOperation operation,
        HashSet<string> targetPaths,
        HashSet<string> ensuredDirectories,
        Dictionary<string, PaneTransferItem> destinationEntries,
        List<TransferEnqueueResponse> accepted,
        CancellationToken cancellationToken)
    {
        var requests = new List<TransferEnqueueRequest>(batch.Count);
        foreach (var (item, destinationPath) in batch)
        {
            if (ensuredDirectories.Contains(destinationPath))
            {
                return new ManualTransferEnqueueResult([], [], new StorageFailure(
                    "manual_transfer.source_kind_collision",
                    StorageFailureKind.Integrity,
                    Ui.Validation.TheRecursiveManifestMappedAFileAnd));
            }

            if (!targetPaths.Add(destinationPath))
            {
                return new ManualTransferEnqueueResult([], [], new StorageFailure(
                    "manual_transfer.destination_duplicate",
                    StorageFailureKind.Validation,
                    Ui.Validation.MoreThanOneSelectedFileMapsTo));
            }

            destinationEntries.TryGetValue(destinationPath, out var existing);
            if (existing is { Kind: not StorageItemKind.File })
            {
                return new ManualTransferEnqueueResult([], [], new StorageFailure(
                    "manual_transfer.destination_container_conflict",
                    StorageFailureKind.Conflict,
                    Ui.Validation.ADestinationFolderOrNonFileItem2));
            }

            if (existing is not null && (!existing.HasStableIdentity || !item.HasStableIdentity))
            {
                return new ManualTransferEnqueueResult([], [], new StorageFailure(
                    "manual_transfer.overwrite_identity_required",
                    StorageFailureKind.Conflict,
                    Ui.Validation.ReplacingAnExistingFileRequiresStableSource));
            }

            var request = new TransferEnqueueRequest(
                TransferQueueIpcContract.CurrentVersion,
                Guid.NewGuid(),
                operation,
                new TransferQueueAddress(
                    source.Context.ConnectionId!.Value,
                    source.Context.RootIdentity!,
                    item.RelativePath,
                    item.NativeItemId,
                    item.VersionId,
                    item.EntityTag),
                new TransferQueueAddress(
                    destination.Context.ConnectionId!.Value,
                    destination.Context.RootIdentity!,
                    destinationPath,
                    existing?.NativeItemId,
                    existing?.VersionId,
                    existing?.EntityTag),
                item.Length,
                TransferQueueVerification.StrongHashWhenAvailable,
                ExpectedDestinationVersionId: existing?.VersionId,
                ExpectedDestinationEntityTag: existing?.EntityTag);
            if (!request.HasValidBounds || IsSameAddress(request.Source, request.Destination))
            {
                return new ManualTransferEnqueueResult([], [], new StorageFailure(
                    "manual_transfer.request_invalid",
                    StorageFailureKind.Validation,
                    Ui.Validation.TheRecursiveManifestProducedAnInvalidOr));
            }

            requests.Add(request);
        }

        var result = await _transfers.EnqueuePlanAsync(
            new ManualTransferPlan(requests),
            cancellationToken).ConfigureAwait(false);
        accepted.AddRange(result.Accepted);
        return result.Failure is null && !result.HasAmbiguity ? null : result;
    }

    /// <summary>
    /// Creates every folder between the destination root and <paramref name="path"/> that has not
    /// been created yet, outermost first. Walking up rather than sorting the whole set by depth is
    /// what lets a folder be created the moment it is met.
    /// </summary>
    private async Task<StorageFailure?> EnsureDirectoryChainAsync(
        PaneDestinationSnapshot destination,
        string path,
        HashSet<string> ensuredDirectories,
        CancellationToken cancellationToken)
    {
        var root = destination.Context.RelativePath;
        if (string.Equals(path, root, StringComparison.Ordinal) || ensuredDirectories.Contains(path))
        {
            return null;
        }

        var pending = new List<string>();
        var current = path;
        while (current.Length > root.Length && !ensuredDirectories.Contains(current))
        {
            pending.Add(current);
            var separator = current.LastIndexOf('/');
            if (separator < 0)
            {
                break;
            }

            current = current[..separator];
        }

        pending.Reverse();
        foreach (var directoryPath in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ensured = await _mutations.EnsureDirectoryAsync(new StorageDirectoryEnsureRequest(
                EditableFileIpcContract.CurrentVersion,
                new ObjectInspectorAddress(
                    destination.Context.ConnectionId!.Value,
                    destination.Context.RootIdentity!,
                    directoryPath)), cancellationToken).ConfigureAwait(false);
            if (ensured.Failure is not null)
            {
                return MapFailure(ensured.Failure);
            }

            ensuredDirectories.Add(directoryPath);
        }

        return null;
    }

    /// <summary>Reads a whole tree into memory, for the destination side where a page is no use.</summary>
    private static async Task<TreeListResult> DrainAsync(IAsyncEnumerable<TreePage> pages)
    {
        var entries = new List<StorageListItem>();
        await foreach (var page in pages.ConfigureAwait(false))
        {
            if (page.Failure is not null)
            {
                return new TreeListResult([], page.Failure);
            }

            entries.AddRange(page.Entries);
        }

        return new TreeListResult(entries, null);
    }

    /// <summary>
    /// The tree, a page at a time. It used to accumulate every entry and return them together,
    /// which meant nothing could be queued until the last page arrived -- minutes, on a tree that
    /// CL.Storage has to walk itself because SFTP has no recursive listing.
    /// </summary>
    private async IAsyncEnumerable<TreePage> ListTreePagesAsync(
        Guid connectionId,
        string expectedRootIdentity,
        string relativePath,
        bool allowNotFound,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var continuationTokens = new HashSet<string>(StringComparer.Ordinal);
        string? continuation = null;
        var total = 0;
        for (var pageNumber = 0; pageNumber < MaximumManifestPages; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StorageListPageResponse? response = null;
            StorageFailure? timedOut = null;
            try
            {
                response = await _storage.ListStorageAsync(new StorageListPageRequest(
                    StorageIpcContract.CurrentVersion,
                    connectionId,
                    relativePath,
                    PageSize: StorageIpcLimits.MaximumStableIdentityPageSize,
                    ContinuationToken: continuation,
                    Recursive: true), cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                timedOut = ListingTimedOut(relativePath, pageNumber + 1);
            }

            if (timedOut is not null)
            {
                yield return TreePage.Fail(timedOut);
                yield break;
            }

            if (response!.Failure is not null)
            {
                if (response.Failure.Category == StorageIpcFailureCategory.Unsupported &&
                    continuation is null)
                {
                    await foreach (var page in ListTreeBreadthFirstPagesAsync(
                        connectionId,
                        expectedRootIdentity,
                        relativePath,
                        allowNotFound,
                        cancellationToken).ConfigureAwait(false))
                    {
                        yield return page;
                    }

                    yield break;
                }

                if (allowNotFound && response.Failure.Category == StorageIpcFailureCategory.NotFound)
                {
                    yield break;
                }

                yield return TreePage.Fail(MapFailure(response.Failure));
                yield break;
            }

            if (!string.Equals(response.RootIdentity, expectedRootIdentity, StringComparison.Ordinal))
            {
                yield return TreePage.Fail(new StorageFailure(
                    "manual_transfer.root_identity_changed",
                    StorageFailureKind.Integrity,
                    Ui.Validation.TheConnectionRootIdentityChangedWhileThe));
                yield break;
            }

            total += response.Entries.Length;
            if (total > MaximumManifestEntries)
            {
                yield return TreePage.Fail(ManifestLimitExceeded());
                yield break;
            }

            // Read before the page is handed on: a provider that repeats a page repeats its
            // entries too, and the consumer would report those as duplicates rather than naming
            // the provider fault that produced them.
            continuation = response.ContinuationToken;
            if (continuation is not null && !continuationTokens.Add(continuation))
            {
                yield return TreePage.Fail(new StorageFailure(
                    "manual_transfer.repeated_page_token",
                    StorageFailureKind.Integrity,
                    Ui.Validation.TheProviderRepeatedARecursiveListingPage));
                yield break;
            }

            if (response.Entries.Length > 0)
            {
                yield return new TreePage(response.Entries, null);
            }

            if (continuation is null)
            {
                yield break;
            }
        }

        yield return TreePage.Fail(new StorageFailure(
            "manual_transfer.page_limit_exceeded",
            StorageFailureKind.Validation,
            Ui.Validation.TheRecursiveListingExceededItsBoundedPage));
    }

    /// <summary>
    /// The same, for a provider that will not list recursively at all: one directory at a time,
    /// breadth first. Each directory's page is yielded as it arrives, so this streams too.
    /// </summary>
    private async IAsyncEnumerable<TreePage> ListTreeBreadthFirstPagesAsync(
        Guid connectionId,
        string expectedRootIdentity,
        string relativePath,
        bool allowNotFound,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var directories = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { relativePath };
        directories.Enqueue(relativePath);
        var pageCount = 0;
        var total = 0;
        while (directories.Count > 0)
        {
            var directory = directories.Dequeue();
            var continuationTokens = new HashSet<string>(StringComparer.Ordinal);
            string? continuation = null;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++pageCount > MaximumManifestPages)
                {
                    yield return TreePage.Fail(new StorageFailure(
                        "manual_transfer.page_limit_exceeded",
                        StorageFailureKind.Validation,
                        Ui.Validation.TheRecursiveListingExceededItsBoundedPage));
                    yield break;
                }

                StorageListPageResponse? response = null;
                StorageFailure? timedOut = null;
                try
                {
                    response = await _storage.ListStorageAsync(new StorageListPageRequest(
                        StorageIpcContract.CurrentVersion,
                        connectionId,
                        directory,
                        PageSize: StorageIpcLimits.MaximumStableIdentityPageSize,
                        ContinuationToken: continuation,
                        Recursive: false), cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    timedOut = ListingTimedOut(directory, pageCount);
                }

                if (timedOut is not null)
                {
                    yield return TreePage.Fail(timedOut);
                    yield break;
                }

                if (response!.Failure is not null)
                {
                    if (allowNotFound && directory == relativePath &&
                        response.Failure.Category == StorageIpcFailureCategory.NotFound)
                    {
                        yield break;
                    }

                    yield return TreePage.Fail(MapFailure(response.Failure));
                    yield break;
                }

                if (!string.Equals(response.RootIdentity, expectedRootIdentity, StringComparison.Ordinal))
                {
                    yield return TreePage.Fail(new StorageFailure(
                        "manual_transfer.root_identity_changed",
                        StorageFailureKind.Integrity,
                        Ui.Validation.TheConnectionRootIdentityChangedWhileThe));
                    yield break;
                }

                foreach (var entry in response.Entries)
                {
                    if (entry.Kind is StorageItemKind.Directory or StorageItemKind.Prefix &&
                        visited.Add(entry.RelativePath))
                    {
                        directories.Enqueue(entry.RelativePath);
                    }
                }

                total += response.Entries.Length;
                if (total > MaximumManifestEntries)
                {
                    yield return TreePage.Fail(ManifestLimitExceeded());
                    yield break;
                }

                continuation = response.ContinuationToken;
                if (continuation is not null && !continuationTokens.Add(continuation))
                {
                    yield return TreePage.Fail(new StorageFailure(
                        "manual_transfer.repeated_page_token",
                        StorageFailureKind.Integrity,
                        Ui.Validation.TheProviderRepeatedARecursiveListingPage));
                    yield break;
                }

                if (response.Entries.Length > 0)
                {
                    yield return new TreePage(response.Entries, null);
                }
            }
            while (continuation is not null);
        }
    }

    private static StorageFailure ManifestLimitExceeded() => new(
        "manual_transfer.manifest_limit_exceeded",
        StorageFailureKind.Validation,
        $"A recursive transfer is limited to {MaximumManifestEntries:N0} files and folders.");

    private static string Combine(string parent, string child) =>
        parent.Length == 0 ? child : $"{parent}/{child}";

    private static bool TryGetDescendantSuffix(string root, string path, out string suffix)
    {
        if (string.Equals(root, path, StringComparison.Ordinal))
        {
            suffix = string.Empty;
            return true;
        }

        var prefix = root.Length == 0 ? string.Empty : root + "/";
        if (path.StartsWith(prefix, StringComparison.Ordinal) && path.Length > prefix.Length)
        {
            suffix = path[prefix.Length..];
            return true;
        }

        suffix = string.Empty;
        return false;
    }

    private static bool IsSameAddress(TransferQueueAddress left, TransferQueueAddress right) =>
        left.ConnectionId == right.ConnectionId &&
        string.Equals(left.RootIdentity, right.RootIdentity, StringComparison.Ordinal) &&
        string.Equals(left.RelativePath, right.RelativePath, StringComparison.Ordinal);

    /// <summary>
    /// A listing that ran out of time. Named separately from a broken pipe because the agent is
    /// alive and working -- it is the tree that is bigger than the budget -- and the path is worth
    /// saying, since it is the one fact that tells somebody which folder to narrow.
    /// </summary>
    private static StorageFailure ListingTimedOut(string relativePath, int page) => new(
        "manual_transfer.listing_timed_out",
        StorageFailureKind.Timeout,
        Ui.Format(Ui.Validation.TheRecursiveListingTimedOutFormat, relativePath, page),
        isTransient: true);

    private static ManualTransferEnqueueResult Failure(string code, string message, bool isTransient = false) =>
        new([], [], new StorageFailure(code, StorageFailureKind.Validation, message, isTransient));

    private static StorageFailure MapFailure(StorageIpcFailure failure) => new(
        failure.Code,
        failure.Category switch
        {
            StorageIpcFailureCategory.Validation => StorageFailureKind.Validation,
            StorageIpcFailureCategory.NotFound => StorageFailureKind.NotFound,
            StorageIpcFailureCategory.Conflict => StorageFailureKind.Conflict,
            StorageIpcFailureCategory.Unsupported => StorageFailureKind.Unsupported,
            StorageIpcFailureCategory.Unauthorized => StorageFailureKind.Unauthorized,
            StorageIpcFailureCategory.Unavailable => StorageFailureKind.Unavailable,
            StorageIpcFailureCategory.Timeout => StorageFailureKind.Timeout,
            StorageIpcFailureCategory.Cancelled => StorageFailureKind.Cancelled,
            StorageIpcFailureCategory.Integrity => StorageFailureKind.Integrity,
            StorageIpcFailureCategory.Security => StorageFailureKind.Security,
            StorageIpcFailureCategory.Provider => StorageFailureKind.Provider,
            _ => StorageFailureKind.Unexpected
        },
        failure.Message,
        failure.IsTransient);

    private sealed record TreeListResult(IReadOnlyList<StorageListItem> Entries, StorageFailure? Failure);

    private sealed record TreePage(IReadOnlyList<StorageListItem> Entries, StorageFailure? Failure)
    {
        internal static TreePage Fail(StorageFailure failure) => new([], failure);
    }
}
