using System.ComponentModel;
using System.Windows.Input;
using StorageHub.Desktop.Framework;

namespace StorageHub.Desktop.Views;

/// <summary>
/// The splash: which startup step the shell is on, anything worth knowing that did not stop it,
/// and -- when a step does stop it -- what went wrong and what can be done.
/// </summary>
/// <remarks>
/// Ported from 1.x's StorageHubSplashForm. 2.0 had no splash at all: it opened the main window at
/// once, before the agent was running and before settings or language had been read, and said what
/// the settings check had done in a dialog afterwards.
/// </remarks>
internal sealed class SplashModel : INotifyPropertyChanged
{
    private BootStage _stage = BootStage.PreparingData;
    private string _status = BootText.Describe(BootStage.PreparingData);
    private string _details = string.Empty;
    private string _warnings = string.Empty;
    private bool _failed;
    private bool _canRetry;

    internal SplashModel()
    {
        QuitCommand = new RelayCommand(_ => QuitRequested?.Invoke(this, EventArgs.Empty));
        RetryCommand = new RelayCommand(_ => RetryRequested?.Invoke(this, EventArgs.Empty), _ => _canRetry);
        CopyDetailsCommand = new RelayCommand(_ => CopyRequested?.Invoke(this, CopyText));
        CheckInstallationCommand = new RelayCommand(_ => CheckInstallationRequested?.Invoke(this, EventArgs.Empty));
    }

    public static string Title => "StorageHub";

    public static string Version => DesktopApplicationVersion.Current;

    public static string QuitLabel => BootText.Quit;

    public static string RetryLabel => BootText.Retry;

    public static string CopyDetailsLabel => BootText.CopyDetails;

    public static string CheckInstallationLabel => BootText.CheckInstallation;

    /// <summary>The step, or the failure, in one line.</summary>
    public string Status
    {
        get => _status;
        private set => Set(ref _status, value, nameof(Status));
    }

    /// <summary>Warnings gathered along the way, and a failure's details, under the status.</summary>
    public string Details
    {
        get => _details;
        private set
        {
            Set(ref _details, value, nameof(Details));
            Raise(nameof(HasDetails));
        }
    }

    public bool HasDetails => _details.Length > 0;

    /// <summary>Whether startup stopped; the progress bar gives way to the buttons.</summary>
    public bool IsFailed
    {
        get => _failed;
        private set
        {
            Set(ref _failed, value, nameof(IsFailed));
            Raise(nameof(IsWorking));
        }
    }

    public bool IsWorking => !_failed;

    public bool CanRetry
    {
        get => _canRetry;
        private set
        {
            Set(ref _canRetry, value, nameof(CanRetry));
            (RetryCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public ICommand QuitCommand { get; }

    public ICommand RetryCommand { get; }

    public ICommand CopyDetailsCommand { get; }

    /// <summary>
    /// Offered on the failure screen above all, as in 1.4: when the agent does not come up the main
    /// window never opens, so this is the one screen from which the check can be reached.
    /// </summary>
    public ICommand CheckInstallationCommand { get; }

    internal event EventHandler? QuitRequested;

    internal event EventHandler? RetryRequested;

    /// <summary>Raised to open the installation check; the window owns the dialog.</summary>
    internal event EventHandler? CheckInstallationRequested;

    /// <summary>Raised with the text to put on the clipboard; the window owns the clipboard.</summary>
    internal event EventHandler<string>? CopyRequested;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// "StorageHub 2.0.0", then the message and its details: enough for a bug report to say which
    /// build failed and how.
    /// </summary>
    internal string CopyText =>
        $"StorageHub {Version}{Environment.NewLine}{_status}" +
        (_details.Length > 0 ? $"{Environment.NewLine}{Environment.NewLine}{_details}" : string.Empty);

    /// <summary>Shows the step the shell has reached, and keeps any warning it carries.</summary>
    internal void Report(BootStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        _stage = status.Stage;
        Status = BootText.Describe(status.Stage);

        // Warnings accumulate rather than replace: a set-aside settings file and a validator
        // warning are both worth seeing, and neither should interrupt a start that is working.
        if (!string.IsNullOrWhiteSpace(status.Warning))
        {
            _warnings = _warnings.Length == 0
                ? status.Warning
                : $"{_warnings}{Environment.NewLine}{status.Warning}";
            Details = _warnings;
        }
    }

    /// <summary>
    /// Adds the patience line, if the agent is still the thing being waited for. Only that wait
    /// runs long enough to need explaining, and only while it is running.
    /// </summary>
    internal void StillWaiting()
    {
        if (_stage == BootStage.StartingAgent && !_failed)
        {
            Status = $"{BootText.Describe(BootStage.StartingAgent)} {BootText.Patience}";
        }
    }

    internal void ShowFailure(string message, string? details, bool canRetry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Status = message;
        Details = string.IsNullOrWhiteSpace(details) ? _warnings : details;
        CanRetry = canRetry;
        IsFailed = true;
    }

    /// <summary>Back to progress, so a retry reads as a fresh attempt.</summary>
    internal void Resume(BootStatus status)
    {
        IsFailed = false;
        CanRetry = false;
        Details = _warnings;
        Report(status);
    }

    private void Set<T>(ref T field, T value, string name)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Raise(name);
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
