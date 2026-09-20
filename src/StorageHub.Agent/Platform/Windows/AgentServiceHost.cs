using System.ServiceProcess;

namespace StorageHub.Agent.Windows;

/// <summary>
/// Attaches the agent to the service control manager without restructuring how it starts.
///
/// The SCM expects a process to call its control dispatcher within about thirty seconds of launch,
/// and it delivers Stop on that dispatcher's thread. The agent's own startup -- CodeLogic, the
/// database, every subsystem -- is far too much to finish inside that window on a cold boot, so
/// the dispatcher runs on its own thread and answers the SCM immediately while startup continues
/// on the main thread. Stop then simply completes the same shutdown signal that Ctrl+C completes
/// in a console session, so both paths retire the agent through identical code.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class AgentServiceHost : ServiceBase
{
    private readonly TaskCompletionSource _stopRequested;

    private AgentServiceHost(TaskCompletionSource stopRequested)
    {
        _stopRequested = stopRequested;
        ServiceName = AgentHostLayout.ServiceName;
        CanShutdown = true;
        CanStop = true;
        AutoLog = false;
    }

    /// <summary>
    /// Runs the SCM dispatcher on a background thread and returns once it is attached, or
    /// immediately if this process was not started by the SCM.
    /// </summary>
    public static void Attach(TaskCompletionSource stopRequested)
    {
        ArgumentNullException.ThrowIfNull(stopRequested);
        var attached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var host = new AgentServiceHost(stopRequested);
                _ = attached.TrySetResult();
                Run(host);
            }
            catch (Exception error)
            {
                // Started outside the SCM, or the dispatcher refused. Neither is fatal on its own:
                // the agent still runs, and whoever launched it still controls its lifetime.
                _ = attached.TrySetResult();
                Console.Error.WriteLine($"StorageHub Agent service dispatcher unavailable: {error.GetType().Name}");
            }
            finally
            {
                // If the dispatcher returns, the SCM has finished with this process.
                _ = stopRequested.TrySetResult();
            }
        })
        {
            IsBackground = true,
            Name = "StorageHub agent service control dispatcher"
        };
        thread.Start();
        _ = attached.Task.Wait(TimeSpan.FromSeconds(5));
    }

    protected override void OnStart(string[] args)
    {
        // Intentionally empty. Startup is already under way on the main thread; blocking here
        // would only delay the acknowledgement the SCM is waiting for.
    }

    protected override void OnStop() => _stopRequested.TrySetResult();

    protected override void OnShutdown() => _stopRequested.TrySetResult();
}
