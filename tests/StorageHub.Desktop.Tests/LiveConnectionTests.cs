using StorageHub.Contracts.Ipc;
using StorageHub.Desktop;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// A connection made, browsed and removed against an agent that is actually running.
/// </summary>
/// <remarks>
/// <para>
/// Skipped unless STORAGEHUB_LIVE_AGENT is set, because it needs one. Everything else in these
/// suites runs against a fake, which proves the shell asks the right questions; this is the only
/// thing that proves the answers come back. It is the whole path in one test: the editor builds a
/// draft, the agent stores it, the listing shows it, a pane browses it, and the manager deletes it.
/// </para>
/// <para>
/// It uses Local/UNC over a temporary directory, so it needs no credentials and no network, and it
/// removes what it made. The profile is named so that one left behind by a cancelled run is
/// recognisable rather than mysterious.
/// </para>
/// </remarks>
public class LiveConnectionTests
{
    private const string ProfileName = "storagehub-live-test";

    private static bool Enabled =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STORAGEHUB_LIVE_AGENT"));

    [Fact]
    public async Task AConnectionCanBeMadeBrowsedAndRemoved()
    {
        Assert.SkipUnless(Enabled, "Set STORAGEHUB_LIVE_AGENT to run against a live agent.");

        var root = Directory.CreateTempSubdirectory("storagehub-live");
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root.FullName, "hello.txt"),
                "from the agent",
                TestContext.Current.CancellationToken);
            Directory.CreateDirectory(Path.Combine(root.FullName, "nested"));

            await RemoveAsync(TestContext.Current.CancellationToken);

            // 1. The editor builds the draft, exactly as the Connection Manager does.
            var editor = new ConnectionEditorModel(Controller);
            editor.Provider = ConnectionProviderCatalog.Get(StorageProviderKind.Local);
            Field(editor, "profileName").Value = ProfileName;
            Field(editor, "rootPath").Value = root.FullName;

            await editor.SaveAsync(TestContext.Current.CancellationToken);

            Assert.False(
                editor.IsNew,
                $"The agent did not store the connection: {editor.Status}");

            // 2. The agent lists it back.
            var listed = await ListAsync(TestContext.Current.CancellationToken);
            var saved = Assert.Single(listed, entry => entry.DisplayName == ProfileName);

            // 3. A pane browses it, which is the part that needs the provider to actually open.
            await using var pane = new BrowserPaneModel(new NamedPipeRemoteStorageAgentClient());
            await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
            await pane.OpenConnectionAsync(saved.ConnectionId, TestContext.Current.CancellationToken);

            Assert.False(pane.HasStatus, $"The pane could not open the connection: {pane.Status}");
            Assert.Contains(pane.Rows, row => row.Name == "hello.txt");
            Assert.Contains(pane.Rows, row => row.Name == "nested" && row.IsContainer);

            // 4. And the manager removes it, at the version it was listed at.
            var removed = await RemoveAsync(TestContext.Current.CancellationToken);
            Assert.True(removed, "The connection was not removed.");
            Assert.DoesNotContain(
                await ListAsync(TestContext.Current.CancellationToken),
                entry => entry.DisplayName == ProfileName);
        }
        finally
        {
            await RemoveAsync(CancellationToken.None);
            try
            {
                root.Delete(recursive: true);
            }
            catch (IOException)
            {
                // A provider may still hold a handle; the temp directory is the platform's problem.
            }
        }
    }

    private static ConnectionFieldModel Field(ConnectionEditorModel editor, string key) =>
        editor.Sections.SelectMany(static section => section.Fields).First(field => field.Key == key);

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

    /// <summary>Removes the test profile if it is there, so a cancelled run does not block the next.</summary>
    private static async Task<bool> RemoveAsync(CancellationToken cancellationToken)
    {
        var existing = (await ListAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(entry => entry.DisplayName == ProfileName);
        if (existing is null) return false;

        var response = await Controller()
            .DeleteAsync(existing.ConnectionId, existing.Version, cancellationToken)
            .ConfigureAwait(false);
        return response.Failure is null;
    }
}
