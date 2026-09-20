using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop;

/// <summary>
/// Which saved connections appear on the favourites menu, and in what order.
/// </summary>
/// <remarks>
/// A static member of OverviewDashboardControl until the port, which is the only reason the rule
/// could not be read anywhere else. It draws nothing: a favourite has to be enabled, has to be a
/// kind that can actually be opened -- storage, or an SSH client -- and the list is de-duplicated,
/// sorted by display name and capped. The overview card and the menu both ask for the same list,
/// and so will the Avalonia shell.
/// </remarks>
internal static class FavoriteConnectionMenu
{
    internal static IReadOnlyList<ConnectionSummary> Select(
        IEnumerable<ConnectionSummary>? connections,
        int maximum = 15) =>
        connections is null || maximum <= 0
            ? []
            : [.. connections
                .Where(static connection =>
                    connection.IsFavorite &&
                    connection.IsEnabled &&
                    connection is
                    {
                        Type: ConnectionProfileType.Storage
                    } or
                    {
                        Type: ConnectionProfileType.Client,
                        Provider: StorageConnectionProvider.Ssh
                    })
                .DistinctBy(static connection => connection.ConnectionId)
                .OrderBy(static connection => connection.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Take(maximum)];
}
