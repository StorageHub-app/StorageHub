namespace StorageHub.Desktop;

/// <summary>
/// What an installer calls at each point in a package's life.
/// </summary>
/// <remarks>
/// Here rather than beside PackagedDesktopLifecycle because of one line: uninstalling has to take
/// the Explorer drop broker's COM registration with it, and that is a registry write. The lifecycle
/// itself is policy and portable; this is the Windows edge of it.
///
/// It is also on its way out. These are Velopack fast hooks, and 2.0 installs from an MSI whose
/// custom actions call the same two methods. What the hooks do survives; the dispatcher does not.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class DesktopPackageLifecycleHooks(PackagedDesktopLifecycle lifecycle)
{
    private readonly PackagedDesktopLifecycle _lifecycle = lifecycle ??
        throw new ArgumentNullException(nameof(lifecycle));
    private readonly Func<bool> _unregisterExplorerDropBroker = ExplorerDropBrokerInstaller.Unregister;

    internal DesktopPackageLifecycleHooks(
        PackagedDesktopLifecycle lifecycle,
        Func<bool> unregisterExplorerDropBroker)
        : this(lifecycle)
    {
        _unregisterExplorerDropBroker = unregisterExplorerDropBroker ??
            throw new ArgumentNullException(nameof(unregisterExplorerDropBroker));
    }

    // A fresh install has no mode yet, so it establishes the historical default rather than letting
    // an absent logon entry be read as a deliberate choice.
    public void AfterInstall() => _ = _lifecycle.ConfigureAutostart(force: true);

    public void AfterUpdate() => _ = _lifecycle.ConfigureAutostart();

    /// <summary>
    /// Stops the agent so an update is not swapping files underneath it -- but only an agent
    /// this desktop started.
    ///
    /// A service-hosted agent answers the same pipe, so an unconditional shutdown request here
    /// stopped the service.s own process behind the service control manager.s back. Windows
    /// recorded an unexpected termination and, with no failure actions configured, left it
    /// stopped -- so every single update ended with the desktop reporting that the agent did not
    /// become ready. There is nothing to get out of the way either: an update replaces the
    /// application, not the machine-owned copy the service runs from.
    /// </summary>
    public void BeforeUpdate()
    {
        if (!_lifecycle.DesktopOwnsAgent)
        {
            return;
        }

        StopSynchronously(AgentShutdownReason.Update);
    }

    public void BeforeUninstall()
    {
        // Same reasoning as BeforeUpdate. Removing the service below is what stops a
        // service-hosted agent, through the service control manager rather than behind it.
        if (_lifecycle.DesktopOwnsAgent)
        {
            StopSynchronously(AgentShutdownReason.Uninstall);
        }

        _ = _lifecycle.RemoveAutostart();
        TryRemoveAgentService();
        _ = _unregisterExplorerDropBroker();
    }

    /// <summary>
    /// Removes the agent service so uninstalling does not leave an auto-starting LocalSystem
    /// service behind, running binaries from a machine directory for an application that is gone.
    ///
    /// Best effort: StorageHub installs per user, so the uninstaller usually has no elevated
    /// token, and a Velopack fast hook is the wrong place to raise a consent prompt. The data in
    /// ProgramData is deliberately left alone either way -- it holds credentials, and uninstalling
    /// an application is not consent to destroy them.
    /// </summary>
    private static void TryRemoveAgentService()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Nothing to remove. This uninstalled the Windows service, which no longer exists: the
        // agent is a per-user process whose only registration is the autostart entry, and
        // RemoveAutostart already takes that.
    }

    private void StopSynchronously(AgentShutdownReason reason)
    {
        // Velopack fast hooks cannot veto an update or uninstall. Give the
        // Agent a bounded graceful-stop window; if it cannot acknowledge,
        // Velopack's normal locking-process handling remains the final fallback.
        _ = _lifecycle.TryStopAgentAsync(reason).AsTask().GetAwaiter().GetResult();
    }
}
