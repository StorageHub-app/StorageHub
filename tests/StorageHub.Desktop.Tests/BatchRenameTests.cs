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

/// <summary>Batch rename from a pane: offered for several items, applied one at a time.</summary>
public sealed class BatchRenameTests
{
    [AvaloniaFact]
    public async Task BatchRenameIsOfferedForSeveralItems()
    {
        var (pane, _) = await PaneAsync(_ => null);
        await using var _1 = pane;

        pane.SelectedRows.Add(pane.Rows.Single(row => row.Name == "reports"));
        Assert.False(pane.BatchRenameCommand.CanExecute(null));

        pane.SelectedRows.Add(pane.Rows.Single(row => row.Name == "render.exr"));
        Assert.True(pane.BatchRenameCommand.CanExecute(null));
    }

    /// <summary>What the preview showed is what is renamed, and the pane says how many.</summary>
    [AvaloniaFact]
    public async Task ThePreviewedRenamesAreApplied()
    {
        var (pane, agent) = await PaneAsync(Replace("re", "old-re"));
        await using var _1 = pane;
        SelectBoth(pane);

        await pane.BatchRenameAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [("reports", "old-reports"), ("render.exr", "old-render.exr")],
            agent.Renames.Select(rename => (rename.Item1, rename.Item2)));
        Assert.Equal(Ui.Format(Ui.Shell.RenamedItemsFormat, 2), pane.Status);
    }

    /// <summary>A provider's refusal stops the batch there and says how far it got.</summary>
    [AvaloniaFact]
    public async Task ARefusalStopsTheBatchAndSaysWhere()
    {
        var (pane, agent) = await PaneAsync(Replace("re", "old-re"));
        await using var _1 = pane;
        SelectBoth(pane);
        agent.Failure = new StorageIpcFailure("taken", StorageIpcFailureCategory.Conflict, "That name is taken.", false);

        await pane.BatchRenameAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Ui.Format(Ui.Shell.RenamedThenStoppedFormat, 0, "reports", "That name is taken."), pane.Status);
    }

    [AvaloniaFact]
    public async Task DismissingThePreviewRenamesNothing()
    {
        var (pane, agent) = await PaneAsync(_ => null);
        await using var _1 = pane;
        SelectBoth(pane);

        await pane.BatchRenameAsync(TestContext.Current.CancellationToken);

        Assert.Empty(agent.Renames);
    }

    /// <summary>The preview follows what is typed, and Rename is dim with the reason until it can go.</summary>
    [AvaloniaFact]
    public void ThePreviewFollowsTheFields()
    {
        var model = new BatchRenameModel(["IMG_1.jpg", "IMG_2.jpg"], ["IMG_1.jpg", "IMG_2.jpg"]);
        Assert.False(model.RenameCommand.CanExecute(null));
        Assert.Equal(Ui.Validation.EnterTextToFindInTheSelected, model.Summary);

        model.Find = "img";
        model.Replace = "photo";

        Assert.True(model.RenameCommand.CanExecute(null));
        Assert.Equal(Ui.Format(Ui.Shell.BatchRenameMappingFormat, "IMG_1.jpg", "photo_1.jpg"), model.Lines[0]);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void ThePreviewCanBePhotographed(bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));

        var model = new BatchRenameModel(
            ["IMG_0412.jpg", "IMG_0413.jpg", "notes.txt", "IMG_0414.jpg"],
            ["IMG_0412.jpg", "IMG_0413.jpg", "notes.txt", "IMG_0414.jpg", "berlin_0414.jpg"])
        {
            Find = "IMG",
            Replace = "berlin"
        };
        var window = new BatchRenameWindow { DataContext = model };
        window.Show();
        window.Measure(new Size(620, 460));
        window.Arrange(new Rect(0, 0, 620, 460));
        window.UpdateLayout();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, $"batch-rename-{(dark ? "dark" : "light")}.png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }

    private static Func<IReadOnlyList<string>, IReadOnlyList<BatchRenameLine>?> Replace(string find, string replace) =>
        sources => BatchRenamePlan.Build(sources, [], find, replace).Lines;

    private static void SelectBoth(BrowserPaneModel pane)
    {
        pane.SelectedRows.Add(pane.Rows.Single(row => row.Name == "reports"));
        pane.SelectedRows.Add(pane.Rows.Single(row => row.Name == "render.exr"));
    }

    private static async Task<(BrowserPaneModel Pane, RecordingAgent Agent)> PaneAsync(
        Func<IReadOnlyList<string>, IReadOnlyList<BatchRenameLine>?> answer)
    {
        var connection = Summary("Studio Assets");
        var agent = new RecordingAgent([connection]);
        var pane = new BrowserPaneModel(
            agent,
            mutations: () => new PaneMutationController(() => agent),
            dialogs: new NoDialogs(),
            batchRename: (sources, _) => Task.FromResult(answer(sources)));
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
        await pane.OpenConnectionAsync(connection.ConnectionId, TestContext.Current.CancellationToken);
        return (pane, agent);
    }

    private sealed class NoDialogs : IDialogService
    {
        public Task ShowAsync(DialogRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<DialogChoice> ConfirmAsync(DialogRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(DialogChoice.Cancel);

        public Task<string?> PromptAsync(DialogPromptRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
