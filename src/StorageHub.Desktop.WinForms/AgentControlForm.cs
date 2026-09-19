using System.Globalization;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// Shows what the background agent is doing and lets it be started, stopped, or restarted.
///
/// The agent owns the durable queue, scheduler, and vault, so when it is degraded the rest of the
/// app quietly stops working. Until now its state was a single word in the status bar with no way
/// to read the reason or act on it. This surfaces the detail the agent already reports - including
/// why it is in recovery - and the lifecycle operations that already existed but had no UI.
/// </summary>
public sealed class AgentControlForm : Form
{
    private readonly Func<AgentMonitorStatus?> _readStatus;
    private readonly IAgentLifecycleController? _controller;
    private readonly Label _state;
    private readonly Label _detail;
    private readonly Label _counters;
    private readonly Label _observed;
    private readonly Label _outcome;
    private readonly StorageHubButton _start;
    private readonly StorageHubButton _stop;
    private readonly StorageHubButton _restart;
    private readonly System.Windows.Forms.Timer _refresh;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _busy;

    public AgentControlForm(Func<AgentMonitorStatus?> readStatus, IAgentLifecycleController? controller)
    {
        _readStatus = readStatus ?? throw new ArgumentNullException(nameof(readStatus));
        _controller = controller;

        Text = Ui.Updates.BackgroundAgent;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = this.LogicalWindowSize(new Size(520, 300));
        BackColor = StorageHubTheme.Canvas;
        ForeColor = StorageHubTheme.Text;
        StorageHubTheme.Register(this);

        var stateFont = StorageHubTheme.CreateSectionFont();
        _state = new Label
        {
            Dock = DockStyle.Top,
            Height = this.TextBoxHeight(stateFont, 6),
            Font = stateFont,
            ForeColor = StorageHubTheme.Text
        };
        _detail = new Label
        {
            Dock = DockStyle.Top,
            Height = this.TextBoxHeight(30),
            ForeColor = StorageHubTheme.TextMuted,
            AccessibleName = Ui.Updates.AgentDetail
        };
        _counters = new Label { Dock = DockStyle.Top, Height = this.TextBoxHeight(3), ForeColor = StorageHubTheme.TextMuted };
        _observed = new Label { Dock = DockStyle.Top, Height = this.TextBoxHeight(3), ForeColor = StorageHubTheme.TextMuted };
        _outcome = new Label
        {
            Dock = DockStyle.Bottom,
            Height = this.TextBoxHeight(19),
            ForeColor = StorageHubTheme.TextMuted,
            AccessibleName = Ui.Updates.LastAgentActionResult
        };

        _start = CreateButton(Ui.Updates.AgentStart, () => RunAsync(AgentLifecycleAction.Start));
        _stop = CreateButton(Ui.Updates.AgentStop, () => RunAsync(AgentLifecycleAction.Stop));
        _restart = CreateButton(Ui.Updates.Restart, () => RunAsync(AgentLifecycleAction.Restart));
        _restart.Variant = StorageHubButtonVariant.Primary;
        _start.Variant = StorageHubButtonVariant.Secondary;
        _stop.Variant = StorageHubButtonVariant.Secondary;

        // Offered next to the lifecycle buttons because it answers the question they raise: if
        // starting the agent does not help, something about the installation is wrong, and this
        // says which of the five places it lives in is the one at fault.
        var check = CreateButton("Check installation", () =>
        {
            using var dialog = new InstallationCheckForm(DesktopAgentHost.Mode);
            dialog.ShowDialog(this);
            Render();
            return Task.CompletedTask;
        });
        check.Variant = StorageHubButtonVariant.Secondary;

        var close = new StorageHubButton { Text = Ui.Dialogs.ButtonClose, DialogResult = DialogResult.Cancel, AutoSize = true };
        close.Variant = StorageHubButtonVariant.Secondary;

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = this.TextBoxHeight(22),
            FlowDirection = FlowDirection.LeftToRight,
            Padding = this.LogicalToDeviceUnits(new Padding(0, 8, 0, 0))
        };
        actions.Controls.AddRange([_restart, _start, _stop, check, close]);

        var body = new Panel { Dock = DockStyle.Fill, Padding = this.LogicalToDeviceUnits(new Padding(14, 12, 14, 0)) };
        // Docked controls stack in reverse order of addition, so add bottom-most first.
        body.Controls.Add(_observed);
        body.Controls.Add(_counters);
        body.Controls.Add(_detail);
        body.Controls.Add(_state);

        Controls.Add(body);
        Controls.Add(_outcome);
        Controls.Add(actions);
        CancelButton = close;

        _refresh = new System.Windows.Forms.Timer { Interval = 1_000 };
        _refresh.Tick += (_, _) => Render();
        Render();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _refresh.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _refresh.Stop();
        _refresh.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
        base.OnFormClosed(e);
    }

    private StorageHubButton CreateButton(string text, Func<Task> handler)
    {
        var button = new StorageHubButton { Text = text, AutoSize = true, Margin = this.LogicalToDeviceUnits(new Padding(0, 0, 8, 0)), Height = this.TextBoxHeight(8) };
        button.Click += async (_, _) =>
        {
            if (_busy) return;
            await handler().ConfigureAwait(true);
        };
        return button;
    }

    private async Task RunAsync(AgentLifecycleAction action)
    {
        if (_controller is null)
        {
            ShowOutcome(Ui.Updates.ThisBuildCannotControlTheAgentProcess, StorageHubTheme.Warning);
            return;
        }

        SetBusy(true, action);
        try
        {
            var result = await _controller.ExecuteAsync(action, _lifetime.Token).ConfigureAwait(true);
            ShowOutcome(result.Message, result.Succeeded ? StorageHubTheme.Success : StorageHubTheme.Danger);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is IOException or TimeoutException or
            InvalidOperationException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ShowOutcome(Ui.Format(Ui.Updates.AgentNoResponseFormat, error.Message), StorageHubTheme.Danger);
        }
        finally
        {
            SetBusy(false, action);
            Render();
        }
    }

    private void Render()
    {
        var status = _readStatus();
        if (status is null)
        {
            _state.Text = Ui.Updates.AgentStateUnknown;
            _state.ForeColor = StorageHubTheme.TextMuted;
            _detail.Text = Ui.Updates.NoStatusHasBeenReportedYet;
            _counters.Text = string.Empty;
            _observed.Text = string.Empty;
            UpdateActionState(null);
            return;
        }

        _state.Text = DescribeState(status.State);
        _state.ForeColor = status.State switch
        {
            AgentConnectionState.Connected => StorageHubTheme.Success,
            AgentConnectionState.RecoveryOnly => StorageHubTheme.Warning,
            AgentConnectionState.Disconnected => StorageHubTheme.Danger,
            _ => StorageHubTheme.TextMuted
        };
        _detail.Text = string.IsNullOrWhiteSpace(status.Detail)
            ? DescribeDefaultDetail(status.State)
            : status.Detail;
        _counters.Text = Ui.Format(
            Ui.Updates.AgentActiveWorkFormat,
            status.ActiveTransfers,
            status.ActiveSyncRuns);
        _observed.Text = Ui.Updates.LastReported +
            status.ObservedAtUtc.ToLocalTime().ToString("T", CultureInfo.CurrentCulture);
        UpdateActionState(status.State);
    }

    /// <summary>
    /// Explains a state the status bar can only abbreviate. Recovery is called out because it is
    /// the one state where the agent is running yet nothing durable works.
    /// </summary>
    internal static string DescribeState(AgentConnectionState state) => state switch
    {
        AgentConnectionState.Connected => Ui.Updates.AgentStateRunning,
        AgentConnectionState.RecoveryOnly => Ui.Updates.AgentStateRecovery,
        AgentConnectionState.Disconnected => Ui.Updates.AgentStateNotRunning,
        AgentConnectionState.Reconnecting => Ui.Shell.AgentReconnectingStatus,
        _ => Ui.Updates.AgentStateStarting
    };

    internal static string DescribeDefaultDetail(AgentConnectionState state) => state switch
    {
        AgentConnectionState.Connected => Ui.Updates.TransfersSyncAndSchedulesAreAvailable,
        AgentConnectionState.RecoveryOnly =>
            Ui.Updates.TheAgentStartedButItsDurableState,
        AgentConnectionState.Disconnected =>
            Ui.Updates.StorageHubCannotReachTheBackgroundAgentTransfers,
        AgentConnectionState.Reconnecting => Ui.Shell.AgentReconnecting,
        _ => Ui.Updates.TheAgentIsStarting
    };

    private void UpdateActionState(AgentConnectionState? state)
    {
        var canControl = _controller is not null && !_busy;
        var running = state is AgentConnectionState.Connected or AgentConnectionState.RecoveryOnly
            or AgentConnectionState.Starting or AgentConnectionState.Reconnecting;
        _start.Enabled = canControl && state is AgentConnectionState.Disconnected;
        _stop.Enabled = canControl && running;
        _restart.Enabled = canControl;
    }

    private void SetBusy(bool busy, AgentLifecycleAction action)
    {
        _busy = busy;
        UseWaitCursor = busy;
        if (busy)
        {
            ShowOutcome(
                action switch
                {
                    AgentLifecycleAction.Start => Ui.Updates.StartingTheAgent,
                    AgentLifecycleAction.Stop => Ui.Updates.StoppingTheAgent,
                    _ => Ui.Updates.RestartingTheAgent
                },
                StorageHubTheme.TextMuted);
        }

        UpdateActionState(_readStatus()?.State);
    }

    private void ShowOutcome(string message, Color color)
    {
        _outcome.Text = message;
        _outcome.ForeColor = color;
    }
}

public enum AgentLifecycleAction
{
    Start = 1,
    Stop = 2,
    Restart = 3
}

public sealed record AgentLifecycleResult(bool Succeeded, string Message);

/// <summary>
/// Starts, stops, and restarts the background agent. Kept as an interface so the dialog can be
/// exercised without launching a real process.
/// </summary>
public interface IAgentLifecycleController
{
    Task<AgentLifecycleResult> ExecuteAsync(
        AgentLifecycleAction action,
        CancellationToken cancellationToken = default);
}
