using System.Globalization;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Configuration;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Updates;

namespace StorageHub.Desktop.Settings;

/// <summary>What a settings row is edited with.</summary>
internal enum SettingsControlKind
{
    Toggle,
    Choice,
    Number,

    /// <summary>A file on this computer, typed or chosen with a Browse button.</summary>
    Path,

    /// <summary>Free text, such as a default initial path or a startup command.</summary>
    Text,

    /// <summary>Text with suggestions to pick from, such as a terminal type or a font.</summary>
    EditableChoice,

    /// <summary>A vault reference, which is imported or cleared rather than typed.</summary>
    Secret
}

/// <summary>One option in a <see cref="SettingsControlKind.Choice"/> row.</summary>
/// <param name="Value">What is stored. Stable; it survives a rename of the label.</param>
/// <param name="Label">What is read, in the current language.</param>
internal sealed record SettingsChoice(string Value, string Label);

/// <summary>
/// One setting: how it reads, how it is edited, and how it moves in and out of the preferences.
/// </summary>
/// <remarks>
/// <para>
/// The value is carried as text whatever the control is -- <c>true</c>, <c>4</c>, <c>dracula</c>.
/// That is what lets one view template serve every row and one test cover every round trip, rather
/// than a page of hand-written bindings per section. SettingsForm was 1,827 lines largely because
/// it did not do this.
/// </para>
/// <para>
/// <see cref="Read"/> and <see cref="Write"/> are delegates rather than reflection: a row that
/// names a property that has been renamed is then a compile error instead of a setting that
/// silently stops saving.
/// </para>
/// </remarks>
internal sealed record SettingsRowDefinition
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    /// <summary>The sentence under the label. Null for a row that needs no explaining.</summary>
    public string? Hint { get; init; }

    /// <summary>
    /// The sentence under the label when it depends on what is chosen, as the host-key modes each
    /// explain themselves. Null, or a null answer, falls back to <see cref="Hint"/>.
    /// </summary>
    public Func<string, string?>? HintFor { get; init; }

    /// <summary>Draws the hint in the warning colour, for a limit worth noticing.</summary>
    public bool HintIsWarning { get; init; }

    public required SettingsControlKind Kind { get; init; }

    public IReadOnlyList<SettingsChoice> Choices { get; init; } = [];

    /// <summary>
    /// What an editable choice offers. A function rather than a list, so the fonts installed on
    /// this computer are asked for when the row is drawn rather than when the catalog is built.
    /// </summary>
    public Func<IReadOnlyList<string>>? Suggestions { get; init; }

    public int Minimum { get; init; }

    /// <summary>
    /// A floor that follows another setting, raised over <see cref="Minimum"/>. A maximum cannot
    /// go below "Start with", which the settings file would refuse, so its field stops there
    /// rather than showing a number that will not be saved.
    /// </summary>
    public Func<DesktopUpdatePreferences, int>? MinimumFor { get; init; }

    public int Maximum { get; init; }

    /// <summary>Digits after the point. Only the terminal's font size is not a whole number.</summary>
    public int DecimalPlaces { get; init; }

    public decimal Increment { get; init; } = 1;

    /// <summary>What a number counts, shown inside its field after the value: "4 jobs".</summary>
    public string? Unit { get; init; }

    /// <summary>
    /// Groups a number's thousands, "16,384 KiB", on the rows 1.4 grouped them on: the ones whose
    /// range runs into the thousands.
    /// </summary>
    public bool ThousandsSeparator { get; init; }

    /// <summary>
    /// The most a text row takes, or 0 for no limit. It is the limit the preferences themselves
    /// keep to, so what is typed is never longer than what would be saved.
    /// </summary>
    public int MaxLength { get; init; }

    /// <summary>What an empty field says.</summary>
    public string? Placeholder { get; init; }

    /// <summary>
    /// Whether the row can be changed, given everything else. Null is always. A row that cannot
    /// is dimmed, not hidden, as 1.x did: "Start with" means nothing without adaptive
    /// concurrency, but it is still worth seeing what it would start with.
    /// </summary>
    public Func<DesktopUpdatePreferences, bool>? Enabled { get; init; }

    public required Func<DesktopUpdatePreferences, string> Read { get; init; }

    public required Func<DesktopUpdatePreferences, string, DesktopUpdatePreferences> Write { get; init; }

    /// <summary>
    /// Why a value will not be kept, or null when it will. Only rows a person types into need one:
    /// a toggle, a choice and a clamped number cannot hold anything Write would refuse.
    /// </summary>
    public Func<string, string?>? Validate { get; init; }

    /// <summary>The Browse picker's title, for a <see cref="SettingsControlKind.Path"/> row.</summary>
    public string? BrowseTitle { get; init; }
}

/// <summary>
/// Rows that belong together, drawn as one card under a capitalised caption.
/// </summary>
/// <remarks>
/// 1.4 grouped a page's rows this way, sharing edges and separated by hairlines, and 2.0 had
/// lost it: every row was a card of its own, so a page read as a pile rather than as a list.
/// </remarks>
internal sealed record SettingsGroupDefinition(string? Caption, IReadOnlyList<SettingsRowDefinition> Rows)
{
    /// <summary>A sentence the card carries, below its rows or in place of them.</summary>
    public string? Note { get; init; }

    public bool NoteIsWarning { get; init; }

    /// <summary>
    /// Draws the note as a row's title rather than as muted text, for a card that has nothing
    /// else to say, as 1.4 drew "no reusable defaults" on the Local page.
    /// </summary>
    public bool NoteIsTitle { get; init; }
}

/// <summary>A page in the settings navigation, and the rows on it.</summary>
internal sealed record SettingsPageDefinition
{
    public required string Key { get; init; }

    /// <summary>What the navigation calls the page.</summary>
    public required string Title { get; init; }

    /// <summary>
    /// The heading over the page, when it says more than the navigation does: "Editing" in the
    /// list is "External editing" on the page, and "FTP" is "FTP defaults".
    /// </summary>
    public string? Heading { get; init; }

    public required string Description { get; init; }

    public required UiGlyph Glyph { get; init; }

    public IReadOnlyList<SettingsGroupDefinition> Groups { get; init; } = [];

    /// <summary>The page this one sits under in the navigation, as a provider sits under trust.</summary>
    public string? ParentKey { get; init; }

    /// <summary>The caption over it among its siblings: Storage or Clients.</summary>
    public string? Group { get; init; }

    /// <summary>The provider whose defaults the page holds, which is what its button creates.</summary>
    public StorageProviderKind? Provider { get; init; }

    /// <summary>A muted paragraph under the cards.</summary>
    public string? Footnote { get; init; }

    public IReadOnlyList<SettingsRowDefinition> Rows => [.. Groups.SelectMany(group => group.Rows)];
}

/// <summary>
/// The settings pages, as data.
/// </summary>
/// <remarks>
/// <para>
/// In 1.4's order and with 1.4's grouping (ui-reference 02): Transfers &amp; sync first, holding
/// what 2.0 had split into Performance and Confirmations; Shortcuts; Connections &amp; trust, with
/// each provider's new-connection defaults under a Storage or Clients caption; the toolbar; the
/// background agent; and updates. What 2.0 added -- the colour scheme, the panel side, the total
/// speed limits -- is filed where 1.4 would have put it.
/// </para>
/// <para>
/// The labels are the WinForms shell's, already translated into Danish and German. Reusing them
/// rather than writing new ones is not laziness: it is the difference between a port and a rewrite
/// that quietly says something else in two languages nobody on the team reads.
/// </para>
/// </remarks>
internal static class SettingsPageCatalog
{
    /// <summary>
    /// The pages whose editor is a screen rather than a list of rows.
    /// </summary>
    /// <remarks>
    /// Named rather than spelled out at each use, so each place the window branches on one says
    /// which page it means. They are still declared in <see cref="Pages"/>, with no rows, so the
    /// navigation stays one list.
    /// </remarks>
    internal const string ToolbarPageKey = "toolbar";

    internal const string ShortcutsPageKey = "shortcuts";

    internal const string AgentPageKey = "agent";

    /// <summary>
    /// Transfers &amp; sync. The key is still "performance", the name the Speed Limits command has
    /// opened it by since the page was called that.
    /// </summary>
    internal const string PerformancePageKey = "performance";

    internal const string ConnectionsPageKey = "connections";

    internal static string ProviderPageKey(StorageProviderKind provider) => $"provider:{provider}";

    internal static IReadOnlyList<SettingsPageDefinition> Pages { get; } =
    [
        TransfersPage(),
        EditingPage(),
        AppearancePage(),
        WorkspacePage(),
        new()
        {
            Key = ShortcutsPageKey,
            Title = Ui.Settings.CategoryShortcuts,
            Description = Ui.Settings.PageShortcutsDescription,
            Glyph = UiGlyph.Keyboard
        },
        ConnectionsPage(),
        .. ConnectionProviderCatalog.All
            .OrderBy(provider => provider.Type == ConnectionProfileType.Storage ? 0 : 1)
            .Select(ProviderPage),
        new()
        {
            Key = ToolbarPageKey,
            Title = Ui.Settings.CategoryToolbar,
            Description = Ui.Settings.PageToolbarDescription,
            Glyph = UiGlyph.Layers
        },
        new()
        {
            Key = AgentPageKey,
            Title = Ui.Settings.CategoryAgent,
            Description = Ui.Settings.PageAgentDescription,
            Glyph = UiGlyph.Server
        },
        UpdatesPage()
    ];

    /// <summary>Every row on every page, for a caller that wants them without the grouping.</summary>
    internal static IEnumerable<SettingsRowDefinition> AllRows => Pages.SelectMany(page => page.Rows);

    /// <summary>
    /// Brings the rows that depend on one another back into agreement after one of them changed.
    /// </summary>
    /// <remarks>
    /// The adaptive controller starts at "Start with" and never goes above either maximum, so the
    /// settings file refuses a start above them and throws the whole concurrency block back to its
    /// defaults. 1.4 raised the maximums as the start was raised, and so does this. It is kept out
    /// of the rows' own Write so that each row still moves exactly one preference.
    /// </remarks>
    internal static DesktopUpdatePreferences Settle(DesktopUpdatePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var minimum = preferences.MinimumConcurrency;
        return preferences.MaximumTransferConcurrency >= minimum && preferences.MaximumSyncConcurrency >= minimum
            ? preferences
            : preferences with
            {
                MaximumTransferConcurrency = Math.Max(preferences.MaximumTransferConcurrency, minimum),
                MaximumSyncConcurrency = Math.Max(preferences.MaximumSyncConcurrency, minimum)
            };
    }

    private static SettingsPageDefinition TransfersPage() => new()
    {
        Key = PerformancePageKey,
        Title = Ui.Settings.CategoryTransfersAndSync,
        Description = Ui.Settings.PagePerformanceDescription,
        Glyph = UiGlyph.Speed,
        Groups =
        [
            new(Ui.Settings.SectionConcurrency,
            [
                new()
                {
                    Key = "adaptive-concurrency",
                    Label = Ui.Settings.AdaptiveConcurrency,
                    Hint = Ui.Settings.AdaptiveConcurrencyHint,
                    Kind = SettingsControlKind.Toggle,
                    Read = p => Text(p.AdaptiveConcurrency),
                    Write = (p, v) => p with { AdaptiveConcurrency = Flag(v) }
                },
                Concurrency(
                    "start-with", Ui.Settings.StartWith, Ui.Settings.MinimumConcurrencyHint, 8,
                    static p => p.MinimumConcurrency,
                    static (p, value) => p with { MinimumConcurrency = value },
                    enabled: static p => p.AdaptiveConcurrency),
                Concurrency(
                    "maximum-transfers", Ui.Settings.MaximumTransfers, Ui.Settings.MaximumTransfersHint, 32,
                    static p => p.MaximumTransferConcurrency,
                    static (p, value) => p with { MaximumTransferConcurrency = value },
                    floor: static p => p.MinimumConcurrency),
                Concurrency(
                    "per-connection", Ui.Settings.PerConnection, Ui.Settings.PerConnectionHint, 16,
                    static p => p.PerConnectionConcurrency,
                    static (p, value) => p with { PerConnectionConcurrency = value }),
                Concurrency(
                    "maximum-synchronizations", Ui.Settings.MaximumSynchronizations,
                    Ui.Settings.MaximumSynchronizationsHint, 8,
                    static p => p.MaximumSyncConcurrency,
                    static (p, value) => p with { MaximumSyncConcurrency = value },
                    floor: static p => p.MinimumConcurrency)
            ]),

            // New in 1.2, so 1.4's page has no place for them. They govern the same transfers
            // the concurrency does, which is why they follow it rather than getting a page.
            new(Ui.Settings.SectionSpeedLimits,
            [
                SpeedLimitRow(
                    "total-upload-limit",
                    Ui.Settings.TotalUploadLimit,
                    Ui.Settings.TotalSpeedLimitHint,
                    static p => p.TotalUploadBytesPerSecond,
                    static (p, limit) => p with { TotalUploadBytesPerSecond = limit }),
                SpeedLimitRow(
                    "total-download-limit",
                    Ui.Settings.TotalDownloadLimit,
                    null,
                    static p => p.TotalDownloadBytesPerSecond,
                    static (p, limit) => p with { TotalDownloadBytesPerSecond = limit })
            ]),

            // New in 2.0. Which zone a new schedule starts on, and nothing more: a schedule keeps
            // the zone it was saved with, and every time shown in the shell follows the system.
            new(Ui.Settings.SectionSchedules,
            [
                new()
                {
                    Key = "new-schedule-time-zone",
                    Label = Ui.Settings.NewScheduleTimeZone,
                    Hint = Ui.Settings.NewScheduleTimeZoneHint,
                    Kind = SettingsControlKind.Choice,
                    Choices =
                    [
                        new(ScheduleTimeZones.FollowSystem, Ui.Settings.TimeZoneFollowSystem),
                        .. ScheduleTimeZones.All.Select(zone => new SettingsChoice(zone.Id, ScheduleTimeZones.Caption(zone)))
                    ],
                    Read = p => ScheduleTimeZones.Normalize(p.NewScheduleTimeZone) ?? ScheduleTimeZones.FollowSystem,
                    // Following is null rather than the zone the system is on today, so a machine
                    // that moves takes its new schedules with it. A zone this machine does not
                    // know changes nothing.
                    Write = (p, v) => v == ScheduleTimeZones.FollowSystem
                        ? p with { NewScheduleTimeZone = null }
                        : ScheduleTimeZones.Normalize(v) is { } zone
                            ? p with { NewScheduleTimeZone = zone }
                            : p
                }
            ]),

            new(Ui.Settings.SectionConfirmations,
            [
                new()
                {
                    Key = "warn-clearing-history",
                    Label = Ui.Settings.WarnClearingHistory,
                    Hint = Ui.Settings.WarnClearingHistoryHint,
                    Kind = SettingsControlKind.Toggle,
                    Read = p => Text(p.ConfirmBeforeClearingTransferHistory),
                    Write = (p, v) => p with { ConfirmBeforeClearingTransferHistory = Flag(v) }
                },
                new()
                {
                    Key = "warn-deleting-items",
                    Label = Ui.Settings.WarnDeletingItems,
                    Hint = Ui.Settings.WarnDeletingItemsHint,
                    Kind = SettingsControlKind.Toggle,
                    Read = p => Text(p.ConfirmBeforeDeletingItems),
                    Write = (p, v) => p with { ConfirmBeforeDeletingItems = Flag(v) }
                }
            ])
        ]
    };

    // What happens when a remote file is opened. The unsafe-edit warning lives here rather than
    // under Confirmations because the warning itself says so: "you can restore this warning later
    // in Settings under Editing".
    private static SettingsPageDefinition EditingPage() => new()
    {
        Key = "editing",
        Title = Ui.Settings.CategoryEditing,
        Heading = Ui.Settings.PageExternalEditing,
        Description = Ui.Settings.PageEditingDescription,
        Glyph = UiGlyph.Rename,
        Groups =
        [
            new(Ui.Settings.PageExternalEditing,
            [
                new()
                {
                    Key = "external-editor",
                    Label = Ui.Settings.EditorExecutable,
                    Hint = Ui.Settings.EditorHint,
                    Kind = SettingsControlKind.Path,
                    Placeholder = Ui.Settings.EditorPlaceholder,
                    BrowseTitle = Ui.Settings.ChooseEditorTitle,
                    Read = p => p.ExternalEditorPath ?? string.Empty,
                    // Blank means the system's own choice; anything else must be a full path, the
                    // rule the settings file itself enforces. A value that is neither is left out
                    // of the working copy rather than saved and dropped on the next load.
                    Write = (p, v) => string.IsNullOrWhiteSpace(v)
                        ? p with { ExternalEditorPath = null }
                        : DesktopConfigRepair.IsValidEditorPath(v.Trim())
                            ? p with { ExternalEditorPath = v.Trim() }
                            : p,
                    Validate = v => string.IsNullOrWhiteSpace(v) || DesktopConfigRepair.IsValidEditorPath(v.Trim())
                        ? null
                        : Ui.Settings.EditorPathMustBeFull
                },
                new()
                {
                    Key = "maximum-editable-kib",
                    Label = Ui.Settings.MaximumEditableSize,
                    Hint = Ui.Settings.MaximumEditableSizeHint,
                    // A limit that is worth noticing before a large file is refused, as 1.4 drew it.
                    HintIsWarning = true,
                    Kind = SettingsControlKind.Number,
                    Unit = Ui.Settings.UnitKibibytes,
                    ThousandsSeparator = true,
                    Minimum = 1,
                    Maximum = EditableFileIpcContract.MaximumContentBytes / 1024,
                    // Kilobytes on screen, bytes in the file, as 1.x had it.
                    Read = p => Text(Math.Clamp(p.MaximumEditableFileBytes / 1024, 1, EditableFileIpcContract.MaximumContentBytes / 1024)),
                    Write = (p, v) => p with
                    {
                        MaximumEditableFileBytes = Number(
                            v, 1, EditableFileIpcContract.MaximumContentBytes / 1024,
                            p.MaximumEditableFileBytes / 1024) * 1024
                    }
                },
                new()
                {
                    Key = "warn-unsafe-edit",
                    Label = Ui.Settings.WarnUnsafeEdit,
                    Hint = Ui.Settings.WarnUnsafeEditHint,
                    Kind = SettingsControlKind.Toggle,
                    Read = p => Text(p.WarnBeforeUnsafeExternalEdit),
                    Write = (p, v) => p with { WarnBeforeUnsafeExternalEdit = Flag(v) }
                }
            ])
        ]
    };

    private static SettingsPageDefinition AppearancePage() => new()
    {
        Key = "appearance",
        Title = Ui.Settings.CategoryAppearance,
        Description = Ui.Settings.PageAppearanceDescription,
        Glyph = UiGlyph.Theme,
        Groups =
        [
            new(Ui.Settings.Theme,
            [
                new()
                {
                    Key = "color-scheme",
                    Label = Ui.Settings.ColorScheme,
                    Hint = Ui.Settings.ColorSchemeHint,
                    Kind = SettingsControlKind.Choice,
                    Choices = [.. ColorSchemeCatalog.All.Select(s => new SettingsChoice(s.Id, s.Name))],
                    Read = p => p.ColorScheme ?? ColorSchemeCatalog.DefaultDarkId,
                    // Only an id the catalog knows. Resolve falls back at read time, so an
                    // unknown one would still open a working shell -- but the file would hold a
                    // scheme that does not exist, and Settings would show the first one selected
                    // while claiming otherwise. Values here arrive from settings import too.
                    Write = (p, v) => p with
                    {
                        ColorScheme = ColorSchemeCatalog.Knows(v) ? v : p.ColorScheme
                    }
                },
                new()
                {
                    Key = "appearance",
                    Label = Ui.Settings.Theme,
                    Hint = Ui.Settings.ThemeHint,
                    Kind = SettingsControlKind.Choice,
                    Choices =
                    [
                        new(nameof(DesktopAppearance.Light), Ui.Settings.ThemeLight),
                        new(nameof(DesktopAppearance.Dark), Ui.Settings.ThemeDark),
                        new(nameof(DesktopAppearance.System), Ui.Settings.ThemeSystem)
                    ],
                    Read = p => p.Appearance.ToString(),
                    Write = (p, v) => p with
                    {
                        Appearance = Enum.TryParse<DesktopAppearance>(v, out var parsed)
                            ? parsed
                            : p.Appearance
                    }
                },
                // Under the theme, where 1.4 had it: it changes how the connections panel looks,
                // not how a workspace behaves.
                new()
                {
                    Key = "show-favourites-in-folders",
                    Label = Ui.Settings.ShowFavoritesInFolders,
                    Hint = Ui.Settings.ShowFavoritesInFoldersHint,
                    Kind = SettingsControlKind.Toggle,
                    Read = p => Text(p.ShowFavoritesInTheirFolders),
                    Write = (p, v) => p with { ShowFavoritesInTheirFolders = Flag(v) }
                },
                new()
                {
                    Key = "connections-panel-side",
                    Label = Ui.Settings.ConnectionsPanelSide,
                    Kind = SettingsControlKind.Choice,
                    Choices =
                    [
                        new(nameof(ConnectionsPanelSide.Left), Ui.Settings.PanelSideLeft),
                        new(nameof(ConnectionsPanelSide.Right), Ui.Settings.PanelSideRight)
                    ],
                    Read = p => p.ConnectionsPanelSide.ToString(),
                    Write = (p, v) => p with
                    {
                        ConnectionsPanelSide = Enum.TryParse<ConnectionsPanelSide>(v, out var parsed)
                            ? parsed
                            : p.ConnectionsPanelSide
                    }
                }
            ]),
            // A card of its own, as in 1.4. Saving it does not change the words on screen: the
            // window offers to restart the shell, which is what reads them again.
            new(Ui.Settings.Language,
            [
                new()
                {
                    Key = "language",
                    Label = Ui.Settings.Language,
                    Hint = Ui.Settings.LanguageHint,
                    Kind = SettingsControlKind.Choice,
                    Choices = [.. LanguageSettings().Select(value => new SettingsChoice(value, DesktopCulture.Describe(value)))],
                    Read = p => LanguageSetting(p.Language) ?? DesktopCulture.AutomaticLanguage,
                    // Only a language this build ships, or automatic, which is also all the
                    // settings file keeps when it is read back.
                    Write = (p, v) => p with { Language = LanguageSetting(v) ?? p.Language }
                }
            ])
        ]
    };

    /// <summary>Following the system first, then each shipped language.</summary>
    private static IEnumerable<string> LanguageSettings() =>
        [DesktopCulture.AutomaticLanguage, .. DesktopCulture.SupportedCultures];

    /// <summary>
    /// The language setting a value names, spelt as this build spells it, or null when it names none.
    /// </summary>
    private static string? LanguageSetting(string? value) =>
        LanguageSettings().FirstOrDefault(setting => string.Equals(setting, value, StringComparison.OrdinalIgnoreCase));

    private static SettingsPageDefinition WorkspacePage() => new()
    {
        Key = "workspace",
        Title = Ui.Settings.CategoryWorkspace,
        Description = Ui.Settings.PageWorkspaceDescription,
        Glyph = UiGlyph.Layers,
        Groups =
        [
            new(Ui.Settings.CategoryWorkspace,
            [
                new()
                {
                    Key = "workspace-layout",
                    Label = Ui.Settings.DefaultPaneLayout,
                    Hint = Ui.Settings.DefaultPaneLayoutHint,
                    Kind = SettingsControlKind.Choice,
                    Choices =
                    [
                        new(nameof(WorkspaceLayout.SideBySide), Ui.Settings.LayoutSideBySide),
                        new(nameof(WorkspaceLayout.TopAndBottom), Ui.Settings.LayoutTopAndBottom)
                    ],
                    Read = p => p.DefaultWorkspaceLayout.ToString(),
                    Write = (p, v) => p with
                    {
                        DefaultWorkspaceLayout = Enum.TryParse<WorkspaceLayout>(v, out var parsed)
                            ? parsed
                            : p.DefaultWorkspaceLayout
                    }
                },
                // "Ask every time" or one of the chooser's six arrangements. Picking one here is
                // how the chooser's "stop asking" is changed or undone, as in 1.4. A two- or
                // three-pane arrangement carries its orientation, so this row and the one above
                // move together and cannot describe two different workspaces.
                new()
                {
                    Key = "new-workspace-layout",
                    Label = Ui.Settings.NewWorkspaceLayout,
                    Hint = Ui.Settings.WorkspacePresetHint,
                    Kind = SettingsControlKind.Choice,
                    Choices =
                    [
                        new(AskEveryTime, Ui.Settings.LayoutAskEveryTime),
                        .. WorkspacePreset.All.Select(preset => new SettingsChoice(PresetValue(preset), preset.Label))
                    ],
                    Read = p => p.DefaultWorkspacePaneCount is { } panes &&
                        WorkspacePreset.Find(panes, p.DefaultWorkspaceLayout) is { } preset
                            ? PresetValue(preset)
                            : AskEveryTime,
                    Write = (p, v) => WorkspacePreset.All.FirstOrDefault(preset => PresetValue(preset) == v) is { } chosen
                        ? p with
                        {
                            DefaultWorkspacePaneCount = chosen.PaneCount,
                            DefaultWorkspaceLayout = chosen.OrientationMatters ? chosen.Layout : p.DefaultWorkspaceLayout
                        }
                        : p with { DefaultWorkspacePaneCount = null }
                },
                new()
                {
                    Key = "reconnect-remote-panes",
                    Label = Ui.Settings.ReconnectRemotePanes,
                    Hint = Ui.Settings.ReconnectRemotePanesHint,
                    Kind = SettingsControlKind.Toggle,
                    Read = p => Text(p.ReconnectRemotePanesAutomatically),
                    Write = (p, v) => p with { ReconnectRemotePanesAutomatically = Flag(v) }
                }
            ])
        ]
    };

    /// <summary>The new-workspace row's value for showing the chooser each time.</summary>
    private const string AskEveryTime = "ask";

    /// <summary>
    /// The new-workspace row's value for an arrangement: its pane count and orientation, which is
    /// what the settings file stores for it.
    /// </summary>
    private static string PresetValue(WorkspacePreset preset) =>
        Text(preset.PaneCount) + "-" + preset.Layout;

    /// <summary>
    /// Connections &amp; trust: how SSH host keys are found, and the standing caveat about them.
    /// </summary>
    /// <remarks>
    /// The providers' pages sit under this one in the navigation. Storage and Clients are captions
    /// over them rather than pages of their own, as 1.4 had it.
    /// </remarks>
    private static SettingsPageDefinition ConnectionsPage() => new()
    {
        Key = ConnectionsPageKey,
        Title = Ui.Settings.CategoryConnectionsAndTrust,
        Description = Ui.Settings.PageTrustDescription,
        Glyph = UiGlyph.Shield,
        Groups =
        [
            new(Ui.Settings.HostKeyDiscovery,
            [
                new()
                {
                    Key = "host-key-discovery",
                    Label = Ui.Settings.HostKeyDiscovery,
                    Kind = SettingsControlKind.Choice,
                    Choices =
                    [
                        new(nameof(SshHostKeyDiscoveryMode.Manual), Ui.Settings.HostKeyManual),
                        new(nameof(SshHostKeyDiscoveryMode.AskBeforeFetching), Ui.Settings.HostKeyAsk),
                        new(nameof(SshHostKeyDiscoveryMode.Automatic), Ui.Settings.HostKeyAutomatic)
                    ],
                    // The chosen mode explains itself in its own row, rather than in a paragraph
                    // underneath with no visible tie to the control it describes.
                    HintFor = v => Enum.TryParse<SshHostKeyDiscoveryMode>(v, out var mode)
                        ? mode switch
                        {
                            SshHostKeyDiscoveryMode.Manual => Ui.Settings.HostKeyManualHint,
                            SshHostKeyDiscoveryMode.Automatic => Ui.Settings.HostKeyAutomaticHint,
                            _ => Ui.Settings.HostKeyAskHint
                        }
                        : null,
                    Read = p => p.SshHostKeyDiscovery.ToString(),
                    Write = (p, v) => p with
                    {
                        SshHostKeyDiscovery = Enum.TryParse<SshHostKeyDiscoveryMode>(v, out var parsed) &&
                            Enum.IsDefined(parsed)
                                ? parsed
                                : p.SshHostKeyDiscovery
                    }
                }
            ]),
            new(null, []) { Note = Ui.Settings.HostKeyCaveat, NoteIsWarning = true }
        ]
    };

    /// <summary>
    /// One provider's defaults for a new connection: the fields worth reusing, then the timeouts
    /// and retries, and for the SSH terminal its session preferences.
    /// </summary>
    private static SettingsPageDefinition ProviderPage(ConnectionProviderDescriptor provider)
    {
        var kind = provider.Kind;
        var fields = ConnectionDefaultSettings.EditableFields(provider);
        List<SettingsGroupDefinition> groups =
        [
            new(Ui.Settings.BasicDefaults, [.. fields.Select(field => ProviderFieldRow(kind, field))])
            {
                // Local has nothing worth reusing: its root path belongs to each connection.
                Note = fields.Count == 0 ? Ui.Settings.NoReusableDefaults : null,
                NoteIsTitle = true
            },
            new(Ui.Settings.AdvancedBehavior, [.. ProviderBehaviourRows(kind)])
        ];

        if (kind == StorageProviderKind.Ssh)
        {
            groups.Add(new(Ui.Settings.TerminalAndShell, [.. TerminalRows()]));
        }

        return new()
        {
            Key = ProviderPageKey(kind),
            Title = provider.DisplayName,
            Heading = Ui.Format(Ui.Settings.ProviderDefaultsFormat, provider.DisplayName),
            Description = kind == StorageProviderKind.Ssh
                ? Ui.Settings.TerminalPreferencesHint
                : Ui.Format(Ui.Settings.ProviderDefaultsDescriptionFormat, provider.DisplayName),
            Glyph = UiGlyph.Server,
            ParentKey = ConnectionsPageKey,
            Group = provider.Type == ConnectionProfileType.Storage
                ? Ui.Settings.CategoryStorage
                : Ui.Settings.CategoryClients,
            Provider = kind,
            Groups = groups
        };
    }

    /// <summary>A reusable field's default, stored under "Provider.field" as 1.x stored it.</summary>
    private static SettingsRowDefinition ProviderFieldRow(StorageProviderKind provider, ConnectionFieldDescriptor field)
    {
        var key = ConnectionDefaultSettings.Key(provider, field.Key);
        var isKey = string.Equals(field.Key, ConnectionDefaultSettings.PrivateKeyReferenceKey, StringComparison.Ordinal);
        var kind = field.Kind switch
        {
            ConnectionFieldKind.Number => SettingsControlKind.Number,
            ConnectionFieldKind.Choice => SettingsControlKind.Choice,
            ConnectionFieldKind.SecretReference => SettingsControlKind.Secret,
            _ => SettingsControlKind.Text
        };

        string Read(DesktopUpdatePreferences p) =>
            ConnectionDefaultSettings.Get(provider, p.ConnectionDefaults).FieldValues[field.Key];

        return new()
        {
            Key = "default:" + key,
            Label = isKey
                ? Ui.Settings.DefaultPrivateKey
                : Ui.Format(Ui.Settings.DefaultFieldFormat, field.Label.ToLower(CultureInfo.CurrentCulture)),
            Hint = field.Key switch
            {
                "authenticationMode" => Ui.Settings.AuthenticationModeHint,
                ConnectionDefaultSettings.PrivateKeyReferenceKey => Ui.Settings.StoredInVault,
                "tlsMode" or "trustMode" when field.HelpText.Length > 0 => field.HelpText,
                _ => null
            },
            Kind = kind,
            Choices = [.. (field.Choices ?? []).Select(choice => new SettingsChoice(choice, choice))],
            Minimum = 1,
            Maximum = 65_535,
            ThousandsSeparator = true,
            MaxLength = ConnectionDefaultSettings.MaximumFieldValueLength,
            Placeholder = isKey ? Ui.Settings.NoDefaultPrivateKey : field.Placeholder,
            Read = Read,
            Write = (p, v) => WriteConnectionDefault(p, key, kind == SettingsControlKind.Text ? v.Trim() : v, Read)
        };
    }

    /// <summary>
    /// The connection timeout, the operation timeout and the retries, as 1.4 offered them.
    /// </summary>
    /// <remarks>
    /// A remote provider has one network timeout in CodeLogic.Storage, so its operation timeout
    /// follows the connection timeout and is dimmed; and only some providers retry. Both rows stay
    /// on screen for every provider, dimmed with the reason, so the pages read alike.
    /// </remarks>
    private static IEnumerable<SettingsRowDefinition> ProviderBehaviourRows(StorageProviderKind provider)
    {
        var local = provider == StorageProviderKind.Local;
        var retries = ConnectionDefaultSettings.SupportsConfigurableRetries(provider);

        yield return ConnectionDefaultNumber(
            provider, ConnectionDefaultSettings.ConnectTimeoutKey, Ui.Settings.ConnectionTimeout, null, 1, 600,
            Ui.Settings.UnitSeconds, static defaults => defaults.ConnectTimeoutSeconds, editable: true);
        yield return ConnectionDefaultNumber(
            provider, ConnectionDefaultSettings.OperationTimeoutKey, Ui.Settings.OperationTimeout,
            local ? null : Ui.Settings.OperationTimeoutHint, 1, 86_400,
            Ui.Settings.UnitSeconds, static defaults => defaults.OperationTimeoutSeconds, editable: local);
        yield return ConnectionDefaultNumber(
            provider, ConnectionDefaultSettings.RetryAttemptsKey, Ui.Settings.RetryAttempts,
            retries ? null : Ui.Settings.RetriesUnsupported, 0, 20,
            null, static defaults => defaults.MaximumRetryAttempts, editable: retries);
    }

    private static SettingsRowDefinition ConnectionDefaultNumber(
        StorageProviderKind provider,
        string setting,
        string label,
        string? hint,
        int minimum,
        int maximum,
        string? unit,
        Func<ConnectionProviderDefaults, int> value,
        bool editable)
    {
        var key = ConnectionDefaultSettings.Key(provider, setting);

        string Read(DesktopUpdatePreferences p) =>
            Text(value(ConnectionDefaultSettings.Get(provider, p.ConnectionDefaults)));

        return new()
        {
            Key = "default:" + key,
            Label = label,
            Hint = hint,
            Kind = SettingsControlKind.Number,
            Unit = unit,
            ThousandsSeparator = true,
            Minimum = minimum,
            Maximum = maximum,
            Enabled = editable ? null : static _ => false,
            Read = Read,
            // A dimmed row writes nothing: what it shows is decided by another row, or by the
            // provider, and storing a value for it would only be thrown away on the next load.
            Write = (p, v) => editable
                ? WriteConnectionDefault(p, key, Text(Number(v, minimum, maximum, int.Parse(Read(p), CultureInfo.InvariantCulture))), Read)
                : p
        };
    }

    /// <summary>
    /// Stores one provider default, or leaves the defaults alone when it is not a value they keep.
    /// </summary>
    /// <remarks>
    /// Normalised on the way in, so what is stored is every default at once and the next load finds
    /// nothing to repair. A value the defaults would drop is refused here rather than written,
    /// because dropping it would put the built-in default back rather than what was there before.
    /// </remarks>
    private static DesktopUpdatePreferences WriteConnectionDefault(
        DesktopUpdatePreferences preferences,
        string key,
        string value,
        Func<DesktopUpdatePreferences, string> read)
    {
        var values = ConnectionDefaultSettings.Normalize(preferences.ConnectionDefaults);
        values[key] = value;
        var next = preferences with { ConnectionDefaults = ConnectionDefaultSettings.Normalize(values) };
        return string.Equals(read(next), value, StringComparison.Ordinal) ? next : preferences;
    }

    /// <summary>The SSH terminal's session preferences, from 1.4's "Terminal &amp; shell" card.</summary>
    private static IEnumerable<SettingsRowDefinition> TerminalRows()
    {
        yield return new()
        {
            Key = "terminal-type",
            Label = Ui.Settings.TerminalType,
            Hint = Ui.Settings.TerminalTypeHint,
            Kind = SettingsControlKind.EditableChoice,
            MaxLength = SshTerminalIpcContract.MaximumTerminalNameLength,
            Suggestions = static () =>
                ["xterm-256color", "xterm", "screen-256color", "tmux-256color", "linux", "vt220", "vt100"],
            Read = p => Terminal(p).TerminalName,
            Write = (p, v) => Terminal(p, t => t with { TerminalName = v.Trim() })
        };
        yield return new()
        {
            Key = "terminal-startup",
            Label = Ui.Settings.StartupShell,
            Kind = SettingsControlKind.Text,
            MaxLength = SshTerminalPreferences.MaximumStartupCommandLength,
            Placeholder = Ui.Settings.StartupShellPlaceholder,
            Read = p => Terminal(p).StartupCommand ?? string.Empty,
            Write = (p, v) => Terminal(p, t => t with { StartupCommand = v.Trim() })
        };
        // No unit: the label already says "(seconds)", which is how 1.4 had it.
        yield return TerminalNumber(
            "terminal-keep-alive", Ui.Settings.KeepAliveInterval, null, 0, 3_600, null,
            static t => t.KeepAliveSeconds, static (t, value) => t with { KeepAliveSeconds = value });
        yield return new()
        {
            Key = "terminal-font",
            Label = Ui.Settings.FontFamily,
            Hint = Ui.Settings.FontFamilyHint,
            Kind = SettingsControlKind.EditableChoice,
            MaxLength = SshTerminalPreferences.MaximumFontFamilyLength,
            Suggestions = InstalledFonts,
            Read = p => Terminal(p).FontFamily,
            Write = (p, v) => Terminal(p, t => t with { FontFamily = v.Trim() })
        };
        yield return new()
        {
            Key = "terminal-font-size",
            Label = Ui.Settings.FontSize,
            Kind = SettingsControlKind.Number,
            Unit = Ui.Settings.UnitPoints,
            Minimum = 6,
            Maximum = 32,
            DecimalPlaces = 1,
            Increment = 0.5M,
            Read = p => Terminal(p).FontSize.ToString("0.#", CultureInfo.InvariantCulture),
            Write = (p, v) => decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var size)
                ? Terminal(p, t => t with { FontSize = (float)Math.Clamp(size, 6, 32) })
                : p
        };
        yield return TerminalNumber(
            "terminal-scrollback", Ui.Settings.ScrollbackLines, null, 100, 20_000, null,
            static t => t.ScrollbackLines, static (t, value) => t with { ScrollbackLines = value });
        yield return TerminalNumber(
            "terminal-refresh", Ui.Settings.OutputRefresh, Ui.Settings.OutputRefreshHint, 16, 500, null,
            static t => t.RefreshIntervalMilliseconds, static (t, value) => t with { RefreshIntervalMilliseconds = value });
        yield return new()
        {
            Key = "terminal-bold",
            Label = Ui.Settings.RenderBoldText,
            Hint = Ui.Settings.RenderBoldTextHint,
            Kind = SettingsControlKind.Toggle,
            Read = p => Text(Terminal(p).RenderBoldText),
            Write = (p, v) => Terminal(p, t => t with { RenderBoldText = Flag(v) })
        };
    }

    private static SettingsRowDefinition TerminalNumber(
        string key,
        string label,
        string? hint,
        int minimum,
        int maximum,
        string? unit,
        Func<SshTerminalPreferences, int> read,
        Func<SshTerminalPreferences, int, SshTerminalPreferences> write) => new()
    {
        Key = key,
        Label = label,
        Hint = hint,
        Kind = SettingsControlKind.Number,
        Unit = unit,
        ThousandsSeparator = true,
        Minimum = minimum,
        Maximum = maximum,
        Read = p => Text(read(Terminal(p))),
        Write = (p, v) => Terminal(p, t => write(t, Number(v, minimum, maximum, read(t))))
    };

    /// <summary>
    /// The font families this computer has, by name, as 1.4 listed them for the terminal.
    /// </summary>
    /// <remarks>
    /// Empty rather than failing where there is no font system to ask, which is a unit test: the
    /// field still takes a typed name, and a suggestion list is a convenience.
    /// </remarks>
    private static IReadOnlyList<string> InstalledFonts()
    {
        try
        {
            return [.. global::Avalonia.Media.FontManager.Current.SystemFonts
                .Select(family => family.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)];
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>The terminal preferences in force, which are the defaults until something is saved.</summary>
    private static SshTerminalPreferences Terminal(DesktopUpdatePreferences preferences) =>
        SshTerminalPreferences.Resolve(preferences.SshTerminal);

    /// <summary>
    /// Changes the terminal preferences, through the same resolution the settings file applies,
    /// so a value it would not keep is not kept here either.
    /// </summary>
    private static DesktopUpdatePreferences Terminal(
        DesktopUpdatePreferences preferences,
        Func<SshTerminalPreferences, SshTerminalPreferences> change) =>
        preferences with { SshTerminal = SshTerminalPreferences.Resolve(change(Terminal(preferences))) };

    private static SettingsPageDefinition UpdatesPage() => new()
    {
        Key = "updates",
        Title = Ui.Settings.CategoryUpdates,
        Description = Ui.Settings.PageUpdatesDescription,
        Glyph = UiGlyph.Download,
        Groups =
        [
            new(Ui.Settings.CategoryUpdates,
            [
                new()
                {
                    Key = "check-automatically",
                    Label = Ui.Settings.CheckAutomatically,
                    Hint = Ui.Settings.CheckAutomaticallyHint,
                    Kind = SettingsControlKind.Toggle,
                    Read = p => Text(p.CheckAutomatically),
                    Write = (p, v) => p with { CheckAutomatically = Flag(v) }
                },
                // Each step needs the one before it: nothing is downloaded that was not looked
                // for, and nothing restarts that was not downloaded.
                new()
                {
                    Key = "download-automatically",
                    Label = Ui.Settings.DownloadAutomatically,
                    Hint = Ui.Settings.DownloadAutomaticallyHint,
                    Kind = SettingsControlKind.Toggle,
                    Enabled = static p => p.CheckAutomatically,
                    Read = p => Text(p.DownloadAutomatically),
                    Write = (p, v) => p with { DownloadAutomatically = Flag(v) }
                },
                new()
                {
                    Key = "restart-automatically",
                    Label = Ui.Settings.RestartAutomatically,
                    Hint = Ui.Settings.RestartAutomaticallyHint,
                    Kind = SettingsControlKind.Toggle,
                    Enabled = static p => p.CheckAutomatically && p.DownloadAutomatically,
                    Read = p => Text(p.RestartAutomatically),
                    Write = (p, v) => p with { RestartAutomatically = Flag(v) }
                },
                new()
                {
                    Key = "include-prereleases",
                    Label = Ui.Settings.IncludePrereleases,
                    Hint = Ui.Settings.IncludePrereleasesHint,
                    Kind = SettingsControlKind.Toggle,
                    Read = p => Text(p.IncludePrereleases),
                    Write = (p, v) => p with { IncludePrereleases = Flag(v) }
                }
            ])
        ],
        Footnote = Ui.Format(
            Ui.Settings.UpdateSourceFormat,
            UpdateFeedOptions.DefaultFeedUrl,
            DesktopApplicationVersion.Current)
    };

    /// <summary>A concurrency ceiling, counted in jobs as 1.4 counted it.</summary>
    private static SettingsRowDefinition Concurrency(
        string key,
        string label,
        string hint,
        int maximum,
        Func<DesktopUpdatePreferences, int> read,
        Func<DesktopUpdatePreferences, int, DesktopUpdatePreferences> write,
        Func<DesktopUpdatePreferences, bool>? enabled = null,
        Func<DesktopUpdatePreferences, int>? floor = null) => new()
    {
        Key = key,
        Label = label,
        Hint = hint,
        Kind = SettingsControlKind.Number,
        Unit = Ui.Settings.UnitJobs,
        Minimum = 1,
        MinimumFor = floor,
        Maximum = maximum,
        Enabled = enabled,
        Read = p => Text(read(p)),
        Write = (p, v) => write(p, Number(v, 1, maximum, read(p)))
    };

    /// <summary>
    /// A total speed limit, in KiB/s on screen and bytes per second in the file; 0 is no limit.
    /// </summary>
    private static SettingsRowDefinition SpeedLimitRow(
        string key,
        string label,
        string? hint,
        Func<DesktopUpdatePreferences, long?> read,
        Func<DesktopUpdatePreferences, long?, DesktopUpdatePreferences> write)
    {
        const int maximumKib = (int)(DesktopUpdatePreferences.MaximumSpeedLimitBytesPerSecond / 1024);
        return new()
        {
            Key = key,
            Label = label,
            Hint = hint,
            Kind = SettingsControlKind.Number,
            Unit = Ui.Settings.UnitKibibytesPerSecond,
            // Grouped like the editing limit, the other count of kibibytes.
            ThousandsSeparator = true,
            Minimum = 0,
            Maximum = maximumKib,
            Read = p => Text((int)((read(p) ?? 0) / 1024)),
            Write = (p, v) =>
            {
                var kib = Number(v, 0, maximumKib, (int)((read(p) ?? 0) / 1024));
                return write(p, kib == 0 ? null : kib * 1024L);
            }
        };
    }

    internal static string Text(bool value) => value ? "true" : "false";

    internal static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A toggle's text, read leniently. Anything that is not "true" is off.
    /// </summary>
    /// <remarks>
    /// Lenient because the value can come from a settings file rather than from a checkbox, and a
    /// setting that refuses to load is worse than one that reads a typo as its default.
    /// </remarks>
    internal static bool Flag(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>A number's text, clamped to the row's range, falling back when it is not one.</summary>
    internal static int Number(string? value, int minimum, int maximum, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : fallback;
}
