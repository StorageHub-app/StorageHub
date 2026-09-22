using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using Lucide.Avalonia;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>One row of the picker: a group heading, or a connection.</summary>
internal sealed record ConnectionPickerRowModel(ConnectionPickerRow Row, bool IsActive)
{
    public bool IsHeader => Row.IsHeader;

    public bool IsCard => !Row.IsHeader;

    /// <summary>The heading, in capitals, as the 1.x picker drew it.</summary>
    public string Heading => (Row.GroupLabel ?? string.Empty).ToUpper(CultureInfo.CurrentCulture);

    public string Name => Row.Card?.Name ?? string.Empty;

    public string Endpoint => Row.Card?.Endpoint ?? string.Empty;

    public bool IsClient => Row.Card?.Type == ConnectionProfileType.Client;

    /// <summary>What the connection is and speaks, e.g. "STORAGE · SFTP".</summary>
    public string Badge => Row.Card is not { } card
        ? string.Empty
        : card.Type == ConnectionProfileType.Client
            ? Ui.Format(Ui.Connections.PickerClientBadgeFormat, Protocol(card))
            : card.Provider == StorageProviderKind.Local
                ? Ui.Connections.PickerSystemLocalBadge
                : Ui.Format(Ui.Connections.PickerStorageBadgeFormat, Protocol(card));

    public LucideIconKind Icon => Row.Card is not { } card
        ? LucideIconKind.Cloud
        : Themes.IconCatalog.Resolve(
              ConnectionIconCatalog.ResolveForConnection(card.IconKey, card.Provider, card.Type))
          ?? LucideIconKind.Cloud;

    public static string ActiveBadge => Ui.Connections.PickerActiveBadge;

    /// <summary>The protocol's own acronym, which is the same in every language.</summary>
    private static string Protocol(ConnectionCardModel card) =>
        card.Provider.ToString().ToUpperInvariant();
}

/// <summary>
/// The searchable connection chooser under a pane's connection button.
/// </summary>
/// <remarks>
/// <para>
/// A drop-down list stops scaling once somebody has more than a handful of saved connections: it
/// can only be scrolled or first-letter matched. This one groups them, marks the one already open,
/// and filters as it is typed into, with the arrow keys and Enter driving the list from the search
/// box so a hand never has to leave the keyboard.
/// </para>
/// <para>
/// The rules are <see cref="ConnectionPickerSession"/>'s. What is here is the rows as the view
/// draws them and the one event the pane listens for.
/// </para>
/// </remarks>
internal sealed class ConnectionPickerModel : INotifyPropertyChanged
{
    private readonly ConnectionPickerSession _session;

    internal ConnectionPickerModel(IEnumerable<ConnectionCardModel> cards, ConnectionCardModel? active)
    {
        _session = new ConnectionPickerSession(cards, active);
        ChooseCommand = new RelayCommand(_ => Choose());
        Rebuild();
    }

    public ObservableCollection<ConnectionPickerRowModel> Rows { get; } = [];

    public string Search
    {
        get => _session.Query;
        set
        {
            if (string.Equals(_session.Query, value ?? string.Empty, StringComparison.Ordinal)) return;
            _session.Query = value ?? string.Empty;
            Raise(nameof(Search));
            Rebuild();
        }
    }

    /// <summary>
    /// The highlighted row.
    /// </summary>
    /// <remarks>
    /// Two-way with the list. A heading cannot be highlighted, so a list that tries is told where
    /// the highlight really is.
    /// </remarks>
    public int Highlighted
    {
        get => _session.Highlighted;
        set
        {
            _session.HighlightRow(value);
            Raise(nameof(Highlighted));
        }
    }

    public bool IsEmpty => _session.IsEmpty;

    public bool HasRows => !_session.IsEmpty;

    public ICommand ChooseCommand { get; }

    /// <summary>Raised with the connection chosen, which the pane opens.</summary>
    internal event EventHandler<ConnectionCardModel>? Chosen;

    /// <summary>Moves the highlight up or down, for the arrow keys in the search box.</summary>
    internal void Move(int direction)
    {
        _session.Move(direction);
        Raise(nameof(Highlighted));
    }

    /// <summary>Chooses the highlighted connection, or does nothing when there is none.</summary>
    internal bool Choose()
    {
        if (_session.HighlightedCard is not { } card) return false;
        Chosen?.Invoke(this, card);
        return true;
    }

    private void Rebuild()
    {
        Rows.Clear();
        foreach (var row in _session.Rows)
        {
            Rows.Add(new ConnectionPickerRowModel(row, row.Card is { } card && _session.IsActive(card)));
        }

        Raise(nameof(IsEmpty));
        Raise(nameof(HasRows));
        Raise(nameof(Highlighted));
    }

    public static string SearchPlaceholder => Ui.Connections.PickerSearchPlaceholder;

    public static string ListAccessibleName => Ui.Connections.PickerListAccessibleName;

    public static string NoMatch => Ui.Connections.PickerNoMatch;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
