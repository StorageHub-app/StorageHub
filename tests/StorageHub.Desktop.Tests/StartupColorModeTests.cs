namespace StorageHub.Desktop.Tests;

/// <summary>
/// The colour mode the WinForms runtime is told to draw in, and when it is told.
/// </summary>
/// <remarks>
/// A dropdown is its own window: it takes its colours from the runtime's mode rather than from the
/// strip that owns it. So a shell whose menu bar is painted dark still opens white menus unless
/// <c>Application.SetColorMode</c> has been called -- and it has to be called before the first
/// window, because the runtime refuses it once a message loop is running.
///
/// Program.Main makes that call through SetAppearance, and for every launch where the appearance
/// was left on System it did nothing at all: the field already said System, so the "nothing
/// changed" guard returned before the runtime was told anything. It went unnoticed because the
/// profile it was developed against had Dark saved explicitly, which is a different value and so
/// took the other path.
/// </remarks>
public sealed class StartupColorModeTests
{
    [Fact]
    public void TheFirstCallTellsTheRuntimeEvenWhenNothingChanged()
    {
        var appearance = DesktopAppearanceService.Appearance;
        DesktopAppearanceService.ForgetFrameworkColorModeForTests();

        // Exactly what Program.Main does before the first window, with the setting already on its
        // default: the same value the service starts with.
        DesktopAppearanceService.SetAppearance(appearance);

        Assert.True(
            DesktopAppearanceService.FrameworkColorModeApplied,
            "Startup left the runtime without a colour mode, so every dropdown opens in the system's.");
    }

    [Fact]
    public void AfterThatAnUnchangedAppearanceIsStillAnEarlyReturn()
    {
        DesktopAppearanceService.SetAppearance(DesktopAppearanceService.Appearance);
        var changes = 0;
        void Count(object? sender, EventArgs e) => changes++;

        DesktopAppearanceService.AppearanceChanged += Count;
        try
        {
            DesktopAppearanceService.SetAppearance(DesktopAppearanceService.Appearance);
            Assert.Equal(0, changes);
        }
        finally
        {
            DesktopAppearanceService.AppearanceChanged -= Count;
        }
    }
}
