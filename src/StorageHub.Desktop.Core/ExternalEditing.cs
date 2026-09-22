using System.Diagnostics;
using System.Security.Cryptography;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>What somebody decided when warned that an edit cannot be checked for conflicts.</summary>
internal readonly record struct UnsafeExternalEditDecision(bool Continue, bool DontShowAgain);

/// <summary>What to do with a file that changed in the editor.</summary>
internal enum EditedFileChoice
{
    /// <summary>Upload it now.</summary>
    Upload,

    /// <summary>Not this change; ask again the next time it is saved.</summary>
    NotNow,

    /// <summary>Stop watching the file altogether.</summary>
    StopWatching
}

/// <summary>
/// The questions external editing asks, and the one kind of thing it says.
/// </summary>
/// <remarks>
/// An interface because the controller runs off the UI thread -- a file watcher raises wherever it
/// likes -- and because 1.x called MessageBox.Show from inside the controller, which is why none of
/// its behaviour could be tested. The shell's implementation puts each question on the UI thread.
/// </remarks>
internal interface IExternalEditPrompts
{
    Task<UnsafeExternalEditDecision> WarnUnsafeAsync(string fileName);

    Task<EditedFileChoice> ConfirmUploadAsync(string fileName);

    Task ShowAsync(string message);
}

/// <summary>Starts an editor on a file.</summary>
internal interface IEditorLauncher
{
    /// <param name="editorPath">A configured editor, or null for the system's own choice.</param>
    void Launch(string? editorPath, string file, string workingDirectory);
}

/// <summary>
/// Starts the configured editor, or asks the system to open the file with whatever it opens it with.
/// </summary>
/// <remarks>
/// With no editor configured, shell execution is what hands the file to the default app -- on
/// Windows through the file association, on Linux through xdg-open, which is what .NET runs for it.
/// The editor's own path is started directly with the file as its one argument, so a name with
/// spaces or quotes in it arrives as one argument rather than being re-parsed by a shell.
/// </remarks>
internal sealed class ProcessEditorLauncher : IEditorLauncher
{
    public void Launch(string? editorPath, string file, string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        var useDefault = string.IsNullOrWhiteSpace(editorPath);
        var start = new ProcessStartInfo
        {
            FileName = useDefault ? file : editorPath!,
            UseShellExecute = useDefault,
            WorkingDirectory = workingDirectory
        };
        if (!useDefault) start.ArgumentList.Add(file);

        try
        {
            using var process = Process.Start(start);
            if (process is null && !useDefault)
            {
                throw new InvalidOperationException(Ui.Validation.TheConfiguredEditorCouldNotStart);
            }
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            throw new InvalidOperationException(
                $"{Ui.Validation.TheConfiguredEditorCouldNotStart} {error.Message}", error);
        }
    }
}

/// <summary>
/// Where edit sessions live, and how each is made private to the person editing.
/// </summary>
/// <remarks>
/// <para>
/// The one part of external editing that is genuinely different per platform. 1.x put sessions in
/// %TEMP% and closed each directory to the current Windows account with an ACL. /tmp is shared
/// between every account on a Linux machine, so there the sessions go under XDG_RUNTIME_DIR, which
/// is the account's own and already private, or under ~/.cache when there is none -- and every
/// level created is created 0700, then checked for its owner, its mode, and not being a link.
/// </para>
/// <para>
/// Private matters because the file is the remote file's content, and the upload trusts whatever is
/// in it: another account able to write there could have its content uploaded under this one's
/// connection.
/// </para>
/// </remarks>
internal static class EditSessionDirectory
{
    /// <summary>How long a session directory is left before it is assumed abandoned.</summary>
    internal static readonly TimeSpan AbandonedAfter = TimeSpan.FromDays(1);

    internal static string DefaultRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Path.GetTempPath(), "StorageHub", "EditSessions");
        }

        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(runtime) && Path.IsPathFullyQualified(runtime))
        {
            return Path.Combine(runtime, "storagehub", "edit-sessions");
        }

        var cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (string.IsNullOrWhiteSpace(cache) || !Path.IsPathFullyQualified(cache))
        {
            cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        }

        return Path.Combine(cache, "storagehub", "edit-sessions");
    }

    /// <summary>Creates a session directory that only this account can read or write.</summary>
    internal static void CreatePrivate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (OperatingSystem.IsLinux())
        {
            StorageHub.Infrastructure.Unix.UnixFileSystem.EnsurePrivateDirectory(path);
            return;
        }

        var directory = Directory.CreateDirectory(path);
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(Ui.Validation.TheExternalEditorTemporaryDirectoryCannotBe);
        }

        if (OperatingSystem.IsWindows()) RestrictToCurrentUser(directory);
    }

    /// <summary>
    /// Removes sessions nobody has touched for a day.
    /// </summary>
    /// <remarks>
    /// A session is deleted when the shell closes, but an editor may still hold its file then, and
    /// a shell that crashed deletes nothing. Links are left alone rather than followed.
    /// </remarks>
    internal static void Scavenge(string root, DateTime utcNow)
    {
        try
        {
            if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            {
                return;
            }

            foreach (var path in Directory.EnumerateDirectories(root))
            {
                try
                {
                    var directory = new DirectoryInfo(path);
                    if ((directory.Attributes & FileAttributes.ReparsePoint) == 0 &&
                        directory.LastWriteTimeUtc < utcNow - AbandonedAfter)
                    {
                        directory.Delete(recursive: true);
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // Still in use, or not ours to delete. Tomorrow's scavenge will try again.
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// The name the file is saved under locally, which cannot leave its session directory.
    /// </summary>
    /// <remarks>
    /// The name comes from the remote listing, and an object store will happily hold a key called
    /// "../../.bashrc". 1.x combined it with the session directory as it stood, so such a name
    /// wrote outside the directory it was meant to be confined to. Only the last segment is kept,
    /// and anything this platform cannot put in a file name becomes an underscore.
    /// </remarks>
    internal static string LocalName(string remoteName)
    {
        var name = (remoteName ?? string.Empty).Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        var invalid = Path.GetInvalidFileNameChars();
        name = new string([.. name.Select(character => invalid.Contains(character) ? '_' : character)]).Trim();
        return name is "" or "." or ".." ? "edited-file" : name;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void RestrictToCurrentUser(DirectoryInfo directory)
    {
        var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException(Ui.Validation.TheCurrentWindowsUserIdentityIsUnavailable);
        var security = new System.Security.AccessControl.DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            currentUser,
            System.Security.AccessControl.FileSystemRights.FullControl,
            System.Security.AccessControl.InheritanceFlags.ContainerInherit |
                System.Security.AccessControl.InheritanceFlags.ObjectInherit,
            System.Security.AccessControl.PropagationFlags.None,
            System.Security.AccessControl.AccessControlType.Allow));
        directory.SetAccessControl(security);
    }
}

/// <summary>
/// Opens a remote file in an editor, and offers to upload it each time it is saved.
/// </summary>
/// <remarks>
/// <para>
/// Ported from the WinForms ExternalEditorController, which the plan described as mostly portable
/// once its prompts were behind an interface; they are <see cref="IExternalEditPrompts"/> now. The
/// flow is 1.x's: download within the configured size limit, warn when the provider cannot hold the
/// upload to the version that was downloaded, save to a private directory, start the editor, watch
/// the file, and ask before each upload.
/// </para>
/// <para>
/// The agent client is made per call rather than held. 1.x held one and shared it between every
/// open session, and a client is strictly correlated: two uploads saved at once on one pipe could
/// have their responses crossed.
/// </para>
/// </remarks>
internal sealed class ExternalEditController : IAsyncDisposable
{
    private readonly DesktopConfigStore _preferences;
    private readonly Func<IObjectInspectorAgentClient> _clients;
    private readonly IExternalEditPrompts _prompts;
    private readonly IEditorLauncher _launcher;
    private readonly string _root;
    private readonly TimeSpan _debounce;
    private readonly List<ExternalEditSession> _sessions = [];
    private readonly Lock _gate = new();
    private bool _disposed;

    internal ExternalEditController(
        DesktopConfigStore preferences,
        Func<IObjectInspectorAgentClient> clients,
        IExternalEditPrompts prompts,
        IEditorLauncher? launcher = null,
        string? sessionRoot = null,
        TimeSpan? debounce = null)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _prompts = prompts ?? throw new ArgumentNullException(nameof(prompts));
        _launcher = launcher ?? new ProcessEditorLauncher();
        _root = sessionRoot ?? EditSessionDirectory.DefaultRoot();
        _debounce = debounce ?? TimeSpan.FromMilliseconds(900);
        EditSessionDirectory.Scavenge(_root, DateTime.UtcNow);
    }

    /// <summary>Raised after an edited file has been uploaded, so the shell can reload the pane.</summary>
    internal event EventHandler? FileUploaded;

    /// <summary>The sessions still being watched.</summary>
    internal IReadOnlyList<ExternalEditSession> Sessions
    {
        get
        {
            lock (_gate) return [.. _sessions];
        }
    }

    /// <summary>
    /// Downloads a file, opens it in the editor, and starts watching it.
    /// </summary>
    /// <returns>The session, or null when the file was not opened -- and then it has said why.</returns>
    internal async Task<ExternalEditSession?> OpenAsync(
        ObjectInspectorAddress address,
        string fileName,
        long? capturedLength,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(address);

        var preferences = _preferences.Load();
        var maximumBytes = Math.Clamp(
            preferences.MaximumEditableFileBytes, 1, EditableFileIpcContract.MaximumContentBytes);
        if (capturedLength is > 0 && capturedLength > maximumBytes)
        {
            await _prompts.ShowAsync(
                Ui.Format(Ui.Dialogs.FileTooLargeToEditFormat, fileName, maximumBytes / 1024)).ConfigureAwait(false);
            return null;
        }

        var response = await DownloadAsync(address, fileName, maximumBytes, preferences, cancellationToken)
            .ConfigureAwait(false);
        if (response is null) return null;

        var directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        EditSessionDirectory.CreatePrivate(directory);
        var localPath = Path.Combine(directory, EditSessionDirectory.LocalName(fileName));
        await WritePrivateAsync(localPath, response.Content, cancellationToken).ConfigureAwait(false);

        var session = new ExternalEditSession(
            this, directory, localPath, response.Address, response.ContentType, maximumBytes,
            response.Content, _debounce);
        lock (_gate) _sessions.Add(session);
        try
        {
            session.StartWatching();
            _launcher.Launch(preferences.ExternalEditorPath, localPath, directory);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Downloads the file within the limit, asking first if its upload cannot be checked.
    /// </summary>
    /// <remarks>
    /// A version or entity tag is what lets the agent refuse an upload over somebody else's newer
    /// write. A provider that cannot condition a read on one says so as Unsupported; the file can
    /// still be edited, but a later upload could overwrite a change nobody saw, and that is asked
    /// about rather than done quietly.
    /// </remarks>
    internal async Task<EditableFileDownloadResponse?> DownloadAsync(
        ObjectInspectorAddress address,
        string fileName,
        int maximumBytes,
        DesktopUpdatePreferences preferences,
        CancellationToken cancellationToken)
    {
        var response = await RequestAsync(address, maximumBytes, cancellationToken).ConfigureAwait(false);
        var unprotected = response.Failure is { Category: StorageIpcFailureCategory.Unsupported } &&
            (address.VersionId is not null || address.EntityTag is not null);
        if (!unprotected)
        {
            if (response.Failure is null) return response;
            await _prompts.ShowAsync(response.Failure.Message).ConfigureAwait(false);
            return null;
        }

        var decision = preferences.WarnBeforeUnsafeExternalEdit
            ? await _prompts.WarnUnsafeAsync(fileName).ConfigureAwait(false)
            : new UnsafeExternalEditDecision(Continue: true, DontShowAgain: false);
        if (!decision.Continue) return null;

        if (decision.DontShowAgain)
        {
            try
            {
                _preferences.Save(preferences with { WarnBeforeUnsafeExternalEdit = false });
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException)
            {
                await _prompts.ShowAsync(Ui.Validation.StorageHubCouldNotSaveThatPreferenceSo).ConfigureAwait(false);
            }
        }

        response = await RequestAsync(address with { VersionId = null, EntityTag = null }, maximumBytes, cancellationToken)
            .ConfigureAwait(false);
        if (response.Failure is null) return response;
        await _prompts.ShowAsync(response.Failure.Message).ConfigureAwait(false);
        return null;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        foreach (var session in Sessions) session.Dispose();
        return ValueTask.CompletedTask;
    }

    internal IExternalEditPrompts Prompts => _prompts;

    internal async Task<EditableFileUploadResponse> UploadAsync(
        ObjectInspectorAddress address, byte[] content, string? contentType)
    {
        await using var client = _clients();
        return await client.UploadEditedFileAsync(
            new EditableFileUploadRequest(EditableFileIpcContract.CurrentVersion, address, content, contentType))
            .ConfigureAwait(false);
    }

    internal void Uploaded() => FileUploaded?.Invoke(this, EventArgs.Empty);

    internal void Closed(ExternalEditSession session)
    {
        lock (_gate) _sessions.Remove(session);
    }

    private async Task<EditableFileDownloadResponse> RequestAsync(
        ObjectInspectorAddress address, int maximumBytes, CancellationToken cancellationToken)
    {
        await using var client = _clients();
        return await client.DownloadEditableFileAsync(
            new EditableFileDownloadRequest(EditableFileIpcContract.CurrentVersion, address, maximumBytes),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes the downloaded content where only this account can read it.</summary>
    private static async Task WritePrivateAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.WriteThrough
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        await using var stream = new FileStream(path, options);
        await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// One file open in an editor: watched, and uploaded when it changes and somebody says so.
/// </summary>
/// <remarks>
/// Editors save in bursts -- a temporary file, a rename, a second write -- so a change is acted on
/// only once the file has been quiet for a moment. Checks never overlap: a save during an upload is
/// picked up by the check after it rather than racing it.
/// </remarks>
internal sealed class ExternalEditSession : IDisposable
{
    private readonly ExternalEditController _owner;
    private readonly string _directory;
    private readonly string? _contentType;
    private readonly int _maximumBytes;
    private readonly TimeSpan _debounce;
    private readonly FileSystemWatcher _watcher;
    private readonly SemaphoreSlim _checking = new(1, 1);
    private readonly Lock _gate = new();
    private CancellationTokenSource? _pending;
    private ObjectInspectorAddress _address;
    private byte[] _observedHash;
    private bool _disposed;

    internal ExternalEditSession(
        ExternalEditController owner,
        string directory,
        string localPath,
        ObjectInspectorAddress address,
        string? contentType,
        int maximumBytes,
        byte[] originalContent,
        TimeSpan debounce)
    {
        _owner = owner;
        _directory = directory;
        LocalPath = localPath;
        _address = address;
        _contentType = contentType;
        _maximumBytes = maximumBytes;
        _debounce = debounce;
        _observedHash = SHA256.HashData(originalContent);
        _watcher = new FileSystemWatcher(directory)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnRenamed;
    }

    /// <summary>Where the file was saved for the editor.</summary>
    internal string LocalPath { get; }

    /// <summary>What an upload is conditioned on, which moves on after each one.</summary>
    internal ObjectInspectorAddress Address => _address;

    internal bool IsDisposed => _disposed;

    internal void StartWatching() => _watcher.EnableRaisingEvents = true;

    /// <summary>
    /// Looks at the file now: if it changed, asks whether to upload it, and does.
    /// </summary>
    /// <remarks>
    /// Called by the watcher once the file has been quiet, and directly by a test.
    /// </remarks>
    internal async Task CheckAsync()
    {
        if (_disposed) return;
        await _checking.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(LocalPath)) return;

            var file = new FileInfo(LocalPath);
            if (file.LinkTarget is not null || (file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                // A link could point anywhere the editor's account can read; uploading through it
                // would send that file to the remote connection instead.
                await _owner.Prompts.ShowAsync(Ui.Validation.TheEditorReplacedTheTemporaryFileWith).ConfigureAwait(false);
                Dispose();
                return;
            }

            if (file.Length > _maximumBytes)
            {
                await _owner.Prompts.ShowAsync(
                    Ui.Format(Ui.Dialogs.ExternalEditorTooLargeFormat, _maximumBytes / 1024)).ConfigureAwait(false);
                return;
            }

            var content = await File.ReadAllBytesAsync(LocalPath).ConfigureAwait(false);
            var hash = SHA256.HashData(content);
            if (CryptographicOperations.FixedTimeEquals(hash, _observedHash)) return;

            switch (await _owner.Prompts.ConfirmUploadAsync(Path.GetFileName(LocalPath)).ConfigureAwait(false))
            {
                case EditedFileChoice.StopWatching:
                    Dispose();
                    return;
                case EditedFileChoice.NotNow:
                    _observedHash = hash;
                    return;
            }

            var response = await _owner.UploadAsync(_address, content, _contentType).ConfigureAwait(false);
            if (response.Failure is not null)
            {
                // The hash is not moved on, so the next save offers the upload again.
                await _owner.Prompts.ShowAsync(response.Failure.Message).ConfigureAwait(false);
                return;
            }

            _address = response.Address;
            _observedHash = hash;
            _owner.Uploaded();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or
            InvalidOperationException or TimeoutException or System.Text.Json.JsonException)
        {
            await _owner.Prompts.ShowAsync(
                Ui.Format(Ui.Dialogs.ExternalEditorProcessFailedFormat, error.Message)).ConfigureAwait(false);
        }
        finally
        {
            _checking.Release();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _pending?.Cancel();
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnChanged;
        _watcher.Created -= OnChanged;
        _watcher.Deleted -= OnChanged;
        _watcher.Renamed -= OnRenamed;
        _watcher.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // An editor may still hold the file. The scavenge removes the directory later.
        }

        _owner.Closed(this);
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (IsTarget(e.FullPath)) Settle();
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (IsTarget(e.FullPath) || IsTarget(e.OldFullPath)) Settle();
    }

    private bool IsTarget(string path) => string.Equals(
        Path.GetFullPath(path),
        LocalPath,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>Checks once the file has been quiet for the debounce interval.</summary>
    private void Settle()
    {
        CancellationTokenSource next;
        lock (_gate)
        {
            if (_disposed) return;
            _pending?.Cancel();
            _pending = next = new CancellationTokenSource();
        }

        _ = SettleAsync(next.Token);
    }

    private async Task SettleAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_debounce, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await CheckAsync().ConfigureAwait(false);
    }
}
