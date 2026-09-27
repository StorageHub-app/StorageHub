using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop.Views;

/// <summary>
/// An agent with no sync profiles and no runs, behind the Sync tasks screens of a preview shell.
/// </summary>
/// <remarks>
/// Coming to Sync tasks reads the agent, and the shells the tests photograph come to it. Over the
/// pipe that read waited out a connect timeout, landed on a sample shared between tests whenever it
/// finished, and with a dev agent running put somebody's real profiles in the pictures. This answers
/// at once with what a fresh install has, and leaves Refresh available, as it is on the reference
/// screenshot. Everything else those screens can ask for is about a run, and there is none.
/// </remarks>
internal sealed class EmptySyncAgent : ISyncManagementAgentClient
{
    internal static Func<ISyncManagementAgentClient> Factory { get; } = static () => new EmptySyncAgent();

    public Task<SyncProfileListResponse> ListProfilesAsync(
        SyncProfileListRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SyncProfileListResponse(SyncManagementIpcContract.CurrentVersion, []));

    public Task<SyncProfileGetResponse> GetProfileAsync(
        SyncProfileGetRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<SyncProfileMutationResponse> CreateProfileAsync(
        SyncProfileCreateRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<SyncProfileMutationResponse> UpdateProfileAsync(
        SyncProfileUpdateRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<SyncPreviewGenerateResponse> GeneratePreviewAsync(
        SyncPreviewGenerateRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<SyncRunStatusResponse> GetRunStatusAsync(
        SyncRunStatusRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<SyncPlanPageResponse> GetPlanPageAsync(
        SyncPlanPageRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<SyncConflictPageResponse> GetConflictPageAsync(
        SyncConflictPageRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<SyncApproveDispatchResponse> ApproveAndDispatchAsync(
        SyncApproveDispatchRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
