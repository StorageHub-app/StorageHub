using Avalonia;

namespace StorageHub.Desktop;

public static class Program
{
    [System.STAThread]
    public static int Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// Public and static so the headless test host builds the same application the executable does.
    /// </summary>
    /// <remarks>
    /// UsePlatformDetect chooses X11 on Linux, which is Avalonia's supported default; the native
    /// Wayland backend graduated from preview in 12.1 but is opt-in and still marked experimental,
    /// so it is a follow-up rather than the shipping choice.
    /// </remarks>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
