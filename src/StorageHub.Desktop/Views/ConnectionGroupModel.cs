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
internal sealed class ConnectionRowModel(ConnectionCardModel card) : INotifyPropertyChanged
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

    /// <summary>"Local / UNC · Studio · Not tested": provider, folder and health on one muted line.</summary>
    public string Subtitle => string.Join(
        " · ",
        new[] { Card.Descriptor.DisplayName, Card.FolderPath, Card.State }
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

/// <summary>
/// One group in the connections panel, and what can be done to it.
/// </summary>
/// <remarks>
/// Built fresh whenever the arrangement changes rather than updated in place. There are at most a
/// handful of groups and a few dozen connections, and a drag that rebuilds is a great deal easier
/// to be sure of than one that reorders two observable collections while a pointer is down.
/// </remarks>
internal sealed class ConnectionGroupModel(
    string name,
    IReadOnlyList<ConnectionRowModel> connections,
    ICommand renameCommand,
    ICommand removeCommand,
    ICommand? changeIconCommand = null,
    string? iconKey = null,
    bool isFavorites = false) : INotifyPropertyChanged
{
    /// <summary>
    /// The group's icon: the one chosen for it, or a folder.
    /// </summary>
    /// <remarks>
    /// 1.x let a folder of connections have an icon of its own and kept the choices in the settings
    /// file, which 2.0 went on saving and never showed. They are shown again. Favorites draws 1.x's
    /// filled star in its place, which is the panel's to draw rather than a Lucide glyph.
    /// </remarks>
    public Lucide.Avalonia.LucideIconKind Icon =>
        (ConnectionIconCatalog.Resolve(iconKey) is { } glyph ? Themes.IconCatalog.Resolve(glyph) : null)
        ?? Lucide.Avalonia.LucideIconKind.Folder;

    /// <summary>
    /// The Favorites group at the top of the panel, rather than one somebody made.
    /// </summary>
    /// <remarks>
    /// Its members are the connections marked as favourites, so it has nothing to rename, remove
    /// or drop a connection into: the panel leaves its menu out and a drag lands elsewhere.
    /// </remarks>
    public bool IsFavorites { get; } = isFavorites;

    public ICommand? ChangeIconCommand { get; } = changeIconCommand;

    public static string ChangeIconLabel => Ui.Connections.ChooseIcon;

    private bool _isExpanded = true;

    public string Name { get; } = name;

    public ObservableCollection<ConnectionRowModel> Connections { get; } = [.. connections];

    public bool IsEmpty => Connections.Count == 0;

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
        }
    }

    public ICommand RenameCommand { get; } = renameCommand;

    public ICommand RemoveCommand { get; } = removeCommand;

    public static string RenameLabel => Ui.Connections.RenameGroup;

    public static string RemoveLabel => Ui.Connections.RemoveGroup;

    public event PropertyChangedEventHandler? PropertyChanged;
}
