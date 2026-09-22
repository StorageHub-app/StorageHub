using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>A folder the sync location picker lists.</summary>
internal sealed record SyncLocationFolder(string Name, string RelativePath);

/// <summary>
/// Browses the folders of one saved connection, for choosing where a sync location starts.
/// </summary>
/// <remarks>
/// <para>
/// This was the body of 1.x's SyncLocationPickerForm, where it could only be reached by showing
/// the Form. Here it is the state a picker window draws: where it is, what it has listed, whether
/// there is more, and one sentence about it.
/// </para>
/// <para>
/// Only a folder that has actually been listed can be chosen (<see cref="HasLoaded"/>), so a typed
/// path that turns out not to exist is refused here rather than by the scan that would have run
/// against it.
/// </para>
/// </remarks>
internal sealed class SyncLocationBrowser
{
    /// <summary>
    /// How many folders are listed before the picker asks for a narrower path. A folder with more
    /// subfolders than this is not one anybody scrolls through to pick from.
    /// </summary>
    internal const int MaximumFolders = 2_000;

    private readonly Func<IRemoteStorageAgentClient> _clients;
    private readonly Guid _connectionId;
    private readonly List<SyncLocationFolder> _folders = [];
    private string? _continuation;

    internal SyncLocationBrowser(Func<IRemoteStorageAgentClient> clients, Guid connectionId)
    {
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _connectionId = connectionId;
    }

    /// <summary>The connection-relative folder being shown; empty for the connection root.</summary>
    internal string CurrentPath { get; private set; } = string.Empty;

    internal IReadOnlyList<SyncLocationFolder> Folders => _folders;

    /// <summary>Whether <see cref="CurrentPath"/> was listed, and so can be chosen.</summary>
    internal bool HasLoaded { get; private set; }

    internal bool CanLoadMore => _continuation is not null;

    internal bool CanGoUp => CurrentPath.Length > 0;

    internal string Status { get; private set; } = Ui.Sync.PickerLoadingFolders;

    internal bool IsFailure { get; private set; }

    /// <summary>Lists a folder from its first page, or says why the path is not one.</summary>
    internal Task NavigateAsync(string? path, CancellationToken cancellationToken = default)
    {
        if (!RemoteBrowserPath.TryNormalize(path, out var normalized, out var error))
        {
            Fail(error ?? Ui.Sync.PickerInvalidPath);
            return Task.CompletedTask;
        }

        CurrentPath = normalized;
        HasLoaded = false;
        _continuation = null;
        _folders.Clear();
        return LoadAsync(append: false, cancellationToken);
    }

    internal Task UpAsync(CancellationToken cancellationToken = default) =>
        NavigateAsync(RemoteBrowserPath.GetParent(CurrentPath), cancellationToken);

    internal Task LoadMoreAsync(CancellationToken cancellationToken = default) =>
        CanLoadMore ? LoadAsync(append: true, cancellationToken) : Task.CompletedTask;

    private async Task LoadAsync(bool append, CancellationToken cancellationToken)
    {
        // Set before the first await, so a window drawing this straight after the call shows it.
        Status = append ? Ui.Sync.PickerLoadingMoreFolders : Ui.Sync.PickerLoadingFolders;
        IsFailure = false;

        try
        {
            // Inside the try: a client that cannot even be made is an agent that is not there.
            await using var client = _clients();
            var response = await client.ListStorageAsync(
                new StorageListPageRequest(
                    StorageIpcContract.CurrentVersion,
                    _connectionId,
                    CurrentPath,
                    StorageIpcLimits.MaximumStableIdentityPageSize,
                    append ? _continuation : null,
                    IncludeVersions: false,
                    Recursive: false),
                cancellationToken).ConfigureAwait(true);
            if (response.Failure is { } failure)
            {
                _continuation = null;
                Fail(RemoteBrowserErrors.ForFailure(failure));
                return;
            }

            CurrentPath = response.RelativePath;
            HasLoaded = true;
            foreach (var entry in response.Entries.Where(static entry => entry.IsContainer))
            {
                if (_folders.Count >= MaximumFolders)
                {
                    _continuation = null;
                    Fail(Ui.Sync.PickerDisplayLimit);
                    return;
                }

                _folders.Add(new SyncLocationFolder(entry.Name, entry.RelativePath));
            }

            _continuation = response.ContinuationToken;
            Status = CurrentPath.Length == 0
                ? Ui.Format(Ui.Sync.PickerRootCountFormat, _folders.Count)
                : Ui.Format(Ui.Sync.PickerPathCountFormat, CurrentPath, _folders.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (RemoteBrowserErrors.IsExpected(error))
        {
            _continuation = null;
            Fail(DesktopAgentAvailability.ReportFailure(error));
        }
    }

    private void Fail(string message)
    {
        Status = message;
        IsFailure = true;
    }
}
