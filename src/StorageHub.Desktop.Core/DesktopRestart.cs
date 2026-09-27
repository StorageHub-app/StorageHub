using System.Diagnostics;

namespace StorageHub.Desktop;

/// <summary>
/// A restart the shell asks for on its way out.
/// </summary>
/// <remarks>
/// Changing the language means rebuilding every window, because each one reads its text when it is
/// constructed. Relaunching is the honest way to do that, but it has to happen after the message
/// loop has ended: starting the replacement from inside a modal dialog leaves two shells briefly
/// alive, both reading and writing the same settings file, and the one that exits last wins.
///
/// So the request is only recorded here. <c>Program.Main</c> acts on it once
/// <c>Application.Run</c> has returned and this process owns nothing.
///
/// The background agent is deliberately left alone. It holds the transfer queue, the sync
/// schedules and the SSH sessions, none of which care which language the shell speaks, and
/// stopping it would turn a two-second bounce into a visible interruption of running work.
/// </remarks>
internal static class DesktopRestart
{
    internal static bool Requested { get; private set; }

    internal static void Request() => Requested = true;

    /// <summary>Exists so a test can run without leaving the flag set for the next one.</summary>
    internal static void Reset() => Requested = false;

    /// <summary>
    /// Starts the replacement shell. Called only after the message loop has ended.
    /// </summary>
    /// <remarks>
    /// Relaunched without arguments on purpose. The only arguments this executable accepts are
    /// <c>--agent-only</c> and the framework's own, and both are handled before a window exists and
    /// exit the process — so carrying them through would restart into something that is not a
    /// shell. The one exception is a launch through the dotnet host, which needs the application
    /// named to start anything at all. Environment variables are inherited, which is what keeps a
    /// development launch under STORAGEHUB_DATA_ROOT pointing at the same data root.
    /// </remarks>
    /// <returns>Whether a replacement shell was started.</returns>
    internal static bool TryStart()
    {
        if (!Requested)
        {
            return false;
        }

        Requested = false;
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            return false;
        }

        try
        {
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory
            };
            if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase) &&
                System.Reflection.Assembly.GetEntryAssembly()?.Location is { Length: > 0 } application)
            {
                start.ArgumentList.Add(application);
            }

            using var started = Process.Start(start);
            return started is not null;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or
            InvalidOperationException or
            System.IO.FileNotFoundException)
        {
            // The settings are already saved, so the language still changes at the next launch.
            // Failing to relaunch is a worse start than a missing restart, and there is no window
            // left to report it to.
            return false;
        }
    }
}
