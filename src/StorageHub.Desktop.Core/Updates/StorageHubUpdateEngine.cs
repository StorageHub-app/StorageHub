using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Updates;

/// <summary>Where releases are published, and what the running build is.</summary>
/// <remarks>
/// Both are here so a test can name its own feed and version without a network or an installed
/// copy. The default feed is the only URL the updater will read, which is what makes HTTPS on the
/// package URLs meaningful: a feed served from anywhere else could name any package it liked.
/// </remarks>
internal sealed record UpdateFeedOptions(
    string FeedUrl,
    string CurrentVersion,
    bool IncludePrereleases)
{
    internal const string DefaultFeedUrl = "https://storagehub.app/releases/storagehub-update.json";

    internal static UpdateFeedOptions Default(bool includePrereleases) => new(
        DefaultFeedUrl,
        DesktopApplicationVersion.Current,
        includePrereleases);
}

/// <summary>
/// StorageHub's own updater: check, download, verify, hand to the platform installer.
/// </summary>
/// <remarks>
/// Replaces the Velopack engine. The reason is control rather than dislike: StorageHub installs
/// through an MSI and a .deb now, and an updater that swaps files underneath a packaged
/// installation leaves the package manager describing a version that is no longer there. Fetching
/// the platform's own package and letting the platform apply it keeps the two in step, and puts the
/// check, the release notes and the progress under this application's control so they can look the
/// same on both systems.
/// </remarks>
internal sealed class StorageHubUpdateEngine : IDesktopUpdateEngine, IDisposable
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(20);

    private readonly UpdateFeedOptions _options;
    private readonly HttpClient _client;
    private readonly IUpdateInstaller? _installer;
    private readonly string _downloadDirectory;
    private readonly bool _ownsClient;
    private string? _downloadedPackage;

    internal StorageHubUpdateEngine(
        UpdateFeedOptions options,
        HttpClient? client = null,
        IUpdateInstaller? installer = null,
        string? downloadDirectory = null)
    {
        _options = options;
        _ownsClient = client is null;
        _client = client ?? new HttpClient { Timeout = CheckTimeout };
        _installer = installer ?? UpdateInstallers.ForCurrentPlatform();
        _downloadDirectory = downloadDirectory ??
            Path.Combine(Path.GetTempPath(), "storagehub-updates");
    }

    /// <summary>
    /// Whether this copy can be updated in place.
    /// </summary>
    /// <remarks>
    /// False on a platform with no packaging story, which is every one but Windows and Linux. It is
    /// not a check for "installed" in the Velopack sense: a .deb-installed copy and one unpacked by
    /// hand look identical from here, and the package manager will refuse the second on its own
    /// terms rather than needing to be pre-empted.
    /// </remarks>
    public bool IsInstalled => _installer is not null;

    public string CurrentVersion => _options.CurrentVersion;

    public async Task<DesktopUpdateCandidate?> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        if (_installer is null) return null;

        using var response = await _client
            .GetAsync(_options.FeedUrl, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (!UpdateManifest.TryParse(payload, out var manifest, out _)) return null;

        if (!SemanticRelease.IsNewer(manifest!.Version, _options.CurrentVersion)) return null;

        // A pre-release is only offered to somebody who asked for them.
        if (!_options.IncludePrereleases && manifest.Version.Contains('-', StringComparison.Ordinal))
        {
            return null;
        }

        var package = manifest.PackageFor(_installer.Kind, _installer.Architecture);
        return package is null ? null : new DesktopUpdateCandidate(manifest.Version, package);
    }

    public async Task DownloadAsync(
        DesktopUpdateCandidate candidate,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var package = candidate.EngineValue as UpdatePackage
            ?? throw new InvalidOperationException("The update candidate did not come from this engine.");

        var result = await UpdateDownload
            .FetchAsync(_client, package, _downloadDirectory, progress, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            // Thrown rather than returned because DesktopUpdater treats a failed download as an
            // exception path, and the message is the one worth showing.
            throw new InvalidOperationException(result.Message ?? Ui.Validation.UpdatesDownloadFailedTryAgainLater);
        }

        _downloadedPackage = result.Path;
    }

    /// <summary>
    /// Starts the platform installer, after which this process is expected to close.
    /// </summary>
    /// <remarks>
    /// Named for the contract it implements rather than for what it does: nothing here is silent,
    /// because the platform installer shows its own progress and asks for elevation in its own
    /// dialog. Drawing that prompt inside StorageHub would be both a lie and the exact shape of a
    /// phishing attempt.
    /// </remarks>
    public void PrepareSilentApplyAndRestart(DesktopUpdateCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (_installer is null || _downloadedPackage is null)
        {
            throw new InvalidOperationException("There is no downloaded update to apply.");
        }

        if (!_installer.Launch(_downloadedPackage))
        {
            throw new InvalidOperationException(Ui.Validation.UpdatesCouldNotStartTheInstaller);
        }
    }

    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
    }
}

/// <summary>Builds the engine the desktop uses.</summary>
internal sealed class StorageHubUpdateEngineFactory : IDesktopUpdateEngineFactory
{
    public IDesktopUpdateEngine Create(bool includePrereleases) =>
        new StorageHubUpdateEngine(UpdateFeedOptions.Default(includePrereleases));
}
