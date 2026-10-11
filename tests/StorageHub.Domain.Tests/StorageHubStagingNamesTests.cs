using StorageHub.Contracts.Ipc;
using StorageHub.Domain.Storage;

namespace StorageHub.Domain.Tests;

/// <summary>The names of StorageHub's working files, and the leftover contract that carries them.</summary>
public sealed class StorageHubStagingNamesTests
{
    [Theory]
    [InlineData(".storagehub-0123abcd.partial", StagingFileKind.ResumePartial)]
    [InlineData(".STORAGEHUB-key.PARTIAL", StagingFileKind.ResumePartial)]
    [InlineData(".storagehub-0f8fad5bd9cb469fa16570867728950e.staging", StagingFileKind.TransferStaging)]
    [InlineData(".storagehub-write-0f8fad5b.tmp", StagingFileKind.WriteProbe)]
    [InlineData(".storagehub-rename-0f8fad5b.tmp", StagingFileKind.RenameTemporary)]
    public void A_working_file_is_recognised(string name, StagingFileKind expected)
    {
        Assert.True(StorageHubStagingNames.TryClassifyFile(name, out var kind));
        Assert.Equal(expected, kind);
        Assert.True(StorageHubStagingNames.IsStagingName(name));
    }

    [Theory]
    [InlineData("report.partial")]
    [InlineData(".storagehub-.partial")]
    [InlineData(".storagehub-internal")]
    [InlineData(".storagehub-drop")]
    [InlineData(".storagehub")]
    [InlineData(".storagehub-notes.txt")]
    [InlineData(null)]
    public void Anything_else_is_not(string? name)
    {
        Assert.False(StorageHubStagingNames.TryClassifyFile(name, out _));
    }

    [Fact]
    public void A_staging_file_names_its_transfer()
    {
        var id = Guid.NewGuid();
        Assert.True(StorageHubStagingNames.TryReadStagingJobId($".storagehub-{id:N}.staging", out var read));
        Assert.Equal(id, read);
        Assert.False(StorageHubStagingNames.TryReadStagingJobId(".storagehub-notaguid.staging", out _));
        Assert.False(StorageHubStagingNames.TryReadStagingJobId($".storagehub-{id:N}.partial", out _));
    }

    [Fact]
    public void A_leftover_is_bounded()
    {
        var address = new TransferQueueAddress(Guid.NewGuid(), "root", "folder/.storagehub-a.partial");
        Assert.True(Leftover(address, ".storagehub-a.partial").HasValidBounds);
        Assert.False(Leftover(address, "a/b").HasValidBounds);
        Assert.False(Leftover(address, "").HasValidBounds);
        Assert.False(Leftover(address, ".storagehub-a.partial") with { Size = -1 } is { HasValidBounds: true });
        Assert.False(new StagingCleanupRemoveRequest(StagingCleanupIpcContract.CurrentVersion, []).HasValidBounds);
        Assert.False(new StagingCleanupRemoveRequest(
            StagingCleanupIpcContract.CurrentVersion,
            [new TransferQueueAddress(Guid.NewGuid(), "root", string.Empty)]).HasValidBounds);
        Assert.False(new StagingCleanupRemoveResponse(
            StagingCleanupIpcContract.CurrentVersion,
            [new StagingCleanupRemoveResult(address, Removed: false)]).HasValidBounds);
    }

    private static StagingLeftover Leftover(TransferQueueAddress address, string name) =>
        new(address, StagingLeftoverKind.ResumePartial, name, "folder", 10, DateTimeOffset.UnixEpoch, InUse: false);
}
