using System.Text;
using CL.Storage;
using CL.Storage.Abstractions;
using CL.Storage.Configuration;
using CL.Storage.Models;
using CL.Storage.Queue;
using CL.Storage.Sync;
using CodeLogic;

// StorageHub's acceptance checks for a CL.Storage build: each check is one defect found while
// reviewing the library, and prints PASS or FAIL with what it saw. Needs the library's own
// integration servers (tests/Storage.Integration.Tests/docker-compose.yml): MinIO, SFTP, Swift.

var root = Path.Combine(Path.GetTempPath(), $"cl-accept-{Guid.NewGuid():N}");
var init = await CodeLogic.CodeLogic.InitializeAsync(o =>
{
    o.FrameworkRootPath = Path.Combine(root, "fw");
    o.ApplicationRootPath = Path.Combine(root, "app");
    o.AppVersion = "acceptance";
    o.HandleShutdownSignals = false;
});
if (!init.Success) throw new Exception(init.Message);
await Libraries.LoadAsync<StorageLibrary>();
Libraries.OverrideConfig<StorageConfig>("CL.Storage", "storage", c => c.Enabled = false);
await CodeLogic.CodeLogic.ConfigureAsync();
await CodeLogic.CodeLogic.StartAsync();
var lib = Libraries.Get<StorageLibrary>()!;

var failures = 0;
void Report(string id, bool pass, string seen)
{
    if (!pass) failures++;
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")}  {id,-4} {seen}");
}

static void Check(CodeLogic.Core.Results.Result r, string what)
{
    if (!r.IsSuccess) throw new Exception($"{what}: {r.Error?.Code} {r.Error?.Message}");
}

var prefix = $"accept-{Guid.NewGuid():N}";
Check(await lib.AddOrUpdateConnectionAsync("s3", new S3ConnectionConfig
{
    Enabled = true,
    Bucket = Environment.GetEnvironmentVariable("CL_ACCEPT_S3_BUCKET") ?? "cl-test",
    Prefix = prefix,
    ServiceUrl = Environment.GetEnvironmentVariable("CL_STORAGE_TEST_S3_URL") ?? "http://127.0.0.1:9010",
    ForcePathStyle = true,
    AllowInsecureHttp = true,
    AuthenticationMode = S3AuthenticationMode.StaticCredentials,
    AccessKey = Environment.GetEnvironmentVariable("CL_STORAGE_TEST_S3_ACCESSKEY") ?? "cltest",
    SecretKey = Environment.GetEnvironmentVariable("CL_STORAGE_TEST_S3_SECRETKEY") ?? "cltest-pw-minio"
}, persist: false), "register s3");
var s3 = lib.GetStorage("s3");

// R2-1: a folder spelled differently on each side of a case-insensitive pair. A new file under
// it must land in the destination's folder, and the next compare must still work.
{
    var local = Path.Combine(root, "r2-1");
    Directory.CreateDirectory(Path.Combine(local, "Docs"));
    File.WriteAllText(Path.Combine(local, "Docs", "a.txt"), "same");
    Check(await lib.AddOrUpdateConnectionAsync("r21", new LocalConnectionConfig { RootPath = local, Enabled = true }, persist: false), "register local");
    var up = await s3.UploadBytesAsync("r2-1/docs/a.txt", Encoding.UTF8.GetBytes("same"));
    if (up.IsFailure) throw new Exception(up.Error?.Code);

    var store = new InMemoryStorageSyncStateStore();
    var options = new StorageSyncOptions
    {
        Direction = StorageSyncDirection.TwoWay,
        StateStore = store,
        SyncId = "r2-1",
        Compare = new StorageCompareOptions { CaseInsensitive = true, CompareBy = StorageCompareBy.Size }
    };
    var first = await lib.SyncAsync("r21", "", "s3", "r2-1", options);
    File.WriteAllText(Path.Combine(local, "Docs", "new.txt"), "new");
    var second = await lib.SyncAsync("r21", "", "s3", "r2-1", options);
    var listed = await s3.ListAsync("r2-1", new StorageListOptions { Recursive = true });
    var paths = listed.IsFailure ? [] : listed.Value!.Items.Select(i => i.Path).OrderBy(p => p, StringComparer.Ordinal).ToArray();
    var third = await lib.CompareAsync("r21", "", "s3", "r2-1", options.Compare);
    var intoDestinationFolder = paths.Any(p => p.EndsWith("docs/new.txt", StringComparison.Ordinal));
    var secondFolder = paths.Any(p => p.Contains("Docs/", StringComparison.Ordinal));
    Report("R2-1", first.IsSuccess && second.IsSuccess && intoDestinationFolder && !secondFolder && third.IsSuccess,
        $"runs ok: {first.IsSuccess}/{second.IsSuccess}; destination: [{string.Join(", ", paths)}]; next compare: {(third.IsSuccess ? "ok" : third.Error?.Code)}");
}

// R2-2: an S3 create-only copy must keep the object's properties and a plain ETag (so the MD5
// server checksum still works), and be one request below 5 GiB.
{
    var payload = new byte[20 << 20];
    Random.Shared.NextBytes(payload);
    await using (var ms = new MemoryStream(payload))
    {
        var up = await s3.UploadAsync("r2-2/src.bin", ms, new StorageUploadOptions { ContentType = "application/x-test" });
        if (up.IsFailure) throw new Exception(up.Error?.Code);
    }

    var copy = await lib.CopyAsync("s3", "r2-2/src.bin", "s3", "r2-2/dst.bin", new StorageTransferOptions { Overwrite = false });
    var info = await s3.GetInfoAsync("r2-2/dst.bin");
    var md5 = await s3.GetServerChecksumAsync("r2-2/dst.bin", StorageChecksumAlgorithm.Md5);
    var etag = info.IsSuccess ? info.Value!.ETag : null;
    var contentType = info.IsSuccess ? info.Value!.ContentType : null;
    Report("R2-2", (copy.Outcome == StorageTransferOutcome.Completed) && etag is not null && !etag.Contains('-') && contentType == "application/x-test" && md5.IsSuccess,
        $"copied: {(copy.Outcome == StorageTransferOutcome.Completed)}; ETag: {etag}; ContentType: {contentType}; server MD5: {(md5.IsSuccess ? "available" : md5.Error?.Code)}");
}

// R2-3: StorageTransferState keeps the numbers 4.8.93 gave it (new states go at the end).
{
    var expected = new Dictionary<string, int> { ["Queued"] = 0, ["Running"] = 1, ["Completed"] = 2, ["Failed"] = 3, ["Cancelled"] = 4 };
    var wrong = expected.Where(e => !Enum.TryParse<StorageTransferState>(e.Key, out var v) || (int)v != e.Value)
        .Select(e => $"{e.Key}={(Enum.TryParse<StorageTransferState>(e.Key, out var v) ? (int)v : -1)} (was {e.Value})").ToArray();
    Report("R2-3", wrong.Length == 0, wrong.Length == 0 ? "numbers unchanged" : string.Join(", ", wrong));
}

async Task<(string A, string B, string DirA, string DirB)> LocalPair(string name)
{
    var a = Path.Combine(root, name, "a");
    var b = Path.Combine(root, name, "b");
    Directory.CreateDirectory(a);
    Directory.CreateDirectory(b);
    Check(await lib.AddOrUpdateConnectionAsync($"{name}-a", new LocalConnectionConfig { RootPath = a, Enabled = true }, persist: false), "register");
    Check(await lib.AddOrUpdateConnectionAsync($"{name}-b", new LocalConnectionConfig { RootPath = b, Enabled = true }, persist: false), "register");
    return ($"{name}-a", $"{name}-b", a, b);
}

// R3-5: excluding a folder excludes what is inside it, as in .gitignore.
{
    var p = await LocalPair("r3-5");
    Directory.CreateDirectory(Path.Combine(p.DirA, "app", "node_modules", "pkg"));
    File.WriteAllText(Path.Combine(p.DirA, "app", "main.js"), "main");
    File.WriteAllText(Path.Combine(p.DirA, "app", "node_modules", "pkg", "index.js"), "dep");
    var r = await lib.SyncAsync(p.A, "", p.B, "", new StorageSyncOptions
    {
        Direction = StorageSyncDirection.Update,
        Compare = new StorageCompareOptions { Exclude = ["**/node_modules"] }
    });
    var copiedDependency = File.Exists(Path.Combine(p.DirB, "app", "node_modules", "pkg", "index.js"));
    Report("R3-5", r.IsSuccess && File.Exists(Path.Combine(p.DirB, "app", "main.js")) && !copiedDependency,
        $"sync ok: {r.IsSuccess}; main.js copied: {File.Exists(Path.Combine(p.DirB, "app", "main.js"))}; excluded folder's file copied: {copiedDependency}");
}

// R3-6: with hidden items left out, the contents of a hidden folder are left out too.
{
    var p = await LocalPair("r3-6");
    Directory.CreateDirectory(Path.Combine(p.DirA, ".git"));
    File.WriteAllText(Path.Combine(p.DirA, ".git", "config"), "secret");
    File.WriteAllText(Path.Combine(p.DirA, "readme.txt"), "hi");
    var r = await lib.SyncAsync(p.A, "", p.B, "", new StorageSyncOptions
    {
        Direction = StorageSyncDirection.Update,
        Compare = new StorageCompareOptions { IncludeHidden = false }
    });
    var copiedHidden = File.Exists(Path.Combine(p.DirB, ".git", "config"));
    Report("R3-6", r.IsSuccess && !copiedHidden, $"sync ok: {r.IsSuccess}; hidden folder's file copied: {copiedHidden}");
}

// R3-7: a file given the Windows Hidden attribute, mirrored to S3 with hidden items left out,
// must not have its copy deleted: S3 does not see it as hidden, so it is not "extraneous".
if (OperatingSystem.IsWindows())
{
    var local = Path.Combine(root, "r3-7");
    Directory.CreateDirectory(local);
    var file = Path.Combine(local, "report.docx");
    File.WriteAllText(file, "quarterly");
    Check(await lib.AddOrUpdateConnectionAsync("r37", new LocalConnectionConfig { RootPath = local, Enabled = true }, persist: false), "register");
    var mirror = new StorageSyncOptions
    {
        Direction = StorageSyncDirection.Mirror,
        DeleteExtraneous = true,
        Compare = new StorageCompareOptions { IncludeHidden = false }
    };
    var first = await lib.SyncAsync("r37", "", "s3", "r3-7", mirror);
    File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.Hidden);
    var second = await lib.SyncAsync("r37", "", "s3", "r3-7", mirror);
    var remote = await s3.ExistsAsync("r3-7/report.docx");
    Report("R3-7", first.IsSuccess && second.IsSuccess && remote.IsSuccess && remote.Value,
        $"runs ok: {first.IsSuccess}/{second.IsSuccess}; remote copy still there: {(remote.IsSuccess ? remote.Value : "?")}");
}

// R3-8: moving a folder onto an existing folder on SFTP must not destroy what the existing
// folder held (Local refuses; a relay merges).
{
    Check(await lib.AddOrUpdateConnectionAsync("sftp", new SftpConnectionConfig
    {
        Enabled = true,
        Host = "127.0.0.1",
        Port = int.Parse(Environment.GetEnvironmentVariable("CL_STORAGE_TEST_SFTP_PORT") ?? "2022"),
        Root = Environment.GetEnvironmentVariable("CL_STORAGE_TEST_SFTP_ROOT") ?? "upload",
        Username = Environment.GetEnvironmentVariable("CL_STORAGE_TEST_SFTP_USER") ?? "cltest",
        Password = Environment.GetEnvironmentVariable("CL_STORAGE_TEST_SFTP_PASS") ?? "cltest-pw",
        AutoAcceptHostKey = true
    }, persist: false), "register sftp");
    var sftp = lib.GetStorage("sftp");
    var dir = $"accept-{Guid.NewGuid():N}";
    await sftp.UploadBytesAsync($"{dir}/from/x.txt", Encoding.UTF8.GetBytes("x"));
    await sftp.UploadBytesAsync($"{dir}/to/keep.txt", Encoding.UTF8.GetBytes("keep"));
    var moved = await lib.MoveAsync("sftp", $"{dir}/from", "sftp", $"{dir}/to");
    var kept = await sftp.ExistsAsync($"{dir}/to/keep.txt");
    Report("R3-8", kept.IsSuccess && kept.Value,
        $"move outcome: {moved.Outcome}; existing folder's file still there: {(kept.IsSuccess ? kept.Value : "?")}");
    await sftp.DeleteAsync(dir, new StorageDeleteOptions { Recursive = true, IgnoreMissing = true });
}

// R3-9: Swift must refuse an upload whose If-Match condition does not hold (it declares
// ConditionalUpdate, so the library does not check it itself).
{
    Check(await lib.AddOrUpdateConnectionAsync("swift", new SwiftConnectionConfig
    {
        Enabled = true,
        AuthenticationMode = SwiftAuthenticationMode.TempAuthV1,
        AuthenticationUrl = Environment.GetEnvironmentVariable("CL_STORAGE_TEST_SWIFT_AUTH_URL") ?? "http://127.0.0.1:8082/auth/v1.0",
        Username = Environment.GetEnvironmentVariable("CL_STORAGE_TEST_SWIFT_USER") ?? "test:tester",
        Password = Environment.GetEnvironmentVariable("CL_STORAGE_TEST_SWIFT_KEY") ?? "testing",
        Container = "cl-test",
        AllowInsecureHttp = true
    }, persist: false), "register swift");
    var swift = lib.GetStorage("swift");
    var name = $"accept-{Guid.NewGuid():N}.txt";
    var first = await swift.UploadBytesAsync(name, Encoding.UTF8.GetBytes("original"));
    await using var replacement = new MemoryStream(Encoding.UTF8.GetBytes("replacement"));
    var conditional = await swift.UploadAsync(name, replacement, new StorageUploadOptions
    {
        Condition = new StorageMutationCondition { ExpectedETag = "\"00000000000000000000000000000000\"" }
    });
    var now = await swift.DownloadBytesAsync(name);
    var content = now.IsSuccess ? Encoding.UTF8.GetString(now.Value!) : "?";
    Report("R3-9", first.IsSuccess && conditional.IsFailure && content == "original",
        $"conditional upload: {(conditional.IsSuccess ? "succeeded" : conditional.Error?.Code)}; content now: \"{content}\"");
    await swift.DeleteAsync(name, new StorageDeleteOptions { IgnoreMissing = true });
}

// R4-1: a same-server move under the Rename policy is still a server-side rename: on SFTP limited to
// one session it must work (a relay through the client needs two).
{
    Check(await lib.AddOrUpdateConnectionAsync("sftp1", new SftpConnectionConfig
    {
        Enabled = true,
        Host = "127.0.0.1",
        Port = int.Parse(Environment.GetEnvironmentVariable("CL_STORAGE_TEST_SFTP_PORT") ?? "2022"),
        Root = Environment.GetEnvironmentVariable("CL_STORAGE_TEST_SFTP_ROOT") ?? "upload",
        Username = Environment.GetEnvironmentVariable("CL_STORAGE_TEST_SFTP_USER") ?? "cltest",
        Password = Environment.GetEnvironmentVariable("CL_STORAGE_TEST_SFTP_PASS") ?? "cltest-pw",
        AutoAcceptHostKey = true,
        Session = new StorageSessionConfig { MaxSessions = 1, MaxIdleSessions = 1 }
    }, persist: false), "register sftp1");
    var sftp = lib.GetStorage("sftp1");
    var dir = $"accept-{Guid.NewGuid():N}";
    await sftp.UploadBytesAsync($"{dir}/a.txt", Encoding.UTF8.GetBytes("a"));
    await sftp.UploadBytesAsync($"{dir}/b.txt", Encoding.UTF8.GetBytes("b"));
    var moved = await lib.MoveAsync("sftp1", $"{dir}/a.txt", "sftp1", $"{dir}/b.txt",
        new StorageTransferOptions { ConflictPolicy = StorageConflictPolicy.Rename });
    Report("R4-1", moved.Outcome == StorageTransferOutcome.Completed,
        $"outcome: {moved.Outcome} {moved.Error?.Code}; written to: {moved.WrittenPath}");
    await sftp.DeleteAsync(dir, new StorageDeleteOptions { Recursive = true, IgnoreMissing = true });
}

// R4-2: an EMPTY folder spelled differently on the other side of a case-insensitive pair: a new file
// must go into that folder, and the next compare must still work.
{
    var local = Path.Combine(root, "r4-2");
    Directory.CreateDirectory(Path.Combine(local, "docs"));
    Check(await lib.AddOrUpdateConnectionAsync("r42", new LocalConnectionConfig { RootPath = local, Enabled = true }, persist: false), "register");
    var folder = await s3.CreateDirectoryAsync("r4-2/Docs");
    File.WriteAllText(Path.Combine(local, "docs", "x.txt"), "x");
    var options = new StorageSyncOptions
    {
        Direction = StorageSyncDirection.TwoWay,
        StateStore = new InMemoryStorageSyncStateStore(),
        SyncId = "r4-2",
        Compare = new StorageCompareOptions { CaseInsensitive = true, CompareBy = StorageCompareBy.Size }
    };
    var run = await lib.SyncAsync("r42", "", "s3", "r4-2", options);
    var listed = await s3.ListAsync("r4-2", new StorageListOptions { Recursive = true });
    var paths = listed.IsFailure ? [] : listed.Value!.Items.Select(i => i.Path).OrderBy(p => p, StringComparer.Ordinal).ToArray();
    var next = await lib.CompareAsync("r42", "", "s3", "r4-2", options.Compare);
    Report("R4-2", folder.IsSuccess && run.IsSuccess && next.IsSuccess && !paths.Any(p => p.StartsWith("r4-2/docs", StringComparison.Ordinal)),
        $"run ok: {run.IsSuccess}; destination: [{string.Join(", ", paths)}]; next compare: {(next.IsSuccess ? "ok" : next.Error?.Code)}");
}

// R4-3: a folder reached through a Windows junction, mirrored with LinkHandling.Follow, keeps its
// contents on the destination (they must be copied, never deleted).
if (OperatingSystem.IsWindows())
{
    var p = await LocalPair("r4-3");
    var target = Path.Combine(root, "r4-3", "target");
    Directory.CreateDirectory(target);
    File.WriteAllText(Path.Combine(target, "f.txt"), "data");
    var junction = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe",
        $"/c mklink /J \"{Path.Combine(p.DirA, "data")}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false });
    junction!.WaitForExit();
    // The connection follows links, as 4.8.93 connections set up to do so did.
    Check(await lib.AddOrUpdateConnectionAsync(p.A, new LocalConnectionConfig { RootPath = p.DirA, Enabled = true, FollowLinks = true }, persist: false), "register");
    Directory.CreateDirectory(Path.Combine(p.DirB, "data"));
    File.WriteAllText(Path.Combine(p.DirB, "data", "f.txt"), "data");
    var r = await lib.SyncAsync(p.A, "", p.B, "", new StorageSyncOptions
    {
        Direction = StorageSyncDirection.Mirror,
        DeleteExtraneous = true,
        Compare = new StorageCompareOptions { LinkHandling = StorageLinkHandling.Follow }
    });
    var kept = File.Exists(Path.Combine(p.DirB, "data", "f.txt"));
    Report("R4-3", junction.ExitCode == 0 && r.IsSuccess && kept,
        $"junction made: {junction.ExitCode == 0}; sync ok: {r.IsSuccess} {r.Error?.Code}; destination copy kept: {kept}");
}

// R4-4: on WebDAV, uploading a file (Overwrite on, the default) onto a path that is a folder must be
// refused, never delete the folder (Local, FTP and SFTP refuse).
{
    Check(await lib.AddOrUpdateConnectionAsync("webdav", new WebDavConnectionConfig
    {
        Enabled = true,
        Endpoint = Environment.GetEnvironmentVariable("CL_STORAGE_TEST_WEBDAV_URL") ?? "http://127.0.0.1:8080/",
        AuthenticationMode = WebDavAuthenticationMode.Basic,
        Username = Environment.GetEnvironmentVariable("CL_STORAGE_TEST_WEBDAV_USER") ?? "cltest",
        Password = Environment.GetEnvironmentVariable("CL_STORAGE_TEST_WEBDAV_PASS") ?? "cltest-pw",
        AllowInsecureHttp = true
    }, persist: false), "register webdav");
    var dav = lib.GetStorage("webdav");
    var dir = $"accept-{Guid.NewGuid():N}";
    await dav.UploadBytesAsync($"{dir}/docs/keep.txt", Encoding.UTF8.GetBytes("keep"));
    var upload = await dav.UploadBytesAsync($"{dir}/docs", Encoding.UTF8.GetBytes("a file"));
    var kept = await dav.ExistsAsync($"{dir}/docs/keep.txt");
    Report("R4-4", upload.IsFailure && kept.IsSuccess && kept.Value,
        $"upload onto the folder: {(upload.IsSuccess ? "succeeded" : upload.Error?.Code)}; folder's file still there: {(kept.IsSuccess ? kept.Value : "?")}");
    await dav.DeleteAsync(dir, new StorageDeleteOptions { Recursive = true, IgnoreMissing = true });
}

// R4-5: a mirror onto an FTP server whose listing gives minutes (no MLSD, e.g. vsftpd) still deletes an extra
// file and updates a changed one: the check at apply must not call every such file changed.
{
    Check(await lib.AddOrUpdateConnectionAsync("ftp", new FtpConnectionConfig
    {
        Enabled = true,
        Host = "127.0.0.1",
        Port = int.Parse(Environment.GetEnvironmentVariable("CL_STORAGE_TEST_FTP_PORT") ?? "2021"),
        Root = Environment.GetEnvironmentVariable("CL_STORAGE_TEST_FTP_ROOT") ?? "home/cltest",
        Username = Environment.GetEnvironmentVariable("CL_STORAGE_TEST_FTP_USER") ?? "cltest",
        Password = Environment.GetEnvironmentVariable("CL_STORAGE_TEST_FTP_PASS") ?? "cltest-pw",
        EncryptionMode = StorageFtpEncryptionMode.None
    }, persist: false), "register ftp");
    var ftp = lib.GetStorage("ftp");
    var dir = $"accept-{Guid.NewGuid():N}";
    var local = Path.Combine(root, "r4-5");
    Directory.CreateDirectory(local);
    File.WriteAllText(Path.Combine(local, "keep.txt"), "new content");
    Check(await lib.AddOrUpdateConnectionAsync("r45", new LocalConnectionConfig { RootPath = local, Enabled = true }, persist: false), "register");
    await ftp.UploadBytesAsync($"{dir}/keep.txt", Encoding.UTF8.GetBytes("old"));
    await ftp.UploadBytesAsync($"{dir}/extra.txt", Encoding.UTF8.GetBytes("extra"));
    var run = await lib.SyncAsync("r45", "", "ftp", dir, new StorageSyncOptions
    {
        Direction = StorageSyncDirection.Mirror,
        DeleteExtraneous = true
    });
    var extra = await ftp.ExistsAsync($"{dir}/extra.txt");
    var kept = await ftp.DownloadBytesAsync($"{dir}/keep.txt");
    var stale = run.IsSuccess ? run.Value!.Results.Count(r => r.Outcome == StorageSyncActionOutcome.Stale) : -1;
    Report("R4-5", run.IsSuccess && extra.IsSuccess && !extra.Value && kept.IsSuccess && Encoding.UTF8.GetString(kept.Value!) == "new content",
        $"run ok: {run.IsSuccess} {run.Error?.Code}; stale steps: {stale}; extra deleted: {(extra.IsSuccess ? !extra.Value : "?")}; keep.txt: \"{(kept.IsSuccess ? Encoding.UTF8.GetString(kept.Value!) : "?")}\"");
    await ftp.DeleteAsync(dir, new StorageDeleteOptions { Recursive = true, IgnoreMissing = true });
}

Console.WriteLine(failures == 0 ? "All accepted." : $"{failures} not accepted.");
// Junctions first, so the recursive delete never reaches through one into its target.
foreach (var link in Directory.Exists(root) ? Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Where(d => new DirectoryInfo(d).LinkTarget is not null).ToArray() : [])
{
    try { Directory.Delete(link); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
}
try { Directory.Delete(root, recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
return failures == 0 ? 0 : 1;

