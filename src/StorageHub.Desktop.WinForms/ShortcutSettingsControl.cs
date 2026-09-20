using Avalonia.Input;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

internal sealed class ShortcutSettingsControl : UserControl
{
    private readonly DataGridView _commands;
    private readonly ShortcutCaptureBox _capture;
    private readonly Label _message;
    private Dictionary<string, KeyGesture?> _shortcuts;

    internal ShortcutSettingsControl(IReadOnlyDictionary<string, KeyGesture?>? shortcuts)
    {
        _shortcuts = ShortcutSettings.Resolve(shortcuts);
        Height = LogicalToDeviceUnits(460);
        Width = LogicalToDeviceUnits(700);
        Margin = Padding.Empty;
        AccessibleName = Ui.Settings.ShortcutsAccessibleName;
        _commands = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AccessibleName = Ui.Settings.ShortcutsGridAccessibleName, BackgroundColor = StorageHubTheme.Surface
        };
        _commands.Columns.Add("Command", Ui.Settings.ShortcutColumnCommand);
        _commands.Columns.Add("Shortcut", Ui.Settings.ShortcutColumnShortcut);
        _commands.Columns.Add("Default", Ui.Settings.ShortcutColumnDefault);
        foreach (var command in ShortcutSettings.Commands)
        {
            var row = _commands.Rows[_commands.Rows.Add(
                Ui.Format(Ui.Settings.ShortcutCommandLabelFormat, command.Menu, command.Label),
                ShortcutSettings.Format(_shortcuts[command.Id]),
                ShortcutSettings.Format(command.Shortcut))];
            row.Tag = command.Id;
        }
        _capture = new ShortcutCaptureBox { Width = 180, AccessibleName = Ui.Settings.ShortcutCaptureAccessibleName, PlaceholderText = Ui.Settings.ShortcutCapturePlaceholder };
        var assign = new StorageHubButton { Text = Ui.Settings.ShortcutAssign, AutoSize = true };
        var clear = new StorageHubButton { Text = Ui.Settings.ShortcutClear, AutoSize = true };
        var reset = new StorageHubButton { Text = Ui.Settings.ShortcutRestoreDefaults, AutoSize = true };
        foreach (var button in new[] { assign, clear, reset }) button.Variant = StorageHubButtonVariant.Secondary;
        // The capture box reads a Keys, because that is what a WinForms key event carries.
        assign.Click += (_, _) => SetSelected(ShortcutKeys.ToGesture(_capture.CapturedKeys));
        clear.Click += (_, _) => SetSelected(null);
        reset.Click += (_, _) => { _shortcuts = ShortcutSettings.Resolve(null); RefreshRows(); Changed?.Invoke(this, EventArgs.Empty); };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, Padding = this.LogicalToDeviceUnits(new Padding(0, 8, 0, 0)) };
        actions.Controls.AddRange([_capture, assign, clear, reset]);
        _message = new Label { Dock = DockStyle.Bottom, Height = 48, AutoEllipsis = true, AccessibleName = Ui.Settings.ShortcutStatusAccessibleName };
        _commands.SelectionChanged += (_, _) => { _capture.Reset(); _message.Text = Ui.Settings.ShortcutSelectCommandHint; };
        Controls.Add(_commands);
        Controls.Add(actions);
        Controls.Add(_message);
    }

    internal event EventHandler? Changed;
    internal Dictionary<string, KeyGesture?> ReadShortcuts() => new(_shortcuts, StringComparer.Ordinal);

    private void SetSelected(KeyGesture? gesture)
    {
        if (_commands.CurrentRow?.Tag is not string id) return;
        var candidate = new Dictionary<string, KeyGesture?>(_shortcuts, StringComparer.Ordinal) { [id] = gesture };
        if (ShortcutSettings.Validate(candidate) is { } error) { _message.Text = error; return; }
        _shortcuts = candidate;
        RefreshRows();
        _message.Text = Ui.Settings.ShortcutUpdatedHint;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshRows()
    {
        foreach (DataGridViewRow row in _commands.Rows)
            if (row.Tag is string id) row.Cells[1].Value = ShortcutSettings.Format(_shortcuts[id]);
    }

    private sealed class ShortcutCaptureBox : TextBox
    {
        internal Keys CapturedKeys { get; private set; }
        internal ShortcutCaptureBox() { ReadOnly = true; }
        internal void Reset() { CapturedKeys = Keys.None; Text = string.Empty; }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData is Keys.Tab or (Keys.Shift | Keys.Tab) or Keys.Escape) return base.ProcessCmdKey(ref msg, keyData);
            CapturedKeys = keyData;
            Text = ShortcutSettings.Format(ShortcutKeys.ToGesture(keyData));
            return true;
        }
    }
}
