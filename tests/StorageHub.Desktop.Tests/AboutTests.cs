using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>Help, About.</summary>
public sealed class AboutTests
{
    [AvaloniaFact]
    public void AboutNamesThisBuild()
    {
        Assert.Contains(DesktopApplicationVersion.Current, AboutModel.Body, StringComparison.Ordinal);
    }

    /// <summary>The project address is the one the updater trusts, not a copy of it.</summary>
    [AvaloniaFact]
    public async Task TheLinkOpensTheProject()
    {
        Uri? opened = null;
        var about = new AboutModel(uri => { opened = uri; return Task.FromResult(true); });

        await about.OpenProjectAsync();

        Assert.Equal(new Uri(StorageHubLinks.Project), opened);
    }

    /// <summary>With no browser to open, the address is shown rather than nothing happening.</summary>
    [AvaloniaFact]
    public async Task WithNoBrowserTheAddressIsShown()
    {
        var dialogs = new RecordingDialogs();
        var about = new AboutModel(_ => Task.FromResult(false), dialogs);

        await about.OpenProjectAsync();

        Assert.Equal(StorageHubLinks.Project, Assert.Single(dialogs.Shown).Message);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void AboutCanBePhotographed(bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));

        var window = new AboutWindow { DataContext = new AboutModel(_ => Task.FromResult(true)) };
        window.Show();
        window.Measure(new Size(500, 240));
        window.Arrange(new Rect(0, 0, 500, 240));
        window.UpdateLayout();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, $"about-{(dark ? "dark" : "light")}.png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }

    private sealed class RecordingDialogs : IDialogService
    {
        internal List<DialogRequest> Shown { get; } = [];

        public Task ShowAsync(DialogRequest request, CancellationToken cancellationToken = default)
        {
            Shown.Add(request);
            return Task.CompletedTask;
        }

        public Task<DialogChoice> ConfirmAsync(DialogRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(DialogChoice.Cancel);

        public Task<string?> PromptAsync(DialogPromptRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
