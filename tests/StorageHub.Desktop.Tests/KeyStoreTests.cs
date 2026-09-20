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
/// The key store: what is imported, and importing, renaming and deleting it.
/// </summary>
/// <remarks>
/// The controller's rules are pinned in KeyStoreControllerTests. What is checked here is the seam
/// between the screen and them: that the table shows what the agent holds in words, that importing
/// goes through the one dialog and lands the new row selected, that renaming asks for a name and
/// deleting asks for consent, and that a search reaches the agent rather than filtering a copy.
/// </remarks>
public class KeyStoreTests
{
    [AvaloniaFact]
    public async Task StoredEntriesReachTheTableInWords()
    {
        var agent = new StubKeyStoreAgent
        {
            Entries =
            {
                Entry("build box", KeyStoreMaterialKind.SshPrivateKey, fingerprint: "SHA256:abc"),
                Entry("Client cert", KeyStoreMaterialKind.Pkcs12Certificate, subject: "CN=client",
                    notAfter: DateTimeOffset.UtcNow.AddDays(10), usedBy: ["Studio S3"])
            }
        };
        var model = KeyStoreModel.Create(() => agent, () => new StubVault());

        await model.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["build box", "Client cert"], model.Entries.Select(static row => row.Name));
        Assert.Equal(Ui.KeyStore.SSHKey, model.Entries[0].Kind);
        Assert.Equal("SHA256:abc", model.Entries[0].Identity);
        Assert.Equal(Ui.KeyStore.NoExpiry, model.Entries[0].Expires);
        Assert.Equal("CN=client", model.Entries[1].Identity);
        Assert.True(model.Entries[1].IsExpiringSoon);
        Assert.Equal("1", model.Entries[1].UsedBy);
        Assert.Equal(Ui.Format(Ui.KeyStore.StoredItemsFormat, 2), model.Status.Text);
    }

    /// <summary>The search reaches the agent, which is what matches on name and description.</summary>
    [AvaloniaFact]
    public async Task TheSearchIsAskedOfTheAgent()
    {
        var agent = new StubKeyStoreAgent { Entries = { Entry("build box", KeyStoreMaterialKind.SshPrivateKey) } };
        var model = KeyStoreModel.Create(() => agent, () => new StubVault());
        await model.LoadAsync(TestContext.Current.CancellationToken);

        model.SearchText = "photo";
        await model.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("photo", agent.Listed!.Text);
        Assert.Empty(model.Entries);
        Assert.Equal(Ui.KeyStore.NothingMatches, model.Status.Text);
    }

    /// <summary>Importing asks once, imports, and lands with the new row in hand.</summary>
    [AvaloniaFact]
    public async Task ImportingLandsWithTheNewRowSelected()
    {
        var agent = new StubKeyStoreAgent();
        var vault = new StubVault();
        var file = Path.Combine(Path.GetTempPath(), "storagehub-import-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(file, "key", TestContext.Current.CancellationToken);
        try
        {
            var asked = new List<KeyStoreMaterialKind>();
            var model = KeyStoreModel.Create(() => agent, () => vault, new RecordingDialogs(), kind =>
            {
                asked.Add(kind);
                return Task.FromResult<KeyStoreImportDraft?>(new KeyStoreImportDraft(
                    kind, file, "build box", "open sesame", KeyStorePrivateKeyFormat.OpenSsh));
            });
            await model.LoadAsync(TestContext.Current.CancellationToken);

            await model.ImportAsync(KeyStoreMaterialKind.SshPrivateKey, TestContext.Current.CancellationToken);

            Assert.Equal([KeyStoreMaterialKind.SshPrivateKey], asked);
            Assert.Equal(2, vault.Enrolments);
            var row = Assert.Single(model.Entries);
            Assert.Equal("build box", row.Name);
            Assert.Same(row, model.SelectedRow);
            Assert.Equal(Ui.Format(Ui.KeyStore.ImportedFormat, "build box"), model.Status.Text);
            Assert.True(model.Status.IsSuccess);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>A dismissed import dialog imports nothing and says nothing.</summary>
    [AvaloniaFact]
    public async Task ADismissedImportImportsNothing()
    {
        var agent = new StubKeyStoreAgent();
        var vault = new StubVault();
        var model = KeyStoreModel.Create(
            () => agent, () => vault, new RecordingDialogs(),
            _ => Task.FromResult<KeyStoreImportDraft?>(null));
        await model.LoadAsync(TestContext.Current.CancellationToken);

        await model.ImportAsync(KeyStoreMaterialKind.Pkcs12Certificate, TestContext.Current.CancellationToken);

        Assert.Equal(0, vault.Enrolments);
        Assert.Null(agent.Created);
        Assert.Equal(Ui.KeyStore.NoKeysOrCertificatesAreStoredYet, model.Status.Text);
    }

    /// <summary>Renaming asks for a name and sends it at the version the row was read at.</summary>
    [AvaloniaFact]
    public async Task RenamingAsksForANameAndSendsIt()
    {
        var agent = new StubKeyStoreAgent { Entries = { Entry("build box", KeyStoreMaterialKind.SshPrivateKey) with { Version = 3 } } };
        var dialogs = new RecordingDialogs { PromptAnswer = "build box 2" };
        var model = KeyStoreModel.Create(() => agent, () => new StubVault(), dialogs);
        await model.LoadAsync(TestContext.Current.CancellationToken);
        model.SelectedRow = model.Entries[0];

        await model.RenameAsync(TestContext.Current.CancellationToken);

        Assert.Equal("build box", dialogs.LastPrompt!.Value);
        Assert.Equal("build box 2", agent.Updated!.DisplayName);
        Assert.Equal(3, agent.Updated.ExpectedVersion);
        Assert.Equal("build box 2", Assert.Single(model.Entries).Name);
        Assert.Equal(Ui.KeyStore.Renamed, model.Status.Text);
    }

    /// <summary>
    /// Deleting asks first, and an entry a connection uses is refused before it asks.
    /// </summary>
    /// <remarks>
    /// The agent would refuse too, but a confirmation for something that is then refused anyway
    /// teaches people to click through the one that matters. The listing knows which connection
    /// and says so instead.
    /// </remarks>
    [AvaloniaFact]
    public async Task DeletingAsksUnlessAConnectionStillUsesTheEntry()
    {
        var agent = new StubKeyStoreAgent
        {
            Entries =
            {
                Entry("build box", KeyStoreMaterialKind.SshPrivateKey),
                Entry("in use", KeyStoreMaterialKind.SshPrivateKey, usedBy: ["Studio SFTP"])
            }
        };
        var dialogs = new RecordingDialogs { Choice = DialogChoice.No };
        var model = KeyStoreModel.Create(() => agent, () => new StubVault(), dialogs);
        await model.LoadAsync(TestContext.Current.CancellationToken);

        model.SelectedRow = model.Entries.Single(row => row.Name == "in use");
        await model.DeleteAsync(TestContext.Current.CancellationToken);
        Assert.Null(dialogs.LastRequest);
        Assert.Contains("Studio SFTP", model.Status.Text, StringComparison.Ordinal);
        Assert.True(model.Status.IsDanger);

        model.SelectedRow = model.Entries.Single(row => row.Name == "build box");
        await model.DeleteAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(dialogs.LastRequest);
        Assert.Equal(DialogChoice.No, dialogs.LastRequest!.Default);
        Assert.Null(agent.Deleted);

        dialogs.Choice = DialogChoice.Yes;
        await model.DeleteAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(agent.Deleted);
        Assert.DoesNotContain(model.Entries, row => row.Name == "build box");
        Assert.Equal(Ui.KeyStore.Deleted, model.Status.Text);
    }

    /// <summary>Without a selection there is nothing to rename or delete, and the buttons say so.</summary>
    [AvaloniaFact]
    public async Task RenameAndDeleteNeedASelection()
    {
        var agent = new StubKeyStoreAgent { Entries = { Entry("build box", KeyStoreMaterialKind.SshPrivateKey) } };
        var model = KeyStoreModel.Create(() => agent, () => new StubVault(), new RecordingDialogs());
        await model.LoadAsync(TestContext.Current.CancellationToken);

        Assert.False(model.RenameCommand.CanExecute(null));
        Assert.False(model.DeleteCommand.CanExecute(null));

        model.SelectedRow = model.Entries[0];

        Assert.True(model.RenameCommand.CanExecute(null));
        Assert.True(model.DeleteCommand.CanExecute(null));
    }

    /// <summary>
    /// The import dialog is dim with the reason until the draft is one the agent will take.
    /// </summary>
    /// <remarks>
    /// The same rules the controller applies, so the button enables exactly when an import would
    /// succeed as far as the desktop can tell. The name is suggested from the file, and a name that
    /// was typed over the suggestion survives choosing another file.
    /// </remarks>
    [AvaloniaFact]
    public async Task TheImportDialogDimsUntilTheDraftWillDo()
    {
        var file = Path.Combine(Path.GetTempPath(), "id_ed25519-" + Guid.NewGuid().ToString("N") + ".key");
        await File.WriteAllTextAsync(file, "key", TestContext.Current.CancellationToken);
        try
        {
            var model = new KeyStoreImportModel(KeyStoreMaterialKind.SshPrivateKey);
            Assert.True(model.ShowsFormat);
            Assert.Equal(Ui.KeyStore.ChooseTheFileToImport, model.Problem);
            Assert.False(model.ImportCommand.CanExecute(null));

            model.FilePath = file;
            Assert.Equal(Path.GetFileNameWithoutExtension(file), model.DisplayName);
            Assert.Equal(Ui.KeyStore.StorageHubCannotStoreAnUnprotectedPrivateKey, model.Problem);

            model.Passphrase = "open sesame";
            Assert.False(model.HasProblem);
            Assert.True(model.ImportCommand.CanExecute(null));

            model.DisplayName = "my own name";
            model.FilePath = file + "2";
            Assert.Equal("my own name", model.DisplayName);

            model.FilePath = file;
            var draft = model.Draft();
            Assert.Equal(KeyStorePrivateKeyFormat.OpenSsh, draft.KeyFormat);
            Assert.Equal("my own name", draft.DisplayName);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>A certificate is not asked which envelope it uses, and may have no password.</summary>
    [AvaloniaFact]
    public async Task ACertificateImportNeedsNoFormatAndNoPassword()
    {
        var file = Path.Combine(Path.GetTempPath(), "client-" + Guid.NewGuid().ToString("N") + ".pfx");
        await File.WriteAllTextAsync(file, "pkcs12", TestContext.Current.CancellationToken);
        try
        {
            var model = new KeyStoreImportModel(KeyStoreMaterialKind.Pkcs12Certificate) { FilePath = file };

            Assert.False(model.ShowsFormat);
            Assert.False(model.HasProblem);
            Assert.Null(model.Draft().KeyFormat);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>The picker starts on the first entry and answers with the one chosen.</summary>
    [AvaloniaFact]
    public void ThePickerAnswersWithTheChosenEntry()
    {
        var entries = new[]
        {
            Entry("zeta", KeyStoreMaterialKind.SshPrivateKey),
            Entry("alpha", KeyStoreMaterialKind.SshPrivateKey)
        };
        var model = new KeyStorePickerModel(entries);

        Assert.Equal("alpha", model.SelectedRow!.Name);
        Assert.True(model.UseCommand.CanExecute(null));

        model.SelectedRow = model.Entries[1];
        model.UseCommand.Execute(null);

        Assert.Equal(entries[0].EntryId, model.Chosen!.EntryId);
    }

    /// <summary>
    /// Photographs the store and its two dialogs, for a human to look at.
    /// </summary>
    /// <remarks>
    /// A toolbar of five buttons and a search box is the kind of row that wraps or clips without
    /// failing anything. Set STORAGEHUB_SHOT_DIR to keep the files.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheStoreCanBePhotographed(bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));

        var agent = new StubKeyStoreAgent
        {
            Entries =
            {
                Entry("build box", KeyStoreMaterialKind.SshPrivateKey, fingerprint: "SHA256:k3yF1ngerpr1ntOfTheBu1ldBox", usedBy: ["Studio SFTP"]),
                Entry("Client certificate", KeyStoreMaterialKind.Pkcs12Certificate, subject: "CN=client, O=Studio",
                    notAfter: DateTimeOffset.UtcNow.AddDays(12)),
                Entry("Old certificate", KeyStoreMaterialKind.Pkcs12Certificate, subject: "CN=old, O=Studio",
                    notAfter: DateTimeOffset.UtcNow.AddDays(-3), tags: ["legacy"])
            }
        };
        var model = KeyStoreModel.Create(() => agent, () => new StubVault(), new RecordingDialogs(),
            _ => Task.FromResult<KeyStoreImportDraft?>(null));
        await model.LoadAsync(TestContext.Current.CancellationToken);
        model.SelectedRow = model.Entries[0];

        Photograph(new KeyStoreWindow { DataContext = model }, 980, 560, $"key-store-{Tone(dark)}");
        Photograph(
            new KeyStoreImportWindow { DataContext = new KeyStoreImportModel(KeyStoreMaterialKind.SshPrivateKey) },
            560, 460, $"key-store-import-{Tone(dark)}");
        Photograph(
            new KeyStorePickerWindow { DataContext = new KeyStorePickerModel(agent.Entries) },
            720, 400, $"key-store-picker-{Tone(dark)}");
    }

    private static string Tone(bool dark) => dark ? "dark" : "light";

    private static void Photograph(Window window, double width, double height, string name)
    {
        window.Show();
        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));
        window.UpdateLayout();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;

        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }

    internal static KeyStoreEntryDocument Entry(
        string name,
        KeyStoreMaterialKind kind,
        string? subject = null,
        string? fingerprint = null,
        DateTimeOffset? notAfter = null,
        string[]? usedBy = null,
        string[]? tags = null) => new(
        Guid.NewGuid(),
        kind,
        name,
        null,
        tags ?? [],
        Reference('m'),
        kind is KeyStoreMaterialKind.SshPrivateKey ? Reference('p') : null,
        new KeyStoreSummaryDocument(Subject: subject, NotAfter: notAfter, Sha256Fingerprint: fingerprint),
        Version: 1,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        usedBy ?? []);

    /// <summary>A reference of the shape the contract accepts: "shs_" and 43 safe characters.</summary>
    internal static string Reference(char fill) => "shs_" + new string(fill, 43);

    /// <summary>The store, scripted by the test.</summary>
    internal sealed class StubKeyStoreAgent : IKeyStoreAgentClient
    {
        internal List<KeyStoreEntryDocument> Entries { get; } = [];

        internal KeyStoreListRequest? Listed { get; private set; }

        internal KeyStoreCreateRequest? Created { get; private set; }

        internal KeyStoreUpdateRequest? Updated { get; private set; }

        internal KeyStoreDeleteRequest? Deleted { get; private set; }

        public Task<KeyStoreListResponse> ListAsync(
            KeyStoreListRequest request, CancellationToken cancellationToken = default)
        {
            Listed = request;
            var matching = Entries
                .Where(entry => request.Kind is null || entry.Kind == request.Kind)
                .Where(entry => request.Text is null ||
                    entry.DisplayName.Contains(request.Text, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return Task.FromResult(new KeyStoreListResponse(KeyStoreIpcContract.CurrentVersion, matching));
        }

        public Task<KeyStoreWriteResponse> CreateAsync(
            KeyStoreCreateRequest request, CancellationToken cancellationToken = default)
        {
            Created = request;
            var entry = new KeyStoreEntryDocument(
                Guid.NewGuid(), request.Kind, request.DisplayName, request.Description, request.Tags,
                request.MaterialReference, request.PassphraseReference,
                new KeyStoreSummaryDocument(KeyFormat: request.KeyFormat), 1,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []);
            Entries.Add(entry);
            return Task.FromResult(new KeyStoreWriteResponse(
                KeyStoreIpcContract.CurrentVersion, KeyStoreWriteOutcome.Applied, entry));
        }

        public Task<KeyStoreWriteResponse> UpdateAsync(
            KeyStoreUpdateRequest request, CancellationToken cancellationToken = default)
        {
            Updated = request;
            var index = Entries.FindIndex(entry => entry.EntryId == request.EntryId);
            if (index < 0)
            {
                return Task.FromResult(new KeyStoreWriteResponse(
                    KeyStoreIpcContract.CurrentVersion, KeyStoreWriteOutcome.NotFound));
            }

            Entries[index] = Entries[index] with { DisplayName = request.DisplayName, Version = Entries[index].Version + 1 };
            return Task.FromResult(new KeyStoreWriteResponse(
                KeyStoreIpcContract.CurrentVersion, KeyStoreWriteOutcome.Applied, Entries[index]));
        }

        public Task<KeyStoreWriteResponse> DeleteAsync(
            KeyStoreDeleteRequest request, CancellationToken cancellationToken = default)
        {
            Deleted = request;
            Entries.RemoveAll(entry => entry.EntryId == request.EntryId);
            return Task.FromResult(new KeyStoreWriteResponse(
                KeyStoreIpcContract.CurrentVersion, KeyStoreWriteOutcome.Applied));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>The vault, which answers with a reference and counts what it was given.</summary>
    internal sealed class StubVault : IRemoteSecretVaultClient
    {
        internal int Enrolments { get; private set; }

        public Task<SecretVaultResponse> EnrollAsync(
            SecretMaterialPurpose purpose, ReadOnlyMemory<byte> secret, CancellationToken cancellationToken = default)
        {
            Enrolments++;
            return Task.FromResult(new SecretVaultResponse(
                SecretVaultIpcContract.CurrentVersion, SecretVaultOperation.Enroll, true,
                Reference((char)('a' + Enrolments)), 1));
        }

        public Task<SecretVaultResponse> UpdateAsync(
            string reference, SecretMaterialPurpose purpose, ReadOnlyMemory<byte> secret,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SecretVaultResponse(
                SecretVaultIpcContract.CurrentVersion, SecretVaultOperation.Update, true, reference, 2));

        public Task<SecretVaultResponse> DeleteAsync(
            string reference, SecretMaterialPurpose purpose, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SecretVaultResponse(
                SecretVaultIpcContract.CurrentVersion, SecretVaultOperation.Delete, true, reference));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>The dialogs, answered by the test instead of by a person.</summary>
    internal sealed class RecordingDialogs : IDialogService
    {
        internal DialogChoice Choice { get; set; } = DialogChoice.No;

        internal string? PromptAnswer { get; set; }

        internal DialogRequest? LastRequest { get; private set; }

        internal DialogPromptRequest? LastPrompt { get; private set; }

        public Task ShowAsync(DialogRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.CompletedTask;
        }

        public Task<DialogChoice> ConfirmAsync(DialogRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(Choice);
        }

        public Task<string?> PromptAsync(DialogPromptRequest request, CancellationToken cancellationToken = default)
        {
            LastPrompt = request;
            return Task.FromResult(PromptAnswer);
        }
    }
}
