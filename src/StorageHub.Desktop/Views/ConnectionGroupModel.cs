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
internal sealed record ConnectionRowModel(ConnectionCardModel Card)
{
    public Guid Id => Card.ConnectionId ?? Guid.Empty;

    public string Name => Card.Name;

    public string Endpoint => Card.Endpoint;

    public string State => Card.State;

    public bool IsEnabled => Card.IsEnabled;

    public string Badge => ConnectionGrouping.BadgeFor(Card);

    /// <summary>Whether this is a shell rather than storage, which the badge is coloured by.</summary>
    public bool IsClient => Card.Type == Contracts.Ipc.ConnectionProfileType.Client;
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
    ICommand removeCommand) : INotifyPropertyChanged
{
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
    /// Not saved. A collapsed group is a way to get something out of the way while doing something
    /// else, and reopening the shell is the clearest signal that the something else is over.
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
