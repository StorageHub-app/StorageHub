using StorageHub.Contracts.Ipc;
using StorageHub.Desktop;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// A key imported through the store, picked into a connection, and used to reach a real server.
/// </summary>
/// <remarks>
/// <para>
/// Skipped unless STORAGEHUB_LIVE_AGENT is set and eng/testlab is running. The store's rules are
/// pinned against fakes in KeyStoreControllerTests; what is unproven without this is the other
/// side of every call: that the agent derives a fingerprint from what the vault was given, that
/// the reference a connection borrows from the store is one the SFTP connector can open, that the
/// agent reports which connection uses an entry, and that it refuses to delete one in use.
/// </para>
/// <para>
/// Everything it makes is named so a cancelled run leaves something recognisable, and it removes
/// its connection and its entry on the way out -- in that order, because the entry cannot go
/// while the connection names it.
/// </para>
/// </remarks>
public class LiveKeyStoreTests
{
    private const string EntryName = "storagehub-live-key-store";
    private const string ConnectionName = "storagehub-live-key-store-sftp";

    private static bool Enabled =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STORAGEHUB_LIVE_AGENT"));

    private static bool LabConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STORAGEHUB_SYNCLAB_SFTP_PORT"));

    [Fact]
    public async Task AKeyImportedInTheStoreOpensAnSftpConnection()
    {
        Assert.SkipUnless(Enabled, "Set STORAGEHUB_LIVE_AGENT to run against a live agent.");
        Assert.SkipUnless(LabConfigured, "Start eng/testlab and dot-source its env.ps1.");
        var token = TestContext.Current.CancellationToken;

        try
        {
            await CleanUpAsync(token);

            // 1. The store, exactly as the window drives it: the import dialog answered by the test.
            var dialogs = new ScriptedDialogs();
            var store = KeyStoreModel.Create(Clients, Vaults, dialogs, _ => Task.FromResult<KeyStoreImportDraft?>(Draft()));
            await store.LoadAsync(token);
            Assert.False(store.Status.IsDanger, $"The store could not load: {store.Status.Text}");

            await store.ImportAsync(KeyStoreMaterialKind.SshPrivateKey, token);
            Assert.True(store.Status.IsSuccess, $"The import was refused: {store.Status.Text}");

            var row = Assert.Single(store.Entries, entry => entry.Name == EntryName);
            Assert.Equal(Ui.KeyStore.SSHKey, row.Kind);

            // The agent derived this from the material the vault was handed, which is the only
            // proof the two pipes agreed about which bytes were enrolled.
            Assert.NotEqual(Ui.KeyStore.UnknownFingerprint, row.Identity);
            Assert.Equal(Ui.KeyStore.NoExpiry, row.Expires);
            Assert.Equal("0", row.UsedBy);

            // 2. Renaming, at the version the row was read at.
            store.SelectedRow = row;
            dialogs.PromptAnswer = EntryName + " renamed";
            await store.RenameAsync(token);
            Assert.Equal(Ui.KeyStore.Renamed, store.Status.Text);
            Assert.Single(store.Entries, entry => entry.Name == EntryName + " renamed");

            // 3. The connection editor borrows it, and the connector opens the server with it.
            var editor = new ConnectionEditorModel(
                Controller,
                Storage,
                keyStore: Clients,
                pickKey: entries => Task.FromResult(
                    entries.FirstOrDefault(entry => entry.DisplayName == EntryName + " renamed")));
            editor.Provider = ConnectionProviderCatalog.Get(StorageProviderKind.Sftp);
            editor.Field("profileName")!.Value = ConnectionName;
            editor.Field("host")!.Value = "127.0.0.1";
            editor.Field("port")!.Value = Lab("STORAGEHUB_SYNCLAB_SFTP_PORT");
            editor.Field("initialPath")!.Value = "/" + Lab("STORAGEHUB_SYNCLAB_SFTP_ROOT");
            editor.Field("username")!.Value = Lab("STORAGEHUB_SYNCLAB_SFTP_USERNAME");
            editor.Field("authenticationMode")!.Value = "Private key reference";
            editor.Field("hostKeyFingerprint")!.Value = Lab("STORAGEHUB_SYNCLAB_SFTP_HOST_SHA256");

            await editor.ChooseFromKeyStoreAsync(editor.Field("privateKeyReference")!, token);
            Assert.Equal(
                Ui.Format(Ui.ConnectionEditor.UsingStoredKeyFormat, EntryName + " renamed"), editor.Status);
            Assert.False(string.IsNullOrEmpty(editor.Field("privateKeyPassphraseReference")!.Value));

            await editor.SaveAsync(token);
            Assert.False(editor.IsNew, $"The agent did not store the connection: {editor.Status}");
            await TrustAsync(editor.Current!.ConnectionId, Lab("STORAGEHUB_SYNCLAB_SFTP_HOST_SHA256"), token);

            await editor.TestAsync(token);
            Assert.Equal(Ui.Connections.ConnectionReachable, editor.Status);

            // 4. The store now names the connection, and refuses to delete what it uses.
            await store.LoadAsync(token);
            row = Assert.Single(store.Entries, entry => entry.Name == EntryName + " renamed");
            Assert.Equal("1", row.UsedBy);

            store.SelectedRow = row;
            dialogs.Choice = DialogChoice.Yes;
            await store.DeleteAsync(token);
            Assert.True(store.Status.IsDanger);
            Assert.Contains(ConnectionName, store.Status.Text, StringComparison.Ordinal);
            Assert.Single(store.Entries, entry => entry.Name == EntryName + " renamed");

            // 5. Once the connection is gone, the entry can go.
            _ = await Controller().DeleteAsync(
                editor.Current.ConnectionId, editor.Current.Version, token);
            await store.LoadAsync(token);
            store.SelectedRow = Assert.Single(store.Entries, entry => entry.Name == EntryName + " renamed");
            await store.DeleteAsync(token);
            Assert.Equal(Ui.KeyStore.Deleted, store.Status.Text);
            Assert.DoesNotContain(store.Entries, entry => entry.Name.StartsWith(EntryName, StringComparison.Ordinal));
        }
        finally
        {
            await CleanUpAsync(CancellationToken.None);
        }
    }

    /// <summary>The lab's client key, declared under the envelope its first line says it is.</summary>
    internal static KeyStoreImportDraft Draft(string name = EntryName)
    {
        var path = Lab("STORAGEHUB_SYNCLAB_SFTP_KEY_PATH");
        var header = File.ReadLines(path).FirstOrDefault() ?? string.Empty;
        var format = header.Contains("OPENSSH PRIVATE KEY", StringComparison.Ordinal)
            ? KeyStorePrivateKeyFormat.OpenSsh
            : header.Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal)
                ? KeyStorePrivateKeyFormat.Pkcs8
                : KeyStorePrivateKeyFormat.Pem;

        return new KeyStoreImportDraft(
            KeyStoreMaterialKind.SshPrivateKey,
            path,
            name,
            Lab("STORAGEHUB_SYNCLAB_SFTP_KEY_PASSPHRASE"),
            format);
    }

    /// <summary>Pins the server's host key, which a pinned connection cannot open without.</summary>
    private static async Task TrustAsync(Guid connectionId, string fingerprint, CancellationToken cancellationToken)
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

    /// <summary>Removes the connection, then the entries, since an entry in use cannot go.</summary>
    internal static async Task CleanUpAsync(CancellationToken cancellationToken, string entryPrefix = EntryName)
    {
        await using (var storage = Storage())
        {
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

        var store = new KeyStoreController(Clients, Vaults);
        var entries = await store.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        foreach (var entry in entries.Entries.Where(
            entry => entry.DisplayName.StartsWith(entryPrefix, StringComparison.Ordinal)))
        {
            _ = await store.DeleteAsync(entry, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string Lab(string name) =>
        Environment.GetEnvironmentVariable(name) ??
        throw new InvalidOperationException($"{name} is not set; dot-source the lab's env.ps1.");

    private static NamedPipeKeyStoreAgentClient Clients() => new();

    private static NamedPipeRemoteSecretVaultClient Vaults() => new();

    private static NamedPipeRemoteStorageAgentClient Storage() => new();

    private static ConnectionManagerController Controller() => new(
        new NamedPipeRemoteConnectionProfileClient(),
        new NamedPipeRemoteSecretVaultClient());

    /// <summary>The dialogs, answered by the test instead of by a person.</summary>
    private sealed class ScriptedDialogs : IDialogService
    {
        internal DialogChoice Choice { get; set; } = DialogChoice.No;

        internal string? PromptAnswer { get; set; }

        public Task ShowAsync(DialogRequest request, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<DialogChoice> ConfirmAsync(DialogRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Choice);

        public Task<string?> PromptAsync(DialogPromptRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(PromptAnswer);
    }
}
