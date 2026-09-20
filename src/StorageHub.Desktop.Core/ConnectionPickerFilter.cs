using StorageHub.Contracts.Ipc;

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
