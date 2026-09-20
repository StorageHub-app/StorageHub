using Lucide.Avalonia;

namespace StorageHub.Desktop.Themes;

/// <summary>
/// The one place StorageHub's semantic glyphs are bound to an icon set.
/// </summary>
/// <remarks>
/// The WinForms shell drew its own 57 glyphs: each one authored on a 24x24 grid in
/// <c>UiIconFactory.DrawGlyph</c>, stroked with round caps and a weight that tapered with size, then
/// rasterized per DPI and cached in a weak registry so an appearance change could re-tint it. That
/// is a lot of machinery to own, and it capped the shell at exactly the glyphs somebody had found
/// time to draw.
///
/// Lucide is the same authoring model - 24x24, stroked, round caps, one weight - with 1853 icons
/// instead of 57, under MIT. It draws as geometry, so there is no rasterization, no DPI cache and
/// no re-tinting: the stroke follows <c>Foreground</c>, which the themes set from a design token.
///
/// The indirection through <see cref="UiGlyph"/> is the point. Commands still declare intent
/// ("this one means Refresh"), and swapping icon sets, or correcting a single choice, is an edit to
/// this table rather than to the command catalog.
/// </remarks>
internal static class IconCatalog
{
    private static readonly Dictionary<UiGlyph, LucideIconKind> Kinds = new()
    {
        [UiGlyph.Add] = LucideIconKind.Plus,
        [UiGlyph.Connections] = LucideIconKind.Network,
        [UiGlyph.Back] = LucideIconKind.ArrowLeft,
        [UiGlyph.Forward] = LucideIconKind.ArrowRight,
        [UiGlyph.Up] = LucideIconKind.ArrowUp,
        [UiGlyph.Refresh] = LucideIconKind.RefreshCw,
        [UiGlyph.Compare] = LucideIconKind.GitCompare,
        [UiGlyph.Run] = LucideIconKind.Play,
        [UiGlyph.Pause] = LucideIconKind.Pause,
        [UiGlyph.Search] = LucideIconKind.Search,
        [UiGlyph.Folder] = LucideIconKind.Folder,
        [UiGlyph.File] = LucideIconKind.File,
        [UiGlyph.Save] = LucideIconKind.Save,
        [UiGlyph.Delete] = LucideIconKind.Trash,
        [UiGlyph.Test] = LucideIconKind.FlaskConical,
        [UiGlyph.Terminal] = LucideIconKind.Terminal,
        [UiGlyph.Lock] = LucideIconKind.Lock,
        [UiGlyph.Warning] = LucideIconKind.TriangleAlert,
        [UiGlyph.More] = LucideIconKind.Ellipsis,
        [UiGlyph.Home] = LucideIconKind.House,
        [UiGlyph.Settings] = LucideIconKind.Settings,
        [UiGlyph.Info] = LucideIconKind.Info,
        [UiGlyph.Cut] = LucideIconKind.Scissors,
        [UiGlyph.Copy] = LucideIconKind.Copy,
        [UiGlyph.Paste] = LucideIconKind.ClipboardPaste,
        [UiGlyph.Rename] = LucideIconKind.SquarePen,
        [UiGlyph.SelectAll] = LucideIconKind.SquareDashed,
        [UiGlyph.Invert] = LucideIconKind.Replace,
        [UiGlyph.Properties] = LucideIconKind.ClipboardList,
        [UiGlyph.Exit] = LucideIconKind.LogOut,
        [UiGlyph.Tree] = LucideIconKind.ListTree,
        [UiGlyph.Queue] = LucideIconKind.ListOrdered,
        [UiGlyph.Log] = LucideIconKind.ScrollText,
        [UiGlyph.Hidden] = LucideIconKind.EyeOff,
        [UiGlyph.Theme] = LucideIconKind.Palette,
        [UiGlyph.Connect] = LucideIconKind.Plug,
        [UiGlyph.Disconnect] = LucideIconKind.Unplug,
        [UiGlyph.Stop] = LucideIconKind.CircleStop,
        [UiGlyph.Speed] = LucideIconKind.Gauge,
        [UiGlyph.Profiles] = LucideIconKind.Workflow,
        [UiGlyph.Schedule] = LucideIconKind.CalendarClock,
        // Lucide's "history" glyph, a clock with a counterclockwise arrow.
        [UiGlyph.History] = LucideIconKind.RotateCcwClock,
        [UiGlyph.Favorite] = LucideIconKind.Star,
        [UiGlyph.Key] = LucideIconKind.KeyRound,
        [UiGlyph.Shield] = LucideIconKind.ShieldCheck,
        [UiGlyph.Cloud] = LucideIconKind.Cloud,
        [UiGlyph.Server] = LucideIconKind.Server,
        [UiGlyph.Download] = LucideIconKind.Download,
        [UiGlyph.Upload] = LucideIconKind.Upload,
        [UiGlyph.Keyboard] = LucideIconKind.Keyboard,
        [UiGlyph.Documentation] = LucideIconKind.BookOpen,
        [UiGlyph.Bug] = LucideIconKind.Bug,
        [UiGlyph.Checksum] = LucideIconKind.Hash,
        [UiGlyph.Diagnostics] = LucideIconKind.Activity,
        [UiGlyph.Close] = LucideIconKind.X,
        [UiGlyph.Link] = LucideIconKind.Link,
        [UiGlyph.Layers] = LucideIconKind.Layers,
    };

    /// <summary>The icon for a glyph, or null when a command declares none.</summary>
    /// <remarks>
    /// An unmapped glyph throws rather than falling back to a placeholder: a missing icon is a gap
    /// in this table, and a silent question-mark in a toolbar is how it would stay one.
    /// </remarks>
    internal static LucideIconKind? Resolve(UiGlyph? glyph)
    {
        if (glyph is null) return null;

        return Kinds.TryGetValue(glyph.Value, out var kind)
            ? kind
            : throw new KeyNotFoundException($"No icon is mapped for the glyph '{glyph}'.");
    }
}
