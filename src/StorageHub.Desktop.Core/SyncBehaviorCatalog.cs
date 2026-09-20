using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// How much damage a behaviour can do, which is what decides how it is presented.
/// </summary>
/// <remarks>
/// A picker that showed nine equal-looking options would put "mirror, deleting whatever is not on
/// the other side" beside "copy new files" with nothing to tell them apart. This is what the badge
/// and the colour are read from.
/// </remarks>
internal enum SyncBehaviorRisk
{
    /// <summary>Reads both sides and changes neither.</summary>
    ReadOnly,

    /// <summary>Adds and updates, and never removes.</summary>
    Safe,

    /// <summary>What most people mean by syncing, and what a new profile starts on.</summary>
    Default,

    /// <summary>Deletes on one side to match the other.</summary>
    Destructive
}

/// <summary>One of the nine things a sync profile can be set to do.</summary>
internal sealed record SyncBehaviorOption(
    SyncIpcBehavior Behavior,
    string Title,
    string Summary,
    string Badge,
    SyncBehaviorRisk Risk);

/// <summary>
/// The nine sync behaviours, named and described.
/// </summary>
/// <remarks>
/// <para>
/// Lifted out of <c>SyncBehaviorPickerControl</c>, which owned both this list and the nine buttons
/// that drew it. The list is not a drawing concern: the tasks table names a profile's behaviour,
/// the profile editor picks one, and the run review explains what one is about to do. All three
/// need the same words, and only one of them is a picker.
/// </para>
/// <para>
/// Built on demand rather than in a static initializer, and rebuilt when the language changes. A
/// static initializer would freeze whichever language happened to be loaded the first time anything
/// asked -- which in practice is whatever the splash screen ran under.
/// </para>
/// </remarks>
internal static class SyncBehaviorCatalog
{
    private static SyncBehaviorOption[]? _options;
    private static string? _culture;

    internal static IReadOnlyList<SyncBehaviorOption> Options
    {
        get
        {
            var culture = Ui.Culture.Name;
            if (_options is { } cached && string.Equals(_culture, culture, StringComparison.Ordinal))
            {
                return cached;
            }

            var built = Build();
            _culture = culture;
            _options = built;
            return built;
        }
    }

    /// <summary>What a profile set to this behaviour is called, in a table or a summary.</summary>
    /// <remarks>
    /// Falls back to the enum's own name rather than to an empty cell: a behaviour added to the
    /// contract and not yet described here should read oddly, not read as nothing.
    /// </remarks>
    internal static string DisplayName(SyncIpcBehavior behavior) =>
        Options.FirstOrDefault(option => option.Behavior == behavior)?.Title ?? behavior.ToString();

    internal static SyncBehaviorOption? Find(SyncIpcBehavior behavior) =>
        Options.FirstOrDefault(option => option.Behavior == behavior);

    private static SyncBehaviorOption[] Build() =>
    [
        new(SyncIpcBehavior.CopyNewFilesAToB, Ui.Sync.BehaviorCopyNewAtoB,
            Ui.Sync.BehaviorCopyNewAtoBSummary, Ui.Sync.BadgeCreateOnly, SyncBehaviorRisk.Safe),
        new(SyncIpcBehavior.UpdateAToB, Ui.Sync.BehaviorUpdateAtoB,
            Ui.Sync.BehaviorUpdateAtoBSummary, Ui.Sync.BadgeDefault, SyncBehaviorRisk.Default),
        new(SyncIpcBehavior.MirrorAToB, Ui.Sync.BehaviorMirrorAtoB,
            Ui.Sync.BehaviorMirrorAtoBSummary, Ui.Sync.BadgeDeletions, SyncBehaviorRisk.Destructive),
        new(SyncIpcBehavior.CopyNewFilesBToA, Ui.Sync.BehaviorCopyNewBtoA,
            Ui.Sync.BehaviorCopyNewBtoASummary, Ui.Sync.BadgeCreateOnly, SyncBehaviorRisk.Safe),
        new(SyncIpcBehavior.UpdateBToA, Ui.Sync.BehaviorUpdateBtoA,
            Ui.Sync.BehaviorUpdateBtoASummary, Ui.Sync.BadgeSafeUpdate, SyncBehaviorRisk.Safe),
        new(SyncIpcBehavior.MirrorBToA, Ui.Sync.BehaviorMirrorBtoA,
            Ui.Sync.BehaviorMirrorBtoASummary, Ui.Sync.BadgeDeletions, SyncBehaviorRisk.Destructive),
        new(SyncIpcBehavior.TwoWaySync, Ui.Sync.BehaviorTwoWay,
            Ui.Sync.BehaviorTwoWaySummary, Ui.Sync.BadgeMerge, SyncBehaviorRisk.Safe),
        new(SyncIpcBehavior.TwoWayWithDeletionPropagation, Ui.Sync.BehaviorTwoWayDeletions,
            Ui.Sync.BehaviorTwoWayDeletionsSummary, Ui.Sync.BadgeDeletions, SyncBehaviorRisk.Destructive),
        new(SyncIpcBehavior.CompareOnly, Ui.Sync.BehaviorCompareOnly,
            Ui.Sync.BehaviorCompareOnlySummary, Ui.Sync.BadgeReadOnly, SyncBehaviorRisk.ReadOnly)
    ];
}
