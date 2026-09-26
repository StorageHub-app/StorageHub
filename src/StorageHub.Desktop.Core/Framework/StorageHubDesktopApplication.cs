using CodeLogic.Framework.Application;
using CodeLogic.Framework.Libraries;
using StorageHub.Desktop.Localization;
using FrameworkApplicationContext = CodeLogic.Framework.Application.ApplicationContext;

namespace StorageHub.Desktop.Framework;

/// <summary>
/// The desktop shell's CodeLogic application.
/// </summary>
/// <remarks>
/// Deliberately separate from <c>StorageHub.Application.StorageHubApplication</c>: that type drives
/// the agent's runtime subsystems through an <c>IApplicationRuntimeCoordinator</c>, which the
/// desktop does not have. The desktop registers configuration and localization models and owns no
/// background work. Ported from 1.x, where it was the reason a saved language took effect at all.
/// </remarks>
internal sealed class StorageHubDesktopApplication : IApplication
{
    public ApplicationManifest Manifest { get; } = new()
    {
        Id = "storagehub-desktop",
        Name = "StorageHub Desktop",
        Version = DesktopApplicationVersion.Current,
        Description = "StorageHub desktop shell",
        Author = "StorageHub Contributors"
    };

    public Task OnConfigureAsync(FrameworkApplicationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The framework generates any missing file and loads every registered model immediately
        // after this returns, so this is the only place the shell declares what text and settings
        // it has. DesktopConfigStore.Preflight has already made the settings files readable by
        // this point, which is what keeps a hand-edited value from throwing out of here.
        DesktopConfigStore.Register(context.Configuration);

        context.Localization.Register<ShellStrings>();
        context.Localization.Register<CommandStrings>();
        context.Localization.Register<DialogStrings>();
        context.Localization.Register<OverviewStrings>();
        context.Localization.Register<ConnectionStrings>();
        context.Localization.Register<TransferStrings>();
        context.Localization.Register<SettingsStrings>();
        context.Localization.Register<SyncStrings>();
        context.Localization.Register<ScheduleStrings>();
        context.Localization.Register<PaneStrings>();
        context.Localization.Register<ConnectionEditorStrings>();
        context.Localization.Register<ProviderStrings>();
        context.Localization.Register<KeyStoreStrings>();
        context.Localization.Register<InspectorStrings>();
        context.Localization.Register<SettingsTransferStrings>();
        context.Localization.Register<UpdateStrings>();
        context.Localization.Register<ValidationStrings>();
        return Task.CompletedTask;
    }

    public Task OnInitializeAsync(FrameworkApplicationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.CompletedTask;
    }

    public Task OnStartAsync(FrameworkApplicationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.CompletedTask;
    }

    public Task OnStopAsync() => Task.CompletedTask;

    public Task<HealthStatus> HealthCheckAsync() =>
        Task.FromResult(HealthStatus.Healthy("StorageHub desktop shell is running."));
}
