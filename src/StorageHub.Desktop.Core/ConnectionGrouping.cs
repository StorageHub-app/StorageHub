using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// One group of the arrangement the desktop kept in its settings file before groups were the
/// agent's: a name somebody chose, and what was in it.
/// </summary>
/// <param name="Members">Connection ids, in the order they were shown.</param>
public sealed record ConnectionGroupEntry(string Name, IReadOnlyList<Guid> Members);

/// <summary>
/// What is left of the desktop's own grouping, now the agent keeps the groups: reading the old
/// arrangement so it can be brought across once, and the badge on a row.
/// </summary>
/// <remarks>
/// <para>
/// The panel's groups were an arrangement in the settings file, made by dragging, with a
/// connection's typed folder path deciding where a new one landed. They are the agent's now
/// (<see cref="ConnectionGroupDocument"/>), and a connection names its group by id. The first time
/// a desktop meets an agent that has not had an arrangement brought in, it sends this one, so
/// nobody's groups, order or members are lost in the move.
/// </para>
/// </remarks>
public static class ConnectionGrouping
{
    /// <summary>The old arrangement's catch-all group, which Ungrouped replaces.</summary>
    public static string DefaultGroupName => Ui.Connections.DefaultGroup;

    /// <summary>
    /// The groups the old panel showed: the saved arrangement, reconciled with what the agent has.
    /// </summary>
    /// <remarks>
    /// Members that no longer exist are dropped, and connections in no group are appended to the
    /// group their folder path names, which is where the old panel showed them.
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
    /// The arrangement the desktop kept before groups were the agent's, as the groups to bring in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The saved groups, in their order and with their members, then a group for each folder path
    /// nobody had filed anywhere else, as the panel used to show them. The default group is left
    /// out: it was where anything not put somewhere landed, which is what Ungrouped is now.
    /// </para>
    /// <para>
    /// Whatever the agent could not keep is dropped rather than refusing the whole arrangement: a
    /// name it cannot store, an icon key too long to be one, members beyond what one group carries.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ConnectionGroupImportEntry> LegacyImport(
        IReadOnlyList<ConnectionGroupEntry>? saved,
        IReadOnlyDictionary<string, string>? icons,
        IReadOnlyList<ConnectionCardModel> connections)
    {
        ArgumentNullException.ThrowIfNull(connections);
        var entries = new List<ConnectionGroupImportEntry>();
        foreach (var group in Arrange(saved, connections))
        {
            var name = group.Name.Trim();
            if (Same(name, DefaultGroupName)) continue;

            var icon = icons?.GetValueOrDefault(group.Name);
            var entry = new ConnectionGroupImportEntry(
                name,
                icon is { Length: > 0 and <= ConnectionProfileIpcLimits.MaximumIconKeyLength } ? icon : null,
                ColorKey: null,
                [.. group.Members.Take(ConnectionGroupIpcLimits.MaximumMembersPerImportedGroup)]);
            if (entry.HasValidBounds && entries.Count < ConnectionGroupIpcLimits.MaximumGroups) entries.Add(entry);
        }

        return entries;
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

    private static int IndexOf(List<ConnectionGroupEntry> groups, string name)
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
