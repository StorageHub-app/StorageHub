using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

internal sealed class DeleteItemsConfirmationForm : Form
{
    private readonly CheckBox _doNotShowAgain;

    internal DeleteItemsConfirmationForm(IReadOnlyList<PaneTransferItem> items, bool local)
    {
        ArgumentNullException.ThrowIfNull(items);
        Text = Ui.Dialogs.ReviewDeleteCaption;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = this.LogicalWindowSize(new Size(520, 330));
        BackColor = StorageHubTheme.Surface;
        ForeColor = StorageHubTheme.Text;
        Font = new Font("Segoe UI", 9F);

        var preview = string.Join(
            Environment.NewLine,
            items.Take(6).Select(static item => Ui.Format(Ui.Dialogs.DeletePreviewItemFormat, item.Name)));
        if (items.Count > 6)
        {
            preview += Environment.NewLine + Ui.Format(Ui.Dialogs.DeletePreviewMoreFormat, items.Count - 6);
        }

        var message = new Label
        {
            Left = 24,
            Top = 22,
            Width = LogicalToDeviceUnits(470),
            Height = LogicalToDeviceUnits(190),
            Text = $"{Ui.Format(Ui.Dialogs.DeleteItemsPromptFormat, items.Count)}\n\n{preview}\n\n" +
                (local
                    ? Ui.Dialogs.DeleteLocalToRecycleBin
                    : Ui.Dialogs.DeleteRemotePermanent),
            AccessibleName = Ui.Dialogs.DeleteReviewAccessibleName
        };
        _doNotShowAgain = new StorageHubCheckBox
        {
            Left = 24,
            Top = 225,
            Width = LogicalToDeviceUnits(300),
            Text = Ui.Dialogs.DontShowWarningAgain,
            AccessibleName = Ui.Dialogs.DontShowDeleteWarningAgainAccessibleName
        };
        var delete = new StorageHubButton
        {
            Text = Ui.Dialogs.ButtonDelete,
            DialogResult = DialogResult.OK,
            Left = 326,
            Top = 270,
            Width = LogicalToDeviceUnits(82)
        };
        var cancel = new StorageHubButton
        {
            Text = Ui.Dialogs.ButtonCancel,
            DialogResult = DialogResult.Cancel,
            Left = 414,
            Top = 270,
            Width = LogicalToDeviceUnits(82)
        };
        delete.Variant = StorageHubButtonVariant.Primary;
        cancel.Variant = StorageHubButtonVariant.Secondary;
        Controls.AddRange([message, _doNotShowAgain, delete, cancel]);
        AcceptButton = delete;
        CancelButton = cancel;
        StorageHubTheme.Register(this);
        StorageHubTheme.Apply(this);
    }

    internal bool DoNotShowAgain => _doNotShowAgain.Checked;
}
