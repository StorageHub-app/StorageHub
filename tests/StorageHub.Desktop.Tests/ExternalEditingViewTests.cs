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
/// External editing as the shell offers it: from the pane, the warning, and the Editing settings.
/// </summary>
/// <remarks>
/// The download, the private copy and the upload are pinned in ExternalEditingTests. These are the
/// ways in: what a pane offers to edit, what opening a file does, and where the editor is chosen.
/// </remarks>
public sealed class ExternalEditingViewTests
{
    /// <summary>
    /// One file on a saved connection can be edited, with the address the inspector would use.
    /// </summary>
    [AvaloniaFact]
    public async Task EditOpensTheOneSelectedFile()
    {
        var (pane, edits) = await PaneAsync();
        await using var _ = pane;

        Assert.False(pane.EditCommand.CanExecute(null));
        pane.SelectedRows.Add(pane.Rows.Single(row => row.Name == "reports"));
        Assert.False(pane.EditCommand.CanExecute(null));

        pane.SelectedRows.Clear();
        pane.SelectedRows.Add(pane.Rows.Single(row => row.Name == "render.exr"));
        Assert.True(pane.EditCommand.CanExecute(null));

        await pane.EditAsync(TestContext.Current.CancellationToken);

        var (address, name) = Assert.Single(edits);
        Assert.Equal("render.exr", name);
        Assert.Equal("render.exr", address.RelativePath);
        Assert.Equal("etag-render.exr", address.EntityTag);
    }

    /// <summary>Opening a file -- a double-click or Enter -- edits it, as it did in 1.x.</summary>
    [AvaloniaFact]
    public async Task OpeningAFileEditsIt()
    {
        var (pane, edits) = await PaneAsync();
        await using var _ = pane;
        var file = pane.Rows.Single(row => row.Name == "render.exr");
        pane.Selected = file;
        pane.SelectedRows.Add(file);

        Assert.True(pane.OpenCommand.CanExecute(null));
        await pane.OpenSelectedAsync(TestContext.Current.CancellationToken);

        Assert.Single(edits);
    }

    /// <summary>Opening a folder still enters it; it never goes to the editor.</summary>
    [AvaloniaFact]
    public async Task OpeningAFolderEntersIt()
    {
        var (pane, edits) = await PaneAsync();
        await using var _ = pane;
        var folder = pane.Rows.Single(row => row.Name == "reports");
        pane.Selected = folder;
        pane.SelectedRows.Add(folder);

        await pane.OpenSelectedAsync(TestContext.Current.CancellationToken);

        Assert.Empty(edits);
    }

    /// <summary>Asked from somewhere that dims nothing, a folder is refused in edit's own words.</summary>
    [AvaloniaFact]
    public async Task EditingAFolderSaysWhyNot()
    {
        var (pane, edits) = await PaneAsync();
        await using var _ = pane;
        pane.SelectedRows.Add(pane.Rows.Single(row => row.Name == "reports"));

        await pane.EditAsync(TestContext.Current.CancellationToken);

        Assert.Empty(edits);
        Assert.Equal(Ui.Shell.ExternalEditRequiresOneFile, pane.Status);
    }

    /// <summary>
    /// Ticking "don't show again" and cancelling does not silence the warning.
    /// </summary>
    /// <remarks>
    /// Stopping the warning only means something for an edit that went ahead; otherwise a warning
    /// nobody acted on would never be seen again.
    /// </remarks>
    [AvaloniaFact]
    public void OnlyGoingAheadCanStopTheWarning()
    {
        var cancelled = new UnsafeEditWarningModel("readme.txt") { DontShowAgain = true };
        cancelled.CancelCommand.Execute(null);
        Assert.Equal(new UnsafeExternalEditDecision(false, false), cancelled.Decision);

        var continued = new UnsafeEditWarningModel("readme.txt") { DontShowAgain = true };
        continued.ContinueCommand.Execute(null);
        Assert.Equal(new UnsafeExternalEditDecision(true, true), continued.Decision);

        Assert.Contains("readme.txt", continued.Message, StringComparison.Ordinal);
    }

    /// <summary>Editing is a page in Settings, with the editor, the limit, and the warning.</summary>
    [AvaloniaFact]
    public void SettingsHasAnEditingPage()
    {
        var settings = Settings(out _);
        var editing = settings.Pages.Single(page => page.Title == Ui.Settings.CategoryEditing);

        Assert.Equal(
            ["external-editor", "maximum-editable-kib", "warn-unsafe-edit"],
            editing.Rows.Select(row => row.Key));
        Assert.True(editing.Rows[0].IsPath);
    }

    /// <summary>
    /// A path that is not a full one says so, and is not what Apply would write.
    /// </summary>
    [AvaloniaFact]
    public void AnEditorPathThatIsNotFullSaysSo()
    {
        var settings = Settings(out _);
        var editor = Editor(settings);

        editor.Text = "code";

        Assert.True(editor.HasProblem);
        Assert.Equal(Ui.Settings.EditorPathMustBeFull, editor.Problem);
        Assert.Null(settings.Working.ExternalEditorPath);

        var full = OperatingSystem.IsWindows() ? @"C:\Tools\editor.exe" : "/usr/bin/editor";
        editor.Text = full;

        Assert.False(editor.HasProblem);
        Assert.Equal(full, settings.Working.ExternalEditorPath);
    }

    /// <summary>Browse fills the path with what was chosen, and dismissing it changes nothing.</summary>
    [AvaloniaFact]
    public async Task BrowseFillsThePath()
    {
        var chosen = OperatingSystem.IsWindows() ? @"C:\Tools\editor.exe" : "/usr/bin/editor";
        var settings = Settings(out var picker, chosen);
        var editor = Editor(settings);

        Assert.True(editor.BrowseCommand.CanExecute(null));
        editor.BrowseCommand.Execute(null);
        await Task.Yield();

        Assert.Equal(chosen, editor.Text);
        Assert.Equal(Ui.Settings.ChooseEditorTitle, Assert.Single(picker.Requests).Title);
    }

    /// <summary>
    /// Photographs the Editing page and the warning, in both appearances, for a human to look at.
    /// </summary>
    /// <remarks>
    /// The page has the first path row Settings has drawn, with a sentence under it when the path
    /// will not be kept. Set STORAGEHUB_SHOT_DIR to keep the files.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheEditingScreensCanBePhotographed(bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));
        var appearance = dark ? "dark" : "light";

        var settings = Settings(out _);
        Editor(settings).Text = "code";
        var window = new SettingsWindow { DataContext = settings };
        window.Show();
        settings.SelectedPage = settings.Pages.ToList().FindIndex(page => page.Title == Ui.Settings.CategoryEditing);
        Photograph(window, 880, 620, $"settings-editing-{appearance}");

        var warning = new UnsafeEditWarningWindow { DataContext = new UnsafeEditWarningModel("quarterly-report.xlsx") };
        warning.Show();
        Photograph(warning, 520, 260, $"unsafe-edit-warning-{appearance}");
    }

    private static void Photograph(Window window, double width, double height, string name)
    {
        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));
        window.UpdateLayout();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, $"{name}.png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }

    private static SettingsRowModel Editor(SettingsModel settings) =>
        settings.Pages.SelectMany(page => page.Rows).Single(row => row.Key == "external-editor");

    private static SettingsModel Settings(out StubFilePicker picker, string? chosen = null)
    {
        var stored = DesktopUpdatePreferences.Defaults;
        picker = new StubFilePicker { OpenPath = chosen };
        return new SettingsModel(() => stored, saved => stored = saved, files: picker);
    }

    private static async Task<(BrowserPaneModel Pane, List<(ObjectInspectorAddress Address, string Name)> Edits)> PaneAsync()
    {
        var connection = Summary("Studio Assets");
        var edits = new List<(ObjectInspectorAddress, string)>();
        var pane = new BrowserPaneModel(new RecordingAgent([connection]), edit: (address, name, _) =>
        {
            edits.Add((address, name));
            return Task.CompletedTask;
        });
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
        await pane.OpenConnectionAsync(connection.ConnectionId, TestContext.Current.CancellationToken);
        return (pane, edits);
    }
}
