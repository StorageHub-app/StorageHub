using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Managing sync schedules.
/// </summary>
/// <remarks>
/// A schedule runs when nobody is watching, so what is checked here is what nobody would be present
/// to notice: that the builder shows only the fields the chosen frequency needs, that opening a
/// schedule written by hand does not rewrite it, and that deleting asks first while disabling --
/// which can be undone from the same screen -- does not.
/// </remarks>
public class ScheduleManagerTests
{
    [Fact]
    public async Task SavedSchedulesReachTheTable()
    {
        var agent = new StubScheduleAgent
        {
            Schedules = [Schedule("Nightly photos", "0 2 * * *"), Schedule("Archive", "0 3 1 * *")],
            Profiles = [Profile("Nightly photos"), Profile("Archive")]
        };
        var model = ScheduleManagerModel.Create(() => agent, () => agent);

        await model.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Archive", "Nightly photos"], model.Schedules.Select(static row => row.Profile));

        // The table says what the schedule does, not the cron it is stored as.
        Assert.Contains(model.Schedules, row => row.Recurrence.Contains("day 1", StringComparison.Ordinal));
    }

    /// <summary>The builder shows the fields the chosen frequency actually needs.</summary>
    [Theory]
    [InlineData((int)ScheduleFrequency.Daily, true, false, false, false)]
    [InlineData((int)ScheduleFrequency.Weekdays, true, false, false, false)]
    [InlineData((int)ScheduleFrequency.Weekly, true, true, false, false)]
    [InlineData((int)ScheduleFrequency.Monthly, true, false, true, false)]
    [InlineData((int)ScheduleFrequency.Custom, false, false, false, true)]
    public void OnlyTheFieldsThatFrequencyNeedsAreShown(
        int frequencyValue,
        bool time,
        bool weekDay,
        bool monthDay,
        bool cron)
    {
        var model = ScheduleManagerModel.Create();
        model.Frequency = ScheduleManagerModel.Frequencies
            .First(choice => choice.Frequency == (ScheduleFrequency)frequencyValue);

        Assert.Equal(time, model.ShowsTime);
        Assert.Equal(weekDay, model.ShowsWeekDay);
        Assert.Equal(monthDay, model.ShowsMonthDay);
        Assert.Equal(cron, model.ShowsCron);
    }

    /// <summary>
    /// Opening a schedule written by hand does not rewrite it.
    /// </summary>
    /// <remarks>
    /// This is what makes the builder safe to put in front of existing schedules. An expression no
    /// preset covers stays exactly as stored, and saving without touching the recurrence sends back
    /// what was already there rather than the nearest preset.
    /// </remarks>
    [Fact]
    public async Task AHandWrittenCronSurvivesBeingOpenedAndSaved()
    {
        var agent = new StubScheduleAgent
        {
            Schedules = [Schedule("Nightly photos", "*/15 9-17 * * 1-5")],
            Profiles = [Profile("Nightly photos")]
        };
        var model = ScheduleManagerModel.Create(() => agent, () => agent);
        await model.LoadAsync(TestContext.Current.CancellationToken);

        model.SelectedRow = model.Schedules[0];
        Assert.True(model.ShowsCron);
        Assert.Equal("*/15 9-17 * * 1-5", model.CronExpression);

        await model.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Equal("*/15 9-17 * * 1-5", agent.Updated!.Draft.CronExpression);
    }

    /// <summary>And a preset one opens as that preset, with the fields filled in.</summary>
    [Fact]
    public async Task APresetScheduleOpensAsItsPreset()
    {
        var agent = new StubScheduleAgent
        {
            Schedules = [Schedule("Weekly backup", "30 7 * * 3")],
            Profiles = [Profile("Weekly backup")]
        };
        var model = ScheduleManagerModel.Create(() => agent, () => agent);
        await model.LoadAsync(TestContext.Current.CancellationToken);

        model.SelectedRow = model.Schedules[0];

        Assert.Equal(ScheduleFrequency.Weekly, model.Frequency.Frequency);
        Assert.Equal(System.DayOfWeek.Wednesday, model.DayOfWeek.Day);
        Assert.Equal(new TimeSpan(7, 30, 0), model.TimeOfDay);
    }

    /// <summary>
    /// The weekdays are named through the shell's own naming, not by the enum.
    /// </summary>
    /// <remarks>
    /// 1.x bound <c>DayOfWeek</c> values straight into a combo box, which calls ToString on them --
    /// so a Danish shell offered "Monday" through "Sunday" beside lists that were translated. The
    /// wiring is what can regress here; that the naming itself follows the shell's language is
    /// pinned in UiEnumNameTests.
    /// </remarks>
    [Fact]
    public void TheWeekdaysAreNamedNotStringified()
    {
        Assert.Equal(7, ScheduleManagerModel.Days.Count);
        Assert.All(
            ScheduleManagerModel.Days,
            day => Assert.Equal(UiEnumNames.Describe(day.Day), day.Caption));
    }

    /// <summary>
    /// Deleting asks first. Disabling does not.
    /// </summary>
    /// <remarks>
    /// Disabling is reversible from the same screen and a confirmation on it would be noise, which
    /// is what teaches people to click through the one that matters. Deleting cannot be undone and
    /// the run history is then the only record the schedule ever existed.
    /// </remarks>
    [Fact]
    public async Task DeletingAsksAndDisablingDoesNot()
    {
        var agent = new StubScheduleAgent
        {
            Schedules = [Schedule("Nightly photos", "0 2 * * *")],
            Profiles = [Profile("Nightly photos")]
        };
        var dialogs = new RecordingDialogs { Choice = DialogChoice.No };
        var model = ScheduleManagerModel.Create(() => agent, () => agent, dialogs);
        await model.LoadAsync(TestContext.Current.CancellationToken);
        model.SelectedRow = model.Schedules[0];

        await model.SetEnabledAsync(false, TestContext.Current.CancellationToken);
        Assert.NotNull(agent.Enabled);
        Assert.Null(dialogs.LastRequest);

        await model.DeleteAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(dialogs.LastRequest);
        Assert.Equal(DialogChoice.No, dialogs.LastRequest!.Default);
        Assert.Null(agent.Deleted);
    }

    /// <summary>And an accepted confirmation deletes it and clears the editor.</summary>
    [Fact]
    public async Task AnAcceptedDeleteRemovesTheRow()
    {
        var agent = new StubScheduleAgent
        {
            Schedules = [Schedule("Nightly photos", "0 2 * * *")],
            Profiles = [Profile("Nightly photos")]
        };
        var dialogs = new RecordingDialogs { Choice = DialogChoice.Yes };
        var model = ScheduleManagerModel.Create(() => agent, () => agent, dialogs);
        await model.LoadAsync(TestContext.Current.CancellationToken);
        model.SelectedRow = model.Schedules[0];

        await model.DeleteAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(agent.Deleted);
        Assert.Empty(model.Schedules);
        Assert.False(model.DeleteCommand.CanExecute(null));
    }

    /// <summary>A schedule with no profile chosen is refused before it reaches the agent.</summary>
    [Fact]
    public async Task ASchedulesWithoutAProfileIsNotSent()
    {
        var agent = new StubScheduleAgent();
        var model = ScheduleManagerModel.Create(() => agent, () => agent);

        await model.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Ui.Schedules.OnlySavedProfiles, model.Status.Text);
        Assert.Null(agent.Created);
    }

    /// <summary>
    /// The execution mode says what it means, and how much to worry.
    /// </summary>
    /// <remarks>
    /// One runs without anybody present; the other queues a plan and waits. They are one drop-down
    /// apart and that is the whole safety story of scheduling.
    /// </remarks>
    [Fact]
    public void TheExecutionModeCarriesItsOwnWarning()
    {
        var model = ScheduleManagerModel.Create();

        model.ExecutionMode = ScheduleManagerModel.ExecutionModes
            .First(static mode => mode.Mode == ScheduleIpcExecutionMode.SafeAutomatic);
        Assert.True(model.ExecutionNotice.IsSuccess);

        model.ExecutionMode = ScheduleManagerModel.ExecutionModes
            .First(static mode => mode.Mode == ScheduleIpcExecutionMode.PreviewOnly);
        Assert.True(model.ExecutionNotice.IsWarning);
    }

    /// <summary>A new schedule starts off, at two in the morning, in this machine's zone.</summary>
    [Fact]
    public void ANewScheduleStartsOnSafeDefaults()
    {
        var model = ScheduleManagerModel.Create();

        Assert.False(model.Enabled);
        Assert.Equal(ScheduleFrequency.Daily, model.Frequency.Frequency);
        Assert.Equal(TimeSpan.FromHours(2), model.TimeOfDay);
        Assert.Equal(TimeZoneInfo.Local.Id, model.TimeZone!.Id);
    }

    /// <summary>The grace period travels in seconds, as the contract has it.</summary>
    [Fact]
    public void TheGracePeriodIsSentInSeconds()
    {
        var model = ScheduleManagerModel.Create();
        model.MisfireGraceMinutes = 20;

        Assert.Equal(1200, model.BuildDraft().MisfireGraceSeconds);
    }

    /// <summary>
    /// Photographs the manager, for a human to look at.
    /// </summary>
    /// <remarks>
    /// A list beside a form whose fields appear and disappear with the frequency is the kind of
    /// layout that jumps about without failing anything. Set STORAGEHUB_SHOT_DIR to keep the files.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheManagerCanBePhotographed(bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));

        var agent = new StubScheduleAgent
        {
            Schedules =
            [
                Schedule("Nightly photos", "0 2 * * *"),
                Schedule("Weekly offsite", "30 7 * * 3"),
                Schedule("Month-end archive", "0 23 28 * *", enabled: false)
            ],
            Profiles = [Profile("Nightly photos"), Profile("Weekly offsite")]
        };
        var model = ScheduleManagerModel.Create(() => agent, () => agent);
        await model.LoadAsync(TestContext.Current.CancellationToken);
        model.SelectedRow = model.Schedules[1];

        var window = new ScheduleManagerWindow { DataContext = model };
        window.Show();
        window.Measure(new Size(1180, 720));
        window.Arrange(new Rect(0, 0, 1180, 720));
        window.UpdateLayout();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;

        Directory.CreateDirectory(directory);
        using var stream = File.Create(
            Path.Combine(directory, $"schedules-{(dark ? "dark" : "light")}.png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }

    private static ScheduleDocument Schedule(string profile, string cron, bool enabled = true) => new(
        Guid.NewGuid(), Guid.NewGuid(), profile, cron, TimeZoneInfo.Local.Id, 900,
        QueueOneWhileRunning: true, Enabled: enabled,
        NextOccurrenceUtc: enabled ? DateTimeOffset.UtcNow.AddHours(6) : null,
        QueuedOccurrenceUtc: null, IsBusy: false, LastRunOutcome: "Completed",
        LastErrorCode: null, Revision: 2);

    private static SyncProfileSummary Profile(string name) => new(
        Guid.NewGuid(), name, Guid.NewGuid(), Guid.NewGuid(),
        SyncIpcDirection.LeftToRight, SyncIpcDeletionMode.Disabled, true, 1, DateTimeOffset.UtcNow);

    /// <summary>Both agent surfaces, scripted by the test.</summary>
    private sealed class StubScheduleAgent
        : IScheduleManagementAgentClient, ISyncManagementAgentClient
    {
        internal ScheduleDocument[] Schedules { get; set; } = [];

        internal SyncProfileSummary[] Profiles { get; set; } = [];

        internal ScheduleCreateRequest? Created { get; private set; }

        internal ScheduleUpdateRequest? Updated { get; private set; }

        internal ScheduleSetEnabledRequest? Enabled { get; private set; }

        internal ScheduleDeleteRequest? Deleted { get; private set; }

        public Task<ScheduleListResponse> ListAsync(
            ScheduleListRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ScheduleListResponse(
                ScheduleManagementIpcContract.CurrentVersion, Schedules));

        public Task<ScheduleGetResponse> GetAsync(
            ScheduleGetRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ScheduleGetResponse(
                ScheduleManagementIpcContract.CurrentVersion,
                request.ScheduleId,
                Schedules.FirstOrDefault(s => s.ScheduleId == request.ScheduleId)));

        public Task<ScheduleMutationResponse> CreateAsync(
            ScheduleCreateRequest request, CancellationToken cancellationToken = default)
        {
            Created = request;
            return Task.FromResult(Answer(request.ScheduleId, request.Draft));
        }

        public Task<ScheduleMutationResponse> UpdateAsync(
            ScheduleUpdateRequest request, CancellationToken cancellationToken = default)
        {
            Updated = request;
            return Task.FromResult(Answer(request.ScheduleId, request.Draft));
        }

        public Task<ScheduleMutationResponse> SetEnabledAsync(
            ScheduleSetEnabledRequest request, CancellationToken cancellationToken = default)
        {
            Enabled = request;
            return Task.FromResult(Answer(request.ScheduleId, null, request.Enabled));
        }

        public Task<ScheduleMutationResponse> DeleteAsync(
            ScheduleDeleteRequest request, CancellationToken cancellationToken = default)
        {
            Deleted = request;
            return Task.FromResult(new ScheduleMutationResponse(
                ScheduleManagementIpcContract.CurrentVersion,
                request.ScheduleId,
                ScheduleMutationOutcome.Succeeded));
        }

        public Task<SyncProfileListResponse> ListProfilesAsync(
            SyncProfileListRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SyncProfileListResponse(
                SyncManagementIpcContract.CurrentVersion, Profiles));

        private ScheduleMutationResponse Answer(
            Guid scheduleId,
            ScheduleDraftDocument? draft,
            bool? enabled = null)
        {
            var existing = Schedules.FirstOrDefault(s => s.ScheduleId == scheduleId)
                ?? Schedule("Nightly photos", draft?.CronExpression ?? "0 2 * * *");

            return new ScheduleMutationResponse(
                ScheduleManagementIpcContract.CurrentVersion,
                scheduleId,
                ScheduleMutationOutcome.Succeeded,
                existing with
                {
                    ScheduleId = scheduleId,
                    CronExpression = draft?.CronExpression ?? existing.CronExpression,
                    Enabled = enabled ?? draft?.Enabled ?? existing.Enabled,
                    Revision = existing.Revision + 1
                });
        }

        public Task<SyncProfileGetResponse> GetProfileAsync(
            SyncProfileGetRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SyncProfileMutationResponse> CreateProfileAsync(
            SyncProfileCreateRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SyncProfileMutationResponse> UpdateProfileAsync(
            SyncProfileUpdateRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SyncPreviewGenerateResponse> GeneratePreviewAsync(
            SyncPreviewGenerateRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SyncRunStatusResponse> GetRunStatusAsync(
            SyncRunStatusRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SyncPlanPageResponse> GetPlanPageAsync(
            SyncPlanPageRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SyncConflictPageResponse> GetConflictPageAsync(
            SyncConflictPageRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SyncApproveDispatchResponse> ApproveAndDispatchAsync(
            SyncApproveDispatchRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>The confirmation, answered by the test instead of by a person.</summary>
    private sealed class RecordingDialogs : IDialogService
    {
        internal DialogChoice Choice { get; set; } = DialogChoice.No;

        internal DialogRequest? LastRequest { get; private set; }

        public Task ShowAsync(DialogRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.CompletedTask;
        }

        public Task<DialogChoice> ConfirmAsync(
            DialogRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(Choice);
        }

        public Task<string?> PromptAsync(
            DialogPromptRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
