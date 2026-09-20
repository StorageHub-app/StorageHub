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
}
