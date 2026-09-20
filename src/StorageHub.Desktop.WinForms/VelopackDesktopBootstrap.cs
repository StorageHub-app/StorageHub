using Velopack;

namespace StorageHub.Desktop;

internal static class VelopackDesktopBootstrap
{
    /// <summary>
    /// The identity Windows files this application under for shortcuts, taskbar pinning and
    /// toast notifications. Changed with the move to the StorageHub-app organization, which
    /// orphans the pins and notification history of any copy installed before that -- acceptable
    /// only because no release from before the move is still offered.
    /// </summary>
    internal const string AppUserModelId = "StorageHubApp.StorageHub.Desktop";
    internal const bool AutoApplyOnStartup = false;

    public static VelopackApp Build(string[] arguments) =>
        VelopackApp.Build()
            .SetArgs(arguments)
            .SetAppUserModelId(AppUserModelId)
            .SetAutoApplyOnStartup(AutoApplyOnStartup)
            .OnAfterInstallFastCallback(static _ => CreateHooks().AfterInstall())
            .OnAfterUpdateFastCallback(static _ => CreateHooks().AfterUpdate())
            .OnBeforeUpdateFastCallback(static _ => CreateHooks().BeforeUpdate())
            .OnBeforeUninstallFastCallback(static _ => CreateHooks().BeforeUninstall());

    private static DesktopPackageLifecycleHooks CreateHooks() =>
        new(WindowsDesktopLifecycle.Create());
}
