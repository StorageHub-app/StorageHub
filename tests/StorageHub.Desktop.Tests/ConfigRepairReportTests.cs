namespace StorageHub.Desktop.Tests;

/// <summary>
/// "Some settings were outside their limits and have been corrected" means it.
/// </summary>
/// <remarks>
/// Preflight decides whether anything was repaired with <c>repaired != loaded</c>, and
/// <see cref="DesktopUpdatePreferences"/> is a record holding four collections: the connection
/// defaults, the shortcuts, and the pinned and recent workspaces. Record equality compares members
/// with <c>EqualityComparer&lt;T&gt;.Default</c>, which for a dictionary or a list is reference
/// equality — while <c>Repair</c> rebuilds each of those collections unconditionally.
///
/// So a settings file with any of them populated reports itself repaired on every launch, with no
/// value having changed. Anyone who has saved a shortcut or opened a workspace then sees the
/// warning at every start, which is how a warning stops being read at all.
/// </remarks>
public sealed class ConfigRepairReportTests
{
    [Fact]
    public void SettingsAlreadyInsideTheirLimitsAreNotReportedAsRepaired()
    {
        var withinLimits = DesktopUpdatePreferences.Defaults with
        {
            // Exactly the state an ordinary install reaches: shortcuts saved once, a workspace
            // opened once, provider defaults present. Every value here is inside its bounds.
            Shortcuts = ShortcutKeys.ToGestures(ShortcutSettings.Resolve(null)),
            ConnectionDefaults = ConnectionDefaultSettings.Normalize(
                new Dictionary<string, string>(StringComparer.Ordinal)),
            RecentWorkspaces = []
        };

        var repaired = DesktopConfigRepair.Repair(withinLimits);

        Assert.False(
            DesktopConfigRepair.ChangedAnything(withinLimits, repaired),
            "Repair reported a change for settings that are all inside their limits, so the " +
            "startup warning fires on every launch for anyone who has saved a shortcut or " +
            "opened a workspace.");
    }

    /// <summary>A value genuinely out of bounds still has to be reported.</summary>
    [Fact]
    public void SettingsOutsideTheirLimitsAreStillReportedAsRepaired()
    {
        var outOfBounds = DesktopUpdatePreferences.Defaults with
        {
            ConnectionsPanelWidth = DesktopUpdatePreferences.MaximumConnectionsPanelWidth + 500
        };

        Assert.True(DesktopConfigRepair.ChangedAnything(outOfBounds, DesktopConfigRepair.Repair(outOfBounds)));
    }
}
