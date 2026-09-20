using StorageHub.Agent;
using StorageHub.Desktop;
using StorageHub.Ipc;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Which agent the desktop talks to, on whichever system it is running on.
/// </summary>
/// <remarks>
/// This used to be a Windows question with a named pipe for an answer: DesktopAgentHost read the
/// service control manager itself and built a pipe name inline. None of that could run on Linux,
/// which is what kept the desktop Windows-only even after the agent was portable. It now asks
/// IAgentPlatform, so these assertions hold on both.
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

    /// <summary>The desktop owns the agent's lifetime on both platforms.</summary>
    /// <remarks>
    /// This asserted that Linux never discovers a service mode, which was the one asymmetry between
    /// the two. Both now host the same two modes and the desktop starts the agent in either.
    /// </remarks>
    [Fact]
    public void TheDesktopOwnsTheAgentOnEveryPlatform()
    {
        Assert.True(DesktopAgentHost.DesktopStartsAgent);
        Assert.Contains(DesktopAgentHost.Mode, (AgentHostMode[])[AgentHostMode.UserSession, AgentHostMode.AppSession]);
    }
}
