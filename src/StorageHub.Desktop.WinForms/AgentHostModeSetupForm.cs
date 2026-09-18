using StorageHub.Agent;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// The one-time choice of how StorageHub runs, shown on the first launch of a build that offers a
/// service.
///
/// A message box was the obvious thing and the wrong one: this decision moves where the database
/// lives and which key protects the vault, and the two options are not "yes" and "no" -- they are
/// two ways of running with different consequences. Both are therefore laid out with what each
/// actually costs, and the safe one is preselected.
/// </summary>
internal sealed class AgentHostModeSetupForm : Form
{
    private readonly RadioButton _userSession = new();
    private readonly RadioButton _appSession = new();
    private readonly RadioButton _service = new();

    internal AgentHostModeSetupForm()
    {
        Text = Ui.Settings.AgentModeStartupQuestionTitle;
        AccessibleName = Ui.Settings.AgentModeStartupQuestionTitle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        BackColor = StorageHubTheme.Surface;
        ForeColor = StorageHubTheme.Text;
        StorageHubTheme.Register(this);
        // Tall enough that the machine-wide-secrets warning is visible without scrolling:
        // a caution the reader has to find is not a caution.
        ClientSize = new Size(580, 560);
        Padding = new Padding(20);

        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true
        };

        var heading = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(510, 0),
            Text = Ui.Settings.AgentModeStartupIntro,
            Font = StorageHubTheme.CreateSectionFont(),
            Margin = new Padding(0, 0, 0, 12)
        };

        _userSession.Name = "AgentModeUserSession";
        _userSession.AutoSize = true;
        _userSession.MaximumSize = new Size(510, 0);
        _userSession.Text = Ui.Settings.AgentModeUserSession;
        _userSession.AccessibleName = Ui.Settings.AgentModeUserSession;
        // Preselected: it changes nothing, needs no elevation, and keeps the vault readable only
        // by this account. Choosing the service should be a deliberate act.
        _userSession.Checked = true;

        _appSession.Name = "AgentModeAppSession";
        _appSession.AutoSize = true;
        _appSession.MaximumSize = new Size(510, 0);
        _appSession.Text = Ui.Settings.AgentModeAppSession;
        _appSession.AccessibleName = Ui.Settings.AgentModeAppSession;

        _service.Name = "AgentModeService";
        _service.AutoSize = true;
        _service.MaximumSize = new Size(510, 0);
        _service.Text = Ui.Settings.AgentModeService;
        _service.AccessibleName = Ui.Settings.AgentModeService;

        layout.Controls.Add(heading);
        layout.Controls.Add(_userSession);
        layout.Controls.Add(Describe(Ui.Settings.AgentModeUserSessionDetail));
        layout.Controls.Add(_appSession);
        layout.Controls.Add(Describe(Ui.Settings.AgentModeAppSessionDetail));
        layout.Controls.Add(_service);
        layout.Controls.Add(Describe(Ui.Settings.AgentModeServiceDetail));
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(510, 0),
            Text = Ui.Settings.AgentModeServiceWarning,
            ForeColor = StorageHubTheme.Warning,
            Margin = new Padding(24, 4, 0, 12)
        });
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(510, 0),
            Text = Ui.Settings.AgentModeChangeLaterHint,
            ForeColor = StorageHubTheme.TextMuted
        });

        var confirm = new StorageHubButton { Text = Ui.Settings.AgentModeContinue, Width = 120 };
        confirm.Click += (_, _) => DialogResult = DialogResult.OK;
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(0, 10, 0, 0)
        };
        buttons.Controls.Add(confirm);

        Controls.Add(layout);
        Controls.Add(buttons);
        AcceptButton = confirm;
    }

    /// <summary>The chosen mode.</summary>
    internal AgentHostMode SelectedMode => _service.Checked
        ? AgentHostMode.WindowsService
        : _appSession.Checked
            ? AgentHostMode.AppSession
            : AgentHostMode.UserSession;

    private static Label Describe(string text) => new()
    {
        AutoSize = true,
        MaximumSize = new Size(510, 0),
        Text = text,
        ForeColor = StorageHubTheme.TextMuted,
        Margin = new Padding(24, 0, 0, 10)
    };
}
