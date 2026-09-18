using System.Security.Cryptography;
using System.Text;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

public sealed class ConnectionManagerForm : Form
{
    private readonly List<Image> _ownedImages = [];
    private readonly StorageHubChoiceField _typeSelector;
    private readonly StorageHubChoiceField _providerSelector;
    private readonly Label _providerSummary;
    private readonly Panel _providerAccent;
    private readonly Label _testState;
    private readonly TabPage _generalPage;
    private readonly TabPage _authenticationPage;
    private readonly TabPage _securityPage;
    private readonly TabControl _settingsTabs;
    private readonly Dictionary<string, Control> _editorFields = new(StringComparer.Ordinal);

    /// <summary>
    /// The reference box behind each secret field, kept separately because choosing one key store
    /// entry fills two fields: the material and the passphrase that unlocks it.
    /// </summary>
    private readonly Dictionary<string, StorageHubTextField> _secretReferenceBoxes = new(StringComparer.Ordinal);
    private readonly IRemoteStorageAgentClient _storageClient;
    private readonly IRemoteConnectionProfileClient _profileClient;
    private readonly IRemoteSecretVaultClient _secretClient;
    private readonly ConnectionManagerController _controller;
    private readonly CancellationTokenSource _formLifetime = new();
    private readonly bool _ownsStorageClient;
    private readonly bool _ownsProfileClient;
    private readonly bool _ownsSecretClient;
    private readonly bool _quickConnectMode;
    private readonly Guid? _initialConnectionId;
    private readonly ConnectionEditorTab _initialTab;
    private readonly string _initialEndpoint;
    private readonly SshHostKeyDiscoveryMode _sshHostKeyDiscoveryMode;
    private readonly IReadOnlyDictionary<string, string> _connectionDefaults;
    private ConnectionProfileDocument? _selectedProfile;
    private CancellationTokenSource? _profileLoadCancellation;
    private bool _loadingEditor;
    private bool _profileLoading;
    private bool _hostKeyDiscoveryActive;
    private string? _lastHostKeyDiscoveryOffer;

    /// <summary>
    /// The connection editor. The saved-connection list lives in the shell's connections panel,
    /// which is what opens this form — on an existing connection, or on nothing for a new one.
    /// </summary>
    public ConnectionManagerForm(
        Guid? connectionId = null,
        StorageProviderKind initialProvider = StorageProviderKind.S3,
        ConnectionEditorTab initialTab = ConnectionEditorTab.General,
        bool quickConnectMode = false,
        string initialEndpoint = "",
        IRemoteStorageAgentClient? storageClient = null,
        IRemoteConnectionProfileClient? profileClient = null,
        IRemoteSecretVaultClient? secretClient = null,
        SshHostKeyDiscoveryMode? sshHostKeyDiscoveryMode = null,
        IReadOnlyDictionary<string, string>? connectionDefaults = null)
    {
        if (connectionId is not null && quickConnectMode)
        {
            throw new ArgumentException(
                Ui.ConnectionEditor.QuickConnectCannotOpenSaved,
                nameof(quickConnectMode));
        }

        _quickConnectMode = quickConnectMode;
        _initialConnectionId = connectionId;
        _initialTab = initialTab;
        _initialEndpoint = initialEndpoint;
        _ownsStorageClient = storageClient is null;
        _ownsProfileClient = profileClient is null;
        _ownsSecretClient = secretClient is null;
        _storageClient = storageClient ?? new NamedPipeRemoteStorageAgentClient();
        _profileClient = profileClient ?? new NamedPipeRemoteConnectionProfileClient();
        _secretClient = secretClient ?? new NamedPipeRemoteSecretVaultClient();
        var preferences = DesktopConfigStore.CreateDefault().Load();
        _sshHostKeyDiscoveryMode = sshHostKeyDiscoveryMode ?? preferences.SshHostKeyDiscovery;
        _connectionDefaults = ConnectionDefaultSettings.Normalize(
            connectionDefaults ?? preferences.ConnectionDefaults);
        _controller = new ConnectionManagerController(_profileClient, _secretClient);
        Text = quickConnectMode ? Ui.ConnectionEditor.QuickConnectTitle : Ui.ConnectionEditor.EditTitle;
        AccessibleName = quickConnectMode ? Ui.ConnectionEditor.QuickConnectAccessibleName : Ui.ConnectionEditor.EditAccessibleName;
        AccessibleDescription = Ui.ConnectionEditor.WindowAccessibleDescription;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(720, 620);
        Size = new Size(880, 760);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = StorageHubTheme.Canvas;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        StorageHubTheme.Register(this);

        // Only Quick Connect needs commands of its own; see BuildToolbar.
        var toolbar = _quickConnectMode ? BuildToolbar() : null;

        _providerAccent = new Panel { Dock = DockStyle.Top, Height = 4, BackColor = StorageHubTheme.Primary };
        _typeSelector = new StorageHubChoiceField
        {
            Width = 260,
            AccessibleName = Ui.ConnectionEditor.ConnectionType,
            AccessibleDescription = Ui.ConnectionEditor.ConnectionTypeHint
        };
        _typeSelector.Items.AddRange(new object[] { ConnectionProfileType.Storage, ConnectionProfileType.Client });
        _typeSelector.SelectedItem = ConnectionProviderCatalog.Get(initialProvider).Type;
        _typeSelector.SelectedIndexChanged += TypeSelectionChanged;
        _providerSelector = new StorageHubChoiceField
        {
            Width = 260,
            AccessibleName = Ui.ConnectionEditor.ConnectionProvider,
            AccessibleDescription = Ui.ConnectionEditor.ProviderHint
        };
        PopulateProviderSelector(ConnectionProviderCatalog.Get(initialProvider).Type, initialProvider);
        _providerSelector.SelectedIndexChanged += ProviderSelectionChanged;

        _providerSummary = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(650, 0),
            ForeColor = StorageHubTheme.TextMuted,
            AccessibleName = Ui.ConnectionEditor.ProviderSummary
        };
        var editorHeader = BuildEditorHeader();

        _settingsTabs = new ThemedTabControl
        {
            Dock = DockStyle.Fill,
            AccessibleName = Ui.ConnectionEditor.ConnectionSettings,
        };
        StorageHubTheme.ConfigureTabs(_settingsTabs);
        _generalPage = NewPage(Ui.ConnectionEditor.TabGeneral);
        _authenticationPage = NewPage(Ui.ConnectionEditor.TabAuthentication);
        _securityPage = NewPage(Ui.ConnectionEditor.TabSecurity);
        _settingsTabs.TabPages.Add(_generalPage);
        _settingsTabs.TabPages.Add(_authenticationPage);
        _settingsTabs.TabPages.Add(_securityPage);
        _settingsTabs.SelectedIndexChanged += SettingsTabSelected;

        var editor = new Panel { Dock = DockStyle.Fill, BackColor = StorageHubTheme.Surface };
        editor.Controls.Add(_settingsTabs);
        editor.Controls.Add(editorHeader);
        editor.Controls.Add(_providerAccent);

        _testState = new Label
        {
            Text = Ui.ConnectionEditor.NotTested,
            AutoSize = true,
            ForeColor = StorageHubTheme.TextMuted,
            Padding = new Padding(8, 9, 8, 0),
            AccessibleName = Ui.ConnectionEditor.ConnectionTestStatus
        };
        var footer = BuildFooter();

        Controls.Add(editor);
        Controls.Add(footer);
        if (toolbar is not null)
        {
            Controls.Add(toolbar);
        }

        UpdateProviderEditor(ConnectionProviderCatalog.Get(initialProvider));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _typeSelector.SelectedIndexChanged -= TypeSelectionChanged;
            _providerSelector.SelectedIndexChanged -= ProviderSelectionChanged;
            _settingsTabs.SelectedIndexChanged -= SettingsTabSelected;
            _formLifetime.Cancel();
            _profileLoadCancellation?.Cancel();
        }

        base.Dispose(disposing);

        if (disposing)
        {
            foreach (var image in _ownedImages)
            {
                image.Dispose();
            }

            _ownedImages.Clear();
            _formLifetime.Dispose();
            _profileLoadCancellation?.Dispose();
            DisposeOwnedClient(_storageClient, _ownsStorageClient);
            DisposeOwnedClient(_profileClient, _ownsProfileClient);
            DisposeOwnedClient(_secretClient, _ownsSecretClient);
        }
    }

    /// <summary>Raised whenever this editor changes what is saved, so the shell can re-list.</summary>
    internal event EventHandler? ProfilesChanged;

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _settingsTabs.SelectedIndex = (int)_initialTab;
        if (_initialConnectionId is { } connectionId)
        {
            await LoadProfileAsync(connectionId, _formLifetime.Token);
        }
        else if (!_quickConnectMode)
        {
            StartNewProfile();
        }
    }

    /// <summary>
    /// Quick Connect's own commands.
    ///
    /// Editing a saved profile does not get a toolbar. The connections panel is where a
    /// connection is created, opened, tested and deleted, so repeating those here offered a
    /// second route to each one and a New connection button that quietly discarded the draft the
    /// dialog had just been opened to edit. Saving was duplicated by the footer's own button.
    /// A Quick Connect has no saved profile behind it and no panel row, so its commands stay.
    /// </summary>
    private ToolStrip BuildToolbar()
    {
        var toolbar = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            Dock = DockStyle.Top,
            ImageScalingSize = new Size(18, 18),
            Padding = new Padding(6, 4, 6, 4),
            BackColor = StorageHubTheme.Surface,
            AccessibleName = Ui.ConnectionEditor.QuickConnectCommands
        };
        toolbar.Items.Add(CreateToolbarButton(UiGlyph.Add, Ui.ConnectionEditor.NewConnection, (_, _) => StartNewProfile()));
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(CreateToolbarButton(UiGlyph.Test, Ui.ConnectionEditor.TestConnection, async (_, _) =>
            await TestSelectedConnectionAsync(_formLifetime.Token)));
        toolbar.Items.Add(CreateToolbarButton(UiGlyph.Terminal, Ui.ConnectionEditor.OpenClient, (_, _) => OpenSelectedClient()));
        return toolbar;
    }

    private void OpenSelectedClient()
    {
        if (_selectedProfile is not
            {
                ConnectionId: var connectionId,
                Draft.Type: ConnectionProfileType.Client,
                Draft.Endpoint.Provider: StorageConnectionProvider.Ssh
            })
        {
            ShowStatus(Ui.ConnectionEditor.SelectClientProfileFirst, StorageHubTheme.Warning);
            return;
        }

        var terminal = new SshTerminalForm(
            connectionId,
            _selectedProfile.Draft.Metadata.DisplayName);
        terminal.Show(this);
    }

    private TableLayoutPanel BuildEditorHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 130,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(18, 12, 18, 10),
            BackColor = StorageHubTheme.Surface
        };
        // Measured rather than fixed at 130: that width fitted the English labels, so
        // "Udbyder / protokol" arrived truncated in Danish. The English width stays the floor.
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, MeasureHeaderLabelWidth()));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(new Label
        {
            Text = Ui.ConnectionEditor.TypeLabel,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = StorageHubTheme.Text
        }, 0, 0);
        header.Controls.Add(_typeSelector, 1, 0);
        header.Controls.Add(new Label
        {
            Text = Ui.ConnectionEditor.ProviderProtocol,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = StorageHubTheme.Text
        }, 0, 1);
        header.Controls.Add(_providerSelector, 1, 1);
        header.Controls.Add(_providerSummary, 1, 2);
        return header;
    }

    /// <summary>The width the two header labels need, with the original 130 as the floor.</summary>
    private int MeasureHeaderLabelWidth()
    {
        const int MinimumHeaderLabelWidth = 130;
        const int LabelGap = 12;
        var widest = Math.Max(
            TextRenderer.MeasureText(Ui.ConnectionEditor.TypeLabel, Font).Width,
            TextRenderer.MeasureText(Ui.ConnectionEditor.ProviderProtocol, Font).Width);
        return Math.Max(MinimumHeaderLabelWidth, widest + LabelGap);
    }

    private void TypeSelectionChanged(object? sender, EventArgs e)
    {
        if (!_loadingEditor && _typeSelector.SelectedItem is ConnectionProfileType type)
        {
            PopulateProviderSelector(type);
        }
    }

    private void PopulateProviderSelector(ConnectionProfileType type, StorageProviderKind? preferred = null)
    {
        var providers = ConnectionProviderCatalog.All.Where(provider => provider.Type == type).ToArray();
        _providerSelector.Items.Clear();
        _providerSelector.Items.AddRange(providers.Cast<object>().ToArray());
        _providerSelector.SelectedItem = preferred is { } kind
            ? providers.FirstOrDefault(provider => provider.Kind == kind) ?? providers.FirstOrDefault()
            : providers.FirstOrDefault();
    }

    private TableLayoutPanel BuildFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            ColumnCount = 2,
            Padding = new Padding(12, 8, 12, 8),
            BackColor = StorageHubTheme.Surface
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(_testState, 0, 0);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty
        };
        var cancel = new StorageHubButton { Text = Ui.ConnectionEditor.Cancel, DialogResult = DialogResult.Cancel };
        cancel.Variant = StorageHubButtonVariant.Secondary;
        var primary = new StorageHubButton
        {
            Text = _quickConnectMode ? Ui.ConnectionEditor.ConnectWithoutSaving : Ui.ConnectionEditor.SaveProfile,
            AccessibleDescription = _quickConnectMode
                ? Ui.ConnectionEditor.ConnectWithoutSavingHint
                : Ui.ConnectionEditor.SaveProfileHint
        };
        if (_quickConnectMode)
        {
            primary.DialogResult = DialogResult.OK;
        }
        else
        {
            primary.Click += async (_, _) => await SaveProfileAsync(_formLifetime.Token);
        }
        primary.Variant = StorageHubButtonVariant.Primary;
        actions.Controls.Add(cancel);
        actions.Controls.Add(primary);
        footer.Controls.Add(actions, 1, 0);
        AcceptButton = primary;
        CancelButton = cancel;
        return footer;
    }

    private static TabPage NewPage(string name) => new(name)
    {
        Padding = new Padding(8)
    };

    private void ProviderSelectionChanged(object? sender, EventArgs e)
    {
        if (_providerSelector.SelectedItem is ConnectionProviderDescriptor provider)
        {
            UpdateProviderEditor(provider);
        }
    }

    private async Task SelectProfileCardAsync(ConnectionCardModel card)
    {
        if (_loadingEditor)
        {
            return;
        }

        _typeSelector.SelectedItem = card.Type;
        PopulateProviderSelector(card.Type, card.Provider);
        if (card.ConnectionId is not { } connectionId)
        {
            _profileLoadCancellation?.Cancel();
            _profileLoading = false;
            _selectedProfile = null;
            return;
        }

        _profileLoadCancellation?.Cancel();
        _profileLoadCancellation?.Dispose();
        var loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(_formLifetime.Token);
        _profileLoadCancellation = loadCancellation;
        _profileLoading = true;
        try
        {
            await LoadProfileAsync(connectionId, loadCancellation.Token);
        }
        finally
        {
            if (ReferenceEquals(_profileLoadCancellation, loadCancellation))
            {
                _profileLoading = false;
            }
        }
    }

    private void UpdateProviderEditor(ConnectionProviderDescriptor provider)
    {
        _editorFields.Clear();
        _secretReferenceBoxes.Clear();
        _providerSummary.Text = provider.Summary;
        _providerAccent.BackColor = StorageHubTheme.ParseAccent(provider.AccentHex);
        ReplacePageContent(_generalPage, BuildProviderPage(Ui.ConnectionEditor.TabEndpoint, provider.EndpointExample, provider.GeneralFields, provider));
        ReplacePageContent(_authenticationPage, BuildProviderPage(
            Ui.ConnectionEditor.TabAuthentication,
            Ui.ConnectionEditor.SecretsLiveInVault,
            provider.AuthenticationFields,
            provider));
        ReplacePageContent(_securityPage, BuildSecurityPage(provider));
        if (provider.Kind == StorageProviderKind.S3)
        {
            ConfigureS3Editor();
        }
        if (provider.Kind is StorageProviderKind.Sftp or StorageProviderKind.Ssh)
        {
            ConfigureSshAuthenticationEditor(provider.Kind == StorageProviderKind.Ssh);
        }
        ApplyConnectionDefaults(provider);

        _testState.Text = Ui.ConnectionEditor.NotTested;
        _testState.ForeColor = StorageHubTheme.TextMuted;
    }

    private void ConfigureS3Editor()
    {
        if (!_editorFields.TryGetValue("s3ServiceType", out var serviceControl) ||
            serviceControl is not StorageHubChoiceField serviceType ||
            !_editorFields.TryGetValue("endpoint", out var endpointControl) ||
            endpointControl is not StorageHubTextField endpoint ||
            !_editorFields.TryGetValue("region", out var regionControl) ||
            regionControl is not StorageHubTextField region ||
            !_editorFields.TryGetValue("addressingStyle", out var addressingControl) ||
            addressingControl is not StorageHubChoiceField addressingStyle)
        {
            return;
        }

        void ApplyServicePreset()
        {
            var selected = serviceType.SelectedItem as string;
            var isR2 = string.Equals(
                selected,
                ConnectionEditorDraftFactory.CloudflareR2ServiceType,
                StringComparison.Ordinal);
            region.ReadOnly = isR2;
            region.AccessibleDescription = isR2
                ? Ui.ConnectionEditor.R2SigningRegion
                : Ui.ConnectionEditor.SigningRegionHint;
            if (isR2)
            {
                region.Text = "auto";
                addressingStyle.SelectedItem = "Path-style";
                if (string.Equals(endpoint.Text.Trim(), "https://s3.amazonaws.com", StringComparison.OrdinalIgnoreCase))
                {
                    endpoint.Clear();
                }

                endpoint.PlaceholderText = "account-id.r2.cloudflarestorage.com";
            }
            else
            {
                if (string.Equals(
                        selected,
                        ConnectionEditorDraftFactory.AmazonS3ServiceType,
                        StringComparison.Ordinal) &&
                    ConnectionEditorDraftFactory.IsCloudflareR2Endpoint(endpoint.Text))
                {
                    endpoint.Text = "https://s3.amazonaws.com";
                    region.Text = "us-east-1";
                    addressingStyle.SelectedItem = "Virtual-hosted (recommended)";
                }

                endpoint.PlaceholderText = "https://s3.example.com";
            }
        }

        serviceType.SelectedIndexChanged += (_, _) => ApplyServicePreset();
        endpoint.TextChanged += (_, _) =>
        {
            if (ConnectionEditorDraftFactory.IsCloudflareR2Endpoint(endpoint.Text) &&
                !string.Equals(
                    serviceType.SelectedItem as string,
                    ConnectionEditorDraftFactory.CloudflareR2ServiceType,
                    StringComparison.Ordinal))
            {
                serviceType.SelectedItem = ConnectionEditorDraftFactory.CloudflareR2ServiceType;
            }
        };
        ApplyServicePreset();
    }

    private void ConfigureSshAuthenticationEditor(bool supportsMultiFactor)
    {
        if (!_editorFields.TryGetValue("authenticationMode", out var modeControl) ||
            modeControl is not StorageHubChoiceField mode ||
            !_editorFields.TryGetValue("passwordReference", out var password) ||
            !_editorFields.TryGetValue("privateKeyReference", out var privateKey) ||
            !_editorFields.TryGetValue("privateKeyPassphraseReference", out var passphrase))
        {
            return;
        }

        void ApplyAuthenticationMode()
        {
            var selected = mode.SelectedItem as string;
            var usesPassword = string.Equals(selected, "Password reference", StringComparison.Ordinal) ||
                supportsMultiFactor && string.Equals(
                    selected,
                    "Private key + password (MFA)",
                    StringComparison.Ordinal);
            var usesKey = string.Equals(selected, "Private key reference", StringComparison.Ordinal) ||
                supportsMultiFactor && string.Equals(
                    selected,
                    "Private key + password (MFA)",
                    StringComparison.Ordinal);
            password.Enabled = usesPassword;
            privateKey.Enabled = usesKey;
            passphrase.Enabled = usesKey;
            mode.AccessibleDescription = selected == "Private key + password (MFA)"
                ? Ui.ConnectionEditor.SshRequiresPublicKeyThenPassword
                : Ui.ConnectionEditor.SelectOneAuthMethod;
        }

        mode.SelectedIndexChanged += (_, _) => ApplyAuthenticationMode();
        ApplyAuthenticationMode();
    }

    private void ApplyConnectionDefaults(ConnectionProviderDescriptor provider)
    {
        var defaults = ConnectionDefaultSettings.Get(provider.Kind, _connectionDefaults);
        foreach (var field in ConnectionDefaultSettings.EditableFields(provider))
        {
            if (_editorFields.TryGetValue(field.Key, out var control) &&
                defaults.FieldValues.TryGetValue(field.Key, out var value) &&
                !(field.Key == "endpoint" && !string.IsNullOrWhiteSpace(_initialEndpoint)))
            {
                SetControlValue(control, value);
            }
        }
    }

    private Panel BuildProviderPage(
        string title,
        string description,
        IReadOnlyList<ConnectionFieldDescriptor> fields,
        ConnectionProviderDescriptor provider)
    {
        var content = CreateScrollableContent(title, description, out var table);
        if (string.Equals(title, Ui.ConnectionEditor.TabEndpoint, StringComparison.Ordinal))
        {
            var profileName = new StorageHubTextField
            {
                Text = _quickConnectMode ? Ui.Format(Ui.ConnectionEditor.TemporaryConnectionFormat, provider.ShortName) : Ui.Format(Ui.ConnectionEditor.NewProviderFormat, provider.DisplayName)
            };
            _editorFields["profileName"] = profileName;
            UiControlFactory.AddLabeledRow(
                table,
                Ui.ConnectionEditor.ProfileNameRequired,
                profileName,
                _quickConnectMode
                    ? Ui.ConnectionEditor.SessionNameHint
                    : Ui.ConnectionEditor.ProfileNameHint);
            var folder = new StorageHubTextField { PlaceholderText = Ui.ConnectionEditor.TeamProject };
            _editorFields["folder"] = folder;
            UiControlFactory.AddLabeledRow(
                table,
                Ui.ConnectionEditor.FolderLabel,
                folder,
                Ui.ConnectionEditor.FolderHint);
            var labels = new StorageHubTextField { PlaceholderText = "production, customer-a, critical" };
            _editorFields["labels"] = labels;
            UiControlFactory.AddLabeledRow(
                table,
                Ui.ConnectionEditor.Labels,
                labels,
                Ui.ConnectionEditor.LabelsHint);

            // Registered under both keys so the generic read/write path carries each one; the
            // control itself is a single row holding the icon button and the colour swatches.
            var appearance = new ConnectionAppearanceField { AccentColor = provider.AccentHex };
            _editorFields["iconKey"] = appearance;
            _editorFields["accentColor"] = appearance;
            UiControlFactory.AddLabeledRow(
                table,
                Ui.ConnectionEditor.Appearance,
                appearance,
                Ui.ConnectionEditor.AppearanceHint);
            var badge = new Label
            {
                Text = Ui.Format(
                    Ui.ConnectionEditor.ProviderColorBadgeFormat, provider.ShortName, provider.AccentHex),
                AutoSize = true,
                ForeColor = StorageHubTheme.ParseAccent(provider.AccentHex),
                BackColor = StorageHubTheme.SurfaceMuted,
                Padding = new Padding(6, 5, 6, 5)
            };
            UiControlFactory.AddLabeledRow(
                table,
                Ui.ConnectionEditor.ConnectionBadge,
                badge,
                Ui.ConnectionEditor.BadgeHint);
        }

        foreach (var field in fields)
        {
            var control = BuildFieldControl(field, provider);
            _editorFields[field.Key] = control;
            var requiredSuffix = field.Required ? " *" : string.Empty;
            UiControlFactory.AddLabeledRow(table, field.Label + requiredSuffix, control, field.HelpText);
        }

        return content;
    }

    private Panel BuildSecurityPage(ConnectionProviderDescriptor provider)
    {
        var content = CreateScrollableContent(Ui.ConnectionEditor.TabTrust, provider.TrustNotice, out var table);
        var notice = new Panel
        {
            Height = 62,
            Dock = DockStyle.Top,
            BackColor = provider.EncryptedByDefault ? StorageHubTheme.SuccessTint : StorageHubTheme.WarningTint,
            Margin = new Padding(4, 4, 4, 12),
            AccessibleName = provider.EncryptedByDefault ? Ui.ConnectionEditor.SecureTransportPolicy : Ui.ConnectionEditor.PlaintextWarning
        };
        var icon = new PictureBox
        {
            Image = CreateOwnedImage(provider.EncryptedByDefault ? UiGlyph.Lock : UiGlyph.Warning, provider.EncryptedByDefault ? StorageHubTheme.Success : StorageHubTheme.Warning),
            SizeMode = PictureBoxSizeMode.CenterImage,
            Dock = DockStyle.Left,
            Width = 48
        };
        var warning = new Label
        {
            Text = provider.TrustNotice,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Padding = new Padding(0, 9, 10, 6),
            ForeColor = provider.EncryptedByDefault ? StorageHubTheme.Success : StorageHubTheme.Warning
        };
        notice.Controls.Add(warning);
        notice.Controls.Add(icon);
        table.Controls.Add(notice, 0, table.RowCount);
        table.SetColumnSpan(notice, 2);
        table.RowCount++;

        foreach (var field in provider.SecurityFields)
        {
            var control = BuildFieldControl(field, provider);
            _editorFields[field.Key] = control;
            UiControlFactory.AddLabeledRow(
                table,
                field.Label + (field.Required ? " *" : string.Empty),
                control,
                field.HelpText);
        }

        return content;
    }

    private Control BuildFieldControl(ConnectionFieldDescriptor field, ConnectionProviderDescriptor provider)
    {
        switch (field.Kind)
        {
            case ConnectionFieldKind.Toggle:
                var isEnabled = bool.TryParse(field.DefaultValue, out var selected) && selected;
                return Toggle(isEnabled, isEnabled ? Ui.ConnectionEditor.Enabled : Ui.ConnectionEditor.Disabled);
            case ConnectionFieldKind.Number:
                var defaultNumber = decimal.TryParse(field.DefaultValue, out var number)
                    ? number
                    : provider.DefaultPort ?? 0;
                return Numeric(defaultNumber, 0, 65535);
            case ConnectionFieldKind.Choice:
                return Choice(field.DefaultValue, field.Choices?.ToArray() ?? []);
            case ConnectionFieldKind.SecretReference:
                return VaultReferencePicker(field);
            case ConnectionFieldKind.CertificateReference:
                return VaultReferencePicker(field);
            case ConnectionFieldKind.Fingerprint:
                return FingerprintPicker(field);
            case ConnectionFieldKind.Path:
                return ReferencePicker(field.Placeholder, Ui.ConnectionEditor.Browse, readOnly: false, initialText: ResolveInitialValue(field));
            default:
                return new StorageHubTextField
                {
                    Text = ResolveInitialValue(field),
                    PlaceholderText = field.Placeholder
                };
        }
    }

    private string ResolveInitialValue(ConnectionFieldDescriptor field) =>
        string.Equals(field.Key, "host", StringComparison.Ordinal) ||
        string.Equals(field.Key, "endpoint", StringComparison.Ordinal) ||
        string.Equals(field.Key, "rootPath", StringComparison.Ordinal)
            ? (string.IsNullOrWhiteSpace(_initialEndpoint) ? field.DefaultValue : _initialEndpoint)
            : field.DefaultValue;

    private static Panel CreateScrollableContent(string title, string description, out TableLayoutPanel table)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = StorageHubTheme.Surface,
            Padding = new Padding(10)
        };
        table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(8),
            BackColor = StorageHubTheme.Surface
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var heading = UiControlFactory.CreateSectionTitle(title);
        var summary = UiControlFactory.CreateDescription(description);
        table.Controls.Add(heading, 0, 0);
        table.SetColumnSpan(heading, 2);
        table.Controls.Add(summary, 0, 1);
        table.SetColumnSpan(summary, 2);
        table.RowCount = 2;
        panel.Controls.Add(table);
        return panel;
    }

    private static StorageHubNumberField Numeric(decimal value, decimal minimum, decimal maximum) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        Value = Math.Clamp(value, minimum, maximum),
        Width = 130
    };

    private static StorageHubChoiceField Choice(string selected, params string[] choices)
    {
        var combo = new StorageHubChoiceField { Width = 260 };
        foreach (var choice in choices)
        {
            combo.Items.Add(choice);
        }

        if (combo.Items.Count > 0)
        {
            var index = combo.Items.IndexOf(selected);
            combo.SelectedIndex = index >= 0 ? index : 0;
        }

        return combo;
    }

    private static StorageHubCheckBox Toggle(bool selected, string text) => new()
    {
        Text = text,
        Checked = selected,
        AutoSize = true
    };

    private static TableLayoutPanel ReferencePicker(
        string placeholder,
        string buttonText,
        bool readOnly = true,
        string initialText = "")
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = Padding.Empty
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var value = new StorageHubTextField
        {
            ReadOnly = readOnly,
            Text = initialText,
            PlaceholderText = placeholder,
            Dock = DockStyle.Fill,
            AccessibleDescription = readOnly ? Ui.ConnectionEditor.StoresReferenceOnly : string.Empty
        };
        var select = new StorageHubButton { Text = buttonText };
        select.Variant = StorageHubButtonVariant.Secondary;
        select.Margin = new Padding(6, 0, 0, 0);
        if (!readOnly)
        {
            select.Click += (_, _) =>
            {
                using var dialog = new FolderBrowserDialog
                {
                    Description = Ui.ConnectionEditor.ChooseRootFolder,
                    InitialDirectory = Directory.Exists(value.Text) ? value.Text : string.Empty,
                    ShowNewFolderButton = false,
                    UseDescriptionForTitle = true
                };
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    value.Text = dialog.SelectedPath;
                }
            };
        }

        panel.Controls.Add(value, 0, 0);
        panel.Controls.Add(select, 1, 0);
        return panel;
    }

    private TableLayoutPanel VaultReferencePicker(ConnectionFieldDescriptor field)
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 3,
            Dock = DockStyle.Top,
            Margin = Padding.Empty
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var value = new StorageHubTextField
        {
            ReadOnly = true,
            PlaceholderText = field.Placeholder,
            Dock = DockStyle.Fill,
            AccessibleName = field.Label,
            AccessibleDescription = Ui.ConnectionEditor.VaultReferenceHint
        };
        _secretReferenceBoxes[field.Key] = value;
        var enroll = new StorageHubButton { Text = Ui.ConnectionEditor.EnrollOrReplace };
        var delete = new StorageHubButton { Text = Ui.ConnectionEditor.Delete };
        enroll.Variant = StorageHubButtonVariant.Secondary;
        delete.Variant = StorageHubButtonVariant.Secondary;
        enroll.Margin = new Padding(6, 0, 0, 0);
        delete.Margin = new Padding(6, 0, 0, 0);
        enroll.Click += async (_, _) => await EnrollOrUpdateSecretAsync(field, value, _formLifetime.Token);
        delete.Click += async (_, _) => await DeleteSecretAsync(field, value, _formLifetime.Token);
        panel.Controls.Add(value, 0, 0);
        panel.Controls.Add(enroll, 1, 0);
        panel.Controls.Add(delete, 2, 0);

        // Material fields can also borrow an already-imported key instead of enrolling a new copy.
        if (KeyStoreSlotKind(field.Key) is { } kind)
        {
            var choose = new StorageHubButton { Text = Ui.ConnectionEditor.KeyStore, Margin = new Padding(6, 0, 0, 0) };
            choose.Variant = StorageHubButtonVariant.Secondary;
            choose.Click += async (_, _) => await ChooseFromKeyStoreAsync(field, kind, _formLifetime.Token);
            panel.ColumnCount = 4;
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.Controls.Add(choose, 3, 0);
        }

        return panel;
    }

    private TableLayoutPanel FingerprintPicker(ConnectionFieldDescriptor field)
    {
        var canFetchFromHost = string.Equals(field.Key, "hostKeyFingerprint", StringComparison.Ordinal);
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = canFetchFromHost ? 3 : 2,
            Dock = DockStyle.Top,
            Margin = Padding.Empty
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        if (canFetchFromHost)
        {
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        }
        var value = new StorageHubTextField
        {
            PlaceholderText = field.Placeholder,
            Dock = DockStyle.Fill,
            AccessibleName = field.Label,
            AccessibleDescription = Ui.ConnectionEditor.VerifyFingerprintHint
        };
        var reject = new StorageHubButton
        {
            Text = Ui.ConnectionEditor.Reject,
            AccessibleDescription = Ui.ConnectionEditor.RejectHint
        };
        reject.Variant = StorageHubButtonVariant.Secondary;
        reject.Margin = new Padding(6, 0, 0, 0);
        reject.Click += async (_, _) => await RejectFingerprintAsync(value, _formLifetime.Token);
        panel.Controls.Add(value, 0, 0);
        if (canFetchFromHost)
        {
            var fetch = new StorageHubButton
            {
                Text = Ui.ConnectionEditor.FetchFromHost,
                AccessibleDescription = Ui.ConnectionEditor.FetchFromHostHint
            };
            fetch.Variant = StorageHubButtonVariant.Secondary;
            fetch.Margin = new Padding(6, 0, 0, 0);
            fetch.Click += async (_, _) => await FetchSshHostKeyAsync(value, _formLifetime.Token);
            panel.Controls.Add(fetch, 1, 0);
            panel.Controls.Add(reject, 2, 0);
        }
        else
        {
            panel.Controls.Add(reject, 1, 0);
        }

        return panel;
    }

    private async void SettingsTabSelected(object? sender, EventArgs e)
    {
        if (_settingsTabs.SelectedTab != _securityPage ||
            _sshHostKeyDiscoveryMode == SshHostKeyDiscoveryMode.Manual ||
            !TryGetSftpDiscoveryTarget(out var host, out var port, out var fingerprint) ||
            !string.IsNullOrWhiteSpace(fingerprint.Text))
        {
            return;
        }

        var endpoint = $"{host}:{port}";
        if (string.Equals(_lastHostKeyDiscoveryOffer, endpoint, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _lastHostKeyDiscoveryOffer = endpoint;
        if (_sshHostKeyDiscoveryMode == SshHostKeyDiscoveryMode.AskBeforeFetching)
        {
            var choice = MessageBox.Show(
                this,
                Ui.Format(Ui.Dialogs.FetchHostKeyPromptFormat, endpoint),
                Ui.Dialogs.FetchHostKeyCaption,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (choice != DialogResult.Yes)
            {
                return;
            }
        }

        await FetchSshHostKeyAsync(fingerprint, _formLifetime.Token);
    }

    private async Task FetchSshHostKeyAsync(StorageHubTextField fingerprint, CancellationToken cancellationToken)
    {
        if (_hostKeyDiscoveryActive)
        {
            ShowStatus(Ui.ConnectionEditor.FetchAlreadyRunning, StorageHubTheme.Warning);
            return;
        }

        if (!TryGetSftpDiscoveryTarget(out var host, out var port, out _))
        {
            ShowStatus(Ui.ConnectionEditor.EnterValidSftpEndpoint, StorageHubTheme.Warning);
            return;
        }

        _hostKeyDiscoveryActive = true;
        try
        {
            ShowStatus(Ui.Format(Ui.ConnectionEditor.FetchingHostKeyFormat, host, port), StorageHubTheme.TextMuted);
            var response = await _profileClient.DiscoverSshHostKeyAsync(
                new ConnectionSshHostKeyDiscoveryRequest(
                    ConnectionTrustIpcContract.CurrentVersion,
                    host,
                    port),
                cancellationToken);
            if (response.Failure is not null ||
                response.Sha256Fingerprint is not { } discoveredFingerprint ||
                response.HostKeyAlgorithm is not { } algorithm)
            {
                ShowStatus(
                    response.Failure?.Message ?? Ui.ConnectionEditor.HostKeyUnusable,
                    StorageHubTheme.Warning);
                return;
            }

            var choice = MessageBox.Show(
                this,
                Ui.Format(Ui.Dialogs.VerifyHostKeyPromptFormat, algorithm, discoveredFingerprint),
                Ui.Dialogs.VerifyHostKeyCaption,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (choice != DialogResult.Yes)
            {
                ShowStatus(Ui.ConnectionEditor.HostKeyNotAdded, StorageHubTheme.Warning);
                return;
            }

            fingerprint.Text = discoveredFingerprint;
            ShowStatus(Ui.ConnectionEditor.HostKeyAdded, StorageHubTheme.Success);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            ShowStatus(Ui.ConnectionEditor.HostKeyFetchFailed, StorageHubTheme.Warning);
        }
        finally
        {
            _hostKeyDiscoveryActive = false;
        }
    }

    private bool TryGetSftpDiscoveryTarget(
        out string host,
        out int port,
        out StorageHubTextField fingerprint)
    {
        host = string.Empty;
        port = 0;
        fingerprint = null!;
        if (_providerSelector.SelectedItem is not ConnectionProviderDescriptor
            { Kind: StorageProviderKind.Sftp or StorageProviderKind.Ssh } ||
            !_editorFields.TryGetValue("host", out var hostControl) ||
            !_editorFields.TryGetValue("port", out var portControl) ||
            !_editorFields.TryGetValue("hostKeyFingerprint", out var fingerprintControl))
        {
            return false;
        }

        host = GetControlValue(hostControl).Trim();
        if (!int.TryParse(
                GetControlValue(portControl),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out port))
        {
            return false;
        }

        fingerprint = fingerprintControl.Controls.OfType<StorageHubTextField>().FirstOrDefault()!;
        return fingerprint is not null && new ConnectionSshHostKeyDiscoveryRequest(
            ConnectionTrustIpcContract.CurrentVersion,
            host,
            port).HasValidBounds;
    }

    private ToolStripButton CreateToolbarButton(UiGlyph glyph, string tooltip, EventHandler click)
    {
        var image = CreateOwnedImage(glyph, StorageHubTheme.Text);
        var button = new ToolStripButton
        {
            Image = image,
            Text = tooltip,
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            ToolTipText = tooltip,
            AccessibleName = tooltip
        };
        button.Click += click;

        return button;
    }

    private Bitmap CreateOwnedImage(UiGlyph glyph, Color color)
    {
        var image = UiIconFactory.Create(glyph, color, 18, DeviceDpi / 96F);
        _ownedImages.Add(image);
        return image;
    }

    private void MarkConnectionTested()
    {
        _testState.Text = Ui.ConnectionEditor.ChangesNotTested;
        _testState.ForeColor = StorageHubTheme.TextMuted;
    }

    private async Task LoadProfileAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        try
        {
            ShowStatus(Ui.ConnectionEditor.LoadingProfile, StorageHubTheme.TextMuted);
            var response = await _controller.GetAsync(connectionId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (response.Profile is null)
            {
                ShowStatus(response.Failure?.Message ?? Ui.ConnectionEditor.ProfileLoadFailed, StorageHubTheme.Warning);
                return;
            }

            _loadingEditor = true;
            try
            {
                _selectedProfile = response.Profile;
                var provider = ConnectionCardFactory.MapProvider(response.Profile.Draft.Endpoint.Provider);
                _typeSelector.SelectedItem = response.Profile.Draft.Type;
                PopulateProviderSelector(response.Profile.Draft.Type, provider);
                var values = ConnectionEditorDraftFactory.ToEditorValues(response.Profile);
                foreach (var pair in values)
                {
                    if (_editorFields.TryGetValue(pair.Key, out var control))
                    {
                        SetControlValue(control, pair.Value, pair.Key);
                    }
                }


                await LoadTrustIntoEditorAsync(response.Profile, cancellationToken);
            }
            finally
            {
                _loadingEditor = false;
            }

            ShowStatus(Ui.Format(Ui.ConnectionEditor.LoadedVersionFormat, response.Profile.Version), StorageHubTheme.Success);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            ShowStatus(Ui.ConnectionEditor.ProfileLoadFailedThroughAgent, StorageHubTheme.Warning);
        }
    }

    private async Task SaveProfileAsync(CancellationToken cancellationToken)
    {
        if (_quickConnectMode || _providerSelector.SelectedItem is not ConnectionProviderDescriptor provider)
        {
            return;
        }

        if (_profileLoading)
        {
            ShowStatus(Ui.ConnectionEditor.WaitBeforeSaving, StorageHubTheme.Warning);
            return;
        }

        try
        {
            var draft = ConnectionEditorDraftFactory.Build(
                provider.Kind,
                ReadEditorValues(),
                ConnectionDefaultSettings.Get(provider.Kind, _connectionDefaults));
            ShowStatus(Ui.ConnectionEditor.SavingProfile, StorageHubTheme.TextMuted);
            var response = await _controller.SaveAsync(draft, _selectedProfile, cancellationToken);
            if (response.Status != ConnectionProfileWriteStatus.Succeeded || response.Profile is null)
            {
                ShowStatus(response.Failure?.Message ?? Ui.ConnectionEditor.ProfileSaveFailed, StorageHubTheme.Warning);
                return;
            }

            _selectedProfile = response.Profile;
            var fingerprint = GetPinnedFingerprint(response.Profile, ReadEditorValues());
            if (fingerprint is not null)
            {
                ShowStatus(Ui.ConnectionEditor.SavingVerifiedTrust, StorageHubTheme.TextMuted);
                var trustResponse = await _controller.TrustOrRolloverAsync(
                    response.Profile,
                    fingerprint,
                    cancellationToken);
                if (trustResponse.Status != ConnectionTrustMutationStatus.Succeeded)
                {
                    ShowStatus(
                        trustResponse.Failure?.Message ??
                        Ui.ConnectionEditor.PinNotEnrolled,
                        StorageHubTheme.Warning);
                    return;
                }
            }

            ProfilesChanged?.Invoke(this, EventArgs.Empty);
            ShowStatus(Ui.Format(Ui.ConnectionEditor.SavedVersionFormat, response.Profile.Version), StorageHubTheme.Success);

            // Saving is the end of the task, so the dialog gets out of the way. Every failure path
            // above returns before here, which is what keeps an unsaved edit on screen.
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ArgumentException error)
        {
            ShowStatus(error.Message, StorageHubTheme.Warning);
        }
        catch (Exception)
        {
            ShowStatus(
                Ui.ConnectionEditor.AgentIncompatible,
                StorageHubTheme.Warning);
        }
    }

    private async Task DeleteProfileAsync(CancellationToken cancellationToken)
    {
        if (_profileLoading)
        {
            ShowStatus(Ui.ConnectionEditor.WaitBeforeDeleting, StorageHubTheme.Warning);
            return;
        }

        if (_selectedProfile is null)
        {
            ShowStatus(Ui.ConnectionEditor.SelectProfileBeforeDeleting, StorageHubTheme.Warning);
            return;
        }

        if (MessageBox.Show(
                this,
                ConnectionCardFactory.DeleteConfirmationPrompt(_selectedProfile.Draft.Metadata.DisplayName),
                ConnectionCardFactory.DeleteConfirmationCaption,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            var response = await _controller.DeleteAsync(_selectedProfile, cancellationToken);
            if (response.Status != ConnectionProfileWriteStatus.Succeeded)
            {
                ShowStatus(response.Failure?.Message ?? Ui.ConnectionEditor.ProfileDeleteFailed, StorageHubTheme.Warning);
                return;
            }

            _selectedProfile = null;
            ProfilesChanged?.Invoke(this, EventArgs.Empty);
            StartNewProfile();
            ShowStatus(Ui.ConnectionEditor.ProfileDeleted, StorageHubTheme.Success);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            ShowStatus(Ui.ConnectionEditor.ProfileDeleteFailedThroughAgent, StorageHubTheme.Warning);
        }
    }

    private async Task TestSelectedConnectionAsync(CancellationToken cancellationToken)
    {
        if (_selectedProfile is null)
        {
            MarkConnectionTested();
            return;
        }

        try
        {
            ShowStatus(Ui.ConnectionEditor.TestingConnection, StorageHubTheme.TextMuted);
            if (_selectedProfile.Draft is
                {
                    Type: ConnectionProfileType.Client,
                    Endpoint.Provider: StorageConnectionProvider.Ssh
                })
            {
                await using var ssh = new NamedPipeSshTerminalAgentClient();
                var opened = await ssh.OpenAsync(new SshTerminalOpenRequest(
                    SshTerminalIpcContract.CurrentVersion,
                    _selectedProfile.ConnectionId,
                    80,
                    24), cancellationToken);
                if (opened.Failure is not null)
                {
                    ShowStatus(opened.Failure.Message, StorageHubTheme.Warning);
                    return;
                }

                _ = await ssh.CloseAsync(new SshTerminalCloseRequest(
                    SshTerminalIpcContract.CurrentVersion,
                    opened.SessionId), cancellationToken);
                ShowStatus(Ui.ConnectionEditor.SshConnectionSucceeded, StorageHubTheme.Success);
                return;
            }

            var response = await _storageClient.TestConnectionAsync(
                new ConnectionTestRequest(StorageIpcContract.CurrentVersion, _selectedProfile.ConnectionId),
                cancellationToken);
            ProfilesChanged?.Invoke(this, EventArgs.Empty);
            ShowStatus(
                response.Succeeded
                    ? Ui.Format(Ui.ConnectionEditor.ConnectionSucceededFormat, response.ElapsedMilliseconds)
                    : response.Failure?.Message ?? Ui.ConnectionEditor.ConnectionTestFailed,
                response.Succeeded ? StorageHubTheme.Success : StorageHubTheme.Warning);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            ShowStatus(Ui.ConnectionEditor.AgentCannotTest, StorageHubTheme.Warning);
        }
    }

    private async Task EnrollOrUpdateSecretAsync(
        ConnectionFieldDescriptor field,
        StorageHubTextField referenceBox,
        CancellationToken cancellationToken)
    {
        byte[]? material = null;
        try
        {
            material = await PromptForSecretMaterialAsync(field, cancellationToken);
            if (material is null)
            {
                return;
            }

            ShowStatus(Ui.ConnectionEditor.WritingVaultEntry, StorageHubTheme.TextMuted);
            var response = await _controller.EnrollOrUpdateSecretAsync(
                MapSecretPurpose(field.Key),
                string.IsNullOrWhiteSpace(referenceBox.Text) ? null : referenceBox.Text,
                material,
                cancellationToken);
            if (!response.Succeeded || response.Reference is null)
            {
                ShowStatus(response.Failure?.Message ?? Ui.ConnectionEditor.VaultEntryFailed, StorageHubTheme.Warning);
                return;
            }

            referenceBox.Text = response.Reference;
            ShowStatus(Ui.Format(Ui.ConnectionEditor.VaultReferenceReadyFormat, response.Version), StorageHubTheme.Success);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            ShowStatus(Ui.ConnectionEditor.SecretOperationFailed, StorageHubTheme.Warning);
        }
        finally
        {
            if (material is not null)
            {
                CryptographicOperations.ZeroMemory(material);
            }
        }
    }

    private async Task DeleteSecretAsync(
        ConnectionFieldDescriptor field,
        StorageHubTextField referenceBox,
        CancellationToken cancellationToken)
    {
        var reference = referenceBox.Text.Trim();
        if (!ConnectionEndpointDocument.IsOpaqueSecretReference(reference) || reference.Length == 0)
        {
            ShowStatus(Ui.ConnectionEditor.NoVaultReference, StorageHubTheme.Warning);
            return;
        }

        if (MessageBox.Show(
                this,
                Ui.Dialogs.DeleteVaultSecretPrompt,
                Ui.Dialogs.DeleteVaultSecretCaption,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            var response = await _controller.DeleteSecretAsync(
                reference,
                MapSecretPurpose(field.Key),
                cancellationToken);
            if (!response.Succeeded)
            {
                ShowStatus(response.Failure?.Message ?? Ui.ConnectionEditor.VaultSecretDeleteFailed, StorageHubTheme.Warning);
                return;
            }

            referenceBox.Clear();
            ShowStatus(Ui.ConnectionEditor.VaultSecretDeleted, StorageHubTheme.Success);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            ShowStatus(Ui.ConnectionEditor.SecretDeleteFailed, StorageHubTheme.Warning);
        }
    }

    private async Task<byte[]?> PromptForSecretMaterialAsync(
        ConnectionFieldDescriptor field,
        CancellationToken cancellationToken)
    {
        if (field.Kind == ConnectionFieldKind.CertificateReference ||
            string.Equals(field.Key, "privateKeyReference", StringComparison.Ordinal))
        {
            using var picker = new OpenFileDialog
            {
                Title = field.Kind == ConnectionFieldKind.CertificateReference
                    ? Ui.ConnectionEditor.SelectPfxCertificate
                    : Ui.ConnectionEditor.SelectPrivateKey,
                Filter = field.Kind == ConnectionFieldKind.CertificateReference
                    ? Ui.ConnectionEditor.CertificateFilter
                    : Ui.ConnectionEditor.PrivateKeyFilter,
                CheckFileExists = true,
                Multiselect = false
            };
            if (picker.ShowDialog(this) != DialogResult.OK)
            {
                return null;
            }

            var info = new FileInfo(picker.FileName);
            if (info.Length is <= 0 or > SecretVaultIpcContract.MaximumSecretBytes)
            {
                throw new ArgumentException(Ui.ConnectionEditor.SecretFileInvalid);
            }

            return await File.ReadAllBytesAsync(picker.FileName, cancellationToken);
        }

        using var dialog = new Form
        {
            Text = Ui.Format(Ui.ConnectionEditor.EnrollFieldFormat, field.Label),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(520, 150),
            Padding = new Padding(14)
        };
        var input = new StorageHubTextField
        {
            Dock = DockStyle.Top,
            UseSystemPasswordChar = true,
            AccessibleName = field.Label,
            AccessibleDescription = Ui.ConnectionEditor.SecretSentToAgentOnly
        };
        var notice = new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            Text = Ui.ConnectionEditor.VaultEncryptionHint,
            ForeColor = StorageHubTheme.TextMuted
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.RightToLeft
        };
        var accept = new StorageHubButton { Text = Ui.ConnectionEditor.Enroll, DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new StorageHubButton { Text = Ui.ConnectionEditor.Cancel, DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(accept);
        buttons.Controls.Add(cancel);
        dialog.Controls.Add(input);
        dialog.Controls.Add(notice);
        dialog.Controls.Add(buttons);
        dialog.AcceptButton = accept;
        dialog.CancelButton = cancel;
        if (dialog.ShowDialog(this) != DialogResult.OK || input.Text.Length == 0)
        {
            input.Clear();
            return null;
        }

        var material = Encoding.UTF8.GetBytes(input.Text);
        input.Clear();
        return material;
    }

    private Dictionary<string, string> ReadEditorValues()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in _editorFields)
        {
            // The appearance row answers for two keys, so it is read by key rather than by type.
            values[pair.Key] = pair.Value is ConnectionAppearanceField appearance
                ? string.Equals(pair.Key, "accentColor", StringComparison.Ordinal)
                    ? appearance.AccentColor
                    : appearance.IconKey
                : GetControlValue(pair.Value);
        }

        return values;
    }

    private async Task LoadTrustIntoEditorAsync(
        ConnectionProfileDocument profile,
        CancellationToken cancellationToken)
    {
        var fieldKey = TrustFingerprintField(profile);
        if (fieldKey is null || !_editorFields.TryGetValue(fieldKey, out var control))
        {
            return;
        }

        var response = await _controller.GetTrustAsync(profile, cancellationToken);
        if (response.Snapshot is null)
        {
            throw new InvalidDataException(
                response.Failure?.Message ?? Ui.ConnectionEditor.TrustStateUnreadable);
        }

        var now = DateTimeOffset.UtcNow;
        var active = response.Snapshot.Records
            .Where(record => record.Decision == ConnectionTrustDecision.Trusted &&
                (record.ExpiresUtc is null || record.ExpiresUtc > now))
            .ToArray();
        SetControlValue(control, active.Length == 1 ? active[0].Sha256Fingerprint : string.Empty);
        if (active.Length > 1)
        {
            throw new InvalidDataException(
                Ui.ConnectionEditor.MultipleTrustRecords);
        }
    }

    private async Task RejectFingerprintAsync(StorageHubTextField value, CancellationToken cancellationToken)
    {
        if (_selectedProfile is null)
        {
            ShowStatus(Ui.ConnectionEditor.SaveBeforeRejecting, StorageHubTheme.Warning);
            return;
        }

        var fingerprint = value.Text.Trim();
        if (!ConnectionTrustIpcLimits.IsValidFingerprint(fingerprint))
        {
            ShowStatus(Ui.ConnectionEditor.EnterValidFingerprint, StorageHubTheme.Warning);
            return;
        }

        if (MessageBox.Show(
                this,
                Ui.Dialogs.RejectServerIdentityPrompt,
                Ui.Dialogs.RejectServerIdentityCaption,
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning) != DialogResult.OK)
        {
            return;
        }

        try
        {
            ShowStatus(Ui.ConnectionEditor.RecordingRejected, StorageHubTheme.TextMuted);
            var response = await _controller.RejectAsync(_selectedProfile, fingerprint, cancellationToken);
            if (response.Status != ConnectionTrustMutationStatus.Succeeded)
            {
                ShowStatus(response.Failure?.Message ?? Ui.ConnectionEditor.RejectedRecordFailed, StorageHubTheme.Warning);
                return;
            }

            value.Clear();
            ShowStatus(Ui.ConnectionEditor.RejectedRecorded, StorageHubTheme.Success);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            ShowStatus(Ui.ConnectionEditor.RejectedRecordFailedThroughAgent, StorageHubTheme.Warning);
        }
    }

    private static string? GetPinnedFingerprint(
        ConnectionProfileDocument profile,
        Dictionary<string, string> values)
    {
        var key = TrustFingerprintField(profile);
        return key is not null && values.TryGetValue(key, out var fingerprint) &&
            !string.IsNullOrWhiteSpace(fingerprint)
                ? fingerprint.Trim()
                : null;
    }

    private static string? TrustFingerprintField(ConnectionProfileDocument profile) =>
        profile.Draft.Endpoint switch
        {
            { Provider: StorageConnectionProvider.Ftps, TlsPolicy: ConnectionTlsCertificatePolicy.Pinned } =>
                "certificatePin",
            { Provider: StorageConnectionProvider.Sftp or StorageConnectionProvider.Ssh, SshHostKeyPolicy: ConnectionSshHostKeyPolicy.Pinned } =>
                "hostKeyFingerprint",
            _ => null
        };

    private void StartNewProfile()
    {
        _selectedProfile = null;
        if (_providerSelector.SelectedItem is ConnectionProviderDescriptor provider)
        {
            UpdateProviderEditor(provider);
        }

        ShowStatus(Ui.ConnectionEditor.NewUnsavedProfile, StorageHubTheme.TextMuted);
    }

    private void ShowStatus(string message, Color color)
    {
        _testState.Text = message;
        _testState.ForeColor = color;
    }

    private static string GetControlValue(Control control) => control switch
    {
        StorageHubTextField textBox => textBox.Text,
        StorageHubChoiceField comboBox => comboBox.SelectedItem?.ToString() ?? comboBox.Text,
        CheckBox checkBox => checkBox.Checked.ToString(System.Globalization.CultureInfo.InvariantCulture),
        StorageHubNumberField numeric => numeric.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        // A composite row -- a value with a Browse or Enroll button beside it -- keeps the value
        // in the one field inside it.
        _ => control.Controls.OfType<StorageHubTextField>().FirstOrDefault()?.Text ?? string.Empty
    };

    private static void SetControlValue(Control control, string value, string? key = null)
    {
        switch (control)
        {
            case ConnectionAppearanceField appearance
                when string.Equals(key, "accentColor", StringComparison.Ordinal):
                appearance.AccentColor = value;
                break;
            case ConnectionAppearanceField appearance:
                appearance.IconKey = value;
                break;
            case StorageHubTextField textBox:
                textBox.Text = value;
                break;
            case StorageHubChoiceField comboBox:
                var index = comboBox.Items.IndexOf(value);
                if (index >= 0)
                {
                    comboBox.SelectedIndex = index;
                }
                break;
            case CheckBox checkBox when bool.TryParse(value, out var selected):
                checkBox.Checked = selected;
                break;
            case StorageHubNumberField numeric when decimal.TryParse(
                value,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out var number):
                numeric.Value = Math.Clamp(number, numeric.Minimum, numeric.Maximum);
                break;
            default:
                var nested = control.Controls.OfType<StorageHubTextField>().FirstOrDefault();
                if (nested is not null)
                {
                    nested.Text = value;
                }
                break;
        }
    }

    /// <summary>
    /// The key material a field can accept, or null when the field is not a key store slot. Only
    /// material fields qualify: a passphrase is filled from whichever entry is chosen, never picked
    /// on its own.
    /// </summary>
    private static KeyStoreMaterialKind? KeyStoreSlotKind(string fieldKey) => fieldKey switch
    {
        "clientCertificateReference" => KeyStoreMaterialKind.Pkcs12Certificate,
        "privateKeyReference" => KeyStoreMaterialKind.SshPrivateKey,
        _ => null
    };

    /// <summary>The passphrase field unlocked by a given material field.</summary>
    private static string? CompanionPassphraseField(string fieldKey) => fieldKey switch
    {
        "clientCertificateReference" => "clientCertificatePasswordReference",
        "privateKeyReference" => "privateKeyPassphraseReference",
        _ => null
    };

    private async Task ChooseFromKeyStoreAsync(
        ConnectionFieldDescriptor field,
        KeyStoreMaterialKind kind,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var client = new NamedPipeKeyStoreAgentClient();
            var listed = await client.ListAsync(
                new KeyStoreListRequest(KeyStoreIpcContract.CurrentVersion, Kind: kind),
                cancellationToken);
            if (listed.Failure is not null)
            {
                ShowStatus(listed.Failure.Message, StorageHubTheme.Warning);
                return;
            }

            if (listed.Entries.Length == 0)
            {
                ShowStatus(
                    Ui.ConnectionEditor.NoStoredKeys,
                    StorageHubTheme.Warning);
                return;
            }

            using var picker = new KeyStorePickerForm(listed.Entries);
            if (picker.ShowDialog(this) != DialogResult.OK || picker.Selected is not { } chosen)
            {
                return;
            }

            if (!_secretReferenceBoxes.TryGetValue(field.Key, out var materialBox))
            {
                return;
            }

            // One entry fills both halves: provider APIs need the passphrase to open the material,
            // and the profile model requires them together.
            materialBox.Text = chosen.MaterialReference;
            if (CompanionPassphraseField(field.Key) is { } companionKey &&
                _secretReferenceBoxes.TryGetValue(companionKey, out var passphraseBox))
            {
                // A password-less certificate has no passphrase reference, so the companion field
                // is cleared rather than left pointing at whatever was there before.
                passphraseBox.Text = chosen.PassphraseReference ?? string.Empty;
            }

            ShowStatus(Ui.Format(Ui.ConnectionEditor.UsingStoredKeyFormat, chosen.DisplayName), StorageHubTheme.Success);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            ShowStatus(Ui.ConnectionEditor.KeyStoreUnreadable, StorageHubTheme.Warning);
        }
    }

    private static SecretMaterialPurpose MapSecretPurpose(string fieldKey) => fieldKey switch
    {
        "accessKeyReference" => SecretMaterialPurpose.AccessKey,
        "secretAccessKeyReference" => SecretMaterialPurpose.SecretAccessKey,
        "sessionTokenReference" => SecretMaterialPurpose.SessionToken,
        "privateKeyReference" => SecretMaterialPurpose.SshPrivateKey,
        "privateKeyPassphraseReference" => SecretMaterialPurpose.SshPrivateKeyPassphrase,
        "clientCertificateReference" => SecretMaterialPurpose.ClientCertificatePfx,
        "clientCertificatePasswordReference" => SecretMaterialPurpose.ClientCertificatePassword,
        "credentialReference" => SecretMaterialPurpose.ProxyCredential,
        _ => SecretMaterialPurpose.Password
    };

    private static void DisposeOwnedClient(IAsyncDisposable client, bool ownsClient)
    {
        if (ownsClient)
        {
            client.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static void ReplacePageContent(TabPage page, Control content)
    {
        var previousControls = page.Controls.Cast<Control>().ToArray();
        page.Controls.Clear();
        foreach (var previous in previousControls)
        {
            previous.Dispose();
        }

        page.Controls.Add(content);
    }
}
