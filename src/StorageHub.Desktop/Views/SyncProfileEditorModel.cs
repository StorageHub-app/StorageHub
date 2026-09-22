using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>A saved connection, as the two location pickers list it.</summary>
/// <remarks>
/// A disabled connection is still offered: a profile may name one that was switched off since, and
/// hiding it would silently change the profile the next time it was saved.
/// </remarks>
/// <param name="ProviderName">What kind of connection it is, for the folder picker's hint.</param>
internal sealed record ConnectionChoice(Guid ConnectionId, string DisplayName, bool IsEnabled, string ProviderName = "")
{
    public string Caption => IsEnabled
        ? DisplayName
        : Ui.Format(Ui.Sync.DisabledConnectionFormat, DisplayName);

    public override string ToString() => Caption;
}

/// <summary>A saved profile, as the picker at the top lists it.</summary>
internal sealed record ProfileChoice(Guid ProfileId, string DisplayName)
{
    internal static ProfileChoice New { get; } = new(Guid.Empty, Ui.Sync.CreateNewProfile);

    public override string ToString() => DisplayName;
}

/// <summary>One of the two conflict policies, in words.</summary>
internal sealed record ConflictPolicyChoice(SyncIpcConflictPolicy Policy, string Caption)
{
    public override string ToString() => Caption;
}

/// <summary>
/// The sync profile editor, against docs/ui-reference/05-sync-profile-editor.png.
/// </summary>
/// <remarks>
/// <para>
/// The WinForms form was 1,153 lines, almost all of it placing fourteen fields by hand. The fields
/// are the easy part; what was worth carrying over is in <see cref="SyncProfileEditorController"/>
/// and <see cref="SyncProfileDraftRules"/>, and what is here is the editing itself -- which field
/// a complaint belongs to, what a new profile starts as, and what Swap does.
/// </para>
/// <para>
/// A root can be typed, or chosen with Browse from the folders of the connection it belongs to.
/// The picker is supplied by the window, so a test answers it instead.
/// </para>
/// </remarks>
internal sealed class SyncProfileEditorModel : INotifyPropertyChanged
{
    private readonly SyncProfileEditorController? _controller;
    private readonly Func<ConnectionChoice, string, string, Task<string?>>? _pickLocation;
    private SyncProfileDocument? _current;
    private ProfileChoice _selectedProfile = ProfileChoice.New;
    private ConnectionChoice? _locationA;
    private ConnectionChoice? _locationB;
    private SyncBehaviorOption _behavior;
    private ConflictPolicyChoice _conflictPolicy;
    private string _name = Ui.Sync.NewProfileTitle;
    private string _locationARoot = string.Empty;
    private string _locationBRoot = string.Empty;
    private string _includeGlobs = string.Empty;
    private string _excludeGlobs = string.Join(Environment.NewLine, DefaultExcludes);
    private bool _enabled;
    private bool _includeHiddenFiles = true;
    private int _maximumDeletionCount = SyncPresentationCatalog.DefaultMassDeleteItemLimit;
    private decimal _maximumDeletionPercentage = SyncPresentationCatalog.DefaultMassDeletePercentageLimit;
    private int _transferBufferSize = 64 * 1024;
    private bool _allowNonAtomicWrites;
    private StatusLine _status = StatusLine.Muted(Ui.Sync.NewProfileDraft);
    private string _nameProblem = string.Empty;
    private string _locationAProblem = string.Empty;
    private string _locationBProblem = string.Empty;
    private string _deletionCountProblem = string.Empty;
    private string _deletionPercentageProblem = string.Empty;
    private string _transferBufferProblem = string.Empty;
    private string _includeGlobsProblem = string.Empty;
    private string _excludeGlobsProblem = string.Empty;
    private bool _isBusy;
    private bool _suppressSelection;

    /// <summary>
    /// What a new profile excludes.
    /// </summary>
    /// <remarks>
    /// StorageHub's own state directory. Synchronising it would copy one machine's baseline over
    /// another's and make the next comparison nonsense, so it is excluded before anybody thinks
    /// about filters at all.
    /// </remarks>
    private static readonly string[] DefaultExcludes =
        [".storagehub", ".storagehub/**", "**/.storagehub/**"];

    /// <param name="controller">The agent behind the editor; none for a preview.</param>
    /// <param name="pickLocation">
    /// Shows a connection's folders and answers the one chosen, or null when dismissed. Given the
    /// connection, the root typed so far, and the location's name.
    /// </param>
    internal SyncProfileEditorModel(
        SyncProfileEditorController? controller = null,
        Func<ConnectionChoice, string, string, Task<string?>>? pickLocation = null)
    {
        _controller = controller;
        _pickLocation = pickLocation;
        _behavior = SyncBehaviorCatalog.Options.First(
            static option => option.Behavior == SyncIpcBehavior.UpdateAToB);
        _conflictPolicy = ConflictPolicies[0];

        SaveCommand = new RelayCommand(_ => _ = SaveAsync(), _ => Live && !IsBusy);
        PreviewCommand = new RelayCommand(_ => _ = PreviewAsync(), _ => Live && !IsBusy);
        RefreshCommand = new RelayCommand(_ => _ = LoadAsync(), _ => Live && !IsBusy);
        NewProfileCommand = new RelayCommand(_ => BeginNewProfile(), _ => !IsBusy);
        SwapCommand = new RelayCommand(_ => Swap(), _ => !IsBusy);
        BrowseLocationACommand = new RelayCommand(
            _ => _ = BrowseAsync(isA: true), _ => !IsBusy && _pickLocation is not null);
        BrowseLocationBCommand = new RelayCommand(
            _ => _ = BrowseAsync(isA: false), _ => !IsBusy && _pickLocation is not null);

        Profiles.Add(ProfileChoice.New);
    }

    /// <summary>An editor with nothing behind it, for a preview or a layout test.</summary>
    internal static SyncProfileEditorModel Create() => new();

    /// <summary>And one that will ask the agent.</summary>
    internal static SyncProfileEditorModel Create(
        Func<ISyncManagementAgentClient> syncClients,
        Func<IRemoteStorageAgentClient> storageClients,
        Func<ConnectionChoice, string, string, Task<string?>>? pickLocation = null) =>
        new(new SyncProfileEditorController(syncClients, storageClients), pickLocation);

    /// <summary>Raised with the run a preview produced, so the shell can show it for review.</summary>
    internal event EventHandler<SyncRunSummary>? PreviewReady;

    public ObservableCollection<ProfileChoice> Profiles { get; } = [];

    public ObservableCollection<ConnectionChoice> Connections { get; } = [];

    public ObservableCollection<SyncBehaviorOption> Behaviors { get; } =
        [.. SyncBehaviorCatalog.Options];

    public static IReadOnlyList<ConflictPolicyChoice> ConflictPolicies { get; } =
    [
        new(SyncIpcConflictPolicy.Block, Ui.Sync.BlockAndReview),
        new(SyncIpcConflictPolicy.KeepBoth, Ui.Sync.KeepBoth)
    ];

    /// <summary>
    /// Which saved profile is being edited, or the "create a new one" entry.
    /// </summary>
    /// <remarks>
    /// Choosing one loads it. The profile list carries only a summary -- not the roots, the filters
    /// or the limits -- so the editor has to read the profile in full before it can show it.
    /// </remarks>
    public ProfileChoice SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!Set(ref _selectedProfile, value) || _suppressSelection) return;
            if (value is null || value.ProfileId == Guid.Empty)
            {
                BeginNewProfile();
                return;
            }

            _ = OpenAsync(value.ProfileId);
        }
    }

    public string Name
    {
        get => _name;
        set { if (Set(ref _name, value)) NameProblem = string.Empty; }
    }

    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value);
    }

    public ConnectionChoice? LocationA
    {
        get => _locationA;
        set { if (Set(ref _locationA, value)) LocationAProblem = string.Empty; }
    }

    public string LocationARoot
    {
        get => _locationARoot;
        set { if (Set(ref _locationARoot, value)) LocationAProblem = string.Empty; }
    }

    public ConnectionChoice? LocationB
    {
        get => _locationB;
        set { if (Set(ref _locationB, value)) LocationBProblem = string.Empty; }
    }

    public string LocationBRoot
    {
        get => _locationBRoot;
        set { if (Set(ref _locationBRoot, value)) LocationBProblem = string.Empty; }
    }

    public SyncBehaviorOption Behavior
    {
        get => _behavior;
        set => Set(ref _behavior, value);
    }

    public ConflictPolicyChoice ConflictPolicy
    {
        get => _conflictPolicy;
        set => Set(ref _conflictPolicy, value);
    }

    public string IncludeGlobs
    {
        get => _includeGlobs;
        set { if (Set(ref _includeGlobs, value)) IncludeGlobsProblem = string.Empty; }
    }

    public string ExcludeGlobs
    {
        get => _excludeGlobs;
        set { if (Set(ref _excludeGlobs, value)) ExcludeGlobsProblem = string.Empty; }
    }

    public bool IncludeHiddenFiles
    {
        get => _includeHiddenFiles;
        set => Set(ref _includeHiddenFiles, value);
    }

    public int MaximumDeletionCount
    {
        get => _maximumDeletionCount;
        set { if (Set(ref _maximumDeletionCount, value)) DeletionCountProblem = string.Empty; }
    }

    public decimal MaximumDeletionPercentage
    {
        get => _maximumDeletionPercentage;
        set { if (Set(ref _maximumDeletionPercentage, value)) DeletionPercentageProblem = string.Empty; }
    }

    public int TransferBufferSize
    {
        get => _transferBufferSize;
        set { if (Set(ref _transferBufferSize, value)) TransferBufferProblem = string.Empty; }
    }

    /// <summary>
    /// Whether the agent may write straight to the destination rather than to a temporary name.
    /// </summary>
    /// <remarks>
    /// Off by default and warned about when on. It exists for providers that cannot rename, and the
    /// cost is that an interrupted transfer leaves a partial file in place of a good one.
    /// </remarks>
    public bool AllowNonAtomicWrites
    {
        get => _allowNonAtomicWrites;
        set
        {
            if (Set(ref _allowNonAtomicWrites, value)) Raise(nameof(NonAtomicWarningVisible));
        }
    }

    public bool NonAtomicWarningVisible => AllowNonAtomicWrites;

    public StatusLine Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            foreach (var command in new[]
                {
                    SaveCommand, PreviewCommand, RefreshCommand, NewProfileCommand, SwapCommand,
                    BrowseLocationACommand, BrowseLocationBCommand
                })
            {
                (command as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string NameProblem
    {
        get => _nameProblem;
        private set { if (Set(ref _nameProblem, value)) Raise(nameof(HasNameProblem)); }
    }

    public string LocationAProblem
    {
        get => _locationAProblem;
        private set { if (Set(ref _locationAProblem, value)) Raise(nameof(HasLocationAProblem)); }
    }

    public string LocationBProblem
    {
        get => _locationBProblem;
        private set { if (Set(ref _locationBProblem, value)) Raise(nameof(HasLocationBProblem)); }
    }

    public string DeletionCountProblem
    {
        get => _deletionCountProblem;
        private set { if (Set(ref _deletionCountProblem, value)) Raise(nameof(HasDeletionCountProblem)); }
    }

    public string DeletionPercentageProblem
    {
        get => _deletionPercentageProblem;
        private set
        {
            if (Set(ref _deletionPercentageProblem, value)) Raise(nameof(HasDeletionPercentageProblem));
        }
    }

    public string TransferBufferProblem
    {
        get => _transferBufferProblem;
        private set { if (Set(ref _transferBufferProblem, value)) Raise(nameof(HasTransferBufferProblem)); }
    }

    public string IncludeGlobsProblem
    {
        get => _includeGlobsProblem;
        private set { if (Set(ref _includeGlobsProblem, value)) Raise(nameof(HasIncludeGlobsProblem)); }
    }

    public string ExcludeGlobsProblem
    {
        get => _excludeGlobsProblem;
        private set { if (Set(ref _excludeGlobsProblem, value)) Raise(nameof(HasExcludeGlobsProblem)); }
    }

    public bool HasNameProblem => NameProblem.Length > 0;

    public bool HasLocationAProblem => LocationAProblem.Length > 0;

    public bool HasLocationBProblem => LocationBProblem.Length > 0;

    public bool HasDeletionCountProblem => DeletionCountProblem.Length > 0;

    public bool HasDeletionPercentageProblem => DeletionPercentageProblem.Length > 0;

    public bool HasTransferBufferProblem => TransferBufferProblem.Length > 0;

    public bool HasIncludeGlobsProblem => IncludeGlobsProblem.Length > 0;

    public bool HasExcludeGlobsProblem => ExcludeGlobsProblem.Length > 0;

    public ICommand SaveCommand { get; }

    public ICommand PreviewCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand NewProfileCommand { get; }

    public ICommand SwapCommand { get; }

    public ICommand BrowseLocationACommand { get; }

    public ICommand BrowseLocationBCommand { get; }

    /// <summary>Reads the saved profiles and the connections they can point at.</summary>
    internal async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_controller is null || IsBusy) return;
        IsBusy = true;
        try
        {
            Status = new StatusLine(Ui.Sync.LoadingProfiles);
            var workspace = await _controller.LoadAsync(cancellationToken).ConfigureAwait(true);

            Show(workspace);
            Status = new StatusLine(
                workspace.Describe(),
                workspace.Failed ? MetricTone.Danger : MetricTone.Success);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Reads one profile in full and puts it in the fields.</summary>
    internal async Task OpenAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        if (_controller is null || IsBusy) return;
        IsBusy = true;
        try
        {
            var (profile, error) = await _controller.OpenAsync(profileId, cancellationToken)
                .ConfigureAwait(true);
            if (profile is null)
            {
                Status = new StatusLine(error ?? Ui.Sync.ProfileNotFound, MetricTone.Danger);
                return;
            }

            Apply(profile);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Saves the fields as they stand.</summary>
    internal async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (_controller is null || IsBusy) return;
        IsBusy = true;
        try
        {
            Status = new StatusLine(Ui.Sync.SavingProfile);
            var result = await _controller.SaveAsync(_current, BuildDraft(), cancellationToken)
                .ConfigureAwait(true);
            Report(result.Problems, result.ErrorMessage);
            if (result.Profile is not { } saved) return;

            Adopt(saved);
            Status = saved.Draft.AllowNonAtomicDestinationWrites
                ? new StatusLine(
                    Ui.Format(Ui.Sync.ProfileSavedNonAtomicFormat, saved.Revision), MetricTone.Warning)
                : new StatusLine(
                    Ui.Format(Ui.Sync.ProfileSavedFormat, saved.Revision), MetricTone.Success);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Saves, then asks the agent to scan both locations and plan the work.
    /// </summary>
    /// <remarks>
    /// A disabled profile previews perfectly well and then never runs by itself, which is invisible
    /// once the plan is on screen. So it is said here, rather than left to be discovered when the
    /// schedule does nothing.
    /// </remarks>
    internal async Task PreviewAsync(CancellationToken cancellationToken = default)
    {
        if (_controller is null || IsBusy) return;
        IsBusy = true;
        try
        {
            Status = new StatusLine(Ui.Sync.ScanningLocations);
            var result = await _controller.PreviewAsync(_current, BuildDraft(), cancellationToken)
                .ConfigureAwait(true);
            Report(result.Problems, result.ErrorMessage);
            if (result.Profile is { } profile) Adopt(profile);
            if (result.Run is not { } run) return;

            Status = (result.Profile?.Draft.Enabled, result.Profile?.Draft.AllowNonAtomicDestinationWrites) switch
            {
                (false, _) => new StatusLine(Ui.Sync.PreviewedWhileDisabled, MetricTone.Warning),
                (_, true) => new StatusLine(Ui.Sync.PlanReadyNonAtomic, MetricTone.Warning),
                _ => new StatusLine(Ui.Sync.PlanReady, MetricTone.Success)
            };
            PreviewReady?.Invoke(this, run);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Clears the fields back to what a new profile starts as.</summary>
    internal void BeginNewProfile()
    {
        _current = null;
        Select(ProfileChoice.New);
        Name = Ui.Sync.NewProfileTitle;
        Enabled = false;
        LocationARoot = string.Empty;
        LocationBRoot = string.Empty;
        Behavior = Behaviors.First(static option => option.Behavior == SyncIpcBehavior.UpdateAToB);
        ConflictPolicy = ConflictPolicies[0];
        IncludeGlobs = string.Empty;
        ExcludeGlobs = string.Join(Environment.NewLine, DefaultExcludes);
        IncludeHiddenFiles = true;
        MaximumDeletionCount = SyncPresentationCatalog.DefaultMassDeleteItemLimit;
        MaximumDeletionPercentage = SyncPresentationCatalog.DefaultMassDeletePercentageLimit;
        TransferBufferSize = 64 * 1024;
        AllowNonAtomicWrites = false;

        // Two different connections where there are two, because a profile whose halves are the
        // same connection and the same root is the one arrangement that is always invalid.
        LocationA = Connections.FirstOrDefault();
        LocationB = Connections.Count > 1 ? Connections[1] : Connections.FirstOrDefault();

        ClearProblems();
        Status = StatusLine.Muted(Ui.Sync.NewProfileDraft);
    }

    /// <summary>
    /// Exchanges the two locations.
    /// </summary>
    /// <remarks>
    /// The behaviour is deliberately left alone. Swapping A and B under "mirror A to B" would
    /// quietly reverse which side gets deleted, and somebody who wanted that can say so.
    /// </remarks>
    internal void Swap()
    {
        (LocationA, LocationB) = (LocationB, LocationA);
        (LocationARoot, LocationBRoot) = (LocationBRoot, LocationARoot);
    }

    /// <summary>
    /// Chooses one location's root from the folders of its connection.
    /// </summary>
    /// <remarks>
    /// The connection has to be chosen first: the folders are that connection's, and a root means
    /// nothing without it.
    /// </remarks>
    internal async Task BrowseAsync(bool isA)
    {
        if (_pickLocation is null) return;
        var locationName = isA ? Ui.Sync.LocationA : Ui.Sync.LocationB;
        var connection = isA ? LocationA : LocationB;
        if (connection is null)
        {
            Status = new StatusLine(
                Ui.Format(Ui.Sync.SelectConnectionForLocationFormat, locationName), MetricTone.Danger);
            return;
        }

        var chosen = await _pickLocation(
            connection, (isA ? LocationARoot : LocationBRoot).Trim(), locationName).ConfigureAwait(true);
        if (chosen is null) return;

        if (isA) LocationARoot = chosen;
        else LocationBRoot = chosen;
        Status = new StatusLine(
            chosen.Length == 0
                ? Ui.Format(Ui.Sync.PickerUsesRootFormat, locationName, connection.DisplayName)
                : Ui.Format(Ui.Sync.PickerFolderSelectedFormat, locationName, chosen),
            MetricTone.Success);
    }

    /// <summary>The draft as the fields currently stand.</summary>
    internal SyncProfileDraftDocument BuildDraft() => new(
        Name.Trim(),
        LocationA?.ConnectionId ?? Guid.Empty,
        LocationARoot.Trim(),
        LocationB?.ConnectionId ?? Guid.Empty,
        LocationBRoot.Trim(),
        Behavior.Behavior,
        ConflictPolicy.Policy,
        ParseGlobs(IncludeGlobs),
        ParseGlobs(ExcludeGlobs),
        IncludeHiddenFiles,
        MaximumDeletionCount,
        MaximumDeletionPercentage,
        TransferBufferSize,
        Enabled)
    {
        AllowNonAtomicDestinationWrites = AllowNonAtomicWrites
    };

    private void Show(SyncProfileWorkspace workspace)
    {
        Connections.Clear();
        foreach (var connection in workspace.Connections.OrderBy(
            static connection => connection.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            Connections.Add(new ConnectionChoice(
                connection.ConnectionId,
                connection.DisplayName,
                connection.IsEnabled,
                ConnectionProviderCatalog.Get(ConnectionCardFactory.MapProvider(connection.Provider)).DisplayName));
        }

        _suppressSelection = true;
        try
        {
            Profiles.Clear();
            Profiles.Add(ProfileChoice.New);
            foreach (var profile in workspace.Profiles.OrderBy(
                static profile => profile.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                Profiles.Add(new ProfileChoice(profile.ProfileId, profile.DisplayName));
            }

            SelectedProfile = _current is { } current
                ? Profiles.FirstOrDefault(choice => choice.ProfileId == current.ProfileId) ?? ProfileChoice.New
                : ProfileChoice.New;
        }
        finally
        {
            _suppressSelection = false;
        }

        // Re-resolve the two location choices against the connections that just arrived, so a
        // loaded profile's locations survive a refresh.
        if (_current is { } loaded)
        {
            LocationA = Find(loaded.Draft.LocationAConnectionId);
            LocationB = Find(loaded.Draft.LocationBConnectionId);
        }
        else if (LocationA is null && LocationB is null)
        {
            BeginNewProfile();
        }
    }

    /// <summary>Puts a saved profile in the fields and makes it the one being edited.</summary>
    private void Apply(SyncProfileDocument profile)
    {
        Adopt(profile);
        Status = StatusLine.Muted(Ui.Format(Ui.Sync.ProfileRevisionLoadedFormat, profile.Revision));
    }

    private void Adopt(SyncProfileDocument profile)
    {
        _current = profile;
        var draft = profile.Draft;

        Name = draft.DisplayName;
        Enabled = draft.Enabled;
        LocationA = Find(draft.LocationAConnectionId);
        LocationARoot = draft.LocationARoot;
        LocationB = Find(draft.LocationBConnectionId);
        LocationBRoot = draft.LocationBRoot;
        Behavior = Behaviors.FirstOrDefault(option => option.Behavior == draft.Behavior) ?? Behavior;
        ConflictPolicy = ConflictPolicies.FirstOrDefault(
            choice => choice.Policy == draft.ConflictPolicy) ?? ConflictPolicies[0];
        IncludeGlobs = string.Join(Environment.NewLine, draft.IncludeGlobs);
        ExcludeGlobs = string.Join(Environment.NewLine, draft.ExcludeGlobs);
        IncludeHiddenFiles = draft.IncludeHiddenFiles;
        MaximumDeletionCount = draft.MaximumDeletionCount;
        MaximumDeletionPercentage = draft.MaximumDeletionPercentage;
        TransferBufferSize = draft.TransferBufferSize;
        AllowNonAtomicWrites = draft.AllowNonAtomicDestinationWrites;

        Select(Profiles.FirstOrDefault(choice => choice.ProfileId == profile.ProfileId)
            ?? Add(profile));
        ClearProblems();
    }

    /// <summary>A profile saved for the first time is not in the picker yet.</summary>
    private ProfileChoice Add(SyncProfileDocument profile)
    {
        var choice = new ProfileChoice(profile.ProfileId, profile.Draft.DisplayName);
        Profiles.Add(choice);
        return choice;
    }

    /// <summary>Selects without letting the setter treat it as somebody choosing a profile.</summary>
    private void Select(ProfileChoice choice)
    {
        _suppressSelection = true;
        try
        {
            SelectedProfile = choice;
        }
        finally
        {
            _suppressSelection = false;
        }
    }

    private ConnectionChoice? Find(Guid connectionId) =>
        Connections.FirstOrDefault(choice => choice.ConnectionId == connectionId);

    /// <summary>Puts each complaint beside the field it is about.</summary>
    private void Report(IReadOnlyList<SyncDraftProblem> problems, string? errorMessage)
    {
        ClearProblems();
        foreach (var problem in problems)
        {
            switch (problem.Field)
            {
                case SyncProfileFields.Name: NameProblem = problem.Message; break;
                case SyncProfileFields.LocationA: LocationAProblem = problem.Message; break;
                case SyncProfileFields.LocationB: LocationBProblem = problem.Message; break;
                case SyncProfileFields.DeletionCount: DeletionCountProblem = problem.Message; break;
                case SyncProfileFields.DeletionPercentage:
                    DeletionPercentageProblem = problem.Message; break;
                case SyncProfileFields.TransferBuffer: TransferBufferProblem = problem.Message; break;
                case SyncProfileFields.IncludeGlobs: IncludeGlobsProblem = problem.Message; break;
                case SyncProfileFields.ExcludeGlobs: ExcludeGlobsProblem = problem.Message; break;
            }
        }

        if (errorMessage is not null)
        {
            Status = new StatusLine(errorMessage, MetricTone.Danger);
            return;
        }

        // A problem with no field of its own still has to be visible somewhere.
        if (problems.FirstOrDefault(
            static problem => problem.Field == SyncProfileFields.Draft) is { } general)
        {
            Status = new StatusLine(general.Message, MetricTone.Danger);
        }
        else if (problems.Count > 0)
        {
            Status = new StatusLine(Ui.Sync.CompleteLocationsHint, MetricTone.Warning);
        }
    }

    private void ClearProblems()
    {
        NameProblem = string.Empty;
        LocationAProblem = string.Empty;
        LocationBProblem = string.Empty;
        DeletionCountProblem = string.Empty;
        DeletionPercentageProblem = string.Empty;
        TransferBufferProblem = string.Empty;
        IncludeGlobsProblem = string.Empty;
        ExcludeGlobsProblem = string.Empty;
    }

    /// <summary>
    /// One glob per line, blanks dropped.
    /// </summary>
    /// <remarks>
    /// A blank line is what a text box leaves behind when somebody deletes a filter, and the agent
    /// refuses an empty glob -- so a draft would become unsaveable by removing a filter, with the
    /// complaint pointing at a line that is no longer there.
    /// </remarks>
    private static string[] ParseGlobs(string text) =>
        [.. text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];

    private bool Live => _controller is not null;

    public static string Heading => Ui.Sync.EditorHeading;

    public static string Subheading => Ui.Sync.EditorSubheading;

    public static string SavedProfileLabel => Ui.Sync.SavedProfile;

    public static string NewProfileLabel => Ui.Sync.NewProfile;

    public static string ProfileNameLabel => Ui.Sync.ProfileName;

    public static string ProfileNameHint => Ui.Sync.ProfileNameHint;

    public static string EnabledLabel => Ui.Sync.Enabled;

    public static string DisabledProfilesHint => Ui.Sync.DisabledProfilesHint;

    public static string StepLocations => Ui.Sync.StepLocations;

    public static string StepBehavior => Ui.Sync.StepBehavior;

    public static string StepSafety => Ui.Sync.StepSafety;

    public static string LocationALabel => Ui.Sync.LocationA;

    public static string BrowseLabel => Ui.Sync.Browse;

    public static string BrowseLocationAAccessibleName => Ui.Sync.BrowseLocationA;

    public static string BrowseLocationBAccessibleName => Ui.Sync.BrowseLocationB;

    public static string LocationBLabel => Ui.Sync.LocationB;

    public static string ConnectionLabel => Ui.Sync.SavedConnection;

    public static string FolderLabel => Ui.Sync.FolderInsideConnection;

    public static string EmptyMeansRoot => Ui.Sync.EmptyMeansRoot;

    public static string LocationsRelativeHint => Ui.Sync.LocationsRelativeHint;

    public static string SwapLabel => Ui.Sync.SwapLocations;

    public static string BehaviorHint => Ui.Sync.BehaviorHint;

    public static string ConflictPolicyLabel => Ui.Sync.ConflictPolicy;

    public static string ConflictPolicyHint => Ui.Sync.ConflictPolicyHint;

    public static string IncludeGlobsLabel => Ui.Sync.IncludeGlobFilters;

    public static string ExcludeGlobsLabel => Ui.Sync.ExcludeGlobFilters;

    public static string GlobHint => Ui.Sync.GlobHint;

    public static string FiltersHint => Ui.Sync.FiltersHint;

    public static string HiddenContentLabel => Ui.Sync.HiddenContent;

    public static string IncludeHiddenFilesLabel => Ui.Sync.IncludeHiddenFiles;

    public static string HiddenFilesHint => Ui.Sync.HiddenFilesHint;

    public static string MaximumDeletesLabel => Ui.Sync.MaximumDeletes;

    public static string ItemLimitHint => Ui.Sync.ItemLimitHint;

    public static string MaximumPercentLabel => Ui.Sync.MaximumBaselinePercent;

    public static string PercentageLimitHint => Ui.Sync.PercentageLimitHint;

    public static string TransferBufferLabel => Ui.Sync.TransferBuffer;

    public static string TransferBufferHint => Ui.Sync.TransferBufferHint;

    public static string NonAtomicLabel => Ui.Sync.AllowNonAtomicWrites;

    public static string NonAtomicHint => Ui.Sync.AllowNonAtomicWritesCompatibility;

    public static string NonAtomicWarning => Ui.Sync.AllowNonAtomicWritesWarning;

    public static string SaveLabel => Ui.Sync.SaveProfile;

    public static string PreviewLabel => Ui.Sync.ReviewAndRun;

    public static string RefreshLabel => Ui.Sync.TasksRefresh;

    public static string ProfileStatusAccessibleName => Ui.Sync.ProfileStatusAccessibleName;

    public static string LocationAAccessibleName => Ui.Sync.LocationAFolderAccessibleName;

    public static string LocationBAccessibleName => Ui.Sync.LocationBFolderAccessibleName;

    public static string BehaviorAccessibleName => Ui.Sync.BehaviorAccessibleName;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
