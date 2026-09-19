using System.Reflection;
using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Every window the application opens is painted in the application's own palette.
/// </summary>
/// <remarks>
/// A <see cref="Form"/> that never asks for the theme is not subtly wrong: WinForms hands it the
/// system colours, so it opens as a white dialog in front of a dark application. That is exactly
/// what the New Workspace chooser did, and nothing failed -- it built, it laid out, its captions
/// fitted, and the fault was visible only to somebody who opened it while running dark.
///
/// So it is checked two ways. Each window is built and its background compared to the palette, and
/// the list of windows built here is compared against every Form in the assembly -- which is what
/// makes the next dialog that forgets the theme fail here rather than in a screenshot.
/// </remarks>
public sealed class DialogThemeTests
{
    /// <summary>
    /// The one window deliberately outside the application palette: a terminal keeps terminal
    /// colours, dark whatever the rest of the shell is set to, because what it shows is a remote
    /// program's idea of a screen rather than StorageHub's.
    /// </summary>
    private static readonly string[] DeliberatelyUnthemed = [nameof(SshTerminalForm)];

    [Fact]
    public void EveryWindowIsPaintedInTheApplicationPalette()
    {
        SyncRunReviewControlTests.RunOnSta(() => WithDialogs(dialogs =>
        {
            var palette = StorageHubTheme.CurrentPalette;
            foreach (var (name, dialog) in dialogs)
            {
                Assert.True(
                    dialog.BackColor == palette.Canvas || dialog.BackColor == palette.Surface,
                    $"{name} opens on {dialog.BackColor}, which is neither the canvas nor a " +
                    "surface: it is a bright dialog in front of a dark application.");
            }
        }));
    }

    [Fact]
    public void EveryWindowIsRegisteredForAppearanceChanges()
    {
        SyncRunReviewControlTests.RunOnSta(() => WithDialogs(dialogs =>
        {
            var registered = RegisteredWindows();
            foreach (var (name, dialog) in dialogs)
            {
                Assert.True(
                    registered.Any(window => ReferenceEquals(window, dialog)),
                    $"{name} is not registered with the appearance service, so switching Light or " +
                    "Dark while it is open leaves it in the palette it was built in.");
            }
        }));
    }

    /// <summary>
    /// The windows the appearance service will repaint. Read through reflection rather than
    /// asserted by switching appearance, because the service marshals its repaint onto the first
    /// registered window with a handle -- which, in a test process where earlier tests have left
    /// their own windows on that list, is a window belonging to a thread that no longer pumps.
    /// </summary>
    private static List<Form> RegisteredWindows()
    {
        var field = typeof(DesktopAppearanceService).GetField(
            "RegisteredWindows",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var references = Assert.IsType<List<WeakReference<Form>>>(field.GetValue(null));
        var windows = new List<Form>();
        foreach (var reference in references)
        {
            if (reference.TryGetTarget(out var form))
            {
                windows.Add(form);
            }
        }

        return windows;
    }

    /// <summary>
    /// Nothing may be left out quietly: a new window either appears in <see cref="CreateDialogs"/>
    /// or is named as deliberately unthemed, with the reason written down.
    /// </summary>
    [Fact]
    public void EveryFormInTheAssemblyIsAccountedFor()
    {
        var forms = typeof(MainForm).Assembly
            .GetTypes()
            .Where(type => type.IsSubclassOf(typeof(Form)) && !type.IsAbstract)
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        var covered = new HashSet<string>(DeliberatelyUnthemed, StringComparer.Ordinal);
        SyncRunReviewControlTests.RunOnSta(() => WithDialogs(dialogs =>
        {
            foreach (var (_, dialog) in dialogs)
            {
                covered.Add(dialog.GetType().Name);
            }
        }));

        var missing = forms.Except(covered).OrderBy(name => name, StringComparer.Ordinal).ToList();
        Assert.True(
            missing.Count == 0,
            "These windows are never checked for the theme: " + string.Join(", ", missing) +
            ". Add each to DialogThemeTests.CreateDialogs, or to DeliberatelyUnthemed with a reason.");
    }

    /// <summary>
    /// Builds every window once, hands them to <paramref name="assert"/>, and disposes them and
    /// the temporary settings directory they were given afterwards.
    /// </summary>
    private static void WithDialogs(Action<IReadOnlyList<(string Name, Form Dialog)>> assert)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"storagehub-theme-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var dialogs = new List<(string Name, Form Dialog)>();
        try
        {
            dialogs.AddRange(CreateDialogs(directory));
            foreach (var (_, dialog) in dialogs)
            {
                // The appearance service marshals its repaint onto the first registered window
                // that has a handle, so a set of windows that were never created is never
                // repainted -- and the check below would pass for the wrong reason.
                dialog.CreateControl();
            }

            assert(dialogs);
        }
        finally
        {
            foreach (var (_, dialog) in dialogs)
            {
                dialog.Dispose();
            }

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // A temp directory that will not delete is not a test failure.
            }
        }
    }

    private static IEnumerable<(string Name, Form Dialog)> CreateDialogs(string directory)
    {
        var store = new DesktopConfigStore(directory);
        var exporter = new SettingsExportService(
            store,
            clock: () => DateTimeOffset.UnixEpoch,
            application: () => "StorageHub test",
            fingerprint: () => "test-fingerprint");

        yield return (nameof(MainForm), new MainForm());
        yield return (nameof(StorageHubSplashForm), new StorageHubSplashForm());
        yield return (nameof(SettingsForm), new SettingsForm());
        yield return (nameof(ConnectionManagerForm), new ConnectionManagerForm());
        yield return (nameof(SyncProfileEditorForm), new SyncProfileEditorForm());
        yield return (nameof(ScheduleManagerForm), new ScheduleManagerForm());
        yield return (nameof(SettingsExportForm), new SettingsExportForm(exporter));
        yield return (
            nameof(SettingsImportForm),
            new SettingsImportForm(new SettingsImportService(
                store, exporter, Path.Combine(directory, "backups"), () => DateTimeOffset.UnixEpoch)));
        yield return (nameof(NewWorkspaceForm), new NewWorkspaceForm(WorkspaceLayout.SideBySide));
        yield return (nameof(UpdateCheckerForm), new UpdateCheckerForm(new DesktopUpdater(store)));
        yield return (nameof(AgentControlForm), new AgentControlForm(static () => null, controller: null));
        yield return (nameof(AgentHostModeSetupForm), new AgentHostModeSetupForm());
        // Given its report rather than left to inspect the machine: the theme check only cares
        // that the window paints, and a real check would read this machine.s services.
        yield return (
            nameof(InstallationCheckForm),
            new InstallationCheckForm(
                StorageHub.Agent.AgentHostMode.UserSession,
                () => new StorageHub.Agent.InstallationReport(
                    StorageHub.Agent.AgentHostMode.UserSession,
                    [
                        new StorageHub.Agent.InstallationFinding(
                            "Database",
                            StorageHub.Agent.InstallationCheckStatus.Ok,
                            "Present.",
                            @"C:\temp\storagehub.db"),
                    ]),
                _ => new StorageHub.Agent.InstallationRepairResult(true, "done")));
        yield return (nameof(KeyStorePickerForm), new KeyStorePickerForm([]));
        yield return (
            nameof(IconPickerForm),
            new IconPickerForm(currentKey: null, StorageHubTheme.Primary, "Choose an icon"));
        yield return (
            nameof(PaneItemNameDialog),
            new PaneItemNameDialog("New folder", "Name", "New folder", "Create"));
        yield return (nameof(BatchRenameDialog), new BatchRenameDialog(["one.txt"], []));
        yield return (
            nameof(DeleteItemsConfirmationForm),
            new DeleteItemsConfirmationForm(
                [PaneTransferItem.Create("report.txt", "reports/report.txt", StorageItemKind.File, 1024).Value],
                local: true));
        yield return (
            nameof(UnsafeExternalEditWarningForm),
            Construct<UnsafeExternalEditWarningForm>(["report.txt"]));
        yield return (nameof(AboutForm), Construct<AboutForm>([]));
        yield return (
            nameof(ClearTransferHistoryConfirmationForm), new ClearTransferHistoryConfirmationForm());
        yield return (nameof(SecretPromptForm), new SecretPromptForm("Passphrase"));
        yield return (nameof(TextPromptForm), new TextPromptForm("Name this entry", "id_ed25519"));
        yield return (nameof(KeyFormatPromptForm), new KeyFormatPromptForm());
        yield return (
            nameof(KeyStoreForm),
            new KeyStoreForm(new PendingKeyStoreClient(), new FakeSecretVaultClient()));
        yield return (
            nameof(ObjectInspectorForm),
            new ObjectInspectorForm(
                new ObjectInspectorController(
                    new PendingInspectorClient(),
                    new ObjectInspectorAddress(Guid.NewGuid(), new string('a', 64), "reports/report.txt")),
                ownsController: true));
        yield return (
            nameof(SyncLocationPickerForm),
            new SyncLocationPickerForm(
                new FakeStorageClient(SyncPickerConnectionId),
                new ConnectionSummary(
                    SyncPickerConnectionId,
                    "Design Archive",
                    StorageConnectionProvider.Local,
                    FolderPath: null,
                    Tags: [],
                    IsFavorite: false,
                    IsEnabled: true,
                    IconKey: null,
                    AccentColor: null,
                    Version: 1),
                string.Empty,
                "Left"));
    }

    private static readonly Guid SyncPickerConnectionId = Guid.Parse("7f7d2d29-6d3a-4a3f-9c5f-2f6b4f1d0c11");

    /// <summary>
    /// Builds a window whose constructor is private because the application opens it through a
    /// static entry point. The alternative is widening those constructors for a test, which makes
    /// the window's contract worse in order to check it.
    /// </summary>
    private static Form Construct<T>(object[] arguments)
        where T : Form
    {
        var constructor = typeof(T)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == arguments.Length);
        return (Form)constructor.Invoke(arguments);
    }

    /// <summary>
    /// Agent clients whose every call is still in flight.
    /// </summary>
    /// <remarks>
    /// A window that talks to the agent starts loading as it opens. These clients never answer, so
    /// each window stays in the state it opens in for as long as the test holds it -- which is the
    /// state whose colours are being checked. Returning real responses would mean reproducing a
    /// dozen message shapes to check a background colour.
    /// </remarks>
    private static Task<T> Pending<T>() => new TaskCompletionSource<T>().Task;

    private sealed class PendingKeyStoreClient : IKeyStoreAgentClient
    {
        public Task<KeyStoreListResponse> ListAsync(KeyStoreListRequest request, CancellationToken cancellationToken = default) => Pending<KeyStoreListResponse>();

        public Task<KeyStoreWriteResponse> CreateAsync(KeyStoreCreateRequest request, CancellationToken cancellationToken = default) => Pending<KeyStoreWriteResponse>();

        public Task<KeyStoreWriteResponse> UpdateAsync(KeyStoreUpdateRequest request, CancellationToken cancellationToken = default) => Pending<KeyStoreWriteResponse>();

        public Task<KeyStoreWriteResponse> DeleteAsync(KeyStoreDeleteRequest request, CancellationToken cancellationToken = default) => Pending<KeyStoreWriteResponse>();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class PendingInspectorClient : IObjectInspectorAgentClient
    {
        public Task<ObjectVersionListResponse> ListVersionsAsync(ObjectVersionListRequest request, CancellationToken cancellationToken = default) => Pending<ObjectVersionListResponse>();

        public Task<ObjectMetadataGetResponse> GetMetadataAsync(ObjectMetadataGetRequest request, CancellationToken cancellationToken = default) => Pending<ObjectMetadataGetResponse>();

        public Task<ObjectTagsGetResponse> GetTagsAsync(ObjectTagsGetRequest request, CancellationToken cancellationToken = default) => Pending<ObjectTagsGetResponse>();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
