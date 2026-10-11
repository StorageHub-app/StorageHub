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
using static StorageHub.Desktop.Tests.WorkspaceFakes;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The read-only object inspector, and the pane command that opens it.
/// </summary>
/// <remarks>
/// The controller underneath was already in Core and already tested for its paging. What is
/// checked here is the words: that each section reports its own failure while the others show,
/// that the status says how much was loaded, and that Properties is offered for exactly one file
/// on a saved connection and refuses everything else with a sentence.
/// </remarks>
public class ObjectInspectorTests
{
    private static readonly ObjectInspectorAddress Address = new(
        Guid.NewGuid(), "root", "shots/render.exr", EntityTag: "etag-1");

    [AvaloniaFact]
    public async Task VersionsMetadataAndTagsReachTheirTabsInWords()
    {
        var agent = new ScriptedInspector
        {
            Versions = [Version("v2", latest: true, size: 2048), Version("v1", deleteMarker: true)],
            Metadata = [new ObjectMetadataEntry("content-type", "image/x-exr")],
            Tags = [new ObjectTagEntry("project", "alpha")]
        };
        await using var model = new ObjectInspectorModel(new ObjectInspectorController(agent, Address));

        Assert.Equal(Ui.Inspector.TheInspectorConnectsToTheBackgroundAgent, model.Status.Text);
        await model.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Ui.Format(Ui.Inspector.TabVersionsFormat, 2), model.VersionsTabLabel);
        Assert.Equal(Ui.Format(Ui.Inspector.TabMetadataFormat, 1), model.MetadataTabLabel);
        Assert.Equal(Ui.Format(Ui.Inspector.TabTagsFormat, 1), model.TagsTabLabel);

        Assert.Equal(Ui.Inspector.Latest, model.Versions[0].Current);
        Assert.Equal(2048.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), model.Versions[0].Size);
        Assert.Equal(Ui.Inspector.No, model.Versions[0].DeleteMarker);
        Assert.Equal(string.Empty, model.Versions[1].Current);
        Assert.Equal(Ui.Inspector.NotReported, model.Versions[1].Size);
        Assert.Equal(Ui.Inspector.Yes, model.Versions[1].DeleteMarker);

        Assert.Equal("image/x-exr", Assert.Single(model.Metadata).Value);
        Assert.Equal("alpha", Assert.Single(model.Tags).Value);
        Assert.Equal(Ui.Format(Ui.Inspector.LoadedSummaryFormat, 2, 1, 1), model.Status.Text);
        Assert.True(model.Status.IsSuccess);
        Assert.False(model.CanLoadMore);
    }

    /// <summary>A section the provider refuses says so under its own tab; the others still show.</summary>
    [AvaloniaFact]
    public async Task AFailedSectionKeepsTheOthers()
    {
        var agent = new ScriptedInspector
        {
            Versions = [Version("v1", latest: true)],
            TagsFailure = new StorageIpcFailure(
                "storage.tags.unsupported", StorageIpcFailureCategory.Unsupported, "This provider has no tags.", false)
        };
        await using var model = new ObjectInspectorModel(new ObjectInspectorController(agent, Address));

        await model.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Single(model.Versions);
        Assert.Equal(Ui.Format(Ui.Inspector.LoadedVersionsFormat, 1), model.VersionsNotice.Text);
        Assert.False(model.VersionsNotice.IsWarning);
        Assert.Equal(Ui.Inspector.NoMetadataReturned, model.MetadataNotice.Text);
        Assert.Equal("This provider has no tags.", model.TagsNotice.Text);
        Assert.True(model.TagsNotice.IsWarning);
        Assert.Equal(Ui.Format(Ui.Inspector.LoadedWithFailuresFormat, 1), model.Status.Text);
        Assert.True(model.Status.IsWarning);
    }

    /// <summary>Load more appends the next page and dims once there is no more.</summary>
    [AvaloniaFact]
    public async Task LoadingMoreAppendsThenStops()
    {
        var agent = new ScriptedInspector
        {
            Versions = [Version("v3", latest: true), Version("v2")],
            NextPage = [Version("v1")]
        };
        await using var model = new ObjectInspectorModel(new ObjectInspectorController(agent, Address));

        await model.LoadAsync(TestContext.Current.CancellationToken);
        Assert.True(model.CanLoadMore);
        Assert.True(model.LoadMoreCommand.CanExecute(null));

        await model.LoadMoreAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["v3", "v2", "v1"], model.Versions.Select(static row => row.VersionId));
        Assert.False(model.CanLoadMore);
        Assert.Equal(Ui.Format(Ui.Inspector.TabVersionsFormat, 3), model.VersionsTabLabel);
    }

    /// <summary>An agent that cannot be reached is a sentence in the status, not a crash.</summary>
    [AvaloniaFact]
    public async Task AnUnreachableAgentIsReported()
    {
        var agent = new ScriptedInspector { Throws = new TimeoutException() };
        await using var model = new ObjectInspectorModel(new ObjectInspectorController(agent, Address));

        await model.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Ui.Inspector.TheObjectInspectorRequestTimedOut, model.Status.Text);
        Assert.True(model.Status.IsWarning);
        Assert.Empty(model.Versions);
    }

    /// <summary>A long path is shortened from the left, never starting on half a character.</summary>
    [Fact]
    public void ALongPathIsShortenedFromTheLeft()
    {
        var path = string.Concat(Enumerable.Repeat("folder/", 20)) + "render.exr";

        var shortened = ObjectInspectorModel.ShortenForTitle(path, 30);

        Assert.StartsWith("…", shortened, StringComparison.Ordinal);
        Assert.EndsWith("render.exr", shortened, StringComparison.Ordinal);
        Assert.Equal(31, shortened.Length);

        var emoji = new string('a', 20) + "😀" + new string('b', 10);
        var cut = ObjectInspectorModel.ShortenForTitle(emoji, 11);
        Assert.False(char.IsLowSurrogate(cut[1]));
    }

    /// <summary>
    /// Properties is offered for one file on a saved connection, and opens the inspector on it.
    /// </summary>
    /// <remarks>
    /// The address that reaches the inspector is what the pane listed: the connection, the root the
    /// agent identified, and the entry's own path and tag. Nothing selected dims the button.
    /// </remarks>
    [AvaloniaFact]
    public async Task PropertiesOpensTheInspectorOnTheOneSelectedFile()
    {
        var connection = Summary("Studio Assets");
        var agent = new RecordingAgent([connection]);
        ObjectInspectorAddress? opened = null;
        StorageItemKind? kind = null;
        await using var pane = new BrowserPaneModel(agent, inspect: (address, itemKind) =>
        {
            opened = address;
            kind = itemKind;
            return Task.CompletedTask;
        });
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
        await pane.OpenConnectionAsync(connection.ConnectionId, TestContext.Current.CancellationToken);

        Assert.False(pane.PropertiesCommand.CanExecute(null));

        pane.SelectedRows.Add(pane.Rows.Single(row => row.Name == "render.exr"));
        Assert.True(pane.PropertiesCommand.CanExecute(null));

        await pane.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(StorageItemKind.File, kind);
        Assert.NotNull(opened);
        Assert.Equal(connection.ConnectionId, opened!.ConnectionId);
        Assert.Equal("root", opened.RootIdentity);
        Assert.Equal("render.exr", opened.RelativePath);
        Assert.Equal("etag-render.exr", opened.EntityTag);
    }

    /// <summary>
    /// A folder has properties too, permissions and times, and opens as a folder.
    /// </summary>
    [AvaloniaFact]
    public async Task PropertiesOpensOnAFolderAsAFolder()
    {
        var connection = Summary("Studio Assets");
        var agent = new RecordingAgent([connection]);
        ObjectInspectorAddress? opened = null;
        StorageItemKind? kind = null;
        await using var pane = new BrowserPaneModel(agent, inspect: (address, itemKind) =>
        {
            opened = address;
            kind = itemKind;
            return Task.CompletedTask;
        });
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
        await pane.OpenConnectionAsync(connection.ConnectionId, TestContext.Current.CancellationToken);
        pane.SelectedRows.Add(pane.Rows.Single(row => row.Name == "reports"));
        Assert.True(pane.PropertiesCommand.CanExecute(null));

        await pane.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal("reports", opened?.RelativePath);
        Assert.Equal(StorageItemKind.Directory, kind);
    }

    /// <summary>The menu path, where nothing dims, answers two selected items with a sentence.</summary>
    [AvaloniaFact]
    public async Task InspectingTwoItemsSaysWhyNot()
    {
        var connection = Summary("Studio Assets");
        var agent = new RecordingAgent([connection]);
        var opened = false;
        await using var pane = new BrowserPaneModel(agent, inspect: (_, _) =>
        {
            opened = true;
            return Task.CompletedTask;
        });
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
        await pane.OpenConnectionAsync(connection.ConnectionId, TestContext.Current.CancellationToken);
        pane.SelectedRows.Add(pane.Rows.Single(row => row.Name == "reports"));
        pane.SelectedRows.Add(pane.Rows.Single(row => row.Name == "render.exr"));

        await pane.InspectAsync(TestContext.Current.CancellationToken);

        Assert.False(opened);
        Assert.Equal(Ui.Shell.SelectOneToInspect, pane.Status);
    }

    /// <summary>Without anything to open the inspector with, the command is unavailable.</summary>
    [AvaloniaFact]
    public async Task WithoutAnInspectorPropertiesIsUnavailable()
    {
        var connection = Summary("Studio Assets");
        await using var pane = new BrowserPaneModel(new RecordingAgent([connection]));
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
        await pane.OpenConnectionAsync(connection.ConnectionId, TestContext.Current.CancellationToken);
        pane.SelectedRows.Add(pane.Rows.Single(row => row.Name == "render.exr"));

        Assert.False(pane.PropertiesCommand.CanExecute(null));
    }

    /// <summary>Photographs the inspector on each of its first two tabs, for a human to look at.</summary>
    [AvaloniaTheory]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    [InlineData(false, 1)]
    public async Task TheInspectorCanBePhotographed(bool dark, int tab)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));

        var agent = new ScriptedInspector
        {
            Versions =
            [
                Version("3sL4kqYzxWkZk8ymgTqXhk9ONoP2HBn1", latest: true, size: 48_213_990),
                Version("Zm9vYmFyMTIzNDU2Nzg5MGFiY2RlZmdo", size: 48_100_000),
                Version("dGhpcyBpcyBhIGRlbGV0ZSBtYXJrZXI=", deleteMarker: true)
            ],
            NextPage = [Version("older")],
            Metadata =
            [
                new ObjectMetadataEntry("content-type", "image/x-exr"),
                new ObjectMetadataEntry("x-amz-meta-shot", "sq010_sh020")
            ],
            TagsFailure = new StorageIpcFailure(
                "storage.tags.unsupported", StorageIpcFailureCategory.Unsupported,
                "This provider does not report object tags.", false),
            Attributes = SftpFile with { CanSetModifiedTime = false }
        };
        await using var model = new ObjectInspectorModel(
            new ObjectInspectorController(agent, Address),
            new PromptDialogs(null),
            static (_, _) => Task.FromResult<int?>(null),
            TimeZoneInfo.Utc);
        await model.LoadAsync(TestContext.Current.CancellationToken);
        model.SelectedTab = tab;

        var window = new ObjectInspectorWindow { DataContext = model };
        window.Show();
        window.Measure(new Size(1120, 720));
        window.Arrange(new Rect(0, 0, 1120, 720));
        window.UpdateLayout();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;

        Directory.CreateDirectory(directory);
        using var stream = File.Create(
            Path.Combine(directory, $"object-inspector-{(tab == 0 ? "general" : "versions")}-{(dark ? "dark" : "light")}.png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }

    /// <summary>
    /// What an SFTP server reports for a file: mode and numeric owner and group, no creation time.
    /// </summary>
    private static readonly ItemAttributes SftpFile = new(
        StorageItemKind.File,
        Size: 48_213_990,
        ModifiedUtc: new DateTimeOffset(2026, 10, 1, 9, 15, 0, TimeSpan.Zero),
        AccessedUtc: new DateTimeOffset(2026, 10, 9, 18, 2, 41, TimeSpan.Zero),
        UnixMode: 0x1A4,
        OwnerId: 1000,
        GroupId: 100,
        CanSetPermissions: true,
        CanSetModifiedTime: true);

    /// <summary>
    /// The General tab says what the connection reported, in words, and what it did not as not reported.
    /// </summary>
    [AvaloniaFact]
    public async Task GeneralShowsTheAttributesTheConnectionReported()
    {
        var agent = new ScriptedInspector { Attributes = SftpFile };
        await using var model = new ObjectInspectorModel(
            new ObjectInspectorController(agent, Address), timeZone: TimeZoneInfo.Utc);

        await model.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, model.SelectedTab);
        Assert.Equal(Ui.Inspector.KindFile, Row(model, Ui.Inspector.Kind));
        Assert.Equal(48_213_990.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), Row(model, Ui.Inspector.Size));
        Assert.Equal("2026-10-01 09:15:00", Row(model, Ui.Inspector.Modified));
        Assert.Equal(Ui.Inspector.NotReported, Row(model, Ui.Inspector.Created));
        Assert.Equal("2026-10-09 18:02:41", Row(model, Ui.Inspector.Accessed));
        Assert.Equal("rw-r--r-- (0644)", Row(model, Ui.Inspector.Permissions));
        Assert.Equal("1000", Row(model, Ui.Inspector.Owner));
        Assert.Equal("100", Row(model, Ui.Inspector.Group));
        Assert.DoesNotContain(model.General, row => row.Name == Ui.Inspector.LinkTarget);
        Assert.Equal(Ui.Format(Ui.Inspector.TimesAreLocalFormat, "UTC+00:00"), model.GeneralNotice.Text);
        Assert.True(model.Status.IsSuccess);
    }

    /// <summary>FTP gives names, SFTP gives IDs; a server that gives both shows both.</summary>
    [AvaloniaFact]
    public async Task OwnerAndGroupShowANameAndAnId()
    {
        var agent = new ScriptedInspector
        {
            Attributes = new ItemAttributes(StorageItemKind.SymbolicLink, Owner: "alice", OwnerId: 1000, Group: "staff",
                LinkTarget: "../shared/render.exr")
        };
        await using var model = new ObjectInspectorModel(new ObjectInspectorController(agent, Address));

        await model.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Ui.Format(Ui.Inspector.PrincipalWithIdFormat, "alice", 1000), Row(model, Ui.Inspector.Owner));
        Assert.Equal("staff", Row(model, Ui.Inspector.Group));
        Assert.Equal(Ui.Inspector.KindLink, Row(model, Ui.Inspector.Kind));
        Assert.Equal("../shared/render.exr", Row(model, Ui.Inspector.LinkTarget));
        Assert.Equal(Ui.Inspector.NotReported, Row(model, Ui.Inspector.Permissions));
    }

    /// <summary>
    /// A folder has no versions, metadata or tags to ask for: only General loads, and the rest hide.
    /// </summary>
    [AvaloniaFact]
    public async Task AFolderLoadsOnlyItsAttributes()
    {
        var agent = new ScriptedInspector
        {
            Attributes = new ItemAttributes(StorageItemKind.Directory, UnixMode: 0x1ED, CanSetPermissions: true)
        };
        await using var model = new ObjectInspectorModel(new ObjectInspectorController(
            agent, Address with { RelativePath = "shots" }, kind: StorageItemKind.Directory));

        await model.LoadAsync(TestContext.Current.CancellationToken);

        Assert.False(model.IsFile);
        Assert.Equal(0, agent.VersionRequests);
        Assert.Equal(Ui.Inspector.KindFolder, Row(model, Ui.Inspector.Kind));
        Assert.DoesNotContain(model.General, row => row.Name == Ui.Inspector.Size);
        Assert.Equal(Ui.Inspector.PropertiesLoaded, model.Status.Text);
        Assert.Equal(Ui.Format(Ui.Inspector.PropertiesTitleFormat, "shots"), model.Title);
    }

    /// <summary>A General tab the agent could not read says why, and the file's other tabs still load.</summary>
    [AvaloniaFact]
    public async Task UnreadableAttributesKeepTheOtherSections()
    {
        var agent = new ScriptedInspector
        {
            Versions = [Version("v1", latest: true)],
            AttributesFailure = new StorageIpcFailure(
                "storage.attributes.unavailable", StorageIpcFailureCategory.Unavailable, "Not now.", true)
        };
        await using var model = new ObjectInspectorModel(new ObjectInspectorController(agent, Address));

        await model.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(model.General);
        Assert.Equal("Not now.", model.GeneralNotice.Text);
        Assert.True(model.GeneralNotice.IsWarning);
        Assert.Single(model.Versions);
        Assert.Equal(Ui.Format(Ui.Inspector.LoadedWithFailuresFormat, 1), model.Status.Text);
        Assert.False(model.ChangePermissionsCommand.CanExecute(null));
    }

    /// <summary>
    /// Change permissions starts the dialog from the mode now, sends the one chosen, and shows the
    /// mode the server then reports.
    /// </summary>
    [AvaloniaFact]
    public async Task ChangingPermissionsSendsTheChosenModeAndShowsTheNewOne()
    {
        var agent = new ScriptedInspector { Attributes = SftpFile };
        (string Name, int Mode)? asked = null;
        await using var model = new ObjectInspectorModel(
            new ObjectInspectorController(agent, Address),
            askPermissions: (name, mode) =>
            {
                asked = (name, mode);
                return Task.FromResult<int?>(0x1ED);
            });
        var changed = 0;
        model.AttributesChanged += (_, _) => changed++;
        await model.LoadAsync(TestContext.Current.CancellationToken);
        Assert.True(model.ChangePermissionsCommand.CanExecute(null));

        await model.ChangePermissionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(("render.exr", 0x1A4), asked);
        Assert.Equal(0x1ED, agent.PermissionsSent);
        Assert.Equal("rwxr-xr-x (0755)", Row(model, Ui.Inspector.Permissions));
        Assert.Equal(Ui.Format(Ui.Inspector.PermissionsChangedFormat, "rwxr-xr-x (0755)"), model.Status.Text);
        Assert.True(model.Status.IsSuccess);
        Assert.Equal(1, changed);
    }

    /// <summary>A dismissed dialog sends nothing.</summary>
    [AvaloniaFact]
    public async Task ADismissedPermissionsDialogChangesNothing()
    {
        var agent = new ScriptedInspector { Attributes = SftpFile };
        await using var model = new ObjectInspectorModel(
            new ObjectInspectorController(agent, Address),
            askPermissions: static (_, _) => Task.FromResult<int?>(null));
        await model.LoadAsync(TestContext.Current.CancellationToken);

        await model.ChangePermissionsAsync(TestContext.Current.CancellationToken);

        Assert.Null(agent.PermissionsSent);
        Assert.Equal("rw-r--r-- (0644)", Row(model, Ui.Inspector.Permissions));
    }

    /// <summary>A server that refuses the change says why in the status, and nothing is reloaded.</summary>
    [AvaloniaFact]
    public async Task ARefusedChangeSaysWhy()
    {
        var agent = new ScriptedInspector
        {
            Attributes = SftpFile,
            ChangeFailure = new StorageIpcFailure(
                "storage.attributes.unauthorized", StorageIpcFailureCategory.Unauthorized,
                "The server refused; the account may not own this item or may lack the right to change it.", false)
        };
        await using var model = new ObjectInspectorModel(
            new ObjectInspectorController(agent, Address),
            askPermissions: static (_, _) => Task.FromResult<int?>(0x1C0));
        var changed = 0;
        model.AttributesChanged += (_, _) => changed++;
        await model.LoadAsync(TestContext.Current.CancellationToken);

        await model.ChangePermissionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(agent.ChangeFailure.Message, model.Status.Text);
        Assert.True(model.Status.IsWarning);
        Assert.Equal("rw-r--r-- (0644)", Row(model, Ui.Inspector.Permissions));
        Assert.Equal(0, changed);
    }

    /// <summary>
    /// A connection that cannot change either thing dims both buttons and says so; the menu-less
    /// path refuses with the same sentence rather than asking.
    /// </summary>
    [AvaloniaFact]
    public async Task AConnectionThatCannotChangeDimsAndSaysSo()
    {
        var agent = new ScriptedInspector { Attributes = new ItemAttributes(StorageItemKind.File, Size: 10) };
        var asked = false;
        await using var model = new ObjectInspectorModel(
            new ObjectInspectorController(agent, Address),
            new PromptDialogs("2026-01-01 00:00:00"),
            (_, _) =>
            {
                asked = true;
                return Task.FromResult<int?>(0x1FF);
            });
        await model.LoadAsync(TestContext.Current.CancellationToken);

        Assert.False(model.ChangePermissionsCommand.CanExecute(null));
        Assert.False(model.SetModifiedTimeCommand.CanExecute(null));
        Assert.Equal(Ui.Inspector.CannotChangeEither, model.ChangesNotice);
        Assert.True(model.HasChangesNotice);

        await model.ChangePermissionsAsync(TestContext.Current.CancellationToken);
        Assert.False(asked);
        Assert.Equal(Ui.Inspector.CannotChangePermissions, model.Status.Text);

        await model.SetModifiedTimeAsync(TestContext.Current.CancellationToken);
        Assert.Null(agent.ModifiedSent);
        Assert.Equal(Ui.Inspector.CannotSetModifiedTime, model.Status.Text);
    }

    /// <summary>One that can change only one of them names the other.</summary>
    [AvaloniaFact]
    public async Task AConnectionThatCanChangeOneNamesTheOther()
    {
        var agent = new ScriptedInspector { Attributes = SftpFile with { CanSetPermissions = false } };
        await using var model = new ObjectInspectorModel(
            new ObjectInspectorController(agent, Address),
            new PromptDialogs(null),
            static (_, _) => Task.FromResult<int?>(null));
        await model.LoadAsync(TestContext.Current.CancellationToken);

        Assert.False(model.ChangePermissionsCommand.CanExecute(null));
        Assert.True(model.SetModifiedTimeCommand.CanExecute(null));
        Assert.Equal(Ui.Inspector.CannotChangePermissions, model.ChangesNotice);
    }

    /// <summary>
    /// The modified time is typed in this computer's zone, starting from the time now, and sent in UTC.
    /// </summary>
    [AvaloniaFact]
    public async Task SettingTheModifiedTimeReadsLocalTimeAndSendsUtc()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");
        var agent = new ScriptedInspector { Attributes = SftpFile };
        var dialogs = new PromptDialogs("2026-10-11 14:30");
        await using var model = new ObjectInspectorModel(
            new ObjectInspectorController(agent, Address), dialogs, timeZone: zone);
        var changed = 0;
        model.AttributesChanged += (_, _) => changed++;
        await model.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("2026-10-01 11:15:00", Row(model, Ui.Inspector.Modified));

        await model.SetModifiedTimeAsync(TestContext.Current.CancellationToken);

        Assert.Equal("2026-10-01 11:15:00", dialogs.Asked!.Value);
        Assert.Contains("UTC+02:00", dialogs.Asked.Label, StringComparison.Ordinal);
        Assert.NotNull(dialogs.Asked.Validate!("yesterday"));
        Assert.Null(dialogs.Asked.Validate("2026-10-11 14:30:00"));
        Assert.Equal(new DateTimeOffset(2026, 10, 11, 12, 30, 0, TimeSpan.Zero), agent.ModifiedSent);
        Assert.Equal(TimeSpan.Zero, agent.ModifiedSent!.Value.Offset);
        Assert.Equal("2026-10-11 14:30:00", Row(model, Ui.Inspector.Modified));
        Assert.Equal(Ui.Format(Ui.Inspector.ModifiedTimeSetFormat, "2026-10-11 14:30:00"), model.Status.Text);
        Assert.Equal(1, changed);
    }

    /// <summary>What the prompt accepts: the sortable form, seconds optional, inside the contract's range.</summary>
    [Fact]
    public void TypedTimesAreReadStrictly()
    {
        Assert.True(ObjectInspectorModel.TryParseModifiedTime(" 2026-10-11 14:30:05 ", TimeZoneInfo.Utc, out var exact, out _));
        Assert.Equal(new DateTimeOffset(2026, 10, 11, 14, 30, 5, TimeSpan.Zero), exact);
        Assert.True(ObjectInspectorModel.TryParseModifiedTime("2026-10-11 14:30", TimeZoneInfo.Utc, out _, out _));

        Assert.False(ObjectInspectorModel.TryParseModifiedTime("11/10/2026 14:30", TimeZoneInfo.Utc, out _, out var format));
        Assert.StartsWith(
            Ui.Format(Ui.Inspector.ModifiedTimeNotUnderstoodFormat, "|").Split('|')[0], format, StringComparison.Ordinal);
        Assert.False(ObjectInspectorModel.TryParseModifiedTime(null, TimeZoneInfo.Utc, out _, out _));

        Assert.False(ObjectInspectorModel.TryParseModifiedTime("1969-12-31 23:59:59", TimeZoneInfo.Utc, out _, out var early));
        Assert.Equal(Ui.Inspector.ModifiedTimeOutOfRange, early);
        Assert.False(ObjectInspectorModel.TryParseModifiedTime("2106-02-07 06:28:16", TimeZoneInfo.Utc, out _, out var late));
        Assert.Equal(Ui.Inspector.ModifiedTimeOutOfRange, late);
        Assert.True(ObjectInspectorModel.TryParseModifiedTime("2106-02-07 06:28:15", TimeZoneInfo.Utc, out _, out _));
    }

    /// <summary>
    /// The permissions dialog keeps the boxes and the octal in step, and keeps the special bits.
    /// </summary>
    [Fact]
    public void ThePermissionsDialogKeepsBoxesAndOctalInStep()
    {
        var model = new PermissionsModel("deploy.sh", 0x9ED); // 4755: set-user-ID, rwxr-xr-x

        Assert.Equal("4755", model.Octal);
        Assert.Equal("rwsr-xr-x", model.Symbolic);
        Assert.True(model.OwnerExecute);
        Assert.False(model.GroupWrite);

        model.GroupWrite = true;
        Assert.Equal("4775", model.Octal);
        Assert.Equal(0x9FD, model.Mode);

        model.Octal = "640";
        Assert.Equal(0x1A0, model.Mode);
        Assert.True(model.OwnerRead && model.OwnerWrite && !model.OwnerExecute);
        Assert.True(model.GroupRead && !model.GroupWrite);
        Assert.False(model.OthersRead);
        Assert.Equal("rw-r-----", model.Symbolic);

        model.AcceptCommand.Execute(null);
        Assert.Equal(0x1A0, model.Answer);
    }

    /// <summary>Octal that is not octal dims Apply, says why, and a box ticked afterwards recovers it.</summary>
    [Fact]
    public void UnreadableOctalDimsApply()
    {
        var model = new PermissionsModel("deploy.sh", 0x1ED);
        var closed = false;
        model.Closed += (_, _) => closed = true;

        model.Octal = "789";
        Assert.True(model.HasError);
        Assert.Equal(Ui.Inspector.OctalNotUnderstood, model.Error);
        Assert.False(model.AcceptCommand.CanExecute(null));

        model.OthersWrite = true;
        Assert.False(model.HasError);
        Assert.Equal("0757", model.Octal);
        Assert.True(model.AcceptCommand.CanExecute(null));

        model.CancelCommand.Execute(null);
        Assert.True(closed);
        Assert.Null(model.Answer);
    }

    /// <summary>Photographs the permissions dialog, for a human to look at.</summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void ThePermissionsDialogCanBePhotographed(bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));

        var model = new PermissionsModel("render.exr", 0x1A4) { Octal = "75x" };
        var window = new PermissionsWindow { DataContext = model };
        window.Show();
        window.UpdateLayout();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;

        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, $"permissions-{(dark ? "dark" : "light")}.png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }

    private static string Row(ObjectInspectorModel model, string name) =>
        model.General.Single(row => row.Name == name).Value;

    /// <summary>Answers a prompt with a fixed text, and keeps what it was asked.</summary>
    private sealed class PromptDialogs(string? answer) : IDialogService
    {
        internal DialogPromptRequest? Asked { get; private set; }

        public Task ShowAsync(DialogRequest request, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<DialogChoice> ConfirmAsync(DialogRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(DialogChoice.Cancel);

        public Task<string?> PromptAsync(DialogPromptRequest request, CancellationToken cancellationToken = default)
        {
            Asked = request;
            return Task.FromResult(answer);
        }
    }

    private static ObjectVersionSummary Version(
        string id, bool latest = false, long? size = null, bool deleteMarker = false) =>
        new(id, deleteMarker ? null : "etag-" + id, size, DateTimeOffset.UtcNow, latest, deleteMarker);

    /// <summary>The inspector's read side, scripted by the test.</summary>
    private sealed class ScriptedInspector : IObjectInspectorAgentClient
    {
        internal ObjectVersionSummary[] Versions { get; set; } = [];

        internal ObjectVersionSummary[]? NextPage { get; set; }

        internal ObjectMetadataEntry[] Metadata { get; set; } = [];

        internal ObjectTagEntry[] Tags { get; set; } = [];

        internal StorageIpcFailure? TagsFailure { get; set; }

        internal Exception? Throws { get; set; }

        internal ItemAttributes? Attributes { get; set; } = new(StorageItemKind.File, Size: 2048);

        internal StorageIpcFailure? AttributesFailure { get; set; }

        /// <summary>What a change is answered with; nothing makes it succeed.</summary>
        internal StorageIpcFailure? ChangeFailure { get; set; }

        internal int VersionRequests { get; private set; }

        internal int? PermissionsSent { get; private set; }

        internal DateTimeOffset? ModifiedSent { get; private set; }

        public Task<ItemAttributesGetResponse> GetItemAttributesAsync(
            ItemAttributesGetRequest request, CancellationToken cancellationToken = default)
        {
            if (Throws is { } error) throw error;
            return Task.FromResult(AttributesFailure is null
                ? new ItemAttributesGetResponse(ItemAttributeIpcContract.CurrentVersion, request.Address, Attributes)
                : new ItemAttributesGetResponse(
                    ItemAttributeIpcContract.CurrentVersion, request.Address, null, AttributesFailure));
        }

        public Task<ItemPermissionsSetResponse> SetItemPermissionsAsync(
            ItemPermissionsSetRequest request, CancellationToken cancellationToken = default)
        {
            PermissionsSent = request.UnixMode;
            if (ChangeFailure is null) Attributes = Attributes! with { UnixMode = request.UnixMode };
            return Task.FromResult(new ItemPermissionsSetResponse(
                ItemAttributeIpcContract.CurrentVersion, request.Address, ChangeFailure is null, ChangeFailure));
        }

        public Task<ItemModifiedTimeSetResponse> SetItemModifiedTimeAsync(
            ItemModifiedTimeSetRequest request, CancellationToken cancellationToken = default)
        {
            ModifiedSent = request.ModifiedUtc;
            if (ChangeFailure is null) Attributes = Attributes! with { ModifiedUtc = request.ModifiedUtc };
            return Task.FromResult(new ItemModifiedTimeSetResponse(
                ItemAttributeIpcContract.CurrentVersion, request.Address, ChangeFailure is null, ChangeFailure));
        }

        public Task<ObjectVersionListResponse> ListVersionsAsync(
            ObjectVersionListRequest request, CancellationToken cancellationToken = default)
        {
            VersionRequests++;
            if (Throws is { } error) throw error;
            var second = request.ContinuationToken is not null;
            return Task.FromResult(new ObjectVersionListResponse(
                ObjectInspectorIpcContract.CurrentVersion,
                request.Address,
                second ? NextPage ?? [] : Versions,
                second || NextPage is null ? null : "page-2"));
        }

        public Task<ObjectMetadataGetResponse> GetMetadataAsync(
            ObjectMetadataGetRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ObjectMetadataGetResponse(
                ObjectInspectorIpcContract.CurrentVersion, request.Address, Metadata));

        public Task<ObjectTagsGetResponse> GetTagsAsync(
            ObjectTagsGetRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ObjectTagsGetResponse(
                ObjectInspectorIpcContract.CurrentVersion, request.Address, Tags, TagsFailure));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
