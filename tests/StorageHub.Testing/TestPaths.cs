namespace StorageHub.Testing;

/// <summary>
/// Fully qualified local roots that the running platform accepts.
/// </summary>
/// <remarks>
/// <see cref="System.IO.Path.IsPathFullyQualified(string)"/> answers for the platform it runs on,
/// so a profile built around a literal <c>C:\Data</c> is rejected out of hand on Linux. The
/// production rule is already portable; only the test data was not. These are shapes, never
/// touched on disk - the suites that need a real directory create one under the temp path.
/// </remarks>
public static class TestPaths
{
    /// <summary>A local root for a profile under test.</summary>
    public static string LocalRoot { get; } =
        OperatingSystem.IsWindows() ? @"C:\Data" : "/data";

    /// <summary>A second local root, for the cases that need two that differ.</summary>
    public static string OtherLocalRoot { get; } =
        OperatingSystem.IsWindows() ? @"C:\Storage" : "/storage";
}
