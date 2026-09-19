using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// Manages the shared key and certificate store: import once, reference from any number of Storage
/// and Client profiles, see what is held and when it expires, and rotate in one place.
///
/// Importing is deliberately a two-step flow. Material is enrolled on the dedicated secret pipe,
/// which returns an opaque reference and never reads anything back; only that reference is then
/// registered here. Nothing in this form ever holds key material beyond the moment it is sent, and
/// the buffers it does hold are zeroed.
/// </summary>
public sealed class KeyStoreForm : Form
{
    private const int MaximumMaterialBytes = 16 * 1024 * 1024;

    private readonly IKeyStoreAgentClient _client;
    private readonly IRemoteSecretVaultClient _secrets;
    private readonly ListView _entries;
    private readonly StorageHubTextField _search;
    private readonly Label _status;
    private readonly StorageHubButton _importCertificate;
    private readonly StorageHubButton _importKey;
    private readonly StorageHubButton _rename;
    private readonly StorageHubButton _delete;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<KeyStoreEntryDocument> _loaded = [];
    private bool _busy;

    public KeyStoreForm(IKeyStoreAgentClient client, IRemoteSecretVaultClient secrets)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));

        Text = Ui.KeyStore.KeyStore;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = this.LogicalWindowSize(new Size(820, 460));
        Size = this.LogicalWindowSize(new Size(980, 560));
        BackColor = StorageHubTheme.Canvas;
        ForeColor = StorageHubTheme.Text;
        StorageHubTheme.Register(this);

        _entries = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            BorderStyle = BorderStyle.None,
            BackColor = StorageHubTheme.Surface,
            ForeColor = StorageHubTheme.Text,
            AccessibleName = Ui.KeyStore.StoredKeysAndCertificates
        };
        _entries.Columns.Add("Name", LogicalToDeviceUnits(200));
        _entries.Columns.Add("Kind", LogicalToDeviceUnits(110));
        _entries.Columns.Add(Ui.KeyStore.Identity, LogicalToDeviceUnits(260));
        _entries.Columns.Add(Ui.KeyStore.Expires, LogicalToDeviceUnits(130));
        _entries.Columns.Add(Ui.KeyStore.UsedBy, 80, HorizontalAlignment.Right);
        _entries.Columns.Add("Tags", LogicalToDeviceUnits(140));
        StorageHubTheme.ConfigureList(_entries);
        _entries.SelectedIndexChanged += (_, _) => UpdateActionState();

        _search = new StorageHubTextField
        {
            Width = LogicalToDeviceUnits(220),
            PlaceholderText = Ui.KeyStore.SearchNameOrDescription,
            AccessibleName = Ui.KeyStore.KeyStoreSearch
        };
        _search.TextChanged += async (_, _) => await ReloadAsync().ConfigureAwait(true);

        _importCertificate = CreateButton(Ui.KeyStore.ImportCertificate, ImportCertificateAsync);
        _importKey = CreateButton(Ui.KeyStore.ImportSSHKey, ImportSshKeyAsync);
        _rename = CreateButton(Ui.KeyStore.Rename, RenameSelectedAsync);
        _delete = CreateButton(Ui.KeyStore.Delete, DeleteSelectedAsync);
        _importCertificate.Variant = StorageHubButtonVariant.Primary;
        _importKey.Variant = StorageHubButtonVariant.Secondary;
        _rename.Variant = StorageHubButtonVariant.Secondary;
        _delete.Variant = StorageHubButtonVariant.Danger;

        _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = this.TextBoxHeight(6),
            Padding = this.LogicalToDeviceUnits(new Padding(10, 6, 10, 4)),
            ForeColor = StorageHubTheme.TextMuted,
            BackColor = StorageHubTheme.Surface,
            Text = Ui.KeyStore.Loading
        };

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = this.TextBoxHeight(26),
            Padding = this.LogicalToDeviceUnits(new Padding(10, 10, 10, 6)),
            BackColor = StorageHubTheme.Canvas,
            WrapContents = false
        };
        toolbar.Controls.AddRange([_importCertificate, _importKey, _rename, _delete, _search]);

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = this.LogicalToDeviceUnits(new Padding(10, 0, 10, 6)),
            BackColor = StorageHubTheme.Canvas
        };
        body.Controls.Add(_entries);

        Controls.Add(body);
        Controls.Add(toolbar);
        Controls.Add(_status);
        UpdateActionState();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await ReloadAsync().ConfigureAwait(true);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        base.OnFormClosed(e);
    }

    private StorageHubButton CreateButton(string text, Func<Task> handler)
    {
        var button = new StorageHubButton
        {
            Text = text,
            AutoSize = true,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 0, 8, 0)),
            Height = this.TextBoxHeight(8)
        };
        button.Click += async (_, _) =>
        {
            if (_busy) return;
            await RunAsync(handler).ConfigureAwait(true);
        };
        return button;
    }

    private async Task RunAsync(Func<Task> handler)
    {
        SetBusy(true);
        try
        {
            await handler().ConfigureAwait(true);
        }
        catch (Exception error) when (IsExpected(error))
        {
            ShowStatus(error.Message, StorageHubTheme.Danger);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ReloadAsync()
    {
        if (_lifetime.IsCancellationRequested) return;
        try
        {
            var response = await _client.ListAsync(
                new KeyStoreListRequest(
                    KeyStoreIpcContract.CurrentVersion,
                    string.IsNullOrWhiteSpace(_search.Text) ? null : _search.Text.Trim()),
                _lifetime.Token).ConfigureAwait(true);
            if (response.Failure is not null)
            {
                ShowStatus(response.Failure.Message, StorageHubTheme.Danger);
                return;
            }

            Populate(response.Entries);
            ShowStatus(
                response.Entries.Length == 0
                    ? Ui.KeyStore.NoKeysOrCertificatesAreStoredYet
                    : $"{response.Entries.Length:N0} stored item(s).",
                StorageHubTheme.TextMuted);
        }
        catch (Exception error) when (IsExpected(error))
        {
            ShowStatus($"The key store is unavailable: {error.Message}", StorageHubTheme.Danger);
        }
    }

    private void Populate(KeyStoreEntryDocument[] entries)
    {
        _loaded.Clear();
        _loaded.AddRange(entries);
        _entries.BeginUpdate();
        try
        {
            _entries.Items.Clear();
            foreach (var entry in entries)
            {
                var item = new ListViewItem(entry.DisplayName) { Tag = entry };
                item.SubItems.Add(entry.Kind is KeyStoreMaterialKind.Pkcs12Certificate
                    ? Ui.KeyStore.Certificate
                    : Ui.KeyStore.SSHKey);
                item.SubItems.Add(DescribeIdentity(entry));
                var expiry = item.SubItems.Add(DescribeExpiry(entry, out var severity));
                expiry.ForeColor = severity;
                item.SubItems.Add(entry.ReferencedByProfiles.Length.ToString(CultureInfo.CurrentCulture));
                item.SubItems.Add(string.Join(", ", entry.Tags));
                _entries.Items.Add(item);
            }
        }
        finally
        {
            _entries.EndUpdate();
        }

        UpdateActionState();
    }

    private static string DescribeIdentity(KeyStoreEntryDocument entry) =>
        entry.Kind is KeyStoreMaterialKind.Pkcs12Certificate
            ? entry.Summary.Subject ?? "(unknown subject)"
            : entry.Summary.Sha256Fingerprint ?? "(unknown fingerprint)";

    /// <summary>
    /// Certificates carry a hard expiry that silently breaks a connection when it passes, so the
    /// column warns before that happens. SSH keys have no expiry to report.
    /// </summary>
    private static string DescribeExpiry(KeyStoreEntryDocument entry, out Color severity)
    {
        severity = StorageHubTheme.TextMuted;
        if (entry.Summary.NotAfter is not { } notAfter)
        {
            return "-";
        }

        var remaining = notAfter - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            severity = StorageHubTheme.Danger;
            return Ui.KeyStore.Expired;
        }

        severity = remaining <= TimeSpan.FromDays(30) ? StorageHubTheme.Warning : StorageHubTheme.TextMuted;
        return notAfter.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.CurrentCulture);
    }

    private async Task ImportCertificateAsync() => await ImportAsync(
        KeyStoreMaterialKind.Pkcs12Certificate,
        Ui.KeyStore.PKCS12CertificatesPfxP12PfxP12,
        SecretMaterialPurpose.ClientCertificatePfx,
        SecretMaterialPurpose.ClientCertificatePassword,
        Ui.KeyStore.CertificatePassword,
        keyFormat: null).ConfigureAwait(true);

    private async Task ImportSshKeyAsync()
    {
        using var picker = new KeyFormatPromptForm();
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        await ImportAsync(
            KeyStoreMaterialKind.SshPrivateKey,
            Ui.KeyStore.PrivateKeysKeyPemKeyPem,
            SecretMaterialPurpose.SshPrivateKey,
            SecretMaterialPurpose.SshPrivateKeyPassphrase,
            Ui.KeyStore.KeyPassphrase,
            picker.SelectedFormat).ConfigureAwait(true);
    }

    private async Task ImportAsync(
        KeyStoreMaterialKind kind,
        string filter,
        SecretMaterialPurpose materialPurpose,
        SecretMaterialPurpose passphrasePurpose,
        string passphraseCaption,
        KeyStorePrivateKeyFormat? keyFormat)
    {
        using var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true, Title = Ui.KeyStore.SelectMaterial };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var file = new FileInfo(dialog.FileName);
        if (file.Length is 0 or > MaximumMaterialBytes)
        {
            ShowStatus(Ui.KeyStore.TheSelectedFileIsEmptyOrLarger, StorageHubTheme.Danger);
            return;
        }

        using var passphrasePrompt = new SecretPromptForm(passphraseCaption);
        if (passphrasePrompt.ShowDialog(this) != DialogResult.OK) return;

        if (DescribeMissingPassphrase(kind, passphrasePrompt.Value) is { } missing)
        {
            ShowStatus(missing, StorageHubTheme.Danger);
            return;
        }

        using var namePrompt = new TextPromptForm(Ui.KeyStore.NameThisEntry, Path.GetFileNameWithoutExtension(file.Name));
        if (namePrompt.ShowDialog(this) != DialogResult.OK) return;

        var material = await File.ReadAllBytesAsync(file.FullName, _lifetime.Token).ConfigureAwait(true);
        // An empty value means the material carries no password, so nothing is enrolled for it:
        // the vault stores secrets, not the absence of one.
        var passphrase = Encoding.UTF8.GetBytes(passphrasePrompt.Value);
        string? materialReference = null;
        string? passphraseReference = null;
        try
        {
            var enrolledMaterial = await _secrets
                .EnrollAsync(materialPurpose, material, _lifetime.Token).ConfigureAwait(true);
            if (!enrolledMaterial.Succeeded || enrolledMaterial.Reference is null)
            {
                ShowStatus(
                    enrolledMaterial.Failure?.Message ?? Ui.KeyStore.TheMaterialCouldNotBeEnrolled,
                    StorageHubTheme.Danger);
                return;
            }

            materialReference = enrolledMaterial.Reference;
            if (passphrase.Length > 0)
            {
                var enrolledPassphrase = await _secrets
                    .EnrollAsync(passphrasePurpose, passphrase, _lifetime.Token).ConfigureAwait(true);
                if (!enrolledPassphrase.Succeeded || enrolledPassphrase.Reference is null)
                {
                    ShowStatus(
                        enrolledPassphrase.Failure?.Message ?? Ui.KeyStore.ThePassphraseCouldNotBeEnrolled,
                        StorageHubTheme.Danger);
                    return;
                }

                passphraseReference = enrolledPassphrase.Reference;
            }
            var created = await _client.CreateAsync(
                new KeyStoreCreateRequest(
                    KeyStoreIpcContract.CurrentVersion,
                    kind,
                    namePrompt.Value,
                    null,
                    [],
                    materialReference,
                    passphraseReference,
                    keyFormat),
                _lifetime.Token).ConfigureAwait(true);

            if (created.Outcome is KeyStoreWriteOutcome.Applied)
            {
                // The agent now owns both envelopes; clearing the locals stops the cleanup below.
                materialReference = null;
                passphraseReference = null;
                await ReloadAsync().ConfigureAwait(true);
                ShowStatus($"Imported '{namePrompt.Value}'.", StorageHubTheme.Success);
                return;
            }

            ShowStatus(DescribeFailure(created), StorageHubTheme.Danger);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
            CryptographicOperations.ZeroMemory(passphrase);
            // An enrollment that never became an entry would otherwise leave an orphan envelope.
            await DiscardOrphanAsync(materialReference, materialPurpose).ConfigureAwait(true);
            await DiscardOrphanAsync(passphraseReference, passphrasePurpose).ConfigureAwait(true);
        }
    }

    private async Task DiscardOrphanAsync(string? reference, SecretMaterialPurpose purpose)
    {
        if (reference is null) return;
        try
        {
            _ = await _secrets.DeleteAsync(reference, purpose, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception error) when (IsExpected(error))
        {
            // Best effort: the entry was not created, so nothing references the envelope.
        }
    }

    private async Task RenameSelectedAsync()
    {
        if (Selected() is not { } entry) return;
        using var prompt = new TextPromptForm(Ui.KeyStore.RenameEntry, entry.DisplayName);
        if (prompt.ShowDialog(this) != DialogResult.OK) return;

        var updated = await _client.UpdateAsync(
            new KeyStoreUpdateRequest(
                KeyStoreIpcContract.CurrentVersion,
                entry.EntryId,
                prompt.Value,
                entry.Description,
                entry.Tags,
                entry.Version),
            _lifetime.Token).ConfigureAwait(true);

        if (updated.Outcome is KeyStoreWriteOutcome.Applied)
        {
            await ReloadAsync().ConfigureAwait(true);
            ShowStatus(Ui.KeyStore.Renamed, StorageHubTheme.Success);
            return;
        }

        ShowStatus(DescribeFailure(updated), StorageHubTheme.Danger);
    }

    private async Task DeleteSelectedAsync()
    {
        if (Selected() is not { } entry) return;
        if (entry.ReferencedByProfiles.Length > 0)
        {
            ShowStatus(
                $"'{entry.DisplayName}' is still used by {string.Join(", ", entry.ReferencedByProfiles)}.",
                StorageHubTheme.Danger);
            return;
        }

        var confirmed = MessageBox.Show(
            this,
            Ui.Format(Ui.Dialogs.DeleteStoredKeyPromptFormat, entry.DisplayName),
            Ui.Dialogs.DeleteStoredKeyCaption,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirmed != DialogResult.Yes) return;

        var deleted = await _client.DeleteAsync(
            new KeyStoreDeleteRequest(KeyStoreIpcContract.CurrentVersion, entry.EntryId, entry.Version),
            _lifetime.Token).ConfigureAwait(true);

        if (deleted.Outcome is KeyStoreWriteOutcome.Applied)
        {
            await ReloadAsync().ConfigureAwait(true);
            ShowStatus(Ui.KeyStore.Deleted, StorageHubTheme.Success);
            return;
        }

        ShowStatus(DescribeFailure(deleted), StorageHubTheme.Danger);
    }

    /// <summary>
    /// Explains why material is refused, or null when it can be stored.
    ///
    /// A PKCS#12 bundle may legitimately carry no password, so an empty value is accepted and no
    /// passphrase is enrolled at all. An SSH private key still requires one: the SFTP connector
    /// rejects an unprotected key outright, so storing one would store something unusable.
    /// </summary>
    internal static string? DescribeMissingPassphrase(KeyStoreMaterialKind kind, string? value) =>
        !string.IsNullOrEmpty(value) || kind is KeyStoreMaterialKind.Pkcs12Certificate
            ? null
            : Ui.KeyStore.StorageHubCannotStoreAnUnprotectedPrivateKey;

    internal static string DescribeFailure(KeyStoreWriteResponse response) => response.Outcome switch
    {
        KeyStoreWriteOutcome.NameConflict => Ui.KeyStore.AnotherEntryAlreadyUsesThatName,
        KeyStoreWriteOutcome.VersionConflict =>
            Ui.KeyStore.TheEntryChangedElsewhereReopenTheKey,
        KeyStoreWriteOutcome.NotFound => Ui.KeyStore.TheEntryNoLongerExists,
        KeyStoreWriteOutcome.StillReferenced => response.ReferencedByProfiles is { Length: > 0 } names
            ? $"The entry is still used by {string.Join(", ", names)}."
            : Ui.KeyStore.TheEntryIsStillUsedByA,
        _ => response.Failure?.Message ?? Ui.KeyStore.TheKeyStoreRejectedTheRequest
    };

    private KeyStoreEntryDocument? Selected() => _entries.SelectedItems.Count == 1
        ? _entries.SelectedItems[0].Tag as KeyStoreEntryDocument
        : null;

    private void UpdateActionState()
    {
        var hasSelection = Selected() is not null;
        _rename.Enabled = hasSelection && !_busy;
        _delete.Enabled = hasSelection && !_busy;
        _importCertificate.Enabled = !_busy;
        _importKey.Enabled = !_busy;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UseWaitCursor = busy;
        UpdateActionState();
    }

    private void ShowStatus(string message, Color color)
    {
        _status.Text = message;
        _status.ForeColor = color;
    }

    private static bool IsExpected(Exception error) => error is
        IOException or UnauthorizedAccessException or InvalidDataException or
        InvalidOperationException or TimeoutException or OperationCanceledException or
        System.Text.Json.JsonException or ArgumentException;
}

/// <summary>Collects a passphrase without echoing it or keeping it in a control's history.</summary>
internal sealed class SecretPromptForm : Form
{
    private readonly StorageHubTextField _value;

    public SecretPromptForm(string caption)
    {
        Text = caption;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = this.LogicalWindowSize(new Size(400, 130));
        BackColor = StorageHubTheme.Canvas;
        StorageHubTheme.Register(this);
        ForeColor = StorageHubTheme.Text;

        var label = new Label
        {
            Text = caption,
            Location = this.LogicalToDeviceUnits(new Point(14, 16)),
            AutoSize = true,
            ForeColor = StorageHubTheme.Text
        };
        _value = new StorageHubTextField
        {
            Location = this.LogicalToDeviceUnits(new Point(14, 42)),
            Width = LogicalToDeviceUnits(370),
            UseSystemPasswordChar = true,
            AccessibleName = caption
        };
        var ok = new StorageHubButton
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = this.LogicalToDeviceUnits(new Point(214, 82)),
            Size = LogicalToDeviceUnits(new Size(84, 30))
        };
        var cancel = new StorageHubButton
        {
            Text = Ui.KeyStore.Cancel,
            DialogResult = DialogResult.Cancel,
            Location = this.LogicalToDeviceUnits(new Point(300, 82)),
            Size = LogicalToDeviceUnits(new Size(84, 30))
        };
        ok.Variant = StorageHubButtonVariant.Primary;
        cancel.Variant = StorageHubButtonVariant.Secondary;
        Controls.AddRange([label, _value, ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string Value => _value.Text;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _value.Clear();
        }

        base.Dispose(disposing);
    }
}

internal sealed class TextPromptForm : Form
{
    private readonly StorageHubTextField _value;

    public TextPromptForm(string caption, string initial)
    {
        Text = caption;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = this.LogicalWindowSize(new Size(400, 130));
        BackColor = StorageHubTheme.Canvas;
        StorageHubTheme.Register(this);
        ForeColor = StorageHubTheme.Text;

        var label = new Label
        {
            Text = caption,
            Location = this.LogicalToDeviceUnits(new Point(14, 16)),
            AutoSize = true,
            ForeColor = StorageHubTheme.Text
        };
        _value = new StorageHubTextField
        {
            Location = this.LogicalToDeviceUnits(new Point(14, 42)),
            Width = LogicalToDeviceUnits(370),
            Text = initial,
            MaxLength = KeyStoreIpcLimits.MaximumDisplayNameLength,
            AccessibleName = caption
        };
        var ok = new StorageHubButton
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = this.LogicalToDeviceUnits(new Point(214, 82)),
            Size = LogicalToDeviceUnits(new Size(84, 30))
        };
        var cancel = new StorageHubButton
        {
            Text = Ui.KeyStore.Cancel,
            DialogResult = DialogResult.Cancel,
            Location = this.LogicalToDeviceUnits(new Point(300, 82)),
            Size = LogicalToDeviceUnits(new Size(84, 30))
        };
        ok.Variant = StorageHubButtonVariant.Primary;
        cancel.Variant = StorageHubButtonVariant.Secondary;
        Controls.AddRange([label, _value, ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string Value => _value.Text.Trim();
}

/// <summary>
/// Asks which envelope an SSH private key uses. StorageHub cannot infer it safely, and the agent
/// validates the declared format against the material before the entry is created.
/// </summary>
internal sealed class KeyFormatPromptForm : Form
{
    private readonly StorageHubChoiceField _format;

    public KeyFormatPromptForm()
    {
        Text = Ui.KeyStore.PrivateKeyFormatLabel;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = this.LogicalWindowSize(new Size(400, 130));
        BackColor = StorageHubTheme.Canvas;
        StorageHubTheme.Register(this);
        ForeColor = StorageHubTheme.Text;

        var label = new Label
        {
            Text = Ui.KeyStore.WhichEnvelopeDoesTheKeyUse,
            Location = this.LogicalToDeviceUnits(new Point(14, 16)),
            AutoSize = true,
            ForeColor = StorageHubTheme.Text
        };
        _format = new StorageHubChoiceField
        {
            Location = this.LogicalToDeviceUnits(new Point(14, 42)),
            Width = LogicalToDeviceUnits(370),
            AccessibleName = Ui.KeyStore.PrivateKeyFormatLabel
        };
        _format.Items.AddRange([Ui.KeyStore.OpenSSHOpensshKeyV1, Ui.KeyStore.LegacyPEM, Ui.KeyStore.PKCS8]);
        _format.SelectedIndex = 0;
        var ok = new StorageHubButton
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = this.LogicalToDeviceUnits(new Point(214, 82)),
            Size = LogicalToDeviceUnits(new Size(84, 30))
        };
        var cancel = new StorageHubButton
        {
            Text = Ui.KeyStore.Cancel,
            DialogResult = DialogResult.Cancel,
            Location = this.LogicalToDeviceUnits(new Point(300, 82)),
            Size = LogicalToDeviceUnits(new Size(84, 30))
        };
        ok.Variant = StorageHubButtonVariant.Primary;
        cancel.Variant = StorageHubButtonVariant.Secondary;
        Controls.AddRange([label, _format, ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public KeyStorePrivateKeyFormat SelectedFormat => _format.SelectedIndex switch
    {
        1 => KeyStorePrivateKeyFormat.Pem,
        2 => KeyStorePrivateKeyFormat.Pkcs8,
        _ => KeyStorePrivateKeyFormat.OpenSsh
    };
}
