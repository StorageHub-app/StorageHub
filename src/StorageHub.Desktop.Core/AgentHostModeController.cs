using StorageHub.Agent;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>What changing how the agent runs did.</summary>
internal sealed record AgentModeChange(bool Succeeded, string Message);

/// <summary>
/// Moves the agent between starting at sign-in and running only while StorageHub is open.
/// </summary>
/// <remarks>
/// <para>
/// 1.4's AgentHostModeController, less the Windows service it also installed and removed with an
/// elevated helper, which 2.0 dropped (AgentHostMode says why). What is left is its session half:
/// the two modes differ only by the sign-in registration, so this adds or removes it and then asks
/// the machine which mode it is in, since policy or the environment switch can refuse it.
/// </para>
/// <para>
/// Windows only, as in 1.4. On Linux the registration is the user's systemd unit, which the .deb
/// installs and leaves to each user to enable, and the agent platform reads the mode from a unit
/// file this would have to write rather than from what systemd has enabled.
/// </para>
/// </remarks>
internal static class AgentHostModeController
{
    internal static AgentModeChange Apply(AgentHostMode desired)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new AgentModeChange(false, Ui.Settings.AgentModeChangeFailed);
        }

        try
        {
            using var lifecycle = WindowsDesktopLifecycle.Create();

            // Forced: without it the lifecycle asks the current mode whether an entry should exist,
            // and "only while StorageHub is open" answers no to the very change being asked for.
            _ = desired == AgentHostMode.UserSession
                ? lifecycle.ConfigureAutostart(force: true)
                : lifecycle.RemoveAutostart();

            // Re-read rather than assume: claiming a mode Windows will not honour would be worse
            // than reporting the failure.
            DesktopAgentHost.Invalidate();
            return DesktopAgentHost.Mode == desired
                ? new AgentModeChange(true, Ui.Settings.AgentModeApplied)
                : new AgentModeChange(false, Ui.Settings.AgentModeChangeFailed);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or
            IOException or UnauthorizedAccessException)
        {
            return new AgentModeChange(false, Ui.Settings.AgentModeChangeFailed);
        }
    }
}

/// <summary>
/// Remembers whether the one-time "How should StorageHub run?" question has been asked.
/// </summary>
/// <remarks>
/// Only the fact that it was asked is stored, as in 1.4. The mode itself is not, because it would
/// only ever disagree with the machine: whether the sign-in registration exists is the truth, and a
/// stored preference would go on claiming a mode after the entry was removed outside the app.
/// </remarks>
internal static class AgentHostModePrompt
{
    private const string FileName = "agent-host-mode-asked";

    internal static string ResolvePath(string desktopDataRoot) =>
        Path.Combine(
            string.IsNullOrWhiteSpace(desktopDataRoot)
                ? throw new ArgumentException("A desktop data root is required.", nameof(desktopDataRoot))
                : desktopDataRoot,
            FileName);

    /// <summary>
    /// Whether to ask now: on Windows, the first time, and only from an installed build.
    /// </summary>
    /// <remarks>
    /// A build run from source has no packaged agent beside it, so a sign-in entry would start
    /// nothing, and its answer would stop the installed build from ever asking. It is left
    /// unasked and unmarked. Linux is skipped for the reason <see cref="AgentHostModeController"/>
    /// gives.
    /// </remarks>
    internal static bool ShouldAsk(string desktopDataRoot)
    {
        if (!OperatingSystem.IsWindows() || AlreadyAsked(desktopDataRoot))
        {
            return false;
        }

        try
        {
            using var lifecycle = WindowsDesktopLifecycle.Create();
            return File.Exists(lifecycle.AgentExecutablePath);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    internal static bool AlreadyAsked(string desktopDataRoot)
    {
        try
        {
            return File.Exists(ResolvePath(desktopDataRoot));
        }
        catch (Exception error) when (error is
            IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or PathTooLongException)
        {
            // An unreadable or malformed marker path must not block startup; the worst case is
            // asking the question once more.
            return false;
        }
    }

    /// <summary>
    /// Records the question as asked. Called before it is shown, so a crash or a forced close
    /// cannot turn a one-time question into one that returns at every launch.
    /// </summary>
    internal static void MarkAsked(string desktopDataRoot)
    {
        try
        {
            var path = ResolvePath(desktopDataRoot);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "asked");
        }
        catch (Exception error) when (error is
            IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Failing to record the answer is not worth interrupting startup over.
        }
    }
}
