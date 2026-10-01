using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
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

    /// <summary>The red asterisk after a required field's label.</summary>
    public string RequiredMark => descriptor.Required ? "*" : string.Empty;

    /// <summary>
    /// The editor this field belongs to, for the icon row: it also draws the colour swatches and
    /// the badge preview, which are the connection's rather than the field's.
    /// </summary>
    public ConnectionEditorModel? Editor { get; internal set; }

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
    /// Everything that is typed into a box with nothing beside it, which is every other kind.
    /// </summary>
    public bool IsText => !IsChoice && !IsToggle && !IsSecret && !IsIcon && !IsFingerprint;

    /// <summary>
    /// A host key or certificate fingerprint: typed, with 1.x's Reject beside it, and Fetch from
    /// host before that for an SSH host key, as 1.x's FingerprintPicker drew it.
    /// </summary>
    public bool IsFingerprint => Kind == ConnectionFieldKind.Fingerprint;

    /// <summary>
    /// Whether the agent can be asked what this fingerprint should be: an SSH host key, which it
    /// can read from the server, and not an FTPS certificate pin, as in 1.x.
    /// </summary>
    public bool CanFetchFromHost => IsFingerprint && Key == ConnectionEditorModel.HostKeyFingerprintKey;

    /// <summary>Fetches the host key the server presents, to be checked and kept. Set by the editor.</summary>
    public ICommand? FetchFromHostCommand { get; internal set; }

    /// <summary>Records the fingerprint in the box as rejected. Set by the editor.</summary>
    public ICommand? RejectCommand { get; internal set; }

    public static string FetchFromHostLabel => Ui.ConnectionEditor.FetchFromHost;

    public static string FetchFromHostHint => Ui.ConnectionEditor.FetchFromHostHint;

    public static string RejectLabel => Ui.ConnectionEditor.Reject;

    public static string RejectHint => Ui.ConnectionEditor.RejectHint;

    public static string VerifyFingerprintHint => Ui.ConnectionEditor.VerifyFingerprintHint;

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

/// <summary>The editor's three tabs, as 1.x had them (ui-reference 08).</summary>
internal enum ConnectionEditorTab
{
    General,
    Authentication,
    Trust
}

/// <summary>One titled group of fields, as the provider's descriptor arranges them.</summary>
/// <param name="Tab">Which of the editor's tabs the group is drawn on.</param>
/// <param name="Hint">The line under the heading, e.g. the endpoint's example.</param>
internal sealed record ConnectionSectionModel(
    string Title,
    IReadOnlyList<ConnectionFieldModel> Fields,
    ConnectionEditorTab Tab = ConnectionEditorTab.General,
    string Hint = "")
{
    public bool HasFields => Fields.Count > 0;

    public bool HasHint => Hint.Length > 0;
}

/// <summary>One of the colour swatches beside a connection's icon.</summary>
internal sealed record AccentSwatch(string Hex)
{
    public Avalonia.Media.IBrush Brush => BrushFor(Hex);

    /// <summary>A hex colour as a brush, or the neutral grey for anything that does not parse.</summary>
    internal static Avalonia.Media.IBrush BrushFor(string hex) =>
        Avalonia.Media.Color.TryParse(hex, out var color)
            ? new Avalonia.Media.SolidColorBrush(color)
            : Avalonia.Media.Brushes.Gray;
}

/// <summary>A choice in the Type drop-down: storage, or a client such as an SSH terminal.</summary>
internal sealed record ConnectionTypeChoice(ConnectionProfileType Type, string Label)
{
    public override string ToString() => Label;
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
/// so trying S3 and then MinIO does not mean typing the name and folder again. What was only ever
/// the old provider's default, such as its port, gives way to the new provider's.
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

    /// <summary>The new-connection defaults from Settings, as saved; null is the built-in ones.</summary>
    private readonly IReadOnlyDictionary<string, string>? _connectionDefaults;
    private readonly List<RelayCommand> _fieldCommands = [];

    /// <summary>
    /// What each field that belongs to the provider started at, by key: one that had nothing held,
    /// or a saved connection's own value for a field Settings has a default for.
    /// </summary>
    private readonly Dictionary<string, string> _startedAt = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private ConnectionProfileDocument? _current;
    private ConnectionProviderDescriptor _provider;
    private readonly SshHostKeyDiscoveryMode _hostKeyDiscovery;
    private string _status = string.Empty;
    private bool _isBusy;
    private bool _isDirty;
    private bool _fetchingHostKey;

    /// <summary>The endpoint the Trust tab last offered to fetch a host key from, as 1.x kept it.</summary>
    private string? _lastDiscoveryOffer;

    /// <summary>The field an SFTP or SSH connection's host key is typed into.</summary>
    internal const string HostKeyFingerprintKey = "hostKeyFingerprint";

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
    /// <param name="connectionDefaults">
    /// Each provider's new-connection defaults from Settings, which a new connection starts from
    /// and every save takes its timeouts and retries from, as 1.4's editor did. Read once, when
    /// the editor opens, as 1.4 read them. Null is the built-in defaults.
    /// </param>
    /// <param name="hostKeyDiscovery">
    /// Settings' SSH host-key discovery: whether opening the Trust tab on an SFTP or SSH
    /// connection with no fingerprint fetches one, asks first, or leaves it to Fetch from host.
    /// </param>
    internal ConnectionEditorModel(
        Func<ConnectionManagerController> controller,
        Func<IRemoteStorageAgentClient>? storage = null,
        IDialogService? dialogs = null,
        IFilePickerService? files = null,
        Func<IKeyStoreAgentClient>? keyStore = null,
        Func<IReadOnlyList<KeyStoreEntryDocument>, Task<KeyStoreEntryDocument?>>? pickKey = null,
        Func<string?, string, Task<IconChoice>>? pickIcon = null,
        IReadOnlyDictionary<string, string>? connectionDefaults = null,
        SshHostKeyDiscoveryMode hostKeyDiscovery = SshHostKeyDiscoveryMode.Manual)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _connectionDefaults = connectionDefaults;
        _hostKeyDiscovery = hostKeyDiscovery;
        _pickIcon = pickIcon;
        _storage = storage;
        _dialogs = dialogs;
        _files = files;
        _keyStore = keyStore;
        _pickKey = pickKey;
        _provider = ConnectionProviderCatalog.All[0];

        SaveCommand = new RelayCommand(_ => _ = SaveAsync(), _ => CanSave);
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
            ForgetUntouchedDefaults();
            _provider = value;
            Rebuild();
            IsDirty = true;
            Raise(nameof(Provider));
            Raise(nameof(Summary));
            Raise(nameof(TrustNotice));
            Raise(nameof(HasTrustNotice));
            Raise(nameof(AccentColor));
            Raise(nameof(AccentBrush));
            Raise(nameof(ProviderAccentBrush));
            Raise(nameof(BadgeText));
            Raise(nameof(EncryptedByDefault));
        }
    }

    public string Summary => _provider.Summary;

    public IEnumerable<ConnectionSectionModel> GeneralSections =>
        Sections.Where(static section => section.Tab == ConnectionEditorTab.General);

    public IEnumerable<ConnectionSectionModel> AuthenticationSections =>
        Sections.Where(static section => section.Tab == ConnectionEditorTab.Authentication);

    public IEnumerable<ConnectionSectionModel> TrustSections =>
        Sections.Where(static section => section.Tab == ConnectionEditorTab.Trust);

    public static IReadOnlyList<ConnectionTypeChoice> Types { get; } =
    [
        new(ConnectionProfileType.Storage, Ui.ConnectionEditor.TypeStorage),
        new(ConnectionProfileType.Client, Ui.ConnectionEditor.TypeClient)
    ];

    /// <summary>
    /// Storage or client. Changing it moves to that type's first provider, which is how 1.x's two
    /// drop-downs worked: the second only ever lists providers of the first's kind.
    /// </summary>
    public ConnectionTypeChoice Type
    {
        get => Types.First(choice => choice.Type == _provider.Type);
        set
        {
            if (value is null || value.Type == _provider.Type) return;
            Provider = Providers.First(provider => provider.Type == value.Type);
            Raise(nameof(Type));
            Raise(nameof(ProvidersForType));
        }
    }

    public IReadOnlyList<ConnectionProviderDescriptor> ProvidersForType =>
        [.. Providers.Where(provider => provider.Type == _provider.Type)];

    /// <summary>The twelve colours 1.x offered beside the icon.</summary>
    public static IReadOnlyList<AccentSwatch> AccentChoices { get; } =
    [
        .. new[]
        {
            "#2563EB", "#7C3AED", "#DB2777", "#DC2626", "#EA580C", "#CA8A04",
            "#16A34A", "#0891B2", "#0EA5E9", "#64748B", "#475569", "#0F172A"
        }.Select(static hex => new AccentSwatch(hex))
    ];

    /// <summary>The chosen colour as something a view can paint with.</summary>
    public Avalonia.Media.IBrush AccentBrush => AccentSwatch.BrushFor(AccentColor);

    /// <summary>
    /// The provider's own colour, for the strip along the top of the dialog. The provider's rather
    /// than the connection's, as 1.x drew it: the strip says what kind of connection this is, and
    /// the badge preview below shows the colour chosen for this one.
    /// </summary>
    public Avalonia.Media.IBrush ProviderAccentBrush => AccentSwatch.BrushFor(_provider.AccentHex);

    /// <summary>
    /// The connection's colour, or the provider's own when none was chosen -- which is what the
    /// draft factory stores for an empty value, so the preview shows what will be saved.
    /// </summary>
    public string AccentColor
    {
        get => _values.TryGetValue("accentColor", out var chosen) && !string.IsNullOrWhiteSpace(chosen)
            ? chosen
            : _provider.AccentHex;
        set
        {
            if (string.IsNullOrWhiteSpace(value) ||
                string.Equals(AccentColor, value, StringComparison.OrdinalIgnoreCase)) return;
            _values["accentColor"] = value;
            IsDirty = true;
            Raise(nameof(AccentColor));
            Raise(nameof(AccentBrush));
            Raise(nameof(BadgeText));
        }
    }

    public ICommand ChooseAccentCommand => _chooseAccent ??= new RelayCommand(
        color => AccentColor = color as string ?? string.Empty);

    private RelayCommand? _chooseAccent;

    /// <summary>"LOCAL · provider color #4C8BF5": how the connection reads in a pane's header.</summary>
    public string BadgeText => Ui.Format(
        Ui.ConnectionEditor.ProviderColorBadgeFormat, _provider.ShortName, AccentColor).Trim();

    public static string BadgeLabel => Ui.ConnectionEditor.ConnectionBadge;

    public static string BadgeHint => Ui.ConnectionEditor.BadgeHint;

    public bool EncryptedByDefault => _provider.EncryptedByDefault;

    /// <summary>"Loaded version 3", in the footer: the version a save will be checked against.</summary>
    public string LoadedVersion => _current is { } current
        ? Ui.Format(Ui.ConnectionEditor.LoadedVersionFormat, current.Version)
        : string.Empty;

    public bool HasLoadedVersion => _current is not null && !HasStatus;

    public static string TypeLabel => Ui.ConnectionEditor.TypeLabel;

    public static string ProviderProtocolLabel => Ui.ConnectionEditor.ProviderProtocol;

    public static string GeneralTabLabel => Ui.ConnectionEditor.TabGeneral;

    public static string AuthenticationTabLabel => Ui.ConnectionEditor.TabAuthentication;

    public static string TrustTabLabel => Ui.ConnectionEditor.TabSecurity;

    public static string SaveProfileLabel => Ui.ConnectionEditor.SaveProfile;

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
            Raise(nameof(HasLoadedVersion));
        }
    }

    public bool HasStatus => _status.Length > 0;

    public ICommand SaveCommand { get; }

    public static string SaveLabel => Ui.Connections.SaveConnection;

    public static string ProviderLabel => Ui.Connections.Provider;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Starts a new connection, on S3 unless a provider is named, and says in the footer that it
    /// is one, as 1.x's did.
    /// </summary>
    /// <remarks>
    /// S3 because 1.x's dialog opened on it from every New: most connections are object storage,
    /// and a caller that knows better, such as Settings' "Create a … connection", names its own.
    /// </remarks>
    internal void StartNew(StorageProviderKind initialProvider = StorageProviderKind.S3)
    {
        _current = null;
        _values.Clear();
        _provider = ConnectionProviderCatalog.Get(initialProvider);
        Rebuild();
        Status = Ui.ConnectionEditor.NewUnsavedProfile;
        IsDirty = false;
        RaiseAll();
    }

    /// <summary>
    /// Loads a saved connection into the editor.
    /// </summary>
    /// <remarks>
    /// The profile is fetched rather than projected from the listing, because a listing carries a
    /// name and a provider and an editor needs every field -- and because the version it comes
    /// back with is what a later save is checked against. A pinned fingerprint is not in the
    /// profile but in its trust record, so it is read from there, as 1.x's LoadTrustIntoEditorAsync
    /// did: without it a saved SFTP connection could not be saved again, not even renamed, until
    /// its host key was typed a second time.
    /// </remarks>
    internal async Task OpenAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        Status = Ui.ConnectionEditor.LoadingProfile;
        try
        {
            var controller = _controller();
            var response = await controller
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

            var values = new Dictionary<string, string>(
                ConnectionEditorDraftFactory.ToEditorValues(profile), StringComparer.Ordinal);
            var trustProblem = await ReadPinAsync(controller, profile, values, cancellationToken)
                .ConfigureAwait(true);

            _current = profile;
            _values.Clear();
            foreach (var (key, value) in values)
            {
                _values[key] = value;
            }

            _provider = ConnectionProviderCatalog.Get(MapProvider(profile.Draft.Endpoint.Provider));
            Rebuild();
            Status = trustProblem ?? string.Empty;
            IsDirty = false;
            RaiseAll();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
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

    /// <summary>
    /// Puts the fingerprint a connection is pinned to into its field, as 1.x did on loading one.
    /// </summary>
    /// <returns>Why it could not be read, or nothing when it was, or there is none to read.</returns>
    /// <remarks>
    /// The active trusted record is the pin: one that expired or was rejected is history. More than
    /// one active record needs reconciling by hand before a rollover can choose between them, which
    /// is said rather than guessed at, and the field is left empty so a save cannot pick one.
    /// </remarks>
    private static async Task<string?> ReadPinAsync(
        ConnectionManagerController controller,
        ConnectionProfileDocument profile,
        Dictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        if (PinField(profile) is not { } key) return null;

        var response = await controller.GetTrustAsync(profile, cancellationToken).ConfigureAwait(true);
        if (response.Snapshot is not { } snapshot)
        {
            return response.Failure?.Message ?? Ui.ConnectionEditor.TrustStateUnreadable;
        }

        var now = DateTimeOffset.UtcNow;
        var active = snapshot.Records
            .Where(record => record.Decision == ConnectionTrustDecision.Trusted &&
                (record.ExpiresUtc is null || record.ExpiresUtc > now))
            .ToArray();
        values[key] = active.Length == 1 ? active[0].Sha256Fingerprint : string.Empty;
        return active.Length > 1 ? Ui.ConnectionEditor.MultipleTrustRecords : null;
    }

    /// <summary>
    /// The field holding what a saved connection is pinned to, or none when it pins nothing: an
    /// FTPS certificate pin, or an SFTP or SSH host key, as 1.x's TrustFingerprintField chose.
    /// </summary>
    private static string? PinField(ConnectionProfileDocument profile) => profile.Draft.Endpoint switch
    {
        { Provider: StorageConnectionProvider.Ftps, TlsPolicy: ConnectionTlsCertificatePolicy.Pinned } =>
            "certificatePin",
        { Provider: StorageConnectionProvider.Sftp or StorageConnectionProvider.Ssh, SshHostKeyPolicy: ConnectionSshHostKeyPolicy.Pinned } =>
            HostKeyFingerprintKey,
        _ => null
    };

    /// <summary>Whether the editor holds something that could be saved.</summary>
    internal bool CanSave => !_isBusy && _isDirty && Sections
        .SelectMany(static section => section.Fields)
        .All(static row => !row.Required || row.Value.Trim().Length > 0);

    /// <summary>
    /// Builds a draft from the fields and writes it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The factory refuses a draft outside the contract's bounds by throwing, which is right for a
    /// programming error and wrong to show somebody, so it is caught and reported as the sentence
    /// it carries.
    /// </para>
    /// <para>
    /// A fingerprint is checked by the factory but is not part of the profile: it is the
    /// connection's trust, so it is pinned once the profile it belongs to is written, as 1.x's save
    /// did. A pin that is refused leaves the editor open with the reason and the edit still dirty,
    /// the profile already written; saving again writes it at its new version and tries once more.
    /// </para>
    /// </remarks>
    internal async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Capture();
        ConnectionProfileDraft draft;
        try
        {
            // The timeouts and retries a provider has no field for come from Settings, as they
            // did in 1.4, rather than from the built-in defaults Settings exists to override.
            draft = ConnectionEditorDraftFactory.Build(
                _provider.Kind,
                _values,
                ConnectionDefaultSettings.Get(_provider.Kind, _connectionDefaults));
        }
        catch (ArgumentException error)
        {
            Status = error.Message;
            return;
        }

        // The editor has no field for these, and the draft is built from its fields, so a save
        // would reset them: a favourite stopped being one every time it was edited, as it did in
        // 1.x, and a disabled connection came back enabled. An edit keeps what the profile had.
        if (_current?.Draft is { } kept)
        {
            draft = draft with
            {
                IsEnabled = kept.IsEnabled,
                Metadata = draft.Metadata with
                {
                    IsFavorite = kept.Metadata.IsFavorite,
                    HomePath = kept.Metadata.HomePath,
                    UploadPath = kept.Metadata.UploadPath,
                    DownloadPath = kept.Metadata.DownloadPath
                }
            };
        }

        IsBusy = true;
        try
        {
            var controller = _controller();
            var response = await controller
                .SaveAsync(draft, _current, cancellationToken)
                .ConfigureAwait(true);

            if (response.Failure is { } failure)
            {
                Status = failure.Message;
                return;
            }

            if (response.Profile is not { } written)
            {
                IsDirty = false;
                Status = Ui.Connections.ConnectionSaved;
                return;
            }

            // Taken from the written profile rather than reported separately, so what the editor
            // holds afterwards is exactly what the agent stored -- including the new version,
            // which is what a second save is checked against. The pin is not in it, so it is kept.
            var pinField = PinField(written);
            var pin = pinField is not null && _values.TryGetValue(pinField, out var typed) &&
                !string.IsNullOrWhiteSpace(typed)
                    ? typed.Trim()
                    : null;
            _current = written;
            _values.Clear();
            foreach (var (key, value) in ConnectionEditorDraftFactory.ToEditorValues(written))
            {
                _values[key] = value;
            }

            if (pinField is not null && pin is not null) _values[pinField] = pin;
            Rebuild();
            RaiseAll();
            Written?.Invoke(this, written.ConnectionId);

            if (pin is not null)
            {
                Status = Ui.ConnectionEditor.SavingVerifiedTrust;
                var trusted = await controller
                    .TrustOrRolloverAsync(written, pin, cancellationToken)
                    .ConfigureAwait(true);
                if (trusted.Status != ConnectionTrustMutationStatus.Succeeded)
                {
                    Status = trusted.Failure?.Message ?? Ui.ConnectionEditor.PinNotEnrolled;
                    return;
                }
            }

            IsDirty = false;
            Status = Ui.Connections.ConnectionSaved;
            Saved?.Invoke(this, written.ConnectionId);
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

    /// <summary>
    /// Raised once a save has been accepted, pin and all, with the id it was written under.
    /// </summary>
    internal event EventHandler<Guid>? Saved;

    /// <summary>
    /// Raised as soon as the agent has written the profile, before its pin: a refused pin keeps
    /// the editor open, but what lists connections has something new to list either way.
    /// </summary>
    internal event EventHandler<Guid>? Written;

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
        new("iconKey", Ui.ConnectionEditor.Appearance, ConnectionFieldKind.Icon,
            HelpText: Ui.ConnectionEditor.AppearanceHint)
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

    /// <summary>
    /// What an FTP server may need beyond where it is. Defaults suit almost every server, so the
    /// section sits last and every field starts at what it would be anyway.
    /// </summary>
    private IReadOnlyList<ConnectionFieldDescriptor> FtpAdvancedFields()
    {
        var defaults = ConnectionDefaultSettings.Get(_provider.Kind, _connectionDefaults);
        return
        [
            new(ConnectionEditorDraftFactory.EncodingKey, Ui.Connections.FieldEncoding, ConnectionFieldKind.Text,
                DefaultValue: "utf-8", HelpText: Ui.Connections.EncodingHint),
            new(ConnectionEditorDraftFactory.ConnectTimeoutKey, Ui.Connections.FieldConnectTimeout, ConnectionFieldKind.Text,
                DefaultValue: defaults.ConnectTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
                HelpText: Ui.Connections.TimeoutSecondsHint),
            new(ConnectionEditorDraftFactory.ReadTimeoutKey, Ui.Connections.FieldOperationTimeout, ConnectionFieldKind.Text,
                DefaultValue: defaults.OperationTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
                HelpText: Ui.Connections.TimeoutSecondsHint),
            new(ConnectionEditorDraftFactory.ServerTimeZoneKey, Ui.Connections.FieldServerTimeZone, ConnectionFieldKind.Text,
                Placeholder: Ui.Connections.ServerTimeZonePlaceholder, HelpText: Ui.Connections.ServerTimeZoneHint),
            new(ConnectionEditorDraftFactory.ListingFormatKey, Ui.Connections.FieldDirectoryListing, ConnectionFieldKind.Choice,
                DefaultValue: ConnectionEditorDraftFactory.ListingFormats[0],
                HelpText: Ui.Connections.ListingFormatHint,
                Choices: ConnectionEditorDraftFactory.ListingFormats),
            new(ConnectionEditorDraftFactory.DataConnectionKey, Ui.Connections.FieldDataConnection, ConnectionFieldKind.Choice,
                DefaultValue: ConnectionEditorDraftFactory.DataConnections[0],
                HelpText: Ui.Connections.DataConnectionHint,
                Choices: ConnectionEditorDraftFactory.DataConnections),
            new(ConnectionEditorDraftFactory.ActivePortsKey, Ui.Connections.FieldActivePorts, ConnectionFieldKind.Text,
                Placeholder: Ui.Connections.ActivePortsPlaceholder, HelpText: Ui.Connections.ActivePortsHint),
            new(ConnectionEditorDraftFactory.ActiveAddressKey, Ui.Connections.FieldActiveAddress, ConnectionFieldKind.Text,
                Placeholder: Ui.Connections.ActiveAddressPlaceholder, HelpText: Ui.Connections.ActiveAddressHint)
        ];
    }

    /// <summary>
    /// The saved connection's own values, when the provider chosen is the one it was saved with.
    /// </summary>
    /// <remarks>
    /// A field with nothing held starts at these rather than at the provider's defaults from
    /// Settings, so going to another provider and back finds the connection as it was. A field
    /// the connection left empty stays empty: filling it from Settings would change the connection
    /// at its next save without anybody having touched it. Anywhere else, for a new connection or
    /// a saved one being moved, the provider's defaults apply, as 1.4 applied them on every change.
    /// </remarks>
    private IReadOnlyDictionary<string, string>? SavedOnThisProvider() =>
        _current is { } current && MapProvider(current.Draft.Endpoint.Provider) == _provider.Kind
            ? ConnectionEditorDraftFactory.ToEditorValues(current)
            : null;

    /// <summary>
    /// Whether a field's starting value comes from the provider's defaults in Settings, and so is
    /// let go of on a provider change when nobody has changed it.
    /// </summary>
    private static bool FollowsProvider(string key) =>
        ConnectionDefaultSettings.HasDefault(key) ||
        key is ConnectionEditorDraftFactory.ConnectTimeoutKey or ConnectionEditorDraftFactory.ReadTimeoutKey;

    /// <summary>
    /// Lets go of every field still at the value it started at, so the next provider's own apply.
    /// </summary>
    /// <remarks>
    /// FTP's port 21 is nobody's choice once the connection is SFTP. What was typed is carried
    /// across, which is the point of keeping values by key; what was only ever a default belongs
    /// to the provider it came from, and 1.4, which rebuilt its fields on every change, never
    /// carried it.
    /// </remarks>
    private void ForgetUntouchedDefaults()
    {
        foreach (var (key, start) in _startedAt)
        {
            if (_values.TryGetValue(key, out var held) && string.Equals(held, start, StringComparison.Ordinal))
            {
                _values.Remove(key);
            }
        }
    }

    /// <summary>Lays out the connection's own fields, then the provider's three sections.</summary>
    private void Rebuild()
    {
        Sections.Clear();
        _fieldCommands.Clear();
        _startedAt.Clear();
        var saved = SavedOnThisProvider();
        var prefill = saved ?? ConnectionDefaultSettings.Get(_provider.Kind, _connectionDefaults).FieldValues;

        // A saved connection's port, TLS mode and the rest of what Settings has a default for
        // count as untouched until somebody changes them, so moving it to another provider lets
        // that provider's defaults apply, as 1.4's did. That includes values held but not on
        // screen here: every saved connection carries a TLS mode, and FTPS should not inherit
        // an SFTP connection's.
        if (saved is not null)
        {
            foreach (var (key, value) in saved)
            {
                if (FollowsProvider(key) &&
                    _values.TryGetValue(key, out var held) &&
                    string.Equals(held, value, StringComparison.Ordinal))
                {
                    _startedAt[key] = value;
                }
            }
        }

        // The General tab opens on the endpoint, as 1.x's did: the connection's own fields first,
        // then where it points. What 2.0 added -- proxy, speed limits, FTP's advanced options --
        // follows under its own heading on the same tab, rather than as a new screen.
        Add(Ui.ConnectionEditor.TabEndpoint, [.. IdentityFields, .. _provider.GeneralFields],
            ConnectionEditorTab.General, _provider.EndpointExample);
        if (_provider.Type == ConnectionProfileType.Storage && _provider.Kind != StorageProviderKind.Local)
        {
            Add(Ui.Connections.SectionProxy, ProxyFields, ConnectionEditorTab.General);
        }

        if (_provider.Type == ConnectionProfileType.Storage)
        {
            Add(Ui.Connections.SectionSpeedLimits, SpeedLimitFields, ConnectionEditorTab.General);
        }

        if (_provider.Kind is StorageProviderKind.Ftp or StorageProviderKind.Ftps)
        {
            Add(Ui.Connections.SectionAdvanced, FtpAdvancedFields(), ConnectionEditorTab.General);
        }

        // Drawn even for a provider with nothing to authenticate, as 1.x's page was: its heading and
        // the vault hint on a tab that is always there, rather than a tab that comes and goes.
        Add(Ui.ConnectionEditor.TabAuthentication, _provider.AuthenticationFields,
            ConnectionEditorTab.Authentication, Ui.ConnectionEditor.SecretsLiveInVault, always: true);
        Add(Ui.ConnectionEditor.TabTrust, _provider.SecurityFields, ConnectionEditorTab.Trust);

        Raise(nameof(GeneralSections));
        Raise(nameof(AuthenticationSections));
        Raise(nameof(TrustSections));
        RaiseCommands();

        void Add(
            string title,
            IReadOnlyList<ConnectionFieldDescriptor> descriptors,
            ConnectionEditorTab tab,
            string hint = "",
            bool always = false)
        {
            if (descriptors.Count == 0 && !always) return;
            var fields = new List<ConnectionFieldModel>(descriptors.Count);
            foreach (var descriptor in descriptors)
            {
                if (!_values.TryGetValue(descriptor.Key, out var value))
                {
                    value = prefill.GetValueOrDefault(descriptor.Key) ?? descriptor.DefaultValue;
                    _startedAt[descriptor.Key] = value;
                }

                var field = new ConnectionFieldModel(descriptor, value);
                field.Changed += (_, _) =>
                {
                    IsDirty = true;
                    RaiseCommands();
                };
                if (field.IsSecret) Arm(field);
                if (field.IsFingerprint) ArmFingerprint(field);
                if (field.IsIcon) ArmIcon(field);
                field.Editor = this;
                fields.Add(field);
            }

            Sections.Add(new ConnectionSectionModel(title, fields, tab, hint));
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

    /// <summary>
    /// Gives a fingerprint field 1.x's Reject, and an SSH host key Fetch from host as well.
    /// </summary>
    /// <remarks>
    /// Reject is offered on a connection not saved yet, as 1.x offered it, and says to save first
    /// when pressed: the rejection is recorded against the saved endpoint.
    /// </remarks>
    private void ArmFingerprint(ConnectionFieldModel field)
    {
        var reject = new RelayCommand(_ => _ = RejectFingerprintAsync(field), _ => !_isBusy && _dialogs is not null);
        field.RejectCommand = reject;
        _fieldCommands.Add(reject);
        if (!field.CanFetchFromHost) return;

        var fetch = new RelayCommand(_ => _ = FetchHostKeyAsync(), _ => !_isBusy && _dialogs is not null);
        field.FetchFromHostCommand = fetch;
        _fieldCommands.Add(fetch);
    }

    /// <summary>
    /// Asks the agent for the host key the SFTP or SSH endpoint presents and shows it, putting it
    /// in the field only when it is accepted, as 1.x's FetchSshHostKeyAsync did.
    /// </summary>
    /// <remarks>
    /// Fetching trusts nothing: the key is pinned when the connection is saved, as one typed in
    /// is. The question defaults to No, as 1.x's did, because the key has to be compared with one
    /// got some other way before it is used.
    /// </remarks>
    internal async Task FetchHostKeyAsync(CancellationToken cancellationToken = default)
    {
        if (_dialogs is null) return;
        if (_fetchingHostKey)
        {
            Status = Ui.ConnectionEditor.FetchAlreadyRunning;
            return;
        }

        if (!TryGetDiscoveryTarget(out var host, out var port, out var fingerprint))
        {
            Status = Ui.ConnectionEditor.EnterValidSftpEndpoint;
            return;
        }

        _fetchingHostKey = true;
        try
        {
            Status = Ui.Format(Ui.ConnectionEditor.FetchingHostKeyFormat, host, port);
            var response = await _controller()
                .DiscoverSshHostKeyAsync(host, port, cancellationToken)
                .ConfigureAwait(true);
            if (response.Failure is not null ||
                response.Sha256Fingerprint is not { } discovered ||
                response.HostKeyAlgorithm is not { } algorithm)
            {
                Status = response.Failure?.Message ?? Ui.ConnectionEditor.HostKeyUnusable;
                return;
            }

            var choice = await _dialogs.ConfirmAsync(
                new DialogRequest
                {
                    Title = Ui.Dialogs.VerifyHostKeyCaption,
                    Message = Ui.Format(Ui.Dialogs.VerifyHostKeyPromptFormat, algorithm, discovered),
                    Severity = DialogSeverity.Warning,
                    Buttons = DialogButtons.YesNo,
                    Default = DialogChoice.No
                },
                cancellationToken).ConfigureAwait(true);
            if (choice != DialogChoice.Yes)
            {
                Status = Ui.ConnectionEditor.HostKeyNotAdded;
                return;
            }

            fingerprint.Value = discovered;
            Status = Ui.ConnectionEditor.HostKeyAdded;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error) when (IsAgentFailure(error) || error is NotSupportedException or InvalidDataException)
        {
            Status = Ui.ConnectionEditor.HostKeyFetchFailed;
        }
        finally
        {
            _fetchingHostKey = false;
        }
    }

    /// <summary>
    /// What Settings' host-key discovery does when the Trust tab is opened, as 1.x's
    /// SettingsTabSelected did: nothing when it is Manual, and otherwise, for an SFTP or SSH
    /// endpoint with no fingerprint yet, fetch its key, asking first when it says to.
    /// </summary>
    /// <remarks>
    /// Offered once per endpoint while the dialog is open, so going back and forth between the
    /// tabs does not ask again; changing the host or port is a new endpoint and is offered anew.
    /// </remarks>
    internal async Task OfferHostKeyDiscoveryAsync(CancellationToken cancellationToken = default)
    {
        if (_hostKeyDiscovery == SshHostKeyDiscoveryMode.Manual || _dialogs is null || _isBusy ||
            !TryGetDiscoveryTarget(out var host, out var port, out var fingerprint) ||
            fingerprint.Value.Trim().Length > 0)
        {
            return;
        }

        var endpoint = string.Create(CultureInfo.InvariantCulture, $"{host}:{port}");
        if (string.Equals(_lastDiscoveryOffer, endpoint, StringComparison.OrdinalIgnoreCase)) return;
        _lastDiscoveryOffer = endpoint;

        if (_hostKeyDiscovery == SshHostKeyDiscoveryMode.AskBeforeFetching)
        {
            var choice = await _dialogs.ConfirmAsync(
                new DialogRequest
                {
                    Title = Ui.Dialogs.FetchHostKeyCaption,
                    Message = Ui.Format(Ui.Dialogs.FetchHostKeyPromptFormat, endpoint),
                    Severity = DialogSeverity.Question,
                    Buttons = DialogButtons.YesNo,
                    Default = DialogChoice.No
                },
                cancellationToken).ConfigureAwait(true);
            if (choice != DialogChoice.Yes) return;
        }

        await FetchHostKeyAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Confirms, then records the fingerprint in the field as rejected for the saved endpoint and
    /// clears the field, as 1.x's RejectFingerprintAsync did.
    /// </summary>
    /// <remarks>
    /// A rejection is history the agent keeps, so the connection has to exist first; one not yet
    /// saved is told so, in 1.x's words.
    /// </remarks>
    internal async Task RejectFingerprintAsync(ConnectionFieldModel field, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (_isBusy || _dialogs is null) return;
        if (_current is not { } current)
        {
            Status = Ui.ConnectionEditor.SaveBeforeRejecting;
            return;
        }

        var fingerprint = field.Value.Trim();
        if (!ConnectionTrustIpcLimits.IsValidFingerprint(fingerprint))
        {
            Status = Ui.ConnectionEditor.EnterValidFingerprint;
            return;
        }

        var choice = await _dialogs.ConfirmAsync(
            new DialogRequest
            {
                Title = Ui.Dialogs.RejectServerIdentityCaption,
                Message = Ui.Dialogs.RejectServerIdentityPrompt,
                Severity = DialogSeverity.Warning,
                Buttons = DialogButtons.OkCancel
            },
            cancellationToken).ConfigureAwait(true);
        if (choice != DialogChoice.Ok) return;

        IsBusy = true;
        try
        {
            Status = Ui.ConnectionEditor.RecordingRejected;
            var response = await _controller()
                .RejectAsync(current, fingerprint, cancellationToken)
                .ConfigureAwait(true);
            if (response.Status != ConnectionTrustMutationStatus.Succeeded)
            {
                Status = response.Failure?.Message ?? Ui.ConnectionEditor.RejectedRecordFailed;
                return;
            }

            field.Value = string.Empty;
            Status = Ui.ConnectionEditor.RejectedRecorded;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error) when (IsAgentFailure(error) || error is InvalidDataException)
        {
            Status = Ui.ConnectionEditor.RejectedRecordFailedThroughAgent;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// The SFTP or SSH endpoint to fetch a host key from, and the field it goes in, when the host
    /// and port make a valid one, as 1.x's TryGetSftpDiscoveryTarget read them.
    /// </summary>
    private bool TryGetDiscoveryTarget(out string host, out int port, out ConnectionFieldModel fingerprint)
    {
        host = string.Empty;
        port = 0;
        fingerprint = null!;
        if (_provider.Kind is not (StorageProviderKind.Sftp or StorageProviderKind.Ssh) ||
            Field("host") is not { } hostField ||
            Field("port") is not { } portField ||
            Field(HostKeyFingerprintKey) is not { } fingerprintField)
        {
            return false;
        }

        host = hostField.Value.Trim();
        if (!int.TryParse(portField.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out port))
        {
            return false;
        }

        fingerprint = fingerprintField;
        return new ConnectionSshHostKeyDiscoveryRequest(ConnectionTrustIpcContract.CurrentVersion, host, port)
            .HasValidBounds;
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
        Raise(nameof(Type));
        Raise(nameof(ProvidersForType));
        Raise(nameof(AccentColor));
        Raise(nameof(AccentBrush));
        Raise(nameof(ProviderAccentBrush));
        Raise(nameof(BadgeText));
        Raise(nameof(EncryptedByDefault));
        Raise(nameof(LoadedVersion));
        Raise(nameof(HasLoadedVersion));
        Raise(nameof(Summary));
        Raise(nameof(TrustNotice));
        Raise(nameof(HasTrustNotice));
        RaiseCommands();
    }

    private void RaiseCommands()
    {
        (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
        foreach (var command in _fieldCommands) command.RaiseCanExecuteChanged();
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
