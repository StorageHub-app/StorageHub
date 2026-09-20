using StorageHub.Contracts.Ipc;
using StorageHub.Contracts.Results;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>Where a pane is being asked to go.</summary>
internal enum PaneNavigationKind
{
    Navigate,
    Back,
    Forward,
    Up,
    Refresh
}

/// <summary>What a pane is showing after a navigation.</summary>
/// <param name="Note">
/// Something worth saying about a navigation that nonetheless succeeded -- most often that the
/// folder asked for is gone and the nearest parent was opened instead. Dropping it leaves the
/// wrong listing on screen with no explanation.
/// </param>
internal sealed record PaneListing(
    string DisplayPath,
    IReadOnlyList<BrowserListItem> Rows,
    bool IsAtRoot,
    bool HasMore,
    string? Note = null);

/// <summary>A navigation that worked, or the reason it did not.</summary>
internal sealed record PaneNavigationResult(PaneListing? Listing, string? Error)
{
    internal static PaneNavigationResult Failed(string? error) => new(null, error);

    internal static PaneNavigationResult Ok(PaneListing listing) => new(listing, null);
}

/// <summary>
/// One place a pane can be pointed at: a saved connection, or this computer.
/// </summary>
/// <remarks>
/// <para>
/// The two browsers underneath are already the same shape -- same navigation kinds, same result,
/// same history -- and the WinForms pane still branched on which one it held at every call site,
/// which is a large part of why it reached 3,913 lines. One interface means the pane has a single
/// path through navigate, refresh, go up and capture-a-transfer-context, and adding a third kind
/// of source later is an implementation rather than an edit to every method.
/// </para>
/// <para>
/// It is also what makes a local-to-remote transfer work without the shell knowing it is one: a
/// pane hands its context to <see cref="PaneTransferSnapshots"/>, and whether that context says
/// ThisPc or SavedConnection is the source's answer, not the pane's.
/// </para>
/// </remarks>
internal interface IPaneSource : IAsyncDisposable
{
    /// <summary>What the pane's heading says.</summary>
    string Title { get; }

    bool CanGoBack { get; }

    bool CanGoForward { get; }

    bool CanGoUp { get; }

    /// <summary>This pane as a transfer endpoint, or why it cannot be one.</summary>
    StorageResult<PaneTransferContext> TransferContext();

    /// <summary>
    /// Goes somewhere, and reports what is there.
    /// </summary>
    /// <param name="target">
    /// Where to, for <see cref="PaneNavigationKind.Navigate"/>. Ignored otherwise, because the
    /// source's own history knows where back, forward and up are.
    /// </param>
    Task<PaneNavigationResult> MoveAsync(
        PaneNavigationKind kind,
        string? target = null,
        CancellationToken cancellationToken = default);
}

/// <summary>A pane over a saved connection, through the agent.</summary>
internal sealed class RemotePaneSource(RemoteBrowserController controller, string title) : IPaneSource
{
    public string Title { get; } = title;

    public bool CanGoBack => controller.CanGoBack;

    public bool CanGoForward => controller.CanGoForward;

    public bool CanGoUp => controller.CanGoUp;

    public StorageResult<PaneTransferContext> TransferContext()
    {
        if (controller.CurrentSnapshot is not { } snapshot)
        {
            return StorageResult<PaneTransferContext>.Fail(new StorageFailure(
                "manual_transfer.pane.not_connected",
                StorageFailureKind.Validation,
                Ui.Pane.SelectProfileToConnect));
        }

        return PaneTransferContext.Create(
            PaneTransferContextKind.SavedConnection,
            snapshot.Connection.ConnectionId,
            snapshot.RootIdentity,
            snapshot.RelativePath);
    }

    public async Task<PaneNavigationResult> MoveAsync(
        PaneNavigationKind kind,
        string? target = null,
        CancellationToken cancellationToken = default)
    {
        var result = await controller
            .NavigateAsync(Translate(kind), target, cancellationToken)
            .ConfigureAwait(false);

        if (result.Status != RemoteBrowserOperationStatus.Succeeded || result.Snapshot is null)
        {
            return PaneNavigationResult.Failed(result.ErrorMessage);
        }

        var snapshot = result.Snapshot;
        return PaneNavigationResult.Ok(new PaneListing(
            snapshot.DisplayPath,
            [.. snapshot.Entries.Select(BrowserRowFactory.FromRemote)],
            snapshot.RelativePath.Length == 0,
            snapshot.HasMore,
            result.ErrorMessage));
    }

    /// <summary>Opens the connection's root, which is also what makes the pane usable at all.</summary>
    internal async Task<PaneNavigationResult> OpenAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        var result = await controller
            .SelectConnectionAsync(connectionId, cancellationToken)
            .ConfigureAwait(false);

        return result.Status == RemoteBrowserOperationStatus.Succeeded && result.Snapshot is { } snapshot
            ? PaneNavigationResult.Ok(new PaneListing(
                snapshot.DisplayPath,
                [.. snapshot.Entries.Select(BrowserRowFactory.FromRemote)],
                snapshot.RelativePath.Length == 0,
                snapshot.HasMore))
            : PaneNavigationResult.Failed(result.ErrorMessage);
    }

    public ValueTask DisposeAsync() => controller.DisposeAsync();

    private static RemoteBrowserNavigationKind Translate(PaneNavigationKind kind) => kind switch
    {
        PaneNavigationKind.Back => RemoteBrowserNavigationKind.Back,
        PaneNavigationKind.Forward => RemoteBrowserNavigationKind.Forward,
        PaneNavigationKind.Up => RemoteBrowserNavigationKind.Up,
        PaneNavigationKind.Refresh => RemoteBrowserNavigationKind.Refresh,
        _ => RemoteBrowserNavigationKind.Navigate
    };
}

/// <summary>
/// A pane over this computer's own drives and folders.
/// </summary>
/// <remarks>
/// The same interface as a connection, deliberately: most transfers have one local end, and a
/// pane that had to know which kind it was holding would branch at every one of them. "This PC"
/// itself -- the list of drives -- is a location like any other, which is what lets back and up
/// arrive at it rather than needing a button of their own.
/// </remarks>
internal sealed class LocalPaneSource(LocalBrowserController controller) : IPaneSource
{
    public string Title => Ui.Pane.ThisPc;

    public bool CanGoBack => controller.CanGoBack;

    public bool CanGoForward => controller.CanGoForward;

    public bool CanGoUp => !controller.CurrentLocation.IsThisPc;

    public StorageResult<PaneTransferContext> TransferContext() =>
        PaneTransferContext.Create(
            PaneTransferContextKind.ThisPc,
            connectionId: null,
            rootIdentity: null,
            controller.CurrentLocation.DirectoryPath ?? string.Empty);

    public async Task<PaneNavigationResult> MoveAsync(
        PaneNavigationKind kind,
        string? target = null,
        CancellationToken cancellationToken = default)
    {
        LocalBrowserLocation? location = null;
        if (kind == PaneNavigationKind.Navigate)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                location = LocalBrowserLocation.ThisPc;
            }
            else if (LocalBrowserLocation.TryParseAddress(target, out var parsed, out var error))
            {
                location = parsed;
            }
            else
            {
                return PaneNavigationResult.Failed(error);
            }
        }

        var result = await controller
            .NavigateAsync(Translate(kind), location, cancellationToken)
            .ConfigureAwait(false);

        if (result.Status != LocalBrowserNavigationStatus.Succeeded || result.Snapshot is null)
        {
            return PaneNavigationResult.Failed(result.ErrorMessage);
        }

        var snapshot = result.Snapshot;
        return PaneNavigationResult.Ok(new PaneListing(
            snapshot.Location.DisplayText,
            [.. snapshot.Entries.Select(BrowserRowFactory.FromLocal)],
            snapshot.Location.IsThisPc,
            snapshot.HasMore,
            result.ErrorMessage));
    }

    public ValueTask DisposeAsync() => controller.DisposeAsync();

    private static LocalBrowserNavigationKind Translate(PaneNavigationKind kind) => kind switch
    {
        PaneNavigationKind.Back => LocalBrowserNavigationKind.Back,
        PaneNavigationKind.Forward => LocalBrowserNavigationKind.Forward,
        PaneNavigationKind.Up => LocalBrowserNavigationKind.Up,
        PaneNavigationKind.Refresh => LocalBrowserNavigationKind.Refresh,
        _ => LocalBrowserNavigationKind.Navigate
    };
}
