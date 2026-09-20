using static StorageHub.Desktop.Tests.LifecycleFixtures;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// What an installer calls at each point in a package's life, and what it must not call.
/// </summary>
/// <remarks>
/// Split from PackagedDesktopLifecycleTests when the lifecycle moved to Desktop.Core. The policy it
/// tests is portable; these four are not, because the hooks unregister a COM server in the registry
/// and that is the one thing about them that is Windows.
/// </remarks>
public sealed class DesktopPackageLifecycleHooksTests
{
    [Fact]
    public void VelopackHooksRegisterRefreshStopAndUnregisterWithoutADataDeletionSurface()
    {
        var fixture = CreateFixture(shutdownResult: true);
        var brokerUnregisterCalls = 0;
        var hooks = new DesktopPackageLifecycleHooks(
            fixture.Lifecycle,
            () =>
            {
                brokerUnregisterCalls++;
                return true;
            });

        hooks.AfterInstall();
        hooks.BeforeUpdate();
        hooks.AfterUpdate();
        hooks.BeforeUninstall();

        Assert.Equal(2, fixture.RunEntries.SetCalls.Count);
        Assert.Equal("StorageHub.Agent", Assert.Single(fixture.RunEntries.RemovedNames));
        Assert.Equal(
            [AgentShutdownReason.Update, AgentShutdownReason.Uninstall],
            fixture.AgentClient.ShutdownReasons);
        Assert.Equal(1, brokerUnregisterCalls);
    }

    /// <summary>
    /// A service-hosted agent answers the same pipe as one the desktop started, so an
    /// unconditional shutdown before an update stopped the service.s own process behind the
    /// service control manager.s back. Windows logged an unexpected termination and, with no
    /// failure actions, left it stopped -- so every update ended with "the background agent did
    /// not become ready in time". It happened four times on one machine in a single evening.
    /// </summary>
    [Fact]
    public void AnUpdateLeavesAServiceHostedAgentAlone()
    {
        var fixture = CreateFixture(desktopOwnsAgent: false);

        new DesktopPackageLifecycleHooks(fixture.Lifecycle).BeforeUpdate();

        Assert.Empty(fixture.AgentClient.ShutdownReasons);
    }

    [Fact]
    public void AnUpdateStillStopsAnAgentTheDesktopStarted()
    {
        var fixture = CreateFixture(desktopOwnsAgent: true, shutdownResult: true);

        new DesktopPackageLifecycleHooks(fixture.Lifecycle).BeforeUpdate();

        Assert.Equal(
            AgentShutdownReason.Update,
            Assert.Single(fixture.AgentClient.ShutdownReasons));
    }

    /// <summary>Removing the service stops it, through the control manager rather than behind it.</summary>
    [Fact]
    public void UninstallingLeavesAServiceHostedAgentToTheServiceControlManager()
    {
        var fixture = CreateFixture(desktopOwnsAgent: false);

        new DesktopPackageLifecycleHooks(fixture.Lifecycle, () => true).BeforeUninstall();

        Assert.Empty(fixture.AgentClient.ShutdownReasons);
    }

    [Fact]
    public void UninstallingStopsAnAgentTheDesktopStarted()
    {
        var fixture = CreateFixture(desktopOwnsAgent: true, shutdownResult: true);

        new DesktopPackageLifecycleHooks(fixture.Lifecycle, () => true).BeforeUninstall();

        Assert.Contains(AgentShutdownReason.Uninstall, fixture.AgentClient.ShutdownReasons);
    }
}
