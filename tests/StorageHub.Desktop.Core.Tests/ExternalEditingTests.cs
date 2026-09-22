using System.Text;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Editing a remote file in an external editor: the download, the private copy, and the upload.
/// </summary>
/// <remarks>
/// 1.x called MessageBox.Show from inside this controller and so none of it had a test. The prompts
/// and the editor launch are behind interfaces now; everything else here is real, including the
/// files and, in one test, the file watcher.
/// </remarks>
public sealed class ExternalEditingTests : IDisposable
{
    private static readonly ObjectInspectorAddress Address =
        new(Guid.NewGuid(), "root", "docs/readme.txt", VersionId: "v1");

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"storagehub-edit-{Guid.NewGuid():N}");

    private readonly StubInspector _agent = new();
    private readonly StubPrompts _prompts = new();
    private readonly StubLauncher _launcher = new();

    public ExternalEditingTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task OpeningSavesTheFileAndStartsTheEditor()
    {
        _agent.Download = Content("hello");
        var editor = TestPaths.Rooted("tools/editor");
        await using var editing = Controller(editor: editor);

        var session = await editing.OpenAsync(Address, "readme.txt", 5);

        Assert.NotNull(session);
        Assert.Equal("hello", File.ReadAllText(session!.LocalPath));
        Assert.Equal("readme.txt", Path.GetFileName(session.LocalPath));
        Assert.Equal((editor, session.LocalPath), (_launcher.Editor, _launcher.File));
        Assert.Single(editing.Sessions);
    }

    /// <summary>A file known to be over the limit is refused before anything is downloaded.</summary>
    [Fact]
    public async Task AFileOverTheLimitIsRefusedBeforeItIsDownloaded()
    {
        await using var editing = Controller(maximumBytes: 1024);

        var session = await editing.OpenAsync(Address, "big.bin", 4096);

        Assert.Null(session);
        Assert.Equal(0, _agent.Downloads);
        Assert.Equal(Ui.Format(Ui.Dialogs.FileTooLargeToEditFormat, "big.bin", 1), Assert.Single(_prompts.Messages));
    }

    [Fact]
    public async Task ADownloadFailureIsSaidAndNothingIsOpened()
    {
        _agent.Download = Failed<EditableFileDownloadResponse>("The object is gone.", StorageIpcFailureCategory.NotFound);
        await using var editing = Controller();

        Assert.Null(await editing.OpenAsync(Address, "readme.txt", 5));

        Assert.Equal("The object is gone.", Assert.Single(_prompts.Messages));
        Assert.Null(_launcher.File);
    }

    /// <summary>
    /// A provider that cannot hold an upload to the downloaded version is warned about, and
    /// declining the warning opens nothing.
    /// </summary>
    [Fact]
    public async Task AnUncheckableEditIsWarnedAboutFirst()
    {
        _agent.Download = Failed<EditableFileDownloadResponse>("No conditional reads.", StorageIpcFailureCategory.Unsupported);
        _prompts.Unsafe = new UnsafeExternalEditDecision(Continue: false, DontShowAgain: false);
        await using var editing = Controller();

        Assert.Null(await editing.OpenAsync(Address, "readme.txt", 5));

        Assert.Equal(["readme.txt"], _prompts.Warned);
        Assert.Equal(1, _agent.Downloads);
    }

    /// <summary>
    /// Going ahead downloads again without the version, and "don't show again" is remembered.
    /// </summary>
    [Fact]
    public async Task GoingAheadDownloadsUnconditionallyAndCanStopTheWarning()
    {
        var first = true;
        _agent.DownloadFor = request =>
        {
            if (!first) return Content("unguarded") with { Address = request.Address };
            first = false;
            return Failed<EditableFileDownloadResponse>("No conditional reads.", StorageIpcFailureCategory.Unsupported);
        };
        _prompts.Unsafe = new UnsafeExternalEditDecision(Continue: true, DontShowAgain: true);
        await using var editing = Controller();

        var session = await editing.OpenAsync(Address, "readme.txt", 5);

        Assert.NotNull(session);
        Assert.Null(_agent.LastDownload!.Address.VersionId);
        Assert.False(Store().Load().WarnBeforeUnsafeExternalEdit);
    }

    [Fact]
    public async Task WithTheWarningOffNoOneIsAsked()
    {
        var first = true;
        _agent.DownloadFor = request =>
        {
            if (!first) return Content("unguarded");
            first = false;
            return Failed<EditableFileDownloadResponse>("No conditional reads.", StorageIpcFailureCategory.Unsupported);
        };
        Store().Save(Store().Load() with { WarnBeforeUnsafeExternalEdit = false });
        await using var editing = Controller();

        Assert.NotNull(await editing.OpenAsync(Address, "readme.txt", 5));
        Assert.Empty(_prompts.Warned);
    }

    /// <summary>
    /// The local name cannot leave its session directory.
    /// </summary>
    /// <remarks>
    /// The name comes from a remote listing, and 1.x joined it to the directory as it stood: an
    /// object called "../../.bashrc" was written outside the directory meant to confine it.
    /// </remarks>
    [Theory]
    [InlineData("../../.bashrc", ".bashrc")]
    [InlineData("folder/sub/readme.txt", "readme.txt")]
    [InlineData(@"..\..\evil.txt", "evil.txt")]
    [InlineData("..", "edited-file")]
    [InlineData("", "edited-file")]
    [InlineData("notes.md", "notes.md")]
    public void TheLocalNameStaysInItsDirectory(string remote, string expected)
    {
        Assert.Equal(expected, EditSessionDirectory.LocalName(remote));
    }

    [Fact]
    public async Task ATraversingNameIsWrittenInsideTheSession()
    {
        _agent.Download = Content("x");
        await using var editing = Controller();

        var session = await editing.OpenAsync(Address, "../../escape.txt", 1);

        var sessionDirectory = Path.GetDirectoryName(session!.LocalPath)!;
        Assert.StartsWith(Path.Combine(_directory, "sessions"), sessionDirectory, StringComparison.Ordinal);
        Assert.Equal("escape.txt", Path.GetFileName(session.LocalPath));
    }

    [Fact]
    public async Task AnUnchangedFileIsNotOffered()
    {
        var session = await OpenAsync("hello");

        await session.CheckAsync();

        Assert.Empty(_prompts.Confirmed);
        Assert.Equal(0, _agent.Uploads);
    }

    /// <summary>A saved change is offered, uploaded, and the next upload is held to the new version.</summary>
    [Fact]
    public async Task ASavedChangeIsUploadedAndTheVersionMovesOn()
    {
        var session = await OpenAsync("hello");
        await using var editing = _last!;
        var uploaded = 0;
        editing.FileUploaded += (_, _) => uploaded++;
        var next = Address with { VersionId = "v2" };
        _agent.Upload = new EditableFileUploadResponse(EditableFileIpcContract.CurrentVersion, next, 7, null);

        File.WriteAllText(session.LocalPath, "goodbye");
        await session.CheckAsync();

        Assert.Equal(["readme.txt"], _prompts.Confirmed);
        Assert.Equal("goodbye", Encoding.UTF8.GetString(_agent.LastUpload!.Content));
        Assert.Equal("v1", _agent.LastUpload.Address.VersionId);
        Assert.Equal(next, session.Address);
        Assert.Equal(1, uploaded);

        // Saved again unchanged: nothing more to offer.
        await session.CheckAsync();
        Assert.Single(_prompts.Confirmed);
    }

    /// <summary>"Not now" skips this change, and the next save is asked about again.</summary>
    [Fact]
    public async Task NotNowSkipsThisChangeOnly()
    {
        var session = await OpenAsync("hello");
        _prompts.Choice = EditedFileChoice.NotNow;

        File.WriteAllText(session.LocalPath, "draft");
        await session.CheckAsync();
        await session.CheckAsync();

        Assert.Single(_prompts.Confirmed);
        Assert.Equal(0, _agent.Uploads);

        File.WriteAllText(session.LocalPath, "draft two");
        await session.CheckAsync();
        Assert.Equal(2, _prompts.Confirmed.Count);
    }

    [Fact]
    public async Task StoppingEndsTheSessionAndRemovesTheCopy()
    {
        var session = await OpenAsync("hello");
        _prompts.Choice = EditedFileChoice.StopWatching;

        File.WriteAllText(session.LocalPath, "changed");
        await session.CheckAsync();

        Assert.True(session.IsDisposed);
        Assert.False(File.Exists(session.LocalPath));
        Assert.Empty(_last!.Sessions);
    }

    /// <summary>A failed upload is said, and the same change is offered again on the next save.</summary>
    [Fact]
    public async Task AFailedUploadIsOfferedAgain()
    {
        var session = await OpenAsync("hello");
        _agent.Upload = Failed<EditableFileUploadResponse>("Somebody else changed it.", StorageIpcFailureCategory.Conflict);

        File.WriteAllText(session.LocalPath, "mine");
        await session.CheckAsync();
        Assert.Equal("Somebody else changed it.", Assert.Single(_prompts.Messages));

        await session.CheckAsync();
        Assert.Equal(2, _prompts.Confirmed.Count);
    }

    [Fact]
    public async Task AnEditThatGrewPastTheLimitIsNotUploaded()
    {
        var session = await OpenAsync("hello", maximumBytes: 1024);

        File.WriteAllText(session.LocalPath, new string('x', 2048));
        await session.CheckAsync();

        Assert.Empty(_prompts.Confirmed);
        Assert.Equal(Ui.Format(Ui.Dialogs.ExternalEditorTooLargeFormat, 1), Assert.Single(_prompts.Messages));
    }

    /// <summary>
    /// The real watcher, end to end: saving the file brings the question.
    /// </summary>
    /// <remarks>
    /// Every other test calls the check directly. This is the one that proves a save is noticed at
    /// all, on whichever platform runs it -- inotify on Linux and ReadDirectoryChangesW on Windows
    /// do not report the same events for the same write.
    /// </remarks>
    [Fact]
    public async Task SavingTheFileBringsTheQuestion()
    {
        var session = await OpenAsync("hello", debounce: TimeSpan.FromMilliseconds(50));
        var asked = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _prompts.OnConfirm = name => asked.TrySetResult(name);

        File.WriteAllText(session.LocalPath, "saved by an editor");

        var name = await asked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("readme.txt", name);
    }

    /// <summary>
    /// On Linux the session is 0700 and its file 0600: /tmp is shared, and the copy is what gets
    /// uploaded under this account's connection.
    /// </summary>
    [Fact]
    public async Task OnLinuxTheCopyIsPrivate()
    {
        if (!OperatingSystem.IsLinux()) return;

        var session = await OpenAsync("secret");

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(Path.GetDirectoryName(session.LocalPath)!));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(session.LocalPath));
    }

    /// <summary>An editor that swapped the file for a link has its session stopped, not uploaded through.</summary>
    [Fact]
    public async Task AFileReplacedByALinkIsNotUploaded()
    {
        if (!OperatingSystem.IsLinux()) return;

        var session = await OpenAsync("hello");
        var elsewhere = Path.Combine(_directory, "elsewhere.txt");
        File.WriteAllText(elsewhere, "not the file");
        File.Delete(session.LocalPath);
        File.CreateSymbolicLink(session.LocalPath, elsewhere);

        await session.CheckAsync();

        Assert.Equal(0, _agent.Uploads);
        Assert.Equal(Ui.Validation.TheEditorReplacedTheTemporaryFileWith, Assert.Single(_prompts.Messages));
        Assert.True(session.IsDisposed);
    }

    [Fact]
    public void AbandonedSessionsAreScavenged()
    {
        var root = Path.Combine(_directory, "scavenge");
        var old = Directory.CreateDirectory(Path.Combine(root, "old"));
        var fresh = Directory.CreateDirectory(Path.Combine(root, "fresh"));
        old.LastWriteTimeUtc = DateTime.UtcNow.AddDays(-2);

        EditSessionDirectory.Scavenge(root, DateTime.UtcNow);

        Assert.False(old.Exists && Directory.Exists(old.FullName));
        Assert.True(Directory.Exists(fresh.FullName));
    }

    private ExternalEditController? _last;

    private async Task<ExternalEditSession> OpenAsync(string content, int? maximumBytes = null, TimeSpan? debounce = null)
    {
        _agent.Download = Content(content);
        _last = Controller(maximumBytes: maximumBytes, debounce: debounce);
        return (await _last.OpenAsync(Address, "readme.txt", content.Length))!;
    }

    private ExternalEditController Controller(string? editor = null, int? maximumBytes = null, TimeSpan? debounce = null)
    {
        var store = Store();
        var preferences = store.Load();
        store.Save(preferences with
        {
            ExternalEditorPath = editor,
            MaximumEditableFileBytes = maximumBytes ?? preferences.MaximumEditableFileBytes
        });
        return new ExternalEditController(
            store, () => _agent, _prompts, _launcher, Path.Combine(_directory, "sessions"), debounce ?? TimeSpan.FromMilliseconds(10));
    }

    private DesktopConfigStore Store() => new(Path.Combine(_directory, "config"));

    private static EditableFileDownloadResponse Content(string text) =>
        new(EditableFileIpcContract.CurrentVersion, Address, Encoding.UTF8.GetBytes(text), "text/plain");

    private static T Failed<T>(string message, StorageIpcFailureCategory category)
    {
        var failure = new StorageIpcFailure("test", category, message, IsTransient: false);
        object response = typeof(T) == typeof(EditableFileDownloadResponse)
            ? new EditableFileDownloadResponse(EditableFileIpcContract.CurrentVersion, Address, [], null, failure)
            : new EditableFileUploadResponse(EditableFileIpcContract.CurrentVersion, Address, 0, null, failure);
        return (T)response;
    }

    private sealed class StubInspector : IObjectInspectorAgentClient
    {
        internal EditableFileDownloadResponse Download { get; set; } = Content(string.Empty);

        internal Func<EditableFileDownloadRequest, EditableFileDownloadResponse>? DownloadFor { get; set; }

        internal EditableFileUploadResponse Upload { get; set; } =
            new(EditableFileIpcContract.CurrentVersion, Address, 0, null);

        internal int Downloads { get; private set; }

        internal int Uploads { get; private set; }

        internal EditableFileDownloadRequest? LastDownload { get; private set; }

        internal EditableFileUploadRequest? LastUpload { get; private set; }

        public Task<EditableFileDownloadResponse> DownloadEditableFileAsync(
            EditableFileDownloadRequest request, CancellationToken cancellationToken = default)
        {
            Downloads++;
            LastDownload = request;
            return Task.FromResult(DownloadFor?.Invoke(request) ?? Download);
        }

        public Task<EditableFileUploadResponse> UploadEditedFileAsync(
            EditableFileUploadRequest request, CancellationToken cancellationToken = default)
        {
            Uploads++;
            LastUpload = request;
            return Task.FromResult(Upload);
        }

        public Task<ObjectVersionListResponse> ListVersionsAsync(
            ObjectVersionListRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ObjectMetadataGetResponse> GetMetadataAsync(
            ObjectMetadataGetRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ObjectTagsGetResponse> GetTagsAsync(
            ObjectTagsGetRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubPrompts : IExternalEditPrompts
    {
        internal UnsafeExternalEditDecision Unsafe { get; set; } = new(true, false);

        internal EditedFileChoice Choice { get; set; } = EditedFileChoice.Upload;

        internal Action<string>? OnConfirm { get; set; }

        internal List<string> Warned { get; } = [];

        internal List<string> Confirmed { get; } = [];

        internal List<string> Messages { get; } = [];

        public Task<UnsafeExternalEditDecision> WarnUnsafeAsync(string fileName)
        {
            Warned.Add(fileName);
            return Task.FromResult(Unsafe);
        }

        public Task<EditedFileChoice> ConfirmUploadAsync(string fileName)
        {
            Confirmed.Add(fileName);
            OnConfirm?.Invoke(fileName);
            return Task.FromResult(Choice);
        }

        public Task ShowAsync(string message)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class StubLauncher : IEditorLauncher
    {
        internal string? Editor { get; private set; }

        internal string? File { get; private set; }

        public void Launch(string? editorPath, string file, string workingDirectory)
        {
            Editor = editorPath;
            File = file;
        }
    }
}
