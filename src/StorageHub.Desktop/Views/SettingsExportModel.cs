using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;
using StorageHub.Security;

namespace StorageHub.Desktop.Views;

/// <summary>One tickable section in the export dialog.</summary>
/// <remarks>
/// A class rather than a record because the tick is two-way bound and has to raise change
/// notifications; the rest of it comes straight from the catalog and never changes.
/// </remarks>
internal sealed class SettingsSectionChoice : INotifyPropertyChanged
{
    private bool _selected;

    internal SettingsSectionChoice(SettingsSectionDefinition definition, bool selected, bool available = true)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
        _selected = selected;
        IsAvailable = available;
    }

    internal SettingsSectionDefinition Definition { get; }

    internal SettingsSectionId Id => Definition.Id;

    public string Label => Definition.Label;

    /// <summary>What the section holds, or why it is not on offer.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Whether the box can be ticked at all. An export cannot reach the agent-backed sections
    /// with no agent, and an import cannot apply what the file does not carry.
    /// </summary>
    public bool IsAvailable { get; }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Sets the tick without telling anyone who is listening.
    /// </summary>
    /// <remarks>
    /// The dialog re-ticks every box when dependencies are expanded, and doing that through
    /// <see cref="Selected"/> would re-enter the handler that is doing the expanding.
    /// </remarks>
    internal void SetQuietly(bool value)
    {
        if (_selected == value) return;
        _selected = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
    }

    /// <summary>Raised when the user changed the tick, not when the dialog did.</summary>
    internal event EventHandler? SelectionChanged;

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Chooses what goes into an export file and where it is written.
/// </summary>
/// <remarks>
/// <para>
/// The section list comes from <see cref="SettingsSectionCatalog"/>, so this dialog and the import
/// wizard cannot offer different things. What can never be exported is listed too, with its
/// reason: a silent omission would be a surprise the first time somebody moved to a new machine
/// and found their connections could not open.
/// </para>
/// <para>
/// Ticks are kept consistent with what a file can actually describe as they are clicked --
/// choosing schedules also takes their sync tasks and connections, and clearing connections clears
/// what depended on them -- rather than quietly expanding the selection at write time.
/// </para>
/// </remarks>
internal sealed class SettingsExportModel : INotifyPropertyChanged
{
    private readonly SettingsExportService _exporter;
    private readonly IFilePickerService? _files;
    private readonly Func<DateTimeOffset> _clock;
    private bool _protect;
    private string _password = string.Empty;
    private string _confirm = string.Empty;
    private StatusLine _status = StatusLine.Muted(string.Empty);
    private bool _busy;
    private bool _updatingSections;

    internal SettingsExportModel(
        SettingsExportService exporter,
        IFilePickerService? files = null,
        Func<DateTimeOffset>? clock = null)
    {
        _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
        _files = files;
        _clock = clock ?? (() => DateTimeOffset.Now);

        // Agent-backed sections are dimmed with a reason rather than offered and then failing
        // halfway through a capture that cannot complete.
        var reachable = exporter.CanReachAgent;
        Sections = [.. SettingsSectionCatalog.Sections.Select(section =>
        {
            var available = reachable || !section.RequiresAgent;
            var choice = new SettingsSectionChoice(
                section,
                selected: available && section.CheckedByDefault,
                available)
            {
                Description = available
                    ? section.Description
                    : Ui.Validation.TheBackgroundAgentIsNotRunning
            };
            choice.SelectionChanged += SectionToggled;
            return choice;
        })];

        ExportCommand = new RelayCommand(_ => _ = ExportAsync(), _ => CanExport);
        CancelCommand = new RelayCommand(_ => Finish(null));
    }

    /// <summary>Every section, ticked or not, in catalog order.</summary>
    public ObservableCollection<SettingsSectionChoice> Sections { get; }

    /// <summary>What can never be written to a file, and why.</summary>
    public static IReadOnlyList<string> Exclusions { get; } =
    [
        $"{Ui.SettingsTransfer.SavedPasswordsAndPrivateKeys} — {Ui.SettingsTransfer.TheyAreHeldForThisWindowsAccount}",
        $"{Ui.SettingsTransfer.KeyStoreEntries} — {Ui.SettingsTransfer.EachEntryIsDerivedFromItsKey}",
        $"{Ui.SettingsTransfer.HostTrustDecisions} — {Ui.SettingsTransfer.TheseRecordWhatYouVerifiedOnThis}"
    ];

    public bool Protect
    {
        get => _protect;
        set
        {
            if (!Set(ref _protect, value)) return;
            if (!value)
            {
                // Cleared rather than kept, so an unticked box cannot leave a password behind to
                // be used by a later tick the user has forgotten about.
                _password = string.Empty;
                _confirm = string.Empty;
                Raise(nameof(Password));
                Raise(nameof(Confirm));
            }

            RaiseValidity();
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (!Set(ref _password, value ?? string.Empty)) return;
            RaiseValidity();
        }
    }

    public string Confirm
    {
        get => _confirm;
        set
        {
            if (!Set(ref _confirm, value ?? string.Empty)) return;
            RaiseValidity();
        }
    }

    /// <summary>The sections that would be written, dependencies included.</summary>
    internal IReadOnlyCollection<SettingsSectionId> SelectedSections =>
        [.. Sections.Where(section => section.Selected).Select(section => section.Id)];

    /// <summary>Why the export cannot run yet, or nothing.</summary>
    public string Problem
    {
        get
        {
            if (SelectedSections.Count == 0) return Ui.SettingsTransfer.ChooseAtLeastOneThingToExport;
            return Protect ? PasswordProblem() ?? string.Empty : string.Empty;
        }
    }

    public bool HasProblem => Problem.Length > 0;

    public bool CanExport => !_busy && !HasProblem && _files is not null;

    public StatusLine Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public ICommand ExportCommand { get; }

    public ICommand CancelCommand { get; }

    /// <summary>The file that was written, or null when the dialog was dismissed.</summary>
    internal string? ExportedPath { get; private set; }

    /// <summary>Raised once there is an answer and the window should close.</summary>
    internal event EventHandler? Closed;

    /// <summary>
    /// Asks where to put the file, captures the chosen sections, and writes it.
    /// </summary>
    /// <remarks>
    /// Capturing is asynchronous because connections, sync tasks and schedules are read from the
    /// agent over a pipe, one round trip per connection. The picker comes first so a long capture
    /// only ever happens for an export somebody has actually committed to.
    /// </remarks>
    internal async Task ExportAsync(CancellationToken cancellationToken = default)
    {
        if (_files is null || HasProblem) return;

        var path = await _files.SaveFileAsync(
            new FilePickerRequest
            {
                Title = Ui.SettingsTransfer.ExportSettings,
                Filters = SettingsTransferFiles.Filters,
                SuggestedFileName =
                    $"storagehub-settings-{_clock():yyyyMMdd}{SettingsExportSerializer.FileExtension}"
            },
            cancellationToken).ConfigureAwait(true);

        if (path is null) return;

        Busy(Ui.SettingsTransfer.CollectingSettings);
        try
        {
            var document = await _exporter
                .CaptureAsync(SelectedSections, cancellationToken).ConfigureAwait(true);
            SettingsExportService.Write(path, document, Protect ? Password : null);
            Finish(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            Status = new StatusLine(
                Ui.Format(Ui.Dialogs.ExportWriteFailedFormat, error.Message), MetricTone.Danger);
        }
        catch (Exception error) when (error is InvalidOperationException or TimeoutException)
        {
            // The agent went away between ticking the boxes and pressing Export.
            Status = new StatusLine(
                Ui.Format(Ui.Dialogs.ExportReadFailedFormat, error.Message), MetricTone.Danger);
        }
        finally
        {
            Idle();
        }
    }

    /// <summary>
    /// Keeps the ticks consistent with what a file can actually describe.
    /// </summary>
    /// <remarks>
    /// Ticking expands to the dependencies; unticking collapses whatever depended on it. Done as
    /// the user clicks so the boxes always show exactly what the file will hold, rather than an
    /// export that silently contains more, or less, than was shown.
    /// </remarks>
    private void SectionToggled(object? sender, EventArgs e)
    {
        if (_updatingSections) return;
        _updatingSections = true;
        try
        {
            var selected = sender is SettingsSectionChoice { Selected: true }
                ? SettingsSectionCatalog.ExpandForExport(SelectedSections)
                : SettingsSectionCatalog.CollapseForExport(SelectedSections);
            foreach (var section in Sections)
            {
                // A section the agent cannot reach is never ticked by an expansion; it would put
                // the dialog into a state its own validation says is impossible.
                section.SetQuietly(section.IsAvailable && selected.Contains(section.Id));
            }
        }
        finally
        {
            _updatingSections = false;
        }

        RaiseValidity();
    }

    private string? PasswordProblem() =>
        SettingsExportEnvelope.ValidatePassword(Password) ??
        (string.Equals(Password, Confirm, StringComparison.Ordinal)
            ? null
            : Ui.SettingsTransfer.TheTwoPasswordsDoNotMatch);

    private void Busy(string message)
    {
        _busy = true;
        Status = StatusLine.Muted(message);
        RaiseValidity();
    }

    private void Idle()
    {
        _busy = false;
        RaiseValidity();
    }

    private void Finish(string? path)
    {
        ExportedPath = path;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseValidity()
    {
        Raise(nameof(Problem));
        Raise(nameof(HasProblem));
        Raise(nameof(CanExport));
        (ExportCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public static string Title => Ui.SettingsTransfer.ExportSettings;

    public static string Heading => Ui.SettingsTransfer.ChooseWhatToInclude;

    public static string Hint => Ui.SettingsTransfer.ChooseWhichSettingsToWriteToA;

    public static string ExclusionsHeading => Ui.SettingsTransfer.NeverIncluded;

    public static string ProtectLabel => Ui.SettingsTransfer.ProtectThisFileWithAPassword;

    public static string PasswordLabel => Ui.SettingsTransfer.Password;

    public static string ConfirmLabel => Ui.SettingsTransfer.Confirm;

    public static string PasswordNotice =>
        Ui.SettingsTransfer.WithoutAPasswordTheFileIsReadable +
        Ui.SettingsTransfer.ALostPasswordCannotBeRecovered;

    public static string ExportLabel => Ui.SettingsTransfer.Export;

    public static string CancelLabel => Ui.SettingsTransfer.Cancel;

    public static string StatusAccessibleName => Ui.SettingsTransfer.ExportStatus;

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

/// <summary>
/// What a settings file looks like to a picker.
/// </summary>
/// <remarks>
/// <see cref="SettingsExportSerializer.FileFilter"/> is a Windows filter string, which no other
/// platform parses; the picker contract takes bare extensions instead so both dialogs ask for the
/// same thing on both platforms.
/// </remarks>
internal static class SettingsTransferFiles
{
    internal static IReadOnlyList<FilePickerFilter> Filters { get; } =
    [
        new(Ui.SettingsTransfer.SettingsFileType, [SettingsExportSerializer.FileExtension.TrimStart('.')]),
        new(Ui.KeyStore.AllFiles, ["*"])
    ];
}
