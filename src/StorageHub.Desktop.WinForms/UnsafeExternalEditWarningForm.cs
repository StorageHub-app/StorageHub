using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

internal readonly record struct UnsafeExternalEditDecision(bool Continue, bool DontShowAgain);

internal sealed class UnsafeExternalEditWarningForm : Form
{
    private readonly CheckBox _dontShowAgain;
    private readonly Bitmap _warningImage;

    private UnsafeExternalEditWarningForm(string fileName)
    {
        Text = Ui.Dialogs.UnsafeExternalEditCaption;
        AccessibleName = Ui.Dialogs.UnsafeExternalEditAccessibleName;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 260);
        BackColor = StorageHubTheme.Canvas;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        StorageHubTheme.Register(this);

        _warningImage = SystemIcons.Warning.ToBitmap();
        var icon = new PictureBox
        {
            Image = _warningImage,
            SizeMode = PictureBoxSizeMode.CenterImage,
            Size = new Size(54, 54),
            Margin = new Padding(0, 2, 14, 0),
            AccessibleName = Ui.Dialogs.WarningAccessibleName
        };
        var message = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            Text = Ui.Format(Ui.Dialogs.UnsafeExternalEditBodyFormat, fileName),
            ForeColor = StorageHubTheme.Text
        };
        _dontShowAgain = new StorageHubCheckBox
        {
            AutoSize = true,
            Text = Ui.Dialogs.DontShowWarningAgain,
            AccessibleDescription = Ui.Dialogs.RestoreWarningInSettingsHint
        };

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(20, 20, 20, 8),
            BackColor = StorageHubTheme.Surface
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        content.Controls.Add(icon, 0, 0);
        content.Controls.Add(message, 1, 0);
        content.Controls.Add(_dontShowAgain, 1, 1);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 62,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(12, 12, 12, 8),
            BackColor = StorageHubTheme.SurfaceMuted
        };
        var cancel = new StorageHubButton { Text = Ui.Dialogs.ButtonCancel, DialogResult = DialogResult.Cancel };
        cancel.Variant = StorageHubButtonVariant.Secondary;
        var continueButton = new StorageHubButton { Text = Ui.Dialogs.ButtonContinueAnyway, DialogResult = DialogResult.OK };
        continueButton.Variant = StorageHubButtonVariant.Primary;
        footer.Controls.Add(continueButton);
        footer.Controls.Add(cancel);

        Controls.Add(content);
        Controls.Add(footer);
        AcceptButton = continueButton;
        CancelButton = cancel;
        StorageHubTheme.Apply(this);
    }

    internal static UnsafeExternalEditDecision Ask(IWin32Window owner, string fileName)
    {
        using var dialog = new UnsafeExternalEditWarningForm(fileName);
        var result = dialog.ShowDialog(owner);
        return new UnsafeExternalEditDecision(
            result == DialogResult.OK,
            result == DialogResult.OK && dialog._dontShowAgain.Checked);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _warningImage.Dispose();
        }

        base.Dispose(disposing);
    }
}
