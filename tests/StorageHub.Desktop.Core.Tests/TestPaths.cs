namespace StorageHub.Desktop.Tests;

/// <summary>
/// Fully qualified sample paths, spelled the way the system running the test spells them.
/// </summary>
/// <remarks>
/// <para>
/// These suites were written as <c>C:\work\a.shw</c> while they could only ever run on Windows.
/// What they exercise is validation that asks <see cref="Path.IsPathFullyQualified"/>, and that is
/// the BCL's platform rule rather than a pattern: on Linux a drive letter is an ordinary filename,
/// so every one of those samples is a relative path and gets rejected. The tests then fail for a
/// reason that has nothing to do with what they check - twenty-two of them did, the first time
/// this project ran on Ubuntu.
/// </para>
/// <para>
/// Nothing here touches the filesystem, and deliberately so: the code under test is pure path
/// logic, and a test that needed a real directory would be testing something else.
/// </para>
/// </remarks>
internal static class TestPaths
{
    /// <summary>Where a fully qualified path starts on this system.</summary>
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\" : "/";

    /// <summary>
    /// <paramref name="relative"/> under that root, with backslashes read as separators so the
    /// call sites still read like the paths a user would type on Windows.
    /// </summary>
    internal static string Rooted(string relative) =>
        Path.Combine(Root, relative.Replace('\\', Path.DirectorySeparatorChar));
}
