using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StorageHub.Desktop.Updates;

/// <summary>One release as GitHub's API lists it, with only the fields the updater reads.</summary>
internal sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string? TagName,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("prerelease")] bool Prerelease,
    [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
    [property: JsonPropertyName("assets")] IReadOnlyList<GitHubReleaseAsset>? Assets);

/// <summary>One file attached to a release.</summary>
internal sealed record GitHubReleaseAsset(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("browser_download_url")] string? DownloadUrl);

/// <summary>
/// The releases StorageHub publishes on GitHub, read as the update feed.
/// </summary>
/// <remarks>
/// <para>
/// Where 1.4's Velopack engine looked, and where CI publishes every release: the fixed public
/// repository, a <c>v</c>-prefixed tag per version, a flat set of assets, and a SHA256SUMS that
/// covers every one of them. A separate feed document would be one more thing to publish and one
/// more place for a release to disagree with itself, so the release is read as it stands.
/// </para>
/// <para>
/// What is offered is the newest release newer than the running one that carries this platform's
/// package and a SHA256SUMS naming it: <c>StorageHub-{version}-win-x64.msi</c> on Windows, the
/// <c>_amd64.deb</c> on Linux. A release candidate is only considered by somebody who asked for
/// them, and a draft never. The package must be downloaded from this repository's own release
/// downloads, so a listing that pointed anywhere else names nothing worth installing.
/// </para>
/// </remarks>
internal static class GitHubReleaseFeed
{
    /// <summary>The releases, newest first. Thirty is far more than are published between updates.</summary>
    internal const string ReleasesUrl =
        "https://api.github.com/repos/StorageHub-app/StorageHub/releases?per_page=30";

    /// <summary>Where every asset of every release is downloaded from.</summary>
    internal const string DownloadPrefix = StorageHubLinks.Releases + "/download/";

    internal const string ChecksumsAssetName = "SHA256SUMS";

    /// <summary>Longest listing this will read: thirty releases with their notes come to far less.</summary>
    internal const int MaximumListingBytes = 4 * 1024 * 1024;

    /// <summary>Longest SHA256SUMS this will read; a release has a dozen lines.</summary>
    internal const int MaximumChecksumsBytes = 64 * 1024;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The newest release this copy should be offered, as a manifest with the one package it can
    /// apply, or null when there is none.
    /// </summary>
    /// <exception cref="HttpRequestException">GitHub could not be reached or refused.</exception>
    /// <exception cref="InvalidDataException">
    /// The listing or the release's SHA256SUMS could not be read, or does not name the package.
    /// Thrown rather than read as "up to date", so a broken release says the check failed.
    /// </exception>
    internal static async Task<UpdateManifest?> FindNewerAsync(
        HttpClient client,
        UpdateFeedOptions options,
        UpdatePackageKind kind,
        string architecture,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        var listing = await ReadBoundedAsync(client, options.FeedUrl, MaximumListingBytes, cancellationToken)
            .ConfigureAwait(false);
        var releases = ParseReleases(listing)
            ?? throw new InvalidDataException("The release listing could not be read.");

        var candidate = ChooseNewer(releases, options, kind, architecture);
        if (candidate is null) return null;

        var (version, release, package, checksums) = candidate.Value;
        var sums = await ReadBoundedAsync(
                client, checksums.DownloadUrl!, MaximumChecksumsBytes, cancellationToken)
            .ConfigureAwait(false);
        var digest = ChecksumFor(Encoding.UTF8.GetString(sums), package.Name!)
            ?? throw new InvalidDataException("The release's SHA256SUMS does not name its package.");

        var manifest = new UpdateManifest(
            UpdateManifest.CurrentSchemaVersion,
            version,
            release.PublishedAt ?? DateTimeOffset.MinValue,
            null,
            [new UpdatePackage(kind, architecture, package.DownloadUrl!, digest, package.Size)]);
        return manifest.Validate() is { } error ? throw new InvalidDataException(error) : manifest;
    }

    /// <summary>Reads the listing, or null when it is not one.</summary>
    internal static IReadOnlyList<GitHubRelease>? ParseReleases(ReadOnlySpan<byte> payload)
    {
        try
        {
            return JsonSerializer.Deserialize<List<GitHubRelease>>(payload, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The newest release newer than the running one with a package for this machine, the package,
    /// and the SHA256SUMS to check it against.
    /// </summary>
    internal static (string Version, GitHubRelease Release, GitHubReleaseAsset Package, GitHubReleaseAsset Checksums)?
        ChooseNewer(
            IEnumerable<GitHubRelease> releases,
            UpdateFeedOptions options,
            UpdatePackageKind kind,
            string architecture)
    {
        ArgumentNullException.ThrowIfNull(releases);
        ArgumentNullException.ThrowIfNull(options);

        (string Version, GitHubRelease Release, GitHubReleaseAsset Package, GitHubReleaseAsset Checksums)? best = null;
        foreach (var release in releases)
        {
            if (release is null || release.Draft || release.Assets is null) continue;

            var version = release.TagName is ['v' or 'V', .. var rest] ? rest : release.TagName;
            if (!SemanticRelease.IsWellFormed(version)) continue;

            // A pre-release is only offered to somebody who asked for them, whichever way GitHub
            // or the version says it is one.
            var isPrerelease = release.Prerelease || version!.Contains('-', StringComparison.Ordinal);
            if (isPrerelease && !options.IncludePrereleases) continue;

            if (!SemanticRelease.IsNewer(version, options.CurrentVersion)) continue;
            if (best is { } current && !SemanticRelease.IsNewer(version, current.Version)) continue;

            var package = release.Assets.FirstOrDefault(asset =>
                IsOurDownload(asset) && IsPackageFor(asset.Name!, version!, kind, architecture));
            var checksums = release.Assets.FirstOrDefault(asset =>
                IsOurDownload(asset) && string.Equals(asset.Name, ChecksumsAssetName, StringComparison.Ordinal));
            if (package is null || checksums is null) continue;

            best = (version!, release, package, checksums);
        }

        return best;
    }

    /// <summary>The SHA-256 SHA256SUMS gives for a file, lower-case, or null when it names none.</summary>
    /// <remarks>
    /// The <c>sha256sum</c> format both packaging scripts write: the digest, two spaces (or a space
    /// and the binary marker), and the name. A name listed twice is refused rather than guessed at.
    /// </remarks>
    internal static string? ChecksumFor(string sums, string fileName)
    {
        ArgumentNullException.ThrowIfNull(sums);
        string? found = null;
        foreach (var rawLine in sums.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length < 66 || line[64] != ' ') continue;
            var name = line[65..].TrimStart('*', ' ');
            if (!string.Equals(name, fileName, StringComparison.Ordinal)) continue;
            if (found is not null) return null;
            found = line[..64].ToLowerInvariant();
        }

        return found;
    }

    /// <summary>
    /// Whether an asset is downloaded from this repository's releases, over HTTPS.
    /// </summary>
    /// <remarks>
    /// GitHub serves the file from a storage host after a redirect, which is fine: what matters is
    /// that the address the listing names is the repository's own, so a listing that named a
    /// look-alike elsewhere offers nothing.
    /// </remarks>
    private static bool IsOurDownload(GitHubReleaseAsset? asset) =>
        asset is { Name.Length: > 0, DownloadUrl: { } url } &&
        url.StartsWith(DownloadPrefix, StringComparison.Ordinal) &&
        !url.Contains("..", StringComparison.Ordinal);

    /// <summary>Whether an asset is the package this machine applies, by the names the scripts give them.</summary>
    private static bool IsPackageFor(string name, string version, UpdatePackageKind kind, string architecture)
    {
        // Only x64 is published, on either system.
        if (!string.Equals(architecture, nameof(System.Runtime.InteropServices.Architecture.X64), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return kind switch
        {
            UpdatePackageKind.Msi => string.Equals(name, $"StorageHub-{version}-win-x64.msi", StringComparison.Ordinal),
            UpdatePackageKind.Deb => name.StartsWith("storagehub_", StringComparison.Ordinal) &&
                name.EndsWith("_amd64.deb", StringComparison.Ordinal),
            _ => false
        };
    }

    /// <summary>
    /// Fetches a document no longer than <paramref name="maximumBytes"/>, as GitHub's API asks to
    /// be called: with a User-Agent, which it refuses requests without, and its own media type.
    /// </summary>
    private static async Task<byte[]> ReadBoundedAsync(
        HttpClient client,
        string url,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("StorageHub", "2"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

        using var response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        _ = response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException("The release listing is larger than StorageHub will read.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0) return buffer.ToArray();
            if (buffer.Length + read > maximumBytes)
            {
                throw new InvalidDataException("The release listing is larger than StorageHub will read.");
            }

            buffer.Write(chunk, 0, read);
        }
    }
}
