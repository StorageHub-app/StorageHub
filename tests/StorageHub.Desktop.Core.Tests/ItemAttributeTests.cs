using StorageHub.Contracts.Ipc;
using StorageHub.Ipc;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The Properties window's attributes below the window: how a mode is written, what the client
/// lets through in each direction, and what the controller asks for and when.
/// </summary>
public sealed class ItemAttributeTests
{
    private static readonly ObjectInspectorAddress Address = new(
        Guid.NewGuid(), "sftp:root", "deploy/run.sh", EntityTag: "etag-1");

    [Theory]
    [InlineData(0x1ED, "rwxr-xr-x", "0755")]
    [InlineData(0x1A4, "rw-r--r--", "0644")]
    [InlineData(0x000, "---------", "0000")]
    [InlineData(0x9ED, "rwsr-xr-x", "4755")]
    [InlineData(0x9A4, "rwSr--r--", "4644")]
    [InlineData(0x5E8, "rwxr-s---", "2750")]
    [InlineData(0x3FF, "rwxrwxrwt", "1777")]
    [InlineData(0x3FE, "rwxrwxrwT", "1776")]
    public void ModesAreWrittenAsLsWritesThem(int mode, string symbolic, string octal)
    {
        Assert.Equal(symbolic, UnixPermissionText.Format(mode));
        Assert.Equal(octal, UnixPermissionText.ToOctal(mode));
        Assert.Equal($"{symbolic} ({octal})", UnixPermissionText.Describe(mode));
        Assert.True(UnixPermissionText.TryParseOctal(octal, out var parsed));
        Assert.Equal(mode, parsed);
    }

    [Theory]
    [InlineData("755", 0x1ED)]
    [InlineData(" 644 ", 0x1A4)]
    [InlineData("7", 7)]
    [InlineData("7777", 0xFFF)]
    public void OctalIsOneToFourDigits(string text, int mode)
    {
        Assert.True(UnixPermissionText.TryParseOctal(text, out var parsed));
        Assert.Equal(mode, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("8")]
    [InlineData("75a")]
    [InlineData("07777")]
    [InlineData("-755")]
    public void AnythingElseIsNotOctal(string? text) =>
        Assert.False(UnixPermissionText.TryParseOctal(text, out _));

    /// <summary>The contract bounds the mode to twelve bits and the time to UTC within 1970..2106.</summary>
    [Fact]
    public void RequestsCarryTheirOwnBounds()
    {
        const int version = ItemAttributeIpcContract.CurrentVersion;
        Assert.True(new ItemPermissionsSetRequest(version, Address, 0xFFF).HasValidBounds);
        Assert.False(new ItemPermissionsSetRequest(version, Address, 0x1000).HasValidBounds);
        Assert.False(new ItemPermissionsSetRequest(version, Address, -1).HasValidBounds);
        Assert.False(new ItemPermissionsSetRequest(version + 1, Address, 0x1ED).HasValidBounds);

        var noon = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
        Assert.True(new ItemModifiedTimeSetRequest(version, Address, noon).HasValidBounds);
        Assert.False(new ItemModifiedTimeSetRequest(version, Address, noon.ToOffset(TimeSpan.FromHours(2))).HasValidBounds);
        Assert.False(new ItemModifiedTimeSetRequest(version, Address, DateTimeOffset.UnixEpoch.AddSeconds(-1)).HasValidBounds);
        Assert.False(new ItemModifiedTimeSetRequest(
            version, Address, ItemAttributeIpcLimits.LatestModifiedTimeUtc.AddSeconds(1)).HasValidBounds);

        Assert.True(new ItemAttributes(StorageItemKind.File, UnixMode: 0xFFF, Owner: "alice").HasValidBounds);
        Assert.False(new ItemAttributes(StorageItemKind.File, UnixMode: 0x1000).HasValidBounds);
        Assert.False(new ItemAttributes(StorageItemKind.File, Owner: "al\nice").HasValidBounds);
        Assert.False(new ItemAttributes(StorageItemKind.File, Owner: new string('a', 257)).HasValidBounds);
        Assert.False(new ItemAttributes(StorageItemKind.File, OwnerId: -1).HasValidBounds);
        Assert.False(new ItemAttributes(StorageItemKind.SymbolicLink, LinkTarget: new string('a', 4_097)).HasValidBounds);
        Assert.False(new ItemAttributes(StorageItemKind.File, ModifiedUtc: noon.ToOffset(TimeSpan.FromHours(1))).HasValidBounds);
        Assert.False(new ItemAttributes((StorageItemKind)42).HasValidBounds);
    }

    /// <summary>A change outside the contract is refused before anything is sent.</summary>
    [Fact]
    public async Task TheClientRefusesAnOutOfBoundsChangeWithoutConnecting()
    {
        var transport = new FakeTransport(static _ => throw new InvalidOperationException("Nothing should be sent."));
        await using var client = CreateClient(transport);

        await Assert.ThrowsAsync<ArgumentException>(() => client.SetItemPermissionsAsync(
            new ItemPermissionsSetRequest(ItemAttributeIpcContract.CurrentVersion, Address, 0x1000)));
        await Assert.ThrowsAsync<ArgumentException>(() => client.SetItemModifiedTimeAsync(
            new ItemModifiedTimeSetRequest(
                ItemAttributeIpcContract.CurrentVersion, Address, new DateTimeOffset(1960, 1, 1, 0, 0, 0, TimeSpan.Zero))));

        Assert.Equal(0, transport.ConnectCount);
    }

    /// <summary>The permissions change goes out as its own message, with the mode asked for.</summary>
    [Fact]
    public async Task ThePermissionsChangeIsSentAsAsked()
    {
        IpcEnvelope? sent = null;
        var transport = new FakeTransport(request =>
        {
            sent = request;
            return IpcEnvelope.Create(
                ItemAttributeIpcMessageTypes.PermissionsSetResponse,
                request.RequestId,
                1,
                new ItemPermissionsSetResponse(ItemAttributeIpcContract.CurrentVersion, Address, Changed: true));
        });
        await using var client = CreateClient(transport);

        var response = await client.SetItemPermissionsAsync(
            new ItemPermissionsSetRequest(ItemAttributeIpcContract.CurrentVersion, Address, 0x1ED));

        Assert.True(response.Changed);
        Assert.Equal(ItemAttributeIpcMessageTypes.PermissionsSetRequest, sent?.MessageType);
        Assert.Equal(0x1ED, sent!.DeserializePayload<ItemPermissionsSetRequest>().UnixMode);
    }

    /// <summary>
    /// What comes back must be the item asked about, inside the bounds, and either attributes or a
    /// failure; a change must be changed or say why not. Anything else retires the pipe.
    /// </summary>
    [Fact]
    public async Task TheClientRejectsAnswersOutsideTheContract()
    {
        var answers = new Queue<object>(
        [
            new ItemAttributesGetResponse(
                ItemAttributeIpcContract.CurrentVersion, Address, new ItemAttributes(StorageItemKind.File, UnixMode: 0x1000)),
            new ItemAttributesGetResponse(
                ItemAttributeIpcContract.CurrentVersion, Address, new ItemAttributes(StorageItemKind.File),
                new StorageIpcFailure("storage.attributes.provider", StorageIpcFailureCategory.Provider, "No.", false)),
            new ItemAttributesGetResponse(
                ItemAttributeIpcContract.CurrentVersion, Address with { RelativePath = "other.sh" },
                new ItemAttributes(StorageItemKind.File)),
            new ItemAttributesGetResponse(ItemAttributeIpcContract.CurrentVersion, Address, null),
            new ItemAttributesGetResponse(
                ItemAttributeIpcContract.CurrentVersion, Address, new ItemAttributes(StorageItemKind.File, Size: 3))
        ]);
        var transport = new FakeTransport(request => IpcEnvelope.Create(
            ItemAttributeIpcMessageTypes.GetResponse, request.RequestId, 1, answers.Dequeue()));
        await using var client = CreateClient(transport);
        var get = new ItemAttributesGetRequest(ItemAttributeIpcContract.CurrentVersion, Address);

        for (var refused = 0; refused < 4; refused++)
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => client.GetItemAttributesAsync(get));
        }

        Assert.Equal(4, transport.DisconnectCount);
        Assert.Equal(3, (await client.GetItemAttributesAsync(get)).Attributes?.Size);

        var changeTransport = new FakeTransport(request => IpcEnvelope.Create(
            ItemAttributeIpcMessageTypes.ModifiedTimeSetResponse,
            request.RequestId,
            1,
            new ItemModifiedTimeSetResponse(
                ItemAttributeIpcContract.CurrentVersion, Address, Changed: true,
                new StorageIpcFailure("storage.attributes.provider", StorageIpcFailureCategory.Provider, "No.", false))));
        await using var changeClient = CreateClient(changeTransport);
        await Assert.ThrowsAsync<InvalidDataException>(() => changeClient.SetItemModifiedTimeAsync(
            new ItemModifiedTimeSetRequest(ItemAttributeIpcContract.CurrentVersion, Address, DateTimeOffset.UnixEpoch)));
    }

    /// <summary>A folder is asked for its attributes only; versions, metadata and tags are a file's.</summary>
    [Fact]
    public async Task AFolderIsAskedOnlyForItsAttributes()
    {
        var client = new CountingClient(new ItemAttributes(StorageItemKind.Directory, UnixMode: 0x1ED));
        await using var controller = new ObjectInspectorController(client, Address, kind: StorageItemKind.Directory);

        var state = await controller.RefreshAsync();

        Assert.Equal(0x1ED, state.Attributes?.UnixMode);
        Assert.Equal(1, client.AttributeReads);
        Assert.Equal(0, client.OtherReads);
    }

    /// <summary>
    /// An agent older than the attribute contract refuses the request; that costs the General tab
    /// its contents and nothing else.
    /// </summary>
    [Fact]
    public async Task AnAgentThatRefusesAttributesCostsOnlyThem()
    {
        var client = new CountingClient(null) { RefuseAttributes = true };
        await using var controller = new ObjectInspectorController(client, Address);

        var state = await controller.RefreshAsync();

        Assert.Null(state.Attributes);
        Assert.Equal("storage.attributes.agent_refused", state.AttributesFailure?.Code);
        Assert.Equal(3, client.OtherReads);
        Assert.Null(state.VersionsFailure);
    }

    /// <summary>A change that was made reads the attributes again; one that was refused does not.</summary>
    [Fact]
    public async Task AChangeReadsTheAttributesAgainOnlyWhenMade()
    {
        var client = new CountingClient(new ItemAttributes(StorageItemKind.File, UnixMode: 0x1A4, CanSetPermissions: true));
        await using var controller = new ObjectInspectorController(client, Address);
        await controller.RefreshAsync();

        Assert.Null(await controller.SetPermissionsAsync(0x1C0));
        Assert.Equal(2, client.AttributeReads);
        Assert.Equal(0x1C0, controller.State.Attributes?.UnixMode);

        client.ChangeFailure = new StorageIpcFailure(
            "storage.attributes.unsupported", StorageIpcFailureCategory.Unsupported, "No MFMT here.", false);
        var refused = await controller.SetModifiedTimeAsync(DateTimeOffset.UnixEpoch);
        Assert.Equal("No MFMT here.", refused?.Message);
        Assert.Equal(2, client.AttributeReads);
    }

    private static NamedPipeObjectInspectorAgentClient CreateClient(IStorageIpcTransport transport) =>
        new(transport, new ObjectInspectorAgentClientOptions { RequestTimeout = TimeSpan.FromSeconds(2) });

    /// <summary>Counts what the controller asks for, and answers a change by making it.</summary>
    private sealed class CountingClient(ItemAttributes? attributes) : IObjectInspectorAgentClient
    {
        private ItemAttributes? _attributes = attributes;

        internal bool RefuseAttributes { get; init; }

        internal StorageIpcFailure? ChangeFailure { get; set; }

        internal int AttributeReads { get; private set; }

        internal int OtherReads { get; private set; }

        public Task<ItemAttributesGetResponse> GetItemAttributesAsync(
            ItemAttributesGetRequest request, CancellationToken cancellationToken = default)
        {
            AttributeReads++;
            if (RefuseAttributes) throw new InvalidDataException("The local agent rejected the request.");
            return Task.FromResult(new ItemAttributesGetResponse(request.ContractVersion, request.Address, _attributes));
        }

        public Task<ItemPermissionsSetResponse> SetItemPermissionsAsync(
            ItemPermissionsSetRequest request, CancellationToken cancellationToken = default)
        {
            if (ChangeFailure is null) _attributes = _attributes! with { UnixMode = request.UnixMode };
            return Task.FromResult(new ItemPermissionsSetResponse(
                request.ContractVersion, request.Address, ChangeFailure is null, ChangeFailure));
        }

        public Task<ItemModifiedTimeSetResponse> SetItemModifiedTimeAsync(
            ItemModifiedTimeSetRequest request, CancellationToken cancellationToken = default)
        {
            if (ChangeFailure is null) _attributes = _attributes! with { ModifiedUtc = request.ModifiedUtc };
            return Task.FromResult(new ItemModifiedTimeSetResponse(
                request.ContractVersion, request.Address, ChangeFailure is null, ChangeFailure));
        }

        public Task<ObjectVersionListResponse> ListVersionsAsync(
            ObjectVersionListRequest request, CancellationToken cancellationToken = default)
        {
            OtherReads++;
            return Task.FromResult(new ObjectVersionListResponse(request.ContractVersion, request.Address, [], null));
        }

        public Task<ObjectMetadataGetResponse> GetMetadataAsync(
            ObjectMetadataGetRequest request, CancellationToken cancellationToken = default)
        {
            OtherReads++;
            return Task.FromResult(new ObjectMetadataGetResponse(request.ContractVersion, request.Address, []));
        }

        public Task<ObjectTagsGetResponse> GetTagsAsync(
            ObjectTagsGetRequest request, CancellationToken cancellationToken = default)
        {
            OtherReads++;
            return Task.FromResult(new ObjectTagsGetResponse(request.ContractVersion, request.Address, []));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeTransport(Func<IpcEnvelope, IpcEnvelope> respond) : IStorageIpcTransport
    {
        private IpcEnvelope? _request;

        public bool IsConnected { get; private set; }

        public int ConnectCount { get; private set; }

        public int DisconnectCount { get; private set; }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            ConnectCount++;
            IsConnected = true;
            return Task.CompletedTask;
        }

        public ValueTask SendAsync(IpcEnvelope envelope, CancellationToken cancellationToken = default)
        {
            _request = envelope;
            return ValueTask.CompletedTask;
        }

        public ValueTask<IpcEnvelope> ReceiveAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(respond(_request ?? throw new InvalidOperationException("No request was sent.")));

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            DisconnectCount++;
            IsConnected = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return ValueTask.CompletedTask;
        }
    }
}
