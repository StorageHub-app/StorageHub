using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;

namespace StorageHub.Desktop.Views;

/// <summary>
/// What the Diagnostics window shows: this installation described in a few lines, to copy into a
/// report, and a support bundle to save and attach.
/// </summary>
internal sealed class DiagnosticsModel : INotifyPropertyChanged
{
    private readonly DiagnosticsFacts _facts;
    private readonly IClipboardService? _clipboard;
    private readonly IFilePickerService? _picker;
    private readonly Func<string, Task<bool>>? _openFolder;
    private readonly Func<string, DiagnosticsFacts, Task<IReadOnlyList<string>>> _writeBundle;
    private StatusLine _status = StatusLine.Muted(Ui.Shell.DiagnosticsPrivacy);

    /// <param name="openFolder">Opens the log folder in the file manager, answering whether anything did.</param>
    /// <param name="writeBundle">Writes the bundle to a path; the app's is <see cref="DesktopDiagnostics.WriteBundleAsync"/>.</param>
    internal DiagnosticsModel(
        DiagnosticsFacts facts,
        string logFolder,
        IClipboardService? clipboard,
        IFilePickerService? picker,
        Func<string, Task<bool>>? openFolder,
        Func<string, DiagnosticsFacts, Task<IReadOnlyList<string>>> writeBundle)
    {
        _facts = facts ?? throw new ArgumentNullException(nameof(facts));
        LogFolder = logFolder ?? throw new ArgumentNullException(nameof(logFolder));
        _clipboard = clipboard;
        _picker = picker;
        _openFolder = openFolder;
        _writeBundle = writeBundle ?? throw new ArgumentNullException(nameof(writeBundle));
        Report = DesktopDiagnostics.Report(facts, logFolder);
        CopyCommand = new RelayCommand(_ => _ = CopyAsync(), _ => _clipboard is not null);
        SaveBundleCommand = new RelayCommand(_ => _ = SaveBundleAsync(), _ => _picker is not null);
        OpenLogsCommand = new RelayCommand(_ => _ = OpenLogsAsync(), _ => _openFolder is not null);
    }

    public string Report { get; }

    internal string LogFolder { get; }

    public StatusLine Status
    {
        get => _status;
        private set
        {
            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }

    public ICommand CopyCommand { get; }

    public ICommand SaveBundleCommand { get; }

    public ICommand OpenLogsCommand { get; }

    public static string Title => Ui.Shell.DiagnosticsTitle;

    public static string Intro => Ui.Shell.DiagnosticsIntro;

    public static string CopyLabel => Ui.Shell.DiagnosticsCopy;

    public static string SaveBundleLabel => Ui.Shell.DiagnosticsSaveBundle;

    public static string OpenLogsLabel => Ui.Shell.DiagnosticsOpenLogs;

    public static string CloseLabel => Ui.Dialogs.ButtonClose;

    internal async Task CopyAsync()
    {
        if (_clipboard is null) return;
        await _clipboard.SetTextAsync(Report).ConfigureAwait(true);
        Status = new StatusLine(Ui.Shell.DiagnosticsCopied, MetricTone.Success);
    }

    /// <summary>Asks where, then writes the bundle there and says what went into it.</summary>
    internal async Task SaveBundleAsync()
    {
        if (_picker is null) return;
        var path = await _picker.SaveFileAsync(new FilePickerRequest
        {
            Title = Ui.Shell.DiagnosticsSaveBundle,
            SuggestedFileName = string.Create(
                CultureInfo.InvariantCulture, $"storagehub-diagnostics-{_facts.CollectedAtUtc:yyyyMMdd-HHmmss}.zip"),
            Filters = [new FilePickerFilter(Ui.Shell.DiagnosticsBundleFilter, ["zip"])]
        }).ConfigureAwait(true);
        if (path is null) return;

        try
        {
            var written = await _writeBundle(path, _facts).ConfigureAwait(true);
            Status = new StatusLine(
                Ui.Format(Ui.Shell.DiagnosticsSavedFormat, Path.GetFileName(path), written.Count),
                MetricTone.Success);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Status = new StatusLine(Ui.Format(Ui.Shell.DiagnosticsSaveFailedFormat, error.Message), MetricTone.Danger);
        }
    }

    /// <summary>Opens the log folder, or says where it is when nothing could.</summary>
    internal async Task OpenLogsAsync()
    {
        if (_openFolder is null) return;
        if (!await _openFolder(LogFolder).ConfigureAwait(true))
        {
            Status = new StatusLine(Ui.Format(Ui.Shell.LogsFolderIsFormat, LogFolder), MetricTone.Warning);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
