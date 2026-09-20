using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// The ".." row that heads a listing anywhere but the root.
/// </summary>
/// <remarks>
/// A private static on BrowserPaneControl until the port, which meant the only way to check that
/// its caption follows the current language was reflection into a Form. It is a BrowserListItem --
/// a type that has been in Desktop.Core since the first extraction -- built from two translated
/// strings, and both shells need exactly this row.
///
/// Built on demand rather than in a static initializer, which would freeze whichever language was
/// installed the first time any pane was created.
/// </remarks>
internal static class BrowserParentNavigation
{
    private static BrowserListItem? _item;
    private static string? _culture;

    internal static BrowserListItem Item
    {
        get
        {
            var culture = Ui.Culture.Name;
            if (_item is { } cached && string.Equals(_culture, culture, StringComparison.Ordinal))
            {
                return cached;
            }

            var built = new BrowserListItem(
                "..",
                string.Empty,
                Ui.Pane.ParentFolder,
                string.Empty,
                Ui.Pane.GoUpOneLevel,
                IsContainer: true,
                Kind: StorageItemKind.Directory,
                IsParentNavigation: true);
            _culture = culture;
            _item = built;
            return built;
        }
    }
}
