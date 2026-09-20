using StorageHub.Contracts.Ipc;
using StorageHub.Contracts.Results;
using StorageHub.Contracts;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>What a batch of deletions actually managed to do.</summary>
/// <param name="Deleted">How many were removed before it stopped, if it stopped.</param>
public sealed record PaneDeleteOutcome(int Deleted, StorageFailure? Failure)
{
    public bool IsSuccess => Failure is null;
}

/// <summary>
/// Creating, renaming and deleting, on a connection or on this computer.
/// </summary>
/// <remarks>
/// <para>
/// One method per operation, and the local-versus-remote split lives inside it. In 1.x this was
/// roughly three hundred lines spread through MainForm, every one of them branching on
/// <see cref="PaneTransferContextKind"/> at the call site -- which is how a rename learned about
/// case-only renames on Windows and a create never did, and why a folder on a bucket and a folder
/// on a disk could refuse for different reasons.
/// </para>
/// <para>
/// Nothing here shows a dialog or touches a pane. Asking for a name, confirming a delete and
/// reloading the listing are the shell's, so this can be driven by a test against a fake agent and
/// a temporary directory, which is what the WinForms version could never be.
/// </para>
/// </remarks>
public sealed class PaneMutationController(
    Func<IObjectInspectorAgentClient> inspector,
    ILocalMutations? local = null) : IAsyncDisposable
{
    private readonly Func<IObjectInspectorAgentClient> _inspector =
        inspector ?? throw new ArgumentNullException(nameof(inspector));

    private readonly ILocalMutations _local = local ?? new LocalMutations();

    /// <summary>Makes an empty folder in the pane's current location.</summary>
    public Task<StorageResult> CreateFolderAsync(
        PaneTransferContext location,
        string name,
        CancellationToken cancellationToken = default) =>
        CreateAsync(location, name, container: true, cancellationToken);

    /// <summary>Makes an empty file in the pane's current location.</summary>
    public Task<StorageResult> CreateFileAsync(
        PaneTransferContext location,
        string name,
        CancellationToken cancellationToken = default) =>
        CreateAsync(location, name, container: false, cancellationToken);

    /// <summary>
    /// Renames one item, in place.
    /// </summary>
    /// <remarks>
    /// The new name has to be a plain name rather than a path: renaming is not a way to move
    /// something, and a name with a separator in it would be one.
    /// </remarks>
    public async Task<StorageResult> RenameAsync(
        PaneTransferContext location,
        PaneTransferItem item,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(item);

        if (PaneItemNameRules.Validate(newName) is { } invalid)
        {
            return StorageResult.Fail(new StorageFailure("pane.rename.name_invalid", StorageFailureKind.Validation, invalid));
        }

        if (string.Equals(item.Name, newName, StringComparison.Ordinal))
        {
            return StorageResult.Success();
        }

        try
        {
            if (location.Kind == PaneTransferContextKind.ThisPc)
            {
                _local.Rename(item.RelativePath, newName, item.IsContainer);
                return StorageResult.Success();
            }

            if (!Addressable(location, out var connectionId, out var rootIdentity))
            {
                return StorageResult.Fail(new StorageFailure(
                    "pane.rename.no_identity", StorageFailureKind.Validation, Ui.Shell.OpenFolderBeforeRenaming));
            }

            await using var client = _inspector();
            var response = await client.RenameItemAsync(
                new StorageItemRenameRequest(
                    EditableFileIpcContract.CurrentVersion,
                    new ObjectInspectorAddress(
                        connectionId,
                        rootIdentity,
                        item.RelativePath,
                        item.NativeItemId,
                        item.VersionId,
                        item.EntityTag),
                    Child(connectionId, rootIdentity, location.RelativePath, newName)),
                cancellationToken).ConfigureAwait(false);

            return response.Failure is { } failure
                ? StorageResult.Fail(Relay(failure))
                : StorageResult.Success();
        }
        catch (OperationCanceledException)
        {
            return StorageResult.Fail(new StorageFailure("pane.rename.cancelled", StorageFailureKind.Cancelled, Ui.Shell.OperationCancelled));
        }
        catch (Exception error) when (IsMutationFailure(error))
        {
            return StorageResult.Fail(new StorageFailure("pane.rename.failed", StorageFailureKind.Provider, error.Message));
        }
    }

    /// <summary>
    /// Deletes a selection, and says how far it got.
    /// </summary>
    /// <remarks>
    /// One at a time, stopping at the first refusal and reporting the count -- because "three of
    /// five were deleted, then this happened" is a different situation from "nothing was deleted",
    /// and a caller that cannot tell them apart cannot say anything useful about either. Folders go
    /// recursively, which is the only way a provider will take one.
    /// </remarks>
    public async Task<PaneDeleteOutcome> DeleteAsync(
        PaneTransferContext location,
        IReadOnlyList<PaneTransferItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0) return new PaneDeleteOutcome(0, null);

        var deleted = 0;
        try
        {
            if (location.Kind == PaneTransferContextKind.ThisPc)
            {
                foreach (var item in items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _local.Delete(item.RelativePath, item.IsContainer);
                    deleted++;
                }

                return new PaneDeleteOutcome(deleted, null);
            }

            if (!Addressable(location, out var connectionId, out var rootIdentity))
            {
                return new PaneDeleteOutcome(0, new StorageFailure(
                    "pane.delete.no_identity", StorageFailureKind.Validation, Ui.Shell.RemoteDeleteRequiresRoot));
            }

            await using var client = _inspector();
            foreach (var item in items)
            {
                var response = await client.DeleteItemAsync(
                    new StorageItemDeleteRequest(
                        EditableFileIpcContract.CurrentVersion,
                        new ObjectInspectorAddress(
                            connectionId,
                            rootIdentity,
                            item.RelativePath,
                            item.NativeItemId,
                            item.VersionId,
                            item.EntityTag),
                        Recursive: item.IsContainer),
                    cancellationToken).ConfigureAwait(false);

                if (response.Failure is { } failure)
                {
                    return new PaneDeleteOutcome(
                        deleted, Relay(failure));
                }

                deleted++;
            }

            return new PaneDeleteOutcome(deleted, null);
        }
        catch (OperationCanceledException)
        {
            return new PaneDeleteOutcome(
                deleted, new StorageFailure("pane.delete.cancelled", StorageFailureKind.Cancelled, Ui.Shell.OperationCancelled));
        }
        catch (Exception error) when (IsMutationFailure(error))
        {
            return new PaneDeleteOutcome(deleted, new StorageFailure("pane.delete.failed", StorageFailureKind.Provider, error.Message));
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<StorageResult> CreateAsync(
        PaneTransferContext location,
        string name,
        bool container,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);

        if (PaneItemNameRules.Validate(name) is { } invalid)
        {
            return StorageResult.Fail(new StorageFailure("pane.create.name_invalid", StorageFailureKind.Validation, invalid));
        }

        if (location.Kind is not (PaneTransferContextKind.ThisPc or PaneTransferContextKind.SavedConnection))
        {
            return StorageResult.Fail(new StorageFailure(
                "pane.create.no_location", StorageFailureKind.Validation, Ui.Shell.OpenFolderBeforeCreating));
        }

        try
        {
            if (location.Kind == PaneTransferContextKind.ThisPc)
            {
                _local.Create(location.RelativePath, name, container);
                return StorageResult.Success();
            }

            if (!Addressable(location, out var connectionId, out var rootIdentity))
            {
                return StorageResult.Fail(new StorageFailure(
                    "pane.create.no_identity", StorageFailureKind.Validation, Ui.Shell.PaneHasNoVerifiedIdentity));
            }

            var address = Child(connectionId, rootIdentity, location.RelativePath, name);
            await using var client = _inspector();
            var failure = container
                ? (await client.CreateDirectoryAsync(
                    new StorageDirectoryCreateRequest(EditableFileIpcContract.CurrentVersion, address),
                    cancellationToken).ConfigureAwait(false)).Failure
                : (await client.CreateFileAsync(
                    new StorageFileCreateRequest(EditableFileIpcContract.CurrentVersion, address),
                    cancellationToken).ConfigureAwait(false)).Failure;

            return failure is null
                ? StorageResult.Success()
                : StorageResult.Fail(Relay(failure));
        }
        catch (OperationCanceledException)
        {
            return StorageResult.Fail(new StorageFailure("pane.create.cancelled", StorageFailureKind.Cancelled, Ui.Shell.OperationCancelled));
        }
        catch (Exception error) when (IsMutationFailure(error))
        {
            return StorageResult.Fail(new StorageFailure("pane.create.failed", StorageFailureKind.Provider, error.Message));
        }
    }

    /// <summary>
    /// Passes an agent's refusal through without reinterpreting it.
    /// </summary>
    /// <remarks>
    /// The code and the message are the provider's. The kind is Provider because the shell is not
    /// better placed than the thing that refused to say what sort of refusal it was, and guessing
    /// would put a wrong word in front of somebody trying to work out what went wrong.
    /// </remarks>
    private static StorageFailure Relay(StorageIpcFailure failure) =>
        new(failure.Code, StorageFailureKind.Provider, failure.Message);

    /// <summary>Whether the pane knows enough about where it is to name a child of it.</summary>
    private static bool Addressable(
        PaneTransferContext location,
        out Guid connectionId,
        out string rootIdentity)
    {
        connectionId = location.ConnectionId ?? Guid.Empty;
        rootIdentity = location.RootIdentity ?? string.Empty;
        return location.Kind == PaneTransferContextKind.SavedConnection &&
            connectionId != Guid.Empty &&
            rootIdentity.Length > 0;
    }

    private static ObjectInspectorAddress Child(
        Guid connectionId,
        string rootIdentity,
        string parent,
        string name) =>
        new(connectionId, rootIdentity, parent.Length == 0 ? name : parent + "/" + name);

    /// <summary>
    /// The failures a mutation is allowed to report rather than crash on.
    /// </summary>
    /// <remarks>
    /// Every one of these is something the person can act on: a name that is taken, a folder they
    /// cannot write to, an agent that went away. Anything else is a bug and is left to throw.
    /// </remarks>
    private static bool IsMutationFailure(Exception error) => error is
        IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or
        InvalidOperationException or TimeoutException or NotSupportedException;
}
