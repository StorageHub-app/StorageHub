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

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Choosing a sync location's folder: the picker, and the Browse buttons that open it.
/// </summary>
public sealed class SyncLocationPickerTests
{
    private static readonly ConnectionChoice Studio = new(Guid.NewGuid(), "Studio Assets", true, "Local / UNC");

    [AvaloniaFact]
    public async Task BrowsingSetsTheRootAndSaysSo()
    {
        (ConnectionChoice Connection, string Root, string Location)? asked = null;
        var model = new SyncProfileEditorModel(pickLocation: (connection, root, location) =>
        {
            asked = (connection, root, location);
            return Task.FromResult<string?>("photos/2019");
        })
        {
            LocationA = Studio,
            LocationARoot = " photos "
        };

        await model.BrowseAsync(isA: true);

        Assert.Equal((Studio, "photos", Ui.Sync.LocationA), asked);
        Assert.Equal("photos/2019", model.LocationARoot);
        Assert.Equal(Ui.Format(Ui.Sync.PickerFolderSelectedFormat, Ui.Sync.LocationA, "photos/2019"), model.Status.Text);
        Assert.True(model.Status.IsSuccess);
    }

    [AvaloniaFact]
    public async Task ChoosingTheRootSaysWhichConnection()
    {
        var model = new SyncProfileEditorModel(pickLocation: (_, _, _) => Task.FromResult<string?>(string.Empty))
        {
            LocationB = Studio,
            LocationBRoot = "old"
        };

        await model.BrowseAsync(isA: false);

        Assert.Equal(string.Empty, model.LocationBRoot);
        Assert.Equal(Ui.Format(Ui.Sync.PickerUsesRootFormat, Ui.Sync.LocationB, "Studio Assets"), model.Status.Text);
    }

    [AvaloniaFact]
    public async Task DismissingThePickerChangesNothing()
    {
        var model = new SyncProfileEditorModel(pickLocation: (_, _, _) => Task.FromResult<string?>(null))
        {
            LocationA = Studio,
            LocationARoot = "photos"
        };
        var before = model.Status;

        await model.BrowseAsync(isA: true);

        Assert.Equal("photos", model.LocationARoot);
        Assert.Equal(before, model.Status);
    }

    /// <summary>Without a connection there are no folders to show, and the editor says which is missing.</summary>
    [AvaloniaFact]
    public async Task BrowsingWithoutAConnectionAsksForOne()
    {
        var asked = false;
        var model = new SyncProfileEditorModel(pickLocation: (_, _, _) =>
        {
            asked = true;
            return Task.FromResult<string?>("x");
        });

        await model.BrowseAsync(isA: true);

        Assert.False(asked);
        Assert.Equal(Ui.Format(Ui.Sync.SelectConnectionForLocationFormat, Ui.Sync.LocationA), model.Status.Text);
        Assert.True(model.Status.IsDanger);
    }

    [AvaloniaFact]
    public void WithoutAPickerBrowseIsDim()
    {
        var model = SyncProfileEditorModel.Create();

        Assert.False(model.BrowseLocationACommand.CanExecute(null));
        Assert.False(model.BrowseLocationBCommand.CanExecute(null));
    }

    /// <summary>Select is dim until a folder is listed, then chooses the folder being shown.</summary>
    [AvaloniaFact]
    public async Task SelectChoosesTheFolderBeingShown()
    {
        var picker = Picker(new Folders(), "photos");
        Assert.False(picker.SelectCommand.CanExecute(null));

        await picker.StartAsync();
        Assert.Equal("photos", picker.Address);
        Assert.Equal(["2019", "2020"], picker.Folders.Select(static folder => folder.Name));

        // Highlighting a folder does not choose it; opening it does, and then selecting.
        picker.SelectedFolder = picker.Folders[0];
        picker.SelectCommand.Execute(null);
        Assert.Equal("photos", picker.Result);
    }

    [AvaloniaFact]
    public async Task OpeningAFolderListsIt()
    {
        var picker = Picker(new Folders(), string.Empty);
        await picker.StartAsync();
        picker.SelectedFolder = picker.Folders.Single(static folder => folder.Name == "photos");

        picker.OpenCommand.Execute(null);
        await WaitUntilIdle(picker);

        Assert.Equal("photos", picker.Address);
        Assert.Equal(["2019", "2020"], picker.Folders.Select(static folder => folder.Name));
        Assert.True(picker.UpCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task ARefusalIsShownAsDanger()
    {
        var picker = Picker(new Folders(), "missing");

        await picker.StartAsync();

        Assert.True(picker.Status.IsDanger);
        Assert.False(picker.SelectCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void CancelIsNotAChoice()
    {
        var picker = Picker(new Folders(), string.Empty);
        var closed = false;
        picker.Closed += (_, _) => closed = true;

        picker.CancelCommand.Execute(null);

        Assert.True(closed);
        Assert.Null(picker.Result);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ThePickerCanBePhotographed(bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));

        var picker = Picker(new Folders(), string.Empty);
        await picker.StartAsync();
        picker.SelectedFolder = picker.Folders[1];
        var window = new SyncLocationPickerWindow { DataContext = picker };
        window.Show();
        window.Measure(new Size(720, 540));
        window.Arrange(new Rect(0, 0, 720, 540));
        window.UpdateLayout();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, $"sync-location-picker-{(dark ? "dark" : "light")}.png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }

    private static SyncLocationPickerModel Picker(Folders agent, string initialPath) =>
        new(new SyncLocationBrowser(() => agent, Studio.ConnectionId), Studio, initialPath, Ui.Sync.LocationA);

    /// <summary>A command starts its listing without waiting; this waits for it to finish.</summary>
    private static async Task WaitUntilIdle(SyncLocationPickerModel picker)
    {
        for (var attempt = 0; picker.IsBusy && attempt < 100; attempt++) await Task.Delay(10);
    }

    /// <summary>A root with three folders, one of which has two more; anything else is not found.</summary>
    private sealed class Folders : IRemoteStorageAgentClient
    {
        private static readonly Dictionary<string, string[]> Tree = new()
        {
            [""] = ["archive", "photos", "renders"],
            ["photos"] = ["photos/2019", "photos/2020"]
        };

        public Task<StorageListPageResponse> ListStorageAsync(
            StorageListPageRequest request,
            CancellationToken cancellationToken = default)
        {
            if (!Tree.TryGetValue(request.RelativePath, out var paths))
            {
                return Task.FromResult(new StorageListPageResponse(
                    StorageIpcContract.CurrentVersion, request.ConnectionId, request.RelativePath, [], null,
                    new StorageIpcFailure("gone", StorageIpcFailureCategory.NotFound, "not found", false)));
            }

            var entries = paths
                .Select(static path => new StorageListItem(
                    path[(path.LastIndexOf('/') + 1)..], path, StorageItemKind.Directory, null, null, null, IsContainer: true))
                .ToArray();
            return Task.FromResult(new StorageListPageResponse(
                StorageIpcContract.CurrentVersion, request.ConnectionId, request.RelativePath, entries, null));
        }

        public Task<ConnectionListResponse> ListConnectionsAsync(
            ConnectionListRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ConnectionTestResponse> TestConnectionAsync(
            ConnectionTestRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
