using StorageHub.Desktop.Configuration;
using StorageHub.Desktop.Framework;

namespace StorageHub.Desktop.Views;

/// <summary>
/// Builds the export and import services over the real settings file and the running agent.
/// </summary>
/// <remarks>
/// <para>
/// Shared by both dialogs, because an import writes a backup with the exporter before it applies
/// anything: the two would otherwise have to be constructed identically in two places, and a
/// backup written by a differently configured exporter is the one that is read back when somebody
/// wants their old settings returned.
/// </para>
/// <para>
/// The four agent clients are made per capture rather than held: each is a short-lived pipe
/// connection, and a dialog that sat open holding four of them would keep them open across a
/// restart of the agent it is talking to.
/// </para>
/// </remarks>
internal static class SettingsTransferServices
{
    /// <summary>The settings store the shell itself reads and writes.</summary>
    internal static DesktopConfigStore Store()
    {
        var store = new DesktopConfigStore(DesktopFrameworkPaths.Resolve().ApplicationRoot);
        store.Preflight();
        return store;
    }

    /// <summary>
    /// The clients the agent-backed sections travel on.
    /// </summary>
    /// <remarks>
    /// Always supplied. Whether the agent is actually running is discovered when one is used, and
    /// reported as a section that could not be read rather than as a dialog that will not open --
    /// the agent may also start between opening the dialog and pressing the button.
    /// </remarks>
    internal static SettingsAgentClients Clients() => new(
        new NamedPipeRemoteStorageAgentClient(),
        new NamedPipeRemoteConnectionProfileClient(),
        new NamedPipeSyncManagementAgentClient(),
        new NamedPipeScheduleManagementAgentClient());

    internal static SettingsExportService Exporter(DesktopConfigStore store) =>
        new(store, Clients());

    /// <summary>
    /// The importer, with the backup directory beside the settings file it will overwrite.
    /// </summary>
    internal static SettingsImportService Importer(DesktopConfigStore store) => new(
        store,
        Exporter(store),
        SettingsImportService.DefaultBackupDirectory(store.FilePath),
        clock: null,
        agent: Clients());
}
