using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Avalonia.Tests;

/// <summary>
/// The New Workspace chooser: six arrangements as pictures, and whether to stop being asked.
/// </summary>
/// <remarks>
/// The thumbnails' geometry is tested in Desktop.Core, where it can be asserted rather than looked
/// at. What is left here is that six of them reach the window, that picking one answers with that
/// preset, and that dismissing answers with nothing.
/// </remarks>
public class NewWorkspaceWindowTests
{
    [AvaloniaFact]
    public void TheChooserOffersSixArrangements()
    {
        var (window, _) = Chooser();

        var thumbnails = window.GetVisualDescendants().OfType<PresetThumbnail>().ToArray();

        Assert.Equal(6, thumbnails.Length);
        Assert.Equal(
            WorkspacePreset.All,
            thumbnails.Select(static thumbnail => thumbnail.Preset));
    }

    [AvaloniaFact]
    public void PickingAnArrangementAnswersWithItAndCloses()
    {
        var (_, model) = Chooser();
        var closed = 0;
        model.Closed += (_, _) => closed++;

        model.ChooseCommand.Execute(WorkspacePreset.All[5]);

        // Equal, not the same instance: the list is rebuilt per access because the descriptions
        // come from the language in force, so two reads give two equal records.
        Assert.Equal(WorkspacePreset.All[5], model.Chosen);
        Assert.Equal(1, closed);
    }

    /// <summary>A chooser nobody answered makes no workspace.</summary>
    [AvaloniaFact]
    public void ADismissedChooserAnswersWithNothing()
    {
        var (_, model) = Chooser();

        Assert.Null(model.Chosen);
        Assert.False(model.Remember);
    }

    /// <summary>
    /// Photographs the chooser in both appearances, for a human to look at.
    /// </summary>
    /// <remarks>
    /// The thumbnails are painted rather than composed from controls, so this is the only thing
    /// that shows whether they read as arrangements. Set STORAGEHUB_SHOT_DIR to keep the files.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheChooserCanBePhotographed(bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));

        var (window, _) = Chooser();
        window.Measure(new Size(660, 520));
        window.Arrange(new Rect(0, 0, 660, 520));
        window.UpdateLayout();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;

        Directory.CreateDirectory(directory);
        using var stream = File.Create(
            Path.Combine(directory, $"new-workspace-{(dark ? "dark" : "light")}.png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }

    private static (Window Window, NewWorkspaceModel Model) Chooser()
    {
        var model = new NewWorkspaceModel();
        var window = new NewWorkspaceWindow { DataContext = model, Width = 660, Height = 520 };
        window.Show();
        window.UpdateLayout();
        return (window, model);
    }
}
