using StorageHub.Agent;
using StorageHub.Desktop;
using StorageHub.Ipc;
using Xunit;

namespace StorageHub.Desktop.Avalonia.Tests;

/// <summary>
/// Which agent the desktop talks to, on whichever system it is running on.
/// </summary>
/// <remarks>
/// This used to be a Windows question with a named pipe for an answer: DesktopAgentHost read the
/// service control manager itself, built a pipe name, and mapped the mode to a trust model inline.
/// None of that could run on Linux, which is what kept the desktop Windows-only even after the
/// agent was portable. It now asks IAgentPlatform, so these assertions hold on both.
/// </remarks>
public class DesktopAgentHostTests
{
    [Fact]
    public void TheDesktopUsesThePlatformForThisSystem()
    {
        Assert.Equal(
            AgentPlatforms.ForCurrentOperatingSystem().Name,
            DesktopAgentHost.Platform.Name);
    }

    [Fact]
    public void TheTwoChannelsAreDifferentEndpoints()
    {
        // One socket for status and browsing, another for secrets. Sharing one would put secret
        // traffic behind the same connection cap as everything else.
        Assert.NotEqual(DesktopAgentHost.NormalEndpoint, DesktopAgentHost.SecretEndpoint);
    }

    [Fact]
    public void TheTrustModelFollowsTheMode()
    {
        var expected = DesktopAgentHost.Mode == AgentHostMode.WindowsService
            ? IpcTrustModel.MachineService
            : IpcTrustModel.SameUser;

        Assert.Equal(expected, DesktopAgentHost.TrustModel);
    }

    [Fact]
    public void TheEndpointIsTheOneThisPlatformPublishes()
    {
        var moniker = DesktopAgentHost.NormalEndpoint.Moniker;

        if (OperatingSystem.IsLinux())
        {
            // A path on the runtime root, not a pipe name - and the agent binds exactly this.
            Assert.EndsWith("agent.sock", moniker, StringComparison.Ordinal);
            Assert.StartsWith("/", moniker, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("StorageHub", moniker, StringComparison.Ordinal);
        }
    }

    /// <summary>There is no service mode on Linux, so the desktop can never discover one.</summary>
    [Fact]
    public void LinuxNeverReportsAServiceMode()
    {
        if (!OperatingSystem.IsLinux()) return;

        Assert.NotEqual(AgentHostMode.WindowsService, DesktopAgentHost.Mode);
        Assert.True(DesktopAgentHost.DesktopStartsAgent);
    }
}
