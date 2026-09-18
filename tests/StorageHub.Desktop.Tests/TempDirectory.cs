namespace StorageHub.Desktop.Tests;

/// <summary>
/// Cleanup shared by the tests that give a settings store a scratch directory of its own.
/// </summary>
/// <remarks>
/// A store writes three files rather than one, so the <c>File.Delete</c> these tests used to end
/// with no longer describes what has to be cleaned up.
/// </remarks>
internal static class TempDirectoryCleanup
{
    internal static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A scratch directory under the temp path that outlives the run is the operating
            // system's problem, not a test failure.
        }
    }
}
