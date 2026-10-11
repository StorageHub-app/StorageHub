using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using StorageHub.Contracts.Ipc;
using StorageHub.Diagnostics;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Tools, Search, Checksums and Diagnostics, and Help, Report issue: what each reads and what it
/// gives back, without a window.
/// </summary>
public sealed class ToolsAndHelpTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"storagehub-tools-{Guid.NewGuid():N}");

    public ToolsAndHelpTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Theory]
    [InlineData("report", "Annual Report.PDF", true)]
    [InlineData("report", "summary.pdf", false)]
    [InlineData("*.jpg", "holiday.JPG", true)]
    [InlineData("*.jpg", "holiday.jpg.bak", false)]
    [InlineData("img_????.png", "IMG_0042.png", true)]
    [InlineData("img_????.png", "IMG_42.png", false)]
    public void ANameMatchesAsAFileDialogWould(string pattern, string name, bool expected)
    {
        Assert.Equal(expected, FileSearch.Matcher(pattern)(name));
    }

    /// <summary>A folder on this computer is walked to every depth, and the folder of each match is given.</summary>
    [Fact]
    public async Task ALocalFolderIsSearchedAtEveryDepth()
    {
        var nested = Directory.CreateDirectory(Path.Combine(_directory, "a", "b"));
        await File.WriteAllTextAsync(Path.Combine(_directory, "notes.txt"), "x");
        await File.WriteAllTextAsync(Path.Combine(nested.FullName, "more-notes.txt"), "xy");
        await File.WriteAllTextAsync(Path.Combine(nested.FullName, "photo.jpg"), "z");
        var where = PaneTransferContext.Create(PaneTransferContextKind.ThisPc, null, null, _directory).Value;

        var result = await new FileSearch(static () => throw new InvalidOperationException("No agent for a local folder."))
            .SearchAsync(where, "notes", progress: null, CancellationToken.None);

        Assert.Null(result.Failure);
        Assert.False(result.Truncated);
        Assert.Equal(5, result.Scanned);
        Assert.Equal(["more-notes.txt", "notes.txt"], result.Hits.Select(static hit => hit.Name).Order());
        var deep = result.Hits.Single(static hit => hit.Name == "more-notes.txt");
        Assert.Equal(nested.FullName, deep.Folder);
        Assert.Equal(2, deep.Length);
    }

    /// <summary>
    /// A connection is listed through the agent, recursively where it can be, and one folder at a
    /// time where the provider says it cannot.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AConnectionIsSearchedThroughTheAgent(bool recursive)
    {
        var storage = new TreeStorage(recursive);
        var where = PaneTransferContext.Create(
            PaneTransferContextKind.SavedConnection, TreeStorage.ConnectionId, "root", "photos").Value;

        var result = await new FileSearch(() => storage).SearchAsync(where, "*.jpg", progress: null, CancellationToken.None);

        Assert.Null(result.Failure);
        Assert.Equal(
            [("photos", "a.jpg"), ("photos/2026", "b.jpg")],
            result.Hits.Select(static hit => (hit.Folder, hit.Name)).Order());
        Assert.Equal(4, result.Scanned);
        Assert.Equal(recursive ? 1 : 3, storage.Requests.Count);
        Assert.Equal(1, storage.Disposed);
    }

    /// <summary>Nothing to search from Connections, or from This PC's list of drives.</summary>
    [Fact]
    public async Task ThereIsNothingToSearchWithoutAFolder()
    {
        var home = PaneTransferContext.Create(PaneTransferContextKind.ThisPc, null, null, string.Empty).Value;

        var result = await new FileSearch(static () => throw new InvalidOperationException())
            .SearchAsync(home, "x", progress: null, CancellationToken.None);

        Assert.False(FileSearch.CanSearch(home));
        Assert.NotNull(result.Failure);
        Assert.Empty(result.Hits);
    }

    /// <summary>A file on this computer is hashed here; one on a connection is asked of the agent.</summary>
    [Fact]
    public async Task ChecksumsComeFromTheFileOrTheAgent()
    {
        var path = Path.Combine(_directory, "data.bin");
        await File.WriteAllTextAsync(path, "hello");
        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("hello")));
        var inspector = new ChecksumInspector(new string('a', 64));
        var calculator = new ChecksumCalculator(() => inspector);

        var local = await calculator.ComputeAsync(
            PaneTransferContext.Create(PaneTransferContextKind.ThisPc, null, null, _directory).Value,
            PaneTransferItem.Create("data.bin", path, StorageItemKind.File, 5).Value,
            CancellationToken.None);
        var remote = await calculator.ComputeAsync(
            PaneTransferContext.Create(PaneTransferContextKind.SavedConnection, Guid.NewGuid(), "root", "docs").Value,
            PaneTransferItem.Create("a.txt", "docs/a.txt", StorageItemKind.File, 3, entityTag: "e1").Value,
            CancellationToken.None);

        Assert.Equal(expected, local.Sha256);
        Assert.Equal(new string('a', 64), remote.Sha256);
        Assert.Equal(ObjectChecksumSource.Server, remote.Source);
        Assert.True(inspector.Asked!.PreferServer);
        Assert.Equal("docs/a.txt", inspector.Asked.Address.RelativePath);
        Assert.Equal("e1", inspector.Asked.Address.EntityTag);
    }

    [Theory]
    [InlineData("2CF24DBA5FB0A30E26E83B2AC5B9E29E1B161E5C1FA7425E73043362938B9824", true)]
    [InlineData("sha256:2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", true)]
    [InlineData("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824  hello.txt", true)]
    [InlineData("2cf24dba", false)]
    [InlineData("", false)]
    public void APastedChecksumIsReadAsSha256sumWritesIt(string text, bool valid)
    {
        Assert.Equal(
            valid ? "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824" : null,
            ChecksumCalculator.Normalize(text));
    }

    /// <summary>
    /// The support bundle holds what StorageHub.Diagnostics allows and nothing else, and the error
    /// log goes in without the user's name, the machine's or the home folder.
    /// </summary>
    [Fact]
    public async Task TheBundleHoldsOnlyWhatThePolicyAllowsWithNamesTakenOut()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var log = Path.Combine(_directory, "desktop.log");
        await File.WriteAllTextAsync(
            log,
            $"2026-10-11 [ui] failed at {Path.Combine(home, "Documents", "x.txt")} for {Environment.UserName} on {Environment.MachineName}");
        var facts = DesktopDiagnostics.Collect(
            new AgentMonitorStatus(AgentConnectionState.Connected, 2, 1, "Ready", DateTimeOffset.UtcNow),
            new Dictionary<string, int> { ["Transferring"] = 2 });
        var zipPath = Path.Combine(_directory, "bundle.zip");

        var written = await DesktopDiagnostics.WriteBundleAsync(zipPath, facts, log);

        Assert.Equal(
            ["environment/summary.json", "health/latest.json", "logs/application.log", "packages/inventory.json"],
            written);
        await using var zip = await ZipFile.OpenReadAsync(zipPath);
        Assert.All(zip.Entries, entry => Assert.True(DiagnosticBundlePolicy.IsAllowedRelativePath(entry.FullName)));
        using var reader = new StreamReader(zip.GetEntry("logs/application.log")!.Open());
        var redacted = await reader.ReadToEndAsync();
        Assert.DoesNotContain(home, redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("~", redacted, StringComparison.Ordinal);
        if (Environment.UserName.Length > 1)
        {
            Assert.Contains("<redacted>", redacted, StringComparison.Ordinal);
        }

        using var health = new StreamReader(zip.GetEntry("health/latest.json")!.Open());
        Assert.Contains("\"Connected\"", await health.ReadToEndAsync(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Report issue opens the repository's bug form with its version and environment boxes filled,
    /// and nothing that names the person filing it.
    /// </summary>
    [Fact]
    public void ReportIssueFillsTheBugFormWithoutNamingAnybody()
    {
        var facts = DesktopDiagnostics.Collect(
            new AgentMonitorStatus(AgentConnectionState.Disconnected, 0, 0, "No answer", DateTimeOffset.UtcNow));
        var environment = DesktopDiagnostics.IssueEnvironment(facts);

        var address = StorageHubLinks.BugReportFor("2.0.0-preview.9", environment);

        Assert.StartsWith(StorageHubLinks.Issues + "/new?template=bug_report.yml&version=2.0.0-preview.9&environment=", address.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("agent Disconnected"), address.AbsoluteUri, StringComparison.Ordinal);
        Assert.DoesNotContain(Uri.EscapeDataString(Environment.MachineName), address.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(StorageHubLinks.Project + "#readme", StorageHubLinks.Documentation);
    }

    /// <summary>A storage client with one small tree: photos/a.jpg, photos/notes.txt, photos/2026/b.jpg.</summary>
    private sealed class TreeStorage(bool recursive) : IRemoteStorageAgentClient
    {
        internal static readonly Guid ConnectionId = Guid.NewGuid();

        internal List<StorageListPageRequest> Requests { get; } = [];

        internal int Disposed { get; private set; }

        public Task<StorageListPageResponse> ListStorageAsync(
            StorageListPageRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (request.Recursive && !recursive)
            {
                return Task.FromResult(Page(request, [], new StorageIpcFailure(
                    "storage.unsupported", StorageIpcFailureCategory.Unsupported, "No recursive listing.", IsTransient: false)));
            }

            StorageListItem[] entries = request.RelativePath switch
            {
                "photos" when request.Recursive =>
                [
                    Item("a.jpg", "photos/a.jpg"), Item("notes.txt", "photos/notes.txt"),
                    Folder("2026", "photos/2026"), Item("b.jpg", "photos/2026/b.jpg")
                ],
                "photos" => [Item("a.jpg", "photos/a.jpg"), Item("notes.txt", "photos/notes.txt"), Folder("2026", "photos/2026")],
                "photos/2026" => [Item("b.jpg", "photos/2026/b.jpg")],
                _ => []
            };
            return Task.FromResult(Page(request, entries));
        }

        public Task<ConnectionListResponse> ListConnectionsAsync(
            ConnectionListRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ConnectionTestResponse> TestConnectionAsync(
            ConnectionTestRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask DisposeAsync()
        {
            Disposed++;
            return ValueTask.CompletedTask;
        }

        private static StorageListPageResponse Page(
            StorageListPageRequest request, StorageListItem[] entries, StorageIpcFailure? failure = null) =>
            new(StorageIpcContract.CurrentVersion, request.ConnectionId, request.RelativePath, entries, null, failure, "root");

        private static StorageListItem Item(string name, string path) =>
            new(name, path, StorageItemKind.File, 10, DateTimeOffset.UtcNow, null, IsContainer: false);

        private static StorageListItem Folder(string name, string path) =>
            new(name, path, StorageItemKind.Directory, null, null, null, IsContainer: true);
    }

    /// <summary>An inspector that answers every checksum with the server's own.</summary>
    private sealed class ChecksumInspector(string value) : IObjectInspectorAgentClient
    {
        internal ObjectChecksumGetRequest? Asked { get; private set; }

        public Task<ObjectChecksumGetResponse> GetChecksumAsync(
            ObjectChecksumGetRequest request,
            CancellationToken cancellationToken = default)
        {
            Asked = request;
            return Task.FromResult(new ObjectChecksumGetResponse(
                request.ContractVersion, request.Address, ObjectChecksumGetResponse.Sha256, value, ObjectChecksumSource.Server, 3));
        }

        public Task<ObjectVersionListResponse> ListVersionsAsync(
            ObjectVersionListRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ObjectMetadataGetResponse> GetMetadataAsync(
            ObjectMetadataGetRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ObjectTagsGetResponse> GetTagsAsync(
            ObjectTagsGetRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
