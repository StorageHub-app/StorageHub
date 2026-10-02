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

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The Edit Connection dialog, the editor in it, and the panel's own delete.
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

        // Laid out on 1.x's three tabs (ui-reference 08). General opens on the endpoint: the
        // profile's own fields -- name, folder, labels, icon -- then the provider's general ones.
        var endpoint = editor.GeneralSections.First();
        Assert.Equal(Ui.ConnectionEditor.TabEndpoint, endpoint.Title);
        Assert.Equal(descriptor.EndpointExample, endpoint.Hint);
        Assert.Equal(
            ["profileName", "folder", "labels", "iconKey", .. descriptor.GeneralFields.Select(static f => f.Key)],
            endpoint.Fields.Select(static f => f.Key));

        Assert.Equal(
            descriptor.AuthenticationFields.Select(static f => f.Key),
            editor.AuthenticationSections.SelectMany(static s => s.Fields).Select(static f => f.Key));
        Assert.Equal(
            descriptor.SecurityFields.Select(static f => f.Key),
            editor.TrustSections.SelectMany(static s => s.Fields).Select(static f => f.Key));
    }

    /// <summary>The Type drop-down narrows the providers to its kind, as 1.x's two drop-downs did.</summary>
    [AvaloniaFact]
    public void ChoosingATypeOffersOnlyThatTypesProviders()
    {
        var editor = new ConnectionEditorModel(() => Controller(new FakeProfiles()));

        editor.Type = ConnectionEditorModel.Types.Single(static t => t.Type == ConnectionProfileType.Client);

        Assert.Equal(ConnectionProfileType.Client, editor.Provider.Type);
        Assert.All(editor.ProvidersForType, static p => Assert.Equal(ConnectionProfileType.Client, p.Type));
    }

    /// <summary>A swatch sets the colour the profile is saved with, and the badge follows it.</summary>
    [AvaloniaFact]
    public void ChoosingASwatchColoursTheBadgeAndNeedsSaving()
    {
        var editor = new ConnectionEditorModel(() => Controller(new FakeProfiles()));
        var swatch = ConnectionEditorModel.AccentChoices[4].Hex;

        editor.ChooseAccentCommand.Execute(swatch);

        Assert.Equal(swatch, editor.AccentColor);
        Assert.Contains(swatch, editor.BadgeText, StringComparison.Ordinal);
        Assert.True(editor.IsDirty);
    }

    /// <summary>A storage connection can be given speed limits; an SSH terminal moves no files, so it cannot.</summary>
    [AvaloniaFact]
    public void OnlyStorageConnectionsHaveSpeedLimits()
    {
        var editor = new ConnectionEditorModel(() => Controller(new FakeProfiles()));

        foreach (var provider in ConnectionProviderCatalog.All)
        {
            editor.Provider = provider;

            var limits = editor.Sections.SingleOrDefault(static s => s.Title == Ui.Connections.SectionSpeedLimits);
            if (provider.Type == ConnectionProfileType.Storage)
            {
                Assert.Equal(
                    [ConnectionEditorDraftFactory.UploadLimitKey, ConnectionEditorDraftFactory.DownloadLimitKey],
                    limits!.Fields.Select(static f => f.Key));
            }
            else
            {
                Assert.Null(limits);
            }
        }
    }

    /// <summary>Every remote storage connection can use a proxy; a local one and a terminal cannot.</summary>
    [AvaloniaFact]
    public void OnlyRemoteStorageConnectionsHaveAProxy()
    {
        var editor = new ConnectionEditorModel(() => Controller(new FakeProfiles()));

        foreach (var provider in ConnectionProviderCatalog.All)
        {
            editor.Provider = provider;

            var proxy = editor.Sections.SingleOrDefault(static s => s.Title == Ui.Connections.SectionProxy);
            if (provider.Type == ConnectionProfileType.Storage && provider.Kind != StorageProviderKind.Local)
            {
                Assert.Equal(
                    [
                        ConnectionEditorDraftFactory.ProxyAddressKey,
                        ConnectionEditorDraftFactory.ProxyUsernameKey,
                        ConnectionEditorDraftFactory.ProxyPasswordKey
                    ],
                    proxy!.Fields.Select(static f => f.Key));
                Assert.True(proxy.Fields[2].IsSecret);
            }
            else
            {
                Assert.Null(proxy);
            }
        }
    }

    /// <summary>FTP and FTPS have an Advanced section, starting at the provider's own timeouts.</summary>
    [AvaloniaFact]
    public void OnlyFtpHasAdvancedSettings()
    {
        var editor = new ConnectionEditorModel(() => Controller(new FakeProfiles()));

        foreach (var provider in ConnectionProviderCatalog.All)
        {
            editor.Provider = provider;

            var advanced = editor.Sections.SingleOrDefault(static s => s.Title == Ui.Connections.SectionAdvanced);
            if (provider.Kind is StorageProviderKind.Ftp or StorageProviderKind.Ftps)
            {
                var defaults = ConnectionDefaultSettings.Get(provider.Kind, stored: null);
                Assert.Equal(
                    defaults.ConnectTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    advanced!.Fields.Single(f => f.Key == ConnectionEditorDraftFactory.ConnectTimeoutKey).Value);
                Assert.Equal("utf-8", advanced.Fields.Single(f => f.Key == ConnectionEditorDraftFactory.EncodingKey).Value);
            }
            else
            {
                Assert.Null(advanced);
            }
        }
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
                // Exactly one of the six editors applies to each field, whatever its kind.
                var drawn = (field.IsText ? 1 : 0) + (field.IsChoice ? 1 : 0) +
                    (field.IsToggle ? 1 : 0) + (field.IsSecret ? 1 : 0) + (field.IsIcon ? 1 : 0) +
                    (field.IsFingerprint ? 1 : 0);
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

    /// <summary>
    /// A new connection starts from each provider's defaults in Settings, as 1.4's editor did.
    /// </summary>
    /// <remarks>
    /// A default belongs to its provider, so moving from FTP to FTPS lets FTP's port go while what
    /// was typed comes along. Once saved, the connection is its own: a path left empty stays empty
    /// rather than being refilled from Settings and changed by the next save. Moving the saved
    /// connection lets its FTPS port go too, so FTP's default applies, as it did in 1.4.
    /// </remarks>
    [AvaloniaFact]
    public async Task ANewConnectionStartsFromTheDefaultsInSettings()
    {
        var profiles = new FakeProfiles();
        var defaults = new Dictionary<string, string>
        {
            [ConnectionDefaultSettings.Key(StorageProviderKind.Ftp, "port")] = "2121",
            [ConnectionDefaultSettings.Key(StorageProviderKind.Ftps, "port")] = "990",
            [ConnectionDefaultSettings.Key(StorageProviderKind.Ftps, "tlsMode")] = "Implicit TLS",
            [ConnectionDefaultSettings.Key(StorageProviderKind.Ftps, "initialPath")] = "/incoming",
            [ConnectionDefaultSettings.Key(StorageProviderKind.Ftps, ConnectionDefaultSettings.ConnectTimeoutKey)] = "45"
        };
        var editor = new ConnectionEditorModel(() => Controller(profiles), connectionDefaults: defaults);

        editor.Provider = ConnectionProviderCatalog.Get(StorageProviderKind.Ftp);
        Assert.Equal("2121", Field(editor, "port").Value);
        Field(editor, "host").Value = "files.example.com";

        editor.Provider = ConnectionProviderCatalog.Get(StorageProviderKind.Ftps);
        Assert.Equal("990", Field(editor, "port").Value);
        Assert.Equal("Implicit TLS", Field(editor, "tlsMode").Value);
        Assert.Equal("/incoming", Field(editor, "initialPath").Value);
        Assert.Equal("45", Field(editor, ConnectionEditorDraftFactory.ConnectTimeoutKey).Value);
        Assert.Equal("files.example.com", Field(editor, "host").Value);

        Field(editor, "initialPath").Value = string.Empty;
        Field(editor, "passwordReference").Value = "shs_" + new string('a', 43);
        Fill(editor);
        await editor.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Ui.Connections.ConnectionSaved, editor.Status);
        var saved = profiles.LastDraft!;
        Assert.Equal(990, saved.Endpoint.Port);
        Assert.Equal(ConnectionFtpsTlsMode.Implicit, saved.Endpoint.FtpsTlsMode);
        Assert.Equal(45, saved.OperationalOptions.ConnectTimeoutSeconds);
        Assert.Null(saved.Endpoint.RootPath);
        Assert.NotEqual("/incoming", Field(editor, "initialPath").Value);

        editor.Provider = ConnectionProviderCatalog.Get(StorageProviderKind.Ftp);
        Assert.Equal("2121", Field(editor, "port").Value);
        Assert.Equal("files.example.com", Field(editor, "host").Value);
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

    /// <summary>The icon chosen in the picker is the one the saved connection carries.</summary>
    [AvaloniaFact]
    public async Task AChosenIconIsSavedWithTheConnection()
    {
        var profiles = new FakeProfiles();
        string? offered = "unasked";
        var editor = new ConnectionEditorModel(
            () => Controller(profiles),
            pickIcon: (current, _) =>
            {
                offered = current;
                return Task.FromResult(new IconChoice(true, "layers"));
            });
        Fill(editor);

        Field(editor, "iconKey").ChooseIconCommand!.Execute(null);
        await editor.SaveAsync(TestContext.Current.CancellationToken);

        // Nothing chosen yet is offered as nothing, so the picker highlights no icon.
        Assert.Null(offered);
        Assert.Equal("layers", profiles.LastDraft?.Metadata.IconKey);
    }

    /// <summary>"Use default" clears the choice; dismissing the picker leaves it as it was.</summary>
    [AvaloniaFact]
    public void TheIconCanBeClearedAndADismissalChangesNothing()
    {
        var answer = IconChoice.Dismissed;
        var editor = new ConnectionEditorModel(
            () => Controller(new FakeProfiles()),
            pickIcon: (_, _) => Task.FromResult(answer));
        var icon = Field(editor, "iconKey");
        icon.Value = "server";

        icon.ChooseIconCommand!.Execute(null);
        Assert.Equal("server", icon.Value);

        answer = new IconChoice(true, null);
        icon.ChooseIconCommand.Execute(null);
        Assert.Equal(string.Empty, icon.Value);
    }

    /// <summary>With nothing to ask, the button is dim rather than silently doing nothing.</summary>
    [AvaloniaFact]
    public void WithoutAPickerTheIconCannotBeChosen()
    {
        var editor = new ConnectionEditorModel(() => Controller(new FakeProfiles()));

        Assert.False(Field(editor, "iconKey").ChooseIconCommand!.CanExecute(null));
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
    /// 1.x's plain Edit Connection dialog: New starts on S3 and says it is new; the Trust tab
    /// offers to fetch the host key, as Settings' discovery says, and a key accepted is pinned by
    /// the save, which closes the dialog; Edit opens it on the tab asked for, at the version saved
    /// and with the pinned key back in its field, so a rename alone can be saved; Reject records
    /// the key as rejected and clears it; and Cancel writes nothing.
    /// </summary>
    [AvaloniaFact]
    public async Task TheDialogOpensOnANewOrASavedConnectionAndASaveClosesIt()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var profiles = new FakeProfiles();
        var dialogs = new YesDialogs();
        var created = new ConnectionManagerModel(
            () => Controller(profiles),
            dialogs: dialogs,
            hostKeyDiscovery: SshHostKeyDiscoveryMode.AskBeforeFetching);
        var closed = 0;
        var written = 0;
        created.Closed += (_, _) => closed++;
        created.ProfilesChanged += (_, _) => written++;

        await created.OpenAsync(null, cancellationToken: cancellation);
        Assert.Equal(StorageProviderKind.S3, created.Editor.Provider.Kind);
        Assert.True(created.Editor.IsNew);
        Assert.Equal(Ui.ConnectionEditor.NewUnsavedProfile, created.Editor.Status);

        created.Editor.Provider = ConnectionProviderCatalog.Get(StorageProviderKind.Sftp);
        Field(created.Editor, "privateKeyReference").Value = KeyStoreTests.Reference('k');
        Field(created.Editor, "privateKeyPassphraseReference").Value = KeyStoreTests.Reference('p');
        var hostKey = Field(created.Editor, "hostKeyFingerprint");
        Assert.True(hostKey is { IsFingerprint: true, CanFetchFromHost: true, IsText: false });
        Field(created.Editor, "host").Value = "sftp.example.com";
        await created.Editor.RejectFingerprintAsync(hostKey, cancellation);
        Assert.Equal(Ui.ConnectionEditor.SaveBeforeRejecting, created.Editor.Status);

        // Asked, then shown the key: two yeses put it in the field. Offered once per endpoint.
        await created.Editor.OfferHostKeyDiscoveryAsync(cancellation);
        Assert.Equal((HostKey, 2), (hostKey.Value, dialogs.Asked));
        Assert.Equal(Ui.ConnectionEditor.HostKeyAdded, created.Editor.Status);
        hostKey.Value = string.Empty;
        await created.Editor.OfferHostKeyDiscoveryAsync(cancellation);
        Assert.Equal(2, dialogs.Asked);
        dialogs.Choice = Desktop.Shell.DialogChoice.No;
        await created.Editor.FetchHostKeyAsync(cancellation);
        Assert.Equal((string.Empty, Ui.ConnectionEditor.HostKeyNotAdded), (hostKey.Value, created.Editor.Status));
        dialogs.Choice = Desktop.Shell.DialogChoice.Yes;
        await created.Editor.FetchHostKeyAsync(cancellation);
        Fill(created.Editor);
        await created.Editor.SaveAsync(cancellation);
        Assert.Equal(HostKey, Assert.Single(profiles.Pins.Values));
        Assert.Equal((1, 1), (closed, written));

        // As "Review trust…" opens it: on TLS / SSH Trust, which is there for every provider.
        var edited = new ConnectionManagerModel(() => Controller(profiles)) { Tab = ConnectionEditorTab.Trust };
        await edited.OpenAsync(created.Editor.Current!.ConnectionId, cancellationToken: cancellation);
        Assert.Equal(Ui.Format(Ui.ConnectionEditor.LoadedVersionFormat, 1), edited.Editor.LoadedVersion);
        Assert.Equal(HostKey, Field(edited.Editor, "hostKeyFingerprint").Value);
        Name(edited.Editor, "Studio Assets 2");
        Assert.True(edited.Editor.SaveCommand.CanExecute(null));

        var window = new ConnectionManagerWindow { DataContext = edited };
        window.Show();
        Assert.Equal((int)ConnectionEditorTab.Trust, window.FindControl<TabControl>("PART_Tabs")!.SelectedIndex);

        var rejecting = new ConnectionManagerModel(
            () => Controller(profiles), dialogs: new YesDialogs { Choice = Desktop.Shell.DialogChoice.Ok });
        await rejecting.OpenAsync(created.Editor.Current!.ConnectionId, cancellationToken: cancellation);
        await rejecting.Editor.RejectFingerprintAsync(Field(rejecting.Editor, "hostKeyFingerprint"), cancellation);
        Assert.Equal(Ui.ConnectionEditor.RejectedRecorded, rejecting.Editor.Status);
        Assert.Equal(string.Empty, Field(rejecting.Editor, "hostKeyFingerprint").Value);
        Assert.Equal((HostKey, ConnectionTrustDecision.Rejected), profiles.Decisions[^1]);

        edited.CloseCommand.Execute(null);
        Assert.False(window.IsVisible);
        Assert.Equal((1, 0), (profiles.Creates, profiles.Updates));
    }

    /// <summary>
    /// The panel deletes a connection itself, as 1.x's did: it asks first, and a yes deletes at the
    /// version the panel listed, takes the details with it and tells the shell. A connection whose
    /// credentials were refused offers "Fix credentials…", which opens the editor on its tab.
    /// </summary>
    [AvaloniaFact]
    public async Task DeletingFromThePanelAsksFirstAndPinsTheListedVersion()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var profiles = new FakeProfiles();
        var dialogs = new YesDialogs { Choice = Desktop.Shell.DialogChoice.No };
        var refused = new ConnectionHealthSnapshot(
            ConnectionHealthState.NeedsAttention, DateTimeOffset.UtcNow, 5, "Refused",
            RequiresCredentialAction: true);
        ConnectionSummary[] listing = [Summary("Studio Assets", version: 7) with { Health = refused }];
        var sidebar = new ConnectionsSidebar(
            new RelayCommand(static _ => { }),
            () => new ListingAgent(profiles.DeletedVersion is null ? listing : []),
            dialogs,
            profiles: () => profiles);
        var changed = 0;
        sidebar.ConnectionsChanged += (_, _) => changed++;
        (Guid Id, ConnectionEditorTab Tab)? edited = null;
        sidebar.EditConnection = (id, tab) => edited = (id, tab);
        await sidebar.RefreshAsync(cancellation);
        var row = sidebar.Groups[0].Connections[0];
        sidebar.Select(row);

        Assert.Equal(Ui.Connections.DetailFixCredentials, sidebar.AttentionLabel);
        sidebar.AttentionCommand.Execute(null);
        Assert.Equal((row.Id, ConnectionEditorTab.Authentication), edited);

        Assert.False(await sidebar.DeleteListedAsync(row.Id, cancellation));
        Assert.Null(profiles.DeletedVersion);

        // Delete on the card and in the details panel is that, with nothing assigned by the shell.
        dialogs.Choice = Desktop.Shell.DialogChoice.Yes;
        var described = new List<string?>();
        sidebar.PropertyChanged += (_, e) => described.Add(e.PropertyName);
        sidebar.DeleteSelectedCommand.Execute(null);
        Assert.Equal(7, profiles.DeletedVersion);
        Assert.False(sidebar.HasSelection);
        Assert.Contains(nameof(ConnectionsSidebar.Details), described);
        Assert.Equal(1, changed);
    }

    /// <summary>
    /// A favourite, as 1.4 kept one: marked from a card's menu and saved in the profile, listed
    /// under Favorites above the groups and under Go, and still a favourite after an edit.
    /// </summary>
    [AvaloniaFact]
    public async Task AFavouriteIsSavedListedAboveTheGroupsAndUnderGoAndKeptByAnEdit()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var profiles = new FakeProfiles();
        var editor = new ConnectionEditorModel(() => Controller(profiles));
        var saved = Guid.Empty;
        editor.Saved += (_, id) => saved = id;
        Fill(editor);
        await editor.SaveAsync(cancellation);

        // The listing is what the agent says once the flag is written.
        ConnectionSummary[] listing =
        [
            Summary("Studio Assets") with { ConnectionId = saved, IsFavorite = true, FolderPath = "Team" },
            Summary("Backups"),
            Summary("Retired") with { IsFavorite = true, IsEnabled = false },
        ];
        var sidebar = new ConnectionsSidebar(
            new RelayCommand(static _ => { }),
            () => new ListingAgent(listing),
            profiles: () => profiles);
        var changed = 0;
        sidebar.ConnectionsChanged += (_, _) => changed++;

        // Toggle favorite writes the profile back at the version it read, with the flag flipped,
        // and tells the shell, so Welcome's favourites-first list follows.
        await sidebar.ToggleFavoriteAsync(saved, cancellation);
        Assert.Equal(1, profiles.Updates);
        Assert.True(profiles.LastDraft!.Metadata.IsFavorite);
        Assert.Equal(1, changed);

        // Favorites lists the enabled favourite, which is in its own group too; both copies select
        // together, the details say so, and its menu offers the toggle.
        var favourite = Assert.Single(sidebar.Favorites!.Connections);
        Assert.Equal(saved, favourite.Id);
        Assert.Contains(sidebar.Groups.Single(static g => g.Name == "Team").Connections, row => row.Id == saved);
        sidebar.Select(favourite);
        Assert.All(
            sidebar.Groups.SelectMany(static g => g.Connections).Where(row => row.Id == saved),
            static row => Assert.True(row.IsSelected));
        Assert.Contains(new ConnectionDetailRow(Ui.Connections.FieldFavorite, Ui.Connections.DetailYes), sidebar.Details);

        // The details read the saved profile into 1.4's sections, never a secret's value.
        // A local folder has no wire to secure, so no Security section.
        Assert.Equal(
            [Ui.Connections.SectionServer, Ui.Connections.SectionAuthentication,
             Ui.Connections.SectionTransfer, Ui.Connections.SectionOrganisation, Ui.Connections.SectionStatus],
            sidebar.Details.Where(static row => row.IsSection).Select(static row => row.Key));
        Assert.DoesNotContain(sidebar.Details, static row => row.Value == Ui.Connections.DetailLoading);
        Assert.DoesNotContain(sidebar.Details, static row => row.Value.StartsWith("shs_", StringComparison.Ordinal));

        // Plain FTP says it is unencrypted, and SFTP whether its host key is pinned.
        var stored = (await profiles.GetAsync(
            new ConnectionProfileGetRequest(ConnectionProfileIpcContract.CurrentVersion, saved), cancellation)).Profile!;
        ConnectionDetailRow[] Security(StorageConnectionProvider provider) =>
        [
            .. ConnectionDetailFacts.Build(
                favourite.Card,
                null,
                stored with
                {
                    Draft = stored.Draft with { Endpoint = new ConnectionEndpointDocument(provider, Host: "lab") }
                })
        ];
        Assert.Contains(
            new ConnectionDetailRow(Ui.Connections.FieldTransport, Ui.Connections.TransportUnencrypted),
            Security(StorageConnectionProvider.Ftp));
        Assert.Contains(
            new ConnectionDetailRow(Ui.Connections.FieldHostKey, Ui.Connections.DetailPinned),
            Security(StorageConnectionProvider.Sftp));
        Assert.Contains(
            sidebar.ContextEntriesFor(favourite),
            static entry => entry.Label == Ui.Connections.ContextToggleFavorite && entry.Enabled);

        // Settings can list it under Favorites only.
        sidebar.ShowFavoritesInTheirFolders = false;
        Assert.DoesNotContain(sidebar.Groups.SelectMany(static g => g.Connections), row => row.Id == saved);

        // Go lists it under a heading, and says so when there is none.
        var shell = ShellPreview.CreateOnWorkspace();
        var go = shell.Menus.Single(static section => section.Menu == UiMenuId.Go).Items;
        shell.ListFavoritesInTheMenu(listing);
        Assert.Equal([Ui.Shell.Favorites, "Studio Assets"], go.SkipWhile(static e => !e.IsHeading).Select(static e => e.Label));
        shell.ListFavoritesInTheMenu([]);
        Assert.Equal(Ui.Shell.NoFavoriteConnections, go[^1].Label);

        // An edit keeps it a favourite, which the editor has no field for.
        await editor.OpenAsync(saved, cancellation);
        Name(editor, "Studio Assets 2");
        await editor.SaveAsync(cancellation);
        Assert.True(profiles.LastDraft!.Metadata.IsFavorite);
    }

    /// <summary>
    /// A secret is never typed. The field shows a reference and offers the vault and the key store.
    /// </summary>
    /// <remarks>
    /// Only the two material fields offer the key store: a passphrase is filled from whichever
    /// entry is chosen, never picked on its own, because the two are only meaningful together.
    /// </remarks>
    [AvaloniaFact]
    public void ASecretFieldIsNotTypedInto()
    {
        var editor = new ConnectionEditorModel(() => Controller(new FakeProfiles()));
        editor.Provider = ConnectionProviderCatalog.Get(StorageProviderKind.Sftp);

        var key = Field(editor, "privateKeyReference");
        Assert.True(key.IsSecret);
        Assert.False(key.IsText);
        Assert.True(key.IsKeyStoreSlot);
        Assert.NotNull(key.EnrollCommand);
        Assert.NotNull(key.ChooseFromKeyStoreCommand);

        var passphrase = Field(editor, "privateKeyPassphraseReference");
        Assert.True(passphrase.IsSecret);
        Assert.False(passphrase.IsKeyStoreSlot);

        Assert.True(Field(editor, "hostKeyFingerprint").IsFingerprint);

        // Nothing to enrol with and nothing to pick from, so the buttons say so.
        Assert.False(key.EnrollCommand!.CanExecute(null));
        Assert.False(key.ChooseFromKeyStoreCommand!.CanExecute(null));
    }

    /// <summary>
    /// Choosing a stored key fills the material field and the passphrase field together.
    /// </summary>
    /// <remarks>
    /// The listing is asked for the kind the field accepts, so a certificate is never offered
    /// where a key is required. One entry fills both halves: the provider needs the passphrase to
    /// open the material, and the profile requires them together.
    /// </remarks>
    [AvaloniaFact]
    public async Task ChoosingFromTheKeyStoreFillsBothHalves()
    {
        var stored = KeyStoreTests.Entry("build box", KeyStoreMaterialKind.SshPrivateKey);
        var keyStore = new KeyStoreTests.StubKeyStoreAgent { Entries = { stored } };
        KeyStoreEntryDocument[]? offered = null;
        var editor = new ConnectionEditorModel(
            () => Controller(new FakeProfiles()),
            keyStore: () => keyStore,
            pickKey: entries =>
            {
                offered = [.. entries];
                return Task.FromResult<KeyStoreEntryDocument?>(entries[0]);
            });
        editor.Provider = ConnectionProviderCatalog.Get(StorageProviderKind.Sftp);
        var key = Field(editor, "privateKeyReference");
        Assert.True(key.ChooseFromKeyStoreCommand!.CanExecute(null));

        await editor.ChooseFromKeyStoreAsync(key, TestContext.Current.CancellationToken);

        Assert.Equal(KeyStoreMaterialKind.SshPrivateKey, keyStore.Listed!.Kind);
        Assert.Single(offered!);
        Assert.Equal(stored.MaterialReference, key.Value);
        Assert.Equal(stored.PassphraseReference, Field(editor, "privateKeyPassphraseReference").Value);
        Assert.Equal(Ui.Format(Ui.ConnectionEditor.UsingStoredKeyFormat, "build box"), editor.Status);
        Assert.True(editor.IsDirty);
    }

    /// <summary>An empty store says where to import one, rather than opening an empty picker.</summary>
    [AvaloniaFact]
    public async Task AnEmptyKeyStoreSaysSo()
    {
        var picked = false;
        var editor = new ConnectionEditorModel(
            () => Controller(new FakeProfiles()),
            keyStore: () => new KeyStoreTests.StubKeyStoreAgent(),
            pickKey: _ =>
            {
                picked = true;
                return Task.FromResult<KeyStoreEntryDocument?>(null);
            });
        editor.Provider = ConnectionProviderCatalog.Get(StorageProviderKind.Sftp);

        await editor.ChooseFromKeyStoreAsync(Field(editor, "privateKeyReference"), TestContext.Current.CancellationToken);

        Assert.False(picked);
        Assert.Equal(Ui.ConnectionEditor.NoStoredKeys, editor.Status);
    }

    /// <summary>
    /// Enrolling a typed secret sends it to the vault and keeps only the reference.
    /// </summary>
    /// <remarks>
    /// The prompt is a secret one, so the box does not echo. What the field holds afterwards is
    /// the vault's reference, which is what the profile stores; the secret itself went to the
    /// vault and nowhere else.
    /// </remarks>
    [AvaloniaFact]
    public async Task EnrollingATypedSecretKeepsOnlyTheReference()
    {
        var vault = new RecordingVault();
        var dialogs = new KeyStoreTests.RecordingDialogs { PromptAnswer = "hunter2" };
        var editor = new ConnectionEditorModel(() => new ConnectionManagerController(new FakeProfiles(), vault), dialogs: dialogs);
        editor.Provider = ConnectionProviderCatalog.Get(StorageProviderKind.Ftp);
        var password = Field(editor, "passwordReference");
        Assert.True(password.EnrollCommand!.CanExecute(null));

        await editor.EnrollAsync(password, TestContext.Current.CancellationToken);

        Assert.True(dialogs.LastPrompt!.Secret);
        var enrolled = Assert.Single(vault.Enrolled);
        Assert.Equal(SecretMaterialPurpose.Password, enrolled.Purpose);
        Assert.Equal("hunter2"u8.ToArray(), enrolled.Secret);
        Assert.Equal(enrolled.Reference, password.Value);
        Assert.Equal(Ui.Format(Ui.ConnectionEditor.VaultReferenceReadyFormat, 1), editor.Status);
        Assert.True(editor.IsDirty);
    }

    /// <summary>A field that already names a reference is updated in place, keeping the reference.</summary>
    [AvaloniaFact]
    public async Task ReEnrollingUpdatesTheExistingReference()
    {
        var vault = new RecordingVault();
        var dialogs = new KeyStoreTests.RecordingDialogs { PromptAnswer = "hunter3" };
        var editor = new ConnectionEditorModel(() => new ConnectionManagerController(new FakeProfiles(), vault), dialogs: dialogs);
        editor.Provider = ConnectionProviderCatalog.Get(StorageProviderKind.Ftp);
        var password = Field(editor, "passwordReference");
        var existing = KeyStoreTests.Reference('x');
        password.Value = existing;

        await editor.EnrollAsync(password, TestContext.Current.CancellationToken);

        Assert.Equal(existing, Assert.Single(vault.Updated).Reference);
        Assert.Empty(vault.Enrolled);
        Assert.Equal(existing, password.Value);
    }

    /// <summary>Deleting a secret asks first; a "no" leaves the vault and the field alone.</summary>
    [AvaloniaFact]
    public async Task DeletingASecretAsksFirst()
    {
        var vault = new RecordingVault();
        var dialogs = new KeyStoreTests.RecordingDialogs { Choice = Desktop.Shell.DialogChoice.No };
        var editor = new ConnectionEditorModel(() => new ConnectionManagerController(new FakeProfiles(), vault), dialogs: dialogs);
        editor.Provider = ConnectionProviderCatalog.Get(StorageProviderKind.Ftp);
        var password = Field(editor, "passwordReference");
        var existing = KeyStoreTests.Reference('x');
        password.Value = existing;
        Assert.True(password.DeleteSecretCommand!.CanExecute(null));

        await editor.DeleteSecretAsync(password, TestContext.Current.CancellationToken);
        Assert.Empty(vault.Deleted);
        Assert.Equal(existing, password.Value);

        dialogs.Choice = Desktop.Shell.DialogChoice.Yes;
        await editor.DeleteSecretAsync(password, TestContext.Current.CancellationToken);
        Assert.Equal(existing, Assert.Single(vault.Deleted).Reference);
        Assert.Equal(string.Empty, password.Value);
        Assert.Equal(Ui.ConnectionEditor.VaultSecretDeleted, editor.Status);
    }

    /// <summary>
    /// Photographs the Edit Connection dialog in both appearances, for a human to look at.
    /// </summary>
    /// <remarks>
    /// Two hundred descriptor-driven rows is exactly the sort of screen that lays out wrong without
    /// failing anything. Set STORAGEHUB_SHOT_DIR to keep the files.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheDialogCanBePhotographed(bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));

        var manager = new ConnectionManagerModel(() => Controller(new FakeProfiles()));
        await manager.OpenAsync(null, cancellationToken: TestContext.Current.CancellationToken);

        // At the size it opens at, and with the editor alone in it: the connections are the
        // panel's to list, as they were in 1.x.
        var window = new ConnectionManagerWindow { DataContext = manager };
        window.Show();
        window.UpdateLayout();
        Assert.Empty(window.GetVisualDescendants().OfType<ListBox>());

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;

        Directory.CreateDirectory(directory);
        using (var stream = File.Create(
            Path.Combine(directory, $"connection-editor-{(dark ? "dark" : "light")}.png")))
        {
            frame!.Save(stream, new PngBitmapEncoderOptions());
        }

        // And on SFTP, which is where the secret rows are: a reference box with three buttons
        // under it, twice, plus a fingerprint. The row that wraps or clips is one of these.
        manager.Editor.Provider = ConnectionProviderCatalog.Get(StorageProviderKind.Sftp);
        // Taller than the window opens, so every row down to the speed limits is in the frame
        // rather than under the scroll.
        window.Measure(new Size(880, 1600));
        window.Arrange(new Rect(0, 0, 880, 1600));
        window.UpdateLayout();
        var sftp = window.CaptureRenderedFrame();
        Assert.NotNull(sftp);
        using (var stream = File.Create(
            Path.Combine(directory, $"connection-editor-sftp-{(dark ? "dark" : "light")}.png")))
        {
            sftp!.Save(stream, new PngBitmapEncoderOptions());
        }

        // And FTPS, the longest editor: its certificate rows, then Proxy, Speed limits and Advanced.
        manager.Editor.Provider = ConnectionProviderCatalog.Get(StorageProviderKind.Ftps);
        window.Measure(new Size(880, 2100));
        window.Arrange(new Rect(0, 0, 880, 2100));
        window.UpdateLayout();
        var ftps = window.CaptureRenderedFrame();
        Assert.NotNull(ftps);
        using (var stream = File.Create(
            Path.Combine(directory, $"connection-editor-ftps-{(dark ? "dark" : "light")}.png")))
        {
            ftps!.Save(stream, new PngBitmapEncoderOptions());
        }

        // And SFTP's Trust tab: the host key with Fetch from host and Reject beside it, as 1.x.
        manager.Editor.Provider = ConnectionProviderCatalog.Get(StorageProviderKind.Sftp);
        window.FindControl<TabControl>("PART_Tabs")!.SelectedIndex = (int)ConnectionEditorTab.Trust;
        window.Measure(new Size(880, 760));
        window.Arrange(new Rect(0, 0, 880, 760));
        window.UpdateLayout();
        var trust = window.CaptureRenderedFrame();
        Assert.NotNull(trust);
        using (var stream = File.Create(
            Path.Combine(directory, $"connection-editor-trust-{(dark ? "dark" : "light")}.png")))
        {
            trust!.Save(stream, new PngBitmapEncoderOptions());
        }
    }

    /// <summary>A host key as the agent reports one, which is what a pin is checked against.</summary>
    private static readonly string HostKey = "SHA256:" + Convert.ToBase64String(new byte[32]).TrimEnd('=');

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

        internal ConnectionProfileDraft? LastDraft { get; private set; }

        internal StorageIpcFailure? Failure { get; init; }

        /// <summary>The fingerprint each connection is pinned to, one trusted record apiece.</summary>
        internal Dictionary<Guid, string> Pins { get; } = [];

        /// <summary>Each trust decision asked for, in order.</summary>
        internal List<(string Fingerprint, ConnectionTrustDecision Decision)> Decisions { get; } = [];

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
                ConnectionProfileIpcContract.CurrentVersion, ConnectionProfileWriteStatus.Succeeded));
        }

        public Task<ConnectionTrustGetResponse> GetTrustAsync(
            ConnectionTrustGetRequest request,
            CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            ConnectionTrustRecordDocument[] records = Pins.TryGetValue(request.ConnectionId, out var pin)
                ? [new("trust-1", pin, ConnectionTrustDecision.Trusted, now, now, null, null, 1)]
                : [];
            return Task.FromResult(new ConnectionTrustGetResponse(
                ConnectionTrustIpcContract.CurrentVersion,
                new ConnectionTrustSnapshot(
                    request.ConnectionId,
                    request.ExpectedProfileVersion,
                    new ConnectionTrustTargetDocument(ConnectionTrustArtifactKind.SshHostKey, "sample", 22),
                    records)));
        }

        public Task<ConnectionTrustMutationResponse> DecideTrustAsync(
            ConnectionTrustDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            Decisions.Add((request.Sha256Fingerprint, request.Decision));
            if (request.Decision == ConnectionTrustDecision.Trusted) Pins[request.ConnectionId] = request.Sha256Fingerprint;
            else Pins.Remove(request.ConnectionId);
            return Task.FromResult(new ConnectionTrustMutationResponse(
                ConnectionTrustIpcContract.CurrentVersion, ConnectionTrustMutationStatus.Succeeded));
        }

        public Task<ConnectionTrustMutationResponse> RolloverTrustAsync(
            ConnectionTrustRolloverRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <summary>Every endpoint presents <see cref="HostKey"/>.</summary>
        public Task<ConnectionSshHostKeyDiscoveryResponse> DiscoverSshHostKeyAsync(
            ConnectionSshHostKeyDiscoveryRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionSshHostKeyDiscoveryResponse(
                ConnectionTrustIpcContract.CurrentVersion,
                new ConnectionTrustTargetDocument(ConnectionTrustArtifactKind.SshHostKey, request.Host, request.Port),
                "ssh-ed25519",
                HostKey,
                null));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private Task<ConnectionProfileWriteResponse> Written(
            Guid id,
            long version,
            ConnectionProfileDraft draft)
        {
            LastDraft = draft;
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

    /// <summary>The vault, for tests that are not about it: every call throws rather than quietly answering.</summary>
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

    /// <summary>The vault, which answers with a reference and remembers what it was given.</summary>
    private sealed class RecordingVault : IRemoteSecretVaultClient
    {
        private int _count;

        internal List<(string Reference, SecretMaterialPurpose Purpose, byte[] Secret)> Enrolled { get; } = [];

        internal List<(string Reference, SecretMaterialPurpose Purpose, byte[] Secret)> Updated { get; } = [];

        internal List<(string Reference, SecretMaterialPurpose Purpose)> Deleted { get; } = [];

        public Task<SecretVaultResponse> EnrollAsync(
            SecretMaterialPurpose purpose,
            ReadOnlyMemory<byte> secret,
            CancellationToken cancellationToken = default)
        {
            var reference = KeyStoreTests.Reference((char)('a' + _count++));
            Enrolled.Add((reference, purpose, secret.ToArray()));
            return Task.FromResult(new SecretVaultResponse(
                SecretVaultIpcContract.CurrentVersion, SecretVaultOperation.Enroll, true, reference, 1));
        }

        public Task<SecretVaultResponse> UpdateAsync(
            string reference,
            SecretMaterialPurpose purpose,
            ReadOnlyMemory<byte> secret,
            CancellationToken cancellationToken = default)
        {
            Updated.Add((reference, purpose, secret.ToArray()));
            return Task.FromResult(new SecretVaultResponse(
                SecretVaultIpcContract.CurrentVersion, SecretVaultOperation.Update, true, reference, 2));
        }

        public Task<SecretVaultResponse> DeleteAsync(
            string reference,
            SecretMaterialPurpose purpose,
            CancellationToken cancellationToken = default)
        {
            Deleted.Add((reference, purpose));
            return Task.FromResult(new SecretVaultResponse(
                SecretVaultIpcContract.CurrentVersion, SecretVaultOperation.Delete, true, reference));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class YesDialogs : Desktop.Shell.IDialogService
    {
        internal Desktop.Shell.DialogChoice Choice { get; set; } = Desktop.Shell.DialogChoice.Yes;

        internal int Asked { get; private set; }

        public Task ShowAsync(
            Desktop.Shell.DialogRequest request,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<Desktop.Shell.DialogChoice> ConfirmAsync(
            Desktop.Shell.DialogRequest request,
            CancellationToken cancellationToken = default)
        {
            Asked++;
            return Task.FromResult(Choice);
        }

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
