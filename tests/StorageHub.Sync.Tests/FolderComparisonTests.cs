using StorageHub.Contracts.Results;
using StorageHub.Domain.Capabilities;
using StorageHub.Domain.Identifiers;
using StorageHub.Domain.Storage;
using StorageHub.Storage.Abstractions;

namespace StorageHub.Sync.Tests;

public sealed class FolderComparisonTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly ConnectionProfileId _leftId = ConnectionProfileId.New();
    private readonly ConnectionProfileId _rightId = ConnectionProfileId.New();

    [Fact]
    public async Task Each_path_is_only_left_only_right_different_or_the_same()
    {
        await using var left = Session(_leftId, "left",
        [
            Folder(_leftId, "left", "base/docs"),
            File(_leftId, "left", "base/docs/a.txt", 10, Noon),
            File(_leftId, "left", "base/same.txt", 5, Noon),
            File(_leftId, "left", "base/bigger.txt", 7, Noon),
            File(_leftId, "left", "base/newer.txt", 3, Noon.AddMinutes(5)),
            File(_leftId, "left", "base/kind", 1, Noon)
        ]);
        await using var right = Session(_rightId, "right",
        [
            Folder(_rightId, "right", "remote/docs"),
            File(_rightId, "right", "remote/same.txt", 5, Noon.AddSeconds(1)),
            File(_rightId, "right", "remote/bigger.txt", 6, Noon),
            File(_rightId, "right", "remote/newer.txt", 3, Noon),
            Folder(_rightId, "right", "remote/kind"),
            File(_rightId, "right", "remote/extra.bin", 9, Noon)
        ]);

        var result = await FolderComparison.CompareAsync(
            left, Root(_leftId, "left", "base"), right, Root(_rightId, "right", "remote"),
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess);
        var byPath = result.Value.Entries.ToDictionary(static entry => entry.RelativePath);
        Assert.Equal(
            ["bigger.txt", "docs", "docs/a.txt", "extra.bin", "kind", "newer.txt", "same.txt"],
            result.Value.Entries.Select(static entry => entry.RelativePath));
        Assert.Equal(FolderComparisonKind.Same, byPath["docs"].Kind);
        Assert.Equal(FolderComparisonKind.OnlyLeft, byPath["docs/a.txt"].Kind);
        Assert.Equal(FolderComparisonKind.OnlyRight, byPath["extra.bin"].Kind);
        Assert.Equal(FolderComparisonKind.Same, byPath["same.txt"].Kind);
        Assert.Equal(FolderComparisonReasons.Size, byPath["bigger.txt"].Reasons);
        Assert.Equal(FolderComparisonReasons.LeftNewer, byPath["newer.txt"].Reasons);
        Assert.Equal(FolderComparisonReasons.Type, byPath["kind"].Reasons);
    }

    /// <summary>A folder comes right before what is inside it, as a tree reads.</summary>
    [Fact]
    public void Paths_sort_with_each_folder_before_its_contents()
    {
        string[] paths = ["a-b", "a/b", "a", "B", "a/a"];
        Array.Sort(paths, FolderComparison.ComparePaths);
        Assert.Equal(["B", "a", "a/a", "a/b", "a-b"], paths);
    }

    [Fact]
    public async Task StorageHubs_own_staging_files_are_not_differences()
    {
        await using var left = Session(_leftId, "left",
        [
            File(_leftId, "left", "base/.storagehub-0123abcd.partial", 4, Noon),
            Folder(_leftId, "left", "base/.storagehub-internal"),
            Folder(_leftId, "left", "base/.storagehub-internal/staging"),
            File(_leftId, "left", "base/.storagehub-internal/staging/ff.partial", 4, Noon),
            File(_leftId, "left", "base/kept.txt", 1, Noon)
        ]);
        await using var right = Session(_rightId, "right", [File(_rightId, "right", "remote/.storagehub-9.staging", 1, Noon)]);

        var result = await FolderComparison.CompareAsync(
            left, Root(_leftId, "left", "base"), right, Root(_rightId, "right", "remote"),
            cancellationToken: CancellationToken.None);

        Assert.Equal(["kept.txt"], result.Value.Entries.Select(static entry => entry.RelativePath));
    }

    /// <summary>One case-insensitive side makes "A.txt" and "a.txt" the same file.</summary>
    [Fact]
    public async Task A_case_insensitive_side_pairs_names_that_differ_only_in_case()
    {
        await using var left = Session(_leftId, "left", [File(_leftId, "left", "base/Report.TXT", 2, Noon)],
            StorageCaseSensitivity.Insensitive);
        await using var right = Session(_rightId, "right", [File(_rightId, "right", "remote/report.txt", 2, Noon)]);

        var result = await FolderComparison.CompareAsync(
            left, Root(_leftId, "left", "base"), right, Root(_rightId, "right", "remote"),
            cancellationToken: CancellationToken.None);

        var entry = Assert.Single(result.Value.Entries);
        Assert.Equal(FolderComparisonKind.Same, entry.Kind);
        Assert.Equal("Report.TXT", entry.RelativePath);
        Assert.Equal("report.txt", entry.RightRelativePath);
        Assert.True(result.Value.CaseInsensitive);
    }

    /// <summary>
    /// What only one side has, in a folder both have under different spellings, is filed under the
    /// left's spelling and says where it is, or would go, on the right.
    /// </summary>
    [Fact]
    public async Task An_item_only_on_one_side_is_filed_under_the_lefts_spelling_of_its_folder()
    {
        await using var left = Session(_leftId, "left",
        [
            Folder(_leftId, "left", "base/Docs"),
            File(_leftId, "left", "base/Docs/old.txt", 1, Noon)
        ],
            StorageCaseSensitivity.Insensitive);
        await using var right = Session(_rightId, "right",
        [
            Folder(_rightId, "right", "remote/docs"),
            File(_rightId, "right", "remote/docs/new.txt", 1, Noon)
        ]);

        var result = await FolderComparison.CompareAsync(
            left, Root(_leftId, "left", "base"), right, Root(_rightId, "right", "remote"),
            cancellationToken: CancellationToken.None);

        Assert.Equal(["Docs", "Docs/new.txt", "Docs/old.txt"], result.Value.Entries.Select(static entry => entry.RelativePath));
        Assert.Equal("docs", result.Value.Entries[0].RightRelativePath);
        Assert.Equal("docs/new.txt", result.Value.Entries[1].RightRelativePath);
        Assert.Equal(FolderComparisonKind.OnlyLeft, result.Value.Entries[2].Kind);
        Assert.Equal("docs/old.txt", result.Value.Entries[2].RightRelativePath);
    }

    /// <summary>
    /// With contents compared, equal checksums make two files the same whatever their times say,
    /// different ones make them different, and files whose sizes already differ are not read.
    /// </summary>
    [Fact]
    public async Task Checksums_decide_pairs_of_the_same_size_when_contents_are_compared()
    {
        var read = new List<string>();
        await using var left = Session(_leftId, "left",
        [
            File(_leftId, "left", "base/copied.txt", 4, Noon.AddHours(1)),
            File(_leftId, "left", "base/edited.txt", 4, Noon),
            File(_leftId, "left", "base/longer.txt", 8, Noon)
        ], checksums: Digests(read, new() { ["copied.txt"] = 'a', ["edited.txt"] = 'b' }));
        await using var right = Session(_rightId, "right",
        [
            File(_rightId, "right", "remote/copied.txt", 4, Noon),
            File(_rightId, "right", "remote/edited.txt", 4, Noon),
            File(_rightId, "right", "remote/longer.txt", 4, Noon)
        ], checksums: Digests(read, new() { ["copied.txt"] = 'a', ["edited.txt"] = 'c' }));

        var result = await FolderComparison.CompareAsync(
            left, Root(_leftId, "left", "base"), right, Root(_rightId, "right", "remote"),
            new FolderComparisonOptions(CompareContents: true),
            CancellationToken.None);

        var byPath = result.Value.Entries.ToDictionary(static entry => entry.RelativePath);
        Assert.Equal(FolderComparisonKind.Same, byPath["copied.txt"].Kind);
        Assert.Equal(new string('a', 64), byPath["copied.txt"].LeftDigest!.Value);
        Assert.Equal(FolderComparisonKind.Different, byPath["edited.txt"].Kind);
        Assert.Equal(FolderComparisonReasons.Checksum, byPath["edited.txt"].Reasons);
        Assert.Equal(FolderComparisonReasons.Size, byPath["longer.txt"].Reasons);
        Assert.DoesNotContain("longer.txt", read);
        Assert.Equal(0, result.Value.UncheckedPairs);
    }

    /// <summary>A file that cannot be read leaves its pair judged by size and time, and is counted.</summary>
    [Fact]
    public async Task A_pair_that_cannot_be_checksummed_is_counted_and_judged_without_it()
    {
        await using var left = Session(_leftId, "left", [File(_leftId, "left", "base/locked.txt", 4, Noon)],
            checksums: static (_, _) => ValueTask.FromResult(StorageResult<PortableChecksumResult>.Fail(
                new StorageFailure("locked", StorageFailureKind.Unavailable, "Locked."))));
        await using var right = Session(_rightId, "right", [File(_rightId, "right", "remote/locked.txt", 4, Noon)],
            checksums: Digests([], new() { ["locked.txt"] = 'd' }));

        var result = await FolderComparison.CompareAsync(
            left, Root(_leftId, "left", "base"), right, Root(_rightId, "right", "remote"),
            new FolderComparisonOptions(CompareContents: true),
            CancellationToken.None);

        var entry = Assert.Single(result.Value.Entries);
        Assert.Equal(FolderComparisonKind.Same, entry.Kind);
        Assert.Null(entry.LeftDigest);
        Assert.Equal(1, result.Value.UncheckedPairs);
    }

    [Fact]
    public async Task A_folder_bigger_than_the_limit_is_refused()
    {
        await using var left = Session(_leftId, "left",
            [.. Enumerable.Range(0, 5).Select(index => File(_leftId, "left", $"base/{index}.txt", 1, Noon))]);
        await using var right = Session(_rightId, "right", []);

        var result = await FolderComparison.CompareAsync(
            left, Root(_leftId, "left", "base"), right, Root(_rightId, "right", "remote"),
            new FolderComparisonOptions(MaximumEntries: 3),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("sync.scan.limit_exceeded", result.Error.Code);
    }

    private static Func<PortableChecksumRequest, CancellationToken, ValueTask<StorageResult<PortableChecksumResult>>> Digests(
        List<string> read,
        Dictionary<string, char> byName) => (request, _) =>
        {
            lock (read) read.Add(request.ExpectedEntry.Name);
            var digest = new PortableContentDigest(
                PortableChecksumAlgorithm.Sha256,
                new string(byName[request.ExpectedEntry.Name], 64));
            return ValueTask.FromResult(StorageResult<PortableChecksumResult>.Success(
                new PortableChecksumResult(digest, request.ExpectedEntry.Size!.Value)));
        };

    private static FakeEndpointSession Session(
        ConnectionProfileId id,
        string root,
        IEnumerable<StorageEntry> entries,
        StorageCaseSensitivity caseSensitivity = StorageCaseSensitivity.Sensitive,
        Func<PortableChecksumRequest, CancellationToken, ValueTask<StorageResult<PortableChecksumResult>>>? checksums = null) =>
        new(id, root, entries, caseSensitivity, checksumHandler: checksums);

    private static StorageAddress Root(ConnectionProfileId id, string root, string path) =>
        SyncTestEntries.Address(id, root, path);

    private static StorageEntry Folder(ConnectionProfileId id, string root, string path) =>
        SyncTestEntries.Directory(id, root, path);

    private static StorageEntry File(ConnectionProfileId id, string root, string path, long length, DateTimeOffset modified) =>
        StorageEntry.Create(SyncTestEntries.Address(id, root, path), StorageEntryKind.File, length, modified).Value;
}
