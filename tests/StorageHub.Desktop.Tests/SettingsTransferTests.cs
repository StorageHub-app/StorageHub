using StorageHub.Contracts.Ipc;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Configuration;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The export dialog and the import wizard.
/// </summary>
/// <remarks>
/// The rules about what a file may contain, and the gates an import runs before touching
/// anything, are pinned in the Core tests. What is checked here is the seam between the screens
/// and them: that ticking a section takes what it cannot stand without, that a sealed file asks
/// for its password and a wrong one leaves the wizard where it was, and that the review offers
/// only what the file actually carries.
/// </remarks>
public sealed class SettingsTransferTests : IDisposable
{
    private const string Password = "a good long password";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"storagehub-settings-transfer-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    /// <summary>
    /// Schedules cannot be described without the sync tasks they run, nor those without their
    /// connections, so ticking one takes both.
    /// </summary>
    /// <remarks>
    /// The catalog's rules are pinned in SettingsSectionCatalogTests. What this adds is that the
    /// dialog applies them as the box is clicked, so the ticks always show exactly what the file
    /// will hold.
    /// </remarks>
    [AvaloniaFact]
    public void TickingASectionTakesWhatItCannotStandWithout()
    {
        var model = Export(agent: true);
        foreach (var section in model.Sections) section.SetQuietly(false);

        Choice(model, SettingsSectionId.Schedules).Selected = true;

        Assert.Contains(SettingsSectionId.SyncProfiles, model.SelectedSections);
        Assert.Contains(SettingsSectionId.Connections, model.SelectedSections);
    }

    /// <summary>And clearing one clears whatever depended on it, rather than leaving it ticked.</summary>
    [AvaloniaFact]
    public void ClearingASectionClearsWhatDependedOnIt()
    {
        var model = Export(agent: true);
        Choice(model, SettingsSectionId.Schedules).Selected = true;

        Choice(model, SettingsSectionId.Connections).Selected = false;

        Assert.DoesNotContain(SettingsSectionId.SyncProfiles, model.SelectedSections);
        Assert.DoesNotContain(SettingsSectionId.Schedules, model.SelectedSections);
    }

    /// <summary>
    /// An expansion never reaches a section the agent cannot be asked for, because that is a state
    /// the dialog's own validation says cannot happen.
    /// </summary>
    [AvaloniaFact]
    public void AnExpansionDoesNotTickWhatTheAgentCannotBeAskedFor()
    {
        var model = Export();
        foreach (var section in model.Sections) section.SetQuietly(false);

        Choice(model, SettingsSectionId.Shortcuts).Selected = true;

        Assert.Equal([SettingsSectionId.Shortcuts], model.SelectedSections);
    }

    /// <summary>Nothing ticked is not an export, and the dialog says which of the two it is.</summary>
    [AvaloniaFact]
    public void AnEmptySelectionIsRefusedWithAReason()
    {
        var model = Export();
        foreach (var section in model.Sections) section.SetQuietly(false);
        Choice(model, SettingsSectionId.Shortcuts).Selected = true;
        Choice(model, SettingsSectionId.Shortcuts).Selected = false;

        Assert.True(model.HasProblem);
        Assert.Equal(Ui.SettingsTransfer.ChooseAtLeastOneThingToExport, model.Problem);
        Assert.False(model.CanExport);
    }

    /// <summary>A password that is not typed twice the same way is not a password.</summary>
    [AvaloniaFact]
    public void ProtectingTheFileRequiresTheTwoPasswordsToAgree()
    {
        var model = Export();

        model.Protect = true;
        model.Password = Password;
        model.Confirm = Password + " not quite";

        Assert.Equal(Ui.SettingsTransfer.TheTwoPasswordsDoNotMatch, model.Problem);

        model.Confirm = Password;
        Assert.False(model.HasProblem);
    }

    /// <summary>Unticking the box forgets the password rather than keeping it out of sight.</summary>
    [AvaloniaFact]
    public void ClearingTheProtectBoxForgetsThePassword()
    {
        var model = Export();
        model.Protect = true;
        model.Password = Password;
        model.Confirm = Password;

        model.Protect = false;

        Assert.Empty(model.Password);
        Assert.Empty(model.Confirm);
    }

    /// <summary>
    /// The agent-backed sections are dimmed with a reason when there is no agent, rather than
    /// offered and then failing halfway through a capture that cannot complete.
    /// </summary>
    [AvaloniaFact]
    public void WithNoAgentTheSectionsItOwnsAreDimmed()
    {
        var store = Store();
        var model = new SettingsExportModel(new SettingsExportService(store), Picker());

        var connections = Choice(model, SettingsSectionId.Connections);
        Assert.False(connections.IsAvailable);
        Assert.False(connections.Selected);
        Assert.Equal(Ui.Validation.TheBackgroundAgentIsNotRunning, connections.Description);
        Assert.True(Choice(model, SettingsSectionId.Shortcuts).IsAvailable);
    }

    /// <summary>The file lands where the picker said, and the dialog answers with the path.</summary>
    [AvaloniaFact]
    public async Task ExportingWritesTheFileThePickerChose()
    {
        var path = Path.Combine(Directory.CreateDirectory(_directory).FullName, "out.shsettings");
        var model = Export(new StubFilePicker { SavePath = path });

        await model.ExportAsync(TestContext.Current.CancellationToken);

        Assert.True(File.Exists(path));
        Assert.Equal(path, model.ExportedPath);
        Assert.Equal(SettingsFileKind.PlainText, SettingsImportService.Inspect(path).Kind);
    }

    /// <summary>Dismissing the picker writes nothing and leaves the dialog open.</summary>
    [AvaloniaFact]
    public async Task DismissingTheSavePickerWritesNothing()
    {
        var model = Export(new StubFilePicker { SavePath = null });
        var closed = false;
        model.Closed += (_, _) => closed = true;

        await model.ExportAsync(TestContext.Current.CancellationToken);

        Assert.Null(model.ExportedPath);
        Assert.False(closed);
    }

    /// <summary>A protected export is sealed, and reads back as such.</summary>
    [AvaloniaFact]
    public async Task AProtectedExportIsSealed()
    {
        var path = Path.Combine(Directory.CreateDirectory(_directory).FullName, "sealed.shsettings");
        var model = Export(new StubFilePicker { SavePath = path });
        model.Protect = true;
        model.Password = Password;
        model.Confirm = Password;

        await model.ExportAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SettingsFileKind.PasswordProtected, SettingsImportService.Inspect(path).Kind);
    }

    /// <summary>An unsealed file opens straight onto the review, with no password asked for.</summary>
    [AvaloniaFact]
    public void APlainFileOpensOntoTheReview()
    {
        var path = Written(password: null);
        var model = Import();

        model.LoadFile(path);

        Assert.Equal(SettingsImportStep.ChooseFile, model.Step);
        Assert.False(model.NeedsPassword);
        Assert.True(model.CanOpen);
        Assert.Contains(Ui.SettingsTransfer.ThisFileIsNotPasswordProtected, model.FileSummary, StringComparison.Ordinal);

        model.OpenSelectedFile();

        Assert.Equal(SettingsImportStep.Review, model.Step);
        Assert.NotEmpty(model.Sections);
    }

    /// <summary>
    /// A wrong password leaves the wizard on the first step so it can simply be retyped, and says
    /// so where the prompt was.
    /// </summary>
    [AvaloniaFact]
    public void AWrongPasswordStaysOnTheFileStep()
    {
        var path = Written(Password);
        var model = Import();
        model.LoadFile(path);

        Assert.True(model.NeedsPassword);
        Assert.Equal(Ui.SettingsTransfer.EnterTheFileSPassword, model.PasswordPrompt);

        model.Password = "not the password";
        model.OpenSelectedFile();

        Assert.Equal(SettingsImportStep.ChooseFile, model.Step);
        Assert.True(model.PasswordPromptIsProblem);

        model.Password = Password;
        model.OpenSelectedFile();

        Assert.Equal(SettingsImportStep.Review, model.Step);
    }

    /// <summary>A file that is not an export at all is refused before a password is asked for.</summary>
    [AvaloniaFact]
    public void AFileThatIsNotAnExportIsRefusedWithoutAskingForAPassword()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "empty.shsettings");
        File.WriteAllText(path, string.Empty);
        var model = Import();

        model.LoadFile(path);

        Assert.False(model.CanOpen);
        Assert.False(model.NeedsPassword);
        Assert.Equal(SettingsImportStep.ChooseFile, model.Step);
    }

    /// <summary>
    /// The review offers every section, but only ticks and enables what the file actually holds,
    /// so what was left out of the file is said rather than silently missing.
    /// </summary>
    [AvaloniaFact]
    public void TheReviewOffersOnlyWhatTheFileCarries()
    {
        var path = Written(password: null, SettingsSectionId.Shortcuts);
        var model = Import();
        model.LoadFile(path);
        model.OpenSelectedFile();

        Assert.Equal(SettingsSectionCatalog.Sections.Count, model.Sections.Count);
        var shortcuts = model.Sections.First(section => section.Id is SettingsSectionId.Shortcuts);
        Assert.True(shortcuts.IsAvailable);
        Assert.True(shortcuts.Selected);

        var connections = model.Sections.First(section => section.Id is SettingsSectionId.Connections);
        Assert.False(connections.IsAvailable);
        Assert.False(connections.Selected);
        Assert.Equal(Ui.SettingsTransfer.NotInThisFile, connections.Description);

        // Nothing the agent owns is in this file, so there is nothing that could collide.
        Assert.False(model.ShowsConflictPolicy);
    }

    /// <summary>
    /// "This computer only" has to be asked for even when the file carries it: those settings
    /// point at the machine that wrote them.
    /// </summary>
    [AvaloniaFact]
    public void MachineSpecificSettingsArriveUnticked()
    {
        var path = Written(password: null, SettingsSectionId.MachineSpecific, SettingsSectionId.Shortcuts);
        var model = Import();
        model.LoadFile(path);
        model.OpenSelectedFile();

        var machine = model.Sections.First(section => section.Id is SettingsSectionId.MachineSpecific);
        Assert.True(machine.IsAvailable);
        Assert.False(machine.Selected);
    }

    /// <summary>Applying reports what it did and leaves a backup to get back to.</summary>
    [AvaloniaFact]
    public async Task ApplyingReportsWhatItDidAndLeavesABackup()
    {
        var path = Written(password: null, SettingsSectionId.Shortcuts);
        var model = Import();
        model.LoadFile(path);
        model.OpenSelectedFile();

        await model.ApplyImportAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SettingsImportStep.Result, model.Step);
        Assert.NotNull(model.Report);
        Assert.Contains(SettingsSectionId.Shortcuts, model.Report!.Applied);
        Assert.True(model.Changed);
        Assert.True(model.HasBackup);
        Assert.Contains(Ui.SettingsTransfer.Imported, model.ResultSummary, StringComparison.Ordinal);
    }

    /// <summary>Importing nothing is not a change, and the shell is not told to refresh.</summary>
    [AvaloniaFact]
    public async Task ImportingNothingIsNotAChange()
    {
        var path = Written(password: null, SettingsSectionId.Shortcuts);
        var model = Import();
        model.LoadFile(path);
        model.OpenSelectedFile();
        foreach (var section in model.Sections) section.Selected = false;

        await model.ApplyImportAsync(TestContext.Current.CancellationToken);

        Assert.False(model.Changed);
        Assert.Equal(Ui.SettingsTransfer.NothingWasImported, model.ResultSummary);
    }

    /// <summary>Back from the review returns to the file step, with the file still chosen.</summary>
    [AvaloniaFact]
    public void BackReturnsToTheFileStep()
    {
        var path = Written(password: null);
        var model = Import();
        model.LoadFile(path);
        model.OpenSelectedFile();

        model.BackCommand.Execute(null);

        Assert.Equal(SettingsImportStep.ChooseFile, model.Step);
        Assert.True(model.CanOpen);
    }

    /// <summary>
    /// Photographs the export dialog in both appearances, for a human to look at.
    /// </summary>
    /// <remarks>
    /// Seven sections with a description under each, then three exclusions and a password group
    /// that must stay above the fold, is exactly the sort of screen that lays out wrong without
    /// failing anything. Set STORAGEHUB_SHOT_DIR to keep the files.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheExportDialogCanBePhotographed(bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));

        var model = Export(agent: true);
        // Sealed, because the password group is the part that has to stay above the fold and it
        // is only fully drawn once the box is ticked.
        model.Protect = true;
        model.Password = Password;
        model.Confirm = Password;

        var window = new SettingsExportWindow { DataContext = model };
        Photograph(window, 660, 900, $"settings-export-{(dark ? "dark" : "light")}");
    }

    /// <summary>
    /// Photographs all three import steps, which is where the layout risk is: each is a different
    /// shape in the same window.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheImportWizardCanBePhotographed(bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));

        var appearance = dark ? "dark" : "light";
        var path = Written(Password, SettingsSectionId.DesktopGeneral, SettingsSectionId.Shortcuts);
        var model = Import();
        var window = new SettingsImportWindow { DataContext = model };

        model.LoadFile(path);
        Photograph(window, 660, 560, $"settings-import-file-{appearance}");

        model.Password = Password;
        model.OpenSelectedFile();
        Assert.Equal(SettingsImportStep.Review, model.Step);
        Photograph(window, 660, 700, $"settings-import-review-{appearance}");

        await model.ApplyImportAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SettingsImportStep.Result, model.Step);
        Photograph(window, 660, 560, $"settings-import-result-{appearance}");
    }

    /// <summary>
    /// Renders a window at a given size and keeps the frame when asked to.
    /// </summary>
    /// <remarks>
    /// The capture is asserted on whether or not the frame is kept, so a window that cannot be
    /// rendered at all fails on every machine rather than only on one with the variable set.
    /// </remarks>
    private static void Photograph(Window window, double width, double height, string name)
    {
        if (!window.IsVisible) window.Show();
        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));
        window.UpdateLayout();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;

        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, $"{name}.png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }

    private static SettingsSectionChoice Choice(SettingsExportModel model, SettingsSectionId id) =>
        model.Sections.First(section => section.Id == id);

    private DesktopConfigStore Store()
    {
        Directory.CreateDirectory(_directory);
        return new DesktopConfigStore(_directory);
    }

    /// <summary>
    /// The export dialog over a temporary settings file.
    /// </summary>
    /// <param name="agent">
    /// Whether the agent-backed sections are on offer. The clients are never called: what needs an
    /// agent here is only whether those sections can be ticked, and a capture is never run in a
    /// test that passes true.
    /// </param>
    private SettingsExportModel Export(IFilePickerService? picker = null, bool agent = false) =>
        new(new SettingsExportService(
                Store(),
                agent ? UnreachableClients : null,
                clock: () => DateTimeOffset.UnixEpoch,
                application: () => "StorageHub test",
                fingerprint: () => "test-fingerprint"),
            picker ?? Picker(),
            () => DateTimeOffset.UnixEpoch);

    /// <summary>
    /// Clients that stand for a reachable agent without being one.
    /// </summary>
    /// <remarks>
    /// Named for what it is: every one of these throws if a capture actually reaches it, so a test
    /// that starts exporting agent-backed sections fails loudly here rather than appearing to
    /// export something. What agent-backed sections mean over a real pipe is covered by
    /// SettingsAgentTransferTests and the live tests.
    /// </remarks>
    private static SettingsAgentClients UnreachableClients { get; } = new(
        new UnreachableAgent(),
        new UnreachableAgent(),
        new UnreachableAgent(),
        new UnreachableAgent());

    private SettingsImportModel Import()
    {
        var store = Store();
        var exporter = new SettingsExportService(
            store,
            clock: () => DateTimeOffset.UnixEpoch,
            application: () => "StorageHub test",
            fingerprint: () => "test-fingerprint");
        return new SettingsImportModel(
            new SettingsImportService(
                store,
                exporter,
                Path.Combine(_directory, "backups")),
            Picker());
    }

    /// <summary>An export file on disk, carrying the sections named or all of the desktop ones.</summary>
    private string Written(string? password, params SettingsSectionId[] sections)
    {
        var store = Store();
        var exporter = new SettingsExportService(
            store,
            clock: () => DateTimeOffset.UnixEpoch,
            application: () => "StorageHub test",
            fingerprint: () => "test-fingerprint");
        var chosen = sections.Length > 0
            ? sections
            : [SettingsSectionId.DesktopGeneral, SettingsSectionId.Shortcuts];
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.shsettings");
        SettingsExportService.Write(path, exporter.Capture(chosen), password);
        return path;
    }

    private static StubFilePicker Picker() => new();
}

/// <summary>
/// A file picker that answers with whatever the test put in it.
/// </summary>
/// <remarks>
/// The real one is a portal call on Linux and a common item dialog on Windows; neither can be
/// driven from a headless test, and what these tests are about is what the dialog does with the
/// answer rather than how it was obtained.
/// </remarks>
internal sealed class StubFilePicker : IFilePickerService
{
    /// <summary>What a save picker answers with. Null is a dismissed picker.</summary>
    internal string? SavePath { get; init; }

    /// <summary>What an open picker answers with. Null is a dismissed picker.</summary>
    internal string? OpenPath { get; init; }

    /// <summary>Every request that was made, so a test can check what was asked for.</summary>
    internal List<FilePickerRequest> Requests { get; } = [];

    public Task<string?> PickFileAsync(FilePickerRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult(OpenPath);
    }

    public Task<IReadOnlyList<string>> PickFilesAsync(
        FilePickerRequest request,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult<IReadOnlyList<string>>(OpenPath is null ? [] : [OpenPath]);
    }

    public Task<string?> PickFolderAsync(FilePickerRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult(OpenPath);
    }

    public Task<string?> SaveFileAsync(FilePickerRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult(SavePath);
    }
}

/// <summary>
/// The four agent clients, as a stand-in for an agent that is present.
/// </summary>
/// <remarks>
/// Every method throws. It exists so a test can say "the agent-backed sections are on offer"
/// without also saying what they contain: a test that starts actually capturing them fails here,
/// loudly, rather than appearing to export something. What those sections mean over a real pipe is
/// covered by SettingsAgentTransferTests and by the live tests.
/// </remarks>
internal sealed class UnreachableAgent :
    IRemoteStorageAgentClient,
    IRemoteConnectionProfileClient,
    ISyncManagementAgentClient,
    IScheduleManagementAgentClient
{
    private static Task<T> Unreachable<T>() =>
        throw new NotSupportedException("This test must not reach the agent.");

    Task<ConnectionListResponse> IRemoteStorageAgentClient.ListConnectionsAsync(
        ConnectionListRequest request,
        CancellationToken cancellationToken) => Unreachable<ConnectionListResponse>();
    Task<ConnectionTestResponse> IRemoteStorageAgentClient.TestConnectionAsync(
        ConnectionTestRequest request,
        CancellationToken cancellationToken) => Unreachable<ConnectionTestResponse>();
    Task<StorageListPageResponse> IRemoteStorageAgentClient.ListStorageAsync(
        StorageListPageRequest request,
        CancellationToken cancellationToken) => Unreachable<StorageListPageResponse>();
    Task<ConnectionProfileGetResponse> IRemoteConnectionProfileClient.GetAsync(
        ConnectionProfileGetRequest request,
        CancellationToken cancellationToken) => Unreachable<ConnectionProfileGetResponse>();
    Task<ConnectionProfileWriteResponse> IRemoteConnectionProfileClient.CreateAsync(
        ConnectionProfileCreateRequest request,
        CancellationToken cancellationToken) => Unreachable<ConnectionProfileWriteResponse>();
    Task<ConnectionProfileWriteResponse> IRemoteConnectionProfileClient.UpdateAsync(
        ConnectionProfileUpdateRequest request,
        CancellationToken cancellationToken) => Unreachable<ConnectionProfileWriteResponse>();
    Task<ConnectionProfileWriteResponse> IRemoteConnectionProfileClient.DeleteAsync(
        ConnectionProfileDeleteRequest request,
        CancellationToken cancellationToken) => Unreachable<ConnectionProfileWriteResponse>();
    Task<ConnectionTrustGetResponse> IRemoteConnectionProfileClient.GetTrustAsync(
        ConnectionTrustGetRequest request,
        CancellationToken cancellationToken) => Unreachable<ConnectionTrustGetResponse>();
    Task<ConnectionSshHostKeyDiscoveryResponse> IRemoteConnectionProfileClient.DiscoverSshHostKeyAsync(
        ConnectionSshHostKeyDiscoveryRequest request,
        CancellationToken cancellationToken) => Unreachable<ConnectionSshHostKeyDiscoveryResponse>();
    Task<ConnectionTrustMutationResponse> IRemoteConnectionProfileClient.DecideTrustAsync(
        ConnectionTrustDecisionRequest request,
        CancellationToken cancellationToken) => Unreachable<ConnectionTrustMutationResponse>();
    Task<ConnectionTrustMutationResponse> IRemoteConnectionProfileClient.RolloverTrustAsync(
        ConnectionTrustRolloverRequest request,
        CancellationToken cancellationToken) => Unreachable<ConnectionTrustMutationResponse>();
    Task<SyncProfileListResponse> ISyncManagementAgentClient.ListProfilesAsync(
        SyncProfileListRequest request,
        CancellationToken cancellationToken) => Unreachable<SyncProfileListResponse>();
    Task<SyncProfileGetResponse> ISyncManagementAgentClient.GetProfileAsync(
        SyncProfileGetRequest request,
        CancellationToken cancellationToken) => Unreachable<SyncProfileGetResponse>();
    Task<SyncProfileMutationResponse> ISyncManagementAgentClient.CreateProfileAsync(
        SyncProfileCreateRequest request,
        CancellationToken cancellationToken) => Unreachable<SyncProfileMutationResponse>();
    Task<SyncProfileMutationResponse> ISyncManagementAgentClient.UpdateProfileAsync(
        SyncProfileUpdateRequest request,
        CancellationToken cancellationToken) => Unreachable<SyncProfileMutationResponse>();
    Task<SyncPreviewGenerateResponse> ISyncManagementAgentClient.GeneratePreviewAsync(
        SyncPreviewGenerateRequest request,
        CancellationToken cancellationToken) => Unreachable<SyncPreviewGenerateResponse>();
    Task<SyncRunStatusResponse> ISyncManagementAgentClient.GetRunStatusAsync(
        SyncRunStatusRequest request,
        CancellationToken cancellationToken) => Unreachable<SyncRunStatusResponse>();
    Task<SyncRunListResponse> ISyncManagementAgentClient.ListRunsAsync(
        SyncRunListRequest request,
        CancellationToken cancellationToken) => Unreachable<SyncRunListResponse>();
    Task<SyncPlanPageResponse> ISyncManagementAgentClient.GetPlanPageAsync(
        SyncPlanPageRequest request,
        CancellationToken cancellationToken) => Unreachable<SyncPlanPageResponse>();
    Task<SyncConflictPageResponse> ISyncManagementAgentClient.GetConflictPageAsync(
        SyncConflictPageRequest request,
        CancellationToken cancellationToken) => Unreachable<SyncConflictPageResponse>();
    Task<SyncApproveDispatchResponse> ISyncManagementAgentClient.ApproveAndDispatchAsync(
        SyncApproveDispatchRequest request,
        CancellationToken cancellationToken) => Unreachable<SyncApproveDispatchResponse>();
    Task<ScheduleListResponse> IScheduleManagementAgentClient.ListAsync(
        ScheduleListRequest request,
        CancellationToken cancellationToken) => Unreachable<ScheduleListResponse>();
    Task<ScheduleGetResponse> IScheduleManagementAgentClient.GetAsync(
        ScheduleGetRequest request,
        CancellationToken cancellationToken) => Unreachable<ScheduleGetResponse>();
    Task<ScheduleMutationResponse> IScheduleManagementAgentClient.CreateAsync(
        ScheduleCreateRequest request,
        CancellationToken cancellationToken) => Unreachable<ScheduleMutationResponse>();
    Task<ScheduleMutationResponse> IScheduleManagementAgentClient.UpdateAsync(
        ScheduleUpdateRequest request,
        CancellationToken cancellationToken) => Unreachable<ScheduleMutationResponse>();
    Task<ScheduleMutationResponse> IScheduleManagementAgentClient.SetEnabledAsync(
        ScheduleSetEnabledRequest request,
        CancellationToken cancellationToken) => Unreachable<ScheduleMutationResponse>();
    Task<ScheduleMutationResponse> IScheduleManagementAgentClient.DeleteAsync(
        ScheduleDeleteRequest request,
        CancellationToken cancellationToken) => Unreachable<ScheduleMutationResponse>();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
