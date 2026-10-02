using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace StorageHub.Desktop.Themes;

/// <summary>
/// The rules every table's columns keep, whatever is dragged or however narrow the window gets.
/// </summary>
/// <remarks>
/// <para>
/// Avalonia's TableViewColumn has no minimum width. Dragging one heading wide let that column take
/// the whole table, and the columns after it were squeezed to nothing -- only their dividers were
/// left, stacked at the right edge. The same happened when a window was made narrower than its
/// fixed-width columns. These rules close both:
/// </para>
/// <list type="number">
/// <item>No column is narrower than <see cref="MinimumWidth"/>.</item>
/// <item>A column can only grow as far as leaves every other column its minimum.</item>
/// <item>When the table is too narrow for its fixed-width columns, those give way -- the widest
/// first -- before any column goes below its minimum.</item>
/// <item>When even that is not enough, every column stays at its minimum and the table scrolls
/// sideways; the flexible ones get their own widths back when there is room again.</item>
/// </list>
/// <para>
/// Installed once for every TableView in the application (<see cref="Install"/>), so a new table
/// keeps them without anybody remembering to. <c>TableColumnRulesTests</c> drags every column of
/// every table in the shell and checks them.
/// </para>
/// </remarks>
internal static class TableColumnRules
{
    /// <summary>The narrowest a column may be: room for a short heading and its sort arrow.</summary>
    internal const double MinimumWidth = 60;

    /// <summary>Room kept for the vertical scroll bar, which the columns share the width with.</summary>
    private const double ScrollBarAllowance = 14;

    /// <summary>
    /// The narrowest a flexible column may be in one table, where that is more than
    /// <see cref="MinimumWidth"/>.
    /// </summary>
    /// <remarks>
    /// A pane's Name column is flexible and takes what the fixed ones leave, so in a two-pane
    /// workspace it was the column at the minimum, an icon and four letters, while Size, Type and
    /// Modified kept their full widths. 1.4 kept the name readable and let the others be narrow. A
    /// table that sets this has its fixed columns give way first, down to their own minimum.
    /// </remarks>
    internal static readonly AttachedProperty<double> FlexibleMinimumProperty =
        AvaloniaProperty.RegisterAttached<TableView, double>("FlexibleMinimum", typeof(TableColumnRules), MinimumWidth);

    internal static void SetFlexibleMinimum(TableView table, double value) =>
        table.SetValue(FlexibleMinimumProperty, Math.Max(MinimumWidth, value));

    private static readonly AttachedProperty<bool> AppliedProperty =
        AvaloniaProperty.RegisterAttached<TableView, bool>("TableColumnRulesApplied", typeof(TableColumnRules));

    private static bool _installed;

    /// <summary>Applies the rules to every TableView as it is attached.</summary>
    internal static void Install()
    {
        if (_installed) return;
        _installed = true;
        Control.LoadedEvent.AddClassHandler<TableView>((table, _) => Apply(table), RoutingStrategies.Direct);
    }

    /// <summary>Applies the rules to one table. Safe to call more than once.</summary>
    internal static void Apply(TableView table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (table.GetValue(AppliedProperty)) return;
        table.SetValue(AppliedProperty, true);

        var enforcing = false;
        void Enforce(TableViewColumn? changed)
        {
            if (enforcing) return;
            enforcing = true;
            try
            {
                Clamp(table, changed);
            }
            finally
            {
                enforcing = false;
            }
        }

        foreach (var column in table.Columns)
        {
            column.PropertyChanged += (_, e) =>
            {
                if (e.Property == TableViewColumn.WidthProperty) Enforce(column);
            };
        }

        table.SizeChanged += (_, _) => Enforce(null);
        Enforce(null);
    }

    /// <summary>
    /// Brings the columns within the rules. <paramref name="changed"/> is the column somebody just
    /// resized; it is the one cut back when the others need the room.
    /// </summary>
    internal static void Clamp(TableView table, TableViewColumn? changed)
    {
        var columns = table.Columns;
        if (columns.Count == 0) return;

        var available = table.Bounds.Width - ScrollBarAllowance;
        if (available <= 0) return;

        var flexibleMinimum = table.GetValue(FlexibleMinimumProperty);
        Unpin(table, available, flexibleMinimum);

        // Every fixed column at least the minimum.
        foreach (var column in columns)
        {
            if (column.Width.IsAbsolute && column.Width.Value < MinimumWidth)
            {
                column.Width = new GridLength(MinimumWidth);
            }
        }

        // What the fixed columns take, and what the rest need at their minimum.
        double Fixed() => columns.Where(static c => c.Width.IsAbsolute).Sum(static c => c.Width.Value);
        var overflow = Fixed() + FlexibleMinimum(columns.Where(static c => !c.Width.IsAbsolute), flexibleMinimum) - available;
        if (overflow <= 0) return;

        // The column just resized gives back first; then the widest fixed columns, each only as far
        // as its own minimum.
        var order = columns
            .Where(static c => c.Width.IsAbsolute)
            .OrderByDescending(c => ReferenceEquals(c, changed))
            .ThenByDescending(static c => c.Width.Value)
            .ToList();
        foreach (var column in order)
        {
            if (overflow <= 0) break;
            var give = Math.Min(overflow, column.Width.Value - MinimumWidth);
            if (give <= 0) continue;
            column.Width = new GridLength(column.Width.Value - give);
            overflow -= give;
        }

        // Still no room: the table is narrower than its columns at their minimum. The flexible
        // columns are pinned at the minimum and the table scrolls sideways rather than squeezing
        // them. What they were is kept, and given back once there is room again.
        if (overflow > 0)
        {
            foreach (var column in columns.Where(static c => !c.Width.IsAbsolute))
            {
                Pinned.AddOrUpdate(column, new PinnedWidth(column.Width));
                column.Width = new GridLength(flexibleMinimum);
            }
        }
    }

    /// <summary>
    /// The room flexible columns need for every one of them to reach the minimum.
    /// </summary>
    /// <remarks>
    /// Not the count times the minimum: they share their room by weight, so a 75* column beside
    /// 85*, 110* and 115* gets less than a quarter. The lightest one decides it.
    /// </remarks>
    private static double FlexibleMinimum(IEnumerable<TableViewColumn> columns, double minimum) =>
        FlexibleMinimum(columns.Select(static c => c.Width), minimum);

    private static double FlexibleMinimum(IEnumerable<GridLength> widths, double minimum)
    {
        var weights = widths.Where(static w => w.IsStar).Select(static w => Math.Max(w.Value, 0.001)).ToList();
        return weights.Count == 0 ? 0 : minimum * weights.Sum() / weights.Min();
    }

    /// <summary>A flexible column's own width, while it is pinned at the minimum.</summary>
    private sealed record PinnedWidth(GridLength Width);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TableViewColumn, PinnedWidth> Pinned = new();

    /// <summary>Gives pinned columns their own widths back, if the table now has room for them.</summary>
    private static void Unpin(TableView table, double available, double flexibleMinimum)
    {
        var pinned = table.Columns.Where(static c => Pinned.TryGetValue(c, out _)).ToList();
        if (pinned.Count == 0) return;

        var fixedWidth = table.Columns
            .Where(c => c.Width.IsAbsolute && !pinned.Contains(c))
            .Sum(static c => c.Width.Value);
        var restored = pinned.Select(static c => Pinned.TryGetValue(c, out var original) ? original.Width : c.Width);
        var flexible = table.Columns.Where(c => !c.Width.IsAbsolute && !pinned.Contains(c)).Select(static c => c.Width);
        if (fixedWidth + FlexibleMinimum(restored.Concat(flexible), flexibleMinimum) > available) return;

        foreach (var column in pinned)
        {
            if (Pinned.TryGetValue(column, out var original)) column.Width = original.Width;
            Pinned.Remove(column);
        }
    }
}
