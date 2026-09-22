using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// One rendered row in the connection picker: either a group heading or a selectable connection.
/// Headings are rows rather than decoration so the list can be drawn and measured in one pass.
/// </summary>
internal sealed record ConnectionPickerRow(string? GroupLabel, ConnectionCardModel? Card)
{
    public bool IsHeader => Card is null;
}

/// <summary>
/// Filters and groups the connection list for the picker.
///
/// Kept as pure functions so the matching rules can be tested without a window. The fields searched
/// deliberately mirror Connection Manager, which already searches names, endpoints, folders,
/// providers, states, and tags: a query that finds a connection in one place should find it in the
/// other.
/// </summary>
internal static class ConnectionPickerFilter
{
    public static bool Matches(ConnectionCardModel card, string? query)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        // Every whitespace-separated term must match somewhere, so typing more narrows rather than
        // widens - "s3 archive" finds the S3 connection called Archive, not every S3 connection.
        foreach (var term in query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!MatchesTerm(card, term))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesTerm(ConnectionCardModel card, string term) =>
        Contains(card.Name, term) ||
        Contains(card.Endpoint, term) ||
        Contains(card.FolderPath, term) ||
        Contains(card.State, term) ||
        Contains(card.Provider.ToString(), term) ||
        Contains(card.Descriptor.DisplayName, term) ||
        Contains(card.Type.ToString(), term) ||
        card.DisplayTags.Any(tag => Contains(tag, term));

    private static bool Contains(string? value, string term) =>
        value is not null && value.Contains(term, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>
    /// Builds the visible rows, inserting a heading whenever the group changes. The incoming order
    /// is preserved: the caller has already sorted favourites, storage, and clients into the order
    /// the rest of the shell uses.
    /// </summary>
    public static IReadOnlyList<ConnectionPickerRow> BuildRows(
        IEnumerable<ConnectionCardModel> cards,
        string? query,
        Func<ConnectionCardModel, string> groupLabel)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(groupLabel);

        var rows = new List<ConnectionPickerRow>();
        string? currentGroup = null;
        foreach (var card in cards)
        {
            if (!Matches(card, query))
            {
                continue;
            }

            var label = groupLabel(card);
            if (!string.Equals(label, currentGroup, StringComparison.OrdinalIgnoreCase))
            {
                rows.Add(new ConnectionPickerRow(label, null));
                currentGroup = label;
            }

            rows.Add(new ConnectionPickerRow(null, card));
        }

        return rows;
    }

    /// <summary>
    /// The heading a connection sits under in the picker.
    /// </summary>
    /// <remarks>
    /// The rule 1.x used, which lived in BrowserPaneControl as GetConnectionGroupLabel and so could
    /// not leave the WinForms shell. This computer first, then favourites, then a connection's own
    /// folder, and otherwise its kind and provider.
    /// </remarks>
    public static string GroupLabel(ConnectionCardModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (card.ConnectionId is null) return Ui.Pane.OnThisDevice;
        if (!card.IsEnabled) return Ui.Pane.Disabled;
        if (card.IsFavorite) return Ui.Pane.Favorites;
        if (!string.IsNullOrWhiteSpace(card.FolderPath)) return card.FolderPath.Trim();

        return card.Type == ConnectionProfileType.Client
            ? Ui.Format(Ui.Pane.ClientGroupFormat, card.Descriptor.DisplayName)
            : Ui.Format(Ui.Pane.StorageGroupFormat, card.Descriptor.DisplayName);
    }

    /// <summary>
    /// Puts cards in the order the picker shows them, so every group is one run.
    /// </summary>
    /// <remarks>
    /// <see cref="BuildRows"/> writes a heading whenever the group changes, so the order is what
    /// decides whether a group appears once or is split across the list with its heading repeated.
    /// 1.x left the order to its caller; stating it here is what makes that testable. This
    /// computer, favourites and disabled connections keep their places at the top, after them, and
    /// at the end; between them groups are alphabetical, and names within a group.
    /// </remarks>
    public static IReadOnlyList<ConnectionCardModel> Order(IEnumerable<ConnectionCardModel> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var comparer = StringComparer.Create(System.Globalization.CultureInfo.CurrentCulture, ignoreCase: true);
        return
        [
            .. cards
                .OrderBy(Rank)
                .ThenBy(GroupLabel, comparer)
                .ThenBy(static card => card.Name, comparer)
        ];

        static int Rank(ConnectionCardModel card) => card switch
        {
            { ConnectionId: null } => 0,
            { IsEnabled: false } => 3,
            { IsFavorite: true } => 1,
            _ => 2
        };
    }

    /// <summary>
    /// This computer, as a card, so it can be searched and chosen like any saved connection.
    /// </summary>
    public static ConnectionCardModel ThisComputer() => new(
        Ui.Pane.ThisPc,
        StorageProviderKind.Local,
        Environment.MachineName,
        Ui.Pane.Ready);

    /// <summary>The first selectable row at or after an index, or -1 when none remains.</summary>
    public static int NextSelectable(IReadOnlyList<ConnectionPickerRow> rows, int start, int direction)
    {
        ArgumentNullException.ThrowIfNull(rows);
        for (var index = start; index >= 0 && index < rows.Count; index += direction == 0 ? 1 : direction)
        {
            if (!rows[index].IsHeader)
            {
                return index;
            }

            if (direction == 0)
            {
                break;
            }
        }

        return -1;
    }
}

/// <summary>
/// One opening of the picker: what is typed, which row is highlighted, and which is open already.
/// </summary>
/// <remarks>
/// <para>
/// The part of ConnectionPickerPopup that was behaviour rather than drawing: the highlight starts
/// on the connection already open in the pane, never rests on a heading, follows the arrow keys
/// from the search box, and survives typing when the connection it was on still matches.
/// </para>
/// <para>
/// Pure, so the keyboard rules can be tested without a window -- which in 1.x they could not, since
/// they were event handlers on a ToolStripDropDown.
/// </para>
/// </remarks>
internal sealed class ConnectionPickerSession
{
    private readonly IReadOnlyList<ConnectionCardModel> _cards;
    private readonly Guid? _activeId;
    private readonly string? _activeName;
    private string _query = string.Empty;

    /// <param name="cards">Every connection the pane can show, in any order.</param>
    /// <param name="active">What the pane has open now, or null for a pane that has nothing.</param>
    internal ConnectionPickerSession(IEnumerable<ConnectionCardModel> cards, ConnectionCardModel? active)
    {
        ArgumentNullException.ThrowIfNull(cards);
        _cards = ConnectionPickerFilter.Order(cards);
        _activeId = active?.ConnectionId;
        _activeName = active?.Name;
        Rebuild(keep: null);
    }

    internal IReadOnlyList<ConnectionPickerRow> Rows { get; private set; } = [];

    /// <summary>The highlighted row, or -1 when nothing matches.</summary>
    internal int Highlighted { get; private set; } = -1;

    internal ConnectionCardModel? HighlightedCard =>
        Highlighted >= 0 && Highlighted < Rows.Count ? Rows[Highlighted].Card : null;

    internal bool IsEmpty => Rows.Count == 0;

    internal string Query
    {
        get => _query;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(_query, next, StringComparison.Ordinal)) return;
            _query = next;
            Rebuild(keep: HighlightedCard);
        }
    }

    /// <summary>Whether a card is the one the pane has open: by id, or for this computer by name.</summary>
    internal bool IsActive(ConnectionCardModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return _activeId is { } id
            ? card.ConnectionId == id
            : card.ConnectionId is null && _activeName is not null &&
              string.Equals(card.Name, _activeName, StringComparison.Ordinal);
    }

    /// <summary>Moves the highlight to the next connection up or down, stepping over headings.</summary>
    internal void Move(int direction)
    {
        if (Rows.Count == 0 || direction == 0) return;
        var step = Math.Sign(direction);
        var start = Highlighted < 0 ? (step > 0 ? 0 : Rows.Count - 1) : Highlighted + step;
        var next = ConnectionPickerFilter.NextSelectable(Rows, start, step);
        if (next >= 0) Highlighted = next;
    }

    /// <summary>Highlights a row the pointer is over, if it is a connection rather than a heading.</summary>
    internal void HighlightRow(int index)
    {
        if (index >= 0 && index < Rows.Count && !Rows[index].IsHeader) Highlighted = index;
    }

    private void Rebuild(ConnectionCardModel? keep)
    {
        Rows = ConnectionPickerFilter.BuildRows(_cards, _query, ConnectionPickerFilter.GroupLabel);
        if (Rows.Count == 0)
        {
            Highlighted = -1;
            return;
        }

        // The connection that was highlighted if it still matches, else the one open in the pane,
        // else the first. Typing never throws away a highlight it has no reason to.
        var target = keep is not null ? IndexOf(row => ReferenceEquals(row.Card, keep)) : -1;
        if (target < 0) target = IndexOf(row => row.Card is { } card && IsActive(card));
        Highlighted = target >= 0 ? target : ConnectionPickerFilter.NextSelectable(Rows, 0, 1);
    }

    private int IndexOf(Func<ConnectionPickerRow, bool> predicate)
    {
        for (var index = 0; index < Rows.Count; index++)
        {
            if (predicate(Rows[index])) return index;
        }

        return -1;
    }
}
