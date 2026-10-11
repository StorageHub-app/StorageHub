using System.Text.RegularExpressions;
using StorageHub.Contracts.Ipc;
using StorageHub.Contracts.Results;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>One file or folder whose name matched.</summary>
/// <param name="Folder">
/// The folder it is in, as the pane navigates to it: a path under the connection's root, or a
/// full local path on this computer.
/// </param>
internal sealed record FileSearchHit(
    string Name,
    string Folder,
    StorageItemKind Kind,
    long? Length,
    DateTimeOffset? ModifiedUtc)
{
    public bool IsContainer => Kind is StorageItemKind.Directory or StorageItemKind.Prefix;
}

/// <summary>What a search found, how much it looked through, and why it stopped early if it did.</summary>
/// <param name="Truncated">The walk reached one of its limits before it had looked everywhere.</param>
internal sealed record FileSearchResult(
    IReadOnlyList<FileSearchHit> Hits,
    int Scanned,
    bool Truncated,
    StorageFailure? Failure = null);

/// <summary>
/// Tools, Search: every name under the active pane's folder that matches, at any depth.
/// </summary>
/// <remarks>
/// <para>
/// 1.4 declared Search ("Search within the focused endpoint") with Ctrl+F and a toolbar button, and
/// never wired either; the pane's filter box, which narrows the folder shown, was all it had. This
/// is the search the entry described: the folder and everything under it.
/// </para>
/// <para>
/// A folder on this computer is walked here; one on a connection through the agent, with the same
/// recursive listing a folder transfer reads, and one folder at a time where a provider cannot list
/// recursively. Either is bounded, by entries looked at and by matches kept, because a search
/// started at the root of a large bucket would otherwise run until somebody gave up on it; the
/// result says when a limit was reached rather than passing a partial answer off as the whole.
/// </para>
/// </remarks>
internal sealed class FileSearch
{
    /// <summary>The most entries one search looks through.</summary>
    internal const int MaximumScanned = 50_000;

    /// <summary>The most matches one search keeps.</summary>
    internal const int MaximumHits = 1_000;

    /// <summary>The most listing pages one search asks the agent for.</summary>
    private const int MaximumPages = 2_000;

    private readonly Func<IRemoteStorageAgentClient> _connect;

    internal FileSearch(Func<IRemoteStorageAgentClient> connect)
    {
        _connect = connect ?? throw new ArgumentNullException(nameof(connect));
    }

    /// <summary>Whether a pane at this location has a folder to search.</summary>
    internal static bool CanSearch(PaneTransferContext? where) => where is
        { Kind: PaneTransferContextKind.SavedConnection } or
        { Kind: PaneTransferContextKind.ThisPc, RelativePath.Length: > 0 };

    /// <summary>
    /// The test a name is put to: a pattern with <c>*</c> or <c>?</c> is a wildcard over the whole
    /// name, as a file dialog's is, and anything else is found anywhere in it. Neither minds case.
    /// </summary>
    internal static Func<string, bool> Matcher(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var trimmed = pattern.Trim();
        if (trimmed.IndexOfAny(['*', '?']) < 0)
        {
            return name => name.Contains(trimmed, StringComparison.OrdinalIgnoreCase);
        }

        var expression = new Regex(
            "^" + Regex.Escape(trimmed).Replace(@"\*", ".*", StringComparison.Ordinal).Replace(@"\?", ".", StringComparison.Ordinal) + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline,
            TimeSpan.FromSeconds(1));
        return expression.IsMatch;
    }

    /// <param name="progress">Told how many entries have been looked through, after each page or folder.</param>
    internal async Task<FileSearchResult> SearchAsync(
        PaneTransferContext where,
        string pattern,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(where);
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return new FileSearchResult([], 0, Truncated: false, Failure(Ui.Shell.SearchNeedsAName));
        }

        if (!CanSearch(where))
        {
            return new FileSearchResult([], 0, Truncated: false, Failure(Ui.Shell.SearchNeedsAFolder));
        }

        var matches = Matcher(pattern);
        return where.Kind == PaneTransferContextKind.ThisPc
            ? await Task.Run(() => SearchLocal(where.RelativePath, matches, progress, cancellationToken), cancellationToken)
                .ConfigureAwait(false)
            : await SearchConnectionAsync(where, matches, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A folder on this computer, walked without following links, skipping what cannot be read, as
    /// the pane itself lists it.
    /// </summary>
    private static FileSearchResult SearchLocal(
        string root,
        Func<string, bool> matches,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var hits = new List<FileSearchHit>();
        var scanned = 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false,
        };

        try
        {
            foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos("*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++scanned % 500 == 0) progress?.Report(scanned);
                if (matches(entry.Name))
                {
                    var file = entry as FileInfo;
                    hits.Add(new FileSearchHit(
                        entry.Name,
                        Path.GetDirectoryName(entry.FullName) ?? root,
                        file is null ? StorageItemKind.Directory : StorageItemKind.File,
                        file?.Length,
                        entry.LastWriteTimeUtc));
                    if (hits.Count >= MaximumHits) return new FileSearchResult(hits, scanned, Truncated: true);
                }

                if (scanned >= MaximumScanned) return new FileSearchResult(hits, scanned, Truncated: true);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new FileSearchResult(hits, scanned, Truncated: true, Failure(error.Message));
        }

        progress?.Report(scanned);
        return new FileSearchResult(hits, scanned, Truncated: false);
    }

    /// <summary>A folder on a connection, read through the agent.</summary>
    private async Task<FileSearchResult> SearchConnectionAsync(
        PaneTransferContext where,
        Func<string, bool> matches,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var hits = new List<FileSearchHit>();
        var scanned = 0;
        var pages = 0;
        var client = _connect();
        try
        {
            // The whole tree in one listing where the provider can; one folder at a time where it
            // says it cannot, which is what the first page's Unsupported means.
            var folders = new Queue<string>();
            folders.Enqueue(where.RelativePath);
            var recursive = true;
            var visited = new HashSet<string>(StringComparer.Ordinal) { where.RelativePath };
            while (folders.Count > 0)
            {
                var folder = folders.Dequeue();
                string? continuation = null;
                var tokens = new HashSet<string>(StringComparer.Ordinal);
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++pages > MaximumPages) return new FileSearchResult(hits, scanned, Truncated: true);

                    var page = await client.ListStorageAsync(
                        new StorageListPageRequest(
                            StorageIpcContract.CurrentVersion,
                            where.ConnectionId!.Value,
                            folder,
                            ContinuationToken: continuation,
                            Recursive: recursive),
                        cancellationToken).ConfigureAwait(false);
                    if (page.Failure is { } refused)
                    {
                        if (recursive && continuation is null &&
                            refused.Category == StorageIpcFailureCategory.Unsupported)
                        {
                            recursive = false;
                            folders.Enqueue(folder);
                            break;
                        }

                        return new FileSearchResult(hits, scanned, Truncated: true, Failure(refused.Message));
                    }

                    foreach (var entry in page.Entries)
                    {
                        // A recursive listing names the folder itself among its entries on some
                        // providers; it is where the search started, not something it found.
                        if (string.Equals(entry.RelativePath, where.RelativePath, StringComparison.Ordinal)) continue;

                        scanned++;
                        if (!recursive && entry.IsContainer && visited.Add(entry.RelativePath))
                        {
                            folders.Enqueue(entry.RelativePath);
                        }

                        if (matches(entry.Name))
                        {
                            hits.Add(new FileSearchHit(
                                entry.Name,
                                ParentOf(entry.RelativePath),
                                entry.Kind,
                                entry.IsContainer ? null : entry.Size,
                                entry.LastModifiedUtc));
                            if (hits.Count >= MaximumHits) return new FileSearchResult(hits, scanned, Truncated: true);
                        }

                        if (scanned >= MaximumScanned) return new FileSearchResult(hits, scanned, Truncated: true);
                    }

                    progress?.Report(scanned);
                    continuation = page.ContinuationToken;

                    // A provider that hands back a page it already gave would loop for ever.
                    if (continuation is not null && !tokens.Add(continuation))
                    {
                        return new FileSearchResult(hits, scanned, Truncated: true);
                    }
                }
                while (continuation is not null);
            }
        }
        catch (TimeoutException)
        {
            return new FileSearchResult(hits, scanned, Truncated: true, Failure(Ui.Shell.SearchTimedOut));
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }

        return new FileSearchResult(hits, scanned, Truncated: false);
    }

    /// <summary>The folder a root-relative path is in: "" for one at the root.</summary>
    internal static string ParentOf(string relativePath)
    {
        var trimmed = relativePath.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash <= 0 ? string.Empty : trimmed[..slash];
    }

    private static StorageFailure Failure(string message) =>
        new("tools.search.failed", StorageFailureKind.Validation, message);
}
