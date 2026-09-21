using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;

namespace StorageHub.Desktop.Views;

/// <summary>
/// One way of resolving a name that is already taken, in the picker.
/// </summary>
/// <remarks>
/// Named apart from the sync editor's <see cref="ConflictPolicyChoice"/>, which is about two files
/// that both changed. This one is about an imported item whose name is already in use here.
/// </remarks>
internal sealed record ImportConflictChoice(SettingsConflictPolicy Policy, string Caption)
{
    public override string ToString() => Caption;
}

/// <summary>
/// Walks a settings file through the gates that decide whether it is safe to apply, shows what it
/// would change, and applies only what was ticked.
/// </summary>
/// <remarks>
/// <para>
/// Three steps in one window -- <see cref="SettingsImportStep"/> -- rather than three dialogs.
/// Nothing is written until the review step has been confirmed, so a file picked by mistake is
/// always recoverable by closing the window.
/// </para>
/// <para>
/// A wrong password and a damaged file are reported identically and leave the wizard on the first
/// step, so the password can simply be retyped. That is the import service's decision, kept here.
/// </para>
/// </remarks>
internal sealed class SettingsImportModel : INotifyPropertyChanged
{
    private readonly SettingsImportService _importer;
    private readonly IFilePickerService? _files;
    private readonly IDialogService? _dialogs;
    private SettingsImportStep _step = SettingsImportStep.ChooseFile;
    private SettingsExportDocument? _document;
    private string? _path;
    private string _fileSummary = string.Empty;
    private string _passwordPrompt = string.Empty;
    private bool _passwordPromptIsProblem;
    private bool _needsPassword;
    private bool _canOpen;
    private string _password = string.Empty;
    private string _reviewHeading = string.Empty;
    private string _concurrencyNotice = string.Empty;
    private bool _showsConflictPolicy;
    private ImportConflictChoice _conflictPolicy;
    private string _resultSummary = string.Empty;
    private bool _busy;

    internal SettingsImportModel(
        SettingsImportService importer,
        IFilePickerService? files = null,
        IDialogService? dialogs = null)
    {
        _importer = importer ?? throw new ArgumentNullException(nameof(importer));
        _files = files;
        _dialogs = dialogs;

        // Skip by default: an import should not overwrite work that is already on this machine
        // unless the user says so.
        _conflictPolicy = ConflictPolicies.First(choice => choice.Policy is SettingsConflictPolicy.Skip);

        BrowseCommand = new RelayCommand(_ => _ = PromptForFileAsync(), _ => _files is not null);
        OpenCommand = new RelayCommand(_ => OpenSelectedFile(), _ => CanOpen);
        BackCommand = new RelayCommand(_ => Step = SettingsImportStep.ChooseFile);
        ImportCommand = new RelayCommand(_ => _ = ApplyImportAsync(), _ => !_busy);
        CloseCommand = new RelayCommand(_ => Closed?.Invoke(this, EventArgs.Empty));
        ShowBackupCommand = new RelayCommand(_ => _ = ShowBackupAsync(), _ => Report?.BackupPath is not null);
    }

    public static IReadOnlyList<ImportConflictChoice> ConflictPolicies { get; } =
    [
        new(SettingsConflictPolicy.Skip, Ui.SettingsTransfer.LeaveWhatIsAlreadyHereAlone),
        new(SettingsConflictPolicy.Replace, Ui.SettingsTransfer.ReplaceWhatIsAlreadyHere),
        new(SettingsConflictPolicy.ImportAsCopy, Ui.SettingsTransfer.KeepBothImportingAsACopy)
    ];

    /// <summary>What the file holds, section by section, once one has been opened.</summary>
    public ObservableCollection<SettingsSectionChoice> Sections { get; } = [];

    /// <summary>Which step is showing.</summary>
    public SettingsImportStep Step
    {
        get => _step;
        private set
        {
            if (!Set(ref _step, value)) return;
            Raise(nameof(IsChoosingFile));
            Raise(nameof(IsReviewing));
            Raise(nameof(IsShowingResult));
        }
    }

    public bool IsChoosingFile => Step is SettingsImportStep.ChooseFile;

    public bool IsReviewing => Step is SettingsImportStep.Review;

    public bool IsShowingResult => Step is SettingsImportStep.Result;

    /// <summary>The chosen file's name, size, and whether it is sealed.</summary>
    public string FileSummary
    {
        get => _fileSummary;
        private set => Set(ref _fileSummary, value);
    }

    /// <summary>The prompt above the password box, which doubles as where a refusal is said.</summary>
    public string PasswordPrompt
    {
        get => _passwordPrompt;
        private set => Set(ref _passwordPrompt, value);
    }

    /// <summary>Whether that prompt is currently a refusal rather than an instruction.</summary>
    public bool PasswordPromptIsProblem
    {
        get => _passwordPromptIsProblem;
        private set => Set(ref _passwordPromptIsProblem, value);
    }

    public bool HasPasswordPrompt => PasswordPrompt.Length > 0;

    /// <summary>Whether the chosen file is sealed, and so whether a password is offered at all.</summary>
    public bool NeedsPassword
    {
        get => _needsPassword;
        private set => Set(ref _needsPassword, value);
    }

    public string Password
    {
        get => _password;
        set => Set(ref _password, value ?? string.Empty);
    }

    /// <summary>Whether there is a readable file to open. False while none is chosen.</summary>
    public bool CanOpen
    {
        get => _canOpen;
        private set
        {
            if (!Set(ref _canOpen, value)) return;
            (OpenCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    /// <summary>When the file was written, by what, and whether on this computer.</summary>
    public string ReviewHeading
    {
        get => _reviewHeading;
        private set => Set(ref _reviewHeading, value);
    }

    /// <summary>Whether this import needs the agent restarted, said before it is applied.</summary>
    public string ConcurrencyNotice
    {
        get => _concurrencyNotice;
        private set
        {
            if (!Set(ref _concurrencyNotice, value)) return;
            Raise(nameof(HasConcurrencyNotice));
        }
    }

    public bool HasConcurrencyNotice => ConcurrencyNotice.Length > 0;

    /// <summary>Whether the file carries anything the agent owns, which is what can conflict.</summary>
    public bool ShowsConflictPolicy
    {
        get => _showsConflictPolicy;
        private set => Set(ref _showsConflictPolicy, value);
    }

    public ImportConflictChoice ConflictPolicy
    {
        get => _conflictPolicy;
        set
        {
            if (value is null) return;
            Set(ref _conflictPolicy, value);
        }
    }

    /// <summary>What the import did, laid out as lines.</summary>
    public string ResultSummary
    {
        get => _resultSummary;
        private set => Set(ref _resultSummary, value);
    }

    public bool HasBackup => Report?.BackupPath is not null;

    public ICommand BrowseCommand { get; }

    public ICommand OpenCommand { get; }

    public ICommand BackCommand { get; }

    public ICommand ImportCommand { get; }

    public ICommand CloseCommand { get; }

    public ICommand ShowBackupCommand { get; }

    /// <summary>What the import did, once it has run.</summary>
    internal SettingsImportReport? Report { get; private set; }

    /// <summary>Whether anything actually changed, which is what tells the shell to refresh.</summary>
    internal bool Changed => Report is { Applied.Count: > 0 } report && report.Succeeded;

    /// <summary>Raised once the wizard is finished with and the window should close.</summary>
    internal event EventHandler? Closed;

    /// <summary>Asks which file to import, and inspects whatever was chosen.</summary>
    internal async Task<bool> PromptForFileAsync(CancellationToken cancellationToken = default)
    {
        if (_files is null) return false;

        var path = await _files.PickFileAsync(
            new FilePickerRequest
            {
                Title = Ui.SettingsTransfer.ImportSettings,
                Filters = SettingsTransferFiles.Filters
            },
            cancellationToken).ConfigureAwait(true);

        if (path is null) return false;
        LoadFile(path);
        return true;
    }

    /// <summary>
    /// Inspects a file and says what it is, without opening it.
    /// </summary>
    /// <remarks>
    /// Only the first bytes are read, which is what decides whether a password is worth asking
    /// for. A file that is not an export at all is refused here, before a password is requested
    /// for something that could never be opened with one.
    /// </remarks>
    internal void LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        var inspection = SettingsImportService.Inspect(path);
        var name = Path.GetFileName(path);
        if (inspection.Kind is SettingsFileKind.Unreadable)
        {
            FileSummary = $"{name}{Environment.NewLine}{Environment.NewLine}" +
                (inspection.Message ?? Ui.SettingsTransfer.ThatFileCouldNotBeRead);
            NeedsPassword = false;
            SetPrompt(string.Empty, isProblem: false);
            CanOpen = false;
            Step = SettingsImportStep.ChooseFile;
            return;
        }

        FileSummary =
            $"{name}{Environment.NewLine}{inspection.SizeBytes:N0} bytes{Environment.NewLine}{Environment.NewLine}" +
            (inspection.Kind is SettingsFileKind.PasswordProtected
                ? Ui.SettingsTransfer.ThisFileIsProtectedWithAPassword
                : Ui.SettingsTransfer.ThisFileIsNotPasswordProtected);
        NeedsPassword = inspection.Kind is SettingsFileKind.PasswordProtected;
        SetPrompt(NeedsPassword ? Ui.SettingsTransfer.EnterTheFileSPassword : string.Empty, isProblem: false);
        CanOpen = true;
        Step = SettingsImportStep.ChooseFile;
    }

    /// <summary>
    /// Opens the chosen file and moves to the review, or explains why it could not be opened.
    /// </summary>
    /// <remarks>
    /// A refusal stays on this step so the password can simply be retyped. A wrong password and a
    /// damaged file read the same, deliberately.
    /// </remarks>
    internal void OpenSelectedFile()
    {
        if (_path is null) return;
        var result = SettingsImportService.Open(_path, NeedsPassword ? Password : null);
        if (result.Document is null)
        {
            SetPrompt(result.Message ?? Ui.SettingsTransfer.ThatFileCouldNotBeOpened, isProblem: true);
            return;
        }

        _document = result.Document;
        BuildReview(_document);
        Step = SettingsImportStep.Review;
    }

    /// <summary>Applies what is ticked, then shows what happened.</summary>
    internal async Task ApplyImportAsync(CancellationToken cancellationToken = default)
    {
        if (_document is null || _busy) return;
        _busy = true;
        (ImportCommand as RelayCommand)?.RaiseCanExecuteChanged();
        try
        {
            Report = await _importer.ApplyAsync(
                _document,
                [.. Sections.Where(section => section is { Selected: true, IsAvailable: true })
                    .Select(section => section.Id)],
                ConflictPolicy.Policy,
                cancellationToken).ConfigureAwait(true);
            ResultSummary = Describe(Report);
            Raise(nameof(HasBackup));
            (ShowBackupCommand as RelayCommand)?.RaiseCanExecuteChanged();
            Step = SettingsImportStep.Result;
        }
        finally
        {
            _busy = false;
            (ImportCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Describes what the file holds, section by section.
    /// </summary>
    /// <remarks>
    /// A section the file does not carry is shown and dimmed rather than hidden, so the list is
    /// the same shape as the export dialog's and says plainly what was left out of the file.
    /// Ticked only where the file has something and the section is on by default, so "this
    /// computer only" still has to be asked for even when the file carries it.
    /// </remarks>
    private void BuildReview(SettingsExportDocument document)
    {
        var present = document.PresentSections();
        ReviewHeading =
            $"Exported {document.CreatedUtc.ToLocalTime():g} by {document.Application}{Environment.NewLine}" +
            (SettingsExportFingerprint.MatchesThisMachine(document.MachineFingerprint)
                ? Ui.SettingsTransfer.WrittenOnThisComputer
                : Ui.SettingsTransfer.WrittenOnAnotherComputer);

        Sections.Clear();
        foreach (var section in SettingsSectionCatalog.Sections)
        {
            var inFile = present.Contains(section.Id);
            Sections.Add(new SettingsSectionChoice(
                section,
                selected: inFile && section.CheckedByDefault,
                available: inFile)
            {
                Description = inFile
                    ? DescribeContents(document, section)
                    : Ui.SettingsTransfer.NotInThisFile
            });
        }

        ShowsConflictPolicy = SettingsSectionCatalog.Sections
            .Any(section => section.RequiresAgent && present.Contains(section.Id));
        ConcurrencyNotice = document.DesktopGeneral is not null
            ? Ui.SettingsTransfer.TransferConcurrencyChangesTakeEffectAfterThe
            : string.Empty;
    }

    private static string DescribeContents(SettingsExportDocument document, SettingsSectionDefinition section)
    {
        var count = document.CountIn(section.Id);
        return section.Id switch
        {
            SettingsSectionId.DesktopGeneral =>
                Ui.SettingsTransfer.ReplacesAppearanceConcurrencyEditingAndUpdatePreferences,
            SettingsSectionId.MachineSpecific =>
                Ui.SettingsTransfer.ReplacesTheEditorPathAndThePinned,
            SettingsSectionId.Shortcuts => $"Replaces {count} keyboard shortcuts.",
            SettingsSectionId.ConnectionDefaults => $"Replaces {count} connection default values.",
            _ => count == 1 ? "1 item." : $"{count} items."
        };
    }

    /// <summary>What the import did, as the result screen reads it out.</summary>
    private static string Describe(SettingsImportReport report)
    {
        var lines = new List<string>();
        if (report.Failure is { } failure)
        {
            lines.Add(failure);
        }
        else if (report.Applied.Count == 0)
        {
            lines.Add(Ui.SettingsTransfer.NothingWasImported);
        }
        else
        {
            lines.Add(Ui.SettingsTransfer.Imported);
            lines.AddRange(report.Applied
                .OrderBy(id => (int)id)
                .Select(id => $"    {SettingsSectionCatalog.Get(id).Label}"));
        }

        if (report.Blocked.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add(Ui.SettingsTransfer.NotImported);
            lines.AddRange(report.Blocked
                .OrderBy(pair => (int)pair.Key)
                .Select(pair => $"    {SettingsSectionCatalog.Get(pair.Key).Label} — {pair.Value}"));
        }

        if (report.Agent is { Count: > 0 } agent)
        {
            foreach (var (title, items) in new[]
            {
                (Ui.SettingsTransfer.Connections, agent.Connections),
                (Ui.SettingsTransfer.SyncTasks, agent.SyncProfiles),
                (Ui.SettingsTransfer.Schedules, agent.Schedules)
            })
            {
                if (items.Count == 0) continue;
                lines.Add(string.Empty);
                lines.Add($"{title}:");
                lines.AddRange(items.Select(item => $"    {item.Name} — {item.Result}"));
            }
        }

        if (report.NeedsCredentials.Count > 0)
        {
            // Said plainly here rather than left to be discovered the first time one of these
            // fails to connect.
            lines.Add(string.Empty);
            lines.Add(Ui.SettingsTransfer.TheseConnectionsNeedTheirCredentialsEnteredBefore);
            lines.AddRange(report.NeedsCredentials.Select(name => $"    {name}"));
        }

        if (report.ConcurrencyChanged)
        {
            lines.Add(string.Empty);
            lines.Add(Ui.SettingsTransfer.TransferConcurrencyChangedTheBackgroundAgentRestarts);
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Says where the backup went.
    /// </summary>
    /// <remarks>
    /// The WinForms version selected it in Explorer, which has no cross-platform equivalent worth
    /// having: the portals that could do it are not present on every desktop this now runs on. The
    /// path is shown instead, which is what the Explorer call fell back to anyway.
    /// </remarks>
    private async Task ShowBackupAsync(CancellationToken cancellationToken = default)
    {
        if (Report?.BackupPath is not { } path || _dialogs is null) return;
        await _dialogs.ShowAsync(
            new DialogRequest
            {
                Title = Ui.Dialogs.BackupLocationCaption,
                Message = BackupLinkLabel,
                // The path is the detail rather than the message: it is a timestamp and a guid,
                // and drawn quieter it can wrap without pushing the sentence off the top.
                Detail = path
            },
            cancellationToken).ConfigureAwait(true);
    }

    private void SetPrompt(string text, bool isProblem)
    {
        PasswordPrompt = text;
        PasswordPromptIsProblem = isProblem;
        Raise(nameof(HasPasswordPrompt));
    }

    public static string Title => Ui.SettingsTransfer.ImportSettings;

    public static string Hint => Ui.SettingsTransfer.ReviewASettingsFileAndChooseWhat;

    public static string BrowseLabel => Ui.KeyStore.Browse;

    public static string OpenLabel => Ui.Settings.ImportOpen;

    public static string BackLabel => Ui.Settings.ImportBack;

    public static string ImportLabel => Ui.SettingsTransfer.Import;

    public static string CancelLabel => Ui.SettingsTransfer.Cancel;

    public static string CloseLabel => Ui.Dialogs.ButtonClose;

    public static string ConflictLabel => Ui.SettingsTransfer.ConnectionsAndTasksThatAlreadyExistHere;

    public static string ConflictAccessibleName => Ui.SettingsTransfer.WhatToDoAboutConnectionsAndTasks;

    public static string BackupLinkLabel => Ui.SettingsTransfer.ShowTheBackupOfYourPreviousSettings;

    public static string FileAccessibleName => Ui.SettingsTransfer.SelectedFile;

    public static string PasswordAccessibleName => Ui.SettingsTransfer.FilePassword;

    public static string SummaryAccessibleName => Ui.SettingsTransfer.FileSummary;

    public static string ResultAccessibleName => Ui.SettingsTransfer.ImportResult;

    public static string NoticeAccessibleName => Ui.SettingsTransfer.AgentRestartNotice;

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
