namespace StorageHub.Desktop;

/// <summary>
/// Starting, stopping and restarting the background agent.
/// </summary>
/// <remarks>
/// Declared at the bottom of AgentControlForm, so the lifecycle client and the controllers that
/// speak these types could not leave the WinForms shell either. They are contracts, not UI.
/// </remarks>
public enum AgentLifecycleAction
{
    Start = 1,
    Stop = 2,
    Restart = 3
}

public sealed record AgentLifecycleResult(bool Succeeded, string Message);

/// <summary>
/// Starts, stops, and restarts the background agent. Kept as an interface so the dialog can be
/// exercised without launching a real process.
/// </summary>
public interface IAgentLifecycleController
{
    Task<AgentLifecycleResult> ExecuteAsync(
        AgentLifecycleAction action,
        CancellationToken cancellationToken = default);
}
