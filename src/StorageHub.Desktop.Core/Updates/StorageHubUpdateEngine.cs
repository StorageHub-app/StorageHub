using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Updates;

/// <summary>Where releases are published, and what the running build is.</summary>
/// <remarks>
/// Both are here so a test can name its own feed and version without a network or an installed
/// copy. The default feed is the repository's own release listing on GitHub, the only one the
/// updater will read, and <see cref="GitHubReleaseFeed"/> only takes a package from that
/// repository's release downloads: a listing served from anywhere else could name any package it
/// liked.
/// </remarks>
internal sealed record UpdateFeedOptions(
    string FeedUrl,
    string CurrentVersion,
    bool IncludePrereleases)
{
    internal const string DefaultFeedUrl = GitHubReleaseFeed.ReleasesUrl;

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
///
/// The release is found where CI publishes it, on GitHub (<see cref="GitHubReleaseFeed"/>), and the
/// package is checked against the release's SHA256SUMS and the size GitHub lists for it before
/// anything runs it. Nothing is installed while StorageHub is open: the installer is started once
/// the shell has closed (<see cref="DesktopUpdateInstall"/>), as 1.4's Velopack waited for the
/// process to exit before it applied anything.
/// </remarks>
internal sealed class StorageHubUpdateEngine : IDesktopUpdateEngine, IDisposable
{
    /// <summary>
    /// How long a check may take. Not the client's timeout: that one would also cut off the
    /// download, which is a hundred-odd megabytes and takes as long as it takes.
    /// </summary>
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
        _client = client ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _installer = installer ?? UpdateInstallers.ForCurrentPlatform();
        _downloadDirectory = downloadDirectory ??
            Path.Combine(Path.GetTempPath(), "storagehub-updates");
    }

    /// <summary>
    /// Whether this copy can be updated in place.
    /// </summary>
    /// <remarks>
    /// False on a platform with no packaging story, which is every one but Windows and Linux, and
    /// on Windows for any copy the MSI did not install, such as a build run from source: installing
    /// the MSI would update a different copy from the one that asked. A .deb-installed copy and one
    /// unpacked by hand look identical from here, and the package manager will refuse the second on
    /// its own terms rather than needing to be pre-empted.
    /// </remarks>
    public bool IsInstalled => _installer is { ManagesThisCopy: true };

    public string CurrentVersion => _options.CurrentVersion;

    public async Task<DesktopUpdateCandidate?> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        if (_installer is null) return null;

        // A GitHub that cannot be reached, or a release that does not hold together, throws: the
        // updater says the check failed, rather than that StorageHub is up to date.
        using var check = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        check.CancelAfter(CheckTimeout);
        UpdateManifest? manifest;
        try
        {
            manifest = await GitHubReleaseFeed
                .FindNewerAsync(_client, _options, _installer.Kind, _installer.Architecture, check.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("GitHub did not answer in time.");
        }

        var package = manifest?.PackageFor(_installer.Kind, _installer.Architecture);
        return package is null ? null : new DesktopUpdateCandidate(manifest!.Version, package);
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
    /// Has the platform installer start once StorageHub has closed, after which the shell is
    /// expected to close.
    /// </summary>
    /// <remarks>
    /// Named for the contract it implements rather than for what it does: nothing here is silent,
    /// because the platform installer shows its own progress and asks for elevation, where it needs
    /// it, in its own dialog. Drawing that prompt inside StorageHub would be both a lie and the
    /// exact shape of a phishing attempt.
    ///
    /// Started after the close rather than now, so the installer never meets a running shell's
    /// files, and a close cancelled at "save changes?" leaves the update to install when StorageHub
    /// does close, as the status bar then says.
    /// </remarks>
    public void PrepareSilentApplyAndRestart(DesktopUpdateCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var installer = _installer;
        var package = _downloadedPackage;
        if (installer is null || package is null || !File.Exists(package))
        {
            throw new InvalidOperationException("There is no downloaded update to apply.");
        }

        DesktopUpdateInstall.Stage(() => installer.Launch(package));
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
