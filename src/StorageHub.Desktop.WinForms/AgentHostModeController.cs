using StorageHub.Agent;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>What the agent is doing right now, regardless of what was configured.</summary>
/// <remarks>
/// This used to describe an installed Windows service as well, and the two could disagree - a
/// service removed outside the app left the configured mode pointing at something that was not
/// there. There is one agent now, and the only question is whether it is registered to start again
/// at sign-in, so the configured mode and what is in force cannot drift apart.
/// </remarks>
internal sealed record AgentHostModeStatus(AgentHostMode Configured)
{
    internal AgentHostMode Effective => Configured;

    /// <summary>Always false. Kept so callers need not know the drift became impossible.</summary>
    internal bool Mismatched => Configured != Effective;
}

internal sealed record AgentHostModeChangeResult(bool Succeeded, string Summary);

/// <summary>
/// Applies a host-mode change from the desktop.
/// </summary>
/// <remarks>
/// Both modes are this user's own agent; they differ only by whether an autostart registration
/// outlives the application. Changing between them is therefore writing or removing that
/// registration, and needs no privilege.
///
/// It used to need a great deal. Installing the Windows service meant re-launching the agent
/// elevated with a management verb and waiting for it, because the desktop could not register a
/// service itself - and the vault had to be re-protected on the far side of that prompt, which is
/// why the elevated process had to keep this user's identity.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class AgentHostModeController
{
    internal static AgentHostModeStatus Describe(AgentHostMode configured) => new(configured);

    /// <summary>
    /// Writes or removes the autostart registration, and reports what Windows actually did.
    /// </summary>
    /// <remarks>
    /// The mode is re-read rather than assumed: autostart can be suppressed by policy or by the
    /// environment switch, and claiming a mode Windows will not honour is worse than reporting that
    /// it did not take.
    /// </remarks>
    internal static AgentHostModeChangeResult Apply(AgentHostMode desired)
    {
        try
        {
            var lifecycle = WindowsDesktopLifecycle.Create();
            DesktopAgentHost.Invalidate();
            _ = desired == AgentHostMode.UserSession
                ? lifecycle.ConfigureAutostart()
                : lifecycle.RemoveAutostart();

            DesktopAgentHost.Invalidate();
            return DesktopAgentHost.Mode == desired
                ? new AgentHostModeChangeResult(true, Ui.Settings.AgentModeApplied)
                : new AgentHostModeChangeResult(false, Ui.Settings.AgentModeChangeFailed);
        }
        catch (Exception)
        {
            return new AgentHostModeChangeResult(false, Ui.Settings.AgentModeChangeFailed);
        }
    }
}
