using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

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
    /// Everything that is typed into a box, which is every other kind.
    /// </summary>
    /// <remarks>
    /// Including a secret reference, a certificate reference and a fingerprint. Those name
    /// something held elsewhere rather than carrying it, so the editor's job is the name -- and a
    /// picker for each belongs with the key store screen, which is not ported yet.
    /// </remarks>
    public bool IsText => !IsChoice && !IsToggle;

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
    internal ConnectionEditorModel(
        Func<ConnectionManagerController> controller,
        Func<IRemoteStorageAgentClient>? storage = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _storage = storage;
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
            HelpText: Ui.Connections.TagsHint)
    ];

    /// <summary>Lays out the connection's own fields, then the provider's three sections.</summary>
    private void Rebuild()
    {
        Sections.Clear();
        Add(Ui.Connections.SectionIdentity, IdentityFields);
        Add(Ui.Connections.SectionGeneral, _provider.GeneralFields);
        Add(Ui.Connections.SectionAuthentication, _provider.AuthenticationFields);
        Add(Ui.Connections.SectionSecurity, _provider.SecurityFields);
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
                fields.Add(field);
            }

            Sections.Add(new ConnectionSectionModel(title, fields));
        }
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
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
