using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;

namespace StorageHub.Desktop.Views;

/// <summary>A row of the saved schedules table.</summary>
internal sealed record ScheduleRow(
    Guid ScheduleId,
    string Profile,
    string Recurrence,
    string NextRun,
    string State,
    string LastOutcome);

/// <summary>One frequency, in the picker.</summary>
internal sealed record FrequencyChoice(ScheduleFrequency Frequency, string Caption)
{
    public override string ToString() => Caption;
}

/// <summary>One time zone, named the way an operating system names it.</summary>
internal sealed record TimeZoneChoice(string Id, string Caption)
{
    internal static TimeZoneChoice Unavailable(string id) =>
        new(id, Ui.Format(Ui.Schedules.UnavailableRegionFormat, id));

    public override string ToString() => Caption;
}

/// <summary>
/// One weekday, named in the shell's language.
/// </summary>
/// <remarks>
/// A record rather than the enum itself. 1.x bound <see cref="System.DayOfWeek"/> values straight
/// into a combo box, which calls <c>ToString</c> on them -- so a Danish shell offered "Monday"
/// through "Sunday" while every other list beside it was translated.
/// </remarks>
internal sealed record DayChoice(DayOfWeek Day, string Caption)
{
    public override string ToString() => Caption;
}

/// <summary>One execution mode, with the sentence that says what it means.</summary>
internal sealed record ExecutionModeChoice(
    ScheduleIpcExecutionMode Mode,
    string Caption,
    string Notice)
{
    public override string ToString() => Caption;
}

/// <summary>
/// The schedule manager, against docs/ui-reference/06-schedules.png.
/// </summary>
/// <remarks>
/// <para>
/// A list on the left and one schedule's settings on the right, out of 1,111 lines of WinForms.
/// The recurrence is chosen rather than typed: <see cref="ScheduleRecurrence"/> turns a frequency
/// and a time into the cron the agent stores, and reads one back, so the cron box is there for the
/// expressions no preset covers and nothing else.
/// </para>
/// <para>
/// Deleting confirms, and disabling does not. A schedule that has been switched off can be switched
/// on again from the same screen; a deleted one cannot, and its history is the only record left
/// that it ever ran.
/// </para>
/// </remarks>
internal sealed class ScheduleManagerModel : INotifyPropertyChanged
{
    private readonly ScheduleManagerController? _controller;
    private readonly IDialogService? _dialogs;
    private ScheduleDocument? _current;
    private ScheduleRow? _selectedRow;
    private ProfileChoice? _profile;
    private FrequencyChoice _frequency;
    private TimeZoneChoice? _timeZone;
    private ExecutionModeChoice _executionMode;
    private DayChoice _dayOfWeek;
    private int _dayOfMonth = 1;
    private TimeSpan _timeOfDay = TimeSpan.FromHours(2);
    private string _cronExpression = "0 2 * * *";
    private int _misfireGraceMinutes = 15;
    private bool _queueOneWhileRunning = true;
    private bool _enabled;
    private StatusLine _status = StatusLine.Muted(Ui.Schedules.NewScheduleDraft);
    private bool _isBusy;

    internal ScheduleManagerModel(
        ScheduleManagerController? controller = null,
        IDialogService? dialogs = null)
    {
        _controller = controller;
        _dialogs = dialogs;
        _frequency = Frequencies[0];
        _executionMode = ExecutionModes[0];
        _dayOfWeek = Day(System.DayOfWeek.Monday);
        _timeZone = LocalZone();

        RefreshCommand = new RelayCommand(_ => _ = LoadAsync(), _ => Live && !IsBusy);
        SaveCommand = new RelayCommand(_ => _ = SaveAsync(), _ => Live && !IsBusy);
        NewScheduleCommand = new RelayCommand(_ => BeginNewSchedule(), _ => !IsBusy);
        ToggleCommand = new RelayCommand(
            _ => _ = SetEnabledAsync(!(_current?.Enabled ?? false)),
            _ => Live && !IsBusy && _current is not null);
        DeleteCommand = new RelayCommand(
            _ => _ = DeleteAsync(), _ => Live && !IsBusy && _current is not null);
    }

    /// <summary>A manager with nothing behind it, for a preview or a layout test.</summary>
    internal static ScheduleManagerModel Create() => new();

    /// <summary>And one that will ask the agent.</summary>
    internal static ScheduleManagerModel Create(
        Func<IScheduleManagementAgentClient> scheduleClients,
        Func<ISyncManagementAgentClient> syncClients,
        IDialogService? dialogs = null) =>
        new(new ScheduleManagerController(scheduleClients, syncClients), dialogs);

    public ObservableCollection<ScheduleRow> Schedules { get; } = [];

    public ObservableCollection<ProfileChoice> Profiles { get; } = [];

    public static IReadOnlyList<FrequencyChoice> Frequencies { get; } =
        [.. Enum.GetValues<ScheduleFrequency>()
            .Select(static frequency => new FrequencyChoice(
                frequency, ScheduleRecurrence.Name(frequency)))];

    /// <summary>
    /// Every time zone the machine knows, by its region name.
    /// </summary>
    /// <remarks>
    /// The region is what the agent stores, not an offset: an offset is right until the clocks
    /// change, and a nightly sync at two in the morning should stay at two in the morning. The
    /// current offset is shown beside it because that is what makes a region recognisable.
    /// </remarks>
    public static IReadOnlyList<TimeZoneChoice> TimeZones { get; } =
        [.. TimeZoneInfo.GetSystemTimeZones()
            .Select(static zone => new TimeZoneChoice(zone.Id, $"{zone.Id} · UTC{Offset(zone)}"))
            .OrderBy(static zone => zone.Id, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>
    /// A zone's standard offset, as "+01:00".
    /// </summary>
    /// <remarks>
    /// Written out rather than handed to a format string: TimeSpan's custom formats have no
    /// positive/negative sections -- that is a numeric-format feature -- and asking for one throws
    /// a FormatException from inside a static initializer, where it surfaces as the entire screen
    /// failing to construct rather than as one wrong label.
    /// </remarks>
    private static string Offset(TimeZoneInfo zone)
    {
        var offset = zone.BaseUtcOffset;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(offset < TimeSpan.Zero ? '-' : '+')}{Math.Abs(offset.Hours):00}:{Math.Abs(offset.Minutes):00}");
    }

    /// <summary>The seven days, in the order a week is read in and in the shell's language.</summary>
    public static IReadOnlyList<DayChoice> Days { get; } =
        [.. Enum.GetValues<DayOfWeek>()
            .Select(static day => new DayChoice(day, UiEnumNames.Describe(day)))];

    public static IReadOnlyList<ExecutionModeChoice> ExecutionModes { get; } =
    [
        new(ScheduleIpcExecutionMode.SafeAutomatic,
            Ui.Schedules.SafeAutomatic, Ui.Schedules.SafeAutomaticDescription),
        new(ScheduleIpcExecutionMode.PreviewOnly,
            Ui.Schedules.ReviewOnly, Ui.Schedules.ReviewOnlyDescription)
    ];

    /// <summary>The schedule in hand. Choosing one fills the editor beside the list.</summary>
    public ScheduleRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (!Set(ref _selectedRow, value) || value is null) return;
            Open(value.ScheduleId);
        }
    }

    public ProfileChoice? Profile
    {
        get => _profile;
        set => Set(ref _profile, value);
    }

    public FrequencyChoice Frequency
    {
        get => _frequency;
        set
        {
            if (!Set(ref _frequency, value)) return;
            RaiseRecurrence();
        }
    }

    public TimeSpan TimeOfDay
    {
        get => _timeOfDay;
        set { if (Set(ref _timeOfDay, value)) RaiseRecurrence(); }
    }

    public DayChoice DayOfWeek
    {
        get => _dayOfWeek;
        set { if (Set(ref _dayOfWeek, value)) RaiseRecurrence(); }
    }

    public int DayOfMonth
    {
        get => _dayOfMonth;
        set { if (Set(ref _dayOfMonth, value)) RaiseRecurrence(); }
    }

    /// <summary>The cron the agent will store, editable only under the custom frequency.</summary>
    public string CronExpression
    {
        get => _cronExpression;
        set { if (Set(ref _cronExpression, value)) RaiseRecurrence(); }
    }

    public TimeZoneChoice? TimeZone
    {
        get => _timeZone;
        set => Set(ref _timeZone, value);
    }

    public ExecutionModeChoice ExecutionMode
    {
        get => _executionMode;
        set
        {
            if (!Set(ref _executionMode, value)) return;
            Raise(nameof(ExecutionNotice));
        }
    }

    /// <summary>
    /// What the chosen execution mode means, and how much it should stand out.
    /// </summary>
    /// <remarks>
    /// Safe automatic runs without anybody present. Review only queues a plan and waits. The
    /// difference is the whole safety story of scheduling, and it is one drop-down apart.
    /// </remarks>
    public StatusLine ExecutionNotice => new(
        ExecutionMode.Notice,
        ExecutionMode.Mode == ScheduleIpcExecutionMode.SafeAutomatic
            ? MetricTone.Success
            : MetricTone.Warning);

    public int MisfireGraceMinutes
    {
        get => _misfireGraceMinutes;
        set => Set(ref _misfireGraceMinutes, value);
    }

    public bool QueueOneWhileRunning
    {
        get => _queueOneWhileRunning;
        set => Set(ref _queueOneWhileRunning, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value);
    }

    /// <summary>What the recurrence does, in a sentence, as it is being built.</summary>
    public string RecurrenceSummary => BuildRecurrence().Describe();

    public bool ShowsTime => Frequency.Frequency != ScheduleFrequency.Custom;

    public bool ShowsWeekDay => Frequency.Frequency == ScheduleFrequency.Weekly;

    public bool ShowsMonthDay => Frequency.Frequency == ScheduleFrequency.Monthly;

    public bool ShowsCron => Frequency.Frequency == ScheduleFrequency.Custom;

    /// <summary>What the toggle button says, which depends on what the schedule is now.</summary>
    public string ToggleLabel => _current?.Enabled == true
        ? Ui.Schedules.Disable
        : Ui.Schedules.Enable;

    /// <summary>Whether the agent is running this schedule right now.</summary>
    public bool IsBusyRun => _current?.IsBusy == true;

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
                { RefreshCommand, SaveCommand, NewScheduleCommand, ToggleCommand, DeleteCommand })
            {
                (command as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public ICommand RefreshCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand NewScheduleCommand { get; }

    public ICommand ToggleCommand { get; }

    public ICommand DeleteCommand { get; }

    /// <summary>Reads the saved schedules and the profiles they can name.</summary>
    internal async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_controller is null || IsBusy) return;
        IsBusy = true;
        try
        {
            Status = new StatusLine(Ui.Schedules.LoadingSchedules);
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

    /// <summary>Saves the editor as it stands, creating or updating.</summary>
    internal async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (_controller is null || IsBusy) return;
        if (Profile is null || Profile.ProfileId == Guid.Empty)
        {
            Status = new StatusLine(Ui.Schedules.OnlySavedProfiles, MetricTone.Warning);
            return;
        }

        IsBusy = true;
        try
        {
            Status = new StatusLine(Ui.Schedules.SavingSchedule);
            var result = await _controller.SaveAsync(_current, BuildDraft(), cancellationToken)
                .ConfigureAwait(true);
            if (!Adopt(result)) return;

            Status = new StatusLine(
                ExecutionMode.Mode == ScheduleIpcExecutionMode.SafeAutomatic
                    ? Ui.Schedules.SavedSafeAutomatic
                    : Ui.Schedules.SavedReviewOnly,
                MetricTone.Success);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Turns the loaded schedule on or off.</summary>
    internal async Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (_controller is null || IsBusy || _current is not { } current) return;
        IsBusy = true;
        try
        {
            Status = new StatusLine(
                enabled ? Ui.Schedules.EnablingSchedule : Ui.Schedules.DisablingSchedule);
            var result = await _controller.SetEnabledAsync(current, enabled, cancellationToken)
                .ConfigureAwait(true);
            if (!Adopt(result)) return;

            Status = new StatusLine(
                enabled ? Ui.Schedules.ScheduleEnabled : Ui.Schedules.ScheduleDisabled,
                MetricTone.Success);
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
    /// Confirms, then deletes the loaded schedule.
    /// </summary>
    /// <remarks>
    /// Disabling is the reversible version of this and is one button away, so the confirmation says
    /// so. The run history survives: it is the only record that the schedule ever ran.
    /// </remarks>
    internal async Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        if (_controller is null || IsBusy || _current is not { } current) return;

        if (_dialogs is null)
        {
            Status = new StatusLine(Ui.Schedules.SelectScheduleFirst, MetricTone.Warning);
            return;
        }

        var choice = await _dialogs.ConfirmAsync(
            new DialogRequest
            {
                Title = Ui.Schedules.Delete,
                Message = Ui.Format(Ui.Schedules.DisabledProfileFormat, current.ProfileDisplayName),
                Detail = Ui.Schedules.ScheduleDeleted,
                Severity = DialogSeverity.Warning,
                Buttons = DialogButtons.YesNo,
                Default = DialogChoice.No
            },
            cancellationToken).ConfigureAwait(true);
        if (choice != DialogChoice.Yes) return;

        IsBusy = true;
        try
        {
            Status = new StatusLine(Ui.Schedules.DeletingSchedule);
            var result = await _controller.DeleteAsync(current, cancellationToken)
                .ConfigureAwait(true);
            if (!result.Changed)
            {
                Status = new StatusLine(result.ErrorMessage!, MetricTone.Danger);
                return;
            }

            Remove(current.ScheduleId);
            BeginNewSchedule();
            Status = new StatusLine(Ui.Schedules.ScheduleDeleted, MetricTone.Success);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Clears the editor back to what a new schedule starts as.</summary>
    internal void BeginNewSchedule()
    {
        _current = null;
        SelectedRow = null;
        Apply(ScheduleRecurrence.Default);
        Profile = Profiles.FirstOrDefault();
        TimeZone = LocalZone();
        ExecutionMode = ExecutionModes[0];
        MisfireGraceMinutes = 15;
        QueueOneWhileRunning = true;
        Enabled = false;
        RaiseCurrent();
        Status = StatusLine.Muted(Ui.Schedules.NewScheduleDraft);
    }

    /// <summary>The draft as the editor currently stands.</summary>
    internal ScheduleDraftDocument BuildDraft() => new(
        Profile?.ProfileId ?? Guid.Empty,
        BuildRecurrence().ToCron(),
        TimeZone?.Id ?? string.Empty,
        checked(MisfireGraceMinutes * 60),
        QueueOneWhileRunning,
        Enabled,
        ExecutionMode.Mode);

    /// <summary>The recurrence the builder currently describes.</summary>
    internal ScheduleRecurrence BuildRecurrence() => new(
        Frequency.Frequency,
        TimeOfDay.Hours,
        TimeOfDay.Minutes,
        DayOfWeek.Day,
        DayOfMonth,
        CronExpression);

    private void Show(ScheduleWorkspace workspace)
    {
        Profiles.Clear();
        foreach (var profile in workspace.Profiles.OrderBy(
            static profile => profile.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            Profiles.Add(new ProfileChoice(profile.ProfileId, profile.DisplayName));
        }

        Schedules.Clear();
        foreach (var schedule in workspace.Schedules.OrderBy(
            static schedule => schedule.ProfileDisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            Schedules.Add(Row(schedule));
        }

        if (_current is null) BeginNewSchedule();
    }

    /// <summary>Puts a schedule in the editor and makes it the one being changed.</summary>
    private void Open(Guid scheduleId)
    {
        if (_schedulesById.TryGetValue(scheduleId, out var schedule)) Load(schedule);
    }

    private readonly Dictionary<Guid, ScheduleDocument> _schedulesById = [];

    private void Load(ScheduleDocument schedule)
    {
        _current = schedule;
        Apply(ScheduleRecurrence.Parse(schedule.CronExpression));
        Profile = KnownProfile(schedule);
        TimeZone = TimeZones.FirstOrDefault(zone => zone.Id == schedule.TimeZoneId)
            ?? TimeZoneChoice.Unavailable(schedule.TimeZoneId);
        ExecutionMode = ExecutionModes.FirstOrDefault(mode => mode.Mode == schedule.ExecutionMode)
            ?? ExecutionModes[0];
        MisfireGraceMinutes = Math.Max(1, schedule.MisfireGraceSeconds / 60);
        QueueOneWhileRunning = schedule.QueueOneWhileRunning;
        Enabled = schedule.Enabled;
        RaiseCurrent();
        Status = StatusLine.Muted(Describe(schedule));
    }

    private void Apply(ScheduleRecurrence recurrence)
    {
        Frequency = Frequencies.First(choice => choice.Frequency == recurrence.Frequency);
        TimeOfDay = new TimeSpan(recurrence.Hour, recurrence.Minute, 0);
        DayOfWeek = Day(recurrence.DayOfWeek);
        DayOfMonth = recurrence.DayOfMonth;

        // Kept as written even when a preset describes it, so switching to Custom shows the
        // expression that is actually stored rather than one rebuilt from the preset.
        CronExpression = recurrence.CustomExpression.Length > 0
            ? recurrence.CustomExpression
            : recurrence.ToCron();
        RaiseRecurrence();
    }

    /// <summary>Takes the agent's answer, or reports why there was not one.</summary>
    private bool Adopt(ScheduleChangeResult result)
    {
        if (!result.Changed)
        {
            Status = new StatusLine(result.ErrorMessage!, MetricTone.Danger);
            return false;
        }

        if (result.Schedule is { } schedule)
        {
            Upsert(schedule);
            Load(schedule);
        }

        return true;
    }

    private void Upsert(ScheduleDocument schedule)
    {
        _schedulesById[schedule.ScheduleId] = schedule;
        var row = Row(schedule);
        var existing = Schedules.FirstOrDefault(entry => entry.ScheduleId == schedule.ScheduleId);
        if (existing is null)
        {
            Schedules.Add(row);
            return;
        }

        Schedules[Schedules.IndexOf(existing)] = row;
    }

    private void Remove(Guid scheduleId)
    {
        _schedulesById.Remove(scheduleId);
        if (Schedules.FirstOrDefault(entry => entry.ScheduleId == scheduleId) is { } row)
        {
            Schedules.Remove(row);
        }
    }

    private ScheduleRow Row(ScheduleDocument schedule)
    {
        _schedulesById[schedule.ScheduleId] = schedule;
        return new ScheduleRow(
            schedule.ScheduleId,
            schedule.ProfileDisplayName,
            ScheduleRecurrence.Parse(schedule.CronExpression).Describe(),
            NextRun(schedule),
            schedule.IsBusy
                ? Ui.Schedules.RunActive
                : schedule.Enabled ? Ui.Schedules.Enabled : Ui.Sync.TaskDisabled,
            Describe(schedule));
    }

    /// <summary>
    /// When the schedule next runs, in the reader's own zone.
    /// </summary>
    /// <remarks>
    /// The agent computes it after a save or an enable, so before either there is nothing to show
    /// and saying "not scheduled until saved and enabled" is more use than an empty cell.
    /// </remarks>
    private static string NextRun(ScheduleDocument schedule) => schedule.NextOccurrenceUtc switch
    {
        { } next => next.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
        _ when !schedule.Enabled => Ui.Schedules.NotScheduledUntilSaved,
        _ => Ui.Schedules.NoFutureRun
    };

    private static string Describe(ScheduleDocument schedule) => schedule.LastRunOutcome switch
    {
        { Length: > 0 } outcome => Ui.Format(
            Ui.Schedules.IdleOutcomeFormat,
            outcome,
            schedule.LastErrorCode is { Length: > 0 } code ? $" · {code}" : string.Empty),
        _ => Ui.Schedules.IdleNoOutcome
    };

    private void RaiseRecurrence()
    {
        Raise(nameof(RecurrenceSummary));
        Raise(nameof(ShowsTime));
        Raise(nameof(ShowsWeekDay));
        Raise(nameof(ShowsMonthDay));
        Raise(nameof(ShowsCron));
    }

    private void RaiseCurrent()
    {
        Raise(nameof(ToggleLabel));
        Raise(nameof(IsBusyRun));
        (ToggleCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DeleteCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// The profile picker entry for a schedule, adding it to the list if it is not already there.
    /// </summary>
    /// <remarks>
    /// A schedule can name a profile the list does not have -- one deleted since, or a profile list
    /// that failed to load while the schedules loaded fine. A choice that is not in the collection
    /// cannot be shown by a combo box, so the picker went blank and the schedule appeared to name
    /// nothing. The agent still has the id; what was missing was any way to see it.
    /// </remarks>
    private ProfileChoice KnownProfile(ScheduleDocument schedule)
    {
        foreach (var choice in Profiles)
        {
            if (choice.ProfileId == schedule.ProfileId) return choice;
        }

        var missing = new ProfileChoice(schedule.ProfileId, schedule.ProfileDisplayName);
        Profiles.Add(missing);
        return missing;
    }

    /// <summary>
    /// The choice for a given weekday.
    /// </summary>
    /// <remarks>
    /// By value rather than by index, because the property named DayOfWeek shadows the enum inside
    /// this class and indexing on a cast is one rename away from being wrong silently.
    /// </remarks>
    private static DayChoice Day(DayOfWeek day)
    {
        foreach (var choice in Days)
        {
            if (choice.Day == day) return choice;
        }

        return Days[0];
    }

    /// <summary>This machine's own zone, which is what a new schedule should start on.</summary>
    private static TimeZoneChoice? LocalZone()
    {
        foreach (var zone in TimeZones)
        {
            if (zone.Id == TimeZoneInfo.Local.Id) return zone;
        }

        return TimeZones.Count > 0 ? TimeZones[0] : null;
    }

    private bool Live => _controller is not null;

    public static string Title => Ui.Schedules.WindowTitle;

    public static string SavedSchedules => Ui.Schedules.SavedSchedules;

    public static string NewScheduleLabel => Ui.Schedules.NewSchedule;

    public static string RefreshLabel => Ui.Schedules.Refresh;

    public static string SaveLabel => Ui.Schedules.SaveSchedule;

    public static string DeleteLabel => Ui.Schedules.Delete;

    public static string ProfileLabel => Ui.Schedules.SyncProfile;

    public static string RepeatsLabel => Ui.Schedules.Repeats;

    public static string DayOfWeekLabel => Ui.Schedules.DayOfWeek;

    public static string DayOfMonthLabel => Ui.Schedules.DayOfMonth;

    public static string TimeZoneLabel => Ui.Schedules.TimeZone;

    public static string TimeZoneHint => Ui.Schedules.TimeZoneHint;

    public static string CronLabel => Ui.Schedules.Cron;

    public static string CronHint => Ui.Schedules.CronHint;

    public static string ExecutionModeLabel => Ui.Schedules.ExecutionMode;

    public static string OverlapLabel => Ui.Schedules.Overlap;

    public static string QueueOneHint => Ui.Schedules.CoalesceHint;

    public static string MisfireGraceLabel => Ui.Schedules.MisfireGrace;

    public static string MisfireGraceHint => Ui.Schedules.MisfireHint;

    public static string EnabledLabel => Ui.Schedules.Enabled;

    public static string ColumnProfile => Ui.Schedules.Profile;

    public static string ColumnRecurrence => Ui.Schedules.Schedule;

    public static string ColumnNext => Ui.Schedules.Next;

    public static string ColumnState => Ui.Schedules.State;

    public static string ColumnOutcome => Ui.Schedules.CurrentState;

    public static string RunActiveBlocks => Ui.Schedules.RunActiveBlocks;

    public static string ListAccessibleName => Ui.Schedules.ListAndEditor;

    public static string StatusAccessibleName => Ui.Schedules.ManagementStatus;

    public static string FrequencyAccessibleName => Ui.Schedules.FrequencyAccessibleName;

    public static string TimeAccessibleName => Ui.Schedules.TimeAccessibleName;

    public static string TimeZoneAccessibleName => Ui.Schedules.TimeZoneAccessibleName;

    public static string ExecutionModeAccessibleName => Ui.Schedules.ExecutionModeAccessibleName;

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
