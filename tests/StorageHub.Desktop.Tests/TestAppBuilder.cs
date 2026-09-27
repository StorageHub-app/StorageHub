using System.Runtime.CompilerServices;
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

    /// <summary>
    /// Sends what the tests make the desktop log to a file of their own. A test that has the agent
    /// refuse a request on purpose wrote its entry into the user's desktop.log, the file they read
    /// to find out what went wrong with the real app.
    /// </summary>
    [ModuleInitializer]
    internal static void KeepTheErrorLogOutOfTheUsersData() =>
        Framework.DesktopErrorLog.FilePath =
            Path.Combine(Path.GetTempPath(), "StorageHub.Desktop.Tests", "logs", "desktop.log");
}
