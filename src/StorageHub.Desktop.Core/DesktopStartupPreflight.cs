using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>Why the desktop will not open, in words a user can act on.</summary>
/// <remarks>
/// It sat at the bottom of Program.cs beside the WinExe entry point, which is the only reason
/// it was Windows-only. Both shells have to explain the same four outcomes, and the words are
/// already translated.
/// </remarks>
internal static class DesktopStartupPreflight
{
    internal static string DescribeFailure(AgentEnsureStatus status) => status switch
    {
        AgentEnsureStatus.MissingExecutable => Ui.Dialogs.AgentMissingExecutable,
        AgentEnsureStatus.StartupTimedOut => Ui.Dialogs.AgentStartupTimedOut,
        AgentEnsureStatus.LaunchFailed => Ui.Dialogs.AgentLaunchFailed,
        _ => Ui.Dialogs.AgentNotReady
    };
}
