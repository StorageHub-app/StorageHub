using StorageHub.Contracts.Ipc;
using StorageHub.Desktop;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Creating, renaming and deleting, on a connection and on this computer.
/// </summary>
/// <remarks>
/// <para>
/// The point of this controller is that the two are one method each. In 1.x the split was at the
/// call site, roughly three hundred lines of MainForm branching on the pane's context kind -- which
/// is how a local rename learned about case-only renames and a remote one never had to, and why
/// the same refusal came out worded differently depending on where the folder was.
/// </para>
/// <para>
/// Both halves are driven here: the remote one against a fake agent, the local one against a
/// recorder, so every rule holds on both platforms without a disk being touched.
/// </para>
/// </remarks>
public class PaneMutationControllerTests
{
    [Fact]
    public async Task CreatingAFolderOnAConnectionAddressesItUnderTheCurrentPath()
    {
        var agent = new FakeInspector();
        await using var controller = new PaneMutationController(() => agent);

        var result = await controller.CreateFolderAsync(Remote("reports"), "q3", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("reports/q3", Assert.Single(agent.CreatedDirectories));
    }

    /// <summary>At the root there is no parent segment to join, only the name.</summary>
    [Fact]
    public async Task CreatingAtTheRootUsesTheNameAlone()
    {
        var agent = new FakeInspector();
        await using var controller = new PaneMutationController(() => agent);

        await controller.CreateFileAsync(Remote(string.Empty), "notes.txt", CancellationToken.None);

        Assert.Equal("notes.txt", Assert.Single(agent.CreatedFiles));
    }

    /// <summary>
    /// A name that is not a plain name is refused before anything is asked of the agent.
    /// </summary>
    /// <remarks>
    /// "../elsewhere" combines into a perfectly valid path outside the folder somebody is looking
    /// at. Creating is not a way to reach another folder, and neither is renaming.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../escape")]
    [InlineData("with/separator")]
    public async Task ANameThatIsNotAPlainNameIsRefused(string name)
    {
        var agent = new FakeInspector();
        await using var controller = new PaneMutationController(() => agent);

        var result = await controller.CreateFolderAsync(Remote(string.Empty), name, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(agent.CreatedDirectories);
    }

    /// <summary>
    /// A pane that is not in a folder has nothing to create in.
    /// </summary>
    /// <remarks>
    /// The connections list is a pane state, not a location: there is no folder behind it to put a
    /// new one in. A SavedConnection with no verified root cannot be built at all -- PaneTransferContext
    /// refuses it -- so this is the shape the refusal actually takes.
    /// </remarks>
    [Fact]
    public async Task CreatingSomewhereThatIsNotAFolderIsRefused()
    {
        var agent = new FakeInspector();
        await using var controller = new PaneMutationController(() => agent);
        var home = PaneTransferContext.Create(
            PaneTransferContextKind.ConnectionsHome, null, null, string.Empty);
        Assert.True(home.IsSuccess);

        var result = await controller.CreateFolderAsync(home.Value, "q3", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(agent.CreatedDirectories);
    }

    /// <summary>And renaming in one is refused for the same reason.</summary>
    [Fact]
    public async Task RenamingSomewhereThatIsNotAFolderIsRefused()
    {
        var agent = new FakeInspector();
        await using var controller = new PaneMutationController(() => agent);
        var home = PaneTransferContext.Create(
            PaneTransferContextKind.ConnectionsHome, null, null, string.Empty).Value;

        var result = await controller.RenameAsync(
            home, Item("q1.pdf", "q1.pdf"), "q2.pdf", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(agent.Renames);
    }

    [Fact]
    public async Task RenamingSendsTheOldAddressAndTheNewName()
    {
        var agent = new FakeInspector();
        await using var controller = new PaneMutationController(() => agent);

        var result = await controller.RenameAsync(
            Remote("reports"),
            Item("q1.pdf", "reports/q1.pdf"),
            "q1-final.pdf",
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var (from, to) = Assert.Single(agent.Renames);
        Assert.Equal("reports/q1.pdf", from);
        Assert.Equal("reports/q1-final.pdf", to);
    }

    /// <summary>Renaming something to what it is already called asks the agent nothing.</summary>
    [Fact]
    public async Task RenamingToTheSameNameDoesNothing()
    {
        var agent = new FakeInspector();
        await using var controller = new PaneMutationController(() => agent);

        var result = await controller.RenameAsync(
            Remote(string.Empty), Item("q1.pdf", "q1.pdf"), "q1.pdf", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(agent.Renames);
    }

    /// <summary>An agent's refusal is relayed rather than reworded.</summary>
    [Fact]
    public async Task AnAgentRefusalComesBackAsItWasGiven()
    {
        var agent = new FakeInspector
        {
            Failure = new StorageIpcFailure(
                "provider.exists", StorageIpcFailureCategory.Conflict, "That name is taken.", false)
        };
        await using var controller = new PaneMutationController(() => agent);

        var result = await controller.CreateFolderAsync(Remote(string.Empty), "q3", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("provider.exists", result.Error.Code);
        Assert.Equal("That name is taken.", result.Error.Message);
    }

    /// <summary>A folder is deleted recursively, a file is not.</summary>
    [Fact]
    public async Task DeletingAskedRecursivelyOnlyForFolders()
    {
        var agent = new FakeInspector();
        await using var controller = new PaneMutationController(() => agent);

        var outcome = await controller.DeleteAsync(
            Remote(string.Empty),
            [Item("notes.txt", "notes.txt"), Item("reports", "reports", container: true)],
            CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(2, outcome.Deleted);
        Assert.Equal([("notes.txt", false), ("reports", true)], agent.Deletes);
    }

    /// <summary>
    /// A batch that fails part way says how far it got.
    /// </summary>
    /// <remarks>
    /// "Three of five were deleted, then this happened" is a different situation from "nothing was
    /// deleted", and a caller that cannot tell them apart cannot say anything useful about either.
    /// </remarks>
    [Fact]
    public async Task DeletingStopsAtTheFirstRefusalAndSaysHowFarItGot()
    {
        var agent = new FakeInspector { FailDeleteAt = 2 };
        await using var controller = new PaneMutationController(() => agent);

        var outcome = await controller.DeleteAsync(
            Remote(string.Empty),
            [Item("a", "a"), Item("b", "b"), Item("c", "c")],
            CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(1, outcome.Deleted);
    }

    [Fact]
    public async Task DeletingNothingIsNotAFailure()
    {
        var agent = new FakeInspector();
        await using var controller = new PaneMutationController(() => agent);

        var outcome = await controller.DeleteAsync(Remote(string.Empty), [], CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(0, outcome.Deleted);
        Assert.Empty(agent.Deletes);
    }

    // ------------------------------------------------------------------ this computer

    [Fact]
    public async Task CreatingOnThisComputerGoesToTheFilesystemAndNotTheAgent()
    {
        var agent = new FakeInspector();
        var disk = new RecordingDisk();
        await using var controller = new PaneMutationController(() => agent, disk);

        var result = await controller.CreateFolderAsync(
            Local(TestPaths.Rooted("work")), "drafts", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal((TestPaths.Rooted("work"), "drafts", true), Assert.Single(disk.Created));
        Assert.Empty(agent.CreatedDirectories);
    }

    [Fact]
    public async Task RenamingOnThisComputerCarriesWhetherItIsAFolder()
    {
        var agent = new FakeInspector();
        var disk = new RecordingDisk();
        await using var controller = new PaneMutationController(() => agent, disk);

        await controller.RenameAsync(
            Local(TestPaths.Rooted("work")),
            Item("drafts", TestPaths.Rooted("work/drafts"), container: true),
            "final",
            CancellationToken.None);

        Assert.Equal(
            (TestPaths.Rooted("work/drafts"), "final", true),
            Assert.Single(disk.Renamed));
    }

    /// <summary>A filesystem refusal becomes a sentence rather than an exception.</summary>
    [Fact]
    public async Task AFilesystemRefusalIsReportedRatherThanThrown()
    {
        var agent = new FakeInspector();
        var disk = new RecordingDisk { Throw = new IOException("That name is taken.") };
        await using var controller = new PaneMutationController(() => agent, disk);

        var result = await controller.CreateFolderAsync(
            Local(TestPaths.Rooted("work")), "drafts", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("That name is taken.", result.Error.Message);
    }

    [Fact]
    public async Task DeletingOnThisComputerStopsAtTheFirstRefusal()
    {
        var agent = new FakeInspector();
        var disk = new RecordingDisk { ThrowOnDeleteAt = 2 };
        await using var controller = new PaneMutationController(() => agent, disk);

        var outcome = await controller.DeleteAsync(
            Local(TestPaths.Rooted("work")),
            [
                Item("a", TestPaths.Rooted("work/a")),
                Item("b", TestPaths.Rooted("work/b")),
                Item("c", TestPaths.Rooted("work/c"))
            ],
            CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(1, outcome.Deleted);
    }

    private static PaneTransferContext Remote(string path) => PaneTransferContext.Create(
        PaneTransferContextKind.SavedConnection, Guid.NewGuid(), "root", path).Value;

    private static PaneTransferContext Local(string path) =>
        PaneTransferContext.Create(PaneTransferContextKind.ThisPc, null, null, path).Value;

    private static PaneTransferItem Item(string name, string path, bool container = false) =>
        PaneTransferItem.Create(
            name,
            path,
            container ? StorageItemKind.Directory : StorageItemKind.File,
            container ? null : 10,
            entityTag: "etag-" + name).Value;

    /// <summary>The filesystem, written down instead of written to.</summary>
    private sealed class RecordingDisk : ILocalMutations
    {
        internal List<(string Parent, string Name, bool Container)> Created { get; } = [];

        internal List<(string Path, string NewName, bool Container)> Renamed { get; } = [];

        internal List<(string Path, bool Container)> Deleted { get; } = [];

        internal Exception? Throw { get; init; }

        /// <summary>Which delete fails, counting from one. Zero means none of them.</summary>
        internal int ThrowOnDeleteAt { get; init; }

        public void Create(string parent, string name, bool container)
        {
            if (Throw is not null) throw Throw;
            Created.Add((parent, name, container));
        }

        public void Rename(string path, string newName, bool container)
        {
            if (Throw is not null) throw Throw;
            Renamed.Add((path, newName, container));
        }

        public void Delete(string path, bool container)
        {
            if (Deleted.Count + 1 == ThrowOnDeleteAt) throw new IOException("It is in use.");
            Deleted.Add((path, container));
        }
    }

    /// <summary>An inspector that records the mutations it was asked for.</summary>
    private sealed class FakeInspector : IObjectInspectorAgentClient
    {
        internal List<string> CreatedDirectories { get; } = [];

        internal List<string> CreatedFiles { get; } = [];

        internal List<(string From, string To)> Renames { get; } = [];

        internal List<(string Path, bool Recursive)> Deletes { get; } = [];

        internal StorageIpcFailure? Failure { get; init; }

        /// <summary>Which delete is refused, counting from one. Zero means none of them.</summary>
        internal int FailDeleteAt { get; init; }

        public Task<StorageDirectoryCreateResponse> CreateDirectoryAsync(
            StorageDirectoryCreateRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Failure is null) CreatedDirectories.Add(request.Address.RelativePath);
            return Task.FromResult(new StorageDirectoryCreateResponse(
                EditableFileIpcContract.CurrentVersion, request.Address, Failure is null, Failure));
        }

        public Task<StorageFileCreateResponse> CreateFileAsync(
            StorageFileCreateRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Failure is null) CreatedFiles.Add(request.Address.RelativePath);
            return Task.FromResult(new StorageFileCreateResponse(
                EditableFileIpcContract.CurrentVersion, request.Address, Failure is null, Failure));
        }

        public Task<StorageItemRenameResponse> RenameItemAsync(
            StorageItemRenameRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Failure is null)
            {
                Renames.Add((request.Source.RelativePath, request.Destination.RelativePath));
            }

            return Task.FromResult(new StorageItemRenameResponse(
                EditableFileIpcContract.CurrentVersion,
                request.Source,
                request.Destination,
                Failure is null,
                Failure));
        }

        public Task<StorageItemDeleteResponse> DeleteItemAsync(
            StorageItemDeleteRequest request,
            CancellationToken cancellationToken = default)
        {
            var refuse = Failure ?? (Deletes.Count + 1 == FailDeleteAt
                ? new StorageIpcFailure(
                    "provider.busy", StorageIpcFailureCategory.Conflict, "It is in use.", false)
                : null);
            if (refuse is null) Deletes.Add((request.Address.RelativePath, request.Recursive));
            return Task.FromResult(new StorageItemDeleteResponse(
                EditableFileIpcContract.CurrentVersion, request.Address, refuse is null, refuse));
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

        public Task<StorageDirectoryEnsureResponse> EnsureDirectoryAsync(
            StorageDirectoryEnsureRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
