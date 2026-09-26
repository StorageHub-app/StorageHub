using System.Globalization;
using System.Text;

namespace StorageHub.Desktop.Framework;

/// <summary>
/// Where the desktop writes what went wrong: <c>logs/desktop.log</c> under its data folder.
/// </summary>
/// <remarks>
/// 2.0 logged to the debugger only, so a release build that failed left nothing behind to read.
/// This is the floor: every unhandled exception, with the time and the build, appended to one file
/// that is trimmed when it grows past a megabyte. It never throws -- a logger that can fail is one
/// more way for the thing it is reporting to get worse.
/// </remarks>
internal static class DesktopErrorLog
{
    private const long MaximumBytes = 1024 * 1024;
    private static readonly Lock Gate = new();

    internal static string FilePath { get; } = Resolve();

    internal static void Write(string source, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var entry = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture))
            .Append(" [").Append(source).Append("] StorageHub ").Append(DesktopApplicationVersion.Current)
            .AppendLine()
            .AppendLine(error.ToString())
            .AppendLine()
            .ToString();

        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaximumBytes)
                {
                    File.Move(FilePath, FilePath + ".1", overwrite: true);
                }

                File.AppendAllText(FilePath, entry, Encoding.UTF8);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // Nowhere to write. The error is still shown on screen by whoever called this.
            }
        }
    }

    private static string Resolve()
    {
        try
        {
            return Path.Combine(DesktopFrameworkPaths.Resolve().ApplicationRoot, "logs", "desktop.log");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return Path.Combine(Path.GetTempPath(), "StorageHub", "desktop.log");
        }
    }
}
