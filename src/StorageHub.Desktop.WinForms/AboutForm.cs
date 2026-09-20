using System.ComponentModel;
using System.Diagnostics;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// Help &gt; About: what this build is, and where the project lives.
/// </summary>
/// <remarks>
/// A window rather than the message box this used to be, for one reason: a message box cannot
/// carry a link, and the address of the project is the thing somebody opening About is most
/// likely to want. The address is not written here -- it is the same constant the updater checks
/// for new releases, so the application has exactly one idea of where it comes from.
/// </remarks>
internal sealed class AboutForm : Form
{
    private readonly Bitmap _icon;

    private AboutForm()
    {
        Text = Ui.Dialogs.AboutCaption;
        AccessibleName = Ui.Dialogs.AboutCaption;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = this.LogicalWindowSize(new Size(500, 236));
        BackColor = StorageHubTheme.Canvas;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        StorageHubTheme.Register(this);

        _icon = ApplicationBadge();
        var badge = new PictureBox
        {
            Image = _icon,
            SizeMode = PictureBoxSizeMode.CenterImage,
            Size = new Size(LogicalToDeviceUnits(56), LogicalToDeviceUnits(56)),
            Margin = new Padding(0, 0, LogicalToDeviceUnits(14), 0),
            AccessibleName = Ui.Dialogs.AboutCaption
        };

        var body = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(LogicalToDeviceUnits(370), 0),
            Text = Ui.Format(Ui.Dialogs.AboutBodyFormat, DesktopApplicationVersion.Current),
            ForeColor = StorageHubTheme.Text
        };

        var projectLink = new LinkLabel
        {
            AutoSize = true,
            Margin = new Padding(0, LogicalToDeviceUnits(10), 0, 0),
            Text = Ui.Dialogs.AboutProjectLink,
            LinkColor = StorageHubTheme.Primary,
            ActiveLinkColor = StorageHubTheme.PrimaryPressed,
            VisitedLinkColor = StorageHubTheme.Primary,
            AccessibleName = Ui.Dialogs.AboutProjectLink,
            AccessibleDescription = ProjectUrl
        };
        projectLink.LinkClicked += (_, _) => OpenProject();

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(
                LogicalToDeviceUnits(20),
                LogicalToDeviceUnits(20),
                LogicalToDeviceUnits(20),
                LogicalToDeviceUnits(8)),
            BackColor = StorageHubTheme.Surface
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        content.Controls.Add(badge, 0, 0);
        content.Controls.Add(body, 1, 0);
        content.Controls.Add(projectLink, 1, 1);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = this.TextBoxHeight(34),
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(
                LogicalToDeviceUnits(12),
                LogicalToDeviceUnits(12),
                LogicalToDeviceUnits(12),
                LogicalToDeviceUnits(8)),
            BackColor = StorageHubTheme.SurfaceMuted
        };
        var close = new StorageHubButton
        {
            Text = Ui.Dialogs.ButtonClose,
            DialogResult = DialogResult.OK,
            Variant = StorageHubButtonVariant.Primary
        };
        footer.Controls.Add(close);

        Controls.Add(content);
        Controls.Add(footer);
        AcceptButton = close;
        CancelButton = close;
        StorageHubTheme.Apply(this);
    }

    /// <summary>
    /// The application's own icon, drawn from the running executable, or the shell's storage
    /// glyph when there is no executable to read one from -- under a test host, or a build run
    /// through a launcher that is not StorageHub.
    /// </summary>
    private Bitmap ApplicationBadge()
    {
        var size = LogicalToDeviceUnits(48);
        try
        {
            if (Environment.ProcessPath is { Length: > 0 } path
                && Icon.ExtractAssociatedIcon(path) is { } associated)
            {
                using (associated)
                {
                    using var sized = new Icon(associated, new Size(size, size));
                    return sized.ToBitmap();
                }
            }
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // Fall through to the drawn glyph below.
        }

        return UiIconFactory.Create(UiGlyph.Connections, StorageHubTheme.Primary, size, DeviceDpi / 96F);
    }

    /// <summary>
    /// Where the project lives. The same address the updater trusts for releases, so About cannot
    /// come to disagree with the place new versions are fetched from.
    /// </summary>
    internal static string ProjectUrl => StorageHubLinks.Project;

    internal static void ShowFor(IWin32Window owner)
    {
        using var dialog = new AboutForm();
        _ = dialog.ShowDialog(owner);
    }

    private void OpenProject()
    {
        try
        {
            using var browser = Process.Start(new ProcessStartInfo(ProjectUrl) { UseShellExecute = true });
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            // No browser, or the shell refused to start one. The address itself is still the
            // useful thing, so it is shown rather than swallowed.
            _ = MessageBox.Show(
                this, ProjectUrl, Ui.Dialogs.AboutCaption, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _icon.Dispose();
        }

        base.Dispose(disposing);
    }
}
