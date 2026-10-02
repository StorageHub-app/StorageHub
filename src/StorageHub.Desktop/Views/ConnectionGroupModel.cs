using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>
/// One connection in the panel: the card, and what kind of thing it is.
/// </summary>
/// <remarks>
/// The badge is what is left of the Storage and Clients split. It belongs on the row rather than
/// at the top of two fixed lists, because it is genuinely useful to see at a glance -- an SSH
/// client and an SFTP connection to the same host look identical without it -- and it is not a
/// reason to organise the whole panel around.
/// </remarks>
/// <param name="groupName">The name of the group it is filed in, for its subtitle; null when Ungrouped.</param>
internal sealed class ConnectionRowModel(ConnectionCardModel card, string? groupName = null) : INotifyPropertyChanged
{
    private bool _isSelected;

    public ConnectionCardModel Card { get; } = card;

    public Guid Id => Card.ConnectionId ?? Guid.Empty;

    public string Name => Card.Name;

    public string Endpoint => Card.Endpoint;

    public string State => Card.State;

    public bool IsEnabled => Card.IsEnabled;

    /// <summary>Whether it is a favourite, which the details panel marks with a star before its name.</summary>
    public bool IsFavorite => Card.IsFavorite;

    public string Badge => ConnectionGrouping.BadgeFor(Card);

    public bool IsClient => Card.Type == Contracts.Ipc.ConnectionProfileType.Client;

    public static string OpenHint => Ui.Connections.OpenInActivePane;

    /// <summary>
    /// The connection's own icon, drawn on its colour in a rounded tile, as 1.x's cards were
    /// (ui-reference 09). The colour is the one chosen in the editor, or the provider's.
    /// </summary>
    public Lucide.Avalonia.LucideIconKind Icon => Themes.IconCatalog.Resolve(
            ConnectionIconCatalog.ResolveForConnection(Card.IconKey, Card.Provider, Card.Type))
        ?? Lucide.Avalonia.LucideIconKind.Cloud;

    public Avalonia.Media.IBrush AccentBrush => AccentSwatch.BrushFor(Card.AccentHex);

    /// <summary>
    /// "Local / UNC · Studio · Not tested": provider, group and health on one muted line, where 1.x
    /// put the folder that was its group.
    /// </summary>
    public string Subtitle => string.Join(
        " · ",
        new[] { Card.Descriptor.DisplayName, groupName, Card.State }
            .Where(static part => !string.IsNullOrWhiteSpace(part)));

    public IReadOnlyList<string> Tags => Card.DisplayTags;

    public bool HasTags => Card.DisplayTags.Count > 0;

    /// <summary>The card the details panel is showing, which wears the accent border.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        internal set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>What a section of the connections panel is.</summary>
internal enum ConnectionGroupKind
{
    /// <summary>A group the agent keeps, which somebody made and can rename, recolour and remove.</summary>
    Group,

    /// <summary>The connections filed in no group, last, and only while there are any.</summary>
    Ungrouped,

    /// <summary>The favourites, above the groups: a state rather than a place, so nothing is filed in it.</summary>
    Favorites
}

/// <summary>
/// One section of the connections panel, and what can be done to it.
/// </summary>
/// <remarks>
/// Built fresh whenever the panel is drawn again rather than updated in place. There are at most a
/// handful of groups and a few dozen connections, and a drag that rebuilds is a great deal easier
/// to be sure of than one that reorders two observable collections while a pointer is down.
/// </remarks>
internal sealed class ConnectionGroupModel : INotifyPropertyChanged
{
    private bool _isExpanded = true;

    internal ConnectionGroupModel(
        string name,
        IReadOnlyList<ConnectionRowModel> connections,
        ConnectionGroupKind kind = ConnectionGroupKind.Group,
        Guid? groupId = null,
        string? iconKey = null,
        string? colorKey = null)
    {
        Name = name;
        Kind = kind;
        GroupId = kind == ConnectionGroupKind.Group ? groupId : null;
        IconKey = iconKey;
        ColorKey = Avalonia.Media.Color.TryParse(colorKey, out _) ? colorKey : null;
        Connections = [.. connections];
    }

    public string Name { get; }

    public ConnectionGroupKind Kind { get; }

    /// <summary>The agent's id for the group, or null for Ungrouped and Favorites.</summary>
    public Guid? GroupId { get; }

    public string? IconKey { get; }

    /// <summary>The group's colour as #RRGGBB, or null for none.</summary>
    public string? ColorKey { get; }

    public bool IsFavorites => Kind == ConnectionGroupKind.Favorites;

    public bool IsUngrouped => Kind == ConnectionGroupKind.Ungrouped;

    /// <summary>Whether it has a menu: only a group somebody made can be renamed, moved or removed.</summary>
    public bool HasMenu => Kind == ConnectionGroupKind.Group;

    /// <summary>
    /// The group's icon: the one chosen for it, or a folder; an inbox for Ungrouped.
    /// </summary>
    /// <remarks>
    /// Favorites draws 1.x's filled star in its place, which is the panel's to draw rather than a
    /// Lucide glyph.
    /// </remarks>
    public Lucide.Avalonia.LucideIconKind Icon => Kind == ConnectionGroupKind.Ungrouped
        ? Lucide.Avalonia.LucideIconKind.Inbox
        : (ConnectionIconCatalog.Resolve(IconKey) is { } glyph ? Themes.IconCatalog.Resolve(glyph) : null)
            ?? Lucide.Avalonia.LucideIconKind.Folder;

    /// <summary>Whether the group wears a colour, drawn as a small tile behind its icon as a card's is.</summary>
    public bool HasColor => ColorKey is not null && Kind == ConnectionGroupKind.Group;

    /// <summary>The icon on its own, muted, for a group without a colour.</summary>
    public bool ShowsPlainIcon => !HasColor && !IsFavorites;

    public Avalonia.Media.IBrush ColorBrush => AccentSwatch.BrushFor(ColorKey ?? string.Empty);

    public ObservableCollection<ConnectionRowModel> Connections { get; }

    public bool IsEmpty => Connections.Count == 0;

    /// <summary>How many connections are in it, after its name.</summary>
    public string Count => Connections.Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);

    public string CountAccessibleName => Ui.Format(Ui.Connections.GroupCountAccessibleFormat, Connections.Count);

    /// <summary>"Team, 4 connection(s)", for a reader who cannot see the panel.</summary>
    public string AccessibleName =>
        Ui.Format(Ui.Connections.GroupAccessibleNameFormat, Name, Connections.Count);

    /// <summary>
    /// Whether the group is open.
    /// </summary>
    /// <remarks>
    /// The panel keeps a closed group closed across its listings and searches, as 1.x did, but
    /// does not save it: 1.x did not either. A collapsed group is a way to get something out of
    /// the way while doing something else, and reopening the shell is the clearest signal that the
    /// something else is over.
    /// </remarks>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowsEmptyLine)));
        }
    }

    /// <summary>
    /// "No connections", on one quiet line under an open empty group, rather than a blank block.
    /// The line is the group's too, so a connection can still be dropped on it.
    /// </summary>
    public bool ShowsEmptyLine => _isExpanded && IsEmpty;

    public ICommand? RenameCommand { get; init; }

    /// <summary>Opens the picker for the group's icon and colour; also what right-clicking its heading does.</summary>
    public ICommand? ChangeIconCommand { get; init; }

    public ICommand? MoveUpCommand { get; init; }

    public ICommand? MoveDownCommand { get; init; }

    public ICommand? RemoveCommand { get; init; }

    public static string RenameLabel => Ui.Connections.RenameGroup;

    public static string RemoveLabel => Ui.Connections.RemoveGroup;

    public static string ChangeIconLabel => Ui.Connections.GroupIconAndColor;

    public static string MoveUpLabel => Ui.Connections.MoveGroupUp;

    public static string MoveDownLabel => Ui.Connections.MoveGroupDown;

    public static string EmptyLabel => Ui.Connections.GroupEmpty;

    public static string MenuLabel => Ui.Connections.GroupOptions;

    public event PropertyChangedEventHandler? PropertyChanged;
}
