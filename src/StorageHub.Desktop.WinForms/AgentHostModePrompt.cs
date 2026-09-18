using StorageHub.Agent;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// Remembers whether the one-time "how should StorageHub run?" question has been asked.
///
/// Only the fact that it was asked is stored. The mode itself is not, because it would only ever
/// disagree with reality: whether the service exists is the truth, and a service removed by an
/// administrator outside the app would leave a stored preference quietly lying about what is
/// running. Everything else is read from the service control manager each time.
/// </summary>
internal static class AgentHostModePrompt
{
    private const string FileName = "agent-host-mode-asked";

    internal static string ResolvePath(string desktopDataRoot) =>
        Path.Combine(
            string.IsNullOrWhiteSpace(desktopDataRoot)
                ? throw new ArgumentException("A desktop data root is required.", nameof(desktopDataRoot))
                : desktopDataRoot,
            FileName);

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

    /// <summary>
    /// Asks the question once, if it has not been asked and the machine is not already running a
    /// service. Returns the mode the operator chose, or null when nothing should change.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static AgentHostMode? AskOnce(IWin32Window? owner, string desktopDataRoot)
    {
        if (AlreadyAsked(desktopDataRoot) || AgentServiceInstaller.Describe().Installed)
        {
            return null;
        }

        MarkAsked(desktopDataRoot);
        using var setup = new AgentHostModeSetupForm();
        if (setup.ShowDialog(owner) != DialogResult.OK)
        {
            return null;
        }

        // Null means "leave things as they are". Sign-in is already how the app behaves, so only a
        // real change is reported back for applying.
        return setup.SelectedMode == AgentHostMode.UserSession ? null : setup.SelectedMode;
    }
}
