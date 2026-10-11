namespace StorageHub.Desktop;

/// <summary>
/// The addresses StorageHub points people at.
/// </summary>
/// <remarks>
/// Gathered here because they were constants on the update engine, which is the wrong owner: the
/// project URL is what About shows and what Settings links to, and neither has anything to do with
/// how updates are applied. Keeping them together also makes it obvious that every one is HTTPS.
/// </remarks>
internal static class StorageHubLinks
{
    internal const string Project = "https://github.com/StorageHub-app/StorageHub";

    internal const string Releases = Project + "/releases";

    internal const string Issues = Project + "/issues";

    /// <summary>
    /// Help, Documentation: the project's README, which is where StorageHub is documented. 1.4
    /// declared the entry and gave it nowhere to go.
    /// </summary>
    internal const string Documentation = Project + "#readme";

    /// <summary>The repository's bug report form, which Help, Report issue opens.</summary>
    internal const string BugReport = Issues + "/new?template=bug_report.yml";

    /// <summary>
    /// The bug report form with its version and environment boxes already filled in.
    /// </summary>
    /// <remarks>
    /// GitHub fills an issue form's fields from query parameters named by the fields' ids. Only
    /// what the form asks for and nothing that names anybody: the version, the operating system,
    /// the processor, the runtime and whether the agent answered -- no user or machine name, no
    /// path, no connection. The form itself warns about the rest.
    /// </remarks>
    internal static Uri BugReportFor(string version, string environment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(environment);
        return new Uri(
            BugReport +
            "&version=" + Uri.EscapeDataString(version) +
            "&environment=" + Uri.EscapeDataString(environment));
    }
}
