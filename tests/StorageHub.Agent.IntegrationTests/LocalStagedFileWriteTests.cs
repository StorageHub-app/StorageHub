using StorageHub.Agent.Transfers;
using StorageHub.Contracts.Ipc;
using StorageHub.Contracts.Results;
using StorageHub.Domain.Capabilities;
using StorageHub.Domain.Identifiers;
using StorageHub.Domain.Storage;
using StorageHub.Storage.Abstractions;
using StorageHub.Storage.Models;
using StorageHub.Transfers;

namespace StorageHub.Agent.IntegrationTests;

/// <summary>
/// A file written into a folder on this PC is staged beside its destination and renamed into
/// place: an interrupted download keeps what it has and continues, the destination is never
/// half-written, and a file somebody chose to replace is replaced whole.
/// </summary>
public sealed class LocalStagedFileWriteTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"storagehub-staged-{Guid.NewGuid():N}");

    public LocalStagedFileWriteTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A leftover temp folder is not worth failing a test over.
        }
    }

    [Fact]
    public async Task ASuspendedWriteKeepsItsBytesBesideTheDestinationAndTheNextContinuesThem()
    {
        var (address, session) = await OpenAsync("download.bin");
        var request = new StorageWriteRequest(address, StorageWriteMode.CreateNew, 6, resumeKey: "abc123");

        var first = Assert.IsAssignableFrom<IResumableStorageWriteHandle>(
            (await session.OpenWriteAsync(request)).Value);
        Assert.Equal(0, first.AcceptedOffset);
        await first.Content.WriteAsync(new byte[] { 1, 2, 3 });
        Assert.True((await first.SuspendAsync()).IsSuccess);
        await first.DisposeAsync();

        Assert.False(File.Exists(Path.Combine(_folder, "download.bin")));
        Assert.True(File.Exists(Path.Combine(_folder, ".storagehub-abc123.partial")));

        var second = Assert.IsAssignableFrom<IResumableStorageWriteHandle>(
            (await session.OpenWriteAsync(request)).Value);
        Assert.Equal(3, second.AcceptedOffset);
        Assert.Equal(".storagehub-abc123.partial", second.ResumeToken);
        var staged = await second.OpenAcceptedContentAsync();
        Assert.True(staged.IsSuccess);
        await using (var stream = staged.Value)
        {
            var buffer = new byte[3];
            await stream.ReadExactlyAsync(buffer);
            Assert.Equal(new byte[] { 1, 2, 3 }, buffer);
        }

        await second.Content.WriteAsync(new byte[] { 4, 5, 6 });
        var committed = await second.CommitAsync();
        await second.DisposeAsync();

        Assert.True(committed.IsSuccess, committed.IsFailure ? committed.Error.Message : null);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, await File.ReadAllBytesAsync(Path.Combine(_folder, "download.bin")));
        Assert.Empty(Directory.EnumerateFiles(_folder, "*.partial"));
    }

    [Fact]
    public async Task AnAbortedWriteLeavesNothingBehind()
    {
        var (address, session) = await OpenAsync("aborted.bin");
        await using var handle = (await session.OpenWriteAsync(
            new StorageWriteRequest(address, StorageWriteMode.CreateNew, resumeKey: "gone"))).Value;
        await handle.Content.WriteAsync(new byte[] { 1 });

        Assert.True((await handle.AbortAsync()).IsSuccess);

        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));
    }

    [Fact]
    public async Task TwoWritesToTheSameStagedFileAreNotAllowedAtOnce()
    {
        var (address, session) = await OpenAsync("busy.bin");
        var request = new StorageWriteRequest(address, StorageWriteMode.CreateNew, resumeKey: "same");
        await using var first = (await session.OpenWriteAsync(request)).Value;

        var second = await session.OpenWriteAsync(request);

        Assert.True(second.IsFailure);
        Assert.Equal("local-user.busy", second.Error.Code);
        Assert.True(second.Error.IsTransient);
    }

    [Fact]
    public async Task CreateNewRefusesAFileThatIsThereAndOverwriteReplacesItWhole()
    {
        var path = Path.Combine(_folder, "existing.txt");
        await File.WriteAllTextAsync(path, "old");
        var (address, session) = await OpenAsync("existing.txt");

        var refused = await session.OpenWriteAsync(new StorageWriteRequest(address, StorageWriteMode.CreateNew));
        Assert.True(refused.IsFailure);
        Assert.Equal("local-user.exists", refused.Error.Code);

        await using var handle = (await session.OpenWriteAsync(
            new StorageWriteRequest(address, StorageWriteMode.Overwrite, 9))).Value;
        await handle.Content.WriteAsync("brand new"u8.ToArray());
        Assert.Equal("old", await File.ReadAllTextAsync(path));
        var committed = await handle.CommitAsync();

        Assert.True(committed.IsSuccess, committed.IsFailure ? committed.Error.Message : null);
        Assert.Equal("brand new", await File.ReadAllTextAsync(path));
        var file = new FileInfo(path);
        Assert.Equal(LocalTransferFolder.FileIdentity(file.LastWriteTimeUtc, file.Length), committed.Value.Address.EntityTag);
    }

    [Fact]
    public async Task ALocalFileCarriesTheIdentityTheDesktopComputesForIt()
    {
        var path = Path.Combine(_folder, "seen.txt");
        await File.WriteAllTextAsync(path, "content");
        var (address, session) = await OpenAsync("seen.txt");

        var entry = await session.GetEntryAsync(address);

        Assert.True(entry.IsSuccess);
        var listed = new DateTimeOffset(new FileInfo(path).LastWriteTime);
        Assert.Equal(LocalTransferFolder.FileIdentity(listed, 7), entry.Value.Address.EntityTag);
    }

    [Fact]
    public async Task AVersionConditionIsRefusedRatherThanPretendedTo()
    {
        var (address, session) = await OpenAsync("conditional.txt");

        var refused = await session.OpenWriteAsync(new StorageWriteRequest(
            address, StorageWriteMode.Overwrite, expectedDestinationEntityTag: "local:1:2"));

        Assert.True(refused.IsFailure);
        Assert.Equal("local-user.write-mode", refused.Error.Code);
    }

    [Fact]
    public async Task AnInterruptedDownloadResumesThroughTheTransferExecutor()
    {
        var payload = Enumerable.Range(0, 300_000).Select(index => (byte)(index * 31 % 251)).ToArray();
        var remote = new FlakyRemote(payload) { FailAfter = 120_000 };
        var (destination, session) = await OpenAsync("big.bin");
        var intent = new TransferIntent(
            TransferJobId.New(),
            TransferOperationKind.Copy,
            remote.Address,
            destination,
            payload.LongLength,
            TransferVerificationPolicy.StrongHashWhenAvailable,
            DateTimeOffset.UtcNow);
        var options = new TransferExecutionOptions(BufferSize: 8_192, Resumable: true);

        var interrupted = await TransferExecutor.ExecuteAsync(intent, remote, session, options);
        Assert.True(interrupted.IsFailure);
        Assert.True(interrupted.Error.IsTransient);
        Assert.False(File.Exists(Path.Combine(_folder, "big.bin")));
        var partial = Assert.Single(Directory.EnumerateFiles(_folder, "*.partial"));
        var staged = new FileInfo(partial).Length;
        Assert.True(staged > 0);

        remote.FailAfter = null;
        var resumed = await TransferExecutor.ExecuteAsync(intent, remote, session, options);

        Assert.True(resumed.IsSuccess, resumed.IsFailure ? resumed.Error.Message : null);
        Assert.Equal(staged, remote.LastOffset);
        Assert.Equal(staged, resumed.Value.BytesResumed);
        Assert.Equal(payload, await File.ReadAllBytesAsync(Path.Combine(_folder, "big.bin")));
        Assert.Empty(Directory.EnumerateFiles(_folder, "*.partial"));
    }

    private async Task<(StorageAddress Address, IStorageEndpointSession Session)> OpenAsync(string name)
    {
        var address = LocalUserPathTransferEndpoint.CreateAddress(_folder, name);
        Assert.True(address.IsSuccess, address.IsFailure ? address.Error.Message : null);
        var connection = LocalUserPathTransferEndpoint.Open(address.Value);
        Assert.True(connection.IsSuccess);
        await Task.CompletedTask;
        return (address.Value, connection.Value.Session);
    }

    private sealed class FlakyRemote(byte[] payload) : IStorageEndpointSession
    {
        public ConnectionProfileId ProfileId { get; } = ConnectionProfileId.New();
        public string RootIdentity => "remote-root";
        public StorageAddress Address => StorageAddress.Create(ProfileId, RootIdentity, "big.bin").Value;
        public int? FailAfter { get; set; }
        public long LastOffset { get; private set; } = -1;

        public EffectiveStorageCapabilities Capabilities { get; } = new([
            new KeyValuePair<StorageFeature, FeatureSupport>(StorageFeature.ReadStream, FeatureSupport.Native()),
            new KeyValuePair<StorageFeature, FeatureSupport>(StorageFeature.ResumeDownload, FeatureSupport.Native())]);

        public ValueTask<StorageResult> CheckHealthAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(StorageResult.Success());

        public ValueTask<StorageResult<StorageEntry>> GetEntryAsync(StorageAddress address, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(StorageEntry.Create(
                address, StorageEntryKind.File, payload.LongLength, new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), eTag: "\"v1\""));

        public ValueTask<StorageResult<Stream>> OpenReadAsync(StorageReadRequest request, CancellationToken cancellationToken = default)
        {
            LastOffset = request.Offset;
            Stream stream = new Flaky(payload[(int)request.Offset..], FailAfter);
            return ValueTask.FromResult(StorageResult<Stream>.Success(stream));
        }

        public ValueTask<StorageResult<StoragePage>> ListAsync(StorageAddress address, StorageListRequest? request = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<StorageResult<IStorageWriteHandle>> OpenWriteAsync(StorageWriteRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<StorageResult<StorageEntry>> CreateDirectoryAsync(StorageAddress address, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<StorageResult> DeleteAsync(StorageDeleteRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<StorageResult<StorageEntry>> CopyAsync(StorageCopyRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<StorageResult<StorageEntry>> MoveAsync(StorageMoveRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Flaky(byte[] bytes, int? failAfter) : MemoryStream(bytes, writable: false)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            failAfter is { } limit && Position >= limit
                ? throw new IOException("The connection dropped.")
                : base.ReadAsync(buffer, cancellationToken);
    }
}
