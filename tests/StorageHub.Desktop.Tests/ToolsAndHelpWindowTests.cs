using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Lucide.Avalonia;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Tools, Search, Checksums, Logs and Diagnostics, and Help, Documentation and Report issue: each
/// entry does what its name says, and the windows behind three of them.
/// </summary>
public sealed class ToolsAndHelpWindowTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"storagehub-tools-ui-{Guid.NewGuid():N}");

    public ToolsAndHelpWindowTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// Documentation opens the README, Report issue the bug form carrying this version, and Logs the
    /// log folder, made first if nothing has been logged yet.
    /// </summary>
    [AvaloniaFact]
    public void TheLinksAndTheLogFolderAreOpened()
    {
        var model = ShellPreview.CreateOnWorkspace();
        var opened = new List<Uri>();
        var folders = new List<string>();
        var logs = Path.Combine(_directory, "logs");
        model.OpenUri = address =>
        {
            opened.Add(address);
            return Task.FromResult(true);
        };
        model.OpenFolder = folder =>
        {
            folders.Add(folder);
            return Task.FromResult(true);
        };
        model.LogFolder = () => logs;

        model.Router.For(UiCommandIds.HelpDocumentation).Execute(null);
        model.Router.For(UiCommandIds.HelpReportIssue).Execute(null);
        model.Router.For(UiCommandIds.ToolsLogs).Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(StorageHubLinks.Documentation, opened[0].AbsoluteUri);
        Assert.StartsWith(StorageHubLinks.BugReport + "&version=", opened[1].AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString(DesktopApplicationVersion.Current), opened[1].AbsoluteUri, StringComparison.Ordinal);
        Assert.DoesNotContain(Environment.UserName, opened[1].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([logs], folders);
        Assert.True(Directory.Exists(logs));
    }

    /// <summary>
    /// Search lights for a pane in a folder and Checksums for a selected file in one; neither for a
    /// pane showing Connections, where there is nothing to walk or read.
    /// </summary>
    [AvaloniaFact]
    public async Task SearchAndChecksumsFollowTheActivePane()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, "notes.txt"), "x");
        var model = ShellPreview.CreateOnWorkspace();
        var pane = model.Workspaces.Select(static tab => tab.Workspace).First(static w => w is not null)!.Panes[0];
        pane.IsActive = true;

        // Their windows are App's, which hands them over as it does here.
        model.Router.Handle(UiCommandIds.ToolsSearch, static () => { });
        model.Router.Handle(UiCommandIds.ToolsChecksums, static () => { });
        Assert.False(model.Router.CanRun(UiCommandIds.ToolsSearch));
        Assert.False(model.Router.CanRun(UiCommandIds.ToolsChecksums));

        await pane.OpenAsync(
            new PaneConnection(null, Ui.Pane.ThisPc, LucideIconKind.HardDrive, PaneContentKind.ThisPc),
            TestContext.Current.CancellationToken);
        await pane.NavigateAsync(_directory, TestContext.Current.CancellationToken);
        Assert.True(model.Router.CanRun(UiCommandIds.ToolsSearch));
        Assert.False(model.Router.CanRun(UiCommandIds.ToolsChecksums));

        await pane.RevealAsync(_directory, "notes.txt", TestContext.Current.CancellationToken);
        Assert.Equal("notes.txt", pane.Selected?.Name);
        Assert.True(model.Router.CanRun(UiCommandIds.ToolsChecksums));
        Assert.Equal(["notes.txt"], model.ChecksumTarget()!.Items.Select(static item => item.Name));
    }

    /// <summary>Search fills the table with what it found and says how far it got; a new search replaces it.</summary>
    [AvaloniaFact]
    public async Task SearchListsTheMatchesAndGoesToTheChosenOne()
    {
        FileSearchHit? revealed = null;
        string? asked = null;
        var model = new FileSearchModel(
            (pattern, _, _) =>
            {
                asked = pattern;
                return Task.FromResult(new FileSearchResult(Hits(), 120, Truncated: false, Failure: null));
            },
            "/home/ana/Photos",
            hit =>
            {
                revealed = hit;
                return Task.CompletedTask;
            });
        Assert.False(model.SearchCommand.CanExecute(null));

        model.Query = "*.jpg";
        await model.SearchAsync();
        model.SelectedRow = model.Rows[1];
        await model.RevealAsync();

        Assert.Equal("*.jpg", asked);
        Assert.Equal(["beach.jpg", "dunes.jpg"], model.Rows.Select(static row => row.Name));
        Assert.Equal("dunes.jpg", revealed?.Name);
        Assert.False(model.IsSearching);
        Assert.Contains("2", model.Status.Text, StringComparison.Ordinal);
    }

    /// <summary>Stop leaves the table empty and says the search was stopped.</summary>
    [AvaloniaFact]
    public async Task StoppingASearchSaysSo()
    {
        var model = new FileSearchModel(
            async (_, _, cancellationToken) =>
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return new FileSearchResult([], 0, false, null);
            },
            "/",
            reveal: null)
        { Query = "x" };

        var running = model.SearchAsync();
        Assert.True(model.StopCommand.CanExecute(null));
        model.Stop();
        await running;

        Assert.Empty(model.Rows);
        Assert.Equal(Ui.Shell.SearchStopped, model.Status.Text);
        Assert.False(model.RevealCommand.CanExecute(null));
    }

    /// <summary>
    /// Each file's checksum is shown with where it came from, the files are compared with one
    /// another, and once one is pasted in, with that instead.
    /// </summary>
    [AvaloniaFact]
    public async Task ChecksumsAreComparedWithEachOtherOrWithTheOneExpected()
    {
        var clipboard = new Clipboard();
        var model = Checksums(clipboard);

        await model.ComputeAsync();

        Assert.Equal(Ui.Format(Ui.Shell.ChecksumSameAsFormat, "copy.iso"), model.Rows[0].Match);
        Assert.Equal(Ui.Shell.ChecksumUnique, model.Rows[2].Match);
        Assert.Equal(Ui.Shell.ChecksumFromServer, model.Rows[0].Source);
        Assert.Equal(
            Ui.Format(Ui.Shell.ChecksumDistinctFormat, 2, 3) + " " + Ui.Format(Ui.Shell.ChecksumSkippedFormat, 1),
            model.Status.Text);

        model.Expected = "sha256:" + new string('b', 64);
        Assert.True(model.Rows[2].Matches);
        Assert.True(model.Rows[0].Differs);
        Assert.Equal(MetricTone.Warning, model.Status.Tone);

        await model.CopyAsync();
        Assert.Equal(
            $"{new string('a', 64)}  disk.iso\n{new string('a', 64)}  copy.iso\n{new string('b', 64)}  other.iso\n",
            clipboard.Text);
    }

    /// <summary>The report copies as it reads, the bundle goes where it was saved, and Open log folder opens it.</summary>
    [AvaloniaFact]
    public async Task DiagnosticsCopiesSavesAndOpens()
    {
        var clipboard = new Clipboard();
        var saved = new List<string>();
        var opened = new List<string>();
        var model = new DiagnosticsModel(
            Facts(),
            "/home/ana/.local/share/StorageHub/Desktop/logs",
            clipboard,
            new SavePicker("/home/ana/Desktop/support.zip"),
            folder =>
            {
                opened.Add(folder);
                return Task.FromResult(false);
            },
            (path, _) =>
            {
                saved.Add(path);
                return Task.FromResult<IReadOnlyList<string>>(["environment/summary.json", "health/latest.json"]);
            });

        await model.CopyAsync();
        Assert.Equal(model.Report, clipboard.Text);
        Assert.Contains("2.0.0", model.Report, StringComparison.Ordinal);

        await model.SaveBundleAsync();
        Assert.Equal(["/home/ana/Desktop/support.zip"], saved);
        Assert.Equal(Ui.Format(Ui.Shell.DiagnosticsSavedFormat, "support.zip", 2), model.Status.Text);

        // Nothing opened it, so the folder is named instead.
        await model.OpenLogsAsync();
        Assert.Equal([model.LogFolder], opened);
        Assert.Equal(Ui.Format(Ui.Shell.LogsFolderIsFormat, model.LogFolder), model.Status.Text);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheSearchWindowCanBePhotographed(bool dark)
    {
        var model = new FileSearchModel(
            (_, _, _) => Task.FromResult(new FileSearchResult(Hits(), 4_812, Truncated: false, Failure: null)),
            "/home/ana/Photos",
            static _ => Task.CompletedTask)
        { Query = "*.jpg" };
        await model.SearchAsync();
        model.SelectedRow = model.Rows[0];
        Photograph(new FileSearchWindow { DataContext = model }, "tools-search", dark);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheChecksumsWindowCanBePhotographed(bool dark)
    {
        var model = Checksums(new Clipboard());
        await model.ComputeAsync();
        Photograph(new ChecksumsWindow { DataContext = model }, "tools-checksums", dark);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheDiagnosticsWindowCanBePhotographed(bool dark)
    {
        var model = new DiagnosticsModel(
            Facts(),
            "/home/ana/.local/share/StorageHub/Desktop/logs",
            new Clipboard(),
            new SavePicker(null),
            static _ => Task.FromResult(true),
            static (_, _) => Task.FromResult<IReadOnlyList<string>>([]));
        Photograph(new DiagnosticsWindow { DataContext = model }, "tools-diagnostics", dark);
    }

    private static void Photograph(Window window, string name, bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));
        window.Show();
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        window.UpdateLayout();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        window.Close();
        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, $"{name}-{(dark ? "dark" : "light")}.png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }

    private static FileSearchHit[] Hits() =>
    [
        new("beach.jpg", "/home/ana/Photos/2026/Summer", StorageItemKind.File, 3_481_204, Noon),
        new("dunes.jpg", "/home/ana/Photos/2025", StorageItemKind.File, 2_904_117, Noon.AddDays(-300))
    ];

    /// <summary>Three files, two alike from the server and one computed here.</summary>
    private static ChecksumsModel Checksums(Clipboard clipboard)
    {
        PaneTransferItem File(string name) =>
            PaneTransferItem.Create(name, "isos/" + name, StorageItemKind.File, 4_294_967_296).Value;

        return new ChecksumsModel(
            [File("disk.iso"), File("copy.iso"), File("other.iso")],
            skipped: 1,
            (item, _) => Task.FromResult(item.Name == "other.iso"
                ? new ChecksumOutcome(new string('b', 64), ObjectChecksumSource.Computed, item.Length ?? 0, null)
                : new ChecksumOutcome(new string('a', 64), ObjectChecksumSource.Server, 0, null)),
            clipboard);
    }

    private static DiagnosticsFacts Facts() => new(
        "2.0.0",
        "Ubuntu 24.04.3 LTS",
        "X64",
        ".NET 10.0.1",
        "da-DK",
        "da-DK",
        AgentConnectionState.Connected,
        "Ready",
        1,
        0,
        Noon,
        new Dictionary<string, int> { ["Transferring"] = 1, ["Pending"] = 3 },
        Noon);

    private sealed class Clipboard : IClipboardService
    {
        internal string? Text { get; private set; }

        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            Text = text;
            return Task.CompletedTask;
        }

        public Task<string?> GetTextAsync(CancellationToken cancellationToken = default) => Task.FromResult(Text);
    }

    private sealed class SavePicker(string? path) : IFilePickerService
    {
        public Task<string?> PickFileAsync(FilePickerRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>> PickFilesAsync(FilePickerRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> PickFolderAsync(FilePickerRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<string?> SaveFileAsync(FilePickerRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(path);
    }
}
