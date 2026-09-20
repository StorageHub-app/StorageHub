using System.Runtime.InteropServices;
using StorageHub.Desktop.Framework;

namespace StorageHub.Desktop;

/// <summary>
/// The window StorageHub shows while it starts.
/// </summary>
/// <remarks>
/// Startup was previously silent for as long as sixteen seconds — two eight-second agent timeouts
/// with no window on screen — which reads as a failed launch. This narrates the same work rather
/// than shortening it, and becomes the one surface that reports a startup failure, so the shell no
/// longer has to raise a message box before it owns a window.
///
/// It deliberately never reads configuration. The framework refuses a colour-mode switch once a
/// message loop is running, so the theme has to be settled before this form exists — and settings
/// are among the things it is waiting for. It follows the operating system instead, and the
/// configured appearance is applied just before the main window is built.
/// </remarks>
internal sealed class StorageHubSplashForm : Form
{
    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 1;
    private const int HtCaption = 2;

    /// <summary>How long the agent may take before the wait is worth explaining.</summary>
    private static readonly TimeSpan PatienceDelay = TimeSpan.FromSeconds(3);

    /// <summary>How wide the logo is drawn, in logical pixels.</summary>
    private const int LogoSize = 96;

    private readonly Label _status = new();
    private readonly Label _detail = new();
    private readonly ProgressBar _progress = new();
    private readonly FlowLayoutPanel _buttons = new();
    private readonly TableLayoutPanel _body = new();
    private readonly System.Windows.Forms.Timer _patience = new();
    private readonly Icon? _icon;
    private readonly Bitmap? _logo;

    private BootStage _stage;
    private string? _warning;

    internal StorageHubSplashForm()
    {
        Text = "StorageHub";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;

        // In the taskbar on purpose. A borderless window that cannot be reached with Alt+Tab is
        // how a slow start gets reported as a hang.
        ShowInTaskbar = true;
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = StorageHubTheme.Surface;
        ForeColor = StorageHubTheme.Text;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        Padding = new Padding(LogicalToDeviceUnits(20));
        AccessibleName = "StorageHub is starting";
        _icon = LoadIcon();
        if (_icon is not null)
        {
            Icon = _icon;
        }

        _logo = LoadLogo(LogicalToDeviceUnits(LogoSize));

        StorageHubTheme.Register(this);
        BuildBody();
        ResizeToContent();

        _patience.Interval = (int)PatienceDelay.TotalMilliseconds;
        _patience.Tick += PatienceElapsed;
    }

    /// <summary>Raised when the user asks to retry the step that failed.</summary>
    internal event EventHandler? RetryRequested;

    /// <summary>Raised when the user gives up on a failed start.</summary>
    internal event EventHandler? QuitRequested;

    private void BuildBody()
    {
        const int Rows = 7;
        _body.Dock = DockStyle.Fill;
        _body.ColumnCount = 1;
        _body.RowCount = Rows;
        _body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (var row = 0; row < Rows; row++)
        {
            _body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        // Centred rather than left-aligned: the window is a badge, not a form, and a centred mark
        // over centred text is what makes it read as one.
        var mark = new PictureBox
        {
            Image = _logo,
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(LogicalToDeviceUnits(LogoSize), LogicalToDeviceUnits(LogoSize)),
            Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(10)),
            Anchor = AnchorStyles.None,
            Visible = _logo is not null,
            AccessibleName = "StorageHub"
        };
        var title = new Label
        {
            Text = "StorageHub",
            AutoSize = true,
            Font = new Font("Segoe UI", 16F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = StorageHubTheme.Text,
            Margin = new Padding(0),
            Anchor = AnchorStyles.None
        };
        var version = new Label
        {
            Text = DesktopApplicationVersion.Current,
            AutoSize = true,
            ForeColor = StorageHubTheme.TextMuted,
            Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(16)),
            Anchor = AnchorStyles.None
        };

        _status.AutoSize = true;
        _status.MaximumSize = new Size(LogicalToDeviceUnits(380), 0);
        _status.ForeColor = StorageHubTheme.Text;
        _status.Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(8));
        _status.Text = Describe(BootStage.PreparingData);
        _status.AccessibleName = "Startup status";
        _status.TextAlign = ContentAlignment.MiddleCenter;
        _status.Anchor = AnchorStyles.None;

        _detail.AutoSize = true;
        _detail.MaximumSize = new Size(LogicalToDeviceUnits(380), 0);
        _detail.ForeColor = StorageHubTheme.TextMuted;
        _detail.Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(10));
        _detail.Visible = false;
        _detail.AccessibleName = "Startup details";
        _detail.TextAlign = ContentAlignment.MiddleCenter;
        _detail.Anchor = AnchorStyles.None;

        _progress.Style = ProgressBarStyle.Marquee;
        _progress.MarqueeAnimationSpeed = 30;
        _progress.Height = LogicalToDeviceUnits(6);
        _progress.Dock = DockStyle.Fill;
        _progress.Margin = new Padding(0);
        _progress.AccessibleName = "Startup progress";

        _buttons.AutoSize = true;
        _buttons.FlowDirection = FlowDirection.RightToLeft;
        _buttons.Dock = DockStyle.Fill;
        _buttons.Margin = new Padding(0, LogicalToDeviceUnits(12), 0, 0);
        _buttons.Visible = false;

        _body.Controls.Add(mark, 0, 0);
        _body.Controls.Add(title, 0, 1);
        _body.Controls.Add(version, 0, 2);
        _body.Controls.Add(_status, 0, 3);
        _body.Controls.Add(_detail, 0, 4);
        _body.Controls.Add(_progress, 0, 5);
        _body.Controls.Add(_buttons, 0, 6);
        Controls.Add(_body);
    }

    /// <summary>
    /// Takes the height the content actually needs. A fixed height either clips a wrapped failure
    /// message or leaves a band of empty window under a one-line status.
    /// </summary>
    private void ResizeToContent()
    {
        var width = LogicalToDeviceUnits(420);
        _body.MaximumSize = new Size(width - Padding.Horizontal, 0);
        ClientSize = new Size(width, _body.PreferredSize.Height + Padding.Vertical);
    }

    /// <summary>Shows the step the shell has reached.</summary>
    internal void Report(BootStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        _stage = status.Stage;
        _status.Text = Describe(status.Stage);

        if (!string.IsNullOrWhiteSpace(status.Warning))
        {
            // Warnings accumulate rather than replace: a quarantined settings file and a validator
            // warning are both worth seeing, and neither should interrupt a start that is working.
            _warning = string.IsNullOrWhiteSpace(_warning)
                ? status.Warning
                : $"{_warning}{Environment.NewLine}{status.Warning}";
            _detail.Text = _warning;
            _detail.Visible = true;

            // The window was sized for a hidden detail line, so without this the warning is drawn
            // past the bottom edge and the one thing worth reading is the part that is cut off.
            // ShowFailure has always done this; a warning needs it just as much, and needs it again
            // for each warning that accumulates.
            ResizeToContent();
            CenterToScreen();
        }

        // Only the agent wait runs long enough to need explaining, and only while it is running.
        _patience.Enabled = status.Stage == BootStage.StartingAgent;
    }

    private void PatienceElapsed(object? sender, EventArgs e)
    {
        _patience.Enabled = false;
        if (_stage != BootStage.StartingAgent)
        {
            return;
        }

        _status.Text = $"{Describe(BootStage.StartingAgent)} This can take a few seconds the first time.";
    }

    /// <summary>Replaces the progress body with a failure the user can act on.</summary>
    internal void ShowFailure(string message, string? details, bool canRetry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        _patience.Enabled = false;
        _progress.Visible = false;

        _status.Text = message;
        _status.ForeColor = StorageHubTheme.Danger;

        if (!string.IsNullOrWhiteSpace(details))
        {
            _detail.Text = details;
            _detail.Visible = true;
        }

        _buttons.Controls.Clear();
        var quit = new StorageHubButton { Text = "Quit", Variant = StorageHubButtonVariant.Secondary };
        quit.Click += (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty);
        _buttons.Controls.Add(quit);

        var copy = new StorageHubButton { Text = "Copy details", Variant = StorageHubButtonVariant.Secondary };
        copy.Click += (_, _) => CopyDetails(message, details);
        _buttons.Controls.Add(copy);

        if (canRetry)
        {
            var retry = new StorageHubButton { Text = "Retry", Variant = StorageHubButtonVariant.Primary };
            retry.Click += (_, _) => RetryRequested?.Invoke(this, EventArgs.Empty);
            _buttons.Controls.Add(retry);
            AcceptButton = retry;
        }

        _buttons.Visible = true;

        // A failure explains more than a status line does, so let the window grow to fit it.
        ResizeToContent();
        CenterToScreen();
    }

    /// <summary>Restores the progress body so a retry looks like a fresh attempt.</summary>
    internal void ResumeProgress(BootStatus status)
    {
        _buttons.Visible = false;
        _buttons.Controls.Clear();
        AcceptButton = null;
        _status.ForeColor = StorageHubTheme.Text;
        _detail.Text = _warning ?? string.Empty;
        _detail.Visible = !string.IsNullOrWhiteSpace(_warning);
        _progress.Visible = true;
        Report(status);
    }

    private static void CopyDetails(string message, string? details)
    {
        try
        {
            var text = string.IsNullOrWhiteSpace(details)
                ? message
                : $"{message}{Environment.NewLine}{Environment.NewLine}{details}";
            Clipboard.SetText($"StorageHub {DesktopApplicationVersion.Current}{Environment.NewLine}{text}");
        }
        catch (ExternalException)
        {
            // Another process owns the clipboard. The text is on screen either way.
        }
    }

    /// <summary>Fades out so the hand-off to the main window is not a flicker.</summary>
    internal async Task FadeOutAsync()
    {
        for (var step = 0; step < 10; step++)
        {
            Opacity = 1d - ((step + 1) * 0.1d);
            await Task.Delay(18).ConfigureAwait(true);
        }
    }

    internal static string Describe(BootStage stage) => stage switch
    {
        BootStage.PreparingData => "Preparing StorageHub data...",
        BootStage.StartingFramework => "Starting the application framework...",
        BootStage.CheckingEnvironment => "Checking the StorageHub folders...",
        BootStage.LoadingSettings => "Loading your settings...",
        BootStage.LoadingLanguage => "Loading language files...",
        BootStage.StartingAgent => "Starting the background agent...",
        BootStage.Opening => "Opening StorageHub...",
        _ => "Starting StorageHub..."
    };

    /// <summary>Lets a borderless window be dragged, so a slow start is not pinned to the centre.</summary>
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WmNcHitTest && m.Result == HtClient)
        {
            m.Result = HtCaption;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPaint(e);
        using var pen = new Pen(StorageHubTheme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    /// <summary>
    /// The branding mark, read from this assembly and pre-scaled to the size it is drawn at.
    /// </summary>
    /// <remarks>
    /// The source image is 1024px square. Handing that to a PictureBox means the full bitmap is
    /// held and rescaled on every paint, which on a marquee-animated window is real work for no
    /// visible difference. Resampling once here costs one allocation and keeps the splash cheap.
    ///
    /// A missing or unreadable resource leaves the splash without its mark rather than without a
    /// window: this is the surface that has to report a damaged installation.
    /// </remarks>
    private static Bitmap? LoadLogo(int size)
    {
        try
        {
            using var stream = typeof(StorageHubSplashForm).Assembly
                .GetManifestResourceStream("StorageHub.Desktop.storagehub-icon.png");
            if (stream is null)
            {
                return null;
            }

            using var source = Image.FromStream(stream);
            var scaled = new Bitmap(size, size);
            using (var graphics = Graphics.FromImage(scaled))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, size, size));
            }

            return scaled;
        }
        catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static Icon? LoadIcon()
    {
        try
        {
            var executable = Environment.ProcessPath;
            return string.IsNullOrWhiteSpace(executable)
                ? null
                : Icon.ExtractAssociatedIcon(executable);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _patience.Tick -= PatienceElapsed;
            _patience.Dispose();
            _icon?.Dispose();
            _logo?.Dispose();
        }

        base.Dispose(disposing);
    }
}
