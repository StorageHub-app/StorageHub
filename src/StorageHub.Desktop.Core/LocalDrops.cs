using StorageHub.Contracts.Ipc;
using StorageHub.Contracts.Results;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// Turns files dropped from the operating system's own file manager into transfer selections.
/// </summary>
/// <remarks>
/// <para>
/// A drop from Explorer, Nautilus or Dolphin arrives as paths. A transfer selection is rooted in
/// one folder, so paths from several folders become several selections, each queued on its own.
/// That is the same shape a paste from a This PC pane has, which is what lets a drop and a paste
/// share everything after this point.
/// </para>
/// <para>
/// Anything that is not a file or folder on this computer is refused as a whole rather than
/// skipped: a drop that silently transfers three of four things is worse than one that says why
/// it did nothing.
/// </para>
/// </remarks>
internal static class LocalDrops
{
    internal static StorageResult<IReadOnlyList<PaneSelectionSnapshot>> From(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0) return Refused();

        var byDirectory = new Dictionary<string, List<PaneTransferItem>>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return Refused();

            var full = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(directory)) return Refused();

            StorageResult<PaneTransferItem> item;
            if (Directory.Exists(full))
            {
                item = PaneTransferItem.Create(Path.GetFileName(full), full, StorageItemKind.Directory, null);
            }
            else if (File.Exists(full))
            {
                long? length;
                DateTimeOffset? modified;
                try
                {
                    var file = new FileInfo(full);
                    length = file.Length;
                    modified = new DateTimeOffset(file.LastWriteTime);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    length = null;
                    modified = null;
                }

                // With the identity the agent finds it by, so a dropped file can replace one
                // already at the destination when that is what was chosen.
                item = PaneTransferItem.Create(
                    Path.GetFileName(full), full, StorageItemKind.File, length, modifiedUtc: modified);
                if (item.IsSuccess) item = StorageResult<PaneTransferItem>.Success(item.Value.WithLocalIdentity());
            }
            else
            {
                return Refused();
            }

            if (item.IsFailure) return StorageResult<IReadOnlyList<PaneSelectionSnapshot>>.Fail(item.Error);

            if (!byDirectory.TryGetValue(directory, out var items))
            {
                items = [];
                byDirectory[directory] = items;
            }

            items.Add(item.Value);
        }

        var selections = new List<PaneSelectionSnapshot>(byDirectory.Count);
        foreach (var (directory, items) in byDirectory)
        {
            var context = PaneTransferContext.Create(PaneTransferContextKind.ThisPc, null, null, directory);
            if (context.IsFailure) return StorageResult<IReadOnlyList<PaneSelectionSnapshot>>.Fail(context.Error);

            var selection = PaneSelectionSnapshot.Create(context.Value, items);
            if (selection.IsFailure) return StorageResult<IReadOnlyList<PaneSelectionSnapshot>>.Fail(selection.Error);

            selections.Add(selection.Value);
        }

        return StorageResult<IReadOnlyList<PaneSelectionSnapshot>>.Success(selections);
    }

    private static StorageResult<IReadOnlyList<PaneSelectionSnapshot>> Refused() =>
        StorageResult<IReadOnlyList<PaneSelectionSnapshot>>.Fail(new StorageFailure(
            "manual_transfer.drop.unusable",
            StorageFailureKind.Validation,
            Ui.Pane.DroppedItemsUnusable));
}
