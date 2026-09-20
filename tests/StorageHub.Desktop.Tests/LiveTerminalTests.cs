using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// A shell opened in a pane against the lab's SSH server, with output read back through the painter.
/// </summary>
/// <remarks>
/// <para>
/// Skipped unless STORAGEHUB_LIVE_AGENT is set and eng/testlab is running. The emulator and the
/// painter are tested without a server elsewhere; what is unproven without this is the chain: a
/// client profile whose key comes from the key store, a session the agent opens on it, a real
/// shell's output arriving in the document, and the view keeping its place in that history while
/// more arrives.
/// </para>
/// <para>
/// The lab's key-only server runs a normal sshd with a shell for exactly this, rather than the
/// chrooted internal-sftp the ready-made images offer.
/// </para>
/// </remarks>
public class LiveTerminalTests
{
    private const string ConnectionName = "storagehub-live-terminal-ssh";
    private const string KeyName = "storagehub-live-terminal-key";
    private const string Marker = "storagehub-terminal-marker";

    private static bool Enabled =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STORAGEHUB_LIVE_AGENT"));

    private static bool LabConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STORAGEHUB_SYNCLAB_SFTP_PORT"));

    [AvaloniaFact]
    public async Task AShellsOutputReachesThePainterAndTheViewKeepsItsPlace()
    {
        Assert.SkipUnless(Enabled, "Set STORAGEHUB_LIVE_AGENT to run against a live agent.");
        Assert.SkipUnless(LabConfigured, "Start eng/testlab and dot-source its env.ps1.");
        var token = TestContext.Current.CancellationToken;

        try
        {
            await CleanUpAsync(token);

            // 1. A client profile, with its key borrowed from the key store.
            var imported = await new KeyStoreController(KeyStore, Vault)
                .ImportAsync(LiveKeyStoreTests.Draft(KeyName), token);
            Assert.True(imported.Changed, $"The key could not be imported: {imported.ErrorMessage}");

            var editor = new ConnectionEditorModel(
                Controller,
                keyStore: KeyStore,
                pickKey: entries => Task.FromResult(entries.FirstOrDefault(entry => entry.DisplayName == KeyName)));
            editor.Provider = ConnectionProviderCatalog.Get(StorageProviderKind.Ssh);
            editor.Field("profileName")!.Value = ConnectionName;
            editor.Field("host")!.Value = "127.0.0.1";
            editor.Field("port")!.Value = Lab("STORAGEHUB_SYNCLAB_SFTP_PORT");
            editor.Field("username")!.Value = Lab("STORAGEHUB_SYNCLAB_SFTP_USERNAME");
            editor.Field("authenticationMode")!.Value = "Private key reference";
            editor.Field("hostKeyFingerprint")!.Value = Lab("STORAGEHUB_SYNCLAB_SFTP_HOST_SHA256");
            await editor.ChooseFromKeyStoreAsync(editor.Field("privateKeyReference")!, token);

            await editor.SaveAsync(token);
            Assert.False(editor.IsNew, $"The agent did not store the connection: {editor.Status}");
            var connectionId = editor.Current!.ConnectionId;
            await TrustAsync(connectionId, Lab("STORAGEHUB_SYNCLAB_SFTP_HOST_SHA256"), token);

            // 2. A pane opens it, which for a client profile is a shell rather than a listing.
            await using var pane = new BrowserPaneModel(
                Storage(), terminals: static () => new NamedPipeSshTerminalAgentClient());
            await pane.LoadConnectionsAsync(token);
            await pane.OpenConnectionAsync(connectionId, token);

            Assert.True(pane.IsTerminal, "The pane did not become a terminal.");
            Assert.True(pane.HasTerminal, $"The pane has no session: {pane.Status}");
            var session = pane.Terminal!;
            var document = session.Document;

            var view = new TerminalView { Session = session };
            var window = new Window { Content = view, Width = 720, Height = 400 };
            window.Show();
            window.Measure(new Size(720, 400));
            window.Arrange(new Rect(0, 0, 720, 400));
            window.UpdateLayout();

            // 3. The shell's output lands in the document, and more than a screen of it is history.
            await WaitForAsync(
                () => Text(document).Contains('$', StringComparison.Ordinal),
                () => $"pane: {pane.Status}; text: '{Text(document)}'", token);
            session.SendText($"printf 'line %s\\n' $(seq 1 100); echo {Marker}\n");
            await WaitForAsync(
                () => Text(document).Contains(Marker, StringComparison.Ordinal),
                () => $"pane: {pane.Status}; text: '{Text(document)}'", token);

            Assert.True(document.ScrollbackCount > 0, "A hundred lines left no history.");
            Assert.True(view.FollowsTail);

            // 4. Reading back holds its place while the shell keeps talking.
            view.ScrollByLines(-30);
            var held = view.ViewportTopLine;
            session.SendText($"echo {Marker}-again\n");
            await WaitForAsync(
                () => Text(document).Contains(Marker + "-again", StringComparison.Ordinal),
                () => $"pane: {pane.Status}", token);
            window.UpdateLayout();

            Assert.Equal(held, view.ViewportTopLine);
            Assert.False(view.FollowsTail);

            // 5. And what is selected is what the shell wrote, from history.
            var line = LineNumberOf(document, "line 50");
            Assert.True(line >= 0, "line 50 is not in the document.");
            view.Select(line, 0, line, 7);
            Assert.Equal("line 50", view.SelectedText);

            window.Close();
        }
        finally
        {
            await CleanUpAsync(CancellationToken.None);
        }
    }

    private static string Text(VtTerminalDocument document) =>
        document.GetText(document.FirstLineNumber, document.TotalLineCount, joinWrapped: false);

    private static long LineNumberOf(VtTerminalDocument document, string prefix)
    {
        for (var number = document.FirstLineNumber; number <= document.LastLineNumber; number++)
        {
            if (document.FindLine(number) is { } line)
            {
                var builder = new System.Text.StringBuilder();
                line.AppendTextTo(builder, 0, line.TrimmedLength());
                if (builder.ToString().Trim() == prefix) return number;
            }
        }

        return -1;
    }

    private static async Task WaitForAsync(
        Func<bool> condition, Func<string> describe, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline && !condition())
        {
            await Task.Delay(200, cancellationToken).ConfigureAwait(true);
        }

        Assert.True(condition(), "The shell did not answer in time. " + describe());
    }

    private static async Task TrustAsync(Guid connectionId, string fingerprint, CancellationToken cancellationToken)
    {
        var controller = Controller();
        var profile = (await controller.GetAsync(connectionId, cancellationToken)).Profile;
        Assert.True(profile is not null, "The saved SSH connection could not be read back.");

        var trusted = await controller.TrustOrRolloverAsync(profile!, fingerprint, cancellationToken);
        Assert.True(
            trusted.Status == ConnectionTrustMutationStatus.Succeeded,
            $"The host key could not be pinned: {trusted.Failure?.Message}");
    }

    private static string Lab(string name) =>
        Environment.GetEnvironmentVariable(name) ??
        throw new InvalidOperationException($"{name} is not set; dot-source the lab's env.ps1.");

    private static NamedPipeRemoteStorageAgentClient Storage() => new();

    private static NamedPipeKeyStoreAgentClient KeyStore() => new();

    private static NamedPipeRemoteSecretVaultClient Vault() => new();

    private static ConnectionManagerController Controller() => new(
        new NamedPipeRemoteConnectionProfileClient(),
        new NamedPipeRemoteSecretVaultClient());

    /// <summary>Removes the connection, then the key, since a key in use cannot go.</summary>
    private static async Task CleanUpAsync(CancellationToken cancellationToken)
    {
        await using (var storage = Storage())
        {
            var listed = await storage
                .ListConnectionsAsync(new ConnectionListRequest(IncludeDisabled: true), cancellationToken)
                .ConfigureAwait(true);
            foreach (var entry in listed.Connections.Where(static entry => entry.DisplayName == ConnectionName))
            {
                _ = await Controller().DeleteAsync(entry.ConnectionId, entry.Version, cancellationToken);
            }
        }

        var store = new KeyStoreController(KeyStore, Vault);
        var keys = await store.ListAsync(cancellationToken: cancellationToken);
        foreach (var key in keys.Entries.Where(static key => key.DisplayName == KeyName))
        {
            _ = await store.DeleteAsync(key, cancellationToken);
        }
    }
}
