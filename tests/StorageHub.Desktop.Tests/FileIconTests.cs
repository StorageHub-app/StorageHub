using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Lucide.Avalonia;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Services;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The icon beside a name: the system's where there is one, a glyph where there is not.
/// </summary>
/// <remarks>1.x showed Windows shell icons; 2.0's Name column was text alone.</remarks>
public class FileIconTests
{
    [Theory]
    [InlineData("holiday.JPG", false, nameof(LucideIconKind.FileImage))]
    [InlineData("backup.tar.gz", false, nameof(LucideIconKind.FileArchive))]
    [InlineData("Program.cs", false, nameof(LucideIconKind.FileCode))]
    [InlineData("notes.md", false, nameof(LucideIconKind.FileText))]
    [InlineData("mystery.qqq", false, nameof(LucideIconKind.File))]
    [InlineData("reports", true, nameof(LucideIconKind.Folder))]
    public void TheGlyphFollowsTheKindAndTheExtension(string name, bool folder, string expected)
    {
        var item = new BrowserListItem(name, "", "", "", "", Location: "bucket/" + name, IsContainer: folder);
        Assert.Equal(Enum.Parse<LucideIconKind>(expected), FileIcons.Glyph(item));
    }

    [Fact]
    public void ADriveIsALocalRootAndGoingUpHasItsOwnGlyph()
    {
        var root = OperatingSystem.IsWindows() ? @"C:\" : "/";
        var drive = new BrowserListItem("C:", "", "", "", "", Location: root, IsContainer: true);
        var folder = new BrowserListItem("work", "", "", "", "", Location: Path.Combine(root, "work"), IsContainer: true);

        Assert.True(FileIcons.IsDrive(drive));
        Assert.Equal(LucideIconKind.HardDrive, FileIcons.Glyph(drive));
        Assert.False(FileIcons.IsDrive(folder));
        Assert.Equal(LucideIconKind.FolderUp, FileIcons.Glyph(BrowserParentNavigation.Item));
    }

    /// <summary>A remote row is never looked up by path: it is not a path on this computer.</summary>
    [Fact]
    public void OnlyAFullyQualifiedLocationIsLocal()
    {
        Assert.False(FileIcons.IsLocal(new BrowserListItem("a.txt", "", "", "", "", Location: "photos/a.txt")));
        Assert.True(FileIcons.IsLocal(new BrowserListItem("a.txt", "", "", "", "",
            Location: Path.Combine(Path.GetTempPath(), "a.txt"))));
    }

    [AvaloniaFact]
    public void WindowsGivesAShellIconForAFileType()
    {
        if (!OperatingSystem.IsWindows()) return;

        var icon = WindowsShellIcons.For(new BrowserListItem("readme.txt", "", "", "", "", Location: "docs/readme.txt"));

        Assert.NotNull(icon);
        Assert.True(icon!.Size.Width >= 16);
        Assert.Same(icon, WindowsShellIcons.For(new BrowserListItem("other.txt", "", "", "", "", Location: "x/other.txt")));
    }

    /// <summary>A real folder, with a mix of types, for a human to look at. STORAGEHUB_SHOT_DIR keeps it.</summary>
    [AvaloniaFact]
    public async Task APaneOfFilesCanBePhotographed()
    {
        var folder = Directory.CreateTempSubdirectory("storagehub-icons-");
        try
        {
            Directory.CreateDirectory(Path.Combine(folder.FullName, "Photos"));
            Directory.CreateDirectory(Path.Combine(folder.FullName, "Projects"));
            foreach (var name in new[] { "notes.txt", "report.pdf", "holiday.jpg", "backup.zip", "Program.cs", "budget.xlsx", "song.mp3", "setup.exe" })
            {
                await File.WriteAllTextAsync(Path.Combine(folder.FullName, name), "x", TestContext.Current.CancellationToken);
            }

            await using var pane = new BrowserPaneModel(new NoConnections());
            await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
            await pane.OpenAsync(pane.Connections.Single(c => c.Id is null), TestContext.Current.CancellationToken);
            await pane.NavigateAsync(folder.FullName, TestContext.Current.CancellationToken);

            var window = new Window { Content = new BrowserPaneView { DataContext = pane }, Width = 700, Height = 420 };
            window.Show();
            window.Measure(new Size(700, 420));
            window.Arrange(new Rect(0, 0, 700, 420));
            window.UpdateLayout();
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(10, pane.Rows.Count(static row => !row.IsParentNavigation));

            var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
            if (string.IsNullOrWhiteSpace(directory)) return;
            Directory.CreateDirectory(directory);
            using var stream = File.Create(Path.Combine(directory, "pane-file-icons.png"));
            frame!.Save(stream, new PngBitmapEncoderOptions());
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private sealed class NoConnections : IRemoteStorageAgentClient
    {
        public Task<ConnectionListResponse> ListConnectionsAsync(
            ConnectionListRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionListResponse(StorageIpcContract.CurrentVersion, []));

        public Task<ConnectionTestResponse> TestConnectionAsync(
            ConnectionTestRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StorageListPageResponse> ListStorageAsync(
            StorageListPageRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
