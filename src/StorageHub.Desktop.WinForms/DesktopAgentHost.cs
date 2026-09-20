using StorageHub.Agent;
using StorageHub.Ipc;

namespace StorageHub.Desktop;

/// <summary>
/// Which agent the desktop should talk to, and how.
///
/// The pipe name, the pipe's security and whether the desktop may start an agent of its own all
/// follow from one fact: whether the machine has a StorageHub service. Deciding that in one place
/// stops the three from disagreeing -- connecting to the machine pipe with current-user-only
/// security, or launching a session agent beside a running service, are both silent failures that
/// look like "the agent is offline".
/// </summary>
internal static class DesktopAgentHost
{
    private static readonly Lock Gate = new();

    /// <summary>
    /// Must match <see cref="PackagedDesktopLifecycleOptions.RunEntryName"/>; the default is
    /// repeated here because reading the entry cannot depend on building a lifecycle, which needs
    /// an executable path this type has no reason to know.
    /// </summary>
    private const string RunEntryName = "StorageHub.Agent";
    private static AgentHostMode? _mode;

    /// <summary>The mode in force, discovered from the service control manager and then cached.</summary>
    internal static AgentHostMode Mode
    {
        get
        {
            lock (Gate)
            {
                _mode ??= ResolveMode();
                return _mode.Value;
            }
        }
    }

    internal static string NormalPipeName => AgentHostLayout.ResolvePipeNames(Mode).Normal;

    internal static string SecretPipeName => AgentHostLayout.ResolvePipeNames(Mode).Secret;

    internal static IpcTrustModel PipeAccess => Mode == AgentHostMode.WindowsService
        ? IpcTrustModel.MachineService
        : IpcTrustModel.SameUser;

    /// <summary>True when the desktop owns the agent's lifetime and may start one.</summary>
    internal static bool DesktopStartsAgent => Mode != AgentHostMode.WindowsService;

    /// <summary>
    /// True when the agent should be stopped as the desktop closes, rather than left running for
    /// the rest of the session.
    /// </summary>
    internal static bool DesktopStopsAgent => Mode == AgentHostMode.AppSession;

    /// <summary>
    /// True only when a logon entry should exist. Deliberately narrower than
    /// <see cref="DesktopStartsAgent"/>: the desktop starts the agent in both session modes, but
    /// only one of them wants it started again at sign-in. Conflating the two made an update
    /// re-register the entry and silently move anyone on "only while StorageHub is open" back to
    /// starting at sign-in.
    /// </summary>
    internal static bool RegistersAutostart => Mode == AgentHostMode.UserSession;

    /// <summary>
    /// Forgets the cached mode, so the next connection re-reads it. Called after the mode is
    /// changed from Settings: the answer is otherwise fixed for the life of the process, and the
    /// desktop would keep talking to the agent it found at startup.
    /// </summary>
    internal static void Invalidate()
    {
        lock (Gate)
        {
            _mode = null;
        }
    }

    private static AgentHostMode ResolveMode()
    {
        if (!OperatingSystem.IsWindows())
        {
            return AgentHostMode.UserSession;
        }

        try
        {
            if (AgentServiceInstaller.Describe().Installed)
            {
                return AgentHostMode.WindowsService;
            }

            // No service. The logon entry is then the only durable difference between an agent
            // that outlives the app and one that does not, so it is read rather than stored: a
            // remembered preference could disagree with what Windows will actually do.
            return WindowsCurrentUserRunEntryStore.Exists(RunEntryName)
                ? AgentHostMode.UserSession
                : AgentHostMode.AppSession;
        }
        catch (Exception)
        {
            // Unable to ask the service control manager: assume the mode that needs no privileges
            // and no service, which is also how every installation before this behaved.
            return AgentHostMode.UserSession;
        }
    }
}
