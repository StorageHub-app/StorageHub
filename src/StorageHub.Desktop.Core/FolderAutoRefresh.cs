namespace StorageHub.Desktop;

/// <summary>How a pane can learn that the folder it shows has changed.</summary>
/// <param name="LocalFolder">A folder on this computer, which the file system reports changes to.</param>
/// <param name="Polled">A connection's folder, which nothing reports on and has to be read again.</param>
internal readonly record struct PaneFolderChanges(string? LocalFolder, bool Polled)
{
    internal static PaneFolderChanges None => default;

    internal static PaneFolderChanges Watched(string folder) => new(folder, false);

    internal static PaneFolderChanges Polling => new(null, true);
}

/// <summary>What Settings says about refreshing folders when they change.</summary>
/// <param name="Enabled">Off by default: a pane reads its folder when asked, as it always has.</param>
/// <param name="RemoteInterval">How often a connection's folder is read again; bounded.</param>
internal readonly record struct FolderAutoRefreshSettings(bool Enabled, TimeSpan RemoteInterval)
{
    /// <summary>Fast enough to notice a colleague's upload, slow enough to be no load on a server.</summary>
    internal const int DefaultRemoteSeconds = 30;

    /// <summary>Below this a listing every few seconds per pane starts to be a load on the server.</summary>
    internal const int MinimumRemoteSeconds = 10;

    /// <summary>Above this the folder is as good as not refreshed at all.</summary>
    internal const int MaximumRemoteSeconds = 600;

    internal static FolderAutoRefreshSettings Off => new(false, TimeSpan.FromSeconds(DefaultRemoteSeconds));

    internal static FolderAutoRefreshSettings From(bool enabled, int remoteSeconds) => new(
        enabled,
        TimeSpan.FromSeconds(Math.Clamp(remoteSeconds, MinimumRemoteSeconds, MaximumRemoteSeconds)));

    internal static bool IsValidRemoteSeconds(int seconds) => seconds is >= MinimumRemoteSeconds and <= MaximumRemoteSeconds;
}

/// <summary>
/// Decides when a pane reads its folder again by itself: soon after a folder on this computer
/// reports a change, and every so often for a connection's.
/// </summary>
/// <remarks>
/// <para>
/// Driven by a tick the pane gives it, on the pane's own thread, and it only answers "now" or "not
/// yet": the pane does the reading, quietly, keeping the filter, selection and scroll, and does
/// not when it is busy with something else -- in which case the change is still owed and is read
/// on a later tick. Nothing is lost by a busy pane; it is only later.
/// </para>
/// <para>
/// A folder on this computer is watched rather than read again on a timer: the file system says
/// when something changed, and a copy that writes a file in many pieces is a burst of reports,
/// so a change is acted on once the folder has been quiet for <see cref="Settle"/> -- or after
/// <see cref="LongestWait"/> regardless, so a folder that never stops changing is still shown.
/// A watcher that cannot be made (a folder gone, a share that does not report) leaves that folder
/// as it was before this setting existed.
/// </para>
/// <para>
/// A connection has nothing to report changes with -- the library's own watch polls for every
/// provider but Local, and an agent-side watch per pane would hold a session open for each --
/// so its folder is read again every <see cref="FolderAutoRefreshSettings.RemoteInterval"/>,
/// the same quiet re-read the shell already does while transfers run.
/// </para>
/// </remarks>
internal sealed class FolderAutoRefresh : IDisposable
{
    /// <summary>How long a watched folder has to be quiet before it is read again.</summary>
    internal static readonly TimeSpan Settle = TimeSpan.FromSeconds(1);

    /// <summary>How long a folder that keeps changing waits at most before it is read again.</summary>
    internal static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(5);

    private readonly Func<string, Action, IDisposable?> _watch;
    private readonly object _gate = new();
    private IDisposable? _watcher;
    private PaneFolderChanges _following;
    private FolderAutoRefreshSettings _settings = FolderAutoRefreshSettings.Off;
    private DateTimeOffset? _firstChange;
    private DateTimeOffset? _lastChange;
    private DateTimeOffset _lastRead;
    private Func<DateTimeOffset> _clock = static () => DateTimeOffset.UtcNow;

    /// <param name="watch">
    /// Starts watching a folder on this computer, calling the action on any change from any
    /// thread, or returns null where it cannot. <see cref="LocalFolderWatch.Start"/> in the
    /// application.
    /// </param>
    internal FolderAutoRefresh(Func<string, Action, IDisposable?> watch)
    {
        _watch = watch ?? throw new ArgumentNullException(nameof(watch));
    }

    /// <summary>Whether a folder on this computer is being watched now.</summary>
    internal bool IsWatching
    {
        get
        {
            lock (_gate) return _watcher is not null;
        }
    }

    /// <summary>
    /// Follows what the pane shows now under the settings now. Following the same folder again
    /// changes nothing, so a pane can call this after every listing.
    /// </summary>
    internal void Follow(PaneFolderChanges changes, FolderAutoRefreshSettings settings, Func<DateTimeOffset> clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var target = settings.Enabled ? changes : PaneFolderChanges.None;
        IDisposable? retired = null;
        lock (_gate)
        {
            _clock = clock;
            _settings = settings;
            if (target == _following) return;

            retired = _watcher;
            _watcher = null;
            _following = target;
            _firstChange = null;
            _lastChange = null;
            _lastRead = clock();
        }

        retired?.Dispose();
        if (target.LocalFolder is not { } folder) return;

        var watcher = _watch(folder, Changed);
        lock (_gate)
        {
            // Followed somewhere else while the watcher was being made: it is for nothing now.
            if (_following == target && _watcher is null)
            {
                _watcher = watcher;
                watcher = null;
            }
        }

        watcher?.Dispose();
    }

    /// <summary>The folder changed: from the watcher, on its thread.</summary>
    internal void Changed()
    {
        lock (_gate)
        {
            if (_following.LocalFolder is null) return;
            var now = _clock();
            _firstChange ??= now;
            _lastChange = now;
        }
    }

    /// <summary>Whether the folder should be read again now.</summary>
    internal bool Due(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_following.Polled) return now - _lastRead >= _settings.RemoteInterval;
            return _lastChange is { } last && _firstChange is { } first &&
                (now - last >= Settle || now - first >= LongestWait);
        }
    }

    /// <summary>The folder was read again: what changed until now is shown.</summary>
    internal void Read(DateTimeOffset now)
    {
        lock (_gate)
        {
            _lastRead = now;

            // A change reported while the read was on its way may not be in it, but the next
            // report after it will be; only what came before this read is settled by it.
            if (_lastChange is { } last && last <= now)
            {
                _firstChange = null;
                _lastChange = null;
            }
        }
    }

    public void Dispose()
    {
        IDisposable? retired;
        lock (_gate)
        {
            retired = _watcher;
            _watcher = null;
            _following = PaneFolderChanges.None;
        }

        retired?.Dispose();
    }
}

/// <summary>A folder on this computer, watched for what is added, removed, renamed or written in it.</summary>
internal static class LocalFolderWatch
{
    /// <summary>
    /// Watches the folder itself, not below it -- the pane shows one folder -- or returns null
    /// where the folder cannot be watched: gone, refused, or the system out of watches.
    /// </summary>
    internal static IDisposable? Start(string folder, Action changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                    NotifyFilters.LastWrite | NotifyFilters.Size
            };
            watcher.Created += (_, _) => changed();
            watcher.Deleted += (_, _) => changed();
            watcher.Changed += (_, _) => changed();
            watcher.Renamed += (_, _) => changed();

            // Too much at once to report one by one: the folder is read again all the same.
            watcher.Error += (_, _) => changed();
            watcher.EnableRaisingEvents = true;
            var started = watcher;
            watcher = null;
            return started;
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException
            or PlatformNotSupportedException)
        {
            return null;
        }
        finally
        {
            watcher?.Dispose();
        }
    }
}
