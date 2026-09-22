using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>The icon picker's three answers, and how it looks.</summary>
public sealed class IconPickerTests
{
    /// <summary>What was chosen before is what the grid opens on.</summary>
    [AvaloniaFact]
    public void TheCurrentIconIsSelected()
    {
        var model = new IconPickerModel("LAYERS", "Icon");

        Assert.Equal("layers", model.Selected?.Key);
        Assert.Equal(ConnectionIconCatalog.Choices.Count, model.Choices.Count);
    }

    [AvaloniaFact]
    public void UseAnswersTheSelectedIcon()
    {
        var model = new IconPickerModel(null, "Icon");
        var closed = false;
        model.Closed += (_, _) => closed = true;

        model.Selected = model.Choices.Single(choice => choice.Key == "server");
        model.UseCommand.Execute(null);

        Assert.True(closed);
        Assert.Equal(new IconChoice(true, "server"), model.Result);
    }

    [AvaloniaFact]
    public void UseDefaultAnswersNoIcon()
    {
        var model = new IconPickerModel("layers", "Icon");

        model.UseDefaultCommand.Execute(null);

        Assert.Equal(new IconChoice(true, null), model.Result);
    }

    /// <summary>Cancelling, or closing the window, is not a choice of the default.</summary>
    [AvaloniaFact]
    public void CancelAndClosingAreDismissals()
    {
        var model = new IconPickerModel("layers", "Icon");
        Assert.Equal(IconChoice.Dismissed, model.Result);

        model.CancelCommand.Execute(null);

        Assert.False(model.Result.Chosen);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void ThePickerCanBePhotographed(bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));

        var window = new IconPickerWindow { DataContext = new IconPickerModel("layers", "Icon for Studio Assets") };
        window.Show();
        window.UpdateLayout();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, $"icon-picker-{(dark ? "dark" : "light")}.png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }
}
