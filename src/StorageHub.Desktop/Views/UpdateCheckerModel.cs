using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Threading;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>
/// One place to see the installed version, check for a newer one, watch it download, and install it.
/// </summary>
/// <remarks>
/// <para>
/// The manual check in 1.x was a chain of message boxes with no feedback: the check and the
/// download both blocked with nothing on screen, and a download of a hundred-odd megabytes looked
/// like the application had hung. The updater already publishes progress; this shows it, and keeps
/// every step in one window that can be cancelled.
/// </para>
/// <para>
/// Check, download and install are one button rather than a wizard per step, because what the
/// button does next is a pure function of the updater's state --
/// <see cref="DesktopUpdatePresentation.NextAction"/> -- and that is shared with whatever else
/// draws an update flow rather than restated here.
/// </para>
/// </remarks>
internal sealed class UpdateCheckerModel : INotifyPropertyChanged, IDisposable
{
    private readonly DesktopUpdater _updater;
    private readonly Action<DesktopUpdateSnapshot>? _onUiThread;
    private DesktopUpdateSnapshot _snapshot;
    private CancellationTokenSource _operation = new();
    private bool _closing;

    /// <param name="post">
    /// How a status change reaches the UI thread. The updater raises on whichever thread its work
    /// finished on, and Avalonia requires the property change on the UI one. Replaced in tests,
    /// which have no dispatcher loop running to drain.
    /// </param>
    internal UpdateCheckerModel(DesktopUpdater updater, Action<DesktopUpdateSnapshot>? post = null)
    {
        _updater = updater ?? throw new ArgumentNullException(nameof(updater));
        _onUiThread = post;
        _snapshot = updater.Snapshot;

        PrimaryCommand = new RelayCommand(_ => _ = PrimaryAsync(), _ => CanPrimary);
        CloseCommand = new RelayCommand(_ => Closed?.Invoke(this, EventArgs.Empty));

        _updater.StatusChanged += OnStatusChanged;
    }

    /// <summary>What the updater last reported.</summary>
    internal DesktopUpdateSnapshot Snapshot => _snapshot;

    public string Headline => DesktopUpdatePresentation.DescribeHeadline(_snapshot);

    public string Detail => DesktopUpdatePresentation.DescribeDetail(_snapshot);

    /// <summary>What is running now. Static: the version cannot change while the window is open.</summary>
    public static string Installed => Ui.Updates.InstalledVersion + DesktopApplicationVersion.Current;

    /// <summary>Which releases this copy is offered, which is a setting rather than a state.</summary>
    public string Channel => _updater.Preferences.IncludePrereleases
        ? Ui.Updates.ChannelStableReleasesAndReleaseCandidates
        : Ui.Updates.ChannelStableReleasesOnly;

    public bool IsDownloading => _snapshot.State is DesktopUpdateState.Downloading;

    /// <summary>How far the download has got. Clamped, because it reaches the bar directly.</summary>
    public int ProgressPercent => Math.Clamp(_snapshot.ProgressPercent ?? 0, 0, 100);

    /// <summary>
    /// The colour of the headline.
    /// </summary>
    /// <remarks>
    /// An available update is a warning rather than a success: nothing is wrong, but something is
    /// waiting to be done. Ready-to-install is a success because the risky half is over.
    /// </remarks>
    public MetricTone Tone => _snapshot.State switch
    {
        DesktopUpdateState.ReadyToRestart or DesktopUpdateState.UpToDate => MetricTone.Success,
        DesktopUpdateState.UpdateAvailable => MetricTone.Warning,
        DesktopUpdateState.Failed => MetricTone.Danger,
        _ => MetricTone.Neutral
    };

    public bool IsSuccess => Tone == MetricTone.Success;

    public bool IsWarning => Tone == MetricTone.Warning;

    public bool IsDanger => Tone == MetricTone.Danger;

    /// <summary>What the one action button says, which follows what it would do.</summary>
    public string PrimaryLabel => DesktopUpdatePresentation.NextAction(_snapshot.State) switch
    {
        UpdateAction.Download => Ui.Updates.DownloadUpdate,
        UpdateAction.Restart => Ui.Updates.RestartAndInstall,
        UpdateAction.None => Ui.Updates.Working,
        _ => Ui.Updates.CheckForUpdates
    };

    public bool CanPrimary => DesktopUpdatePresentation.NextAction(_snapshot.State) is not UpdateAction.None;

    /// <summary>
    /// Closing during a download cancels it, so the button says so.
    /// </summary>
    /// <remarks>
    /// This was the one place the WinForms window said "Close" as an English literal rather than
    /// through its strings, so in Danish and German the button changed language halfway through a
    /// download.
    /// </remarks>
    public string CloseLabel => IsDownloading ? Ui.Updates.Cancel : Ui.Dialogs.ButtonClose;

    public ICommand PrimaryCommand { get; }

    public ICommand CloseCommand { get; }

    /// <summary>Raised when the window should close.</summary>
    internal event EventHandler? Closed;

    /// <summary>
    /// Does whatever the button says next: check, download, or install and restart.
    /// </summary>
    /// <remarks>
    /// Each press gets a fresh cancellation source, so cancelling a download does not leave the
    /// next check cancelled before it starts.
    /// </remarks>
    internal async Task PrimaryAsync()
    {
        var action = DesktopUpdatePresentation.NextAction(_snapshot.State);
        _operation.Dispose();
        _operation = new CancellationTokenSource();
        try
        {
            switch (action)
            {
                case UpdateAction.Check:
                    await _updater.CheckForUpdatesAsync(_operation.Token).ConfigureAwait(true);
                    break;

                case UpdateAction.Download:
                    await _updater.DownloadAvailableAsync(_operation.Token).ConfigureAwait(true);
                    break;

                case UpdateAction.Restart:
                    if (!_updater.ApplyAndRestart())
                    {
                        Show(_snapshot with
                        {
                            State = DesktopUpdateState.Failed,
                            Message = Ui.Updates.StorageHubCouldNotStartTheUpdaterReopen
                        });
                    }

                    break;

                default:
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Closing the window cancels in-flight work; there is nothing to report.
        }
        catch (Exception error) when (error is IOException or TimeoutException or
            InvalidOperationException or UnauthorizedAccessException or HttpRequestException)
        {
            Show(_snapshot with
            {
                State = DesktopUpdateState.Failed,
                Message = Ui.Format(Ui.Updates.UpdateCheckFailedFormat, error.Message)
            });
        }
    }

    /// <summary>
    /// Stops listening and cancels whatever is running.
    /// </summary>
    /// <remarks>
    /// The unsubscribe comes first. The updater outlives this window -- it is the shell's, and it
    /// checks on a timer -- so a model that stayed subscribed would be kept alive by it and would
    /// go on raising property changes for a window that had closed.
    /// </remarks>
    public void Dispose()
    {
        if (_closing) return;
        _closing = true;
        _updater.StatusChanged -= OnStatusChanged;
        _operation.Cancel();
        _operation.Dispose();
    }

    private void OnStatusChanged(object? sender, DesktopUpdateSnapshot snapshot)
    {
        if (_closing) return;
        if (_onUiThread is { } post)
        {
            post(snapshot);
            return;
        }

        Dispatcher.UIThread.Post(() => Show(snapshot));
    }

    /// <summary>Takes a snapshot and tells the window everything that follows from it.</summary>
    internal void Show(DesktopUpdateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_closing) return;
        _snapshot = snapshot;

        Raise(nameof(Headline));
        Raise(nameof(Detail));
        Raise(nameof(Channel));
        Raise(nameof(IsDownloading));
        Raise(nameof(ProgressPercent));
        Raise(nameof(Tone));
        Raise(nameof(IsSuccess));
        Raise(nameof(IsWarning));
        Raise(nameof(IsDanger));
        Raise(nameof(PrimaryLabel));
        Raise(nameof(CanPrimary));
        Raise(nameof(CloseLabel));
        (PrimaryCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public static string Title => Ui.Updates.StorageHubUpdates;

    public static string DetailAccessibleName => Ui.Updates.UpdateDetail;

    public static string ProgressAccessibleName => Ui.Updates.UpdateDownloadProgress;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
