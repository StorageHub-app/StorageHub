namespace StorageHub.Desktop;

/// <summary>
/// An update the shell installs on its way out.
/// </summary>
/// <remarks>
/// 1.4's Velopack staged an update and applied it once the process had exited. The MSI is applied
/// the same way: "Restart and install" closes the shell, and the installer is only started once
/// <c>Application.Run</c> has returned, as <see cref="DesktopRestart"/> starts a new shell, so it
/// never meets this one's files in use. The MSI stops the agent itself, and reopens StorageHub when
/// it is done.
///
/// A close cancelled at "save changes?" leaves it staged, so the update installs when StorageHub
/// does close, which is what the status bar says by then.
/// </remarks>
internal static class DesktopUpdateInstall
{
    private static Func<bool>? _launch;

    internal static bool Staged => Volatile.Read(ref _launch) is not null;

    /// <summary>Records how to start the installer. The latest staged replaces any before it.</summary>
    internal static void Stage(Func<bool> launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        Volatile.Write(ref _launch, launch);
    }

    /// <summary>Withdraws a staged installer. For tests, so as not to leave one for the next.</summary>
    internal static void Reset() => Volatile.Write(ref _launch, null);

    /// <summary>
    /// Starts the staged installer. Called only after the message loop has ended.
    /// </summary>
    /// <returns>Whether an installer was started.</returns>
    internal static bool TryStart()
    {
        var launch = Interlocked.Exchange(ref _launch, null);
        if (launch is null) return false;

        try
        {
            return launch();
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or
            InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            // There is no window left to say so. The download is verified again at the next check,
            // which offers the same update.
            return false;
        }
    }
}
