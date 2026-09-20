using System.Text;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// A sync built, previewed, approved and dispatched through the screens, against a live agent.
/// </summary>
/// <remarks>
/// <para>
/// Skipped unless STORAGEHUB_LIVE_AGENT is set. The sync engine itself is proven against real
/// servers elsewhere -- <c>SftpS3SyncLabIntegrationTests</c> mirrors SFTP into S3, and
/// <c>ScheduledSyncEndToEndTests</c> drives a schedule through to moved bytes -- so what is left
/// unproven, and what this is for, is the desktop half: that the editor builds a draft the agent
/// accepts, that previewing produces a run the review screen can load, that approving it actually
/// dispatches, and that the files then move.
/// </para>
/// <para>
/// It syncs between two temporary directories through Local connections. The provider path is not
/// what is under test here and the two suites above already cover it against real servers; using
/// local directories is what lets this assert on the destination's contents directly, which is the
/// only assertion that proves the whole chain rather than that each link answered.
/// </para>
/// <para>
/// Everything it makes is named so a cancelled run leaves something recognisable, and it removes
/// its connections on the way out. A sync profile cannot be deleted -- the contract has no such
/// call -- so it reuses the one it made last time.
/// </para>
/// </remarks>
public class LiveSyncTests
{
    private const string SourceName = "storagehub-live-sync-source";
    private const string DestinationName = "storagehub-live-sync-destination";
    private const string ProfileName = "storagehub-live-sync";
    private const string RemoteName = "storagehub-live-sync-sftp";
    private const string KeyName = "storagehub-live-sync-key";
    private const string SftpProfileName = "storagehub-live-sync-over-sftp";
    private const string FileName = "photo.txt";
    private const string Contents = "the bytes that have to arrive";

    private static bool Enabled =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STORAGEHUB_LIVE_AGENT"));

    [Fact]
    public async Task ASyncBuiltInTheEditorRunsAndMovesBytes()
    {
        Assert.SkipUnless(Enabled, "Set STORAGEHUB_LIVE_AGENT to run against a live agent.");
        var token = TestContext.Current.CancellationToken;

        var source = Directory.CreateTempSubdirectory("storagehub-live-sync-a");
        var destination = Directory.CreateTempSubdirectory("storagehub-live-sync-b");
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(source.FullName, FileName), Contents, Encoding.UTF8, token);

            await RemoveConnectionsAsync(token);
            var sourceId = await MakeConnectionAsync(SourceName, source.FullName, token);
            var destinationId = await MakeConnectionAsync(DestinationName, destination.FullName, token);

            // 1. The editor, exactly as the window drives it.
            var editor = SyncProfileEditorModel.Create(Sync, Storage);
            await editor.LoadAsync(token);

            Assert.Contains(editor.Connections, choice => choice.ConnectionId == sourceId);
            Assert.Contains(editor.Connections, choice => choice.ConnectionId == destinationId);

            // Reuse the profile from a previous run, because there is no way to delete one.
            if (editor.Profiles.FirstOrDefault(
                choice => choice.DisplayName == ProfileName) is { } existing)
            {
                await editor.OpenAsync(existing.ProfileId, token);
            }

            editor.Name = ProfileName;
            editor.Enabled = true;
            editor.LocationA = editor.Connections.Single(c => c.ConnectionId == sourceId);
            editor.LocationARoot = string.Empty;
            editor.LocationB = editor.Connections.Single(c => c.ConnectionId == destinationId);
            editor.LocationBRoot = string.Empty;
            editor.Behavior = editor.Behaviors.Single(
                option => option.Behavior == SyncIpcBehavior.UpdateAToB);

            // 2. Previewing saves the profile and asks the agent to scan and plan.
            SyncRunSummary? previewed = null;
            editor.PreviewReady += (_, run) => previewed = run;
            await editor.PreviewAsync(token);

            Assert.True(
                previewed is not null,
                $"The agent did not produce a plan: {editor.Status.Text}");

            // 3. The review screen loads that run, and finds the file in its plan.
            var dialogs = new ApprovingDialogs();
            using var review = SyncRunHistoryModel.Create(Sync, dialogs);
            await review.LoadRunAsync(previewed!.SyncRunId, token);

            Assert.True(review.CanApprove, $"The run cannot be approved: {review.PlanStatus.Text}");
            Assert.Contains(review.Plan, row => row.To.EndsWith(FileName, StringComparison.Ordinal));

            // 4. Approving dispatches it, and the agent then executes it.
            await review.ApproveAsync(token);
            Assert.False(review.CanApprove, $"The run was not dispatched: {review.PlanStatus.Text}");

            // 5. Which is only believable once the bytes are on the other side.
            var arrived = Path.Combine(destination.FullName, FileName);
            await WaitForAsync(() => File.Exists(arrived), token);

            Assert.True(
                File.Exists(arrived),
                $"The sync did not copy the file. Run status: {review.PlanStatus.Text}");
            Assert.Equal(Contents, await File.ReadAllTextAsync(arrived, token));
        }
        finally
        {
            await RemoveConnectionsAsync(CancellationToken.None);
            Delete(source);
            Delete(destination);
        }
    }

    /// <summary>
    /// A schedule made, listed, disabled and deleted through the manager, against a live agent.
    /// </summary>
    /// <remarks>
    /// That a due schedule moves bytes is proven by <c>ScheduledSyncEndToEndTests</c> against real
    /// servers. What is unproven without this is that the manager's draft is one the agent accepts
    /// -- the cron the builder produces, the region it names, the grace period in the units the
    /// contract wants -- and that the agent answers with a next occurrence at all.
    /// </remarks>
    [Fact]
    public async Task AScheduleBuiltInTheManagerIsAcceptedByTheAgent()
    {
        Assert.SkipUnless(Enabled, "Set STORAGEHUB_LIVE_AGENT to run against a live agent.");
        var token = TestContext.Current.CancellationToken;

        var source = Directory.CreateTempSubdirectory("storagehub-live-schedule-a");
        var destination = Directory.CreateTempSubdirectory("storagehub-live-schedule-b");
        try
        {
            await RemoveConnectionsAsync(token);
            var sourceId = await MakeConnectionAsync(SourceName, source.FullName, token);
            var destinationId = await MakeConnectionAsync(DestinationName, destination.FullName, token);
            var profileId = await EnsureProfileAsync(sourceId, destinationId, token);
            await RemoveSchedulesAsync(token);

            var dialogs = new ApprovingDialogs();
            var manager = ScheduleManagerModel.Create(Schedules, Sync, dialogs);
            await manager.LoadAsync(token);

            Assert.False(
                manager.Status.IsDanger,
                $"The manager could not load: {manager.Status.Text}");
            var profile = Assert.Single(manager.Profiles, choice => choice.ProfileId == profileId);

            // Every weekday at 07:30, which is the builder producing "30 7 * * 1-5".
            manager.BeginNewSchedule();
            manager.Profile = profile;
            manager.Frequency = ScheduleManagerModel.Frequencies
                .Single(choice => choice.Frequency == ScheduleFrequency.Weekdays);
            manager.TimeOfDay = new TimeSpan(7, 30, 0);
            manager.ExecutionMode = ScheduleManagerModel.ExecutionModes
                .Single(mode => mode.Mode == ScheduleIpcExecutionMode.PreviewOnly);
            manager.Enabled = true;
            Assert.Equal("30 7 * * 1-5", manager.BuildDraft().CronExpression);

            // Deliberately left on this machine's own zone rather than forced to UTC. A schedule in
            // any zone with a real offset could not be created at all until this test found it, and
            // pinning UTC here would have hidden it exactly as the repository's own tests did.
            Assert.Equal(TimeZoneInfo.Local.Id, manager.TimeZone!.Id);

            await manager.SaveAsync(token);
            Assert.False(
                manager.Status.IsDanger,
                $"The agent refused the schedule: {manager.Status.Text}");
            var saved = Assert.Single(manager.Schedules, row => row.Profile == ProfileName);

            // The agent works out the next occurrence, so a real one proves it read the cron and
            // the region rather than merely storing them.
            Assert.False(
                saved.NextRun == Ui.Schedules.NoFutureRun ||
                    saved.NextRun == Ui.Schedules.NotScheduledUntilSaved,
                $"The agent did not schedule an occurrence: {manager.Status.Text}");

            // Disabling is a change of its own, and takes the revision the save returned.
            await manager.SetEnabledAsync(false, token);
            Assert.Equal(
                Ui.Sync.TaskDisabled,
                Assert.Single(manager.Schedules, row => row.Profile == ProfileName).State);

            await manager.DeleteAsync(token);
            Assert.DoesNotContain(manager.Schedules, row => row.Profile == ProfileName);
        }
        finally
        {
            await RemoveSchedulesAsync(CancellationToken.None);
            await RemoveConnectionsAsync(CancellationToken.None);
            Delete(source);
            Delete(destination);
        }
    }

    /// <summary>
    /// The same thing over SFTP, against the test lab's server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The other sync test uses two local directories so it can read the destination off the disk.
    /// This one gives that up to prove the part it cannot: that the desktop can stand up an SFTP
    /// connection end to end -- enrolling the key and its passphrase in the vault, pinning the host
    /// key, and only then syncing -- and that the bytes arrive on a real server.
    /// </para>
    /// <para>
    /// It needs eng/testlab running, and points at the key-only sshd because an encrypted private
    /// key is the authentication StorageHub offers for SFTP. It writes one file with a fixed name
    /// into a fixed root, so a second run overwrites rather than accumulates.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ASyncToTheSftpServerCarriesTheBytesOverTheWire()
    {
        Assert.SkipUnless(Enabled, "Set STORAGEHUB_LIVE_AGENT to run against a live agent.");
        Assert.SkipUnless(LabConfigured, "Start eng/testlab and dot-source its env.ps1.");
        var token = TestContext.Current.CancellationToken;

        var source = Directory.CreateTempSubdirectory("storagehub-live-sftp");
        try
        {
            var payload = $"over the wire at {DateTimeOffset.UtcNow:O}";
            await File.WriteAllTextAsync(
                Path.Combine(source.FullName, FileName), payload, Encoding.UTF8, token);

            await RemoveConnectionsAsync(token);
            var localId = await MakeConnectionAsync(SourceName, source.FullName, token);
            var remoteId = await MakeSftpConnectionAsync(token);

            // The pinned host key, decided before anything tries to open the connection. Without it
            // the agent refuses every session, which is what pinning is for.
            await TrustAsync(remoteId, Lab("STORAGEHUB_SYNCLAB_SFTP_HOST_SHA256"), token);

            var editor = SyncProfileEditorModel.Create(Sync, Storage);
            await editor.LoadAsync(token);
            if (editor.Profiles.FirstOrDefault(
                choice => choice.DisplayName == SftpProfileName) is { } existing)
            {
                await editor.OpenAsync(existing.ProfileId, token);
            }

            editor.Name = SftpProfileName;
            editor.Enabled = true;
            editor.LocationA = editor.Connections.Single(c => c.ConnectionId == localId);
            editor.LocationARoot = string.Empty;
            editor.LocationB = editor.Connections.Single(c => c.ConnectionId == remoteId);
            editor.LocationBRoot = string.Empty;
            editor.Behavior = editor.Behaviors.Single(
                option => option.Behavior == SyncIpcBehavior.UpdateAToB);

            SyncRunSummary? previewed = null;
            editor.PreviewReady += (_, run) => previewed = run;

            // Set rather than left alone: the profile is reused between runs, so it comes back
            // carrying whatever the last run finished with -- which is exactly the value under test.
            editor.AllowNonAtomicWrites = false;

            // First as the editor offers it, which for SFTP is refused. The server cannot create a
            // file in one step -- no ConditionalCreate, no AtomicRename, no TemporaryFiles -- so
            // StorageHub will not write to it without being told that is acceptable. The refusal
            // has to arrive here, at the preview, and say what to do about it.
            await editor.PreviewAsync(token);

            Assert.True(previewed is null, "An SFTP destination was planned without being allowed.");
            Assert.Equal(Ui.Sync.OperationNotSafeHere, editor.Status.Text);

            // Then as it has to be set for an endpoint like this one.
            editor.AllowNonAtomicWrites = true;
            await editor.PreviewAsync(token);

            Assert.True(
                previewed is not null,
                $"The agent could not scan the SFTP server: {editor.Status.Text}");

            var dialogs = new ApprovingDialogs();
            using var review = SyncRunHistoryModel.Create(Sync, dialogs);
            await review.LoadRunAsync(previewed!.SyncRunId, token);
            Assert.True(review.CanApprove, $"The run cannot be approved: {review.PlanStatus.Text}");

            await review.ApproveAsync(token);

            // Read back through a pane, which is the desktop's own view of the server rather than
            // a second way of asking the same question.
            await using var pane = new BrowserPaneModel(new NamedPipeRemoteStorageAgentClient());
            await pane.LoadConnectionsAsync(token);

            BrowserListItem? arrived = null;
            for (var attempt = 0; attempt < 40 && arrived is null; attempt++)
            {
                if (attempt > 0) await Task.Delay(250, token);
                await pane.OpenConnectionAsync(remoteId, token);
                arrived = pane.Rows.FirstOrDefault(row => row.Name == FileName);
            }

            Assert.True(
                arrived is not null,
                "The file never reached the SFTP server. " +
                await DescribeRunAsync(previewed.SyncRunId, token));

            // The name alone would pass against a file left by an earlier run, so the length has to
            // match. Taken from the file on disk rather than from the payload, because
            // Encoding.UTF8 writes a byte-order mark and GetByteCount does not count it.
            Assert.Equal(
                new FileInfo(Path.Combine(source.FullName, FileName)).Length, arrived!.Length);
        }
        finally
        {
            await RemoveConnectionsAsync(CancellationToken.None);
            Delete(source);
        }
    }

    /// <summary>
    /// An SFTP connection, built the way the Connection Manager builds one.
    /// </summary>
    /// <remarks>
    /// The key and its passphrase go into the vault first, and the connection stores only the
    /// references -- which is the whole point of the vault: the profile the agent keeps never holds
    /// key material. Enrolling goes through the controller because the key store screen that will
    /// do it is not ported yet, and this is the call it will make.
    /// </remarks>
    /// <summary>
    /// The SFTP connection, with its key imported through the key store and borrowed from it.
    /// </summary>
    /// <remarks>
    /// The key store is how a person gets a key into a connection, so it is how this does too:
    /// one import, then the editor's own "Key Store…" fills the material and passphrase fields
    /// together. The entry is removed with the connection, after it, since it cannot go first.
    /// </remarks>
    private static async Task<Guid> MakeSftpConnectionAsync(CancellationToken cancellationToken)
    {
        var imported = await new KeyStoreController(KeyStore, Vault)
            .ImportAsync(LiveKeyStoreTests.Draft(KeyName), cancellationToken);
        Assert.True(imported.Changed, $"The key could not be imported: {imported.ErrorMessage}");

        var editor = new ConnectionEditorModel(
            Controller,
            keyStore: KeyStore,
            pickKey: entries => Task.FromResult(entries.FirstOrDefault(entry => entry.DisplayName == KeyName)));
        editor.Provider = ConnectionProviderCatalog.Get(StorageProviderKind.Sftp);
        Field(editor, "profileName").Value = RemoteName;
        Field(editor, "host").Value = "127.0.0.1";
        Field(editor, "port").Value = Lab("STORAGEHUB_SYNCLAB_SFTP_PORT");
        Field(editor, "initialPath").Value = "/" + Lab("STORAGEHUB_SYNCLAB_SFTP_ROOT");
        Field(editor, "username").Value = Lab("STORAGEHUB_SYNCLAB_SFTP_USERNAME");
        Field(editor, "authenticationMode").Value = "Private key reference";
        Field(editor, "hostKeyFingerprint").Value = Lab("STORAGEHUB_SYNCLAB_SFTP_HOST_SHA256");

        await editor.ChooseFromKeyStoreAsync(Field(editor, "privateKeyReference"), cancellationToken);
        Assert.Equal(imported.Entry!.MaterialReference, Field(editor, "privateKeyReference").Value);
        Assert.Equal(imported.Entry.PassphraseReference, Field(editor, "privateKeyPassphraseReference").Value);

        await editor.SaveAsync(cancellationToken);
        Assert.False(editor.IsNew, $"The agent did not store the SFTP connection: {editor.Status}");

        return Assert.Single(
            await ListAsync(cancellationToken),
            entry => entry.DisplayName == RemoteName).ConnectionId;
    }

    /// <summary>Pins the server's host key, which a pinned connection cannot open without.</summary>
    private static async Task TrustAsync(
        Guid connectionId,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var controller = Controller();
        var profile = (await controller.GetAsync(connectionId, cancellationToken)).Profile;
        Assert.True(profile is not null, "The saved SFTP connection could not be read back.");

        var trusted = await controller
            .TrustOrRolloverAsync(profile!, fingerprint, cancellationToken)
            .ConfigureAwait(false);

        Assert.True(
            trusted.Status == ConnectionTrustMutationStatus.Succeeded,
            $"The host key could not be pinned: {trusted.Failure?.Message}");
    }

    /// <summary>
    /// What became of a run, for a failure message worth reading.
    /// </summary>
    /// <remarks>
    /// "The file did not arrive" says nothing about why. The phase and status code are what the
    /// agent decided, and the conflicts are what it decided it about -- and a run that stops with
    /// uncertain provider state records the reason there rather than anywhere a screen would show.
    /// </remarks>
    private static async Task<string> DescribeRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var client = Sync();
        var status = await client.GetRunStatusAsync(
            new SyncRunStatusRequest(SyncManagementIpcContract.CurrentVersion, runId),
            cancellationToken).ConfigureAwait(false);

        var conflicts = await client.GetConflictPageAsync(
            new SyncConflictPageRequest(SyncManagementIpcContract.CurrentVersion, runId),
            cancellationToken).ConfigureAwait(false);

        var described = conflicts.Conflicts
            .Select(static conflict => $"{conflict.RelativePath}: {conflict.ConflictKind} ({conflict.SafeReason})");

        return $"phase={status.Run?.Phase} status={status.Run?.StatusCode} " +
            $"dispatch={status.Run?.DispatchState} conflicts=[{string.Join("; ", described)}]";
    }

    private static bool LabConfigured =>
        !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("STORAGEHUB_SYNCLAB_SFTP_PORT"));

    private static string Lab(string name) =>
        Environment.GetEnvironmentVariable(name) ??
        throw new InvalidOperationException($"{name} is not set; dot-source the lab's env.ps1.");

    /// <summary>
    /// Clears any schedule left on this profile.
    /// </summary>
    /// <remarks>
    /// A profile can carry several schedules, so a run that stopped before its delete leaves one
    /// behind and the next run finds two. Cleared at both ends rather than only at the end, because
    /// the run that leaves one behind is by definition the one that did not reach its finally.
    /// </remarks>
    private static async Task RemoveSchedulesAsync(CancellationToken cancellationToken)
    {
        await using var client = Schedules();
        var listed = await client
            .ListAsync(new ScheduleListRequest(IncludeDisabled: true), cancellationToken)
            .ConfigureAwait(false);

        foreach (var schedule in listed.Schedules.Where(
            static schedule => schedule.ProfileDisplayName == ProfileName))
        {
            _ = await client.DeleteAsync(
                new ScheduleDeleteRequest(
                    ScheduleManagementIpcContract.CurrentVersion,
                    schedule.ScheduleId,
                    schedule.Revision),
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The profile the schedule needs, made or reused.</summary>
    private static async Task<Guid> EnsureProfileAsync(
        Guid sourceId,
        Guid destinationId,
        CancellationToken cancellationToken)
    {
        var editor = SyncProfileEditorModel.Create(Sync, Storage);
        await editor.LoadAsync(cancellationToken);

        if (editor.Profiles.FirstOrDefault(
            choice => choice.DisplayName == ProfileName) is { } existing)
        {
            await editor.OpenAsync(existing.ProfileId, cancellationToken);
        }

        editor.Name = ProfileName;
        editor.LocationA = editor.Connections.Single(c => c.ConnectionId == sourceId);
        editor.LocationB = editor.Connections.Single(c => c.ConnectionId == destinationId);
        editor.LocationARoot = string.Empty;
        editor.LocationBRoot = string.Empty;
        await editor.SaveAsync(cancellationToken);

        await using var client = Sync();
        var profiles = await client
            .ListProfilesAsync(new SyncProfileListRequest(IncludeDisabled: true), cancellationToken)
            .ConfigureAwait(false);
        return Assert.Single(profiles.Profiles, p => p.DisplayName == ProfileName).ProfileId;
    }

    private static async Task<Guid> MakeConnectionAsync(
        string name,
        string root,
        CancellationToken cancellationToken)
    {
        var editor = new ConnectionEditorModel(Controller);
        editor.Provider = ConnectionProviderCatalog.Get(StorageProviderKind.Local);
        Field(editor, "profileName").Value = name;
        Field(editor, "rootPath").Value = root;

        await editor.SaveAsync(cancellationToken);
        Assert.False(editor.IsNew, $"The agent did not store '{name}': {editor.Status}");

        return Assert.Single(
            await ListAsync(cancellationToken), entry => entry.DisplayName == name).ConnectionId;
    }

    /// <summary>
    /// Waits for the agent to finish, rather than assuming it already has.
    /// </summary>
    /// <remarks>
    /// A dispatch is only a promise that the request is durable; the providers run afterwards. The
    /// wait is what turns "the agent agreed" into "the file is there", which is the difference this
    /// whole test exists to check.
    /// </remarks>
    private static async Task WaitForAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline && !condition())
        {
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }
    }

    private static ConnectionFieldModel Field(ConnectionEditorModel editor, string key) =>
        editor.Sections.SelectMany(static section => section.Fields).First(field => field.Key == key);

    private static ISyncManagementAgentClient Sync() => new NamedPipeSyncManagementAgentClient();

    private static IRemoteStorageAgentClient Storage() => new NamedPipeRemoteStorageAgentClient();

    private static IScheduleManagementAgentClient Schedules() =>
        new NamedPipeScheduleManagementAgentClient();

    private static ConnectionManagerController Controller() => new(
        new NamedPipeRemoteConnectionProfileClient(),
        new NamedPipeRemoteSecretVaultClient());

    private static async Task<IReadOnlyList<ConnectionSummary>> ListAsync(CancellationToken cancellationToken)
    {
        await using var client = new NamedPipeRemoteStorageAgentClient();
        var response = await client
            .ListConnectionsAsync(new ConnectionListRequest(IncludeDisabled: true), cancellationToken)
            .ConfigureAwait(false);
        return response.Connections;
    }

    private static async Task RemoveConnectionsAsync(CancellationToken cancellationToken)
    {
        var listed = await ListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var entry in listed.Where(
            static entry => entry.DisplayName is SourceName or DestinationName or RemoteName))
        {
            _ = await Controller()
                .DeleteAsync(entry.ConnectionId, entry.Version, cancellationToken)
                .ConfigureAwait(false);
        }

        // After the connections, because an entry a connection names cannot be deleted.
        var store = new KeyStoreController(KeyStore, Vault);
        var keys = await store.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        foreach (var key in keys.Entries.Where(static key => key.DisplayName == KeyName))
        {
            _ = await store.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
        }
    }

    private static NamedPipeKeyStoreAgentClient KeyStore() => new();

    private static NamedPipeRemoteSecretVaultClient Vault() => new();

    private static void Delete(DirectoryInfo directory)
    {
        try
        {
            directory.Delete(recursive: true);
        }
        catch (IOException)
        {
            // A provider may still hold a handle; the temp directory is the platform's problem.
        }
    }

    /// <summary>Says yes, because the person who would be asked is the test.</summary>
    private sealed class ApprovingDialogs : IDialogService
    {
        public Task ShowAsync(DialogRequest request, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<DialogChoice> ConfirmAsync(
            DialogRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(
                request.Buttons == DialogButtons.YesNo ? DialogChoice.Yes : DialogChoice.Ok);

        public Task<string?> PromptAsync(
            DialogPromptRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
