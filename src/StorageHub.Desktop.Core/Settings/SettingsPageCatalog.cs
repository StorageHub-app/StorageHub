using System.Globalization;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Configuration;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Themes;

namespace StorageHub.Desktop.Settings;

/// <summary>What a settings row is edited with.</summary>
internal enum SettingsControlKind
{
    Toggle,
    Choice,
    Number,

    /// <summary>A file on this computer, typed or chosen with a Browse button.</summary>
    Path
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

    public required SettingsControlKind Kind { get; init; }

    public IReadOnlyList<SettingsChoice> Choices { get; init; } = [];

    public int Minimum { get; init; }

    public int Maximum { get; init; }

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

/// <summary>A page in the settings navigation, and the rows on it.</summary>
internal sealed record SettingsPageDefinition(
    string Key,
    string Title,
    string Description,
    UiGlyph Glyph,
    IReadOnlyList<SettingsRowDefinition> Rows);

/// <summary>
/// The settings pages, as data.
/// </summary>
/// <remarks>
/// <para>
/// Not the whole of the old Settings dialog yet: the pages that need a screen of their own --
/// shortcuts, connection defaults, external editing -- arrive with those screens. The toolbar is
/// the first of those to land, and is declared here with no rows so the navigation stays one list.
/// The rest of what is here is every setting that is a toggle, a choice or a number, which is most
/// of them and all of the ones a person changes.
/// </para>
/// <para>
/// The labels are the WinForms shell's, already translated into Danish and German. Reusing them
/// rather than writing new ones is not laziness: it is the difference between a port and a rewrite
/// that quietly says something else in two languages nobody on the team reads.
/// </para>
/// </remarks>
internal static class SettingsPageCatalog
{
    internal static IReadOnlyList<SettingsPageDefinition> Pages { get; } =
    [
        new(
            "appearance",
            Ui.Settings.CategoryAppearance,
            Ui.Settings.PageAppearanceDescription,
            UiGlyph.Theme,
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
                        new(nameof(DesktopAppearance.System), Ui.Settings.ThemeSystem),
                        new(nameof(DesktopAppearance.Light), Ui.Settings.ThemeLight),
                        new(nameof(DesktopAppearance.Dark), Ui.Settings.ThemeDark)
                    ],
                    Read = p => p.Appearance.ToString(),
                    Write = (p, v) => p with
                    {
                        Appearance = Enum.TryParse<DesktopAppearance>(v, out var parsed)
                            ? parsed
                            : p.Appearance
                    }
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

        new(
            "workspace",
            Ui.Settings.CategoryWorkspace,
            Ui.Settings.PageWorkspaceDescription,
            UiGlyph.Tree,
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
                new()
                {
                    Key = "reconnect-remote-panes",
                    Label = Ui.Settings.ReconnectRemotePanes,
                    Hint = Ui.Settings.ReconnectRemotePanesHint,
                    Kind = SettingsControlKind.Toggle,
                    Read = p => Text(p.ReconnectRemotePanesAutomatically),
                    Write = (p, v) => p with { ReconnectRemotePanesAutomatically = Flag(v) }
                },
                new()
                {
                    Key = "show-favourites-in-folders",
                    Label = Ui.Settings.ShowFavoritesInTheirFolders,
                    Kind = SettingsControlKind.Toggle,
                    Read = p => Text(p.ShowFavoritesInTheirFolders),
                    Write = (p, v) => p with { ShowFavoritesInTheirFolders = Flag(v) }
                }
            ]),

        new(
            ToolbarPageKey,
            Ui.Settings.CategoryToolbar,
            Ui.Settings.PageToolbarDescription,
            UiGlyph.Layers,
            // No rows: arranging a toolbar is two lists and five buttons, which ToolbarPageModel
            // draws. The page is still declared here so the navigation list stays one list.
            []),

        // What happens when a remote file is opened. The unsafe-edit warning lives here rather than
        // under Confirmations because the warning itself says so: "you can restore this warning
        // later in Settings under Editing".
        new(
            "editing",
            Ui.Settings.CategoryEditing,
            Ui.Settings.PageEditingDescription,
            UiGlyph.Rename,
            [
                new()
                {
                    Key = "external-editor",
                    Label = Ui.Settings.EditorExecutable,
                    Hint = Ui.Settings.EditorHint,
                    Kind = SettingsControlKind.Path,
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
                    Kind = SettingsControlKind.Number,
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
            ]),

        new(
            "performance",
            Ui.Settings.CategoryPerformance,
            Ui.Settings.PagePerformanceDescription,
            UiGlyph.Speed,
            [
                new()
                {
                    Key = "adaptive-concurrency",
                    Label = Ui.Settings.AdaptiveConcurrency,
                    Kind = SettingsControlKind.Toggle,
                    Read = p => Text(p.AdaptiveConcurrency),
                    Write = (p, v) => p with { AdaptiveConcurrency = Flag(v) }
                },
                new()
                {
                    Key = "maximum-transfers",
                    Label = Ui.Settings.MaximumTransfers,
                    Hint = Ui.Settings.MaximumTransfersHint,
                    Kind = SettingsControlKind.Number,
                    Minimum = 1,
                    Maximum = 16,
                    Read = p => Text(p.MaximumTransferConcurrency),
                    Write = (p, v) => p with { MaximumTransferConcurrency = Number(v, 1, 16, p.MaximumTransferConcurrency) }
                },
                new()
                {
                    Key = "per-connection",
                    Label = Ui.Settings.PerConnection,
                    Hint = Ui.Settings.PerConnectionHint,
                    Kind = SettingsControlKind.Number,
                    Minimum = 1,
                    Maximum = 8,
                    Read = p => Text(p.PerConnectionConcurrency),
                    Write = (p, v) => p with { PerConnectionConcurrency = Number(v, 1, 8, p.PerConnectionConcurrency) }
                },
                new()
                {
                    Key = "maximum-synchronizations",
                    Label = Ui.Settings.MaximumSynchronizations,
                    Hint = Ui.Settings.MaximumSynchronizationsHint,
                    Kind = SettingsControlKind.Number,
                    Minimum = 1,
                    Maximum = 8,
                    Read = p => Text(p.MaximumSyncConcurrency),
                    Write = (p, v) => p with { MaximumSyncConcurrency = Number(v, 1, 8, p.MaximumSyncConcurrency) }
                }
            ]),

        new(
            "confirmations",
            Ui.Settings.SectionConfirmations,
            Ui.Settings.PageConfirmationsDescription,
            UiGlyph.Warning,
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
            ]),

        new(
            "updates",
            Ui.Settings.CategoryUpdates,
            Ui.Settings.PageUpdatesDescription,
            UiGlyph.Refresh,
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
                new()
                {
                    Key = "download-automatically",
                    Label = Ui.Settings.DownloadAutomatically,
                    Hint = Ui.Settings.DownloadAutomaticallyHint,
                    Kind = SettingsControlKind.Toggle,
                    Read = p => Text(p.DownloadAutomatically),
                    Write = (p, v) => p with { DownloadAutomatically = Flag(v) }
                },
                new()
                {
                    Key = "restart-automatically",
                    Label = Ui.Settings.RestartAutomatically,
                    Hint = Ui.Settings.RestartAutomaticallyHint,
                    Kind = SettingsControlKind.Toggle,
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
    ];

    /// <summary>
    /// The page whose editor is a screen rather than a list of rows.
    /// </summary>
    /// <remarks>
    /// Named rather than spelled out at each use, so the one place the window branches on it says
    /// which page it means. The toolbar is the first of these; shortcuts and connection defaults
    /// will join it.
    /// </remarks>
    internal const string ToolbarPageKey = "toolbar";

    /// <summary>Every row on every page, for a caller that wants them without the grouping.</summary>
    internal static IEnumerable<SettingsRowDefinition> AllRows => Pages.SelectMany(page => page.Rows);

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
