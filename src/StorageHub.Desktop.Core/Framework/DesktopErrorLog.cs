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

    /// <summary>
    /// The file written to. Settable so a test run can send its entries somewhere of its own
    /// rather than into the log somebody reads about their real app.
    /// </summary>
    internal static string FilePath { get; set; } = Resolve();

    internal static void Write(string source, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        Append(source, error.ToString());
    }

    /// <summary>
    /// Records something that happened rather than something that threw, such as the agent being
    /// restarted or stopped and why. Without it a restart leaves nothing behind to explain it.
    /// </summary>
    internal static void Note(string source, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Append(source, message);
    }

    private static void Append(string source, string body)
    {
        var entry = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture))
            .Append(" [").Append(source).Append("] StorageHub ").Append(DesktopApplicationVersion.Current)
            .AppendLine()
            .AppendLine(body)
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
