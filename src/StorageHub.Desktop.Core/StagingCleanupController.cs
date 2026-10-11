using StorageHub.Contracts.Ipc;
using StorageHub.Contracts.Results;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>What a leftover is, as the maintenance window names it.</summary>
public enum LeftoverKind
{
    ResumePartial,
    TransferStaging,
    WriteProbe,
    RenameTemporary,

    /// <summary>A folder the external editor downloaded a file into, for a file no longer being edited.</summary>
    EditSession
}

/// <summary>One leftover, from the agent or from this desktop's own temporary folders.</summary>
/// <param name="Address">Where the agent found it; null for one of this desktop's own.</param>
/// <param name="LocalDirectory">The editor session folder; null for one the agent found.</param>
/// <param name="InUse">Whether something may still need it, which leaves it where it is.</param>
public sealed record LeftoverItem(
    LeftoverKind Kind,
    string Name,
    string Folder,
    long Size,
    DateTimeOffset? LastModifiedUtc,
    bool InUse,
    TransferQueueAddress? Address = null,
    string? LocalDirectory = null);

/// <summary>Everything one search found.</summary>
/// <param name="AgentFailure">Why the agent could not be asked, when it could not; this desktop's own leftovers are still listed.</param>
public sealed record LeftoverScan(
    IReadOnlyList<LeftoverItem> Items,
    int FoldersUnreachable,
    bool Truncated,
    bool SyncRunning,
    StorageFailure? AgentFailure);

/// <summary>What a removal came to.</summary>
/// <param name="Kept">Each leftover that was left, with why.</param>
public sealed record LeftoverRemoval(
    int Removed,
    long BytesRemoved,
    IReadOnlyList<(LeftoverItem Item, string Reason)> Kept);

/// <summary>
/// Clean up leftover files: the working files transfers left beside their destinations, which the
/// agent finds, and the external editor's downloads for files no longer open, which are this
/// desktop's own.
/// </summary>
/// <remarks>
/// The editor's are found here rather than by the agent because only this process knows which are
/// still open: a session folder is the editor's working copy until the session closes, whatever its
/// age. They are cleared on their own after a day; this is for having the space back sooner.
/// </remarks>
public sealed class StagingCleanupController : IAsyncDisposable
{
    private readonly IStagingCleanupAgentClient _agent;
    private readonly bool _ownsClient;
    private readonly string _editRoot;
    private readonly Func<IReadOnlyCollection<string>> _openEditDirectories;

    public StagingCleanupController(Func<IReadOnlyCollection<string>> openEditDirectories)
        : this(new NamedPipeStagingCleanupAgentClient(), openEditDirectories, ownsClient: true)
    {
    }

    public StagingCleanupController(
        IStagingCleanupAgentClient agent,
        Func<IReadOnlyCollection<string>> openEditDirectories,
        bool ownsClient = false,
        string? editRoot = null)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _openEditDirectories = openEditDirectories ?? throw new ArgumentNullException(nameof(openEditDirectories));
        _ownsClient = ownsClient;
        _editRoot = editRoot ?? EditSessionDirectory.DefaultRoot();
    }

    /// <summary>Looks for leftovers, on saved connections too when <paramref name="includeRemote"/> says so.</summary>
    public async Task<LeftoverScan> ScanAsync(bool includeRemote, CancellationToken cancellationToken = default)
    {
        var items = new List<LeftoverItem>();
        StorageFailure? failure = null;
        StagingCleanupScanResponse? scanned = null;
        try
        {
            scanned = await _agent.ScanAsync(
                new StagingCleanupScanRequest(StagingCleanupIpcContract.CurrentVersion, includeRemote),
                cancellationToken).ConfigureAwait(false);
            items.AddRange(scanned.Items.Select(static item => new LeftoverItem(
                (LeftoverKind)item.Kind,
                item.Name,
                item.Folder,
                item.Size ?? 0,
                item.LastModifiedUtc,
                item.InUse,
                item.Address)));
        }
        catch (Exception error) when (IsAgentFailure(error))
        {
            failure = new StorageFailure(
                "cleanup.agent_unavailable",
                StorageFailureKind.Unavailable,
                Ui.Validation.TheBackgroundAgentCouldNotLookForLeftovers,
                isTransient: true);
        }

        items.AddRange(EditSessions());
        return new LeftoverScan(
            items.AsReadOnly(),
            scanned?.FoldersUnreachable ?? 0,
            scanned?.Truncated ?? false,
            scanned?.SyncRunning ?? false,
            failure);
    }

    /// <summary>Removes each of <paramref name="items"/> that nothing is using, and says why any was left.</summary>
    public async Task<LeftoverRemoval> RemoveAsync(
        IEnumerable<LeftoverItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        var chosen = items.Where(static item => item is not null).ToArray();
        var removed = 0;
        long bytes = 0;
        var kept = new List<(LeftoverItem, string)>();

        foreach (var item in chosen.Where(static item => item.InUse))
        {
            kept.Add((item, Ui.Validation.ALeftoverStillInUseWasLeft));
        }

        foreach (var item in chosen.Where(static item => !item.InUse && item.LocalDirectory is not null))
        {
            if (RemoveEditSession(item.LocalDirectory!) is { } reason)
            {
                kept.Add((item, reason));
                continue;
            }

            removed++;
            bytes += item.Size;
        }

        var forAgent = chosen.Where(static item => !item.InUse && item.Address is not null).ToArray();
        foreach (var batch in forAgent.Chunk(StagingCleanupIpcLimits.MaximumItems))
        {
            StagingCleanupRemoveResponse response;
            try
            {
                response = await _agent.RemoveAsync(
                    new StagingCleanupRemoveRequest(
                        StagingCleanupIpcContract.CurrentVersion,
                        [.. batch.Select(static item => item.Address!)]),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (IsAgentFailure(error))
            {
                kept.AddRange(batch.Select(static item => (item, Ui.Validation.TheBackgroundAgentCouldNotLookForLeftovers)));
                continue;
            }

            // The agent answers in the order it was asked.
            for (var index = 0; index < batch.Length; index++)
            {
                var result = response.Results[index];
                if (result.Removed)
                {
                    removed++;
                    bytes += result.Size ?? batch[index].Size;
                }
                else
                {
                    kept.Add((batch[index], result.Failure?.Message ?? Ui.Validation.ALeftoverStillInUseWasLeft));
                }
            }
        }

        return new LeftoverRemoval(removed, bytes, kept.AsReadOnly());
    }

    /// <summary>The editor's session folders, each with what is in it, the open ones marked in use.</summary>
    private List<LeftoverItem> EditSessions()
    {
        var found = new List<LeftoverItem>();
        try
        {
            var root = new DirectoryInfo(_editRoot);
            if (!root.Exists || root.LinkTarget is not null)
            {
                return found;
            }

            var open = new HashSet<string>(_openEditDirectories().Select(Normalize), PathComparer);
            foreach (var directory in root.EnumerateDirectories())
            {
                if (directory.LinkTarget is not null)
                {
                    continue;
                }

                var files = directory.EnumerateFiles("*", SearchOption.AllDirectories)
                    .Where(static file => file.LinkTarget is null)
                    .ToArray();
                found.Add(new LeftoverItem(
                    LeftoverKind.EditSession,
                    files.Length == 1 ? files[0].Name : directory.Name,
                    directory.FullName,
                    files.Sum(static file => file.Length),
                    new DateTimeOffset(directory.LastWriteTimeUtc, TimeSpan.Zero),
                    open.Contains(Normalize(directory.FullName)),
                    LocalDirectory: directory.FullName));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A temporary folder that cannot be read has nothing to offer here.
        }

        return found;
    }

    /// <summary>
    /// Removes one editor session folder, if it is still one -- directly in the session root, not a
    /// link -- and is not open again. Null when it is gone; otherwise why it was left.
    /// </summary>
    private string? RemoveEditSession(string path)
    {
        try
        {
            var directory = new DirectoryInfo(path);
            if (!string.Equals(Normalize(directory.Parent?.FullName ?? string.Empty), Normalize(_editRoot), PathComparison) ||
                directory.LinkTarget is not null)
            {
                return Ui.Validation.ALeftoverStillInUseWasLeft;
            }

            if (_openEditDirectories().Any(open => string.Equals(Normalize(open), Normalize(path), PathComparison)))
            {
                return Ui.Validation.ALeftoverStillInUseWasLeft;
            }

            if (directory.Exists) directory.Delete(recursive: true);
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Ui.Validation.ALeftoverCouldNotBeRemoved;
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsAgentFailure(Exception error) =>
        error is IOException or TimeoutException or InvalidDataException or InvalidOperationException or ObjectDisposedException;

    public async ValueTask DisposeAsync()
    {
        if (_ownsClient)
        {
            await _agent.DisposeAsync().ConfigureAwait(false);
        }
    }
}
