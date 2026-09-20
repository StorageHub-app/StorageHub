using StorageHub.Contracts.Ipc;
using StorageHub.Desktop;

namespace StorageHub.Desktop.Avalonia.Tests;

/// <summary>
/// A storage client that answers from memory, or refuses.
/// </summary>
/// <remarks>
/// Only ListConnectionsAsync is implemented, because only the sidebar uses this so far. The rest
/// throw rather than returning something empty: a test that reaches one of them is asking a question
/// this fake cannot answer, and should say so rather than pass.
/// </remarks>
internal sealed class FakeStorageClient : IRemoteStorageAgentClient
{
    private readonly ConnectionListResponse? _response;
    private readonly Exception? _failure;

    internal FakeStorageClient(ConnectionListResponse response) => _response = response;

    internal FakeStorageClient(Exception failure) => _failure = failure;

    internal ConnectionListRequest? LastRequest { get; private set; }

    public Task<ConnectionListResponse> ListConnectionsAsync(
        ConnectionListRequest request,
        CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return _failure is not null
            ? Task.FromException<ConnectionListResponse>(_failure)
            : Task.FromResult(_response!);
    }

    public Task<ConnectionTestResponse> TestConnectionAsync(
        ConnectionTestRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<StorageListPageResponse> ListStorageAsync(
        StorageListPageRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
