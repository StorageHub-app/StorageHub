using StorageHub.Desktop.Localization;
namespace StorageHub.Desktop;

/// <summary>
/// Chooses what goes into an export file and where it is written.
///
/// The section list is built from <see cref="SettingsSectionCatalog"/>, so this dialog and the
/// import wizard cannot offer different things. What can never be exported is shown here too,
/// greyed with its reason: a silent omission would be a surprise the first time somebody moved to
/// a new machine and found their connections could not open.
/// </summary>
public sealed class SettingsExportForm : Form
{
    private readonly SettingsExportService _exporter;
    private readonly Dictionary<SettingsSectionId, CheckBox> _sections = [];
    private readonly CheckBox _protect;
    private readonly StorageHubTextField _password;
    private readonly StorageHubTextField _confirm;
    private readonly Label _passwordHint;
    private readonly Label _status;
    private readonly StorageHubButton _export;
    private bool _updatingSections;

    internal SettingsExportForm(SettingsExportService exporter)
    {
        _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));

        Text = Ui.SettingsTransfer.ExportSettings;
        AccessibleName = Ui.SettingsTransfer.ExportSettings2;
        AccessibleDescription = Ui.SettingsTransfer.ChooseWhichSettingsToWriteToA;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = this.LogicalWindowSize(new Size(660, 640));
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = StorageHubTheme.Canvas;
        ForeColor = StorageHubTheme.Text;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        StorageHubTheme.Register(this);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoScroll = true,
            Padding = this.LogicalToDeviceUnits(new Padding(18, 16, 18, 8))
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        content.Controls.Add(new Label
        {
            Text = Ui.SettingsTransfer.ChooseWhatToInclude,
            AutoSize = true,
            Font = StorageHubTheme.CreateSectionFont(),
            ForeColor = StorageHubTheme.Text,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 0, 0, 10))
        });

        foreach (var section in SettingsSectionCatalog.Sections)
        {
            var check = new StorageHubCheckBox
            {
                Text = section.Label,
                Checked = section.CheckedByDefault,
                AutoSize = true,
                Margin = this.LogicalToDeviceUnits(new Padding(0, 6, 0, 0)),
                AccessibleName = section.Label,
                AccessibleDescription = section.Description
            };
            check.CheckedChanged += SectionCheckedChanged;
            _sections[section.Id] = check;
            content.Controls.Add(check);
            content.Controls.Add(new Label
            {
                Text = section.Description,
                AutoSize = true,
                MaximumSize = new Size(LogicalToDeviceUnits(600), 0),
                ForeColor = StorageHubTheme.TextMuted,
                Margin = this.LogicalToDeviceUnits(new Padding(20, 0, 0, 4))
            });
        }

        content.Controls.Add(CreateExclusions());

        _protect = new StorageHubCheckBox
        {
            Text = Ui.SettingsTransfer.ProtectThisFileWithAPassword,
            AutoSize = true,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 0, 0, 4)),
            AccessibleName = Ui.SettingsTransfer.ProtectTheExportWithAPassword
        };
        _protect.CheckedChanged += (_, _) => { UpdatePasswordState(); UpdateExportState(); };
        _password = CreatePasswordBox(Ui.SettingsTransfer.ExportPassword);
        _confirm = CreatePasswordBox(Ui.SettingsTransfer.ConfirmExportPassword);
        _password.TextChanged += (_, _) => UpdateExportState();
        _confirm.TextChanged += (_, _) => UpdateExportState();
        _passwordHint = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(LogicalToDeviceUnits(600), 0),
            ForeColor = StorageHubTheme.TextMuted,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 2, 0, 0))
        };

        // Docked rather than added to the scrolling list above: whether the file is readable or
        // sealed changes what the file *is*, and it should never sit below the fold.
        var passwordGroup = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            ColumnCount = 1,
            Padding = this.LogicalToDeviceUnits(new Padding(18, 8, 18, 4)),
            BackColor = StorageHubTheme.Canvas
        };
        passwordGroup.Controls.Add(_protect);
        passwordGroup.Controls.Add(Labelled(Ui.SettingsTransfer.Password, _password));
        passwordGroup.Controls.Add(Labelled(Ui.SettingsTransfer.Confirm, _confirm));
        passwordGroup.Controls.Add(_passwordHint);
        passwordGroup.Controls.Add(new Label
        {
            Text = Ui.SettingsTransfer.WithoutAPasswordTheFileIsReadable +
                Ui.SettingsTransfer.ALostPasswordCannotBeRecovered,
            AutoSize = true,
            MaximumSize = new Size(LogicalToDeviceUnits(600), 0),
            ForeColor = StorageHubTheme.TextMuted,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 4, 0, 0))
        });

        _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = this.TextBoxHeight(3),
            ForeColor = StorageHubTheme.TextMuted,
            Padding = this.LogicalToDeviceUnits(new Padding(18, 0, 18, 0)),
            AccessibleName = Ui.SettingsTransfer.ExportStatus
        };

        _export = new StorageHubButton { Text = Ui.SettingsTransfer.Export, AutoSize = true, Margin = this.LogicalToDeviceUnits(new Padding(8, 0, 0, 0)) };
        _export.Click += async (_, _) => await ExportClickedAsync().ConfigureAwait(true);
        _export.Variant = StorageHubButtonVariant.Primary;
        var cancel = new StorageHubButton
        {
            Text = Ui.SettingsTransfer.Cancel,
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
            Margin = this.LogicalToDeviceUnits(new Padding(8, 0, 0, 0))
        };
        cancel.Variant = StorageHubButtonVariant.Secondary;
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = this.TextBoxHeight(29),
            Padding = this.LogicalToDeviceUnits(new Padding(16, 12, 16, 10))
        };
        buttons.Controls.Add(_export);
        buttons.Controls.Add(cancel);

        Controls.Add(content);
        Controls.Add(passwordGroup);
        Controls.Add(_status);
        Controls.Add(buttons);
        CancelButton = cancel;

        UpdatePasswordState();
        UpdateExportState();
    }

    /// <summary>The file that was written, or null when the dialog was cancelled.</summary>
    internal string? ExportedPath { get; private set; }

    internal IReadOnlyCollection<SettingsSectionId> SelectedSections =>
        [.. _sections.Where(pair => pair.Value.Checked).Select(pair => pair.Key)];

    /// <summary>
    /// Keeps the ticks consistent with what a file can actually describe: choosing schedules also
    /// takes their sync tasks and connections, and clearing connections clears what depended on
    /// them. Doing this as the user clicks avoids an export that silently contains more, or less,
    /// than the boxes showed.
    /// </summary>
    private void SectionCheckedChanged(object? sender, EventArgs e)
    {
        if (_updatingSections) return;
        _updatingSections = true;
        try
        {
            var selected = sender is CheckBox { Checked: true }
                ? SettingsSectionCatalog.ExpandForExport(SelectedSections)
                : SettingsSectionCatalog.CollapseForExport(SelectedSections);
            foreach (var (id, check) in _sections)
            {
                check.Checked = selected.Contains(id);
            }
        }
        finally
        {
            _updatingSections = false;
        }

        UpdateExportState();
    }

    private void UpdatePasswordState()
    {
        _password.Enabled = _protect.Checked;
        _confirm.Enabled = _protect.Checked;
        if (!_protect.Checked)
        {
            _password.Clear();
            _confirm.Clear();
        }
    }

    private void UpdateExportState()
    {
        var sections = SelectedSections.Count > 0;
        var passwordProblem = _protect.Checked ? DescribePasswordProblem() : null;
        _passwordHint.Text = passwordProblem ?? string.Empty;
        _passwordHint.ForeColor = passwordProblem is null
            ? StorageHubTheme.TextMuted
            : StorageHubTheme.Warning;
        _export.Enabled = sections && passwordProblem is null;
        _status.Text = sections
            ? string.Empty
            : Ui.SettingsTransfer.ChooseAtLeastOneThingToExport;
    }

    private string? DescribePasswordProblem() =>
        Security.SettingsExportEnvelope.ValidatePassword(_password.Text) ??
        (string.Equals(_password.Text, _confirm.Text, StringComparison.Ordinal)
            ? null
            : Ui.SettingsTransfer.TheTwoPasswordsDoNotMatch);

    private async Task ExportClickedAsync()
    {
        using var dialog = new SaveFileDialog
        {
            Title = Ui.SettingsTransfer.ExportSettings,
            Filter = SettingsExportSerializer.FileFilter,
            DefaultExt = SettingsExportSerializer.FileExtension.TrimStart('.'),
            AddExtension = true,
            FileName = $"storagehub-settings-{DateTime.Now:yyyyMMdd}{SettingsExportSerializer.FileExtension}"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _export.Enabled = false;
        _status.Text = Ui.SettingsTransfer.CollectingSettings;
        try
        {
            // Async because connections, sync tasks and schedules are read from the agent over a
            // pipe, one round trip per connection.
            var document = await _exporter.CaptureAsync(SelectedSections).ConfigureAwait(true);
            SettingsExportService.Write(
                dialog.FileName,
                document,
                _protect.Checked ? _password.Text : null);
            ExportedPath = dialog.FileName;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            _ = MessageBox.Show(
                this,
                Ui.Format(Ui.Dialogs.ExportWriteFailedFormat, error.Message),
                Ui.Dialogs.ExportSettingsCaption,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch (Exception error) when (error is InvalidOperationException or TimeoutException)
        {
            // The agent went away between ticking the boxes and pressing Export.
            _ = MessageBox.Show(
                this,
                Ui.Format(Ui.Dialogs.ExportReadFailedFormat, error.Message),
                Ui.Dialogs.ExportSettingsCaption,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _export.Enabled = true;
            _status.Text = string.Empty;
            UpdateExportState();
        }
    }

    private TableLayoutPanel CreateExclusions()
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 14, 0, 0))
        };
        panel.Controls.Add(new Label
        {
            Text = Ui.SettingsTransfer.NeverIncluded,
            AutoSize = true,
            Font = StorageHubTheme.CreateSectionFont(),
            ForeColor = StorageHubTheme.Text,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 0, 0, 4))
        });
        foreach (var (title, reason) in new[]
        {
            (Ui.SettingsTransfer.SavedPasswordsAndPrivateKeys,
                Ui.SettingsTransfer.TheyAreHeldForThisWindowsAccount),
            (Ui.SettingsTransfer.KeyStoreEntries,
                Ui.SettingsTransfer.EachEntryIsDerivedFromItsKey),
            (Ui.SettingsTransfer.HostTrustDecisions,
                Ui.SettingsTransfer.TheseRecordWhatYouVerifiedOnThis)
        })
        {
            panel.Controls.Add(new Label
            {
                Text = $"{title} — {reason}",
                AutoSize = true,
                MaximumSize = new Size(LogicalToDeviceUnits(600), 0),
                ForeColor = StorageHubTheme.TextMuted,
                Margin = this.LogicalToDeviceUnits(new Padding(0, 0, 0, 2))
            });
        }

        return panel;
    }

    private StorageHubTextField CreatePasswordBox(string accessibleName) => new()
    {
        Width = LogicalToDeviceUnits(280),
        UseSystemPasswordChar = true,
        MaxLength = Security.SettingsExportEnvelope.MaximumPasswordLength,
        AccessibleName = accessibleName
    };

    private TableLayoutPanel Labelled(string text, Control control)
    {
        var row = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Margin = this.LogicalToDeviceUnits(new Padding(20, 4, 0, 0))
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(80)));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = StorageHubTheme.Text,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 4, 0, 0))
        });
        row.Controls.Add(control);
        return row;
    }
}
