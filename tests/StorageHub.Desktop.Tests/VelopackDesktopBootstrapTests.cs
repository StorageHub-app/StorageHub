namespace StorageHub.Desktop.Tests;

/// <summary>
/// The last of PackagedDesktopLifecycleTests still tied to this shell.
/// </summary>
/// <remarks>
/// VelopackDesktopBootstrap is the hook dispatcher, and 2.0 replaces it with MSI custom actions
/// calling the same two methods. This case goes when it does.
/// </remarks>
public sealed class VelopackDesktopBootstrapTests
{
    [Fact]
    public void VelopackBootstrapDisablesFrameworkAutoApplySoUpdaterPreferencesRemainAuthoritative()
    {
        Assert.False(VelopackDesktopBootstrap.AutoApplyOnStartup);
    }
}
