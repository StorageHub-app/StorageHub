using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>A row of the picker: enough to recognise an entry, nothing that could unlock it.</summary>
internal sealed record KeyStorePickerRow(
    Guid EntryId,
    string Name,
    string Identity,
    string Expires,
    KeyStoreExpiry Expiry,
    string UsedBy)
{
    public bool IsExpiringSoon => Expiry == KeyStoreExpiry.ExpiringSoon;

    public bool IsExpired => Expiry == KeyStoreExpiry.Expired;
}

/// <summary>
/// Chooses an already-imported key or certificate for a connection field.
/// </summary>
/// <remarks>
/// The list arrives pre-filtered to the kind the field accepts, so a certificate can never be
/// offered where an SSH key is required. Only metadata is shown; the material itself never leaves
/// the vault. Answers with an entry, or with nothing when dismissed -- the same contract every
/// dialog in the shell has.
/// </remarks>
internal sealed class KeyStorePickerModel : INotifyPropertyChanged
{
    private readonly Dictionary<Guid, KeyStoreEntryDocument> _entriesById = [];
    private KeyStorePickerRow? _selectedRow;

    internal KeyStorePickerModel(IReadOnlyList<KeyStoreEntryDocument> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in entries.OrderBy(
            static entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            _entriesById[entry.EntryId] = entry;
            Entries.Add(new KeyStorePickerRow(
                entry.EntryId,
                entry.DisplayName,
                entry.Summary.Subject ?? entry.Summary.Sha256Fingerprint ?? Ui.KeyStore.NoExpiry,
                KeyStoreRules.DescribeExpiry(entry, now),
                KeyStoreRules.Expiry(entry, now),
                entry.ReferencedByProfiles.Length.ToString(CultureInfo.CurrentCulture)));
        }

        // The first entry starts chosen, so Enter on a store with one key is enough.
        _selectedRow = Entries.FirstOrDefault();

        UseCommand = new RelayCommand(_ => Finish(Selected), _ => _selectedRow is not null);
        CancelCommand = new RelayCommand(_ => Finish(null));
    }

    public List<KeyStorePickerRow> Entries { get; } = [];

    public KeyStorePickerRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (!Set(ref _selectedRow, value)) return;
            (UseCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public ICommand UseCommand { get; }

    public ICommand CancelCommand { get; }

    /// <summary>The entry the selected row stands for, or nothing.</summary>
    internal KeyStoreEntryDocument? Selected =>
        _selectedRow is { } row && _entriesById.TryGetValue(row.EntryId, out var entry) ? entry : null;

    /// <summary>The entry chosen, or nothing when the dialog was dismissed.</summary>
    internal KeyStoreEntryDocument? Chosen { get; private set; }

    /// <summary>Raised once there is an answer and the window should close.</summary>
    internal event EventHandler? Closed;

    private void Finish(KeyStoreEntryDocument? chosen)
    {
        Chosen = chosen;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    public static string Title => Ui.KeyStore.ChooseFromKeyStore;

    public static string Hint => Ui.KeyStore.PickerHint;

    public static string UseLabel => Ui.KeyStore.Use;

    public static string CancelLabel => Ui.KeyStore.Cancel;

    public static string ListAccessibleName => Ui.KeyStore.StoredKeysAndCertificates;

    public static string ColumnName => Ui.KeyStore.Name;

    public static string ColumnIdentity => Ui.KeyStore.Identity;

    public static string ColumnExpires => Ui.KeyStore.Expires;

    public static string ColumnUsedBy => Ui.KeyStore.UsedBy;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
