using Avalonia;
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

/// <summary>Clean Up Leftover Files: listed with sizes, removed only once confirmed, never while in use.</summary>
public sealed class StagingCleanupWindowTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [AvaloniaFact]
    public async Task WhatCanGoIsListedFirstAndSummed()
    {
        var model = Model(Sample(), new Dialogs(DialogChoice.Yes), out _);
        await model.SearchAsync();

        Assert.Equal(4, model.Rows.Count);
        Assert.True(model.Rows[^1].InUse);
        Assert.Equal(".storagehub-7f3a.partial", model.Rows[0].Name);
        Assert.Equal(
            Ui.Format(Ui.Settings.CleanupFoundFormat, 4, UiFormatting.FormatBytes(Sample().Sum(static item => item.Size)), 3),
            model.Status.Text);
        Assert.False(model.RemoveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task OnlyTheConfirmedSelectionThatIsNotInUseIsRemoved()
    {
        var dialogs = new Dialogs(DialogChoice.Yes);
        var model = Model(Sample(), dialogs, out var removed);
        await model.SearchAsync();
        model.SelectedRows.Add(model.Rows[0]);
        model.SelectedRows.Add(model.Rows[^1]);

        await model.RemoveAsync();

        Assert.Equal(Ui.Settings.CleanupConfirmDetail, dialogs.Asked!.Detail);
        Assert.Equal([".storagehub-7f3a.partial"], removed.Select(static item => item.Name));
        Assert.Equal(3, model.Rows.Count);
        Assert.Equal(MetricTone.Success, model.Status.Tone);
    }

    [AvaloniaFact]
    public async Task NoMeansNothingIsRemoved()
    {
        var model = Model(Sample(), new Dialogs(DialogChoice.No), out var removed);
        await model.SearchAsync();
        model.SelectedRows.Add(model.Rows[0]);

        await model.RemoveAsync();

        Assert.Empty(removed);
        Assert.Equal(4, model.Rows.Count);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheWindowCanBePhotographed(bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));

        var model = Model(Sample(), new Dialogs(DialogChoice.No), out _);
        await model.SearchAsync();
        model.SelectedRows.Add(model.Rows[0]);
        var window = new StagingCleanupWindow { DataContext = model };
        window.Show();
        window.Measure(new Size(960, 540));
        window.Arrange(new Rect(0, 0, 960, 540));
        window.UpdateLayout();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        window.Close();
        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, $"staging-cleanup-{(dark ? "dark" : "light")}.png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }

    private static LeftoverItem[] Sample() =>
    [
        new(LeftoverKind.ResumePartial, ".storagehub-7f3a.partial", OperatingSystem.IsWindows() ? @"C:\Users\ana\Videos" : "/home/ana/Videos",
            734_003_200, Noon, InUse: false, new TransferQueueAddress(Guid.NewGuid(), "root", ".storagehub-7f3a.partial")),
        new(LeftoverKind.TransferStaging, ".storagehub-0f8fad5bd9cb469fa16570867728950e.staging", "/backups/2026",
            12_582_912, Noon, InUse: false, new TransferQueueAddress(Guid.NewGuid(), "root", "backups/2026/x.staging")),
        new(LeftoverKind.EditSession, "budget.xlsx", "/tmp/storagehub/edit-sessions/4c1e", 48_128, Noon, InUse: false,
            LocalDirectory: "/tmp/storagehub/edit-sessions/4c1e"),
        new(LeftoverKind.ResumePartial, ".storagehub-91bd.partial", "/uploads", 2_097_152, Noon, InUse: true,
            new TransferQueueAddress(Guid.NewGuid(), "root", "uploads/.storagehub-91bd.partial"))
    ];

    private static StagingCleanupModel Model(LeftoverItem[] items, IDialogService dialogs, out List<LeftoverItem> removed)
    {
        var taken = new List<LeftoverItem>();
        removed = taken;
        return new StagingCleanupModel(
            (_, _) => Task.FromResult(new LeftoverScan(items, 0, Truncated: false, SyncRunning: false, AgentFailure: null)),
            (chosen, _) =>
            {
                taken.AddRange(chosen);
                return Task.FromResult(new LeftoverRemoval(chosen.Count, chosen.Sum(static item => item.Size), []));
            },
            dialogs);
    }

    private sealed class Dialogs(DialogChoice answer) : IDialogService
    {
        public DialogRequest? Asked { get; private set; }

        public Task ShowAsync(DialogRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<DialogChoice> ConfirmAsync(DialogRequest request, CancellationToken cancellationToken = default)
        {
            Asked = request;
            return Task.FromResult(answer);
        }

        public Task<string?> PromptAsync(DialogPromptRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
