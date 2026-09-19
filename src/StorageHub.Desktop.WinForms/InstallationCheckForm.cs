using StorageHub.Agent;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// Shows what the installation check found, and offers the repairs it named.
///
/// It exists because the state that decides whether StorageHub works is spread over a per-user
/// data root, a machine data root, a staged binary tree, a service registration and a named pipe.
/// When one of those is wrong the only symptom is the desktop reporting that the agent did not
/// become ready, which points at the wrong thing entirely. This says which of the five it is.
/// </summary>
public sealed class InstallationCheckForm : Form
{
    private readonly AgentHostMode _mode;
    private readonly Func<InstallationReport> _inspect;
    private readonly Func<InstallationRepair, InstallationRepairResult> _repair;
    private readonly FlowLayoutPanel _findings;
    private readonly Label _summary;
    private readonly Font _titleFont = StorageHubTheme.CreateSectionFont();

    public InstallationCheckForm(AgentHostMode mode)
        : this(
            mode,
            () => AgentInstallationCheck.Inspect(
                mode,
                DesktopApplicationVersion.Current,
                "StorageHub.Agent.Windows.exe",
                new WindowsInstallationProbe()),
            repair => AgentInstallationRepair.Apply(repair, mode))
    {
    }

    /// <summary>Test seam: the check and the repairs are supplied rather than reached for.</summary>
    internal InstallationCheckForm(
        AgentHostMode mode,
        Func<InstallationReport> inspect,
        Func<InstallationRepair, InstallationRepairResult> repair)
    {
        _mode = mode;
        _inspect = inspect ?? throw new ArgumentNullException(nameof(inspect));
        _repair = repair ?? throw new ArgumentNullException(nameof(repair));

        Text = "Check installation — StorageHub";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = this.LogicalWindowSize(new Size(560, 420));
        ClientSize = this.LogicalWindowSize(new Size(680, 560));
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = StorageHubTheme.Canvas;
        ForeColor = StorageHubTheme.Text;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        StorageHubTheme.Register(this);

        _summary = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = this.TextBoxHeight(14),
            Padding = this.LogicalToDeviceUnits(new Padding(16, 10, 16, 4)),
            Font = _titleFont,
            ForeColor = StorageHubTheme.Text
        };

        _findings = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = this.LogicalToDeviceUnits(new Padding(12, 4, 12, 12)),
            BackColor = StorageHubTheme.Canvas
        };

        var recheck = new StorageHubButton { Text = "Check again", AutoSize = true };
        recheck.Variant = StorageHubButtonVariant.Secondary;
        recheck.Click += (_, _) => Render();
        var close = new StorageHubButton
        {
            Text = Ui.Dialogs.ButtonClose,
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 0, 8, 0))
        };
        close.Variant = StorageHubButtonVariant.Primary;

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = this.TextBoxHeight(26),
            FlowDirection = FlowDirection.RightToLeft,
            Padding = this.LogicalToDeviceUnits(new Padding(12, 8, 12, 8)),
            BackColor = StorageHubTheme.Surface
        };
        actions.Controls.Add(close);
        actions.Controls.Add(recheck);

        Controls.Add(_findings);
        Controls.Add(_summary);
        Controls.Add(actions);
        CancelButton = close;
        Render();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // The rows wrap their detail text against the panel width, which is not known until the
        // form has been laid out. Re-measuring here is what keeps a long sentence from clipping.
        BeginInvoke(ResizeRows);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ResizeRows();
    }

    private void Render()
    {
        InstallationReport report;
        try
        {
            report = _inspect();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _summary.Text = "The check could not run.";
            _summary.ForeColor = StorageHubTheme.Danger;
            _findings.Controls.Clear();
            _findings.Controls.Add(CreateRow(new InstallationFinding("Check failed", InstallationCheckStatus.Problem, error.Message)));
            return;
        }

        _summary.Text = report.Worst switch
        {
            InstallationCheckStatus.Ok => $"Everything checked out. Agent mode: {Describe(_mode)}.",
            InstallationCheckStatus.Warning => $"Works, with things worth fixing. Agent mode: {Describe(_mode)}.",
            _ => $"Something is wrong. Agent mode: {Describe(_mode)}.",
        };
        _summary.ForeColor = ToneOf(report.Worst);

        _findings.SuspendLayout();
        foreach (Control existing in _findings.Controls)
        {
            existing.Dispose();
        }

        _findings.Controls.Clear();
        foreach (var finding in report.Findings)
        {
            _findings.Controls.Add(CreateRow(finding));
        }

        _findings.ResumeLayout();
        ResizeRows();
    }

    private UiCard CreateRow(InstallationFinding finding)
    {
        var card = new UiCard
        {
            BackColor = StorageHubTheme.Surface,
            Accent = ToneOf(finding.Status),
            Margin = this.LogicalToDeviceUnits(new Padding(0, 0, 0, 8)),
            Padding = this.LogicalToDeviceUnits(new Padding(10, 8, 10, 10))
        };

        var title = new Label
        {
            Text = finding.Title,
            AutoSize = true,
            Font = _titleFont,
            ForeColor = StorageHubTheme.Text,
            Location = this.LogicalToDeviceUnits(new Point(12, 8))
        };

        var detail = new Label
        {
            Text = finding.Detail,
            AutoSize = true,
            MaximumSize = new Size(LogicalToDeviceUnits(420), 0),
            ForeColor = StorageHubTheme.TextMuted,
            Location = new Point(title.Left, title.Bottom + LogicalToDeviceUnits(2))
        };

        card.Controls.Add(title);
        card.Controls.Add(detail);

        if (!string.IsNullOrWhiteSpace(finding.Location))
        {
            var where = new Label
            {
                Text = finding.Location,
                AutoSize = true,
                ForeColor = StorageHubTheme.TextMuted,
                Location = new Point(title.Left, detail.Bottom + LogicalToDeviceUnits(2))
            };
            card.Controls.Add(where);
        }

        if (finding.Repair != InstallationRepair.None)
        {
            var apply = new StorageHubButton
            {
                Text = finding.RequiresElevation ? "Repair (needs administrator)" : "Repair",
                AutoSize = true,
                Enabled = !finding.RequiresElevation
            };
            apply.Variant = StorageHubButtonVariant.Secondary;
            apply.Click += (_, _) => ApplyRepair(finding.Repair);
            card.Controls.Add(apply);
            card.Tag = apply;
        }

        return card;
    }

    private void ApplyRepair(InstallationRepair repair)
    {
        var result = _repair(repair);
        MessageBox.Show(
            this,
            result.Message,
            result.Succeeded ? "Repaired" : "Could not repair",
            MessageBoxButtons.OK,
            result.Succeeded ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        Render();
    }

    /// <summary>
    /// Lays each card out against the current width. The detail wraps, so a card's height is not
    /// known until the width is, which is why this runs on resize rather than at construction.
    /// </summary>
    private void ResizeRows()
    {
        // OnResize fires while the base constructor assigns ClientSize, which is before the
        // fields declared below it exist. So this runs at least once with nothing to lay out.
        if (IsDisposed || _findings is not { IsDisposed: false })
        {
            return;
        }

        var width = Math.Max(
            LogicalToDeviceUnits(240),
            _findings.ClientSize.Width - _findings.Padding.Horizontal - LogicalToDeviceUnits(18));
        var inset = LogicalToDeviceUnits(12);

        foreach (Control control in _findings.Controls)
        {
            if (control is not UiCard card)
            {
                continue;
            }

            card.Width = width;
            var bottom = inset;
            foreach (Control child in card.Controls)
            {
                if (child is Label label)
                {
                    label.MaximumSize = new Size(width - (inset * 2), 0);
                }

                child.Left = inset;
                child.Top = bottom;
                bottom = child.Bottom + LogicalToDeviceUnits(3);
            }

            card.Height = bottom + inset;
        }
    }

    private static Color ToneOf(InstallationCheckStatus status) => status switch
    {
        InstallationCheckStatus.Ok => StorageHubTheme.Success,
        InstallationCheckStatus.Warning => StorageHubTheme.Warning,
        _ => StorageHubTheme.Danger,
    };

    private static string Describe(AgentHostMode mode) => mode switch
    {
        AgentHostMode.WindowsService => "Windows service",
        AgentHostMode.AppSession => "only while StorageHub is open",
        _ => "signed-in session",
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont.Dispose();
        }

        base.Dispose(disposing);
    }
}
