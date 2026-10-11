using System.Runtime.CompilerServices;

namespace StorageHub.Desktop.Tests;

internal static class ErrorLogLocation
{
    /// <summary>
    /// Sends what these tests make the desktop log to a file of their own. The lifecycle tests
    /// write the agent's start and stop decisions, and wrote them into the user's desktop.log, the
    /// file they read to find out what happened to the real agent.
    /// </summary>
    [ModuleInitializer]
    internal static void KeepTheErrorLogOutOfTheUsersData() =>
        Framework.DesktopErrorLog.FilePath =
            Path.Combine(Path.GetTempPath(), "StorageHub.Desktop.Core.Tests", "logs", "desktop.log");
}
