using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// One arrangement a new workspace can start in: a pane count paired with the orientation that
/// shapes it. Two panes side by side and two stacked are different presets, not one preset and a
/// setting, so both the chooser and Settings can offer every arrangement in a single list.
/// </summary>
internal sealed record WorkspacePreset(int PaneCount, WorkspaceLayout Layout, string Description)
{
    /// <summary>
    /// Every distinct arrangement, in increasing pane count.
    ///
    /// One pane and four panes appear once each because <see cref="WorkspaceLayoutModel.CreatePreset"/>
    /// ignores the orientation for them: a single pane has nothing to divide, and four panes are
    /// always a 2 x 2 grid. Listing them twice would offer the user a choice that changes nothing.
    /// </summary>
    /// <remarks>
    /// Built on each access rather than cached, because the descriptions follow the current
    /// language and a cached list would pin them to whatever language was active when this type
    /// was first touched. The six presets are compared by pane count and orientation, so a fresh
    /// list still equals the one a combo box is holding.
    /// </remarks>
    internal static IReadOnlyList<WorkspacePreset> All =>
    [
        new(1, WorkspaceLayout.SideBySide, Ui.Shell.PresetSingle),
        new(2, WorkspaceLayout.SideBySide, Ui.Shell.PresetSideBySide),
        new(2, WorkspaceLayout.TopAndBottom, Ui.Shell.PresetTopAndBottom),
        new(3, WorkspaceLayout.SideBySide, Ui.Shell.PresetLargeLeftTwoStacked),
        new(3, WorkspaceLayout.TopAndBottom, Ui.Shell.PresetLargeTopTwoBeside),
        new(4, WorkspaceLayout.SideBySide, Ui.Shell.PresetGrid)
    ];

    /// <summary>Whether the orientation actually changes this pane count's arrangement.</summary>
    internal bool OrientationMatters => PaneCount is 2 or 3;

    internal string Title => Ui.Format(
        PaneCount == 1 ? Ui.Shell.PresetPaneOneFormat : Ui.Shell.PresetPaneManyFormat,
        PaneCount);

    internal string Label => Ui.Format(Ui.Shell.PresetLabelFormat, Title, Description);

    /// <summary>
    /// The preset matching a stored pane count and orientation, or null when the pane count is out
    /// of range. Falls back to the first preset for that count when the orientation does not
    /// change it, so a stored "1 pane, top and bottom" still resolves.
    /// </summary>
    internal static WorkspacePreset? Find(int paneCount, WorkspaceLayout layout) =>
        All.FirstOrDefault(preset => preset.PaneCount == paneCount && preset.Layout == layout) ??
        All.FirstOrDefault(preset => preset.PaneCount == paneCount);
}
