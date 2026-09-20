using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;

namespace StorageHub.Desktop.Views;

/// <summary>One private-key envelope, in the picker.</summary>
internal sealed record KeyFormatChoice(KeyStorePrivateKeyFormat Format, string Caption)
{
    public override string ToString() => Caption;
}

/// <summary>
/// What to import: the file, what protects it, and what to call it.
/// </summary>
/// <remarks>
/// <para>
/// One dialog where 1.x had a chain of three, plus the file picker between them. The kind is
/// decided by which button opened it, so a certificate is never asked which SSH envelope it uses,
/// and the name is suggested from the file so the common case is Browse, passphrase, Import.
/// </para>
/// <para>
/// The Import button is dim with the reason showing until the draft is one the controller will
/// take -- the same rules, <see cref="KeyStoreRules.Validate"/>, so a draft this accepts is one
/// the agent will be asked to store.
/// </para>
/// </remarks>
internal sealed class KeyStoreImportModel : INotifyPropertyChanged
{
    private readonly IFilePickerService? _files;
    private string _filePath = string.Empty;
    private string _displayName = string.Empty;
    private string _suggestedName = string.Empty;
    private string _passphrase = string.Empty;
    private KeyFormatChoice _format;

    internal KeyStoreImportModel(KeyStoreMaterialKind kind, IFilePickerService? files = null)
    {
        Kind = kind;
        _files = files;
        _format = Formats[0];

        BrowseCommand = new RelayCommand(_ => _ = BrowseAsync(), _ => _files is not null);
        ImportCommand = new RelayCommand(_ => Finish(Draft()), _ => !HasProblem);
        CancelCommand = new RelayCommand(_ => Finish(null));
    }

    public KeyStoreMaterialKind Kind { get; }

    public static IReadOnlyList<KeyFormatChoice> Formats { get; } =
        [.. Enum.GetValues<KeyStorePrivateKeyFormat>()
            .Select(static format => new KeyFormatChoice(format, UiEnumNames.Describe(format)))];

    public string Title => Kind is KeyStoreMaterialKind.Pkcs12Certificate
        ? Ui.KeyStore.ImportCertificateTitle
        : Ui.KeyStore.ImportSshKeyTitle;

    public string FilePath
    {
        get => _filePath;
        set
        {
            if (!Set(ref _filePath, value ?? string.Empty)) return;
            SuggestName();
            RaiseProblem();
        }
    }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (!Set(ref _displayName, value ?? string.Empty)) return;
            RaiseProblem();
        }
    }

    public string Passphrase
    {
        get => _passphrase;
        set
        {
            if (!Set(ref _passphrase, value ?? string.Empty)) return;
            RaiseProblem();
        }
    }

    public KeyFormatChoice Format
    {
        get => _format;
        set
        {
            if (value is null || !Set(ref _format, value)) return;
            RaiseProblem();
        }
    }

    /// <summary>Only an SSH key declares its envelope; a certificate has one shape.</summary>
    public bool ShowsFormat => Kind is KeyStoreMaterialKind.SshPrivateKey;

    public string PassphraseLabel => Kind is KeyStoreMaterialKind.Pkcs12Certificate
        ? Ui.KeyStore.CertificatePassword
        : Ui.KeyStore.KeyPassphrase;

    /// <summary>A bundle may have no password; a key must have a passphrase. Say which.</summary>
    public string PassphraseHint => Kind is KeyStoreMaterialKind.Pkcs12Certificate
        ? Ui.KeyStore.CertificatePasswordHint
        : Ui.KeyStore.StorageHubCannotStoreAnUnprotectedPrivateKey;

    /// <summary>Why the draft will not do, or nothing.</summary>
    public string Problem => KeyStoreRules.Validate(Draft()) ?? string.Empty;

    public bool HasProblem => Problem.Length > 0;

    public ICommand BrowseCommand { get; }

    public ICommand ImportCommand { get; }

    public ICommand CancelCommand { get; }

    /// <summary>The draft to import, or nothing when the dialog was dismissed.</summary>
    internal KeyStoreImportDraft? Result { get; private set; }

    /// <summary>Raised once there is an answer and the window should close.</summary>
    internal event EventHandler? Closed;

    /// <summary>The draft as the fields stand.</summary>
    internal KeyStoreImportDraft Draft() => new(
        Kind,
        FilePath.Trim(),
        DisplayName,
        Passphrase,
        ShowsFormat ? Format.Format : null);

    /// <summary>Opens the platform's picker, filtered to what this kind of material looks like.</summary>
    internal async Task BrowseAsync(CancellationToken cancellationToken = default)
    {
        if (_files is null) return;

        var picked = await _files.PickFileAsync(
            new FilePickerRequest
            {
                Title = Ui.KeyStore.SelectMaterial,
                Filters = Kind is KeyStoreMaterialKind.Pkcs12Certificate
                    ?
                    [
                        new FilePickerFilter(Ui.KeyStore.CertificateFiles, ["pfx", "p12"]),
                        new FilePickerFilter(Ui.KeyStore.AllFiles, ["*"])
                    ]
                    :
                    [
                        new FilePickerFilter(Ui.KeyStore.PrivateKeyFiles, ["key", "pem"]),
                        new FilePickerFilter(Ui.KeyStore.AllFiles, ["*"])
                    ],
                SuggestedDirectory = Path.GetDirectoryName(FilePath)
            },
            cancellationToken).ConfigureAwait(true);

        if (picked is not null) FilePath = picked;
    }

    /// <summary>
    /// Names the entry after the file, unless a name has been typed.
    /// </summary>
    /// <remarks>
    /// The suggestion is remembered so choosing a second file replaces it, while a name somebody
    /// typed over it is left alone. That is the difference between a default and a nuisance.
    /// </remarks>
    private void SuggestName()
    {
        var suggestion = Path.GetFileNameWithoutExtension(FilePath.Trim());
        if (DisplayName.Length == 0 || string.Equals(DisplayName, _suggestedName, StringComparison.Ordinal))
        {
            DisplayName = suggestion;
        }

        _suggestedName = suggestion;
    }

    private void Finish(KeyStoreImportDraft? result)
    {
        Result = result;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseProblem()
    {
        Raise(nameof(Problem));
        Raise(nameof(HasProblem));
        (ImportCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public static string FileLabel => Ui.KeyStore.File;

    public static string BrowseLabel => Ui.KeyStore.Browse;

    public static string NameLabel => Ui.KeyStore.Name;

    public static string FormatLabel => Ui.KeyStore.PrivateKeyFormatLabel;

    public static string ImportLabel => Ui.KeyStore.Import;

    public static string CancelLabel => Ui.KeyStore.Cancel;

    public static string Hint => Ui.KeyStore.ImportHint;

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
