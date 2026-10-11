using Lucide.Avalonia;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>A place the pane has shown, which Go > History can take it back to.</summary>
/// <param name="Connection">What the pane was pointed at: This PC or a saved connection.</param>
/// <param name="Target">
/// The folder as the pane navigates to it: a full path on this computer ("" for This PC itself),
/// a relative one on a connection ("" for its top).
/// </param>
/// <param name="Display">What the address bar said there.</param>
internal sealed record PaneVisit(PaneConnection Connection, string Target, string Display);

/// <summary>One entry of a list the pane offers under its address bar.</summary>
/// <param name="Go">What choosing it does; null for an entry that is only there to be read.</param>
internal sealed record PaneChoice(string Label, string Detail, Func<Task>? Go);

/// <summary>
/// A short list for somebody to pick from, shown at the pane: Go > History's visited folders,
/// or Go > Favorites' connections.
/// </summary>
/// <param name="Empty">What the list says when there is nothing in it, and why.</param>
internal sealed record PaneChoices(string Heading, IReadOnlyList<PaneChoice> Items, PaneChoice Empty);

/// <summary>
/// What the View, Go and Connections menus do to a pane: its tree and hidden files, home, history
/// and favourites, and reconnecting or disconnecting what it has open.
/// </summary>
/// <remarks>
/// <para>
/// Kept apart from the listing and navigation they build on, which are the rest of this class.
/// Every one of these was a menu entry in 1.4 that did nothing; each is built here from what the
/// pane already does -- navigate, open a connection, point at Connections Home -- rather than as a
/// second way of doing it.
/// </para>
/// <para>
/// None of them touches a control. The lists Go > History and Go > Favorites offer are raised as
/// <see cref="ChoicesRequested"/> and drawn by the view, so a headless test reads them as data.
/// </para>
/// </remarks>
internal sealed partial class BrowserPaneModel
{
    /// <summary>How many folders Go > History remembers per pane, newest first.</summary>
    internal const int MaximumVisits = 20;

    private bool _showTree = true;
    private bool _showHidden = true;
    private readonly List<PaneVisit> _visits = [];

    /// <summary>
    /// Whether the folder tree is shown beside the list: View > Directory Tree, and the pane's own
    /// menu. Saved with the workspace as <c>TreeHidden</c>, so a pane reopens the way it was left.
    /// </summary>
    /// <remarks>
    /// 1.4 always showed the tree; its View entry was declared and never wired. Per pane, as the
    /// connection and files bars are, because a narrow pane in a four-pane workspace is where the
    /// width is wanted back and a wide one beside it may still want its tree.
    /// </remarks>
    public bool ShowTree
    {
        get => _showTree;
        set
        {
            if (_showTree == value) return;
            _showTree = value;
            Raise(nameof(ShowTree));
        }
    }

    public static string ShowTreeLabel => Ui.Shell.ShowDirectoryTree;

    /// <summary>
    /// Whether the list and the tree include what is hidden: a name starting with a dot, or a file
    /// this computer marks hidden. View > Hidden Files sets it on every pane at once.
    /// </summary>
    /// <remarks>
    /// The hidden rows stay in the listing index, so turning this back on shows them without the
    /// folder being read again. A paste still checks its names against them, since a file the pane
    /// is not showing is still there to be overwritten.
    /// </remarks>
    internal bool ShowHiddenItems
    {
        get => _showHidden;
        set
        {
            if (_showHidden == value) return;
            _showHidden = value;
            Raise(nameof(ShowHiddenItems));

            // Nothing listed yet is nothing to show again, and building the index here would open
            // a SQLite file for a pane that has not been pointed anywhere.
            if (_index is null || _source is null || IsTerminal) return;
            ApplyView();
            FollowTree(_index.CreateView(_sortColumn, _sortAscending), append: false);
        }
    }

    /// <summary>The folders this pane has shown this session, newest first, the one showing included.</summary>
    internal IReadOnlyList<PaneVisit> Visits => _visits;

    /// <summary>Raised for the view to show a list at the pane, and act on what is chosen from it.</summary>
    internal event EventHandler<PaneChoices>? ChoicesRequested;

    /// <summary>Where the pane goes for Go > Home on this computer: the user's own folder.</summary>
    /// <remarks>Settable so a test can name a folder of its own.</remarks>
    internal Func<string> LocalHome { get; set; } = static () =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// Notes the folder just shown, for Go > History.
    /// </summary>
    /// <remarks>
    /// Only folders: Connections Home and a terminal have nowhere in them to come back to. A folder
    /// already in the list moves to the front rather than appearing twice.
    /// </remarks>
    private void RecordVisit()
    {
        if (_connection is not { Kind: PaneContentKind.ThisPc or PaneContentKind.SavedStorage } connection ||
            _source is not (LocalPaneSource or RemotePaneSource) ||
            PaneTransferSnapshots.ContextFor(_source) is not { IsSuccess: true, Value: var here })
        {
            return;
        }

        var visit = new PaneVisit(connection, here.RelativePath, _path);
        _visits.RemoveAll(existing => SamePlace(existing, visit));
        _visits.Insert(0, visit);
        if (_visits.Count > MaximumVisits) _visits.RemoveRange(MaximumVisits, _visits.Count - MaximumVisits);
    }

    private static bool SamePlace(PaneVisit left, PaneVisit right) =>
        left.Connection.Kind == right.Connection.Kind &&
        left.Connection.Id == right.Connection.Id &&
        string.Equals(left.Target, right.Target, StringComparison.Ordinal);

    /// <summary>Whether Go > Home has somewhere to go: a folder listing that opened.</summary>
    internal bool CanGoHome =>
        _source is LocalPaneSource or RemotePaneSource &&
        PaneTransferSnapshots.ContextFor(_source) is { IsSuccess: true };

    /// <summary>
    /// Go > Home: the top of the connection, or the user's own folder on this computer.
    /// </summary>
    /// <remarks>
    /// A connection's top is where opening it lands, which is the nearest thing a pane has to a
    /// home; on this computer that would be the drive list, which Up already reaches, so home is
    /// the folder every file manager calls it.
    /// </remarks>
    internal Task GoHomeAsync(CancellationToken cancellationToken = default) => _source switch
    {
        LocalPaneSource => NavigateAsync(LocalHome() is { Length: > 0 } home ? home : string.Empty, cancellationToken),
        RemotePaneSource when CanGoHome => NavigateAsync(string.Empty, cancellationToken),
        _ => Task.CompletedTask
    };

    /// <summary>
    /// Go > History: the folders shown this session other than the one showing, to go back to.
    /// </summary>
    internal PaneChoices HistoryChoices()
    {
        var current = _visits.Count > 0 && _connection is { } open && _visits[0].Connection.Kind == open.Kind &&
            _visits[0].Connection.Id == open.Id && string.Equals(_visits[0].Display, _path, StringComparison.Ordinal)
                ? _visits[0]
                : null;
        var items = _visits
            .Where(visit => !ReferenceEquals(visit, current))
            .Select(visit => new PaneChoice(
                visit.Display,
                visit.Connection.Name,
                () => ReturnToAsync(visit)))
            .ToArray();
        return new PaneChoices(
            Ui.Commands.GoHistory,
            items,
            new PaneChoice(Ui.Shell.NoOtherLocationsVisited, Ui.Shell.HistoryHint, null));
    }

    /// <summary>
    /// Go > Favorites: the favourite connections this pane can open, by name, each opening here.
    /// </summary>
    /// <remarks>
    /// The same connections the Go menu lists under its Favorites heading, which are the panel's;
    /// this is the list at the pane, for the keyboard and the toolbar, which a menu foot is not.
    /// </remarks>
    internal PaneChoices FavoriteChoices()
    {
        var items = _cards
            .Where(static card => card is { IsFavorite: true, IsEnabled: true, ConnectionId: not null })
            .OrderBy(static card => card.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(card => new PaneChoice(card.Name, card.Endpoint, () =>
            {
                Choose(card);
                return Task.CompletedTask;
            }))
            .ToArray();
        return new PaneChoices(
            Ui.Shell.Favorites,
            items,
            new PaneChoice(Ui.Shell.NoFavoriteConnections, Ui.Shell.FavoritesEmptyHint, null));
    }

    /// <summary>Asks the view to show Go > History's list at this pane.</summary>
    internal void ShowHistory() => ChoicesRequested?.Invoke(this, HistoryChoices());

    /// <summary>Asks the view to show Go > Favorites' list at this pane.</summary>
    internal void ShowFavorites() => ChoicesRequested?.Invoke(this, FavoriteChoices());

    /// <summary>
    /// Goes back to a folder from the history, opening its connection first when the pane has
    /// moved to another since.
    /// </summary>
    internal async Task ReturnToAsync(PaneVisit visit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(visit);
        if (_connection is not { } open || open.Kind != visit.Connection.Kind || open.Id != visit.Connection.Id ||
            _source is null)
        {
            var choice = Connections.FirstOrDefault(candidate =>
                    candidate.Kind == visit.Connection.Kind && candidate.Id == visit.Connection.Id)
                ?? visit.Connection;
            await PointAtAsync(choice, cancellationToken).ConfigureAwait(true);

            // A connection that would not open says why; there is no folder in it to go to.
            if (_failed || !ReferenceEquals(_connection, choice)) return;

            // Opening lands at the top, so the top needs no second listing.
            if (visit.Target.Length == 0 && visit.Connection.Kind == PaneContentKind.SavedStorage) return;
        }

        await NavigateAsync(visit.Target, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Whether there is a saved connection open, or chosen, to reconnect or disconnect.</summary>
    internal bool HasSavedConnection => _connection is { Id: not null, Kind: not PaneContentKind.ConnectionsHome };

    /// <summary>
    /// Connections > Reconnect: opens the pane's connection again, from nothing, and goes back to
    /// the folder it was in.
    /// </summary>
    /// <remarks>
    /// Opening tests the connection before listing it, so this is also how a pane whose server
    /// went away is brought back without choosing the connection again in the picker. A shell is
    /// closed and a new one started. A pane restored without connecting, because "reconnect remote
    /// panes" is off, connects.
    /// </remarks>
    internal async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!HasSavedConnection || _connection is not { } connection) return;

        var folder = connection.Kind == PaneContentKind.SavedStorage && _source is RemotePaneSource &&
            PaneTransferSnapshots.ContextFor(_source) is { IsSuccess: true, Value: var here }
                ? here.RelativePath
                : string.Empty;

        _controller.CancelCurrentOperation();
        await OpenAsync(connection, cancellationToken).ConfigureAwait(true);
        if (folder.Length > 0 && ReferenceEquals(_connection, connection) && !_failed && _source is RemotePaneSource)
        {
            await NavigateAsync(folder, cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Connections > Disconnect: lets go of the pane's connection and shows Connections Home, where
    /// it, or another, is one double-click away again.
    /// </summary>
    /// <remarks>
    /// Connections Home rather than an empty pane, because that is what a pane with nothing open
    /// shows everywhere else. A running shell is closed with it.
    /// </remarks>
    internal async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!HasSavedConnection) return;

        _controller.CancelCurrentOperation();
        var home = Connections.FirstOrDefault(static candidate => candidate.Kind == PaneContentKind.ConnectionsHome)
            ?? new PaneConnection(null, Ui.Pane.ConnectionsHome, LucideIconKind.House, PaneContentKind.ConnectionsHome);
        await PointAtAsync(home, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Opens Quick Connect's search, the pane's own connection picker.</summary>
    internal void ShowConnectionPicker() => ConnectionPickerRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Raised for the view to open the connection picker with its search box focused.</summary>
    internal event EventHandler? ConnectionPickerRequested;
}
