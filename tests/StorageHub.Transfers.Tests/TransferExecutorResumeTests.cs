using System.Security.Cryptography;
using StorageHub.Contracts.Results;
using StorageHub.Domain.Capabilities;
using StorageHub.Domain.Identifiers;
using StorageHub.Domain.Storage;
using StorageHub.Storage.Abstractions;
using StorageHub.Storage.Models;

namespace StorageHub.Transfers.Tests;

/// <summary>
/// Resume: an interrupted transfer keeps what it staged, the next one for the same source and
/// destination sends only the rest, and staged bytes that cannot be accounted for are never
/// published.
/// </summary>
public sealed class TransferExecutorResumeTests
{
    private static readonly DateTimeOffset SourceModified = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AnInterruptedTransferKeepsItsStagedBytesAndTheNextSendsOnlyTheRest()
    {
        var payload = Payload(200_000);
        var fixture = Fixture(payload, TransferVerificationPolicy.StrongHashWhenAvailable, plannedDigest: true);
        fixture.Source.FailAfterBytes = 70_000;

        var interrupted = await TransferExecutor.ExecuteAsync(
            fixture.Intent, fixture.Source, fixture.Destination, Options());

        Assert.True(interrupted.IsFailure);
        Assert.Equal("transfer.stream.failed", interrupted.Error.Code);
        Assert.True(interrupted.Error.IsTransient);
        Assert.Equal(StorageWriteHandleState.Suspended, fixture.Destination.LastHandle!.State);
        var staged = Assert.Single(fixture.Destination.Staged).Value;
        Assert.True(staged.Count > 0);
        Assert.Null(fixture.Destination.Published);

        fixture.Source.FailAfterBytes = null;
        TransferResumePoint? point = null;
        var resumed = await TransferExecutor.ExecuteAsync(
            fixture.Intent,
            fixture.Source,
            fixture.Destination,
            Options(ready: value => point = value),
            progress: null);

        Assert.True(resumed.IsSuccess, resumed.IsFailure ? resumed.Error.Message : null);
        Assert.Equal(staged.Count, fixture.Source.LastReadRequest!.Offset);
        Assert.Equal(staged.Count, resumed.Value.BytesResumed);
        Assert.Equal(payload.LongLength, resumed.Value.BytesTransferred);
        Assert.Equal(payload, fixture.Destination.Published);
        Assert.Empty(fixture.Destination.Staged);
        Assert.NotNull(point);
        Assert.Equal(staged.Count, point!.ResumedBytes);
    }

    [Fact]
    public async Task ProgressCountsTheResumedBytes()
    {
        var payload = Payload(50_000);
        var fixture = Fixture(payload, TransferVerificationPolicy.Size);
        fixture.Destination.StageFor(fixture, payload.AsSpan(0, 20_000).ToArray());
        var reports = new List<TransferProgress>();

        var result = await TransferExecutor.ExecuteAsync(
            fixture.Intent, fixture.Source, fixture.Destination, Options(), new SyncProgress(reports));

        Assert.True(result.IsSuccess);
        Assert.All(reports, report => Assert.True(report.BytesTransferred > 20_000));
        Assert.Equal(payload.LongLength, reports[^1].BytesTransferred);
        Assert.Equal(payload.LongLength, reports[^1].TotalBytes);
    }

    [Theory]
    [InlineData(true, StorageWriteHandleState.Aborted)]
    [InlineData(false, StorageWriteHandleState.Suspended)]
    public async Task ACancelledTransferKeepsItsBytesOnlyWhenNobodyGaveUpOnIt(
        bool discard,
        StorageWriteHandleState expected)
    {
        var payload = Payload(100_000);
        var fixture = Fixture(payload, TransferVerificationPolicy.Size);
        using var cancellation = new CancellationTokenSource();
        fixture.Source.OnRead = bytes =>
        {
            if (bytes > 30_000)
            {
                cancellation.Cancel();
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await TransferExecutor.ExecuteAsync(
                fixture.Intent,
                fixture.Source,
                fixture.Destination,
                Options(discard: () => discard),
                cancellationToken: cancellation.Token));

        Assert.Equal(expected, fixture.Destination.LastHandle!.State);
        Assert.Equal(!discard, fixture.Destination.Staged.Count == 1);
    }

    [Fact]
    public async Task ASourceThatChangedStartsAgainRatherThanContinuingTheOldBytes()
    {
        var payload = Payload(40_000);
        var fixture = Fixture(payload, TransferVerificationPolicy.Size);
        fixture.Destination.StageFor(fixture, payload.AsSpan(0, 10_000).ToArray());
        fixture.Source.Entry = StorageEntry.Create(
            fixture.Intent.Source,
            StorageEntryKind.File,
            payload.LongLength,
            SourceModified.AddMinutes(5)).Value;

        var result = await TransferExecutor.ExecuteAsync(
            fixture.Intent, fixture.Source, fixture.Destination, Options());

        Assert.True(result.IsSuccess);
        Assert.Equal(0, fixture.Source.LastReadRequest!.Offset);
        Assert.Equal(0, result.Value.BytesResumed);
        Assert.Equal(payload, fixture.Destination.Published);
    }

    [Fact]
    public async Task StagedBytesThatCannotBeReadBackAreDiscardedAndTheTransferStartsAgain()
    {
        var payload = Payload(40_000);
        var fixture = Fixture(payload, TransferVerificationPolicy.StrongHashWhenAvailable);
        fixture.Destination.StageFor(fixture, payload.AsSpan(0, 10_000).ToArray());
        fixture.Destination.FailReadBack = true;

        var result = await TransferExecutor.ExecuteAsync(
            fixture.Intent, fixture.Source, fixture.Destination, Options());

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(0, fixture.Source.LastReadRequest!.Offset);
        Assert.Equal(payload, fixture.Destination.Published);
    }

    [Fact]
    public async Task StagedBytesLongerThanTheSourceAreDiscarded()
    {
        var payload = Payload(1_000);
        var fixture = Fixture(payload, TransferVerificationPolicy.Size);
        fixture.Destination.StageFor(fixture, Payload(2_000));

        var result = await TransferExecutor.ExecuteAsync(
            fixture.Intent, fixture.Source, fixture.Destination, Options());

        Assert.True(result.IsSuccess);
        Assert.Equal(0, fixture.Source.LastReadRequest!.Offset);
        Assert.Equal(payload, fixture.Destination.Published);
    }

    [Fact]
    public async Task ATruncatedSourceDiscardsTheStagedBytes()
    {
        var payload = Payload(10_000);
        var fixture = Fixture(payload, TransferVerificationPolicy.Size);
        fixture.Source.ReadBytes = payload[..5_000];

        var result = await TransferExecutor.ExecuteAsync(
            fixture.Intent, fixture.Source, fixture.Destination, Options());

        Assert.True(result.IsFailure);
        Assert.Equal("transfer.source.truncated", result.Error.Code);
        Assert.Empty(fixture.Destination.Staged);
    }

    [Fact]
    public async Task NothingIsStagedResumablyWhereTheDestinationCannotContinueAWrite()
    {
        var payload = Payload(1_000);
        var fixture = Fixture(payload, TransferVerificationPolicy.Size);
        fixture.Destination.Capabilities = Capabilities(StorageFeature.WriteStream, StorageFeature.ConditionalCreate);

        var result = await TransferExecutor.ExecuteAsync(
            fixture.Intent, fixture.Source, fixture.Destination, Options());

        Assert.True(result.IsSuccess);
        Assert.Null(fixture.Destination.LastRequest!.ResumeKey);
    }

    [Fact]
    public async Task ASourceWithNothingButASizeIsNotResumed()
    {
        var payload = Payload(1_000);
        var fixture = Fixture(payload, TransferVerificationPolicy.Size);
        fixture.Source.Entry = StorageEntry.Create(fixture.Intent.Source, StorageEntryKind.File, payload.LongLength).Value;

        var result = await TransferExecutor.ExecuteAsync(
            fixture.Intent, fixture.Source, fixture.Destination, Options());

        Assert.True(result.IsSuccess);
        Assert.Null(fixture.Destination.LastRequest!.ResumeKey);
    }

    [Fact]
    public void TheResumeKeyFollowsTheSourcesIdentityAndTheDestination()
    {
        var profile = ConnectionProfileId.New();
        var source = StorageAddress.Create(profile, "root", "a.bin").Value;
        var destination = StorageAddress.Create(ConnectionProfileId.New(), "other", "a.bin").Value;
        var elsewhere = StorageAddress.Create(destination.ProfileId, "other", "b.bin").Value;
        var entry = StorageEntry.Create(source, StorageEntryKind.File, 10, SourceModified).Value;
        var edited = StorageEntry.Create(source, StorageEntryKind.File, 10, SourceModified.AddTicks(1)).Value;

        var key = TransferResumeKey.Create(entry, destination);

        Assert.NotNull(key);
        Assert.True(StorageWriteRequest.IsValidResumeKey(key));
        Assert.Equal(key, TransferResumeKey.Create(entry, destination));
        Assert.NotEqual(key, TransferResumeKey.Create(edited, destination));
        Assert.NotEqual(key, TransferResumeKey.Create(entry, elsewhere));
    }

    private static TransferExecutionOptions Options(
        Func<bool>? discard = null,
        Action<TransferResumePoint>? ready = null) => new(
        BufferSize: 4_096,
        Resumable: true,
        DiscardStagedOnCancellation: discard,
        ResumeReady: ready);

    private static byte[] Payload(int length) =>
        Enumerable.Range(0, length).Select(index => (byte)(index * 7 % 253)).ToArray();

    private static ResumeFixture Fixture(
        byte[] payload,
        TransferVerificationPolicy verification,
        bool plannedDigest = false)
    {
        var sourceProfile = ConnectionProfileId.New();
        var destinationProfile = ConnectionProfileId.New();
        var sourceAddress = StorageAddress.Create(sourceProfile, "source-root", "folder/source.bin").Value;
        var destinationAddress = StorageAddress.Create(destinationProfile, "destination-root", "target.bin").Value;
        var intent = new TransferIntent(
            TransferJobId.New(),
            TransferOperationKind.Copy,
            sourceAddress,
            destinationAddress,
            payload.LongLength,
            verification,
            DateTimeOffset.UtcNow,
            expectedSourceDigest: plannedDigest
                ? new PortableContentDigest(
                    PortableChecksumAlgorithm.Sha256,
                    Convert.ToHexStringLower(SHA256.HashData(payload)))
                : null);
        var source = new RangedSource(sourceProfile, "source-root")
        {
            ReadBytes = payload,
            Entry = StorageEntry.Create(sourceAddress, StorageEntryKind.File, payload.LongLength, SourceModified).Value
        };
        var destination = new ResumableDestination(destinationProfile, "destination-root");
        return new ResumeFixture(intent, source, destination);
    }

    private static EffectiveStorageCapabilities Capabilities(params StorageFeature[] features) =>
        new(features.Select(feature =>
            new KeyValuePair<StorageFeature, FeatureSupport>(feature, FeatureSupport.Native())));

    private sealed record ResumeFixture(TransferIntent Intent, RangedSource Source, ResumableDestination Destination);

    /// <summary>Reports synchronously, so the test sees every report before the call returns.</summary>
    private sealed class SyncProgress(List<TransferProgress> reports) : IProgress<TransferProgress>
    {
        public void Report(TransferProgress value) => reports.Add(value);
    }

    private abstract class SessionBase(ConnectionProfileId profileId, string rootIdentity) : IStorageEndpointSession
    {
        public ConnectionProfileId ProfileId { get; } = profileId;
        public string RootIdentity { get; } = rootIdentity;
        public EffectiveStorageCapabilities Capabilities { get; set; } = EffectiveStorageCapabilities.None;

        public ValueTask<StorageResult> CheckHealthAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(StorageResult.Success());

        public virtual ValueTask<StorageResult<StorageEntry>> GetEntryAsync(
            StorageAddress address,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(StorageResult<StorageEntry>.Fail(
                new StorageFailure("storage.not_found", StorageFailureKind.NotFound, "Not found.")));

        public ValueTask<StorageResult<StoragePage>> ListAsync(
            StorageAddress address,
            StorageListRequest? request = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public virtual ValueTask<StorageResult<Stream>> OpenReadAsync(
            StorageReadRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public virtual ValueTask<StorageResult<IStorageWriteHandle>> OpenWriteAsync(
            StorageWriteRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<StorageResult<StorageEntry>> CreateDirectoryAsync(
            StorageAddress address,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<StorageResult> DeleteAsync(
            StorageDeleteRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<StorageResult<StorageEntry>> CopyAsync(
            StorageCopyRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<StorageResult<StorageEntry>> MoveAsync(
            StorageMoveRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RangedSource : SessionBase
    {
        public RangedSource(ConnectionProfileId profileId, string rootIdentity)
            : base(profileId, rootIdentity)
        {
            Capabilities = TransferExecutorResumeTests.Capabilities(StorageFeature.ReadStream, StorageFeature.ResumeDownload);
        }

        public byte[] ReadBytes { get; set; } = [];
        public StorageEntry? Entry { get; set; }
        public int? FailAfterBytes { get; set; }
        public Action<long>? OnRead { get; set; }
        public StorageReadRequest? LastReadRequest { get; private set; }

        public override ValueTask<StorageResult<StorageEntry>> GetEntryAsync(
            StorageAddress address,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(StorageResult<StorageEntry>.Success(Entry!));

        public override ValueTask<StorageResult<Stream>> OpenReadAsync(
            StorageReadRequest request,
            CancellationToken cancellationToken = default)
        {
            LastReadRequest = request;
            var remaining = ReadBytes.AsSpan((int)Math.Min(request.Offset, ReadBytes.Length)).ToArray();
            return ValueTask.FromResult(StorageResult<Stream>.Success(
                new ScriptedStream(remaining, FailAfterBytes, OnRead)));
        }
    }

    private sealed class ScriptedStream(byte[] bytes, int? failAfter, Action<long>? onRead) : MemoryStream(bytes, writable: false)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (failAfter is { } limit && Position >= limit)
            {
                throw new IOException("The connection dropped.");
            }

            var read = await base.ReadAsync(buffer, cancellationToken);
            onRead?.Invoke(Position);
            return read;
        }
    }

    private sealed class ResumableDestination : SessionBase
    {
        public ResumableDestination(ConnectionProfileId profileId, string rootIdentity)
            : base(profileId, rootIdentity)
        {
            Capabilities = TransferExecutorResumeTests.Capabilities(
                StorageFeature.WriteStream, StorageFeature.ConditionalCreate, StorageFeature.ResumeUpload);
        }

        public Dictionary<string, List<byte>> Staged { get; } = new(StringComparer.Ordinal);
        public byte[]? Published { get; set; }
        public bool FailReadBack { get; set; }
        public StagedHandle? LastHandle { get; private set; }
        public StorageWriteRequest? LastRequest { get; private set; }

        public void StageFor(ResumeFixture fixture, byte[] bytes)
        {
            var key = TransferResumeKey.Create(fixture.Source.Entry!, fixture.Intent.Destination)!;
            Staged[key] = [.. bytes];
        }

        public override ValueTask<StorageResult<IStorageWriteHandle>> OpenWriteAsync(
            StorageWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            LastHandle = new StagedHandle(this, request);
            return ValueTask.FromResult(StorageResult<IStorageWriteHandle>.Success(LastHandle));
        }
    }

    private sealed class StagedHandle : IResumableStorageWriteHandle
    {
        private readonly ResumableDestination _owner;
        private readonly string? _key;
        private readonly byte[] _prefix;
        private readonly MemoryStream _written = new();

        public StagedHandle(ResumableDestination owner, StorageWriteRequest request)
        {
            _owner = owner;
            _key = request.ResumeKey;
            Destination = request.Destination;
            _prefix = _key is not null && owner.Staged.TryGetValue(_key, out var staged) ? [.. staged] : [];
        }

        public StorageAddress Destination { get; }
        public Stream Content => _written;
        public long AcceptedOffset => _prefix.LongLength;
        public string? ResumeToken => _key is null ? null : "partial-" + _key[..8];
        public StorageWriteHandleState State { get; private set; } = StorageWriteHandleState.Open;

        public ValueTask<StorageResult<Stream>> OpenAcceptedContentAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_owner.FailReadBack
                ? StorageResult<Stream>.Fail(new StorageFailure("read-back", StorageFailureKind.Unavailable, "No."))
                : StorageResult<Stream>.Success(new MemoryStream(_prefix, writable: false)));

        public ValueTask<StorageResult<StorageEntry>> CommitAsync(CancellationToken cancellationToken = default)
        {
            State = StorageWriteHandleState.Committed;
            _owner.Published = [.. _prefix, .. _written.ToArray()];
            if (_key is not null)
            {
                _ = _owner.Staged.Remove(_key);
            }

            return ValueTask.FromResult(StorageEntry.Create(
                Destination, StorageEntryKind.File, _owner.Published.LongLength));
        }

        public ValueTask<StorageResult> AbortAsync(CancellationToken cancellationToken = default)
        {
            State = StorageWriteHandleState.Aborted;
            if (_key is not null)
            {
                _ = _owner.Staged.Remove(_key);
            }

            return ValueTask.FromResult(StorageResult.Success());
        }

        public ValueTask<StorageResult> SuspendAsync(CancellationToken cancellationToken = default)
        {
            State = StorageWriteHandleState.Suspended;
            _owner.Staged[_key!] = [.. _prefix, .. _written.ToArray()];
            return ValueTask.FromResult(StorageResult.Success());
        }

        public async ValueTask DisposeAsync()
        {
            if (State == StorageWriteHandleState.Open)
            {
                await AbortAsync();
            }
        }
    }
}
