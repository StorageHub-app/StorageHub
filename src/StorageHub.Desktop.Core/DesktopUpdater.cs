using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

internal enum DesktopUpdateState
{
    Idle,
    Disabled,
    Unavailable,
    Checking,
    UpdateAvailable,
    Downloading,
    ReadyToRestart,
    Installing,
    UpToDate,
    Failed
}

internal sealed record DesktopUpdateSnapshot(
    DesktopUpdateState State,
    string Message,
    string? Version = null,
    int? ProgressPercent = null)
{
    internal static DesktopUpdateSnapshot Initial { get; } =
        new(DesktopUpdateState.Idle, Ui.Validation.UpdatesIdle);
}

internal sealed record DesktopUpdateCandidate(string Version, object EngineValue);

internal interface IDesktopUpdateEngine
{
    bool IsInstalled { get; }

    string CurrentVersion { get; }

    Task<DesktopUpdateCandidate?> CheckForUpdatesAsync(CancellationToken cancellationToken);

    Task DownloadAsync(
        DesktopUpdateCandidate candidate,
        IProgress<int> progress,
        CancellationToken cancellationToken);

    void PrepareSilentApplyAndRestart(DesktopUpdateCandidate candidate);
}

internal interface IDesktopUpdateEngineFactory
{
    IDesktopUpdateEngine Create(bool includePrereleases);
}

/// <summary>
/// Checks for, downloads and installs a newer StorageHub, as the update settings allow.
/// </summary>
/// <remarks>
/// The settings are read from disk each time they are needed rather than held. The shell makes
/// one updater for the whole session and the Settings window saves through a store of its own, so
/// a copy taken at construction meant "Include release candidate builds" changed nothing until the
/// next start, and the update window went on naming the channel that had been in force then.
/// </remarks>
internal sealed class DesktopUpdater : IDisposable
{
    private readonly DesktopConfigStore _preferencesStore;
    private readonly IDesktopUpdateEngineFactory _engineFactory;
    private DesktopUpdatePreferences _preferences;
    private IDesktopUpdateEngine? _engine;
    private bool _engineIncludesPrereleases;
    private DesktopUpdateCandidate? _candidate;
    private bool _downloaded;
    private int _operationActive;
    private CancellationTokenSource? _automaticOperation;
    private bool _disposed;

    internal DesktopUpdater(
        DesktopConfigStore preferencesStore,
        IDesktopUpdateEngineFactory? engineFactory = null)
    {
        _preferencesStore = preferencesStore ?? throw new ArgumentNullException(nameof(preferencesStore));
        _engineFactory = engineFactory ?? new Updates.StorageHubUpdateEngineFactory();
        _preferences = preferencesStore.Load();
    }

    internal event EventHandler<DesktopUpdateSnapshot>? StatusChanged;

    internal event EventHandler? RestartRequested;

    internal DesktopUpdateSnapshot Snapshot { get; private set; } = DesktopUpdateSnapshot.Initial;

    /// <summary>The update settings as they stand on disk now.</summary>
    internal DesktopUpdatePreferences Preferences => ReadPreferences();

    internal void SavePreferences(DesktopUpdatePreferences preferences)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(preferences);
        var previous = ReadPreferences();
        FreshStore().Save(preferences);
        var sourceChanged = previous.IncludePrereleases != preferences.IncludePrereleases;
        _preferences = preferences;
        if (sourceChanged)
        {
            _engine = null;
            _candidate = null;
            _downloaded = false;
        }

        if (!preferences.CheckAutomatically || sourceChanged)
        {
            Volatile.Read(ref _automaticOperation)?.Cancel();
        }

        if (!preferences.CheckAutomatically)
        {
            Publish(new DesktopUpdateSnapshot(DesktopUpdateState.Disabled, Ui.Validation.UpdatesAutomaticChecksOff));
        }
    }

    internal async Task RunAutomaticAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReadPreferences().CheckAutomatically)
        {
            Publish(new DesktopUpdateSnapshot(DesktopUpdateState.Disabled, Ui.Validation.UpdatesAutomaticChecksOff));
            return;
        }

        using var automaticOperation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (Interlocked.CompareExchange(ref _automaticOperation, automaticOperation, null) is not null)
        {
            return;
        }

        try
        {
            var snapshot = await CheckForUpdatesAsync(automaticOperation.Token).ConfigureAwait(false);
            if (snapshot.State != DesktopUpdateState.UpdateAvailable || !ReadPreferences().DownloadAutomatically)
            {
                return;
            }

            snapshot = await DownloadAvailableAsync(automaticOperation.Token).ConfigureAwait(false);
            // Read again after the download, which can take minutes: a restart turned off while it
            // ran must not still happen.
            var preferences = ReadPreferences();
            if (!_disposed &&
                snapshot.State == DesktopUpdateState.ReadyToRestart &&
                preferences.CheckAutomatically &&
                preferences.DownloadAutomatically &&
                preferences.RestartAutomatically)
            {
                ApplyAndRestart();
            }
        }
        finally
        {
            _ = Interlocked.CompareExchange(ref _automaticOperation, null, automaticOperation);
        }
    }

    internal async Task<DesktopUpdateSnapshot> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _operationActive, 1, 0) != 0)
        {
            return Snapshot;
        }

        try
        {
            _candidate = null;
            _downloaded = false;
            // A channel changed in Settings since the engine was made means a different feed, so
            // the engine is made again rather than asked with the old one.
            var includePrereleases = ReadPreferences().IncludePrereleases;
            if (_engine is null || _engineIncludesPrereleases != includePrereleases)
            {
                _engine = _engineFactory.Create(includePrereleases);
                _engineIncludesPrereleases = includePrereleases;
            }

            if (!_engine.IsInstalled)
            {
                return Publish(new DesktopUpdateSnapshot(
                    DesktopUpdateState.Unavailable,
                    Ui.Validation.UpdatesInstallStorageHubToEnable));
            }

            Publish(new DesktopUpdateSnapshot(DesktopUpdateState.Checking, Ui.Validation.UpdatesCheckingGitHub));
            _candidate = await _engine.CheckForUpdatesAsync(cancellationToken).ConfigureAwait(false);
            return _candidate is null
                ? Publish(new DesktopUpdateSnapshot(
                    DesktopUpdateState.UpToDate,
                    $"Updates: current ({_engine.CurrentVersion})"))
                : Publish(new DesktopUpdateSnapshot(
                    DesktopUpdateState.UpdateAvailable,
                    $"Update {_candidate.Version} available",
                    _candidate.Version));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            _candidate = null;
            _downloaded = false;
            return Publish(new DesktopUpdateSnapshot(
                DesktopUpdateState.Failed,
                Ui.Validation.UpdatesCheckFailedTryAgainLater));
        }
        finally
        {
            Volatile.Write(ref _operationActive, 0);
        }
    }

    internal async Task<DesktopUpdateSnapshot> DownloadAvailableAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _operationActive, 1, 0) != 0)
        {
            return Snapshot;
        }

        try
        {
            if (_engine is null || _candidate is null)
            {
                return Snapshot;
            }

            var candidate = _candidate;
            Publish(new DesktopUpdateSnapshot(
                DesktopUpdateState.Downloading,
                $"Downloading update {candidate.Version}: 0%",
                candidate.Version,
                0));
            var progress = new InlineProgress<int>(value => Publish(new DesktopUpdateSnapshot(
                DesktopUpdateState.Downloading,
                $"Downloading update {candidate.Version}: {value}%",
                candidate.Version,
                value)));
            await _engine.DownloadAsync(candidate, progress, cancellationToken).ConfigureAwait(false);
            if (!ReferenceEquals(candidate, _candidate))
            {
                throw new InvalidOperationException(Ui.Validation.TheSelectedUpdateChangedWhileDownloading);
            }

            _downloaded = true;
            return Publish(new DesktopUpdateSnapshot(
                DesktopUpdateState.ReadyToRestart,
                $"Update {candidate.Version} ready — restart to install",
                candidate.Version,
                100));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            _downloaded = false;
            return Publish(new DesktopUpdateSnapshot(
                DesktopUpdateState.Failed,
                Ui.Validation.UpdatesDownloadFailedTryAgainLater));
        }
        finally
        {
            Volatile.Write(ref _operationActive, 0);
        }
    }

    internal bool ApplyAndRestart()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var engine = _engine;
        var candidate = _candidate;
        if (!_downloaded || engine is null || candidate is null ||
            Snapshot.State != DesktopUpdateState.ReadyToRestart)
        {
            return false;
        }

        _downloaded = false;
        Publish(new DesktopUpdateSnapshot(
            DesktopUpdateState.Installing,
            $"Update {candidate.Version} will install after StorageHub closes",
            candidate.Version,
            100));
        try
        {
            engine.PrepareSilentApplyAndRestart(candidate);
            RestartRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception)
        {
            Publish(new DesktopUpdateSnapshot(
                DesktopUpdateState.Failed,
                Ui.Validation.UpdatesCouldNotStartTheInstaller));
            return false;
        }
    }

    /// <summary>
    /// A store over the same directory that has not read anything yet, since a store answers from
    /// its first read for as long as it lives.
    /// </summary>
    private DesktopConfigStore FreshStore() => new(_preferencesStore.Directory);

    /// <summary>
    /// The settings on disk, or the last ones read when the files cannot be read just now.
    /// </summary>
    private DesktopUpdatePreferences ReadPreferences()
    {
        try
        {
            return _preferences = FreshStore().Load();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return _preferences;
        }
    }

    private DesktopUpdateSnapshot Publish(DesktopUpdateSnapshot snapshot)
    {
        Snapshot = snapshot;
        StatusChanged?.Invoke(this, snapshot);
        return snapshot;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Volatile.Read(ref _automaticOperation)?.Cancel();
        GC.SuppressFinalize(this);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
