using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// Turns what a pane has selected into something the object inspector can be pointed at.
/// </summary>
/// <remarks>
/// Three rules, each with its own sentence: exactly one file, opened through a saved connection
/// whose root the agent has identified, and carrying an identity the contract accepts. The pane's
/// Properties command is enabled by the same rules, so the sentences are for the menu path, where
/// nothing dims.
/// </remarks>
internal static class PaneInspection
{
    /// <summary>The address to inspect, or why there is none.</summary>
    internal static ObjectInspectorAddress? AddressFor(
        PaneTransferContext? context,
        IReadOnlyList<PaneTransferItem> items,
        out string? problem)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count != 1 || items[0].Kind != StorageItemKind.File)
        {
            problem = Ui.Shell.SelectOneToInspect;
            return null;
        }

        if (context is null ||
            context.Kind != PaneTransferContextKind.SavedConnection ||
            context.ConnectionId is not { } connectionId ||
            string.IsNullOrWhiteSpace(context.RootIdentity))
        {
            problem = Ui.Shell.InspectionRequiresConnection;
            return null;
        }

        var item = items[0];
        var address = new ObjectInspectorAddress(
            connectionId,
            context.RootIdentity,
            item.RelativePath,
            item.NativeItemId,
            item.VersionId,
            item.EntityTag);
        if (!address.HasValidBounds)
        {
            problem = Ui.Shell.FileHasNoObjectIdentity;
            return null;
        }

        problem = null;
        return address;
    }

    /// <summary>Whether the selection could be inspected at all, for a button to dim on.</summary>
    internal static bool CanInspect(PaneTransferContext? context, IReadOnlyList<PaneTransferItem> items) =>
        AddressFor(context, items, out _) is not null;
}
