using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

public sealed record ObjectInspectorState(
    ObjectInspectorAddress Address,
    IReadOnlyList<ObjectVersionSummary> Versions,
    string? VersionContinuationToken,
    IReadOnlyList<ObjectMetadataEntry> Metadata,
    IReadOnlyList<ObjectTagEntry> Tags,
    StorageIpcFailure? VersionsFailure = null,
    StorageIpcFailure? MetadataFailure = null,
    StorageIpcFailure? TagsFailure = null,
    ItemAttributes? Attributes = null,
    StorageIpcFailure? AttributesFailure = null)
{
    public bool CanLoadMoreVersions =>
        VersionsFailure is null && VersionContinuationToken is not null;

    public static ObjectInspectorState Empty(ObjectInspectorAddress address) => new(
        address,
        [],
        VersionContinuationToken: null,
        [],
        [],
        VersionsFailure: null,
        MetadataFailure: null,
        TagsFailure: null);
}

/// <summary>Serializes refresh and version paging while retaining independent tab failures.</summary>
/// <remarks>
/// The item's attributes are read for every kind of item; versions, metadata and tags only for a
/// file, which is the only kind they describe. The two attribute changes go through the same gate,
/// so a change never races a refresh, and each reads the attributes again once it has been made so
/// what is shown is what the server now says rather than what was asked for.
/// </remarks>
public sealed class ObjectInspectorController : IAsyncDisposable
{
    private readonly IObjectInspectorAgentClient _client;
    private readonly bool _ownsClient;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private bool _disposed;

    public ObjectInspectorController(
        IObjectInspectorAgentClient client,
        ObjectInspectorAddress address,
        bool ownsClient = false,
        StorageItemKind kind = StorageItemKind.File)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        if (address?.HasValidBounds != true)
        {
            throw new ArgumentException(
                "A bounded exact object address is required.",
                nameof(address));
        }

        _ownsClient = ownsClient;
        Kind = kind;
        State = ObjectInspectorState.Empty(address);
    }

    public ObjectInspectorState State { get; private set; }

    /// <summary>What the pane listed the item as: a file has versions, metadata and tags to read.</summary>
    public StorageItemKind Kind { get; }

    public bool IsFile => Kind == StorageItemKind.File;

    public event EventHandler<ObjectInspectorState>? StateChanged;

    public async Task<ObjectInspectorState> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var address = State.Address;
            var (attributes, attributesFailure) = await ReadAttributesAsync(address, cancellationToken)
                .ConfigureAwait(false);
            if (!IsFile)
            {
                State = ObjectInspectorState.Empty(address) with
                {
                    Attributes = attributes,
                    AttributesFailure = attributesFailure
                };
                OnStateChanged();
                return State;
            }

            var versions = await _client.ListVersionsAsync(new ObjectVersionListRequest(
                ObjectInspectorIpcContract.CurrentVersion,
                WithoutVersion(address)), cancellationToken).ConfigureAwait(false);
            var metadata = await _client.GetMetadataAsync(new ObjectMetadataGetRequest(
                ObjectInspectorIpcContract.CurrentVersion,
                address), cancellationToken).ConfigureAwait(false);
            var tags = await _client.GetTagsAsync(new ObjectTagsGetRequest(
                ObjectInspectorIpcContract.CurrentVersion,
                address), cancellationToken).ConfigureAwait(false);

            State = new ObjectInspectorState(
                address,
                versions.Versions,
                versions.ContinuationToken,
                metadata.Metadata,
                tags.Tags,
                versions.Failure,
                metadata.Failure,
                tags.Failure,
                attributes,
                attributesFailure);
            OnStateChanged();
            return State;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<ObjectInspectorState> LoadMoreVersionsAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!State.CanLoadMoreVersions)
            {
                return State;
            }

            var response = await _client.ListVersionsAsync(new ObjectVersionListRequest(
                ObjectInspectorIpcContract.CurrentVersion,
                WithoutVersion(State.Address),
                ContinuationToken: State.VersionContinuationToken), cancellationToken)
                .ConfigureAwait(false);
            if (response.Failure is not null)
            {
                State = State with
                {
                    VersionContinuationToken = null,
                    VersionsFailure = response.Failure
                };
            }
            else
            {
                var combined = State.Versions.Concat(response.Versions).ToArray();
                if (combined.Select(static version => version.VersionId)
                    .Distinct(StringComparer.Ordinal).Count() != combined.Length)
                {
                    throw new InvalidDataException(
                        "The local agent returned a duplicate object version across pages.");
                }

                State = State with
                {
                    Versions = combined,
                    VersionContinuationToken = response.ContinuationToken,
                    VersionsFailure = null
                };
            }

            OnStateChanged();
            return State;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>Changes the permission bits, then reads the attributes again.</summary>
    /// <returns>Nothing when the change was made, or the agent's reason it was not.</returns>
    public Task<StorageIpcFailure?> SetPermissionsAsync(int unixMode, CancellationToken cancellationToken = default) =>
        ChangeAsync(async (address, token) =>
        {
            var response = await _client.SetItemPermissionsAsync(new ItemPermissionsSetRequest(
                ItemAttributeIpcContract.CurrentVersion, address, unixMode), token).ConfigureAwait(false);
            return response.Failure;
        }, cancellationToken);

    /// <summary>Sets the modified time, then reads the attributes again.</summary>
    /// <returns>Nothing when the change was made, or the agent's reason it was not.</returns>
    public Task<StorageIpcFailure?> SetModifiedTimeAsync(
        DateTimeOffset modifiedUtc,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(async (address, token) =>
        {
            var response = await _client.SetItemModifiedTimeAsync(new ItemModifiedTimeSetRequest(
                ItemAttributeIpcContract.CurrentVersion, address, modifiedUtc.ToUniversalTime()), token)
                .ConfigureAwait(false);
            return response.Failure;
        }, cancellationToken);

    private async Task<StorageIpcFailure?> ChangeAsync(
        Func<ObjectInspectorAddress, CancellationToken, Task<StorageIpcFailure?>> change,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var failure = await change(State.Address, cancellationToken).ConfigureAwait(false);
            if (failure is not null) return failure;

            var (attributes, attributesFailure) = await ReadAttributesAsync(State.Address, cancellationToken)
                .ConfigureAwait(false);
            State = State with { Attributes = attributes, AttributesFailure = attributesFailure };
            OnStateChanged();
            return null;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// The item's attributes, or why there are none.
    /// </summary>
    /// <remarks>
    /// An agent that refuses the request outright (one older than the attribute contract) costs
    /// the General tab only: it becomes that tab's failure and the other three still load.
    /// </remarks>
    private async Task<(ItemAttributes? Attributes, StorageIpcFailure? Failure)> ReadAttributesAsync(
        ObjectInspectorAddress address,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.GetItemAttributesAsync(new ItemAttributesGetRequest(
                ItemAttributeIpcContract.CurrentVersion, address), cancellationToken).ConfigureAwait(false);
            return (response.Attributes, response.Failure);
        }
        catch (InvalidDataException)
        {
            return (null, new StorageIpcFailure(
                "storage.attributes.agent_refused",
                StorageIpcFailureCategory.Unsupported,
                Ui.Inspector.TheAgentCouldNotReadProperties,
                IsTransient: false));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_ownsClient)
            {
                await _client.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private static ObjectInspectorAddress WithoutVersion(ObjectInspectorAddress address) => new(
        address.ConnectionId,
        address.RootIdentity,
        address.RelativePath,
        address.NativeItemId,
        VersionId: null,
        EntityTag: address.EntityTag);

    private void OnStateChanged() => StateChanged?.Invoke(this, State);
}
