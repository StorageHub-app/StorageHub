using StorageHub.Desktop;
using Xunit;

namespace StorageHub.Desktop.Avalonia.Tests;

/// <summary>
/// The desktop talking to an agent that is actually running.
/// </summary>
/// <remarks>
/// Skipped unless STORAGEHUB_LIVE_AGENT is set, because it needs one. Everything else in this suite
/// proves the desktop resolves the right endpoint; only this proves something answers on it. It is
/// the check that the port's whole point holds on Linux - the desktop was Windows-only until the
/// endpoint stopped being a named pipe, and a build that compiles is not evidence that it connects.
/// </remarks>
public class LiveAgentTests
{
    private static bool Enabled =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STORAGEHUB_LIVE_AGENT"));

    [Fact]
    public async Task TheMonitorReachesARunningAgent()
    {
        Assert.SkipUnless(Enabled, "Set STORAGEHUB_LIVE_AGENT to run against a live agent.");

        await using var monitor = new AgentStatusMonitor(
            pollInterval: TimeSpan.FromSeconds(1),
            connectTimeout: TimeSpan.FromSeconds(2));

        var connected = new TaskCompletionSource<AgentConnectionState>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        monitor.StatusChanged += (_, e) =>
        {
            if (e.Status.State == AgentConnectionState.Connected) connected.TrySetResult(e.Status.State);
        };

        monitor.Start();

        var finished = await Task.WhenAny(
            connected.Task,
            Task.Delay(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken));

        Assert.True(
            finished == connected.Task,
            $"No agent answered on {DesktopAgentHost.NormalEndpoint.Moniker} within 20 seconds.");
    }
}
