namespace StorageHub.Desktop;

/// <summary>
/// What the installer calls at each point in a package's life.
/// </summary>
/// <remarks>
/// Here rather than beside PackagedDesktopLifecycle because of one line: uninstalling has to take
/// the Explorer drop broker's COM registration with it, and that is a registry write. The lifecycle
/// itself is policy and portable; this is the Windows edge of it.
///
/// These were Velopack's fast hooks. The MSI runs them as custom actions instead, each one the
/// installed desktop started with <c>--package-hook</c> and a hook's name
/// (eng/installer/StorageHub.wxs): before-update and before-uninstall by the copy being replaced,
/// before Windows Installer looks for files in use, and after-install or after-update by the new
/// copy once it is in place. Each runs before the framework or a window, and ends the process.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class DesktopPackageLifecycleHooks(PackagedDesktopLifecycle lifecycle)
{
    public const string HookArgument = "--package-hook";

    internal const string AfterInstallHook = "after-install";
    internal const string AfterUpdateHook = "after-update";
    internal const string BeforeUpdateHook = "before-update";
    internal const string BeforeUninstallHook = "before-uninstall";

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

    /// <summary>The hook the command line names, or null when it names none.</summary>
    public static string? Named(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (string.Equals(arguments[index], HookArgument, StringComparison.OrdinalIgnoreCase))
            {
                return arguments[index + 1];
            }
        }

        return null;
    }

    /// <summary>Runs the named hook, and says whether there was one by that name.</summary>
    /// <remarks>
    /// The installer ignores what a hook returns: none of them may fail an install or an uninstall,
    /// and an update or removal that cannot stop the agent still has Windows Installer's own
    /// handling of files in use behind it.
    /// </remarks>
    public bool Run(string? hook)
    {
        switch (hook?.ToLowerInvariant())
        {
            case AfterInstallHook:
                AfterInstall();
                return true;
            case AfterUpdateHook:
                AfterUpdate();
                return true;
            case BeforeUpdateHook:
                BeforeUpdate();
                return true;
            case BeforeUninstallHook:
                BeforeUninstall();
                return true;
            default:
                return false;
        }
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
    /// token, and an installer hook is the wrong place to raise a consent prompt. The data in
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
        // The installer's hooks cannot veto an update or uninstall. Give the
        // Agent a bounded graceful-stop window; if it cannot acknowledge,
        // Windows Installer's handling of files in use remains the final fallback.
        _ = _lifecycle.TryStopAgentAsync(reason).AsTask().GetAwaiter().GetResult();
    }
}
