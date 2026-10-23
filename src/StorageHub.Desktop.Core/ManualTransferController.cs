using StorageHub.Contracts.Ipc;
using StorageHub.Contracts.Results;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

public enum PaneTransferContextKind
{
    SavedConnection,
    ThisPc,
    AdHoc,
    ConnectionsHome
}

/// <summary>An immutable pane location captured before a manual transfer is planned.</summary>
public sealed class PaneTransferContext
{
    private PaneTransferContext(
        PaneTransferContextKind kind,
        Guid? connectionId,
        string? rootIdentity,
        string relativePath)
    {
        Kind = kind;
        ConnectionId = connectionId;
        RootIdentity = rootIdentity;
        RelativePath = relativePath;
    }

    public PaneTransferContextKind Kind { get; }
    public Guid? ConnectionId { get; }
    public string? RootIdentity { get; }
    public string RelativePath { get; }

    public static StorageResult<PaneTransferContext> Create(
        PaneTransferContextKind kind,
        Guid? connectionId,
        string? rootIdentity,
        string relativePath)
    {
        if (!Enum.IsDefined(kind))
        {
            return Invalid("manual_transfer.context.kind_invalid", Ui.Validation.ThePaneContextKindIsInvalid);
        }

        if (!IsBoundedPath(relativePath))
        {
            return Invalid(
                "manual_transfer.context.path_invalid",
                Ui.Validation.ThePaneLocationIsEmptyTooLong);
        }

        if (kind == PaneTransferContextKind.SavedConnection)
        {
            if (connectionId is null || connectionId.Value == Guid.Empty || !IsBoundedIdentity(rootIdentity))
            {
                return Invalid(
                    "manual_transfer.context.saved_identity_required",
                    Ui.Validation.ASavedConnectionIDAndVerifiedRoot);
            }

            if (!RemoteBrowserPath.TryNormalize(relativePath, out var normalized, out _) ||
                !string.Equals(normalized, relativePath, StringComparison.Ordinal))
            {
                return Invalid(
                    "manual_transfer.context.path_not_canonical",
                    Ui.Validation.ASavedConnectionPaneMustUseA);
            }
        }
        else if (connectionId is not null || rootIdentity is not null)
        {
            return Invalid(
                "manual_transfer.context.unsaved_identity_invalid",
                Ui.Validation.ThisPCConnectionHomeAndAdHoc);
        }

        if (kind == PaneTransferContextKind.ConnectionsHome && relativePath.Length != 0)
        {
            return Invalid(
                "manual_transfer.context.home_path_invalid",
                Ui.Validation.TheConnectionsHomeContextCannotIdentifyA);
        }

        return StorageResult<PaneTransferContext>.Success(new PaneTransferContext(
            kind,
            connectionId,
            rootIdentity,
            relativePath));
    }

    private static bool IsBoundedPath(string? value) => value is not null &&
        value.Length <= StorageIpcLimits.MaximumRelativePathLength &&
        !value.Any(char.IsControl);

    private static bool IsBoundedIdentity(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= StorageIpcLimits.MaximumOpaqueIdentityLength &&
        !value.Any(char.IsControl);

    private static StorageResult<PaneTransferContext> Invalid(string code, string message) =>
        StorageResult<PaneTransferContext>.Fail(new StorageFailure(
            code,
            StorageFailureKind.Validation,
            message));
}

/// <summary>One immutable item identity captured from the pane's unfiltered provider snapshot.</summary>
public sealed class PaneTransferItem
{
    private PaneTransferItem(
        string name,
        string relativePath,
        StorageItemKind kind,
        long? length,
        string? nativeItemId,
        string? versionId,
        string? entityTag,
        DateTimeOffset? modifiedUtc)
    {
        Name = name;
        RelativePath = relativePath;
        Kind = kind;
        Length = length;
        NativeItemId = nativeItemId;
        VersionId = versionId;
        EntityTag = entityTag;
        ModifiedUtc = modifiedUtc;
    }

    public string Name { get; }
    public string RelativePath { get; }
    public StorageItemKind Kind { get; }
    public long? Length { get; }
    public string? NativeItemId { get; }
    public string? VersionId { get; }
    public string? EntityTag { get; }

    /// <summary>
    /// When the listing says the item last changed, for the "if newer" conflict choice. Not an
    /// identity: nothing is replaced on the strength of a time.
    /// </summary>
    public DateTimeOffset? ModifiedUtc { get; }
    public bool IsContainer => Kind is StorageItemKind.Directory or StorageItemKind.Prefix;
    public bool HasStableIdentity => VersionId is not null || EntityTag is not null;

    public static StorageResult<PaneTransferItem> Create(
        string name,
        string relativePath,
        StorageItemKind kind,
        long? length,
        string? nativeItemId = null,
        string? versionId = null,
        string? entityTag = null,
        DateTimeOffset? modifiedUtc = null)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            name.Length > StorageIpcLimits.MaximumItemNameLength ||
            name.Any(char.IsControl) ||
            name.Contains('/') ||
            name.Contains('\\'))
        {
            return Invalid("manual_transfer.item.name_invalid", Ui.Validation.ASelectedItemHasAnInvalidName);
        }

        if (string.IsNullOrWhiteSpace(relativePath) ||
            relativePath.Length > StorageIpcLimits.MaximumRelativePathLength ||
            relativePath.Any(char.IsControl) ||
            !Enum.IsDefined(kind) ||
            length is < 0 ||
            kind is StorageItemKind.Directory or StorageItemKind.Prefix && length is not null ||
            !IsBoundedIdentity(nativeItemId) ||
            !IsBoundedIdentity(versionId) ||
            !IsBoundedIdentity(entityTag))
        {
            return Invalid(
                "manual_transfer.item.invalid",
                Ui.Validation.ASelectedItemContainsAnInvalidPath);
        }

        return StorageResult<PaneTransferItem>.Success(new PaneTransferItem(
            name,
            relativePath,
            kind,
            length,
            nativeItemId,
            versionId,
            entityTag,
            modifiedUtc));
    }

    public static StorageResult<PaneTransferItem> Create(StorageListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Create(
            item.Name,
            item.RelativePath,
            item.Kind,
            item.Size,
            item.NativeItemId,
            item.VersionId,
            item.EntityTag,
            item.LastModifiedUtc);
    }

    /// <summary>
    /// A file on this PC, named the way the agent names it: by when it was last written and how
    /// long it is (<see cref="LocalTransferFolder.FileIdentity"/>). That is what lets one be
    /// replaced, or be the source of a replacement, under the same check every other overwrite
    /// gets -- the agent looks again and refuses when the file is no longer the one listed.
    /// </summary>
    internal PaneTransferItem WithLocalIdentity() =>
        Kind == StorageItemKind.File && EntityTag is null && VersionId is null &&
        Length is { } length && ModifiedUtc is { } modified
            ? new PaneTransferItem(
                Name,
                RelativePath,
                Kind,
                Length,
                NativeItemId,
                VersionId,
                LocalTransferFolder.FileIdentity(modified, length),
                ModifiedUtc)
            : this;

    private static bool IsBoundedIdentity(string? value) => value is null ||
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= StorageIpcLimits.MaximumOpaqueIdentityLength &&
        !value.Any(char.IsControl);

    private static StorageResult<PaneTransferItem> Invalid(string code, string message) =>
        StorageResult<PaneTransferItem>.Fail(new StorageFailure(
            code,
            StorageFailureKind.Validation,
            message));
}

public sealed class PaneSelectionSnapshot
{
    public const int MaximumSelectedItems = StorageIpcLimits.MaximumStoragePageSize;

    private PaneSelectionSnapshot(
        PaneTransferContext context,
        IReadOnlyList<PaneTransferItem> items)
    {
        Context = context;
        Items = items;
    }

    public PaneTransferContext Context { get; }
    public IReadOnlyList<PaneTransferItem> Items { get; }

    public static StorageResult<PaneSelectionSnapshot> Create(
        PaneTransferContext context,
        IEnumerable<PaneTransferItem> items)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(items);
        var snapshot = items.ToArray();
        if (snapshot.Length is < 1 or > MaximumSelectedItems || snapshot.Any(static item => item is null))
        {
            return Invalid(
                "manual_transfer.selection.count_invalid",
                $"Select between one and {MaximumSelectedItems} items.");
        }

        if (snapshot.Select(static item => item.RelativePath).Distinct(StringComparer.Ordinal).Count() !=
            snapshot.Length ||
            context.Kind == PaneTransferContextKind.SavedConnection &&
            snapshot.Any(item => !IsCanonicalRemotePath(item.RelativePath) ||
                !IsDirectChild(context.RelativePath, item.RelativePath) ||
                !HasExpectedName(item)))
        {
            return Invalid(
                "manual_transfer.selection.invalid",
                Ui.Validation.TheSelectedItemsAreDuplicatedOrDo);
        }

        return StorageResult<PaneSelectionSnapshot>.Success(new PaneSelectionSnapshot(
            context,
            Array.AsReadOnly(snapshot)));
    }

    internal static bool HasExpectedName(PaneTransferItem item)
    {
        var separator = item.RelativePath.LastIndexOf('/');
        return string.Equals(
            item.Name,
            item.RelativePath[(separator + 1)..],
            StringComparison.Ordinal);
    }

    internal static bool IsCanonicalRemotePath(string path) =>
        RemoteBrowserPath.TryNormalize(path, out var normalized, out _) &&
        string.Equals(path, normalized, StringComparison.Ordinal);

    internal static bool IsDirectChild(string parentPath, string itemPath)
    {
        var prefix = parentPath.Length == 0 ? string.Empty : parentPath + "/";
        if (!itemPath.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var remainder = itemPath[prefix.Length..];
        return remainder.Length > 0 && !remainder.Contains('/', StringComparison.Ordinal);
    }

    private static StorageResult<PaneSelectionSnapshot> Invalid(string code, string message) =>
        StorageResult<PaneSelectionSnapshot>.Fail(new StorageFailure(
            code,
            StorageFailureKind.Validation,
            message));
}

public sealed class PaneDestinationSnapshot
{
    // Conflict-review snapshots remain bounded even though the browser's disk-backed index
    // can display much larger folders. A future provider-side conflict query can lift this.
    public const int MaximumVisibleEntries = 10_000;

    private PaneDestinationSnapshot(
        PaneTransferContext context,
        IReadOnlyList<PaneTransferItem> entries)
    {
        Context = context;
        Entries = entries;
    }

    public PaneTransferContext Context { get; }
    public IReadOnlyList<PaneTransferItem> Entries { get; }

    public static StorageResult<PaneDestinationSnapshot> Create(
        PaneTransferContext context,
        IEnumerable<PaneTransferItem> entries)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entries);
        var snapshot = entries.ToArray();
        if (snapshot.Length > MaximumVisibleEntries || snapshot.Any(static item => item is null))
        {
            return Invalid(
                "manual_transfer.destination.count_invalid",
                Ui.Validation.TheDestinationPaneExceedsTheSafeSnapshot);
        }

        if (snapshot.Select(static item => item.RelativePath).Distinct(StringComparer.Ordinal).Count() !=
                snapshot.Length ||
            snapshot.Select(static item => item.Name).Distinct(StringComparer.Ordinal).Count() != snapshot.Length ||
            context.Kind == PaneTransferContextKind.SavedConnection &&
            snapshot.Any(item =>
                !PaneSelectionSnapshot.IsCanonicalRemotePath(item.RelativePath) ||
                !PaneSelectionSnapshot.IsDirectChild(context.RelativePath, item.RelativePath) ||
                !PaneSelectionSnapshot.HasExpectedName(item)))
        {
            return Invalid(
                "manual_transfer.destination.invalid",
                Ui.Validation.TheDestinationEntriesAreDuplicatedOrDo);
        }

        return StorageResult<PaneDestinationSnapshot>.Success(new PaneDestinationSnapshot(
            context,
            Array.AsReadOnly(snapshot)));
    }

    private static StorageResult<PaneDestinationSnapshot> Invalid(string code, string message) =>
        StorageResult<PaneDestinationSnapshot>.Fail(new StorageFailure(
            code,
            StorageFailureKind.Validation,
            message));
}

public sealed class ManualTransferPlan
{
    internal ManualTransferPlan(IReadOnlyList<TransferEnqueueRequest> requests, int skipped = 0, int renamed = 0)
    {
        Requests = requests;
        Skipped = skipped;
        Renamed = renamed;
    }

    public IReadOnlyList<TransferEnqueueRequest> Requests { get; }

    /// <summary>Selected files left out because they were already at the destination.</summary>
    public int Skipped { get; }

    /// <summary>Requests that write under a free name because the selected one was taken.</summary>
    public int Renamed { get; }
}

public sealed class ManualTransferEnqueueResult
{
    internal ManualTransferEnqueueResult(
        IReadOnlyList<TransferEnqueueResponse> accepted,
        IReadOnlyList<Guid> ambiguousTransferIds,
        StorageFailure? failure)
    {
        Accepted = accepted;
        AmbiguousTransferIds = ambiguousTransferIds;
        Failure = failure;
    }

    public IReadOnlyList<TransferEnqueueResponse> Accepted { get; }
    /// <summary>
    /// Stable IDs whose enqueue acknowledgement was lost. Callers can look these exact IDs up;
    /// retrying the same operation must reuse them rather than create replacement IDs.
    /// </summary>
    public IReadOnlyList<Guid> AmbiguousTransferIds { get; }
    public StorageFailure? Failure { get; }

    /// <summary>Files not queued because they were already at the destination and were skipped.</summary>
    public int Skipped { get; internal init; }

    /// <summary>Files queued under a new name because their own was taken at the destination.</summary>
    public int Renamed { get; internal init; }
    public bool HasAmbiguity => AmbiguousTransferIds.Count > 0;
    public bool IsSuccess => Failure is null && !HasAmbiguity;
    public bool IsPartial => Accepted.Count > 0 && (Failure is not null || HasAmbiguity);
}

public sealed class ManualTransfersEnqueuedEventArgs : EventArgs
{
    public ManualTransfersEnqueuedEventArgs(IEnumerable<Guid> transferIds)
        : this(transferIds, [])
    {
    }

    public ManualTransfersEnqueuedEventArgs(
        IEnumerable<Guid> acceptedTransferIds,
        IEnumerable<Guid> ambiguousTransferIds)
    {
        ArgumentNullException.ThrowIfNull(acceptedTransferIds);
        ArgumentNullException.ThrowIfNull(ambiguousTransferIds);
        var accepted = acceptedTransferIds.ToArray();
        var ambiguous = ambiguousTransferIds.ToArray();
        var all = accepted.Concat(ambiguous).Distinct().ToArray();
        if (all.Length == 0 ||
            accepted.Any(static id => id == Guid.Empty) ||
            ambiguous.Any(static id => id == Guid.Empty) ||
            accepted.Length != accepted.Distinct().Count() ||
            ambiguous.Length != ambiguous.Distinct().Count() ||
            all.Length != accepted.Length + ambiguous.Length)
        {
            throw new ArgumentException(
                Ui.Validation.AtLeastOneUniqueValidAcceptedOr);
        }

        AcceptedTransferIds = Array.AsReadOnly(accepted);
        AmbiguousTransferIds = Array.AsReadOnly(ambiguous);
        TransferIds = AcceptedTransferIds;
        RefreshTransferIds = Array.AsReadOnly(all);
    }

    /// <summary>Durably acknowledged transfer IDs retained for existing queue observers.</summary>
    public IReadOnlyList<Guid> TransferIds { get; }
    public IReadOnlyList<Guid> AcceptedTransferIds { get; }
    public IReadOnlyList<Guid> AmbiguousTransferIds { get; }
    /// <summary>All IDs worth querying, including acknowledgements that remain ambiguous.</summary>
    public IReadOnlyList<Guid> RefreshTransferIds { get; }
}

public sealed class ManualTransferEnqueueAmbiguousException : OperationCanceledException
{
    internal ManualTransferEnqueueAmbiguousException(
        IReadOnlyList<Guid> acceptedTransferIds,
        IReadOnlyList<Guid> ambiguousTransferIds,
        CancellationToken cancellationToken)
        : base(
            Ui.Validation.ManualTransferEnqueueWasCancelledAfterAn,
            innerException: null,
            cancellationToken)
    {
        AcceptedTransferIds = acceptedTransferIds;
        AmbiguousTransferIds = ambiguousTransferIds;
    }

    public IReadOnlyList<Guid> AcceptedTransferIds { get; }
    public IReadOnlyList<Guid> AmbiguousTransferIds { get; }
}

/// <summary>Builds and submits bounded, root-fenced transfers from immutable pane snapshots.</summary>
public sealed class ManualTransferController : IAsyncDisposable
{
    private static readonly TimeSpan AmbiguousEnqueueRetryTimeout = TimeSpan.FromSeconds(3);
    private readonly ITransferQueueAgentClient _client;
    private readonly bool _ownsClient;
    private int _disposed;

    public ManualTransferController()
        : this(new NamedPipeTransferQueueAgentClient(), ownsClient: true)
    {
    }

    public ManualTransferController(ITransferQueueAgentClient client, bool ownsClient = false)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _ownsClient = ownsClient;
    }

    /// <summary>Signals queue surfaces after an accepted or acknowledgement-ambiguous request.</summary>
    public event EventHandler<ManualTransfersEnqueuedEventArgs>? TransfersEnqueued;

    /// <param name="conflicts">
    /// What to do with a selected file whose name is already taken at the destination. Replacing
    /// is what a plan did before there was a choice, and what it still does when none is given.
    /// </param>
    public StorageResult<ManualTransferPlan> BuildPlan(
        PaneSelectionSnapshot source,
        PaneDestinationSnapshot destination,
        TransferQueueOperation operation,
        TransferQueueVerification verification = TransferQueueVerification.StrongHashWhenAvailable,
        int priority = 0,
        TransferConflictChoice conflicts = TransferConflictChoice.Overwrite)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (!Enum.IsDefined(operation) ||
            !Enum.IsDefined(verification) ||
            priority is < TransferQueueIpcLimits.MinimumPriority or > TransferQueueIpcLimits.MaximumPriority ||
            !Enum.IsDefined(conflicts))
        {
            return InvalidPlan(
                "manual_transfer.options.invalid",
                Ui.Validation.TheTransferOperationVerificationPolicyOrPriority);
        }

        var endpointFailure = RequireQueueableEndpoints(source.Context, destination.Context);
        if (endpointFailure is not null)
        {
            return StorageResult<ManualTransferPlan>.Fail(endpointFailure);
        }

        var names = NameComparer(destination.Context);
        var destinationByName = ByName(destination.Entries, names);
        var requests = new List<TransferEnqueueRequest>(source.Items.Count);
        var destinationPaths = new HashSet<string>(names);

        // A name a rename may not take: one at the destination, one another selected file keeps,
        // or one an earlier rename in this plan already took.
        var claimedNames = new HashSet<string>(destinationByName.Keys, names);
        claimedNames.UnionWith(source.Items.Select(static item => item.Name));
        var skipped = 0;
        var renamed = 0;
        for (var index = 0; index < source.Items.Count; index++)
        {
            var item = source.Items[index];
            if (item.Kind != StorageItemKind.File || item.IsContainer)
            {
                return InvalidPlan(
                    "manual_transfer.recursion_not_supported",
                    Ui.Validation.ManualTransferCurrentlyAcceptsFilesOnlyDirectory);
            }

            // A file on this PC carries a time-and-length identity so it can be replaced or replace
            // something, but a move deletes its source, and the agent deletes only what it can
            // name by an exact version. Said here, rather than as a failed row in the queue.
            if (operation == TransferQueueOperation.Move && source.Context.Kind == PaneTransferContextKind.ThisPc)
            {
                return InvalidPlan(
                    "manual_transfer.move.local_source_unsupported",
                    Ui.Validation.MovingFromThisPCIsNotSupported);
            }

            if (operation == TransferQueueOperation.Move && !item.HasStableIdentity)
            {
                return InvalidPlan(
                    "manual_transfer.move.source_identity_required",
                    Ui.Validation.MovingAFileRequiresItsCapturedVersion);
            }

            destinationByName.TryGetValue(item.Name, out var existing);
            if (existing is { Kind: not StorageItemKind.File })
            {
                return InvalidPlan(
                    "manual_transfer.destination.container_conflict",
                    Ui.Validation.ADestinationFolderOrNonFileItem);
            }

            var name = item.Name;
            if (existing is not null)
            {
                switch (TransferConflictRules.Decide(conflicts, item, existing))
                {
                    case TransferConflictOutcome.Skip:
                        skipped++;
                        continue;
                    case TransferConflictOutcome.Rename:
                        if (TransferConflictRules.FreeName(item.Name, claimedNames.Contains) is not { } free)
                        {
                            return InvalidPlan(
                                "manual_transfer.rename.no_free_name",
                                Ui.Format(Ui.Validation.NoFreeNameForRenameFormat, item.Name));
                        }

                        claimedNames.Add(free);
                        name = free;
                        existing = null;
                        renamed++;
                        break;
                }
            }

            if (!TryDestinationPath(destination.Context, name, destinationPaths, out var destinationPath))
            {
                return InvalidPlan(
                    "manual_transfer.destination.path_invalid",
                    Ui.Validation.TheSelectedFilesDoNotProduceUnique);
            }

            if (existing is not null && (!existing.HasStableIdentity || !item.HasStableIdentity))
            {
                return InvalidPlan(
                    "manual_transfer.overwrite.identity_required",
                    Ui.Validation.ReplacingAnExistingFileRequiresCapturedSource);
            }

            var sourceAddress = CreateAddress(
                source.Context,
                item.RelativePath,
                item.NativeItemId,
                item.VersionId,
                item.EntityTag);
            var destinationAddress = CreateAddress(
                destination.Context,
                destinationPath,
                existing?.NativeItemId,
                existing?.VersionId,
                existing?.EntityTag);
            var request = new TransferEnqueueRequest(
                TransferQueueIpcContract.CurrentVersion,
                Guid.NewGuid(),
                operation,
                sourceAddress,
                destinationAddress,
                item.Length,
                verification,
                priority,
                existing?.VersionId,
                existing?.EntityTag);
            if (!request.HasValidBounds)
            {
                return StorageResult<ManualTransferPlan>.Fail(new StorageFailure(
                    "manual_transfer.request.invalid",
                    StorageFailureKind.Integrity,
                    Ui.Validation.AValidatedPaneSnapshotProducedAnInvalid));
            }

            if (sourceAddress.ConnectionId == destinationAddress.ConnectionId &&
                string.Equals(sourceAddress.RootIdentity, destinationAddress.RootIdentity, StringComparison.Ordinal) &&
                string.Equals(sourceAddress.RelativePath, destinationAddress.RelativePath, StringComparison.Ordinal))
            {
                return InvalidPlan(
                    "manual_transfer.same_item",
                    Ui.Validation.AFileCannotBeCopiedOrMoved);
            }

            requests.Add(request);
        }

        return StorageResult<ManualTransferPlan>.Success(new ManualTransferPlan(
            requests.AsReadOnly(),
            skipped,
            renamed));
    }

    /// <summary>
    /// Where a file of this name lands. For a local destination the pane's folder is the address
    /// root, so the path is the file name alone; a saved connection's pane path is already
    /// root-relative and the name is appended to it. False when the path is not canonical or
    /// another file in the plan already lands there.
    /// </summary>
    private static bool TryDestinationPath(
        PaneTransferContext destination,
        string name,
        HashSet<string> taken,
        out string path)
    {
        path = destination.Kind == PaneTransferContextKind.ThisPc || destination.RelativePath.Length == 0
            ? name
            : destination.RelativePath + "/" + name;
        return RemoteBrowserPath.TryNormalize(path, out var normalized, out _) &&
            string.Equals(path, normalized, StringComparison.Ordinal) &&
            taken.Add(path);
    }

    /// <param name="conflicts">
    /// What to do with a file already at the destination, asked for only when there is one. Null
    /// replaces, as every transfer did before there was a choice.
    /// </param>
    public async Task<ManualTransferEnqueueResult> EnqueueAsync(
        PaneSelectionSnapshot source,
        PaneDestinationSnapshot destination,
        TransferQueueOperation operation,
        TransferQueueVerification verification = TransferQueueVerification.StrongHashWhenAvailable,
        int priority = 0,
        TransferConflictPolicy? conflicts = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        conflicts ??= TransferConflictPolicy.Replace;
        var choice = TransferConflictChoice.Overwrite;
        if (FirstConflict(source, destination) is { } first)
        {
            if (await conflicts.ResolveAsync(first, cancellationToken).ConfigureAwait(false) is not { } resolved)
            {
                return new ManualTransferEnqueueResult([], [], StoppedAtConflict());
            }

            choice = resolved;
        }

        var plan = BuildPlan(source, destination, operation, verification, priority, choice);
        if (plan.IsFailure)
        {
            return new ManualTransferEnqueueResult([], [], plan.Error);
        }

        conflicts.Count(plan.Value.Skipped, plan.Value.Renamed);
        var result = await EnqueuePlanAsync(plan.Value, cancellationToken).ConfigureAwait(false);
        return new ManualTransferEnqueueResult(result.Accepted, result.AmbiguousTransferIds, result.Failure)
        {
            Skipped = plan.Value.Skipped,
            Renamed = plan.Value.Renamed
        };
    }

    /// <summary>
    /// How names at the destination are told apart: as the destination's file system tells them.
    /// </summary>
    /// <remarks>
    /// A folder on this PC under Windows holds one of Readme.TXT and readme.txt, not both, so the
    /// one arriving is the one already there. Compared exactly, it was not, so nobody was asked
    /// whether to replace it. A server's names are compared exactly, as the server compares them.
    /// </remarks>
    internal static StringComparer NameComparer(PaneTransferContext destination) =>
        destination.Kind == PaneTransferContextKind.ThisPc && OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    /// <summary>
    /// Refuses a selection with a folder that would land in itself or in a folder inside it.
    /// </summary>
    /// <remarks>
    /// The folder would be read while its copy was being written into it, and on a server read
    /// as it is walked the copies would be read too and copied again, a level deeper each time.
    /// A drag onto a pane's own folder was the only way in that was stopped; a paste, and a drop
    /// from another pane on the same connection, went ahead.
    /// </remarks>
    internal static StorageFailure? RequireNotIntoItself(PaneSelectionSnapshot source, PaneTransferContext destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (source.Context.Kind != destination.Kind ||
            source.Context.ConnectionId != destination.ConnectionId ||
            !string.Equals(source.Context.RootIdentity, destination.RootIdentity, StringComparison.Ordinal))
        {
            return null;
        }

        var comparison = destination.Kind == PaneTransferContextKind.ThisPc && OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        static string Norm(string path) => path.Replace('\\', '/').Trim('/');

        var target = Norm(destination.RelativePath);
        foreach (var item in source.Items.Where(static item => item.IsContainer))
        {
            var folder = Norm(item.RelativePath);
            if (folder.Length == 0) continue;
            if (string.Equals(target, folder, comparison) || target.StartsWith(folder + "/", comparison))
            {
                return new StorageFailure(
                    "manual_transfer.folder_into_itself",
                    StorageFailureKind.Validation,
                    Ui.Validation.AFolderCannotBeCopiedIntoItself);
            }
        }

        return null;
    }

    /// <summary>
    /// The destination's entries by name, the first of any two the comparer cannot tell apart: a
    /// Windows folder made case-sensitive can hold both Readme.TXT and readme.txt.
    /// </summary>
    private static Dictionary<string, PaneTransferItem> ByName(IEnumerable<PaneTransferItem> entries, StringComparer names)
    {
        var byName = new Dictionary<string, PaneTransferItem>(names);
        foreach (var entry in entries) byName.TryAdd(entry.Name, entry);
        return byName;
    }

    /// <summary>The first selected file whose name a file at the destination already has.</summary>
    internal static PaneTransferItem? FirstConflict(PaneSelectionSnapshot source, PaneDestinationSnapshot destination)
    {
        var files = ByName(
            destination.Entries.Where(static entry => entry.Kind == StorageItemKind.File),
            NameComparer(destination.Context));
        return source.Items
            .Where(static item => item.Kind == StorageItemKind.File)
            .Select(item => files.GetValueOrDefault(item.Name))
            .FirstOrDefault(static existing => existing is not null);
    }

    /// <summary>What a transfer stopped at the conflict question reports: a cancellation.</summary>
    internal static StorageFailure StoppedAtConflict() => new(
        "manual_transfer.conflict_cancelled",
        StorageFailureKind.Cancelled,
        Ui.Validation.TransferStoppedAtExistingFile);

    internal async Task<ManualTransferEnqueueResult> EnqueuePlanAsync(
        ManualTransferPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var accepted = new List<TransferEnqueueResponse>(plan.Requests.Count);
        TransferEnqueueRequest? attemptedRequest = null;
        try
        {
            foreach (var request in plan.Requests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attemptedRequest = request;
                TransferEnqueueResponse response;
                var retried = false;
                try
                {
                    response = await _client.EnqueueAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception error) when (IsRecoverableTransportFailure(error))
                {
                    retried = true;
                    var recovered = await RetryAmbiguousEnqueueOnceAsync(
                        request,
                        cancellationToken).ConfigureAwait(false);
                    if (recovered is null)
                    {
                        return Complete(
                            accepted,
                            [request.TransferId],
                            AmbiguousEnqueueFailure());
                    }

                    response = recovered;
                }

                var responseFailure = ValidateResponse(request, response);
                if (responseFailure?.Code == "manual_transfer.agent_response_invalid" && !retried)
                {
                    var recovered = await RetryAmbiguousEnqueueOnceAsync(
                        request,
                        cancellationToken).ConfigureAwait(false);
                    if (recovered is null)
                    {
                        return Complete(accepted, [request.TransferId], AmbiguousEnqueueFailure());
                    }

                    response = recovered;
                    responseFailure = ValidateResponse(request, response);
                }

                if (responseFailure?.Code == "manual_transfer.agent_response_invalid")
                {
                    return Complete(accepted, [request.TransferId], AmbiguousEnqueueFailure());
                }

                attemptedRequest = null;
                if (responseFailure is not null)
                {
                    return Complete(accepted, [], responseFailure);
                }

                accepted.Add(response);
            }

            return Complete(accepted, [], failure: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var acceptedIds = accepted.Select(static response => response.TransferId).ToArray();
            var ambiguousIds = attemptedRequest is null ? [] : new[] { attemptedRequest.TransferId };
            SignalQueueRefresh(acceptedIds, ambiguousIds);
            if (ambiguousIds.Length > 0)
            {
                throw new ManualTransferEnqueueAmbiguousException(
                    Array.AsReadOnly(acceptedIds),
                    Array.AsReadOnly(ambiguousIds),
                    cancellationToken);
            }

            throw;
        }
        catch (Exception error) when (
            IsRecoverableTransportFailure(error))
        {
            return Complete(
                accepted,
                attemptedRequest is null ? [] : [attemptedRequest.TransferId],
                new StorageFailure(
                    attemptedRequest is null
                        ? "manual_transfer.agent_unavailable"
                        : "manual_transfer.enqueue_ambiguous",
                    StorageFailureKind.Unavailable,
                    attemptedRequest is null
                        ? Ui.Validation.TheBackgroundAgentCouldNotEnqueueThe
                        : Ui.Validation.TheBackgroundAgentDidNotConfirmWhether,
                    isTransient: true));
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
            await _client.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A queued transfer moves between a saved connection and either another saved connection or a
    /// folder on this PC. A local folder may be one side but not both: a local-to-local copy has
    /// no atomic create-if-absent guarantee to build overwrite safety on, so it is refused here
    /// rather than approximated. The agent enforces the same rule, because a local path arrives
    /// from the UI rather than from a profile it already trusts.
    /// </summary>
    internal static StorageFailure? RequireQueueableEndpoints(
        PaneTransferContext source,
        PaneTransferContext destination)
    {
        var sourceFailure = RequireQueueableEndpoint(source, source: true);
        if (sourceFailure is not null)
        {
            return sourceFailure;
        }

        var destinationFailure = RequireQueueableEndpoint(destination, source: false);
        if (destinationFailure is not null)
        {
            return destinationFailure;
        }

        if (source.Kind == PaneTransferContextKind.ThisPc &&
            destination.Kind == PaneTransferContextKind.ThisPc)
        {
            return new StorageFailure(
                "manual_transfer.local_to_local_unsupported",
                StorageFailureKind.Validation,
                Ui.Validation.QueueTransfersMoveBetweenThisPCAnd);
        }

        return null;
    }

    private static StorageFailure? RequireQueueableEndpoint(PaneTransferContext context, bool source)
    {
        if (context.Kind == PaneTransferContextKind.SavedConnection &&
            context.ConnectionId is { } connectionId && connectionId != Guid.Empty &&
            !string.IsNullOrWhiteSpace(context.RootIdentity))
        {
            return null;
        }

        if (context.Kind == PaneTransferContextKind.ThisPc &&
            !string.IsNullOrWhiteSpace(context.RelativePath) &&
            Path.IsPathFullyQualified(context.RelativePath))
        {
            return null;
        }

        return new StorageFailure(
            source
                ? "manual_transfer.source.queueable_endpoint_required"
                : "manual_transfer.destination.queueable_endpoint_required",
            StorageFailureKind.Validation,
            source
                ? Ui.Validation.QueueTransfersRequireTheSourcePaneTo
                : Ui.Validation.QueueTransfersRequireTheDestinationPaneTo);
    }

    /// <summary>
    /// Builds the wire address for one side. A This PC pane has no saved profile, so its folder is
    /// carried in the root identity and its id derived from that folder.
    /// </summary>
    internal static TransferQueueAddress CreateAddress(
        PaneTransferContext context,
        string relativePath,
        string? nativeItemId,
        string? versionId,
        string? entityTag)
    {
        if (context.Kind != PaneTransferContextKind.ThisPc)
        {
            return new TransferQueueAddress(
                context.ConnectionId!.Value,
                context.RootIdentity!,
                relativePath,
                nativeItemId,
                versionId,
                entityTag);
        }

        // A This PC pane reports absolute paths, and an address is root-relative, so the item's
        // path is expressed against the pane's folder. A source item arrives as a full path; a
        // destination path was already built from the folder plus the file name.
        var folder = context.RelativePath;
        var relative = Path.IsPathFullyQualified(relativePath)
            ? Path.GetRelativePath(folder, relativePath).Replace(Path.DirectorySeparatorChar, '/')
            : relativePath;
        return new TransferQueueAddress(
            LocalTransferFolder.CreateConnectionId(folder),
            LocalTransferFolder.CreateRootIdentity(folder),
            relative,
            nativeItemId,
            versionId,
            entityTag);
    }

    private ManualTransferEnqueueResult Complete(
        IReadOnlyCollection<TransferEnqueueResponse> accepted,
        IReadOnlyCollection<Guid> ambiguousTransferIds,
        StorageFailure? failure)
    {
        var snapshot = Array.AsReadOnly(accepted.ToArray());
        var ambiguousIds = ambiguousTransferIds.ToArray();
        var ambiguousSnapshot = Array.AsReadOnly(ambiguousIds);
        SignalQueueRefresh(
            snapshot.Select(static response => response.TransferId).ToArray(),
            ambiguousIds);

        return new ManualTransferEnqueueResult(snapshot, ambiguousSnapshot, failure);
    }

    private void SignalQueueRefresh(
        Guid[] acceptedTransferIds,
        Guid[] ambiguousTransferIds)
    {
        if (acceptedTransferIds.Length == 0 && ambiguousTransferIds.Length == 0)
        {
            return;
        }

        var handlers = TransfersEnqueued;
        if (handlers is null)
        {
            return;
        }

        var args = new ManualTransfersEnqueuedEventArgs(acceptedTransferIds, ambiguousTransferIds);
        foreach (EventHandler<ManualTransfersEnqueuedEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch (Exception)
            {
                // Queue notifications are advisory. A disposed UI observer or extension must
                // never turn a durable agent acceptance into an apparent enqueue failure.
            }
        }
    }

    private async Task<TransferEnqueueResponse?> RetryAmbiguousEnqueueOnceAsync(
        TransferEnqueueRequest request,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(AmbiguousEnqueueRetryTimeout);
        try
        {
            // TransferId is the queue's idempotency key. Reusing the exact request can recover a
            // lost acknowledgement without creating a second hidden transfer identity.
            return await _client.EnqueueAsync(request, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception error) when (IsRecoverableTransportFailure(error))
        {
            return null;
        }
    }

    private static bool IsRecoverableTransportFailure(Exception error) => error is
        IOException or
        InvalidDataException or
        InvalidOperationException or
        TimeoutException or
        UnauthorizedAccessException;

    private static StorageFailure AmbiguousEnqueueFailure() => new(
        "manual_transfer.enqueue_ambiguous",
        StorageFailureKind.Unavailable,
        Ui.Validation.TheBackgroundAgentDidNotConfirmWhether2 +
        Ui.Validation.RefreshTheQueueUsingTheReturnedTransfer,
        isTransient: true);

    private static StorageFailure? ValidateResponse(
        TransferEnqueueRequest request,
        TransferEnqueueResponse response)
    {
        if (response is null ||
            response.ContractVersion != TransferQueueIpcContract.CurrentVersion ||
            response.TransferId != request.TransferId ||
            response.AlreadyExisted && !response.Accepted ||
            response.Accepted &&
                (response.Transfer is null ||
                 response.Failure is not null ||
                 response.Transfer.TransferId != request.TransferId ||
                 response.Transfer.Operation != request.Operation ||
                 response.Transfer.SourceConnectionId != request.Source.ConnectionId ||
                 !string.Equals(
                     response.Transfer.SourcePath,
                     request.Source.RelativePath,
                     StringComparison.Ordinal) ||
                 response.Transfer.DestinationConnectionId != request.Destination.ConnectionId ||
                 !string.Equals(
                     response.Transfer.DestinationPath,
                     request.Destination.RelativePath,
                     StringComparison.Ordinal)) ||
            !response.Accepted && (response.Failure is null || response.Transfer is not null))
        {
            return new StorageFailure(
                "manual_transfer.agent_response_invalid",
                StorageFailureKind.Integrity,
                Ui.Validation.TheBackgroundAgentReturnedAnInvalidEnqueue);
        }

        if (response.Accepted)
        {
            return null;
        }

        return new StorageFailure(
            response.Failure!.Code,
            MapFailureKind(response.Failure.Category),
            response.Failure.Message,
            response.Failure.IsTransient,
            response.Failure.Code);
    }

    private static StorageFailureKind MapFailureKind(StorageIpcFailureCategory category) => category switch
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
    };

    private static StorageResult<ManualTransferPlan> InvalidPlan(string code, string message) =>
        StorageResult<ManualTransferPlan>.Fail(new StorageFailure(
            code,
            StorageFailureKind.Validation,
            message));
}
