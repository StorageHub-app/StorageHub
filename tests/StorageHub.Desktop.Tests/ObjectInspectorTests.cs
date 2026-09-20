using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
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
    /// agent identified, and the entry's own path and tag. A folder, or nothing, dims the button.
    /// </remarks>
    [AvaloniaFact]
    public async Task PropertiesOpensTheInspectorOnTheOneSelectedFile()
    {
        var connection = Summary("Studio Assets");
        var agent = new RecordingAgent([connection]);
        ObjectInspectorAddress? opened = null;
        await using var pane = new BrowserPaneModel(agent, inspect: address =>
        {
            opened = address;
            return Task.CompletedTask;
        });
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
        await pane.OpenConnectionAsync(connection.ConnectionId, TestContext.Current.CancellationToken);

        Assert.False(pane.PropertiesCommand.CanExecute(null));

        pane.SelectedRows.Add(pane.Rows.Single(row => row.Name == "reports"));
        Assert.False(pane.PropertiesCommand.CanExecute(null));

        pane.SelectedRows.Clear();
        pane.SelectedRows.Add(pane.Rows.Single(row => row.Name == "render.exr"));
        Assert.True(pane.PropertiesCommand.CanExecute(null));

        await pane.InspectAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(opened);
        Assert.Equal(connection.ConnectionId, opened!.ConnectionId);
        Assert.Equal("root", opened.RootIdentity);
        Assert.Equal("render.exr", opened.RelativePath);
        Assert.Equal("etag-render.exr", opened.EntityTag);
    }

    /// <summary>The menu path, where nothing dims, answers with a sentence instead.</summary>
    [AvaloniaFact]
    public async Task InspectingAFolderSaysWhyNot()
    {
        var connection = Summary("Studio Assets");
        var agent = new RecordingAgent([connection]);
        var opened = false;
        await using var pane = new BrowserPaneModel(agent, inspect: _ =>
        {
            opened = true;
            return Task.CompletedTask;
        });
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
        await pane.OpenConnectionAsync(connection.ConnectionId, TestContext.Current.CancellationToken);
        pane.SelectedRows.Add(pane.Rows.Single(row => row.Name == "reports"));

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

    /// <summary>Photographs the inspector, for a human to look at.</summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheInspectorCanBePhotographed(bool dark)
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
                "This provider does not report object tags.", false)
        };
        await using var model = new ObjectInspectorModel(new ObjectInspectorController(agent, Address));
        await model.LoadAsync(TestContext.Current.CancellationToken);

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
            Path.Combine(directory, $"object-inspector-{(dark ? "dark" : "light")}.png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
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

        public Task<ObjectVersionListResponse> ListVersionsAsync(
            ObjectVersionListRequest request, CancellationToken cancellationToken = default)
        {
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
