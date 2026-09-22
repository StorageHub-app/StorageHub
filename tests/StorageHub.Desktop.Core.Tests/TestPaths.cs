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
    /// A file in the repository, for the rare test whose subject is a file that is not compiled.
    /// </summary>
    /// <remarks>
    /// Found by walking up from the test binary to the solution rather than by counting
    /// directories, because the number of them between the two is a build-configuration detail and
    /// a test that hard-codes it breaks the first time anyone changes the output path.
    /// </remarks>
    internal static string RepositoryFile(string relative)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relative);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "StorageHub.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException(
                "The repository root could not be found from " + AppContext.BaseDirectory + ".");
        }

        return Path.Combine(
            directory.FullName,
            relative.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>
    /// <paramref name="relative"/> under that root, with either slash read as a separator so the
    /// call sites still read like the paths a user would type on Windows.
    /// </summary>
    /// <remarks>
    /// Both slashes, rather than only the backslash the call sites were written with: a sample
    /// built by interpolation is easy to write with a forward slash, and a path that came back
    /// half-converted failed in a way that read like the subject's fault rather than the test's.
    /// </remarks>
    internal static string Rooted(string relative) =>
        Path.Combine(
            Root,
            relative.Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar));
}
