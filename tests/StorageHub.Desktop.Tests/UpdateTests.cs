using System.Net;
using System.Security.Cryptography;
using System.Text;
using StorageHub.Desktop.Updates;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The update feed, and what it takes to be offered an update.
/// </summary>
/// <remarks>
/// StorageHub is installed by an MSI on Windows and a .deb on Linux, so applying an update means
/// handing a downloaded package to an installer that runs with administrative rights. Every refusal
/// here is guarding that handover.
/// </remarks>
public class UpdateManifestTests
{
    private const string Digest = "0000000000000000000000000000000000000000000000000000000000000000";

    private static string Feed(string packages, string version = "2.0.1", int schema = 1) =>
        $$"""
        {
          "schemaVersion": {{schema}},
          "version": "{{version}}",
          "published": "2026-09-20T12:00:00+00:00",
          "notes": "Fixes things.",
          "packages": [{{packages}}]
        }
        """;

    private static string Package(
        string kind = "Msi",
        string arch = "X64",
        string url = "https://downloads.storagehub.app/storagehub-2.0.1-x64.msi",
        string sha = Digest,
        long size = 1024) =>
        $$"""{ "kind": "{{kind}}", "architecture": "{{arch}}", "url": "{{url}}", "sha256": "{{sha}}", "sizeBytes": {{size}} }""";

    private static bool Parse(string json, out UpdateManifest? manifest, out string? error) =>
        UpdateManifest.TryParse(Encoding.UTF8.GetBytes(json), out manifest, out error);

    [Fact]
    public void AWellFormedFeedParses()
    {
        Assert.True(Parse(Feed(Package()), out var manifest, out var error), error);
        Assert.Equal("2.0.1", manifest!.Version);
        Assert.Single(manifest.Packages);
        Assert.Equal(UpdatePackageKind.Msi, manifest.Packages[0].Kind);
    }

    /// <summary>
    /// The refusal that matters most.
    /// </summary>
    /// <remarks>
    /// Everything the feed names ends up run by an installer with administrative rights, so a
    /// package fetched over plain HTTP is one anyone on the path can replace - and replacing it
    /// replaces the digest that would have caught it, because both come from the same document.
    /// </remarks>
    [Theory]
    [InlineData("http://downloads.storagehub.app/x.msi")]
    [InlineData("ftp://downloads.storagehub.app/x.msi")]
    [InlineData("file:///tmp/x.msi")]
    [InlineData("not a url")]
    public void APackageThatIsNotServedOverHttpsIsRefused(string url)
    {
        Assert.False(Parse(Feed(Package(url: url)), out _, out var error));
        Assert.Contains("HTTPS", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nothex")]
    [InlineData("ABCDEF0000000000000000000000000000000000000000000000000000000000")]
    [InlineData("00000000000000000000000000000000")]
    public void APackageWithoutAUsableChecksumIsRefused(string sha)
    {
        Assert.False(Parse(Feed(Package(sha: sha)), out _, out var error));
        Assert.Contains("checksum", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3L * 1024 * 1024 * 1024)]
    public void APackageOfAnImplausibleSizeIsRefused(long size)
    {
        Assert.False(Parse(Feed(Package(size: size)), out _, out _));
    }

    /// <summary>A feed in a newer format says so rather than being read wrongly.</summary>
    [Fact]
    public void ANewerFeedFormatIsRefusedRatherThanGuessedAt()
    {
        Assert.False(Parse(Feed(Package(), schema: UpdateManifest.CurrentSchemaVersion + 1), out _, out var error));
        Assert.Contains("newer format", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    public void SomethingThatIsNotAFeedIsRefusedWithoutThrowing(string json)
    {
        Assert.False(Parse(json, out var manifest, out var error));
        Assert.Null(manifest);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    /// <summary>A feed larger than any release needs costs a length check, not a parse.</summary>
    [Fact]
    public void AnAbsurdlyLargeFeedIsRefusedBeforeItIsParsed()
    {
        var payload = new byte[UpdateManifest.MaximumBytes + 1];
        Assert.False(UpdateManifest.TryParse(payload, out _, out var error));
        Assert.Contains("larger", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThePackageForThisMachineIsTheOneChosen()
    {
        var json = Feed(string.Join(',', [
            Package(kind: "Msi", arch: "X64"),
            Package(kind: "Deb", arch: "X64", url: "https://downloads.storagehub.app/s_2.0.1_amd64.deb"),
            Package(kind: "Deb", arch: "Arm64", url: "https://downloads.storagehub.app/s_2.0.1_arm64.deb"),
        ]));

        Assert.True(Parse(json, out var manifest, out var error), error);
        Assert.Equal(
            "https://downloads.storagehub.app/s_2.0.1_arm64.deb",
            manifest!.PackageFor(UpdatePackageKind.Deb, "arm64")!.Url);
        Assert.Null(manifest.PackageFor(UpdatePackageKind.Msi, "arm64"));
    }
}

/// <summary>Whether a published release is newer than the one running.</summary>
public class SemanticReleaseTests
{
    [Theory]
    [InlineData("2.0.1", "2.0.0", true)]
    [InlineData("2.1.0", "2.0.9", true)]
    [InlineData("3.0.0", "2.9.9", true)]
    [InlineData("2.0.0", "2.0.0", false)]
    [InlineData("1.9.9", "2.0.0", false)]
    [InlineData("2.0.0+build.7", "2.0.0+build.3", false)]
    [InlineData("v2.0.1", "2.0.0", true)]
    public void ANewerReleaseIsRecognised(string candidate, string current, bool expected) =>
        Assert.Equal(expected, SemanticRelease.IsNewer(candidate, current));

    /// <summary>A pre-release leads to a version; it does not follow it.</summary>
    [Theory]
    [InlineData("2.0.0-beta.1", "2.0.0", false)]
    [InlineData("2.0.0", "2.0.0-beta.1", true)]
    [InlineData("2.0.0-beta.2", "2.0.0-beta.1", true)]
    public void APreReleaseSortsBeforeItsRelease(string candidate, string current, bool expected) =>
        Assert.Equal(expected, SemanticRelease.IsNewer(candidate, current));

    /// <summary>
    /// A version that cannot be read is never newer.
    /// </summary>
    /// <remarks>
    /// Wrong in the safe direction on purpose: staying on a working build beats installing
    /// something because a string looked odd.
    /// </remarks>
    [Theory]
    [InlineData(null, "2.0.0")]
    [InlineData("", "2.0.0")]
    [InlineData("latest", "2.0.0")]
    [InlineData("2.0.0", "not-a-version")]
    [InlineData("2.0.0-", "2.0.0")]
    public void AVersionThatCannotBeReadIsNeverNewer(string? candidate, string? current) =>
        Assert.False(SemanticRelease.IsNewer(candidate, current));
}

/// <summary>Downloading a package, and proving it is the one the feed named.</summary>
public class UpdateDownloadTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"storagehub-update-{Guid.NewGuid():N}");

    private sealed class Responder(HttpStatusCode status, byte[] body, bool declareLength = true)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(body) };
            if (!declareLength) response.Content.Headers.ContentLength = null;
            return Task.FromResult(response);
        }
    }

    /// <summary>Records reports on the thread that makes them.</summary>
    private sealed class Recorder : IProgress<int>
    {
        internal List<int> Percentages { get; } = [];

        public void Report(int value) => Percentages.Add(value);
    }

    private static string Sha256Of(byte[] payload) =>
        Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

    private static UpdatePackage Package(byte[] body, string? sha = null, long? size = null) => new(
        UpdatePackageKind.Deb,
        "X64",
        "https://downloads.storagehub.app/storagehub_2.0.1_amd64.deb",
        sha ?? Sha256Of(body),
        size ?? body.Length);

    private static HttpClient Client(byte[] body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new Responder(status, body));

    [Fact]
    public async Task APackageThatMatchesIsKept()
    {
        var body = Encoding.UTF8.GetBytes("a debian package, more or less");
        using var client = Client(body);

        var result = await UpdateDownload.FetchAsync(
            client, Package(body), _directory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(File.Exists(result.Path));
        Assert.Equal(body.Length, new FileInfo(result.Path!).Length);
    }

    /// <summary>
    /// A package whose digest is wrong is deleted, not merely rejected.
    /// </summary>
    /// <remarks>
    /// It is about to be handed to an installer running as root or as an administrator. Leaving a
    /// rejected one on disk is an invitation to run it by hand and find out.
    /// </remarks>
    [Fact]
    public async Task APackageThatDoesNotMatchItsChecksumIsDiscarded()
    {
        var body = Encoding.UTF8.GetBytes("not the package the feed described");
        using var client = Client(body);
        var package = Package(body, sha: Sha256Of(Encoding.UTF8.GetBytes("something else")));

        var result = await UpdateDownload.FetchAsync(
            client, package, _directory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(UpdateDownloadFailure.WrongDigest, result.Failure);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    /// <summary>A body longer than declared is refused rather than truncated into something valid.</summary>
    [Fact]
    public async Task APackageLongerThanDeclaredIsDiscarded()
    {
        var body = Encoding.UTF8.GetBytes("longer than the feed admits to");
        using var client = Client(body);

        var result = await UpdateDownload.FetchAsync(
            client, Package(body, size: 4), _directory,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(UpdateDownloadFailure.WrongSize, result.Failure);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task AServerThatRefusesIsReportedRatherThanThrowing()
    {
        using var client = Client([], HttpStatusCode.NotFound);

        var result = await UpdateDownload.FetchAsync(
            client, Package([1, 2, 3]), _directory,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(UpdateDownloadFailure.Unreachable, result.Failure);
    }

    [Fact]
    public async Task ProgressReachesOneHundred()
    {
        var body = new byte[200_000];
        Random.Shared.NextBytes(body);
        using var client = Client(body);

        // Not Progress<int>: it marshals each report to a synchronization context, so the reports
        // arrive after the download has already returned and the assertion races them.
        var seen = new Recorder();
        var result = await UpdateDownload.FetchAsync(
            client, Package(body), _directory, seen, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Message);
        Assert.Contains(100, seen.Percentages);
        Assert.Equal(seen.Percentages.OrderBy(percent => percent), seen.Percentages);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
