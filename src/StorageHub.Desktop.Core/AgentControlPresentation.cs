using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// What the agent control screen says about a state, and which of its buttons that state allows.
/// </summary>
/// <remarks>
/// <para>
/// The agent owns the durable queue, the scheduler and the vault, so when it is degraded the rest
/// of the application quietly stops working. The status bar can only abbreviate that to a word;
/// this is the sentence behind it, and recovery is called out because it is the one state where
/// the agent is running and yet nothing durable works.
/// </para>
/// <para>
/// Declared here rather than on the window because it is what the screen <em>means</em> rather
/// than how it is drawn -- and because the rule about which buttons a state allows is the sort of
/// thing that is wrong in a way no screenshot shows. The colour stays with the view, which is the
/// only part of this that is a matter of drawing.
/// </para>
/// </remarks>
internal static class AgentControlPresentation
{
    /// <summary>The state in a word, as the heading reads it.</summary>
    internal static string DescribeState(AgentConnectionState state) => state switch
    {
        AgentConnectionState.Connected => Ui.Updates.AgentStateRunning,
        AgentConnectionState.RecoveryOnly => Ui.Updates.AgentStateRecovery,
        AgentConnectionState.Disconnected => Ui.Updates.AgentStateNotRunning,
        AgentConnectionState.Reconnecting => Ui.Shell.AgentReconnectingStatus,
        _ => Ui.Updates.AgentStateStarting
    };

    /// <summary>
    /// What that state means for the rest of the application, when the agent has not said
    /// something more specific itself.
    /// </summary>
    internal static string DescribeDefaultDetail(AgentConnectionState state) => state switch
    {
        AgentConnectionState.Connected => Ui.Updates.TransfersSyncAndSchedulesAreAvailable,
        AgentConnectionState.RecoveryOnly => Ui.Updates.TheAgentStartedButItsDurableState,
        AgentConnectionState.Disconnected => Ui.Updates.StorageHubCannotReachTheBackgroundAgentTransfers,
        AgentConnectionState.Reconnecting => Ui.Shell.AgentReconnecting,
        _ => Ui.Updates.TheAgentIsStarting
    };

    /// <summary>Whatever the agent reported, or the sentence for the state when it said nothing.</summary>
    internal static string DescribeDetail(AgentMonitorStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return string.IsNullOrWhiteSpace(status.Detail)
            ? DescribeDefaultDetail(status.State)
            : status.Detail;
    }

    /// <summary>
    /// Whether the agent is up in any sense, which is what decides whether stopping it is on offer.
    /// </summary>
    /// <remarks>
    /// Recovery counts, and so do Starting and Reconnecting: there is a process to stop in every
    /// one of them. Only a state that has been probed and found absent is not running.
    /// </remarks>
    internal static bool IsRunning(AgentConnectionState state) => state is
        AgentConnectionState.Connected or
        AgentConnectionState.RecoveryOnly or
        AgentConnectionState.Starting or
        AgentConnectionState.Reconnecting;

    /// <summary>
    /// Whether Start is on offer.
    /// </summary>
    /// <remarks>
    /// Only when the agent is known to be down. Offering it while one is already running would
    /// invite a second, and the vault and the durable queue belong to one process per account.
    /// </remarks>
    internal static bool CanStart(AgentConnectionState? state) => state is AgentConnectionState.Disconnected;

    internal static bool CanStop(AgentConnectionState? state) => state is { } known && IsRunning(known);

    // There is deliberately no CanRestart. Restart is offered in every state, including one that
    // has not been reported yet: an agent that is down is exactly what a restart should fix, and
    // dimming the one button that repairs things would be the wrong answer while the shell is
    // still finding out. The only thing that dims it is an action already running.

    /// <summary>What the outcome line says while an action is in flight.</summary>
    internal static string DescribeInProgress(AgentLifecycleAction action) => action switch
    {
        AgentLifecycleAction.Start => Ui.Updates.StartingTheAgent,
        AgentLifecycleAction.Stop => Ui.Updates.StoppingTheAgent,
        _ => Ui.Updates.RestartingTheAgent
    };
}
