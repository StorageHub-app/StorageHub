using Avalonia;
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

namespace StorageHub.Desktop.Avalonia.Tests;

/// <summary>
/// The Connection Manager: every saved connection, and the editor for whichever is chosen.
/// </summary>
/// <remarks>
/// <para>
/// The WinForms version is 1,784 lines, most of them laying out fields by hand for six providers.
/// Here the fields come from <see cref="ConnectionProviderCatalog"/> and the draft from
/// <see cref="ConnectionEditorDraftFactory"/> -- both of which that shell already had and neither
/// of which it used for its layout. So what is worth testing is not the fields but the seam: that
/// the editor asks the catalog, that a save goes through the factory, and that the version a row
/// was listed at is what a delete is checked against.
/// </para>
/// </remarks>
public class ConnectionManagerTests
{
    [AvaloniaFact]
    public void TheEditorAsksTheCatalogWhichFieldsAProviderHas()
    {
        var editor = new ConnectionEditorModel(() => Controller(new FakeProfiles()));

        var descriptor = ConnectionProviderCatalog.All[0];
        var expected = 1 +
            (descriptor.GeneralFields.Count > 0 ? 1 : 0) +
            (descriptor.AuthenticationFields.Count > 0 ? 1 : 0) +
            (descriptor.SecurityFields.Count > 0 ? 1 : 0);

        // One more than the provider has: a name and a folder belong to the profile rather than to
        // the provider, so the editor supplies them and every provider gets them.
        Assert.Equal(expected, editor.Sections.Count);
        Assert.Equal(Ui.Connections.SectionIdentity, editor.Sections[0].Title);
        Assert.Equal(
            descriptor.GeneralFields.Select(static f => f.Key),
            editor.Sections[1].Fields.Select(static f => f.Key));
    }

    /// <summary>Every provider lays out without a field the editor cannot draw.</summary>
    [AvaloniaFact]
    public void EveryProviderCanBeEdited()
    {
        var editor = new ConnectionEditorModel(() => Controller(new FakeProfiles()));

        foreach (var provider in ConnectionProviderCatalog.All)
        {
            editor.Provider = provider;

            Assert.NotEmpty(editor.Sections);
            Assert.All(editor.Sections.SelectMany(static s => s.Fields), field =>
            {
                // Exactly one of the three editors applies to each field, whatever its kind.
                var drawn = (field.IsText ? 1 : 0) + (field.IsChoice ? 1 : 0) + (field.IsToggle ? 1 : 0);
                Assert.Equal(1, drawn);
                Assert.False(string.IsNullOrWhiteSpace(field.Label), $"{provider.Kind}/{field.Key}");
            });
        }
    }

    /// <summary>
    /// Changing the provider keeps what the two have in common.
    /// </summary>
    /// <remarks>
    /// Trying S3 and then SFTP should not mean typing the name and folder again. The values are
    /// held by key, so a field that exists in both keeps what was typed and one that does not is
    /// simply not shown.
    /// </remarks>
    [AvaloniaFact]
    public void ChangingProviderKeepsWhatTheyShare()
    {
        var editor = new ConnectionEditorModel(() => Controller(new FakeProfiles()));
        Name(editor, "Studio Assets");

        editor.Provider = ConnectionProviderCatalog.Get(StorageProviderKind.Sftp);

        Assert.Equal("Studio Assets", Field(editor, "profileName").Value);
    }

    /// <summary>Nothing typed is nothing to save, and a required field left empty is not enough.</summary>
    [AvaloniaFact]
    public void SavingNeedsAChangeAndEveryRequiredField()
    {
        var editor = new ConnectionEditorModel(() => Controller(new FakeProfiles()));

        Assert.False(editor.SaveCommand.CanExecute(null));

        foreach (var field in editor.Sections.SelectMany(static s => s.Fields).Where(static f => f.Required))
        {
            field.Value = "x";
        }

        Assert.True(editor.SaveCommand.CanExecute(null));

        Field(editor, "profileName").Value = string.Empty;
        Assert.False(editor.SaveCommand.CanExecute(null));
    }

    /// <summary>A new connection is created; a loaded one is updated at the version it came back at.</summary>
    [AvaloniaFact]
    public async Task SavingANewConnectionCreatesIt()
    {
        var profiles = new FakeProfiles();
        var editor = new ConnectionEditorModel(() => Controller(profiles));
        Fill(editor);

        await editor.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, profiles.Creates);
        Assert.Equal(0, profiles.Updates);
        Assert.Equal(Ui.Connections.ConnectionSaved, editor.Status);

        // Held at what was written, so a second save updates rather than creating a duplicate.
        Assert.False(editor.IsNew);

        await editor.SaveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, profiles.Creates);
    }

    /// <summary>An agent's refusal is shown rather than thrown.</summary>
    [AvaloniaFact]
    public async Task ARefusedSaveIsReported()
    {
        var profiles = new FakeProfiles
        {
            Failure = new StorageIpcFailure(
                "provider.exists", StorageIpcFailureCategory.Conflict, "That name is taken.", false)
        };
        var editor = new ConnectionEditorModel(() => Controller(profiles));
        Fill(editor);

        await editor.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Equal("That name is taken.", editor.Status);
        Assert.True(editor.IsNew);
    }

    /// <summary>
    /// A delete is checked against the version the row was listed at.
    /// </summary>
    /// <remarks>
    /// That is what turns "somebody else edited this while the manager was open" into a refusal
    /// rather than a deletion at a revision nobody reviewed.
    /// </remarks>
    [AvaloniaFact]
    public async Task DeletingPinsTheVersionThatWasListed()
    {
        var profiles = new FakeProfiles();
        var dialogs = new YesDialogs();
        var manager = new ConnectionManagerModel(
            () => new ListingAgent([Summary("Studio Assets", version: 7)]),
            () => Controller(profiles),
            dialogs);
        await manager.RefreshAsync(TestContext.Current.CancellationToken);

        manager.Selected = manager.Connections[0];
        await manager.DeleteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(7, profiles.DeletedVersion);
    }

    /// <summary>Deleting asks first, and a "no" leaves the connection alone.</summary>
    [AvaloniaFact]
    public async Task DecliningTheDeleteKeepsTheConnection()
    {
        var profiles = new FakeProfiles();
        var dialogs = new YesDialogs { Choice = Desktop.Shell.DialogChoice.No };
        var manager = new ConnectionManagerModel(
            () => new ListingAgent([Summary("Studio Assets")]),
            () => Controller(profiles),
            dialogs);
        await manager.RefreshAsync(TestContext.Current.CancellationToken);
        manager.Selected = manager.Connections[0];

        await manager.DeleteAsync(TestContext.Current.CancellationToken);

        Assert.Null(profiles.DeletedVersion);
    }

    /// <summary>The list says what each connection is, the same way the panel does.</summary>
    [AvaloniaFact]
    public async Task TheListBadgesClientsAndStorage()
    {
        var manager = new ConnectionManagerModel(
            () => new ListingAgent(
            [
                Summary("Studio Assets"),
                Summary("build-box", provider: StorageConnectionProvider.Ssh, client: true)
            ]),
            () => Controller(new FakeProfiles()));

        await manager.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Ui.Connections.BadgeStorage, manager.Connections[0].Badge);
        Assert.Equal(Ui.Connections.BadgeClient, manager.Connections[1].Badge);
    }

    /// <summary>
    /// Photographs the manager in both appearances, for a human to look at.
    /// </summary>
    /// <remarks>
    /// Two hundred descriptor-driven rows is exactly the sort of screen that lays out wrong without
    /// failing anything. Set STORAGEHUB_SHOT_DIR to keep the files.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheManagerCanBePhotographed(bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));

        var manager = new ConnectionManagerModel(
            () => new ListingAgent(
            [
                Summary("Studio Assets"),
                Summary("Site Backups"),
                Summary("build-box", provider: StorageConnectionProvider.Ssh, client: true)
            ]),
            () => Controller(new FakeProfiles()));
        await manager.RefreshAsync(TestContext.Current.CancellationToken);

        var window = new ConnectionManagerWindow { DataContext = manager };
        window.Show();
        window.Measure(new Size(920, 620));
        window.Arrange(new Rect(0, 0, 920, 620));
        window.UpdateLayout();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;

        Directory.CreateDirectory(directory);
        using var stream = File.Create(
            Path.Combine(directory, $"connection-manager-{(dark ? "dark" : "light")}.png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }

    private static ConnectionManagerController Controller(FakeProfiles profiles) =>
        new(profiles, new FakeVault());

    private static ConnectionFieldModel Field(ConnectionEditorModel editor, string key) =>
        editor.Sections.SelectMany(static s => s.Fields).First(f => f.Key == key);

    private static void Name(ConnectionEditorModel editor, string name) =>
        Field(editor, "profileName").Value = name;

    /// <summary>Every required field filled with something the factory will accept.</summary>
    private static void Fill(ConnectionEditorModel editor)
    {
        foreach (var field in editor.Sections.SelectMany(static s => s.Fields))
        {
            if (field.Required && field.Value.Trim().Length == 0) field.Value = "sample";
        }

        Name(editor, "Studio Assets");
    }

    private static ConnectionSummary Summary(
        string name,
        StorageConnectionProvider provider = StorageConnectionProvider.S3,
        bool client = false,
        long version = 1) =>
        new(
            Guid.NewGuid(),
            name,
            provider,
            FolderPath: null,
            Tags: [],
            IsFavorite: false,
            IsEnabled: true,
            IconKey: null,
            AccentColor: null,
            version,
            client ? ConnectionProfileType.Client : ConnectionProfileType.Storage);

    /// <summary>
    /// The profile store, written down instead of written to.
    /// </summary>
    /// <remarks>
    /// A write comes back as the document it produced, because that is what the editor holds
    /// afterwards: the id it was written under and the version a second save will be checked
    /// against. Returning a bare acknowledgement would let the editor create a duplicate on every
    /// save, which is exactly what one of these tests is for.
    /// </remarks>
    private sealed class FakeProfiles : IRemoteConnectionProfileClient
    {
        private readonly Dictionary<Guid, ConnectionProfileDocument> _stored = [];

        internal int Creates { get; private set; }

        internal int Updates { get; private set; }

        internal long? DeletedVersion { get; private set; }

        internal StorageIpcFailure? Failure { get; init; }

        public Task<ConnectionProfileGetResponse> GetAsync(
            ConnectionProfileGetRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionProfileGetResponse(
                ConnectionProfileIpcContract.CurrentVersion,
                _stored.GetValueOrDefault(request.ConnectionId)));

        public Task<ConnectionProfileWriteResponse> CreateAsync(
            ConnectionProfileCreateRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Failure is not null) return Refused();
            Creates++;
            return Written(Guid.NewGuid(), 1, request.Draft);
        }

        public Task<ConnectionProfileWriteResponse> UpdateAsync(
            ConnectionProfileUpdateRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Failure is not null) return Refused();
            Updates++;
            return Written(request.ConnectionId, request.ExpectedVersion + 1, request.Draft);
        }

        public Task<ConnectionProfileWriteResponse> DeleteAsync(
            ConnectionProfileDeleteRequest request,
            CancellationToken cancellationToken = default)
        {
            DeletedVersion = request.ExpectedVersion;
            _ = _stored.Remove(request.ConnectionId);
            return Task.FromResult(new ConnectionProfileWriteResponse(
                ConnectionProfileIpcContract.CurrentVersion, ConnectionProfileWriteStatus.Deleted));
        }

        public Task<ConnectionTrustGetResponse> GetTrustAsync(
            ConnectionTrustGetRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ConnectionTrustMutationResponse> DecideTrustAsync(
            ConnectionTrustDecisionRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ConnectionTrustMutationResponse> RolloverTrustAsync(
            ConnectionTrustRolloverRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private Task<ConnectionProfileWriteResponse> Written(
            Guid id,
            long version,
            ConnectionProfileDraft draft)
        {
            var now = DateTimeOffset.UtcNow;
            var document = new ConnectionProfileDocument(id, version, draft, now, now);
            _stored[id] = document;
            return Task.FromResult(new ConnectionProfileWriteResponse(
                ConnectionProfileIpcContract.CurrentVersion,
                ConnectionProfileWriteStatus.Succeeded,
                document));
        }

        private Task<ConnectionProfileWriteResponse> Refused() =>
            Task.FromResult(new ConnectionProfileWriteResponse(
                ConnectionProfileIpcContract.CurrentVersion,
                ConnectionProfileWriteStatus.NameConflict,
                Failure: Failure));
    }

    /// <summary>The vault, which this screen does not use yet.</summary>
    /// <remarks>
    /// A secret reference field names something the vault holds rather than carrying it, so the
    /// editor's job is the name. Enrolling one belongs with the key store screen, which is not
    /// ported -- so every call here throws rather than quietly answering.
    /// </remarks>
    private sealed class FakeVault : IRemoteSecretVaultClient
    {
        public Task<SecretVaultResponse> EnrollAsync(
            SecretMaterialPurpose purpose,
            ReadOnlyMemory<byte> secret,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SecretVaultResponse> UpdateAsync(
            string reference,
            SecretMaterialPurpose purpose,
            ReadOnlyMemory<byte> secret,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SecretVaultResponse> DeleteAsync(
            string reference,
            SecretMaterialPurpose purpose,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class YesDialogs : Desktop.Shell.IDialogService
    {
        internal Desktop.Shell.DialogChoice Choice { get; init; } = Desktop.Shell.DialogChoice.Yes;

        public Task ShowAsync(
            Desktop.Shell.DialogRequest request,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<Desktop.Shell.DialogChoice> ConfirmAsync(
            Desktop.Shell.DialogRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(Choice);

        public Task<string?> PromptAsync(
            Desktop.Shell.DialogPromptRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class ListingAgent(ConnectionSummary[] connections) : IRemoteStorageAgentClient
    {
        public Task<ConnectionListResponse> ListConnectionsAsync(
            ConnectionListRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionListResponse(StorageIpcContract.CurrentVersion, connections));

        public Task<ConnectionTestResponse> TestConnectionAsync(
            ConnectionTestRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionTestResponse(
                StorageIpcContract.CurrentVersion, request.ConnectionId, Succeeded: true, 1));

        public Task<StorageListPageResponse> ListStorageAsync(
            StorageListPageRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
