using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;

namespace StorageHub.Desktop.Views;

/// <summary>A row of the key store table.</summary>
/// <param name="EntryId">Not shown. It is what selecting the row makes the entry in hand.</param>
internal sealed record KeyStoreRow(
    Guid EntryId,
    string Name,
    string Kind,
    string Identity,
    string Expires,
    KeyStoreExpiry Expiry,
    string UsedBy,
    string Tags)
{
    public bool IsExpiringSoon => Expiry == KeyStoreExpiry.ExpiringSoon;

    public bool IsExpired => Expiry == KeyStoreExpiry.Expired;
}

/// <summary>
/// The key store: what is imported, and importing, renaming and deleting it.
/// </summary>
/// <remarks>
/// <para>
/// A table and four buttons, out of 683 lines of WinForms and its three nested prompts. The
/// prompts became one import dialog -- <see cref="KeyStoreImportModel"/> -- because asking for the
/// envelope, then the file, then the passphrase, then the name, one modal after another, is four
/// chances to cancel and no way to go back.
/// </para>
/// <para>
/// Deleting confirms, and refuses outright while a connection still names the entry: the agent
/// would refuse too, but the listing knows which connection and can say so before asking.
/// </para>
/// </remarks>
internal sealed class KeyStoreModel : INotifyPropertyChanged
{
    private readonly KeyStoreController? _controller;
    private readonly IDialogService? _dialogs;
    private readonly Func<KeyStoreMaterialKind, Task<KeyStoreImportDraft?>>? _import;
    private readonly Dictionary<Guid, KeyStoreEntryDocument> _entriesById = [];
    private KeyStoreRow? _selectedRow;
    private string _searchText = string.Empty;
    private StatusLine _status = StatusLine.Muted(Ui.KeyStore.Loading);
    private bool _isBusy;
    private bool _reloadPending;

    /// <param name="import">
    /// How the screen asks for what to import. The window supplies a dialog; a test supplies the
    /// answer. Null leaves both import buttons unavailable.
    /// </param>
    internal KeyStoreModel(
        KeyStoreController? controller = null,
        IDialogService? dialogs = null,
        Func<KeyStoreMaterialKind, Task<KeyStoreImportDraft?>>? import = null)
    {
        _controller = controller;
        _dialogs = dialogs;
        _import = import;

        RefreshCommand = new RelayCommand(_ => _ = LoadAsync(), _ => Live && !IsBusy);
        ImportCertificateCommand = new RelayCommand(
            _ => _ = ImportAsync(KeyStoreMaterialKind.Pkcs12Certificate), _ => CanImport);
        ImportKeyCommand = new RelayCommand(
            _ => _ = ImportAsync(KeyStoreMaterialKind.SshPrivateKey), _ => CanImport);
        RenameCommand = new RelayCommand(_ => _ = RenameAsync(), _ => CanChangeSelected);
        DeleteCommand = new RelayCommand(_ => _ = DeleteAsync(), _ => CanChangeSelected);
    }

    /// <summary>A store with nothing behind it, for a preview or a layout test.</summary>
    internal static KeyStoreModel Create() => new();

    /// <summary>And one that will ask the agent.</summary>
    internal static KeyStoreModel Create(
        Func<IKeyStoreAgentClient> clients,
        Func<IRemoteSecretVaultClient> vaults,
        IDialogService? dialogs = null,
        Func<KeyStoreMaterialKind, Task<KeyStoreImportDraft?>>? import = null) =>
        new(new KeyStoreController(clients, vaults), dialogs, import);

    public ObservableCollection<KeyStoreRow> Entries { get; } = [];

    /// <summary>The entry in hand, which is what Rename and Delete act on.</summary>
    public KeyStoreRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (!Set(ref _selectedRow, value)) return;
            RaiseCommands();
        }
    }

    /// <summary>
    /// Narrows the table as it is typed.
    /// </summary>
    /// <remarks>
    /// The agent does the matching, on name and description, so every change is a request. One
    /// typed while a request is in flight is not dropped: it is answered by one more request once
    /// the current one returns, which is what makes a fast typist see the last thing they typed.
    /// </remarks>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!Set(ref _searchText, value ?? string.Empty)) return;
            _ = LoadAsync();
        }
    }

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

    public ICommand RefreshCommand { get; }

    public ICommand ImportCertificateCommand { get; }

    public ICommand ImportKeyCommand { get; }

    public ICommand RenameCommand { get; }

    public ICommand DeleteCommand { get; }

    /// <summary>The entry the selected row stands for, or nothing.</summary>
    internal KeyStoreEntryDocument? Selected =>
        _selectedRow is { } row && _entriesById.TryGetValue(row.EntryId, out var entry) ? entry : null;

    /// <summary>Reads what is stored, narrowed by the search box.</summary>
    internal async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_controller is null) return;
        if (IsBusy)
        {
            _reloadPending = true;
            return;
        }

        IsBusy = true;
        try
        {
            do
            {
                _reloadPending = false;
                await RefreshListAsync(cancellationToken).ConfigureAwait(true);
            }
            while (_reloadPending);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Asks what to import, then imports it.</summary>
    internal async Task ImportAsync(KeyStoreMaterialKind kind, CancellationToken cancellationToken = default)
    {
        if (_controller is null || _import is null || IsBusy) return;

        var draft = await _import(kind).ConfigureAwait(true);
        if (draft is null) return;

        IsBusy = true;
        try
        {
            Status = StatusLine.Muted(Ui.KeyStore.Importing);
            var result = await _controller.ImportAsync(draft, cancellationToken).ConfigureAwait(true);
            if (!result.Changed)
            {
                Status = new StatusLine(result.ErrorMessage!, MetricTone.Danger);
                return;
            }

            await RefreshListAsync(cancellationToken).ConfigureAwait(true);
            if (result.Entry is { } entry) Select(entry.EntryId);
            Status = new StatusLine(
                Ui.Format(Ui.KeyStore.ImportedFormat, draft.DisplayName.Trim()), MetricTone.Success);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            EndBusy();
        }
    }

    /// <summary>Asks for a new name, then renames the entry in hand.</summary>
    internal async Task RenameAsync(CancellationToken cancellationToken = default)
    {
        if (_controller is null || _dialogs is null || IsBusy || Selected is not { } entry) return;

        var name = await _dialogs.PromptAsync(
            new DialogPromptRequest
            {
                Title = Ui.KeyStore.RenameEntry,
                Label = Ui.KeyStore.Name,
                Value = entry.DisplayName,
                Accept = Ui.Dialogs.ButtonOk,
                Validate = KeyStoreRules.DescribeBadName
            },
            cancellationToken).ConfigureAwait(true);
        if (name is null) return;

        IsBusy = true;
        try
        {
            var result = await _controller.RenameAsync(entry, name, cancellationToken).ConfigureAwait(true);
            if (!result.Changed)
            {
                Status = new StatusLine(result.ErrorMessage!, MetricTone.Danger);
                return;
            }

            await RefreshListAsync(cancellationToken).ConfigureAwait(true);
            Select(entry.EntryId);
            Status = new StatusLine(Ui.KeyStore.Renamed, MetricTone.Success);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            EndBusy();
        }
    }

    /// <summary>
    /// Confirms, then deletes the entry in hand.
    /// </summary>
    /// <remarks>
    /// Refused before asking when a connection still uses it, naming the connection: a confirmation
    /// for something that is then refused anyway teaches people to click through the one that is
    /// not. The material cannot be recovered afterwards, which is what the confirmation says.
    /// </remarks>
    internal async Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        if (_controller is null || _dialogs is null || IsBusy || Selected is not { } entry) return;

        if (entry.ReferencedByProfiles.Length > 0)
        {
            Status = new StatusLine(
                Ui.Format(
                    Ui.KeyStore.StillUsedByFormat,
                    entry.DisplayName,
                    string.Join(", ", entry.ReferencedByProfiles)),
                MetricTone.Danger);
            return;
        }

        var choice = await _dialogs.ConfirmAsync(
            new DialogRequest
            {
                Title = Ui.Dialogs.DeleteStoredKeyCaption,
                Message = Ui.Format(Ui.Dialogs.DeleteStoredKeyPromptFormat, entry.DisplayName),
                Severity = DialogSeverity.Warning,
                Buttons = DialogButtons.YesNo,
                Default = DialogChoice.No
            },
            cancellationToken).ConfigureAwait(true);
        if (choice != DialogChoice.Yes) return;

        IsBusy = true;
        try
        {
            var result = await _controller.DeleteAsync(entry, cancellationToken).ConfigureAwait(true);
            if (!result.Changed)
            {
                Status = new StatusLine(result.ErrorMessage!, MetricTone.Danger);
                return;
            }

            await RefreshListAsync(cancellationToken).ConfigureAwait(true);
            Status = new StatusLine(Ui.KeyStore.Deleted, MetricTone.Success);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            EndBusy();
        }
    }

    private async Task RefreshListAsync(CancellationToken cancellationToken)
    {
        var text = SearchText.Trim();
        var listing = await _controller!.ListAsync(text, null, cancellationToken).ConfigureAwait(true);
        Show(listing);
        Status = new StatusLine(
            listing.Describe(searched: text.Length > 0),
            listing.Failed ? MetricTone.Danger : MetricTone.Neutral);
    }

    private void Show(KeyStoreListing listing)
    {
        var selected = _selectedRow?.EntryId;
        _entriesById.Clear();
        Entries.Clear();
        foreach (var entry in listing.Entries.OrderBy(
            static entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            _entriesById[entry.EntryId] = entry;
            Entries.Add(Row(entry));
        }

        if (selected is { } id) Select(id);
    }

    /// <summary>Puts the row for an entry in hand, when the table has it.</summary>
    private void Select(Guid entryId) =>
        SelectedRow = Entries.FirstOrDefault(row => row.EntryId == entryId);

    private static KeyStoreRow Row(KeyStoreEntryDocument entry)
    {
        var now = DateTimeOffset.UtcNow;
        return new KeyStoreRow(
            entry.EntryId,
            entry.DisplayName,
            UiEnumNames.Describe(entry.Kind),
            KeyStoreRules.DescribeIdentity(entry),
            KeyStoreRules.DescribeExpiry(entry, now),
            KeyStoreRules.Expiry(entry, now),
            entry.ReferencedByProfiles.Length.ToString(CultureInfo.CurrentCulture),
            string.Join(", ", entry.Tags));
    }

    /// <summary>Ends a busy scope, and answers a search typed during it.</summary>
    private void EndBusy()
    {
        IsBusy = false;
        if (_reloadPending) _ = LoadAsync();
    }

    private bool Live => _controller is not null;

    private bool CanImport => Live && !IsBusy && _import is not null;

    private bool CanChangeSelected => Live && !IsBusy && _dialogs is not null && _selectedRow is not null;

    private void RaiseCommands()
    {
        foreach (var command in new[]
            { RefreshCommand, ImportCertificateCommand, ImportKeyCommand, RenameCommand, DeleteCommand })
        {
            (command as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public static string Title => Ui.KeyStore.KeyStore;

    public static string ImportCertificateLabel => Ui.KeyStore.ImportCertificate;

    public static string ImportKeyLabel => Ui.KeyStore.ImportSSHKey;

    public static string RenameLabel => Ui.KeyStore.Rename;

    public static string DeleteLabel => Ui.KeyStore.Delete;

    public static string RefreshLabel => Ui.KeyStore.Refresh;

    public static string SearchPlaceholder => Ui.KeyStore.SearchNameOrDescription;

    public static string SearchAccessibleName => Ui.KeyStore.KeyStoreSearch;

    public static string ListAccessibleName => Ui.KeyStore.StoredKeysAndCertificates;

    public static string ColumnName => Ui.KeyStore.Name;

    public static string ColumnKind => Ui.KeyStore.Kind;

    public static string ColumnIdentity => Ui.KeyStore.Identity;

    public static string ColumnExpires => Ui.KeyStore.Expires;

    public static string ColumnUsedBy => Ui.KeyStore.UsedBy;

    public static string ColumnTags => Ui.KeyStore.Tags;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
