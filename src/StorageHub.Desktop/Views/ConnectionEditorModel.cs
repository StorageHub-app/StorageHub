using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Input;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;

namespace StorageHub.Desktop.Views;

/// <summary>
/// One field in the connection editor: what it is called, what kind it is, and what is in it.
/// </summary>
/// <remarks>
/// Built from a <see cref="ConnectionFieldDescriptor"/> rather than written out, because the
/// descriptors are what decide which fields a provider has -- six providers with three sections
/// each is around two hundred rows, and hand-written markup for them would be two hundred places
/// for the editor and the draft factory to disagree about a field's key.
/// </remarks>
internal sealed class ConnectionFieldModel(ConnectionFieldDescriptor descriptor, string value)
    : INotifyPropertyChanged
{
    private string _value = value;

    public string Key => descriptor.Key;

    public string Label => descriptor.Label;

    public ConnectionFieldKind Kind => descriptor.Kind;

    public bool Required => descriptor.Required;

    public string Placeholder => descriptor.Placeholder;

    public string HelpText => descriptor.HelpText;

    public bool HasHelp => descriptor.HelpText.Length > 0;

    public IReadOnlyList<string> Choices => descriptor.Choices ?? [];

    /// <summary>Which editor to draw, because a binding cannot switch on an enum.</summary>
    public bool IsChoice => Kind == ConnectionFieldKind.Choice;

    /// <inheritdoc cref="IsChoice"/>
    public bool IsToggle => Kind == ConnectionFieldKind.Toggle;

    /// <summary>
    /// A reference to something the vault holds, which is never typed.
    /// </summary>
    /// <remarks>
    /// The box is read-only and shows the opaque reference; the buttons beside it are how it
    /// changes. Enrol sends a secret to the vault and puts the reference it answers with here.
    /// Delete removes the vault entry. A material field can also borrow an entry from the key
    /// store instead of enrolling a second copy of the same file.
    /// </remarks>
    public bool IsSecret => Kind is ConnectionFieldKind.SecretReference or ConnectionFieldKind.CertificateReference;

    /// <summary>Whether the key store can fill this field. Only the two material fields.</summary>
    public bool IsKeyStoreSlot => ConnectionSecretFields.KeyStoreSlot(Key) is not null;

    /// <summary>
    /// Everything that is typed into a box, which is every other kind.
    /// </summary>
    /// <remarks>
    /// Including a fingerprint, which names something rather than carrying it, so the editor's
    /// job is the name.
    /// </remarks>
    public bool IsText => !IsChoice && !IsToggle && !IsSecret && !IsIcon;

    /// <summary>An icon, shown as itself with a button to choose another.</summary>
    public bool IsIcon => Kind == ConnectionFieldKind.Icon;

    /// <summary>
    /// What the icon row shows. Set by the editor, because an empty value means the provider's own
    /// icon and only the editor knows which provider this is.
    /// </summary>
    internal Func<string, Lucide.Avalonia.LucideIconKind>? IconResolver { get; set; }

    public Lucide.Avalonia.LucideIconKind IconKind =>
        IconResolver?.Invoke(_value) ?? Lucide.Avalonia.LucideIconKind.Cloud;

    /// <summary>Opens the icon picker. Set by the editor, for the icon row.</summary>
    public ICommand? ChooseIconCommand { get; internal set; }

    public static string ChooseIconLabel => Ui.Connections.ChooseIcon;

    /// <summary>Sends a secret to the vault and keeps its reference. Set by the editor.</summary>
    public ICommand? EnrollCommand { get; internal set; }

    /// <summary>Removes the vault entry this field names. Set by the editor.</summary>
    public ICommand? DeleteSecretCommand { get; internal set; }

    /// <summary>Borrows an entry from the key store. Set by the editor, for material fields.</summary>
    public ICommand? ChooseFromKeyStoreCommand { get; internal set; }

    public static string SecretHint => Ui.ConnectionEditor.VaultReferenceHint;

    public static string EnrollLabel => Ui.ConnectionEditor.EnrollOrReplace;

    public static string DeleteSecretLabel => Ui.ConnectionEditor.Delete;

    public static string KeyStoreLabel => Ui.ConnectionEditor.KeyStore;

    /// <summary>A toggle field stores "true" or "false", so it is read and written as text.</summary>
    public bool IsOn
    {
        get => string.Equals(_value, "true", StringComparison.OrdinalIgnoreCase);
        set => Value = value ? "true" : "false";
    }

    public string Value
    {
        get => _value;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_value, value, StringComparison.Ordinal)) return;
            _value = value;
            Raise(nameof(Value));
            Raise(nameof(IsOn));
            Raise(nameof(IconKind));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised when the value changed, so the editor knows it has something to save.</summary>
    internal event EventHandler? Changed;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One titled group of fields, as the provider's descriptor arranges them.</summary>
internal sealed record ConnectionSectionModel(string Title, IReadOnlyList<ConnectionFieldModel> Fields)
{
    public bool HasFields => Fields.Count > 0;
}

/// <summary>
/// The connection editor: a provider, a set of fields, and what happens when they are saved.
/// </summary>
/// <remarks>
/// <para>
/// Every field is driven from <see cref="ConnectionProviderCatalog"/> and every save goes through
/// <see cref="ConnectionEditorDraftFactory"/>, which is the same pair the WinForms editor used.
/// That matters because the factory is what decides, from the text in a Choice field, which
/// authentication kind and TLS mode a profile is saved with -- an editor that built its own draft
/// would be a second opinion on that.
/// </para>
/// <para>
/// Changing the provider rebuilds the fields and keeps whatever the two providers have in common,
/// so trying S3 and then MinIO does not mean typing the name and folder again.
/// </para>
/// </remarks>
internal sealed class ConnectionEditorModel : INotifyPropertyChanged
{
    private readonly Func<ConnectionManagerController> _controller;
    private readonly Func<IRemoteStorageAgentClient>? _storage;
    private readonly IDialogService? _dialogs;
    private readonly IFilePickerService? _files;
    private readonly Func<IKeyStoreAgentClient>? _keyStore;
    private readonly Func<IReadOnlyList<KeyStoreEntryDocument>, Task<KeyStoreEntryDocument?>>? _pickKey;
    private readonly Func<string?, string, Task<IconChoice>>? _pickIcon;
    private readonly List<RelayCommand> _fieldCommands = [];
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private ConnectionProfileDocument? _current;
    private ConnectionProviderDescriptor _provider;
    private string _status = string.Empty;
    private bool _isBusy;
    private bool _isDirty;

    /// <param name="storage">
    /// How the editor reaches the agent to test a connection. Null leaves Test unavailable, which
    /// is what an editor in a test that is not about reachability wants.
    /// </param>
    /// <param name="dialogs">Asks for a typed secret, and confirms deleting one. Null leaves both unavailable.</param>
    /// <param name="files">Picks a certificate or a key file to enrol. Null leaves that unavailable.</param>
    /// <param name="keyStore">Lists what the key store holds, for the material fields.</param>
    /// <param name="pickKey">
    /// Chooses one of those entries. The window supplies a dialog; a test supplies the answer.
    /// </param>
    internal ConnectionEditorModel(
        Func<ConnectionManagerController> controller,
        Func<IRemoteStorageAgentClient>? storage = null,
        IDialogService? dialogs = null,
        IFilePickerService? files = null,
        Func<IKeyStoreAgentClient>? keyStore = null,
        Func<IReadOnlyList<KeyStoreEntryDocument>, Task<KeyStoreEntryDocument?>>? pickKey = null,
        Func<string?, string, Task<IconChoice>>? pickIcon = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _pickIcon = pickIcon;
        _storage = storage;
        _dialogs = dialogs;
        _files = files;
        _keyStore = keyStore;
        _pickKey = pickKey;
        _provider = ConnectionProviderCatalog.All[0];

        SaveCommand = new RelayCommand(_ => _ = SaveAsync(), _ => CanSave);
        TestCommand = new RelayCommand(
            _ => _ = TestAsync(), _ => !_isBusy && _current is not null && _storage is not null);
        Rebuild();
    }

    public static IReadOnlyList<ConnectionProviderDescriptor> Providers => ConnectionProviderCatalog.All;

    public ObservableCollection<ConnectionSectionModel> Sections { get; } = [];

    /// <summary>Which provider this connection is for. Changing it rebuilds the fields.</summary>
    public ConnectionProviderDescriptor Provider
    {
        get => _provider;
        set
        {
            if (value is null || _provider.Kind == value.Kind) return;
            Capture();
            _provider = value;
            Rebuild();
            IsDirty = true;
            Raise(nameof(Provider));
            Raise(nameof(Summary));
            Raise(nameof(TrustNotice));
            Raise(nameof(HasTrustNotice));
        }
    }

    public string Summary => _provider.Summary;

    public string TrustNotice => _provider.TrustNotice;

    public bool HasTrustNotice => _provider.TrustNotice.Length > 0;

    /// <summary>Whether this is a connection that does not exist yet.</summary>
    public bool IsNew => _current is null;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            Raise(nameof(IsBusy));
            RaiseCommands();
        }
    }

    /// <summary>Whether there is anything to save, which is what greys the Save button.</summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (_isDirty == value) return;
            _isDirty = value;
            Raise(nameof(IsDirty));
            RaiseCommands();
        }
    }

    /// <summary>What the last save or test said, or nothing.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            if (string.Equals(_status, value, StringComparison.Ordinal)) return;
            _status = value;
            Raise(nameof(Status));
            Raise(nameof(HasStatus));
        }
    }

    public bool HasStatus => _status.Length > 0;

    public ICommand SaveCommand { get; }

    public ICommand TestCommand { get; }

    public static string SaveLabel => Ui.Connections.SaveConnection;

    public static string TestLabel => Ui.Connections.TestConnection;

    public static string ProviderLabel => Ui.Connections.Provider;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Starts a new connection, on whichever provider is sensible to offer first.</summary>
    internal void StartNew()
    {
        _current = null;
        _values.Clear();
        _provider = ConnectionProviderCatalog.All[0];
        Rebuild();
        Status = string.Empty;
        IsDirty = false;
        RaiseAll();
    }

    /// <summary>
    /// Loads a saved connection into the editor.
    /// </summary>
    /// <remarks>
    /// The profile is fetched rather than projected from the listing, because a listing carries a
    /// name and a provider and an editor needs every field -- and because the version it comes
    /// back with is what a later save is checked against.
    /// </remarks>
    internal async Task OpenAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            var response = await _controller()
                .GetAsync(connectionId, cancellationToken)
                .ConfigureAwait(true);

            if (response.Failure is { } failure)
            {
                Status = failure.Message;
                return;
            }

            if (response.Profile is not { } profile)
            {
                Status = Ui.Connections.ConnectionNotFound;
                return;
            }

            _current = profile;
            _values.Clear();
            foreach (var (key, value) in ConnectionEditorDraftFactory.ToEditorValues(profile))
            {
                _values[key] = value;
            }

            _provider = ConnectionProviderCatalog.Get(MapProvider(profile.Draft.Endpoint.Provider));
            Rebuild();
            Status = string.Empty;
            IsDirty = false;
            RaiseAll();
        }
        catch (Exception error) when (IsAgentFailure(error))
        {
            Status = Ui.Shell.AgentNotConnected;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Whether the editor holds something that could be saved.</summary>
    internal bool CanSave => !_isBusy && _isDirty && Sections
        .SelectMany(static section => section.Fields)
        .All(static row => !row.Required || row.Value.Trim().Length > 0);

    /// <summary>
    /// Builds a draft from the fields and writes it.
    /// </summary>
    /// <remarks>
    /// The factory refuses a draft outside the contract's bounds by throwing, which is right for a
    /// programming error and wrong to show somebody, so it is caught and reported as the sentence
    /// it carries.
    /// </remarks>
    internal async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Capture();
        ConnectionProfileDraft draft;
        try
        {
            draft = ConnectionEditorDraftFactory.Build(_provider.Kind, _values);
        }
        catch (ArgumentException error)
        {
            Status = error.Message;
            return;
        }

        IsBusy = true;
        try
        {
            var response = await _controller()
                .SaveAsync(draft, _current, cancellationToken)
                .ConfigureAwait(true);

            if (response.Failure is { } failure)
            {
                Status = failure.Message;
                return;
            }

            IsDirty = false;

            // Taken from the written profile rather than reported separately, so what the editor
            // holds afterwards is exactly what the agent stored -- including the new version,
            // which is what a second save is checked against.
            if (response.Profile is { } written)
            {
                _current = written;
                _values.Clear();
                foreach (var (key, value) in ConnectionEditorDraftFactory.ToEditorValues(written))
                {
                    _values[key] = value;
                }

                Rebuild();
                RaiseAll();
                Saved?.Invoke(this, written.ConnectionId);
            }

            Status = Ui.Connections.ConnectionSaved;
        }
        catch (Exception error) when (IsAgentFailure(error))
        {
            Status = Ui.Shell.AgentNotConnected;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Asks the agent to reach the saved connection and reports what it found.</summary>
    internal async Task TestAsync(CancellationToken cancellationToken = default)
    {
        if (_current is not { } profile || _storage is null) return;

        IsBusy = true;
        try
        {
            await using var client = _storage();
            var response = await client
                .TestConnectionAsync(
                    new ConnectionTestRequest(StorageIpcContract.CurrentVersion, profile.ConnectionId),
                    cancellationToken)
                .ConfigureAwait(true);

            Status = response.Failure is { } failure
                ? failure.Message
                : response.Succeeded
                    ? Ui.Connections.ConnectionReachable
                    : Ui.Connections.ConnectionUnreachable;
        }
        catch (Exception error) when (IsAgentFailure(error))
        {
            Status = Ui.Shell.AgentNotConnected;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>The connection being edited, for a caller that wants to delete it.</summary>
    internal ConnectionProfileDocument? Current => _current;

    /// <summary>Raised once a save has been accepted, with the id it was written under.</summary>
    internal event EventHandler<Guid>? Saved;

    /// <summary>Takes what is in the fields, so it survives a provider change or a save.</summary>
    private void Capture()
    {
        foreach (var field in Sections.SelectMany(static section => section.Fields))
        {
            _values[field.Key] = field.Value;
        }
    }

    /// <summary>
    /// What every connection has, whichever provider it is on.
    /// </summary>
    /// <remarks>
    /// Not in the provider descriptors, because a name and a folder are the profile's rather than
    /// the provider's -- the WinForms editor drew them as a fixed header above the provider
    /// sections. Declared as descriptors anyway so the same markup renders them, and keyed exactly
    /// as ConnectionEditorDraftFactory reads them.
    /// </remarks>
    private static readonly IReadOnlyList<ConnectionFieldDescriptor> IdentityFields =
    [
        new("profileName", Ui.Connections.FieldConnectionName, ConnectionFieldKind.Text,
            Required: true, HelpText: Ui.Connections.ConnectionNameHint),
        new("folder", Ui.Connections.FieldFolder, ConnectionFieldKind.Text,
            HelpText: Ui.Connections.FolderHint),
        new("labels", Ui.Connections.FieldTags, ConnectionFieldKind.Text,
            HelpText: Ui.Connections.TagsHint),

        // Carried through every save already -- the draft factory keeps it -- but until now there
        // was no way to change it, so an icon chosen in 1.x could be kept and never replaced.
        new("iconKey", Ui.Connections.FieldIcon, ConnectionFieldKind.Icon)
    ];

    /// <summary>
    /// How fast a storage connection may move data, for every provider alike.
    /// </summary>
    /// <remarks>
    /// Not in the provider descriptors for the same reason as the identity fields: the library
    /// enforces them the same way on every provider. An SSH terminal moves no files, so it has none.
    /// </remarks>
    private static IReadOnlyList<ConnectionFieldDescriptor> SpeedLimitFields =>
    [
        new(ConnectionEditorDraftFactory.UploadLimitKey, Ui.Connections.FieldUploadLimit, ConnectionFieldKind.Text,
            Placeholder: Ui.Connections.SpeedLimitPlaceholder, HelpText: Ui.Connections.SpeedLimitHint),
        new(ConnectionEditorDraftFactory.DownloadLimitKey, Ui.Connections.FieldDownloadLimit, ConnectionFieldKind.Text,
            Placeholder: Ui.Connections.SpeedLimitPlaceholder)
    ];

    /// <summary>
    /// The proxy a remote connection is routed through, the same on every provider. A local
    /// connection has no network to route, so it has none.
    /// </summary>
    private static IReadOnlyList<ConnectionFieldDescriptor> ProxyFields =>
    [
        new(ConnectionEditorDraftFactory.ProxyAddressKey, Ui.Connections.FieldProxyAddress, ConnectionFieldKind.Text,
            Placeholder: Ui.Connections.ProxyAddressPlaceholder, HelpText: Ui.Connections.ProxyAddressHint),
        new(ConnectionEditorDraftFactory.ProxyUsernameKey, Ui.Connections.FieldProxyUsername, ConnectionFieldKind.Text),
        new(ConnectionEditorDraftFactory.ProxyPasswordKey, Ui.Connections.FieldProxyPassword, ConnectionFieldKind.SecretReference,
            Placeholder: Ui.Providers.OptionalVaultEntry)
    ];

    /// <summary>Lays out the connection's own fields, then the provider's three sections.</summary>
    private void Rebuild()
    {
        Sections.Clear();
        _fieldCommands.Clear();
        Add(Ui.Connections.SectionIdentity, IdentityFields);
        Add(Ui.Connections.SectionGeneral, _provider.GeneralFields);
        Add(Ui.Connections.SectionAuthentication, _provider.AuthenticationFields);
        Add(Ui.Connections.SectionSecurity, _provider.SecurityFields);
        if (_provider.Type == ConnectionProfileType.Storage && _provider.Kind != StorageProviderKind.Local)
        {
            Add(Ui.Connections.SectionProxy, ProxyFields);
        }

        if (_provider.Type == ConnectionProfileType.Storage)
        {
            Add(Ui.Connections.SectionSpeedLimits, SpeedLimitFields);
        }

        RaiseCommands();

        void Add(string title, IReadOnlyList<ConnectionFieldDescriptor> descriptors)
        {
            if (descriptors.Count == 0) return;
            var fields = new List<ConnectionFieldModel>(descriptors.Count);
            foreach (var descriptor in descriptors)
            {
                var field = new ConnectionFieldModel(
                    descriptor,
                    _values.TryGetValue(descriptor.Key, out var held) ? held : descriptor.DefaultValue);
                field.Changed += (_, _) =>
                {
                    IsDirty = true;
                    RaiseCommands();
                };
                if (field.IsSecret) Arm(field);
                if (field.IsIcon) ArmIcon(field);
                fields.Add(field);
            }

            Sections.Add(new ConnectionSectionModel(title, fields));
        }
    }

    /// <summary>
    /// Gives the icon row its picture and its button.
    /// </summary>
    /// <remarks>
    /// An empty value is the provider's own icon, which is what the draft factory stores when none
    /// is chosen, so the row shows that rather than a blank.
    /// </remarks>
    private void ArmIcon(ConnectionFieldModel field)
    {
        field.IconResolver = key => Themes.IconCatalog.Resolve(ConnectionIconCatalog.ResolveForConnection(
            string.IsNullOrWhiteSpace(key) ? null : key, _provider.Kind, _provider.Type))
            ?? Lucide.Avalonia.LucideIconKind.Cloud;
        var choose = new RelayCommand(_ => _ = ChooseIconAsync(field), _ => !_isBusy && _pickIcon is not null);
        field.ChooseIconCommand = choose;
        _fieldCommands.Add(choose);
    }

    private async Task ChooseIconAsync(ConnectionFieldModel field)
    {
        if (_pickIcon is null) return;
        var name = Sections.SelectMany(static section => section.Fields)
            .FirstOrDefault(static candidate => candidate.Key == "profileName")?.Value;
        var choice = await _pickIcon(
            string.IsNullOrWhiteSpace(field.Value) ? null : field.Value,
            Ui.Format(Ui.Connections.IconPickerConnectionTitleFormat,
                string.IsNullOrWhiteSpace(name) ? _provider.DisplayName : name)).ConfigureAwait(true);
        if (choice.Chosen) field.Value = choice.Key ?? string.Empty;
    }

    /// <summary>
    /// Gives a secret field its three actions.
    /// </summary>
    /// <remarks>
    /// Commands on the field rather than on the editor with the field as a parameter, because the
    /// row is drawn inside two nested item templates and a binding from there back up to the
    /// editor is the kind of path that breaks silently when the markup is rearranged.
    /// </remarks>
    private void Arm(ConnectionFieldModel field)
    {
        var enroll = new RelayCommand(
            _ => _ = EnrollAsync(field),
            _ => !_isBusy && (ConnectionSecretFields.IsFile(field.Key) ? _files is not null : _dialogs is not null));
        var delete = new RelayCommand(
            _ => _ = DeleteSecretAsync(field),
            _ => !_isBusy && _dialogs is not null && field.Value.Trim().Length > 0);
        var choose = new RelayCommand(
            _ => _ = ChooseFromKeyStoreAsync(field),
            _ => !_isBusy && _keyStore is not null && _pickKey is not null);

        field.EnrollCommand = enroll;
        field.DeleteSecretCommand = delete;
        field.ChooseFromKeyStoreCommand = choose;
        field.Changed += (_, _) => delete.RaiseCanExecuteChanged();
        _fieldCommands.Add(enroll);
        _fieldCommands.Add(delete);
        _fieldCommands.Add(choose);
    }

    /// <summary>
    /// Sends a secret to the vault and puts the reference it answers with in the field.
    /// </summary>
    /// <remarks>
    /// A certificate or a private key comes from a file; everything else is typed into a box that
    /// does not echo it. Either way the bytes go to the vault and are zeroed here, and the profile
    /// only ever stores the reference. A field that already names a reference is updated in place,
    /// so a rotated password keeps its reference and every profile sharing it.
    /// </remarks>
    internal async Task EnrollAsync(ConnectionFieldModel field, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (_isBusy) return;

        byte[]? material = null;
        try
        {
            material = await CollectSecretAsync(field, cancellationToken).ConfigureAwait(true);
            if (material is null) return;

            IsBusy = true;
            Status = Ui.ConnectionEditor.WritingVaultEntry;
            var existing = field.Value.Trim();
            var response = await _controller()
                .EnrollOrUpdateSecretAsync(
                    ConnectionSecretFields.Purpose(field.Key),
                    existing.Length == 0 ? null : existing,
                    material,
                    cancellationToken)
                .ConfigureAwait(true);

            if (!response.Succeeded || response.Reference is null)
            {
                Status = response.Failure?.Message ?? Ui.ConnectionEditor.VaultEntryFailed;
                return;
            }

            field.Value = response.Reference;
            Status = Ui.Format(Ui.ConnectionEditor.VaultReferenceReadyFormat, response.Version);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ArgumentException error)
        {
            Status = error.Message;
        }
        catch (Exception error) when (IsAgentFailure(error) || error is UnauthorizedAccessException)
        {
            Status = Ui.ConnectionEditor.SecretOperationFailed;
        }
        finally
        {
            if (material is not null) CryptographicOperations.ZeroMemory(material);
            IsBusy = false;
        }
    }

    /// <summary>
    /// Confirms, then removes the vault entry a field names and clears the field.
    /// </summary>
    /// <remarks>
    /// The profile still carries the old reference until it is saved, which the status says: a
    /// deleted secret and an unsaved profile is the one state where the two disagree.
    /// </remarks>
    internal async Task DeleteSecretAsync(ConnectionFieldModel field, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (_isBusy || _dialogs is null) return;

        var reference = field.Value.Trim();
        if (reference.Length == 0 || !ConnectionEndpointDocument.IsOpaqueSecretReference(reference))
        {
            Status = Ui.ConnectionEditor.NoVaultReference;
            return;
        }

        var choice = await _dialogs.ConfirmAsync(
            new DialogRequest
            {
                Title = Ui.Dialogs.DeleteVaultSecretCaption,
                Message = Ui.Dialogs.DeleteVaultSecretPrompt,
                Severity = DialogSeverity.Warning,
                Buttons = DialogButtons.YesNo,
                Default = DialogChoice.No
            },
            cancellationToken).ConfigureAwait(true);
        if (choice != DialogChoice.Yes) return;

        IsBusy = true;
        try
        {
            var response = await _controller()
                .DeleteSecretAsync(reference, ConnectionSecretFields.Purpose(field.Key), cancellationToken)
                .ConfigureAwait(true);

            if (!response.Succeeded)
            {
                Status = response.Failure?.Message ?? Ui.ConnectionEditor.VaultSecretDeleteFailed;
                return;
            }

            field.Value = string.Empty;
            Status = Ui.ConnectionEditor.VaultSecretDeleted;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error) when (IsAgentFailure(error) || error is UnauthorizedAccessException)
        {
            Status = Ui.ConnectionEditor.SecretDeleteFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Fills a material field, and the passphrase field that goes with it, from the key store.
    /// </summary>
    /// <remarks>
    /// One entry fills both halves: the provider needs the passphrase to open the material, and
    /// the profile requires them together. A password-less certificate has no passphrase
    /// reference, so the companion field is cleared rather than left pointing at whatever was
    /// there before.
    /// </remarks>
    internal async Task ChooseFromKeyStoreAsync(ConnectionFieldModel field, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (_isBusy || _keyStore is null || _pickKey is null ||
            ConnectionSecretFields.KeyStoreSlot(field.Key) is not { } kind)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await using var client = _keyStore();
            var listed = await client.ListAsync(
                new KeyStoreListRequest(KeyStoreIpcContract.CurrentVersion, Kind: kind),
                cancellationToken).ConfigureAwait(true);

            if (listed.Failure is { } failure)
            {
                Status = failure.Message;
                return;
            }

            if (listed.Entries.Length == 0)
            {
                Status = Ui.ConnectionEditor.NoStoredKeys;
                return;
            }

            // Not busy while the picker is open: it is modal, and a dimmed editor behind a modal
            // reads as broken rather than as waiting.
            IsBusy = false;
            var chosen = await _pickKey(listed.Entries).ConfigureAwait(true);
            if (chosen is null) return;

            field.Value = chosen.MaterialReference;
            if (ConnectionSecretFields.CompanionPassphrase(field.Key) is { } companionKey &&
                Field(companionKey) is { } passphrase)
            {
                passphrase.Value = chosen.PassphraseReference ?? string.Empty;
            }

            Status = Ui.Format(Ui.ConnectionEditor.UsingStoredKeyFormat, chosen.DisplayName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error) when (IsAgentFailure(error) || error is UnauthorizedAccessException or InvalidDataException)
        {
            Status = Ui.ConnectionEditor.KeyStoreUnreadable;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>The field with a given key, or nothing when this provider has none.</summary>
    internal ConnectionFieldModel? Field(string key) =>
        Sections.SelectMany(static section => section.Fields).FirstOrDefault(field => field.Key == key);

    /// <summary>
    /// The bytes to enrol: a file's contents for material, a typed value for everything else.
    /// Nothing when the person cancelled.
    /// </summary>
    private async Task<byte[]?> CollectSecretAsync(ConnectionFieldModel field, CancellationToken cancellationToken)
    {
        if (ConnectionSecretFields.IsFile(field.Key))
        {
            if (_files is null) return null;
            var certificate = field.Kind == ConnectionFieldKind.CertificateReference;
            var path = await _files.PickFileAsync(
                new FilePickerRequest
                {
                    Title = certificate ? Ui.ConnectionEditor.SelectPfxCertificate : Ui.ConnectionEditor.SelectPrivateKey,
                    Filters = certificate
                        ?
                        [
                            new FilePickerFilter(Ui.KeyStore.CertificateFiles, ["pfx", "p12"]),
                            new FilePickerFilter(Ui.KeyStore.AllFiles, ["*"])
                        ]
                        :
                        [
                            new FilePickerFilter(Ui.KeyStore.PrivateKeyFiles, ["key", "pem"]),
                            new FilePickerFilter(Ui.KeyStore.AllFiles, ["*"])
                        ]
                },
                cancellationToken).ConfigureAwait(true);
            if (path is null) return null;

            var info = new FileInfo(path);
            if (!info.Exists || info.Length is <= 0 or > SecretVaultIpcContract.MaximumSecretBytes)
            {
                throw new ArgumentException(Ui.ConnectionEditor.SecretFileInvalid);
            }

            return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(true);
        }

        if (_dialogs is null) return null;
        var typed = await _dialogs.PromptAsync(
            new DialogPromptRequest
            {
                Title = Ui.Format(Ui.ConnectionEditor.EnrollFieldFormat, field.Label),
                Label = Ui.ConnectionEditor.VaultEncryptionHint,
                Accept = Ui.ConnectionEditor.Enroll,
                Secret = true
            },
            cancellationToken).ConfigureAwait(true);

        return string.IsNullOrEmpty(typed) ? null : Encoding.UTF8.GetBytes(typed);
    }

    private static StorageProviderKind MapProvider(StorageConnectionProvider provider) => provider switch
    {
        StorageConnectionProvider.Local => StorageProviderKind.Local,
        StorageConnectionProvider.S3 => StorageProviderKind.S3,
        StorageConnectionProvider.Ftp => StorageProviderKind.Ftp,
        StorageConnectionProvider.Ftps => StorageProviderKind.Ftps,
        StorageConnectionProvider.Sftp => StorageProviderKind.Sftp,
        StorageConnectionProvider.Ssh => StorageProviderKind.Ssh,
        _ => StorageProviderKind.S3
    };

    /// <summary>The failures that mean the agent rather than the request, and are reported as such.</summary>
    private static bool IsAgentFailure(Exception error) => error is
        IOException or TimeoutException or InvalidOperationException or ObjectDisposedException;

    private void RaiseAll()
    {
        Raise(nameof(IsNew));
        Raise(nameof(Provider));
        Raise(nameof(Summary));
        Raise(nameof(TrustNotice));
        Raise(nameof(HasTrustNotice));
        RaiseCommands();
    }

    private void RaiseCommands()
    {
        (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (TestCommand as RelayCommand)?.RaiseCanExecuteChanged();
        foreach (var command in _fieldCommands) command.RaiseCanExecuteChanged();
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
