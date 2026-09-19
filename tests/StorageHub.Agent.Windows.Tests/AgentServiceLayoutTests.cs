using StorageHub.Infrastructure.Windows;

namespace StorageHub.Agent.Windows.Tests;

/// <summary>
/// The service's layout, checked against the rule the agent actually enforces at startup.
///
/// Both halves were covered on their own and still combined into a service that could never start:
/// the staging tests asserted where the binaries go, the data-directory tests asserted that a data
/// root may not overlap an application directory, and nobody put the two real paths together. So
/// this runs the production guard over the production layout, which is the only assertion that
/// would have caught it.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class AgentServiceLayoutTests
{
    /// <summary>
    /// What <c>Program</c> does before anything else when started with <c>--service</c>. It threw
    /// here on every start, so the service control manager reported nothing but "the StorageHub
    /// Agent service terminated unexpectedly".
    /// </summary>
    [Fact]
    public void The_service_layout_passes_the_agent_startup_guard()
    {
        var dataRoot = AgentHostLayout.ResolveDataRoot(AgentHostMode.WindowsService);
        var applicationTree = WindowsAgentDataDirectoryLease.ResolveApplicationOwnedTreeRoot(
            AgentServiceStaging.ResolveDirectory());

        WindowsAgentDataDirectoryLease.EnsureDataRootIsSeparateFromApplication(
            dataRoot,
            applicationTree);
        WindowsAgentDataDirectoryLease.EnsureApplicationTreeIsSeparateFromInstanceLock(
            applicationTree);
    }

    /// <summary>
    /// The guard is only worth running above if it rejects the layout that shipped. Pinning the
    /// broken shape keeps a future move back into the data root from passing silently.
    /// </summary>
    [Fact]
    public void Staging_inside_the_data_root_is_still_rejected()
    {
        var dataRoot = AgentHostLayout.ResolveDataRoot(AgentHostMode.WindowsService);

        var error = Assert.Throws<WindowsAgentDataDirectoryException>(
            () => WindowsAgentDataDirectoryLease.EnsureDataRootIsSeparateFromApplication(
                dataRoot,
                Path.Combine(dataRoot, "bin")));

        Assert.Equal(WindowsAgentDataDirectoryFailure.InvalidPath, error.Failure);
    }
}
