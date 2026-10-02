using System.Collections.ObjectModel;
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
    string Created,
    string Expires,
    KeyStoreExpiry Expiry,
    string UsedBy)
{
    public bool IsExpiringSoon => Expiry == KeyStoreExpiry.ExpiringSoon;

    public bool IsExpired => Expiry == KeyStoreExpiry.Expired;
}

/// <summary>
/// Chooses a key or certificate from the Key Store for a connection field, or imports one there
/// and chooses that.
/// </summary>
/// <remarks>
/// <para>
/// The list arrives pre-filtered to the kind the field accepts, so a certificate can never be
/// offered where an SSH key is required. Only metadata is shown; the material itself never leaves
/// the vault. Answers with an entry, or with nothing when dismissed, the same contract every
/// dialog in the shell has.
/// </para>
/// <para>
/// It opens even on an empty store, saying so over the empty table, because Import is right there: 1.4
/// opened its picker either way, and a button that only puts a sentence in a footer reads as one
/// that does nothing. Import is the Key Store's own, the same dialog, rules and warning, and the
/// entry it makes is the one selected afterwards, so Import then Use is the whole job.
/// </para>
/// </remarks>
internal sealed class KeyStorePickerModel : INotifyPropertyChanged
{
    private readonly Dictionary<Guid, KeyStoreEntryDocument> _entriesById = [];
    private readonly KeyStoreController? _controller;
    private readonly Func<KeyStoreMaterialKind, Task<KeyStoreImportDraft?>>? _askImport;
    private KeyStorePickerRow? _selectedRow;
    private StatusLine _status = StatusLine.Muted(string.Empty);
    private bool _isBusy;

    /// <param name="kind">
    /// What the field accepts. Defaults to the entries' own kind, and to an SSH key for none.
    /// </param>
    /// <param name="controller">Imports into the Key Store and lists it again. Null leaves Import dim.</param>
    /// <param name="askImport">
    /// Asks what to import: the window supplies the Key Store's import dialog; a test supplies the
    /// answer. Null leaves Import dim.
    /// </param>
    internal KeyStorePickerModel(
        IReadOnlyList<KeyStoreEntryDocument> entries,
        KeyStoreMaterialKind? kind = null,
        KeyStoreController? controller = null,
        Func<KeyStoreMaterialKind, Task<KeyStoreImportDraft?>>? askImport = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Kind = kind ?? (entries.Count > 0 ? entries[0].Kind : KeyStoreMaterialKind.SshPrivateKey);
        _controller = controller;
        _askImport = askImport;

        UseCommand = new RelayCommand(_ => Finish(Selected), _ => Selected is not null && !_isBusy);
        CancelCommand = new RelayCommand(_ => Finish(null));
        ImportCommand = new RelayCommand(_ => _ = ImportAsync(), _ => CanImport);

        Show(entries);

        // The first entry starts chosen, so Enter on a store with one key is enough.
        SelectedRow = Entries.FirstOrDefault();
    }

    /// <summary>The kind of material the field takes, which is all the list shows and all Import makes.</summary>
    public KeyStoreMaterialKind Kind { get; }

    public ObservableCollection<KeyStorePickerRow> Entries { get; } = [];

    public KeyStorePickerRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (!Set(ref _selectedRow, value)) return;
            RaiseCommands();
        }
    }

    /// <summary>What the last import did, or nothing yet.</summary>
    public StatusLine Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            RaiseCommands();
        }
    }

    public ICommand UseCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand ImportCommand { get; }

    /// <summary>The entry the selected row stands for, or nothing.</summary>
    internal KeyStoreEntryDocument? Selected =>
        _selectedRow is { } row && _entriesById.TryGetValue(row.EntryId, out var entry) ? entry : null;

    /// <summary>The entry chosen, or nothing when the dialog was dismissed.</summary>
    internal KeyStoreEntryDocument? Chosen { get; private set; }

    /// <summary>Raised once there is an answer and the window should close.</summary>
    internal event EventHandler? Closed;

    /// <summary>
    /// Asks what to import, imports it into the Key Store, and selects it.
    /// </summary>
    /// <remarks>
    /// The list is read again afterwards rather than the new entry added by hand, so what is shown
    /// is what the agent holds, including what it derived from the material, such as the
    /// fingerprint. A listing that fails after an import that worked still shows the new entry.
    /// </remarks>
    internal async Task ImportAsync(CancellationToken cancellationToken = default)
    {
        if (!CanImport) return;

        var draft = await _askImport!(Kind).ConfigureAwait(true);
        if (draft is null) return;

        IsBusy = true;
        try
        {
            Status = StatusLine.Muted(Ui.KeyStore.Importing);
            var result = await _controller!.ImportAsync(draft, cancellationToken).ConfigureAwait(true);
            if (!result.Changed)
            {
                Status = new StatusLine(result.ErrorMessage!, MetricTone.Danger);
                return;
            }

            var listing = await _controller.ListAsync(null, Kind, cancellationToken).ConfigureAwait(true);
            var entries = listing.Failed ? [.. _entriesById.Values] : listing.Entries.ToList();
            if (result.Entry is { } added && entries.All(entry => entry.EntryId != added.EntryId))
            {
                entries.Add(added);
            }

            Show(entries);
            if (result.Entry is { } entry)
            {
                SelectedRow = Entries.FirstOrDefault(row => row.EntryId == entry.EntryId);
            }

            Status = KeyStoreModel.DescribeImported(draft);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Show(IEnumerable<KeyStoreEntryDocument> entries)
    {
        _entriesById.Clear();
        Entries.Clear();
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in entries
            .Where(entry => entry.Kind == Kind)
            .OrderBy(static entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            _entriesById[entry.EntryId] = entry;
            Entries.Add(new KeyStorePickerRow(
                entry.EntryId,
                entry.DisplayName,
                KeyStoreRules.DescribeIdentity(entry),
                entry.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.CurrentCulture),
                KeyStoreRules.DescribeExpiry(entry, now),
                KeyStoreRules.Expiry(entry, now),
                entry.ReferencedByProfiles.Length.ToString(CultureInfo.CurrentCulture)));
        }

        Raise(nameof(IsEmpty));
    }

    private void Finish(KeyStoreEntryDocument? chosen)
    {
        Chosen = chosen;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private bool CanImport => _controller is not null && _askImport is not null && !_isBusy;

    private void RaiseCommands()
    {
        (UseCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ImportCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Whether the store has nothing of this kind, which the window says across the empty table
    /// rather than in a row: the sentence is longer than the Name column, and it is not an entry.
    /// </summary>
    public bool IsEmpty => _entriesById.Count == 0;

    public string EmptyMessage => Kind is KeyStoreMaterialKind.Pkcs12Certificate
        ? Ui.KeyStore.NoCertificatesYet
        : Ui.KeyStore.NoSshKeysYet;

    public string ImportLabel => Kind is KeyStoreMaterialKind.Pkcs12Certificate
        ? Ui.KeyStore.ImportCertificate
        : Ui.KeyStore.ImportSSHKey;

    public static string Title => Ui.KeyStore.ChooseFromKeyStore;

    public static string Hint => Ui.KeyStore.PickerHint;

    public static string UseLabel => Ui.KeyStore.Use;

    public static string CancelLabel => Ui.KeyStore.Cancel;

    public static string ListAccessibleName => Ui.KeyStore.StoredKeysAndCertificates;

    public static string ColumnName => Ui.KeyStore.Name;

    public static string ColumnIdentity => Ui.KeyStore.Identity;

    public static string ColumnCreated => Ui.KeyStore.Created;

    public static string ColumnExpires => Ui.KeyStore.Expires;

    public static string ColumnUsedBy => Ui.KeyStore.UsedBy;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
