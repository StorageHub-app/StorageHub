using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>
/// Shows what the background agent is doing, and lets it be started, stopped, or restarted.
/// </summary>
/// <remarks>
/// <para>
/// The agent owns the durable queue, the scheduler and the vault, so when it is degraded the rest
/// of the application quietly stops working. Until this screen its state was a single word in the
/// status bar with no way to read the reason or act on it.
/// </para>
/// <para>
/// The status is read rather than subscribed to, on a timer, because that is what the monitor
/// offers and because a window that polls once a second is simpler than one that has to unsubscribe
/// correctly. The controller may be null: a build with no packaged agent and no systemd unit can
/// report the agent's state but cannot change it, and the screen says so rather than offering
/// buttons that do nothing.
/// </para>
/// </remarks>
internal sealed class AgentControlModel : INotifyPropertyChanged, IDisposable
{
    private readonly Func<AgentMonitorStatus?> _readStatus;
    private readonly IAgentLifecycleController? _controller;
    private readonly CancellationTokenSource _lifetime = new();
    private AgentMonitorStatus? _status;
    private StatusLine _outcome = StatusLine.Muted(string.Empty);
    private bool _busy;

    internal AgentControlModel(
        Func<AgentMonitorStatus?> readStatus,
        IAgentLifecycleController? controller)
    {
        _readStatus = readStatus ?? throw new ArgumentNullException(nameof(readStatus));
        _controller = controller;

        StartCommand = new RelayCommand(_ => _ = RunAsync(AgentLifecycleAction.Start), _ => CanStart);
        StopCommand = new RelayCommand(_ => _ = RunAsync(AgentLifecycleAction.Stop), _ => CanStop);
        RestartCommand = new RelayCommand(_ => _ = RunAsync(AgentLifecycleAction.Restart), _ => CanRestart);
        CloseCommand = new RelayCommand(_ => Closed?.Invoke(this, EventArgs.Empty));

        Refresh();
    }

    /// <summary>The state in a word, or that nothing has been reported yet.</summary>
    public string State => _status is { } known
        ? AgentControlPresentation.DescribeState(known.State)
        : Ui.Updates.AgentStateUnknown;

    /// <summary>What that state means for the rest of the application.</summary>
    public string Detail => _status is { } known
        ? AgentControlPresentation.DescribeDetail(known)
        : Ui.Updates.NoStatusHasBeenReportedYet;

    /// <summary>What the agent is working on, which is empty until it has said.</summary>
    public string Counters => _status is { } known
        ? Ui.Format(Ui.Updates.AgentActiveWorkFormat, known.ActiveTransfers, known.ActiveSyncRuns)
        : string.Empty;

    /// <summary>When the agent last said anything, in this machine's own time.</summary>
    public string Observed => _status is { } known
        ? Ui.Updates.LastReported +
            known.ObservedAtUtc.ToLocalTime().ToString("T", CultureInfo.CurrentCulture)
        : string.Empty;

    /// <summary>
    /// The colour the state is drawn in.
    /// </summary>
    /// <remarks>
    /// The one part of this the view owns rather than Core: recovery is a warning rather than a
    /// failure, because the agent is running and it is only its durable half that is not.
    /// </remarks>
    public MetricTone Tone => _status?.State switch
    {
        AgentConnectionState.Connected => MetricTone.Success,
        AgentConnectionState.RecoveryOnly => MetricTone.Warning,
        AgentConnectionState.Disconnected => MetricTone.Danger,
        _ => MetricTone.Neutral
    };

    public bool IsSuccess => Tone == MetricTone.Success;

    public bool IsWarning => Tone == MetricTone.Warning;

    public bool IsDanger => Tone == MetricTone.Danger;

    /// <summary>What the last action did, or why it could not.</summary>
    public StatusLine Outcome
    {
        get => _outcome;
        private set => Set(ref _outcome, value);
    }

    /// <summary>Whether this build can change the agent's state at all.</summary>
    public bool CanControl => _controller is not null;

    public bool CanStart => CanControl && !_busy && AgentControlPresentation.CanStart(_status?.State);

    public bool CanStop => CanControl && !_busy && AgentControlPresentation.CanStop(_status?.State);

    /// <summary>
    /// Restart is offered whatever the state, since an agent that is down is what it fixes. Only
    /// an action already in flight dims it.
    /// </summary>
    public bool CanRestart => CanControl && !_busy;

    public ICommand StartCommand { get; }

    public ICommand StopCommand { get; }

    public ICommand RestartCommand { get; }

    public ICommand CloseCommand { get; }

    /// <summary>Raised when the window should close.</summary>
    internal event EventHandler? Closed;

    /// <summary>Re-reads the agent's state. Called on a timer while the window is open.</summary>
    internal void Refresh()
    {
        _status = _readStatus();
        Raise(nameof(State));
        Raise(nameof(Detail));
        Raise(nameof(Counters));
        Raise(nameof(Observed));
        Raise(nameof(Tone));
        Raise(nameof(IsSuccess));
        Raise(nameof(IsWarning));
        Raise(nameof(IsDanger));
        RaiseActions();
    }

    /// <summary>
    /// Runs one lifecycle action and reports what it did.
    /// </summary>
    /// <remarks>
    /// The outcome is never cleared on success: what a restart said is the only record the screen
    /// keeps of it, and the state above it is about to change anyway.
    /// </remarks>
    internal async Task RunAsync(AgentLifecycleAction action)
    {
        if (_controller is null)
        {
            Outcome = new StatusLine(Ui.Updates.ThisBuildCannotControlTheAgentProcess, MetricTone.Warning);
            return;
        }

        if (_busy) return;
        SetBusy(true);
        Outcome = StatusLine.Muted(AgentControlPresentation.DescribeInProgress(action));
        try
        {
            var result = await _controller
                .ExecuteAsync(action, _lifetime.Token).ConfigureAwait(true);
            Outcome = new StatusLine(
                result.Message, result.Succeeded ? MetricTone.Success : MetricTone.Danger);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The window closed while the action was in flight. There is nothing left to tell.
        }
        catch (Exception error) when (error is IOException or TimeoutException or
            InvalidOperationException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Outcome = new StatusLine(
                Ui.Format(Ui.Updates.AgentNoResponseFormat, error.Message), MetricTone.Danger);
        }
        finally
        {
            SetBusy(false);
            Refresh();
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        RaiseActions();
    }

    private void RaiseActions()
    {
        Raise(nameof(CanStart));
        Raise(nameof(CanStop));
        Raise(nameof(CanRestart));
        (StartCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (StopCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RestartCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public static string Title => Ui.Updates.BackgroundAgent;

    public static string StartLabel => Ui.Updates.AgentStart;

    public static string StopLabel => Ui.Updates.AgentStop;

    public static string RestartLabel => Ui.Updates.Restart;

    public static string CloseLabel => Ui.Dialogs.ButtonClose;

    public static string DetailAccessibleName => Ui.Updates.AgentDetail;

    public static string OutcomeAccessibleName => Ui.Updates.LastAgentActionResult;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
