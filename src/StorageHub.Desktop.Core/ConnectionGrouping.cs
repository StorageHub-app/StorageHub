using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>One group in the connections panel: a name somebody chose, and what is in it.</summary>
/// <param name="Members">
/// Connection ids, in the order they are shown. Ids rather than cards, because the agent is the
/// authority on which connections exist and this is only an arrangement of them.
/// </param>
public sealed record ConnectionGroupEntry(string Name, IReadOnlyList<Guid> Members);

/// <summary>
/// How the connections panel is organised: groups somebody made, in the order they put them.
/// </summary>
/// <remarks>
/// <para>
/// This replaces the fixed Storage and Clients split. That split was the provider's classification
/// showing through as an organising principle, and it is not one: somebody with four buckets and
/// two shells for the same project wants those six things together, and no amount of sorting
/// inside two fixed lists gives them that.
/// </para>
/// <para>
/// A connection is in exactly one group. Groups hold ids, so a connection that is deleted simply
/// stops appearing, and one that is new lands in the group named by its folder path -- which is
/// what carried the old organisation and so is what an existing installation should reappear as.
/// </para>
/// <para>
/// Every method returns a new arrangement rather than changing one. There is at most a handful of
/// groups and a few dozen connections, so nothing here is worth the bugs that in-place reordering
/// during a drag would cost.
/// </para>
/// </remarks>
public static class ConnectionGrouping
{
    /// <summary>The group that holds anything not put somewhere else.</summary>
    /// <remarks>
    /// Named rather than a null group, so it can be reordered, renamed and dragged out of like any
    /// other. What makes it special is only that unplaced connections land in it.
    /// </remarks>
    public static string DefaultGroupName => Ui.Connections.DefaultGroup;

    /// <summary>
    /// The groups to show: the saved arrangement, reconciled with what the agent actually has.
    /// </summary>
    /// <remarks>
    /// Members that no longer exist are dropped, and connections in no group are appended to the
    /// group their folder path names -- so a connection added from the Connection Manager appears
    /// where somebody would look for it rather than at the bottom of everything.
    /// </remarks>
    public static IReadOnlyList<ConnectionGroupEntry> Arrange(
        IReadOnlyList<ConnectionGroupEntry>? saved,
        IReadOnlyList<ConnectionCardModel> connections)
    {
        ArgumentNullException.ThrowIfNull(connections);

        var known = connections
            .Where(static card => card.ConnectionId is not null)
            .ToDictionary(static card => card.ConnectionId!.Value);
        var placed = new HashSet<Guid>();
        var groups = new List<ConnectionGroupEntry>();

        foreach (var group in saved ?? [])
        {
            var members = group.Members
                .Where(id => known.ContainsKey(id) && placed.Add(id))
                .ToArray();
            groups.Add(group with { Members = members });
        }

        foreach (var card in connections)
        {
            if (card.ConnectionId is not { } id || !placed.Add(id)) continue;
            Place(groups, GroupNameFor(card), id);
        }

        // An arrangement with nothing in it still needs somewhere for the next connection to land.
        if (groups.Count == 0) groups.Add(new ConnectionGroupEntry(DefaultGroupName, []));
        return groups;
    }

    /// <summary>
    /// Moves a connection into a group, at a position.
    /// </summary>
    /// <remarks>
    /// Removed from wherever it was first, because a connection is in exactly one group and a drag
    /// that left a copy behind would be the kind of bug somebody only notices later, once they are
    /// counting on the panel to tell them what they have.
    /// </remarks>
    public static IReadOnlyList<ConnectionGroupEntry> Move(
        IReadOnlyList<ConnectionGroupEntry> groups,
        Guid connectionId,
        string groupName,
        int index)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (connectionId == Guid.Empty || string.IsNullOrWhiteSpace(groupName)) return groups;

        var moved = groups
            .Select(group => group with { Members = [.. group.Members.Where(id => id != connectionId)] })
            .ToList();

        var target = moved.FindIndex(group => Same(group.Name, groupName));
        if (target < 0)
        {
            moved.Add(new ConnectionGroupEntry(groupName.Trim(), [connectionId]));
            return moved;
        }

        var members = moved[target].Members.ToList();
        members.Insert(Math.Clamp(index, 0, members.Count), connectionId);
        moved[target] = moved[target] with { Members = members };
        return moved;
    }

    /// <summary>Adds an empty group, or leaves the arrangement alone if that name is taken.</summary>
    public static IReadOnlyList<ConnectionGroupEntry> Add(
        IReadOnlyList<ConnectionGroupEntry> groups,
        string name)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var trimmed = name?.Trim() ?? string.Empty;
        return trimmed.Length == 0 || groups.Any(group => Same(group.Name, trimmed))
            ? groups
            : [.. groups, new ConnectionGroupEntry(trimmed, [])];
    }

    /// <summary>Renames a group, keeping its place and its contents.</summary>
    public static IReadOnlyList<ConnectionGroupEntry> Rename(
        IReadOnlyList<ConnectionGroupEntry> groups,
        string from,
        string to)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var trimmed = to?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || Same(from, trimmed)) return groups;

        // Refused rather than merged: two groups with one name is a state nothing else here can
        // represent, and merging silently would move connections somebody did not ask to move.
        if (groups.Any(group => Same(group.Name, trimmed))) return groups;

        return [.. groups.Select(group => Same(group.Name, from) ? group with { Name = trimmed } : group)];
    }

    /// <summary>
    /// Removes a group, keeping everything that was in it.
    /// </summary>
    /// <remarks>
    /// Its connections go to the first group left, so removing a group is a way to tidy the panel
    /// and never a way to lose track of a connection. The last group cannot be removed, because
    /// then there would be nowhere for anything to be.
    /// </remarks>
    public static IReadOnlyList<ConnectionGroupEntry> Remove(
        IReadOnlyList<ConnectionGroupEntry> groups,
        string name)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (groups.Count <= 1) return groups;

        var index = IndexOf(groups, name);
        if (index < 0) return groups;

        var orphans = groups[index].Members;
        var kept = groups.Where((_, position) => position != index).ToList();
        var destination = index == 0 ? 0 : index - 1;
        kept[destination] = kept[destination] with
        {
            Members = [.. kept[destination].Members, .. orphans]
        };
        return kept;
    }

    /// <summary>Moves a whole group up or down the panel.</summary>
    public static IReadOnlyList<ConnectionGroupEntry> Reorder(
        IReadOnlyList<ConnectionGroupEntry> groups,
        string name,
        int index)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var from = IndexOf(groups, name);
        if (from < 0) return groups;

        var moved = groups.ToList();
        var group = moved[from];
        moved.RemoveAt(from);
        moved.Insert(Math.Clamp(index, 0, moved.Count), group);
        return moved;
    }

    /// <summary>
    /// The group a connection belongs to before anybody has moved it.
    /// </summary>
    /// <remarks>
    /// Its folder path, which is what organised the sidebar in 1.x, so an installation that had
    /// connections filed under "Team" reappears with a Team group rather than one long list. Only
    /// the first segment is used: a path is a path, and the panel is a flat set of groups.
    /// </remarks>
    public static string GroupNameFor(ConnectionCardModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var folder = card.FolderPath?.Trim();
        if (string.IsNullOrEmpty(folder)) return DefaultGroupName;

        var first = folder.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? DefaultGroupName : first;
    }

    /// <summary>
    /// What a connection is, in two or three letters, for the badge beside its name.
    /// </summary>
    /// <remarks>
    /// The classification 1.x used to split the panel in two. It is genuinely useful to see at a
    /// glance -- an SSH client and an SFTP connection to the same host look identical otherwise --
    /// and it is a property of a row rather than a reason to organise the whole panel around it.
    /// </remarks>
    public static string BadgeFor(ConnectionCardModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card.Type == ConnectionProfileType.Client
            ? Ui.Connections.BadgeClient
            : Ui.Connections.BadgeStorage;
    }

    private static void Place(List<ConnectionGroupEntry> groups, string name, Guid id)
    {
        var index = IndexOf(groups, name);
        if (index < 0)
        {
            groups.Add(new ConnectionGroupEntry(name, [id]));
            return;
        }

        groups[index] = groups[index] with { Members = [.. groups[index].Members, id] };
    }

    private static int IndexOf(IReadOnlyList<ConnectionGroupEntry> groups, string name)
    {
        for (var index = 0; index < groups.Count; index++)
        {
            if (Same(groups[index].Name, name)) return index;
        }

        return -1;
    }

    /// <summary>
    /// Group names are compared without case, because two groups differing only in case would be
    /// two groups somebody meant as one.
    /// </summary>
    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
