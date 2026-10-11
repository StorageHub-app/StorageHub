using System.Text.Json;
using StorageHub.Agent.Host;
using StorageHub.Contracts.Ipc;
using StorageHub.Contracts.Results;
using StorageHub.Ipc;
using StorageHub.Domain.Capabilities;
using StorageHub.Domain.Identifiers;
using StorageHub.Domain.Storage;
using StorageHub.Storage.Abstractions;
using StorageHub.Storage.Models;
using StorageHub.Sync;

namespace StorageHub.Agent.Windows.Tests;

/// <summary>
/// The agent's side of Properties: reading an item's attributes and changing its mode or modified
/// time, on the exact item the pane listed, refused with a sentence where the connection cannot.
/// </summary>
public sealed class ItemAttributeIpcCommandServiceTests
{
    private static readonly EffectiveStorageCapabilities CanChangeBoth = new(
    [
        new(StorageFeature.Permissions, FeatureSupport.Native()),
        new(StorageFeature.SetModifiedTime, FeatureSupport.Native())
    ]);

    [Fact]
    public async Task AttributesAreReadFromTheExactItemAndSaySoWhatCanChange()
    {
        var profileId = ConnectionProfileId.New();
        var session = new FakeAttributeSession(profileId, "sftp:root", CanChangeBoth);
        session.Attributes = Attributes(session, "deploy/run.sh", StorageEntryKind.File) with
        {
            UnixMode = 0x1ED,
            OwnerId = 1000,
            GroupId = 100,
            LastAccessedUtc = new DateTimeOffset(2026, 10, 9, 20, 2, 41, TimeSpan.FromHours(2))
        };
        var (service, connection) = Service(session);

        var response = await Send<ItemAttributesGetResponse>(
            service,
            ItemAttributeIpcMessageTypes.GetRequest,
            new ItemAttributesGetRequest(
                ItemAttributeIpcContract.CurrentVersion,
                new ObjectInspectorAddress(profileId.Value, "sftp:root", "deploy/run.sh")));

        Assert.Null(response.Failure);
        var attributes = Assert.IsType<ItemAttributes>(response.Attributes);
        Assert.True(attributes.HasValidBounds);
        Assert.Equal(StorageItemKind.File, attributes.Kind);
        Assert.Equal(12, attributes.Size);
        Assert.Equal(0x1ED, attributes.UnixMode);
        Assert.Equal(1000, attributes.OwnerId);
        Assert.Equal(TimeSpan.Zero, attributes.AccessedUtc?.Offset);
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 18, 2, 41, TimeSpan.Zero), attributes.AccessedUtc);
        Assert.True(attributes.CanSetPermissions);
        Assert.True(attributes.CanSetModifiedTime);
        Assert.Equal("deploy/run.sh", session.LastAddress?.CanonicalRelativePath);
        Assert.True(connection.IsDisposed);
    }

    /// <summary>
    /// What a server reports that the contract cannot carry is left out as not reported, rather
    /// than costing the window every other attribute.
    /// </summary>
    [Fact]
    public async Task ProviderValuesOutsideTheContractAreLeftOut()
    {
        var profileId = ConnectionProfileId.New();
        var session = new FakeAttributeSession(profileId, "ftp:root", EffectiveStorageCapabilities.None);
        session.Attributes = Attributes(session, "latest", StorageEntryKind.SymbolicLink) with
        {
            UnixMode = 0x10000,
            Owner = "evil\u001b[31m",
            Group = new string('g', 300),
            OwnerId = -5,
            LinkTarget = "../releases/2026.10"
        };
        var (service, _) = Service(session);

        var response = await Send<ItemAttributesGetResponse>(
            service,
            ItemAttributeIpcMessageTypes.GetRequest,
            new ItemAttributesGetRequest(
                ItemAttributeIpcContract.CurrentVersion,
                new ObjectInspectorAddress(profileId.Value, "ftp:root", "latest")));

        var attributes = Assert.IsType<ItemAttributes>(response.Attributes);
        Assert.True(attributes.HasValidBounds);
        Assert.Equal(StorageItemKind.SymbolicLink, attributes.Kind);
        Assert.Null(attributes.UnixMode);
        Assert.Null(attributes.Owner);
        Assert.Null(attributes.Group);
        Assert.Null(attributes.OwnerId);
        Assert.Equal("../releases/2026.10", attributes.LinkTarget);
        Assert.False(attributes.CanSetPermissions);
        Assert.False(attributes.CanSetModifiedTime);
    }

    [Fact]
    public async Task APermissionChangeReachesTheProviderWithTheModeAsked()
    {
        var profileId = ConnectionProfileId.New();
        var session = new FakeAttributeSession(profileId, "sftp:root", CanChangeBoth);
        var (service, connection) = Service(session);

        var response = await Send<ItemPermissionsSetResponse>(
            service,
            ItemAttributeIpcMessageTypes.PermissionsSetRequest,
            new ItemPermissionsSetRequest(
                ItemAttributeIpcContract.CurrentVersion,
                new ObjectInspectorAddress(profileId.Value, "sftp:root", "deploy/run.sh"),
                0x9ED));

        Assert.True(response.Changed);
        Assert.Null(response.Failure);
        Assert.Equal(0x9ED, session.ModeSet);
        Assert.Equal("deploy/run.sh", session.LastAddress?.CanonicalRelativePath);
        Assert.True(connection.IsDisposed);
    }

    [Fact]
    public async Task AModifiedTimeChangeReachesTheProviderInUtc()
    {
        var profileId = ConnectionProfileId.New();
        var session = new FakeAttributeSession(profileId, "sftp:root", CanChangeBoth);
        var (service, _) = Service(session);
        var noon = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

        var response = await Send<ItemModifiedTimeSetResponse>(
            service,
            ItemAttributeIpcMessageTypes.ModifiedTimeSetRequest,
            new ItemModifiedTimeSetRequest(
                ItemAttributeIpcContract.CurrentVersion,
                new ObjectInspectorAddress(profileId.Value, "sftp:root", "deploy/run.sh"),
                noon));

        Assert.True(response.Changed);
        Assert.Equal(noon, session.TimeSet);
    }

    /// <summary>A connection that does not advertise a change is refused before the provider is asked.</summary>
    [Fact]
    public async Task AnUnsupportedChangeIsRefusedWithASentence()
    {
        var profileId = ConnectionProfileId.New();
        var session = new FakeAttributeSession(profileId, "s3:bucket", EffectiveStorageCapabilities.None);
        var (service, _) = Service(session);
        var address = new ObjectInspectorAddress(profileId.Value, "s3:bucket", "photo.jpg");

        var permissions = await Send<ItemPermissionsSetResponse>(
            service,
            ItemAttributeIpcMessageTypes.PermissionsSetRequest,
            new ItemPermissionsSetRequest(ItemAttributeIpcContract.CurrentVersion, address, 0x1A4));
        var time = await Send<ItemModifiedTimeSetResponse>(
            service,
            ItemAttributeIpcMessageTypes.ModifiedTimeSetRequest,
            new ItemModifiedTimeSetRequest(ItemAttributeIpcContract.CurrentVersion, address, DateTimeOffset.UnixEpoch));

        Assert.False(permissions.Changed);
        Assert.Equal(StorageIpcFailureCategory.Unsupported, permissions.Failure?.Category);
        Assert.Equal("This connection cannot change permissions.", permissions.Failure?.Message);
        Assert.False(time.Changed);
        Assert.Equal("This connection cannot set the modified time.", time.Failure?.Message);
        Assert.Null(session.ModeSet);
        Assert.Null(session.TimeSet);
    }

    /// <summary>A server's refusal is put in words about the change, not about credentials.</summary>
    [Fact]
    public async Task AProviderRefusalIsSanitised()
    {
        var profileId = ConnectionProfileId.New();
        var session = new FakeAttributeSession(profileId, "sftp:root", CanChangeBoth)
        {
            ChangeResult = StorageResult.Fail(new StorageFailure(
                "storage.permission_denied", StorageFailureKind.Unauthorized, "EPERM /home/alice/deploy/run.sh"))
        };
        var (service, _) = Service(session);

        var response = await Send<ItemPermissionsSetResponse>(
            service,
            ItemAttributeIpcMessageTypes.PermissionsSetRequest,
            new ItemPermissionsSetRequest(
                ItemAttributeIpcContract.CurrentVersion,
                new ObjectInspectorAddress(profileId.Value, "sftp:root", "deploy/run.sh"),
                0x1ED));

        Assert.False(response.Changed);
        Assert.Equal("storage.attributes.unauthorized", response.Failure?.Code);
        Assert.DoesNotContain("/home/alice", response.Failure?.Message, StringComparison.Ordinal);
        Assert.Contains("own", response.Failure?.Message, StringComparison.Ordinal);
    }

    /// <summary>Out-of-bounds requests are refused as invalid without opening the connection.</summary>
    [Fact]
    public async Task OutOfBoundsRequestsNeverReachTheConnection()
    {
        var profileId = ConnectionProfileId.New();
        var session = new FakeAttributeSession(profileId, "sftp:root", CanChangeBoth);
        var connector = new FakeConnector(StorageResult<ISyncEndpointConnection>.Success(new FakeConnection(session)));
        var service = new ItemAttributeIpcCommandService(connector);
        var address = new ObjectInspectorAddress(profileId.Value, "sftp:root", "deploy/run.sh");

        var mode = await service.HandleAsync(IpcEnvelope.Create(
            ItemAttributeIpcMessageTypes.PermissionsSetRequest, Guid.NewGuid(), 1,
            new ItemPermissionsSetRequest(ItemAttributeIpcContract.CurrentVersion, address, 0x1000)));
        var time = await service.HandleAsync(IpcEnvelope.Create(
            ItemAttributeIpcMessageTypes.ModifiedTimeSetRequest, Guid.NewGuid(), 2,
            new ItemModifiedTimeSetRequest(
                ItemAttributeIpcContract.CurrentVersion, address,
                new DateTimeOffset(2026, 10, 11, 14, 0, 0, TimeSpan.FromHours(2)))));
        var version = await service.HandleAsync(IpcEnvelope.Create(
            ItemAttributeIpcMessageTypes.GetRequest, Guid.NewGuid(), 3,
            new ItemAttributesGetRequest(ItemAttributeIpcContract.CurrentVersion + 1, address)));

        Assert.Equal(IpcProtocol.ErrorResponseMessageType, mode.MessageType);
        Assert.Equal(IpcProtocol.ErrorResponseMessageType, time.MessageType);
        Assert.Equal(IpcProtocol.ErrorResponseMessageType, version.MessageType);
        Assert.Null(connector.LastProfileId);
    }

    /// <summary>A stale root is the inspector's identity mismatch, and nothing is changed.</summary>
    [Fact]
    public async Task ARootMismatchIsRefusedBeforeChanging()
    {
        var profileId = ConnectionProfileId.New();
        var session = new FakeAttributeSession(profileId, "opened-root", CanChangeBoth);
        var (service, _) = Service(session);

        var response = await Send<ItemPermissionsSetResponse>(
            service,
            ItemAttributeIpcMessageTypes.PermissionsSetRequest,
            new ItemPermissionsSetRequest(
                ItemAttributeIpcContract.CurrentVersion,
                new ObjectInspectorAddress(profileId.Value, "stale-root", "deploy/run.sh"),
                0x1ED));

        Assert.Equal("storage.inspector.session_identity_mismatch", response.Failure?.Code);
        Assert.Null(session.ModeSet);
    }

    /// <summary>
    /// A session without attributes still answers with the entry's kind, size and time, and
    /// offers no change even where the capabilities would.
    /// </summary>
    [Fact]
    public async Task ASessionWithoutAttributesFallsBackToTheEntry()
    {
        var profileId = ConnectionProfileId.New();
        var session = new FakePlainSession(profileId, "s3:bucket", CanChangeBoth);
        var (service, _) = Service(session);

        var response = await Send<ItemAttributesGetResponse>(
            service,
            ItemAttributeIpcMessageTypes.GetRequest,
            new ItemAttributesGetRequest(
                ItemAttributeIpcContract.CurrentVersion,
                new ObjectInspectorAddress(profileId.Value, "s3:bucket", "photo.jpg")));

        var attributes = Assert.IsType<ItemAttributes>(response.Attributes);
        Assert.Equal(StorageItemKind.File, attributes.Kind);
        Assert.Equal(12, attributes.Size);
        Assert.False(attributes.CanSetPermissions);
        Assert.False(attributes.CanSetModifiedTime);
    }

    private static StorageItemAttributes Attributes(IStorageEndpointSession session, string path, StorageEntryKind kind) =>
        new(StorageEntry.Create(
            StorageAddress.Create(session.ProfileId, session.RootIdentity, path).Value,
            kind,
            kind == StorageEntryKind.File ? 12 : null,
            new DateTimeOffset(2026, 10, 1, 9, 15, 0, TimeSpan.Zero)).Value);

    private static (ItemAttributeIpcCommandService Service, FakeConnection Connection) Service(IStorageEndpointSession session)
    {
        var connection = new FakeConnection(session);
        return (new ItemAttributeIpcCommandService(
            new FakeConnector(StorageResult<ISyncEndpointConnection>.Success(connection))), connection);
    }

    private static async Task<T> Send<T>(ItemAttributeIpcCommandService service, string messageType, object request)
    {
        var response = await service.HandleAsync(IpcEnvelope.Create(messageType, Guid.NewGuid(), 1, request));
        return response.Payload.Deserialize<T>() ?? throw new InvalidOperationException("No payload.");
    }

    private sealed class FakeConnector(StorageResult<ISyncEndpointConnection> result) : ISyncEndpointConnector
    {
        public ConnectionProfileId? LastProfileId { get; private set; }

        public ValueTask<StorageResult<ISyncEndpointConnection>> OpenAsync(
            ConnectionProfileId profileId,
            CancellationToken cancellationToken = default)
        {
            LastProfileId = profileId;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FakeConnection(IStorageEndpointSession session) : ISyncEndpointConnection
    {
        public IStorageEndpointSession Session { get; } = session;

        public bool IsDisposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeAttributeSession(
        ConnectionProfileId profileId,
        string rootIdentity,
        EffectiveStorageCapabilities capabilities) : FakePlainSession(profileId, rootIdentity, capabilities),
        IStorageAttributeEndpointSession
    {
        public StorageItemAttributes? Attributes { get; set; }

        public StorageResult ChangeResult { get; init; } = StorageResult.Success();

        public StorageAddress? LastAddress { get; private set; }

        public int? ModeSet { get; private set; }

        public DateTimeOffset? TimeSet { get; private set; }

        public ValueTask<StorageResult<StorageItemAttributes>> GetAttributesAsync(
            StorageAddress address,
            CancellationToken cancellationToken = default)
        {
            LastAddress = address;
            return ValueTask.FromResult(StorageResult<StorageItemAttributes>.Success(
                Attributes ?? Attributes(this, address.CanonicalRelativePath, StorageEntryKind.File)));
        }

        public ValueTask<StorageResult> SetPermissionsAsync(
            StorageAddress address,
            int unixMode,
            CancellationToken cancellationToken = default)
        {
            LastAddress = address;
            if (ChangeResult.IsSuccess) ModeSet = unixMode;
            return ValueTask.FromResult(ChangeResult);
        }

        public ValueTask<StorageResult> SetModifiedTimeAsync(
            StorageAddress address,
            DateTimeOffset modifiedUtc,
            CancellationToken cancellationToken = default)
        {
            LastAddress = address;
            if (ChangeResult.IsSuccess) TimeSet = modifiedUtc;
            return ValueTask.FromResult(ChangeResult);
        }
    }

    /// <summary>A session with entries and nothing more.</summary>
    private class FakePlainSession(
        ConnectionProfileId profileId,
        string rootIdentity,
        EffectiveStorageCapabilities capabilities) : IStorageEndpointSession
    {
        public ConnectionProfileId ProfileId { get; } = profileId;

        public string RootIdentity { get; } = rootIdentity;

        public EffectiveStorageCapabilities Capabilities { get; } = capabilities;

        public ValueTask<StorageResult<StorageEntry>> GetEntryAsync(
            StorageAddress address,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(StorageEntry.Create(address, StorageEntryKind.File, 12));

        public ValueTask<StorageResult> CheckHealthAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<StorageResult<StoragePage>> ListAsync(
            StorageAddress address,
            StorageListRequest? request = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<StorageResult<Stream>> OpenReadAsync(
            StorageReadRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<StorageResult<IStorageWriteHandle>> OpenWriteAsync(
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
}
