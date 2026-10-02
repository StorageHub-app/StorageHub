using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Editing a sync profile.
/// </summary>
/// <remarks>
/// The screen is fourteen fields and the fields are the easy part. What is here is the editing
/// around them: that choosing a saved profile loads the parts the list does not carry, that a
/// complaint lands beside the field it is about rather than in one line at the foot of the window,
/// and what a new profile starts as.
/// </remarks>
public class SyncProfileEditorTests
{
    [Fact]
    public async Task ProfilesAndConnectionsArriveInTheirPickers()
    {
        var agent = new StubEditorAgent
        {
            Profiles = [Profile("Nightly photos"), Profile("Archive")],
            Connections =
            [
                Connection("Studio"),
                Connection("Backups"),
                Connection("Zeta") with { IsFavorite = true },
                Connection("Build box") with { Provider = StorageConnectionProvider.Ssh, Type = ConnectionProfileType.Client }
            ]
        };
        var model = SyncProfileEditorModel.Create(() => agent, () => agent);

        await model.LoadAsync(TestContext.Current.CancellationToken);

        // The "create a new one" entry is always first, so the picker is never empty.
        Assert.Equal(
            [Ui.Sync.CreateNewProfile, "Archive", "Nightly photos"],
            model.Profiles.Select(static choice => choice.DisplayName));

        // Storage only, favourites first, as 1.4 listed them: an SSH client has nothing to sync.
        Assert.Equal(["Zeta", "Backups", "Studio"], model.Connections.Select(static c => c.DisplayName));
    }

    /// <summary>
    /// A disabled connection is still listed, and says so.
    /// </summary>
    /// <remarks>
    /// A profile may point at a connection that was switched off since it was saved. Hiding it
    /// would leave the picker with nothing selected, and the next save would silently move the
    /// profile to a different connection.
    /// </remarks>
    [Fact]
    public async Task ADisabledConnectionIsOfferedAndLabelled()
    {
        var agent = new StubEditorAgent { Connections = [Connection("Studio", enabled: false)] };
        var model = SyncProfileEditorModel.Create(() => agent, () => agent);

        await model.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            Ui.Format(Ui.Sync.DisabledConnectionFormat, "Studio"), model.Connections[0].Caption);
    }

    /// <summary>
    /// Choosing a saved profile reads it in full.
    /// </summary>
    /// <remarks>
    /// The list carries a summary: a name, two connection ids and a direction. The roots, the
    /// filters and the limits are not in it, so showing the summary's fields would present an
    /// unfiltered profile with default limits and then save that over the real one.
    /// </remarks>
    [Fact]
    public async Task ChoosingASavedProfileLoadsTheWholeThing()
    {
        var agent = new StubEditorAgent
        {
            Connections = [Connection("Studio"), Connection("Backups")]
        };
        var model = SyncProfileEditorModel.Create(() => agent, () => agent);
        await model.LoadAsync(TestContext.Current.CancellationToken);

        agent.Stored = Draft("Nightly photos") with
        {
            LeftRoot = "photos",
            RightRoot = "mirror",
            IncludeGlobs = ["**/*.jpg"],
            MaximumDeletionCount = 42
        };
        await model.OpenAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal("Nightly photos", model.Name);
        Assert.Equal("photos", model.LocationARoot);
        Assert.Equal("**/*.jpg", model.IncludeGlobs);
        Assert.Equal(42, model.MaximumDeletionCount);
    }

    /// <summary>
    /// A complaint goes beside the field it is about.
    /// </summary>
    /// <remarks>
    /// This is the whole reason the rules report a field. The WinForms editor printed one sentence
    /// at the foot of a window with fourteen fields on it, so a blank name and a buffer size of
    /// zero read exactly alike.
    /// </remarks>
    [Fact]
    public async Task EachComplaintLandsOnItsOwnField()
    {
        var agent = new StubEditorAgent();
        var model = SyncProfileEditorModel.Create(() => agent, () => agent);
        model.Name = "   ";
        model.TransferBufferSize = 0;

        await model.SaveAsync(TestContext.Current.CancellationToken);

        Assert.True(model.HasNameProblem);
        Assert.True(model.HasTransferBufferProblem);
        Assert.False(model.HasDeletionCountProblem);
        Assert.Null(agent.Created);
    }

    /// <summary>And typing in a field clears its complaint rather than leaving it stale.</summary>
    [Fact]
    public async Task EditingAFieldClearsItsComplaint()
    {
        var agent = new StubEditorAgent();
        var model = SyncProfileEditorModel.Create(() => agent, () => agent);
        model.Name = string.Empty;
        await model.SaveAsync(TestContext.Current.CancellationToken);
        Assert.True(model.HasNameProblem);

        model.Name = "Nightly photos";

        Assert.False(model.HasNameProblem);
    }

    /// <summary>A saved profile appears in the picker without another round trip.</summary>
    [Fact]
    public async Task SavingANewProfileAddsItToThePicker()
    {
        var agent = new StubEditorAgent { Connections = [Connection("A"), Connection("B")] };
        var model = SyncProfileEditorModel.Create(() => agent, () => agent);
        await model.LoadAsync(TestContext.Current.CancellationToken);
        model.Name = "Nightly photos";
        model.LocationARoot = "left";
        model.LocationBRoot = "right";

        await model.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Contains(model.Profiles, choice => choice.DisplayName == "Nightly photos");
        Assert.True(model.Status.IsSuccess);
    }

    /// <summary>
    /// A second save is an update, not a second profile.
    /// </summary>
    /// <remarks>
    /// The editor keeps the document it last saved, which is what supplies the profile id and the
    /// revision. Losing it would create a duplicate on every save.
    /// </remarks>
    [Fact]
    public async Task SavingTwiceUpdatesRatherThanDuplicates()
    {
        var agent = new StubEditorAgent { Connections = [Connection("A"), Connection("B")] };
        var model = SyncProfileEditorModel.Create(() => agent, () => agent);
        await model.LoadAsync(TestContext.Current.CancellationToken);
        model.Name = "Nightly photos";
        model.LocationARoot = "left";
        model.LocationBRoot = "right";

        await model.SaveAsync(TestContext.Current.CancellationToken);
        await model.SaveAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(agent.Updated);
        Assert.Single(model.Profiles, static choice => choice.ProfileId != Guid.Empty);
    }

    /// <summary>A new profile starts safe: disabled, updating one way, excluding our own state.</summary>
    [Fact]
    public void ANewProfileStartsOnSafeDefaults()
    {
        var model = SyncProfileEditorModel.Create();

        Assert.False(model.Enabled);
        Assert.Equal(SyncIpcBehavior.UpdateAToB, model.Behavior.Behavior);
        Assert.Equal(SyncIpcConflictPolicy.Block, model.ConflictPolicy.Policy);
        Assert.False(model.AllowNonAtomicWrites);
        Assert.True(model.IncludeHiddenFiles);
        Assert.Contains(".storagehub", model.ExcludeGlobs, StringComparison.Ordinal);
    }

    /// <summary>
    /// Swapping exchanges the locations and leaves the behaviour alone.
    /// </summary>
    /// <remarks>
    /// Under "mirror A to B", swapping the two and the behaviour together would reverse which side
    /// gets deleted without saying so. Somebody who wants that can choose it.
    /// </remarks>
    [Fact]
    public async Task SwappingMovesTheLocationsAndNotTheBehaviour()
    {
        var agent = new StubEditorAgent { Connections = [Connection("A"), Connection("B")] };
        var model = SyncProfileEditorModel.Create(() => agent, () => agent);
        await model.LoadAsync(TestContext.Current.CancellationToken);
        model.LocationARoot = "left";
        model.LocationBRoot = "right";
        var behavior = model.Behavior;
        var first = model.LocationA;

        model.Swap();

        Assert.Equal("right", model.LocationARoot);
        Assert.Equal("left", model.LocationBRoot);
        Assert.Equal(first, model.LocationB);
        Assert.Equal(behavior, model.Behavior);
    }

    /// <summary>The filters are one per line, and a blank line is not a filter.</summary>
    /// <remarks>
    /// Deleting a filter from a text box leaves a blank line behind, and the agent refuses an empty
    /// glob -- so without this, removing a filter would make the profile unsaveable and point the
    /// complaint at a line that is no longer there.
    /// </remarks>
    [Fact]
    public void BlankFilterLinesAreNotSent()
    {
        var model = SyncProfileEditorModel.Create();
        model.IncludeGlobs = "**/*.jpg\n\n   \n**/*.png\n";

        Assert.Equal(["**/*.jpg", "**/*.png"], model.BuildDraft().IncludeGlobs);
    }

    /// <summary>
    /// A preview hands the run on rather than showing it here, but not once the editor has closed.
    /// </summary>
    /// <remarks>
    /// Runs are reviewed and approved on one screen, which is where that is guarded. 1.x embedded a
    /// second copy of the review control in the editor and had two sets of buttons to keep in step.
    /// A plan that arrives after the editor closed would move the main window to Run history on
    /// its own, wherever the user had gone since; 1.4 cancelled everything on close.
    /// </remarks>
    [Fact]
    public async Task APreviewHandsTheRunOver()
    {
        var agent = new StubEditorAgent { Connections = [Connection("A"), Connection("B")] };
        var model = SyncProfileEditorModel.Create(() => agent, () => agent);
        await model.LoadAsync(TestContext.Current.CancellationToken);
        model.Name = "Nightly photos";
        model.LocationARoot = "left";
        model.LocationBRoot = "right";

        // Enabled, so this is the plain success path. The disabled case has its own test, and
        // leaving it off here got that warning instead -- which is the behaviour working.
        model.Enabled = true;

        SyncRunSummary? handed = null;
        var closed = false;
        model.PreviewReady += (_, run) => handed = run;
        model.Closed += (_, _) => closed = true;
        await model.PreviewAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(handed);
        Assert.True(model.Status.IsSuccess);
        Assert.True(closed);

        handed = null;
        agent.Hold = new TaskCompletionSource();
        var late = model.PreviewAsync(TestContext.Current.CancellationToken);
        model.Dispose();
        agent.Hold.SetResult();
        await late;

        Assert.Null(handed);
    }

    /// <summary>
    /// And a preview of a disabled profile says so, with the editor still open to be read.
    /// </summary>
    /// <remarks>
    /// A disabled profile previews perfectly and then never runs by itself, which is invisible once
    /// the plan is on screen and is discovered when the schedule does nothing. Closing the editor
    /// took the warning with it, and it asks for Enabled to be ticked here, so the editor stays, as
    /// 1.4's did. The run is still handed over, so Close leads to it.
    /// </remarks>
    [Fact]
    public async Task PreviewingADisabledProfileWarns()
    {
        var agent = new StubEditorAgent { Connections = [Connection("A"), Connection("B")] };
        var model = SyncProfileEditorModel.Create(() => agent, () => agent);
        await model.LoadAsync(TestContext.Current.CancellationToken);
        model.Name = "Nightly photos";
        model.LocationARoot = "left";
        model.LocationBRoot = "right";
        model.Enabled = false;

        SyncRunSummary? handed = null;
        var closed = false;
        model.PreviewReady += (_, run) => handed = run;
        model.Closed += (_, _) => closed = true;
        await model.PreviewAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Ui.Format(Ui.Sync.PreviewWarningFormat, Ui.Sync.PreviewedWhileDisabled), model.Status.Text);
        Assert.True(model.Status.IsWarning);
        Assert.NotNull(handed);
        Assert.False(closed);
    }

    /// <summary>A screen with nothing behind it never reaches and offers nothing.</summary>
    [Fact]
    public async Task APreviewScreenNeverReaches()
    {
        var model = SyncProfileEditorModel.Create();

        await model.LoadAsync(TestContext.Current.CancellationToken);
        await model.SaveAsync(TestContext.Current.CancellationToken);

        Assert.False(model.SaveCommand.CanExecute(null));
        Assert.False(model.PreviewCommand.CanExecute(null));
    }

    /// <summary>
    /// Photographs the editor, for a human to look at.
    /// </summary>
    /// <remarks>
    /// Fourteen fields down three sections, a nine-item behaviour list and a complaint under each
    /// field is exactly the kind of layout that goes wrong without failing anything. Set
    /// STORAGEHUB_SHOT_DIR to keep the files.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheEditorCanBePhotographed(bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));

        var agent = new StubEditorAgent
        {
            Profiles = [Profile("Nightly photos"), Profile("Archive")],
            Connections = [Connection("Studio Assets"), Connection("Site Backups")]
        };
        var model = SyncProfileEditorModel.Create(
            () => agent, () => agent, static (_, _, _) => Task.FromResult<string?>(null));
        await model.LoadAsync(TestContext.Current.CancellationToken);
        model.Name = "Nightly photos";
        model.LocationARoot = "photos/2019";
        model.LocationBRoot = "archive/photos";
        model.IncludeGlobs = "**/*.jpg\n**/*.raw";

        // A fraction of a percent, stepped in quarters, as 1.4 allowed. The field's floor used to
        // be 1, so a saved 0.5 could be neither typed nor stepped down from.
        model.MaximumDeletionPercentage = 0.5m;

        var window = new SyncProfileEditorWindow { DataContext = model };
        window.Show();
        window.Measure(new Size(1040, 900));
        window.Arrange(new Rect(0, 0, 1040, 900));
        window.UpdateLayout();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        // And the limits at the foot of the form, the percentage among them.
        window.GetVisualDescendants().OfType<ScrollViewer>().First(viewer => viewer.Parent is DockPanel)
            .ScrollToEnd();
        window.UpdateLayout();
        var limits = window.CaptureRenderedFrame();
        Assert.NotNull(limits);

        var percentage = window.GetVisualDescendants().OfType<NumericUpDown>().Single(
            field => AutomationProperties.GetName(field) == SyncProfileEditorModel.MaximumPercentLabel);
        percentage.GetVisualDescendants().OfType<ButtonSpinner>().Single()
            .RaiseEvent(new SpinEventArgs(Spinner.SpinEvent, SpinDirection.Decrease));
        Assert.Equal(0.25m, model.MaximumDeletionPercentage);

        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;

        Directory.CreateDirectory(directory);
        var theme = dark ? "dark" : "light";
        using (var stream = File.Create(Path.Combine(directory, $"sync-profile-editor-{theme}.png")))
        {
            frame!.Save(stream, new PngBitmapEncoderOptions());
        }

        using var scrolled = File.Create(Path.Combine(directory, $"sync-profile-editor-limits-{theme}.png"));
        limits!.Save(scrolled, new PngBitmapEncoderOptions());
    }

    private static SyncProfileDraftDocument Draft(string name) => new(
        name, Guid.NewGuid(), "left", Guid.NewGuid(), "right",
        SyncIpcBehavior.UpdateAToB, SyncIpcConflictPolicy.Block,
        includeGlobs: [], excludeGlobs: [".storagehub"], includeHiddenFiles: true,
        maximumDeletionCount: 100, maximumDeletionPercentage: 10m,
        transferBufferSize: 64 * 1024, enabled: true);

    private static SyncProfileSummary Profile(string name) => new(
        Guid.NewGuid(), name, Guid.NewGuid(), Guid.NewGuid(),
        SyncIpcDirection.LeftToRight, SyncIpcDeletionMode.Disabled, true, 1, DateTimeOffset.UtcNow);

    private static ConnectionSummary Connection(string name, bool enabled = true) => new(
        Guid.NewGuid(), name, StorageConnectionProvider.Local, null, [], false, enabled,
        null, null, 1);

    /// <summary>Both agent surfaces, scripted by the test.</summary>
    private sealed class StubEditorAgent : ISyncManagementAgentClient, IRemoteStorageAgentClient
    {
        internal SyncProfileSummary[] Profiles { get; set; } = [];

        internal ConnectionSummary[] Connections { get; set; } = [];

        /// <summary>What GetProfile answers with.</summary>
        internal SyncProfileDraftDocument? Stored { get; set; }

        internal SyncProfileCreateRequest? Created { get; private set; }

        internal SyncProfileUpdateRequest? Updated { get; private set; }

        /// <summary>Holds the preview unanswered until it is set.</summary>
        internal TaskCompletionSource? Hold { get; set; }

        public Task<SyncProfileListResponse> ListProfilesAsync(
            SyncProfileListRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SyncProfileListResponse(
                SyncManagementIpcContract.CurrentVersion, Profiles));

        public Task<ConnectionListResponse> ListConnectionsAsync(
            ConnectionListRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionListResponse(
                StorageIpcContract.CurrentVersion, Connections));

        public Task<SyncProfileGetResponse> GetProfileAsync(
            SyncProfileGetRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SyncProfileGetResponse(
                SyncManagementIpcContract.CurrentVersion,
                request.ProfileId,
                new SyncProfileDocument(
                    request.ProfileId,
                    Stored ?? Draft("Stored"),
                    3,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow)));

        public Task<SyncProfileMutationResponse> CreateProfileAsync(
            SyncProfileCreateRequest request, CancellationToken cancellationToken = default)
        {
            Created = request;
            return Task.FromResult(new SyncProfileMutationResponse(
                SyncManagementIpcContract.CurrentVersion,
                request.ProfileId,
                SyncProfileMutationOutcome.Succeeded,
                new SyncProfileDocument(
                    request.ProfileId, request.Draft, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)));
        }

        public Task<SyncProfileMutationResponse> UpdateProfileAsync(
            SyncProfileUpdateRequest request, CancellationToken cancellationToken = default)
        {
            Updated = request;
            return Task.FromResult(new SyncProfileMutationResponse(
                SyncManagementIpcContract.CurrentVersion,
                request.ProfileId,
                SyncProfileMutationOutcome.Succeeded,
                new SyncProfileDocument(
                    request.ProfileId,
                    request.Draft,
                    request.ExpectedRevision + 1,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow)));
        }

        public async Task<SyncPreviewGenerateResponse> GeneratePreviewAsync(
            SyncPreviewGenerateRequest request, CancellationToken cancellationToken = default)
        {
            // Deaf to the token, as a reply already on its way is.
            if (Hold is { } hold) await hold.Task;

            var run = new SyncRunSummary(
                Guid.NewGuid(), request.ProfileId, 1, SyncIpcRunPhase.AwaitingApproval,
                SyncIpcStatusCode.None, 1, DateTimeOffset.UtcNow, Guid.NewGuid(),
                new string('a', 64), new string('b', 64), 0, SyncIpcDispatchState.NotDispatched,
                null, DateTimeOffset.UtcNow, 0, 0, 0, true, true);

            return new SyncPreviewGenerateResponse(
                SyncManagementIpcContract.CurrentVersion,
                request.ProfileId,
                run,
                new SyncPlanOverview(
                    run.SyncRunId, run.PlanId, run.PlanSha256, 1, 3, 3, 0, 0, DateTimeOffset.UtcNow));
        }

        public Task<SyncRunStatusResponse> GetRunStatusAsync(
            SyncRunStatusRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SyncRunListResponse> ListRunsAsync(
            SyncRunListRequest request, CancellationToken cancellationToken = default) =>
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

        public Task<StorageListPageResponse> ListStorageAsync(
            StorageListPageRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConnectionTestResponse> TestConnectionAsync(
            ConnectionTestRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
