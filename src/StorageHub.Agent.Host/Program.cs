using System.Reflection;
using CL.Storage;
using CL.Storage.Configuration;
using CodeLogic;
using StorageHub.Agent;
using StorageHub.Ipc;
using StorageHub.Ipc.Windows;
using StorageHub.Agent.Scheduling;
using StorageHub.Agent.Sync;
using StorageHub.Agent.Transfers;
using StorageHub.Agent.Host;
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

// The one platform branch in this file. Everything below asks the platform rather than the
// operating system, so the composition is the same on both and CA1416 reports any line that
// forgets - which is how the twenty-two Windows assumptions that used to live here were found.
if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
{
    Console.Error.WriteLine("The StorageHub agent runs on Windows and Linux.");
    return 3;
}

// The selection lives in AgentPlatforms, because the desktop needs the same answer and answering
// it twice is how the two came to disagree about the autostart entry's name.
if (!AgentPlatforms.TryCreate(out var agentPlatform, out var hostPlatform) ||
    agentPlatform is null || hostPlatform is null)
{
    Console.Error.WriteLine("The StorageHub agent runs on Windows and Linux.");
    return 3;
}

// The mode decides three things that must agree: where the data lives, what protects the vault, and
// which endpoint the desktop looks for. Resolving it through the platform keeps them from drifting
// apart - a service that picked the machine pipe but the user's data root would present an empty
// installation and fail every credential read.
AgentHostMode hostMode;
try
{
    hostMode = hostPlatform.ResolveHostMode(args);
}
catch (PlatformNotSupportedException error)
{
    Console.Error.WriteLine(error.Message);
    return 3;
}

var applicationVersion = GetApplicationVersion();
var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var resolvedPaths = agentPlatform.ResolvePaths(hostMode);
var configuredStorageHubRoot = Environment.GetEnvironmentVariable(AgentHostLayout.DataRootVariable);
if (string.IsNullOrWhiteSpace(configuredStorageHubRoot))
{
    configuredStorageHubRoot = resolvedPaths.DataRoot;
}

// Refuse to start where something else already owns this machine's agent.
if (hostPlatform.DescribeStartupRefusal(hostMode) is { } refusal)
{
    Console.Error.WriteLine(refusal);
    return 4;
}

var normalEndpoint = agentPlatform.ResolveEndpoint(hostMode, AgentIpcChannel.Normal);
var secretEndpoint = agentPlatform.ResolveEndpoint(hostMode, AgentIpcChannel.Secret);
var trustModel = agentPlatform.ResolveTrustModel(hostMode);
var permittedClients = hostPlatform.ResolvePermittedPrincipals(hostMode, configuredStorageHubRoot);

hostPlatform.AttachToServiceManager(hostMode, shutdown);

IAgentDataDirectory agentDataDirectory;
try
{
    agentDataDirectory = hostPlatform.AcquireDataDirectory(hostMode, configuredStorageHubRoot);
}
catch (AgentDataDirectoryException error)
{
    Console.Error.WriteLine($"StorageHub Agent data directory rejected: {error.Message}");
    return 1;
}

using var agentDataDirectoryLifetime = agentDataDirectory;
var storageHubRoot = agentDataDirectory.RootDirectory;
var agentRoot = agentDataDirectory.AgentDirectory;
// The platform owns what protects the vault, which mode decides. Composing it here rather than
// naming DPAPI at the call site is what lets the same line serve a Linux host once the composition
// root moves out of this Windows-only assembly.
var agentPaths = resolvedPaths with { DataRoot = storageHubRoot };
var concurrencyConfiguration = AgentConcurrencyConfiguration.Load(
    Path.Combine(storageHubRoot, "Desktop"));
var runtimeSecretFileMaterializer = agentPlatform.CreateRuntimeSecretFileMaterializer(agentPaths);
_ = runtimeSecretFileMaterializer.ScavengeOrphans(TimeSpan.FromHours(24));

var initialization = await CodeLogic.CodeLogic.InitializeAsync(options =>
{
    options.FrameworkRootPath = agentDataDirectory.FrameworkDirectory;
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
    () => agentPlatform.CreateSecretProtector(hostMode, agentPaths));
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
// The second and last guard. The Explorer drop broker's server half is not a weakened feature
// elsewhere - there is no Explorer - so the handler simply is not registered rather than being
// present and reporting failure.
IAgentIpcCommandHandler? shellTransferCommands = OperatingSystem.IsWindows()
    ? new ShellTransferIpcCommandService(
        transferStore,
        transferEndpointConnector,
        timeProvider: null,
        transferQueueSubsystem)
    : null;
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
    BuildCommandHandlers(
        storageCommands,
        profileCommands,
        keyStoreCommands,
        trustCommands,
        transferCommands,
        syncCommands,
        scheduleCommands,
        objectInspectorCommands,
        sshTerminalCommands,
        new AgentControlIpcCommandService(
            () => shutdown.TrySetResult(),
            Environment.ProcessId),
        shellTransferCommands));
var ipc = new IpcServerSubsystem(
    agentPlatform.Transport,
    new IpcServerOptions
    {
        Endpoint = normalEndpoint,
        TrustModel = trustModel,
        PermittedPrincipals = permittedClients,
        AgentVersion = applicationVersion,
        AgentInstanceId = agentInstanceId,
        // The desktop intentionally owns independent clients for workspaces, queue,
        // sync, settings, connection management, terminals, and status. Eight slots
        // caused healthy clients to look offline while idle forms retained sessions.
        MaxConcurrentClients = 64,
        RequestTimeout = TimeSpan.FromMinutes(2),
        SessionIdleTimeout = TimeSpan.FromMinutes(3)
    },
    agentPlatform.PeerAuthorizer,
    requestHandler.HandleSessionAsync);
var secretRequestHandler = new AgentSecretIpcRequestHandler(
    new SecretVaultIpcCommandService(() => vaultSubsystem.Vault));
var secretIpc = new IpcServerSubsystem(
    agentPlatform.Transport,
    new IpcServerOptions
    {
        Endpoint = secretEndpoint,
        TrustModel = trustModel,
        PermittedPrincipals = permittedClients,
        AgentVersion = applicationVersion,
        AgentInstanceId = agentInstanceId,
        MaxConcurrentClients = 8,
        RequestTimeout = TimeSpan.FromSeconds(30),
        FrameKind = IpcFrameKind.Secret
    },
    agentPlatform.PeerAuthorizer,
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

/// <summary>
/// Composes the handlers, leaving out any the platform does not have.
/// </summary>
/// <remarks>
/// Only the Explorer drop broker is optional today. Registering a stub that reports failure would
/// be worse than not registering it: the desktop asks the agent what it supports, and a handler
/// that answers is a handler that exists.
/// </remarks>
static CompositeAgentIpcCommandHandler BuildCommandHandlers(params IAgentIpcCommandHandler?[] handlers) =>
    new([.. handlers.Where(handler => handler is not null).Select(handler => handler!)]);

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
