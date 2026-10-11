using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Clean up leftover files from the desktop: the agent's list joined with the editor's own session
/// folders, and only what nothing is using removed.
/// </summary>
public sealed class StagingCleanupControllerTests : IDisposable
{
    private readonly string _editRoot = Path.Combine(Path.GetTempPath(), $"storagehub-edit-{Guid.NewGuid():N}");

    public StagingCleanupControllerTests() => Directory.CreateDirectory(_editRoot);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_editRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task The_agents_leftovers_and_the_editors_closed_sessions_are_listed_together()
    {
        var closed = Session("closed", "notes.txt", 300);
        var open = Session("open", "draft.md", 20);
        var agent = new FakeAgent(
            Leftover(".storagehub-a.partial", 1000, inUse: false),
            Leftover(".storagehub-b.partial", 50, inUse: true));
        await using var controller = new StagingCleanupController(agent, () => [open], editRoot: _editRoot);

        var scan = await controller.ScanAsync(includeRemote: false, CancellationToken.None);

        Assert.False(agent.Scanned!.IncludeRemote);
        Assert.Null(scan.AgentFailure);
        Assert.Equal(4, scan.Items.Count);
        var sessions = scan.Items.Where(static item => item.Kind == LeftoverKind.EditSession).ToDictionary(static item => item.Name);
        Assert.Equal(300, sessions["notes.txt"].Size);
        Assert.False(sessions["notes.txt"].InUse);
        Assert.True(sessions["draft.md"].InUse);
        Assert.Equal(closed, sessions["notes.txt"].LocalDirectory);
    }

    [Fact]
    public async Task Only_what_nothing_uses_is_removed()
    {
        var closed = Session("closed", "notes.txt", 300);
        var open = Session("open", "draft.md", 20);
        var agent = new FakeAgent(
            Leftover(".storagehub-a.partial", 1000, inUse: false),
            Leftover(".storagehub-b.partial", 50, inUse: true),
            Leftover(".storagehub-c.partial", 70, inUse: false))
        {
            Refuse = ".storagehub-c.partial"
        };
        await using var controller = new StagingCleanupController(agent, () => [open], editRoot: _editRoot);
        var scan = await controller.ScanAsync(includeRemote: true, CancellationToken.None);

        var removal = await controller.RemoveAsync(scan.Items, CancellationToken.None);

        Assert.Equal(2, removal.Removed);
        Assert.Equal(1300, removal.BytesRemoved);
        Assert.Equal(
            [".storagehub-b.partial", ".storagehub-c.partial", "draft.md"],
            removal.Kept.Select(static kept => kept.Item.Name).Order(StringComparer.Ordinal));
        // In use is never even asked about.
        Assert.Equal(["uploads/.storagehub-a.partial", "uploads/.storagehub-c.partial"], agent.Removed.Select(static address => address.RelativePath));
        Assert.False(Directory.Exists(closed));
        Assert.True(Directory.Exists(open));
    }

    /// <summary>With no agent, the editor's own leftovers are still offered, and the reason is said.</summary>
    [Fact]
    public async Task Without_the_agent_the_editors_sessions_are_still_listed()
    {
        Session("closed", "notes.txt", 3);
        await using var controller = new StagingCleanupController(new FakeAgent { Throw = true }, () => [], editRoot: _editRoot);

        var scan = await controller.ScanAsync(includeRemote: true, CancellationToken.None);

        Assert.Equal("cleanup.agent_unavailable", scan.AgentFailure!.Code);
        Assert.Equal(LeftoverKind.EditSession, Assert.Single(scan.Items).Kind);
    }

    /// <summary>A session folder opened again between the search and the removal is left.</summary>
    [Fact]
    public async Task A_session_opened_again_since_the_search_is_left()
    {
        var reopened = Session("again", "notes.txt", 3);
        var open = new List<string>();
        await using var controller = new StagingCleanupController(new FakeAgent(), () => open, editRoot: _editRoot);
        var scan = await controller.ScanAsync(includeRemote: true, CancellationToken.None);
        open.Add(reopened);

        var removal = await controller.RemoveAsync(scan.Items, CancellationToken.None);

        Assert.Equal(0, removal.Removed);
        Assert.True(Directory.Exists(reopened));
    }

    private string Session(string name, string file, int length)
    {
        var directory = Path.Combine(_editRoot, name);
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, file), new byte[length]);
        return directory;
    }

    private static StagingLeftover Leftover(string name, long size, bool inUse) => new(
        new TransferQueueAddress(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), "root", "uploads/" + name),
        StagingLeftoverKind.ResumePartial,
        name,
        "/uploads",
        size,
        DateTimeOffset.UnixEpoch,
        inUse);

    private sealed class FakeAgent(params StagingLeftover[] leftovers) : IStagingCleanupAgentClient
    {
        public StagingCleanupScanRequest? Scanned { get; private set; }

        public List<TransferQueueAddress> Removed { get; } = [];

        public string? Refuse { get; init; }

        public bool Throw { get; init; }

        public Task<StagingCleanupScanResponse> ScanAsync(
            StagingCleanupScanRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Throw) throw new IOException("The agent is not listening.");
            Scanned = request;
            return Task.FromResult(new StagingCleanupScanResponse(
                request.ContractVersion, leftovers, 1, 0, Truncated: false, SyncRunning: false));
        }

        public Task<StagingCleanupRemoveResponse> RemoveAsync(
            StagingCleanupRemoveRequest request,
            CancellationToken cancellationToken = default)
        {
            Removed.AddRange(request.Items);
            var size = leftovers.ToDictionary(static item => item.Address.RelativePath, static item => item.Size);
            return Task.FromResult(new StagingCleanupRemoveResponse(
                request.ContractVersion,
                [.. request.Items.Select(address => address.RelativePath.EndsWith(Refuse ?? "\0", StringComparison.Ordinal)
                    ? new StagingCleanupRemoveResult(address, false, Failure: new StorageIpcFailure(
                        "cleanup.in_use", StorageIpcFailureCategory.Conflict, "In use.", false))
                    : new StagingCleanupRemoveResult(address, true, size[address.RelativePath]))]));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
