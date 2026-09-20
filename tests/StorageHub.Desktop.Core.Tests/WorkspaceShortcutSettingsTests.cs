using StorageHub.Testing;
using static StorageHub.Desktop.Tests.TestPaths;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The pinned and recent workspace lists are the first genuinely unbounded state in
/// settings.json, and the resolver is the only thing standing between a hand-edited file and a
/// stalled UI thread. These tests are pure: no STA, no window, no filesystem.
/// </summary>
public sealed class WorkspaceShortcutSettingsTests
{
    private const int Pinned = WorkspaceShortcutSettings.MaximumPinned;

    [Fact]
    public void AnEmptyOrMissingListResolvesToNothing()
    {
        Assert.Empty(WorkspaceShortcutSettings.Resolve(null, Pinned));
        Assert.Empty(WorkspaceShortcutSettings.Resolve([], Pinned));
        Assert.Empty(WorkspaceShortcutSettings.Resolve([Entry(Rooted(@"work\a.shw"))], maximum: 0));
    }

    [Fact]
    public void OnlyFullyQualifiedWorkspaceFilesSurvive()
    {
        var resolved = WorkspaceShortcutSettings.Resolve(
            [
                Entry(@"work\relative.shw"),
                Entry(Rooted(@"work\notes.txt")),
                Entry(Rooted("work\\control\u0007char.shw")),
                Entry(Rooted(@"work\") + new string('x', WorkspaceShortcutSettings.MaximumPathLength) + ".shw"),
                Entry("   "),
                Entry(Rooted(@"work\keeper.shw"))
            ],
            Pinned);

        Assert.Equal([Rooted(@"work\keeper.shw")], resolved.Select(entry => entry.Path));
    }

    [Fact]
    public void TheExtensionIsMatchedWithoutRegardToCase()
    {
        var resolved = WorkspaceShortcutSettings.Resolve([Entry(Rooted(@"work\Loud.SHW"))], Pinned);

        Assert.Equal(Rooted(@"work\Loud.SHW"), Assert.Single(resolved).Path);
    }

    [Fact]
    public void DuplicatesCollapseToTheFirstOccurrence()
    {
        var resolved = WorkspaceShortcutSettings.Resolve(
            [
                Entry(Rooted(@"work\alpha.shw"), "Newest"),
                Entry(Rooted(@"WORK\ALPHA.SHW"), "Older"),
                Entry(Rooted(@"work\beta.shw"), "Beta")
            ],
            Pinned);

        Assert.Equal(2, resolved.Count);
        Assert.Equal("Newest", resolved[0].Name);
        Assert.Equal(Rooted(@"work\beta.shw"), resolved[1].Path);
    }

    [Fact]
    public void OrderIsPositionalAndNotResortedByTimestamp()
    {
        // Clock skew and roaming profiles make stored timestamps untrustworthy, and pinned order
        // is the user's own. Index 0 stays index 0.
        var resolved = WorkspaceShortcutSettings.Resolve(
            [
                Entry(Rooted(@"work\stale.shw"), "Stale") with { LastOpenedUtc = DateTimeOffset.UnixEpoch },
                Entry(Rooted(@"work\fresh.shw"), "Fresh") with { LastOpenedUtc = DateTimeOffset.UtcNow }
            ],
            Pinned);

        Assert.Equal([Rooted(@"work\stale.shw"), Rooted(@"work\fresh.shw")], resolved.Select(entry => entry.Path));
    }

    [Fact]
    public void AnOverLongListIsClampedRatherThanRejected()
    {
        var stored = Enumerable.Range(0, Pinned + 5)
            .Select(index => Entry(Rooted($@"work\w{index}.shw")))
            .ToArray();

        var resolved = WorkspaceShortcutSettings.Resolve(stored, Pinned);

        Assert.Equal(Pinned, resolved.Count);
        Assert.Equal(Rooted(@"work\w0.shw"), resolved[0].Path);
    }

    [Fact]
    public void TheByteBudgetStopsLongPathsFromOverflowingTheSettingsFile()
    {
        // Count caps alone do not bound the file: a 400-character path JSON-escapes to roughly
        // 800 bytes because every backslash doubles.
        var stored = Enumerable.Range(0, Pinned)
            .Select(index => Entry(LongPath(index)))
            .ToArray();

        var resolved = WorkspaceShortcutSettings.Resolve(stored, Pinned);

        Assert.InRange(resolved.Count, 1, Pinned - 1);
        Assert.True(
            resolved.Sum(entry => (entry.Path.Length * 2) + (entry.Name?.Length ?? 0) + 96)
                <= WorkspaceShortcutSettings.MaximumListBytes,
            "The resolved list exceeded its own byte budget.");
    }

    [Fact]
    public void ResolveTouchesNoFilesystem()
    {
        // Load() runs synchronously on the UI thread. A stat per entry here would stall the shell.
        var absent = Rooted($@"no-such-directory-{Guid.NewGuid():N}\nested\workspace.shw");

        Assert.Equal(absent, Assert.Single(WorkspaceShortcutSettings.Resolve([Entry(absent)], Pinned)).Path);
    }

    /// <summary>The same, for the entry that made not probing worth insisting on.</summary>
    /// <remarks>
    /// A dead UNC share costs the SMB timeout to stat, which is the difference between a menu that
    /// opens and a shell that appears hung. It is also the one path shape with no counterpart on
    /// Linux: there a leading double backslash is two ordinary characters in a relative name, so
    /// the entry is dropped as unqualified and the case would assert nothing.
    /// </remarks>
    [WindowsOnlyFact]
    public void ResolveDoesNotProbeAnUnreachableShare()
    {
        const string share = @"\\dead-host-that-does-not-resolve\share\workspace.shw";

        Assert.Equal(share, Assert.Single(WorkspaceShortcutSettings.Resolve([Entry(share)], Pinned)).Path);
    }

    [Fact]
    public void NamesAreTrimmedAndOverLongNamesTruncated()
    {
        var resolved = WorkspaceShortcutSettings.Resolve(
            [
                Entry(Rooted(@"work\a.shw"), "  Spaced  "),
                Entry(Rooted(@"work\b.shw"), new string('n', WorkspaceShortcutSettings.MaximumNameLength + 40)),
                Entry(Rooted(@"work\c.shw"), "   ")
            ],
            Pinned);

        Assert.Equal("Spaced", resolved[0].Name);
        Assert.Equal(WorkspaceShortcutSettings.MaximumNameLength, resolved[1].Name!.Length);
        Assert.Null(resolved[2].Name);
    }

    [Fact]
    public void AnEntryWithoutANameStillHasALabel()
    {
        Assert.Equal("nightly", Entry(Rooted(@"work\nightly.shw")).DisplayName);
        Assert.Equal("Nightly Sync", Entry(Rooted(@"work\nightly.shw"), "Nightly Sync").DisplayName);
    }

    [Fact]
    public void ValidateAcceptsWhatResolveWouldKeepAndRejectsTheRest()
    {
        Assert.Null(WorkspaceShortcutSettings.Validate(null, Pinned));
        Assert.Null(WorkspaceShortcutSettings.Validate([Entry(Rooted(@"work\a.shw"), "A")], Pinned));

        Assert.Contains(
            "fully qualified",
            WorkspaceShortcutSettings.Validate([Entry(@"work\a.shw")], Pinned)!,
            StringComparison.Ordinal);
        Assert.Contains(
            "fully qualified",
            WorkspaceShortcutSettings.Validate([Entry(Rooted(@"work\a.txt"))], Pinned)!,
            StringComparison.Ordinal);
        Assert.Contains(
            "more than once",
            WorkspaceShortcutSettings.Validate([Entry(Rooted(@"work\a.shw")), Entry(Rooted(@"WORK\A.SHW"))], Pinned)!,
            StringComparison.Ordinal);
        Assert.Contains(
            "At most",
            WorkspaceShortcutSettings.Validate(
                [.. Enumerable.Range(0, Pinned + 1).Select(index => Entry(Rooted($@"work\w{index}.shw")))],
                Pinned)!,
            StringComparison.Ordinal);
        Assert.Contains(
            "too large",
            WorkspaceShortcutSettings.Validate(
                [.. Enumerable.Range(0, Pinned).Select(index => Entry(LongPath(index)))],
                Pinned)!,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PromoteMovesAnEntryToTheFrontWithoutDuplicatingIt()
    {
        var opened = DateTimeOffset.UtcNow;
        IReadOnlyList<WorkspaceShortcutEntry> list =
            [Entry(Rooted(@"work\a.shw"), "A"), Entry(Rooted(@"work\b.shw"), "B"), Entry(Rooted(@"work\c.shw"), "C")];

        var promoted = WorkspaceShortcutSettings.Promote(
            list,
            new WorkspaceShortcutEntry(Rooted(@"WORK\C.SHW"), "Renamed", opened),
            Pinned);

        Assert.Equal(3, promoted.Count);
        Assert.Equal(Rooted(@"WORK\C.SHW"), promoted[0].Path);
        Assert.Equal("Renamed", promoted[0].Name);
        Assert.Equal(opened, promoted[0].LastOpenedUtc);
        Assert.Equal([Rooted(@"work\a.shw"), Rooted(@"work\b.shw")], promoted.Skip(1).Select(entry => entry.Path));
    }

    [Fact]
    public void PromotingPastTheCapEvictsTheOldestEntry()
    {
        IReadOnlyList<WorkspaceShortcutEntry> list =
            [.. Enumerable.Range(0, Pinned).Select(index => Entry(Rooted($@"work\w{index}.shw")))];

        var promoted = WorkspaceShortcutSettings.Promote(list, Entry(Rooted(@"work\new.shw")), Pinned);

        Assert.Equal(Pinned, promoted.Count);
        Assert.Equal(Rooted(@"work\new.shw"), promoted[0].Path);
        Assert.DoesNotContain(promoted, entry => entry.Path == Rooted($@"work\w{Pinned - 1}.shw"));
    }

    [Fact]
    public void RemoveAndContainsIgnoreCase()
    {
        IReadOnlyList<WorkspaceShortcutEntry> list = [Entry(Rooted(@"work\a.shw")), Entry(Rooted(@"work\b.shw"))];

        Assert.True(WorkspaceShortcutSettings.Contains(list, Rooted(@"WORK\A.SHW")));
        Assert.False(WorkspaceShortcutSettings.Contains(list, Rooted(@"work\z.shw")));
        Assert.False(WorkspaceShortcutSettings.Contains(list, null));
        Assert.False(WorkspaceShortcutSettings.Contains(null, Rooted(@"work\a.shw")));

        var removed = WorkspaceShortcutSettings.Remove(list, Rooted(@"WORK\A.SHW"), Pinned);

        Assert.Equal(Rooted(@"work\b.shw"), Assert.Single(removed).Path);
        Assert.Empty(WorkspaceShortcutSettings.Remove(null, Rooted(@"work\a.shw"), Pinned));
    }

    [Fact]
    public void AnInvalidPathIsNeverReportedAsPresent()
    {
        Assert.False(WorkspaceShortcutSettings.LooksPresent(@"work\relative.shw"));
        Assert.False(WorkspaceShortcutSettings.LooksPresent(Rooted(@"work\notes.txt")));
        Assert.False(WorkspaceShortcutSettings.LooksPresent(
            Rooted($@"no-such-directory-{Guid.NewGuid():N}\workspace.shw")));
    }

    /// <inheritdoc cref="ResolveDoesNotProbeAnUnreachableShare"/>
    [WindowsOnlyFact]
    public void AnUnreachableShareIsAssumedPresentRatherThanProbed()
    {
        // Dimming a menu entry is not worth blocking the UI thread for an SMB timeout.
        Assert.True(WorkspaceShortcutSettings.LooksPresent(
            @"\\dead-host-that-does-not-resolve\share\workspace.shw"));
    }

    /// <summary>A path at the length limit, distinct per index, so the byte budget binds first.</summary>
    private static string LongPath(int index)
    {
        var prefix = Rooted($@"{index:D3}\");
        return prefix +
            new string('d', WorkspaceShortcutSettings.MaximumPathLength - prefix.Length - 4) +
            ".shw";
    }

    private static WorkspaceShortcutEntry Entry(string path, string? name = null) =>
        new(path, name, DateTimeOffset.UtcNow);
}
