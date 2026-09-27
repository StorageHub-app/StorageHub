using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Services;
using StorageHub.Desktop.Shell;

namespace StorageHub.Desktop.Views;

/// <summary>One card of the installation check.</summary>
internal sealed class InstallationFindingRow(InstallationFinding finding, ICommand? repair)
{
    public string Title => finding.Title;

    public string Detail => finding.Detail;

    public string Location => finding.Location ?? string.Empty;

    public bool HasLocation => !string.IsNullOrWhiteSpace(finding.Location);

    public bool IsSuccess => finding.Status == InstallationCheckStatus.Ok;

    public bool IsWarning => finding.Status == InstallationCheckStatus.Warning;

    public bool IsDanger => finding.Status == InstallationCheckStatus.Problem;

    /// <summary>Offered only where the check named a repair.</summary>
    public bool CanRepair => repair is not null;

    public ICommand? RepairCommand => repair;

    public static string RepairLabel => Ui.Updates.InstallationRepairButton;
}

/// <summary>
/// What the installation check found, and the repairs it offers.
/// </summary>
/// <remarks>
/// <para>
/// 1.4's InstallationCheckForm. The check and the repair are passed in rather than reached for, so
/// the window can be driven without a machine to inspect; <see cref="InstallationCheckWindow"/>
/// supplies the real ones.
/// </para>
/// <para>
/// The check only looks, so it runs when the window opens and again on Check again, and after every
/// repair so the card that was fixed says so.
/// </para>
/// </remarks>
internal sealed class InstallationCheckModel : INotifyPropertyChanged
{
    private readonly Func<CancellationToken, Task<InstallationReport>> _inspect;
    private readonly Func<InstallationRepair, InstallationRepairResult> _repair;
    private readonly IDialogService _dialogs;
    private StatusLine _summary = StatusLine.Muted(string.Empty);
    private IReadOnlyList<InstallationFindingRow> _findings = [];
    private bool _busy;

    internal InstallationCheckModel(
        Func<CancellationToken, Task<InstallationReport>> inspect,
        Func<InstallationRepair, InstallationRepairResult> repair,
        IDialogService dialogs)
    {
        _inspect = inspect ?? throw new ArgumentNullException(nameof(inspect));
        _repair = repair ?? throw new ArgumentNullException(nameof(repair));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        CheckAgainCommand = new RelayCommand(_ => _ = RefreshAsync(), _ => !_busy);
        CloseCommand = new RelayCommand(_ => Closed?.Invoke(this, EventArgs.Empty));
    }

    public static string Title => Ui.Updates.InstallationCheckTitle;

    public static string CheckAgainLabel => Ui.Updates.InstallationCheckAgain;

    public static string CloseLabel => Ui.Dialogs.ButtonClose;

    /// <summary>How it went, in the tone of the worst thing found.</summary>
    public StatusLine Summary
    {
        get => _summary;
        private set
        {
            _summary = value;
            Raise(nameof(Summary));
        }
    }

    public IReadOnlyList<InstallationFindingRow> Findings
    {
        get => _findings;
        private set
        {
            _findings = value;
            Raise(nameof(Findings));
        }
    }

    public ICommand CheckAgainCommand { get; }

    public ICommand CloseCommand { get; }

    internal event EventHandler? Closed;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Runs the check and shows what it found.</summary>
    internal async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            await CheckAsync(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Applies one repair, says how it went, and checks again.</summary>
    /// <remarks>
    /// Busy from the repair until the check after it has finished, as 1.4's wait cursor was, so a
    /// second click cannot apply the repair twice and stack a second result over the first.
    /// </remarks>
    internal async Task RepairAsync(InstallationRepair repair)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var result = _repair(repair);
            await _dialogs.ShowAsync(new DialogRequest
            {
                Title = result.Succeeded ? Ui.Updates.InstallationRepaired : Ui.Updates.InstallationCouldNotRepair,
                Message = result.Message,
                Severity = result.Succeeded ? DialogSeverity.Information : DialogSeverity.Warning,
            }).ConfigureAwait(true);
            await CheckAsync(CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        // Said at once: asking the agent takes a second at least, and 1.4 opened on its results
        // rather than on a blank window.
        Summary = StatusLine.Muted(Ui.Updates.InstallationChecking);
        try
        {
            var report = await _inspect(cancellationToken).ConfigureAwait(true);
            Summary = new StatusLine(AgentInstallationCheck.Summarize(report), ToneOf(report.Worst));
            Findings = [.. report.Findings.Select(finding => new InstallationFindingRow(
                finding,
                finding.Repair == InstallationRepair.None
                    ? null
                    : new RelayCommand(_ => _ = RepairAsync(finding.Repair), _ => !_busy)))];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidOperationException or PlatformNotSupportedException)
        {
            // Said on the window rather than thrown past it: this is the screen somebody opens
            // because something is already wrong.
            Summary = new StatusLine(Ui.Updates.InstallationCheckCouldNotRun, MetricTone.Danger);
            Findings =
            [
                new InstallationFindingRow(
                    new InstallationFinding(Ui.Updates.InstallationCheckFailed, InstallationCheckStatus.Problem, error.Message),
                    repair: null),
            ];
        }
    }

    private static MetricTone ToneOf(InstallationCheckStatus status) => status switch
    {
        InstallationCheckStatus.Ok => MetricTone.Success,
        InstallationCheckStatus.Warning => MetricTone.Warning,
        _ => MetricTone.Danger,
    };

    private void SetBusy(bool busy)
    {
        _busy = busy;
        (CheckAgainCommand as RelayCommand)?.RaiseCanExecuteChanged();
        foreach (var row in Findings) (row.RepairCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Says which part of the installation is wrong when the agent will not come up.
/// </summary>
/// <remarks>
/// Opened from the splash's failure screen and from Agent control, as 1.4's was. The splash matters
/// most: when the agent does not come up the main window never opens, so every other way to this
/// window is behind a door that will not open.
/// </remarks>
public partial class InstallationCheckWindow : Window
{
    public InstallationCheckWindow()
    {
        AvaloniaXamlLoader.Load(this);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is InstallationCheckModel model) model.Closed += (_, _) => Close();
        };

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);

        Opened += async (_, _) =>
        {
            if (DataContext is InstallationCheckModel model) await model.RefreshAsync().ConfigureAwait(true);
        };
    }

    /// <summary>Checks this machine's installation, over <paramref name="owner"/>.</summary>
    /// <remarks>
    /// The layout is resolved at every check rather than once, so a failure to resolve it is
    /// reported on the window like any other rather than stopping it from opening.
    /// </remarks>
    internal static Task ShowForThisMachineAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        InstallationCheckWindow? window = null;
        var model = new InstallationCheckModel(
            AgentInstallationCheck.InspectThisMachineAsync,
            repair => AgentInstallationRepair.Apply(repair, InstallationLayout.ForThisMachine()),
            new AvaloniaDialogService(() => window));
        window = new InstallationCheckWindow { DataContext = model };
        return window.ShowDialog(owner);
    }
}
