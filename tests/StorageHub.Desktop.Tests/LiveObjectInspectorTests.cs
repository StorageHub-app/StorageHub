using StorageHub.Contracts.Ipc;
using StorageHub.Desktop;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// An object made through a pane and inspected through the inspector, against the lab's S3 server.
/// </summary>
/// <remarks>
/// <para>
/// Skipped unless STORAGEHUB_LIVE_AGENT is set and eng/testlab is running. The inspector's words
/// are pinned against a scripted client in ObjectInspectorTests; what is unproven without this is
/// that a real provider answers the three calls with something the screen can show, and that the
/// address a pane builds from its listing is one the agent accepts.
/// </para>
/// <para>
/// The S3 connection's keys are enrolled the way the Connection Manager does it now: through the
/// editor's own Enroll on each secret field, with the prompt answered by the test. So this also
/// proves that a typed secret reaches the vault under the purpose the provider will ask for it by.
/// </para>
/// </remarks>
public class LiveObjectInspectorTests
{
    private const string ConnectionName = "storagehub-live-inspector-s3";
    private const string FileName = "storagehub-live-inspector.txt";

    private static bool Enabled =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STORAGEHUB_LIVE_AGENT"));

    private static bool LabConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STORAGEHUB_MINIO_ENDPOINT"));

    [Fact]
    public async Task AnObjectOnTheLabBucketCanBeInspected()
    {
        Assert.SkipUnless(Enabled, "Set STORAGEHUB_LIVE_AGENT to run against a live agent.");
        Assert.SkipUnless(LabConfigured, "Start eng/testlab and dot-source its env.ps1.");
        var token = TestContext.Current.CancellationToken;

        try
        {
            await RemoveConnectionAsync(token);

            // 1. The connection, with both keys enrolled through the editor's own secret prompt.
            var dialogs = new ScriptedDialogs();
            var editor = new ConnectionEditorModel(Controller, Storage, dialogs);
            editor.Provider = ConnectionProviderCatalog.Get(StorageProviderKind.S3);
            editor.Field("profileName")!.Value = ConnectionName;
            editor.Field("s3ServiceType")!.Value = "Other S3-compatible";
            // The lab's MinIO speaks plain HTTP, which the editor refuses on purpose: it offers no
            // way to allow an insecure S3 endpoint. The draft is built with an https placeholder
            // and rewritten below, through the contract's own flag, before it reaches the agent.
            var labEndpoint = new Uri(Lab("STORAGEHUB_MINIO_ENDPOINT"));
            editor.Field("endpoint")!.Value = "https://" + labEndpoint.Authority + "/";
            editor.Field("region")!.Value = "us-east-1";
            editor.Field("bucket")!.Value = Lab("STORAGEHUB_MINIO_BUCKET");
            editor.Field("addressingStyle")!.Value = "Path-style";

            dialogs.PromptAnswer = Lab("STORAGEHUB_MINIO_ACCESS_KEY");
            await editor.EnrollAsync(editor.Field("accessKeyReference")!, token);
            Assert.True(dialogs.LastPrompt!.Secret);
            dialogs.PromptAnswer = Lab("STORAGEHUB_MINIO_SECRET_KEY");
            await editor.EnrollAsync(editor.Field("secretAccessKeyReference")!, token);
            Assert.True(
                editor.Field("secretAccessKeyReference")!.Value.StartsWith("shs_", StringComparison.Ordinal),
                $"The secret was not enrolled: {editor.Status}");

            var draft = ConnectionEditorDraftFactory.Build(
                StorageProviderKind.S3,
                editor.Sections.SelectMany(static section => section.Fields)
                    .ToDictionary(static field => field.Key, static field => field.Value, StringComparer.Ordinal));
            draft = draft with
            {
                Endpoint = draft.Endpoint with
                {
                    ServiceEndpoint = labEndpoint.ToString(),
                    AllowInsecureTransport = labEndpoint.Scheme == Uri.UriSchemeHttp
                }
            };
            var saved = await Controller().SaveAsync(draft, null, token);
            Assert.True(saved.Profile is not null, $"The agent did not store the connection: {saved.Failure?.Message}");
            var connectionId = saved.Profile!.ConnectionId;

            // 2. A pane opens the bucket and makes an object there, as the toolbar's New file does.
            ObjectInspectorAddress? opened = null;
            await using var pane = new BrowserPaneModel(
                Storage(),
                mutations: static () => new PaneMutationController(static () => new NamedPipeObjectInspectorAgentClient()),
                dialogs: dialogs,
                inspect: address =>
                {
                    opened = address;
                    return Task.CompletedTask;
                });
            await pane.LoadConnectionsAsync(token);
            await pane.OpenConnectionAsync(connectionId, token);
            Assert.False(pane.HasStatus, $"The pane could not open the bucket: {pane.Status}");

            if (!pane.Rows.Any(row => row.Name == FileName))
            {
                dialogs.PromptAnswer = FileName;
                await pane.CreateAsync(container: false, token);
                Assert.Contains(pane.Rows, row => row.Name == FileName);
            }

            // 3. Properties, on that one file, builds the address from what the pane listed.
            pane.SelectedRows.Clear();
            pane.SelectedRows.Add(pane.Rows.Single(row => row.Name == FileName));
            Assert.True(pane.PropertiesCommand.CanExecute(null), "Properties is not offered for the file.");
            await pane.InspectAsync(token);
            Assert.NotNull(opened);
            Assert.Equal(connectionId, opened!.ConnectionId);

            // 4. The inspector, over the real agent, shows what the provider knows about it.
            await using var inspector = ObjectInspectorModel.Create(
                opened, static () => new NamedPipeObjectInspectorAgentClient());
            await inspector.LoadAsync(token);

            Assert.False(
                inspector.Status.Text == Ui.Inspector.TheObjectInspectorCouldNotLoadDetails ||
                inspector.Status.Text == Ui.Inspector.TheObjectInspectorRequestTimedOut,
                $"The inspector could not load: {inspector.Status.Text}");
            Assert.False(inspector.VersionsNotice.IsWarning, inspector.VersionsNotice.Text);
            Assert.NotEmpty(inspector.Versions);
            Assert.Contains(inspector.Versions, row => row.Current == Ui.Inspector.Latest);
            Assert.False(inspector.MetadataNotice.IsWarning, inspector.MetadataNotice.Text);
        }
        finally
        {
            await RemoveConnectionAsync(CancellationToken.None);
        }
    }

    private static string Lab(string name) =>
        Environment.GetEnvironmentVariable(name) ??
        throw new InvalidOperationException($"{name} is not set; dot-source the lab's env.ps1.");

    private static NamedPipeRemoteStorageAgentClient Storage() => new();

    private static ConnectionManagerController Controller() => new(
        new NamedPipeRemoteConnectionProfileClient(),
        new NamedPipeRemoteSecretVaultClient());

    private static async Task RemoveConnectionAsync(CancellationToken cancellationToken)
    {
        await using var storage = Storage();
        var listed = await storage
            .ListConnectionsAsync(new ConnectionListRequest(IncludeDisabled: true), cancellationToken)
            .ConfigureAwait(false);
        foreach (var entry in listed.Connections.Where(static entry => entry.DisplayName == ConnectionName))
        {
            _ = await Controller()
                .DeleteAsync(entry.ConnectionId, entry.Version, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>The prompts and confirmations, answered by the test instead of by a person.</summary>
    private sealed class ScriptedDialogs : IDialogService
    {
        internal string? PromptAnswer { get; set; }

        internal DialogPromptRequest? LastPrompt { get; private set; }

        public Task ShowAsync(DialogRequest request, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<DialogChoice> ConfirmAsync(DialogRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(DialogChoice.Yes);

        public Task<string?> PromptAsync(DialogPromptRequest request, CancellationToken cancellationToken = default)
        {
            LastPrompt = request;
            return Task.FromResult(PromptAnswer);
        }
    }
}
