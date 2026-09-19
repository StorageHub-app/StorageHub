using System.Reflection;
using CL.Storage;
using CL.Storage.Configuration;
using CodeLogic;
using StorageHub.Agent;
using StorageHub.Agent.Ipc;
using StorageHub.Agent.Scheduling;
using StorageHub.Agent.Sync;
using StorageHub.Agent.Transfers;
using StorageHub.Agent.Windows;
using StorageHub.Application;
using StorageHub.Contracts.Ipc;
using StorageHub.Infrastructure.Windows;
using StorageHub.Persistence;
using StorageHub.Persistence.Connections;
using StorageHub.Persistence.Credentials;
using StorageHub.Persistence.Scheduling;
using StorageHub.Persistence.Sync;
using StorageHub.Persistence.Transfers;
using StorageHub.Persistence.Trust;
using StorageHub.Storage.CodeLogic;
using StorageHub.Sync;

// The host mode decides three things that must agree: where the data lives, which DPAPI key
// protects the vault, and which pipe the desktop looks for. Resolving it first keeps them from
// drifting apart -- a service that picked the machine pipe but the user's data root would present
// an empty installation and fail every credential read.
var hostMode = args.Contains(AgentHostLayout.ServiceArgument, StringComparer.OrdinalIgnoreCase)
    ? AgentHostMode.WindowsService
    : AgentHostMode.UserSession;
var applicationVersion = GetApplicationVersion();
var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var configuredStorageHubRoot = Environment.GetEnvironmentVariable(AgentHostLayout.DataRootVariable);
if (string.IsNullOrWhiteSpace(configuredStorageHubRoot))
{
    configuredStorageHubRoot = AgentHostLayout.ResolveDataRoot(hostMode);
}

// Service management runs elevated and does no agent work, so it is handled before any of the
// startup below. Elevation keeps the caller's user identity, which is exactly what the migration
// needs: rights to write the machine location, while still able to open the user's own secrets.
if (args.Contains("--install-service", StringComparer.OrdinalIgnoreCase) ||
    args.Contains("--uninstall-service", StringComparer.OrdinalIgnoreCase))
{
    return await AgentServiceCommands.ExecuteAsync(args).ConfigureAwait(false);
}

// Refuse to run a session agent while the service owns this machine. An older desktop, or an
// autostart entry left behind by one, would otherwise start a second agent on a second database:
// no corruption, because the files differ, but the app and the scheduler would quietly disagree
// about what is stored. Exiting here is the only signal a stale launcher will understand.
if (hostMode == AgentHostMode.UserSession && AgentServiceInstaller.Describe().Installed)
{
    Console.Error.WriteLine(
        "The StorageHub agent service is installed; the session agent will not start alongside it.");
    return 4;
}

var pipeNames = AgentHostLayout.ResolvePipeNames(hostMode);
var pipeAccess = hostMode == AgentHostMode.WindowsService
    ? IpcPipeAccess.MachineService
    : IpcPipeAccess.CurrentUserOnly;
var permittedClientSids = hostMode == AgentHostMode.WindowsService
    ? AgentServiceClients.ReadPermittedSids(configuredStorageHubRoot)
    : [];

// Answer the service control manager before the slow startup below, or it retires the
// process as unresponsive long before the agent is ready.
if (hostMode == AgentHostMode.WindowsService)
{
    AgentServiceHost.Attach(shutdown);
}

WindowsAgentDataDirectoryLease agentDataDirectoryLease;
try
{
    var applicationOwnedTreeRoot =
        WindowsAgentDataDirectoryLease.ResolveApplicationOwnedTreeRoot(AppContext.BaseDirectory);
    WindowsAgentDataDirectoryLease.EnsureDataRootIsSeparateFromApplication(
        configuredStorageHubRoot,
        applicationOwnedTreeRoot);
    WindowsAgentDataDirectoryLease.EnsureApplicationTreeIsSeparateFromInstanceLock(
        applicationOwnedTreeRoot);
    agentDataDirectoryLease = WindowsAgentDataDirectoryLease.Acquire(
        configuredStorageHubRoot,
        scope: hostMode == AgentHostMode.WindowsService
            // The machine tree stays reachable by administrators. Its secrets are sealed with the
            // machine key, which any administrator can already use, and the elevated switch back to
            // a session mode runs as the signed-in user -- it has to be able to read what it is
            // bringing home.
            ? AgentDataTreeScope.Machine
            : AgentDataTreeScope.CurrentUser);
}
catch (WindowsAgentDataDirectoryException error)
{
    Console.Error.WriteLine($"StorageHub Agent data directory rejected: {error.Message}");
    return 1;
}
catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
{
    Console.Error.WriteLine("StorageHub Agent data directory rejected: the configured path is invalid.");
    return 1;
}

using var agentDataDirectoryLifetime = agentDataDirectoryLease;
var storageHubRoot = agentDataDirectoryLease.RootDirectory;
var agentRoot = agentDataDirectoryLease.AgentDirectory;
var concurrencyConfiguration = AgentConcurrencyConfiguration.Load(
    Path.Combine(storageHubRoot, "Desktop"));
var runtimeSecretFileMaterializer = new WindowsRuntimeSecretFileMaterializer(
    Path.Combine(agentRoot, "runtime-secrets"));
_ = runtimeSecretFileMaterializer.ScavengeOrphans(TimeSpan.FromHours(24));

var initialization = await CodeLogic.CodeLogic.InitializeAsync(options =>
{
    options.FrameworkRootPath = agentDataDirectoryLease.FrameworkDirectory;
    options.ApplicationRootPath = agentRoot;
    options.AppVersion = applicationVersion;
    options.HandleShutdownSignals = false;
});

if (!initialization.Success || initialization.ShouldExit)
{
    Console.Error.WriteLine($"StorageHub Agent startup failed: {initialization.Message}");
    return 1;
}

await Libraries.LoadAsync<StorageLibrary>();
// Until CL.Storage ships RuntimeOnly mode, disabling configured connections is
// the only safe bootstrap: runtime backends are registered by StorageHub and no
// provider credential is ever written through CodeLogic configuration.
Libraries.OverrideConfig<StorageConfig>("CL.Storage", "storage", config => config.Enabled = false);

var agentInstanceId = Guid.NewGuid();
AgentRuntimeCoordinator? coordinator = null;
var databaseOptions = new SqliteDatabaseOptions(
    Path.Combine(agentRoot, "storagehub.db"));
var databaseSubsystem = new DatabaseAgentSubsystem(databaseOptions);
using var vaultSubsystem = new SecretVaultAgentSubsystem(
    Path.Combine(agentRoot, "vault"),
    hostMode == AgentHostMode.WindowsService
        ? DpapiProtectionScope.LocalMachine
        : DpapiProtectionScope.CurrentUser);
var schedulerDatabase = new SingleWriterSqliteDatabase(databaseOptions);
var schedulerStore = new SqliteScheduledSyncJobStore(schedulerDatabase);
var scheduleManagementRepository = new SqliteSyncScheduleManagementRepository(schedulerDatabase);
var schedulerDispatchStore = new SqliteScheduledSyncDispatchStore(schedulerDatabase);
await using var schedulerSubsystem = new SchedulerAgentSubsystem(
    schedulerStore,
    new DurableScheduledSyncJobRunner(schedulerDispatchStore));
var transferDatabase = new SingleWriterSqliteDatabase(databaseOptions);
var transferStore = new SqliteTransferJobStore(transferDatabase);
var transferTrustStore = new SqliteTrustStore(transferDatabase);
var transferProfiles = new SqliteConnectionProfileRepository(databaseOptions);
// One connector for the subsystem, so a profile's registration -- and with it the storage
// library's listing snapshot and pooled session -- survives between calls instead of being
// rebuilt for each one.
await using var transferConnector = new SharedConnectionProfileConnector(
    () => new CodeLogicConnectionProfileConnector(
        new CodeLogicStorageSessionFactory(
            Libraries.Get<StorageLibrary>() ??
            throw new InvalidOperationException("CL.Storage is not configured.")),
        vaultSubsystem.Vault,
        transferTrustStore,
        runtimeSecretFileMaterializer));
var transferEndpointConnector = new CodeLogicTransferEndpointConnector(
    transferProfiles,
    transferConnector.Get);
await using var transferQueueSubsystem = new TransferQueueAgentSubsystem(
    transferStore,
    transferEndpointConnector,
    new TransferQueueWorkerOptions
    {
        AdaptiveConcurrency = concurrencyConfiguration.Adaptive,
        MinimumConcurrency = concurrencyConfiguration.Minimum,
        MaximumConcurrency = concurrencyConfiguration.MaximumTransfers,
        PerConnectionConcurrency = concurrencyConfiguration.PerConnection
    });
await using var storageCommands = new StorageIpcCommandService(
    databaseOptions,
    () => vaultSubsystem.Vault,
    runtimeSecretFileMaterializer,
    () => new CodeLogicStorageSessionFactory(
        Libraries.Get<StorageLibrary>() ??
        throw new InvalidOperationException("CL.Storage is not configured.")));
var profileCommands = new ConnectionProfileIpcCommandService(databaseOptions);
var keyStoreCommands = new KeyStoreIpcCommandService(
    new SqliteKeyStoreRepository(databaseOptions),
    () => vaultSubsystem.Vault);
var trustCommands = new ConnectionTrustIpcCommandService(databaseOptions);
var transferCommands = new TransferQueueIpcCommandService(
    transferStore,
    transferStore,
    transferQueueSubsystem);
var shellTransferCommands = new ShellTransferIpcCommandService(
    transferStore,
    transferEndpointConnector,
    timeProvider: null,
    transferQueueSubsystem);
var syncProfiles = new SqliteSyncProfileRepository(transferDatabase);
var syncBaselines = new SqliteSyncBaselineStore(transferDatabase);
var syncPlans = new SqliteSyncPlanStore(transferDatabase);
var syncRuns = new SqliteSyncRunStore(transferDatabase);
var syncConflicts = new SqliteSyncConflictStore(transferDatabase);
await using var syncSharedConnector = new SharedConnectionProfileConnector(
    () => new CodeLogicConnectionProfileConnector(
        new CodeLogicStorageSessionFactory(
            Libraries.Get<StorageLibrary>() ??
            throw new InvalidOperationException("CL.Storage is not configured.")),
        vaultSubsystem.Vault,
        transferTrustStore,
        runtimeSecretFileMaterializer));
var syncConnector = new CodeLogicSyncEndpointConnector(
    transferProfiles,
    syncSharedConnector.Get);
var syncOrchestration = new SyncOrchestrationService(
    syncProfiles,
    syncBaselines,
    syncPlans,
    syncRuns,
    syncConflicts,
    syncConnector);
var syncOutbox = new SqliteReliableOutboxStore(transferDatabase);
var syncExecution = new SqliteSyncExecutionStore(transferDatabase);
var syncAuditEvents = new SqliteAuditEventStore(transferDatabase);
var syncOutboxProcessor = new SyncOutboxEventProcessor(
    syncOrchestration,
    syncProfiles,
    syncPlans,
    syncExecution,
    syncConnector,
    syncAuditEvents);
await using var syncOutboxSubsystem = new SyncOutboxAgentSubsystem(
    syncOutbox,
    syncOutboxProcessor,
    new SyncOutboxWorkerOptions
    {
        AdaptiveConcurrency = concurrencyConfiguration.Adaptive,
        MinimumConcurrency = concurrencyConfiguration.Minimum,
        MaximumConcurrency = concurrencyConfiguration.MaximumSyncs
    });
var syncCommands = new SyncManagementIpcCommandService(
    syncProfiles,
    syncOrchestration,
    syncRuns,
    syncPlans,
    syncConflicts);
var scheduleCommands = new ScheduleManagementIpcCommandService(scheduleManagementRepository);
var objectInspectorCommands = new ObjectInspectorIpcCommandService(syncConnector);
await using var sshTerminalCommands = new SshTerminalIpcCommandService(
    transferProfiles,
    () => vaultSubsystem.Vault,
    transferTrustStore);
var requestHandler = new AgentIpcRequestHandler(
    () => CreateStatusSnapshot(
        coordinator,
        agentInstanceId,
        transferQueueSubsystem.ActiveExecutionCount,
        syncOutboxSubsystem.ActiveCount),
    new CompositeAgentIpcCommandHandler(
        storageCommands,
        profileCommands,
        keyStoreCommands,
        trustCommands,
        transferCommands,
        shellTransferCommands,
        syncCommands,
        scheduleCommands,
        objectInspectorCommands,
        sshTerminalCommands,
        new AgentControlIpcCommandService(
            () => shutdown.TrySetResult(),
            Environment.ProcessId)));
var ipc = new NamedPipeIpcServerSubsystem(
    new NamedPipeIpcServerOptions
    {
        PipeName = pipeNames.Normal,
        Access = pipeAccess,
        PermittedUserSids = permittedClientSids,
        AgentVersion = applicationVersion,
        AgentInstanceId = agentInstanceId,
        // The desktop intentionally owns independent clients for workspaces, queue,
        // sync, settings, connection management, terminals, and status. Eight slots
        // caused healthy clients to look offline while idle forms retained sessions.
        MaxConcurrentClients = 64,
        RequestTimeout = TimeSpan.FromMinutes(2),
        SessionIdleTimeout = TimeSpan.FromMinutes(3)
    },
    requestHandler.HandleSessionAsync);
var secretRequestHandler = new AgentSecretIpcRequestHandler(
    new SecretVaultIpcCommandService(() => vaultSubsystem.Vault));
var secretIpc = new NamedPipeIpcServerSubsystem(
    new NamedPipeIpcServerOptions
    {
        PipeName = pipeNames.Secret,
        Access = pipeAccess,
        PermittedUserSids = permittedClientSids,
        AgentVersion = applicationVersion,
        AgentInstanceId = agentInstanceId,
        MaxConcurrentClients = 8,
        RequestTimeout = TimeSpan.FromSeconds(30),
        FrameKind = IpcFrameKind.Secret
    },
    secretRequestHandler.HandleSessionAsync);
await using var runtimeCoordinator = new AgentRuntimeCoordinator(
    [
        ipc,
        secretIpc,
        databaseSubsystem,
        vaultSubsystem,
        transferQueueSubsystem,
        syncOutboxSubsystem,
        schedulerSubsystem
    ]);
coordinator = runtimeCoordinator;
CodeLogic.CodeLogic.RegisterApplication(new StorageHubApplication(runtimeCoordinator, applicationVersion));

try
{
    await CodeLogic.CodeLogic.ConfigureAsync();
    await CodeLogic.CodeLogic.StartAsync();

    if (initialization.RunHealthCheck || args.Contains("--health", StringComparer.OrdinalIgnoreCase))
    {
        var report = await CodeLogic.CodeLogic.GetHealthAsync();
        Console.WriteLine(report.ToConsoleString());
        return report.IsHealthy ? 0 : 2;
    }

    if (args.Contains("--run-once", StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine("StorageHub Agent initialized successfully.");
        return 0;
    }

    Console.WriteLine("StorageHub Agent is running. Press Ctrl+C to stop.");
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        shutdown.TrySetResult();
    };

    await shutdown.Task.ConfigureAwait(false);
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"StorageHub Agent failed: {error.GetType().Name}");
    return 1;
}
finally
{
    await CodeLogic.CodeLogic.StopAsync();
}

static string GetApplicationVersion()
{
    var assembly = Assembly.GetExecutingAssembly();
    var informationalVersion = assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion;
    if (!string.IsNullOrWhiteSpace(informationalVersion))
    {
        var metadataSeparator = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        return metadataSeparator < 0
            ? informationalVersion
            : informationalVersion[..metadataSeparator];
    }

    return assembly.GetName().Version?.ToString(3) ?? "0.1.0";
}

static AgentStatusSnapshot CreateStatusSnapshot(
    AgentRuntimeCoordinator? coordinator,
    Guid agentInstanceId,
    int activeTransfers,
    int activeSyncRuns)
{
    var state = coordinator?.State switch
    {
        ApplicationOperationalState.Ready => AgentLifecycleState.Ready,
        ApplicationOperationalState.RecoveryOnly => AgentLifecycleState.Degraded,
        ApplicationOperationalState.Faulted => AgentLifecycleState.Faulted,
        ApplicationOperationalState.Stopping or ApplicationOperationalState.Stopped => AgentLifecycleState.Stopping,
        _ => AgentLifecycleState.Starting
    };

    return new AgentStatusSnapshot(
        agentInstanceId,
        state,
        DateTimeOffset.UtcNow,
        ActiveTransfers: activeTransfers,
        ActiveSyncRuns: activeSyncRuns,
        coordinator?.HealthMessage);
}
