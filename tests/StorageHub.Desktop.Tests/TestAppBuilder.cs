using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(StorageHub.Desktop.Tests.TestAppBuilder))]

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Points the headless runner at the real application.
/// </summary>
/// <remarks>
/// UseHeadless replaces the windowing backend and nothing above it, so styles, theme variants,
/// layout and binding are all the shipped code. UseHeadlessDrawing is off so Skia renders for real,
/// which is what lets a test photograph the shell on a machine with no display.
/// </remarks>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<StorageHub.Desktop.App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
