using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// No column in any table can be squeezed to nothing, by a drag or by a narrow window.
/// </summary>
/// <remarks>
/// Dragging one heading wide used to let that column take the whole table and leave the others
/// as a stack of dividers at the right edge. <see cref="TableColumnRules"/> is what stops it; this
/// checks every table the shell shows, so a new table is covered without being listed here.
/// </remarks>
public class TableColumnRulesTests
{
    public static TheoryData<string> Screens => ["welcome", "sync", "workspace"];

    [AvaloniaTheory]
    [MemberData(nameof(Screens))]
    public void DraggingAColumnWideLeavesEveryOtherColumnItsMinimum(string screen)
    {
        var window = Open(screen, 1500);
        var tables = Tables(window);
        Assert.NotEmpty(tables);

        foreach (var table in tables)
        {
            table.Columns[0].Width = new GridLength(5000);
            Settle(window);
            AssertAllAtLeastMinimum(table);
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Screens))]
    public void ANarrowWindowStillLeavesEveryColumnItsMinimum(string screen)
    {
        var window = Open(screen, 1500);
        window.Width = 800;
        window.Measure(new Size(800, 920));
        window.Arrange(new Rect(0, 0, 800, 920));
        Settle(window);

        foreach (var table in Tables(window).Where(static t => t.Bounds.Width >= t.Columns.Count * TableColumnRules.MinimumWidth + 20))
        {
            AssertAllAtLeastMinimum(table);
        }
    }

    /// <summary>
    /// Too narrow for its columns, a table keeps them at the minimum and scrolls; given room again,
    /// the columns share it as they were written to.
    /// </summary>
    [AvaloniaFact]
    public void ATableTooNarrowPinsItsColumnsAndGivesThemBackWhenWidened()
    {
        var table = new TableView
        {
            Columns =
            {
                new TableViewColumn { Width = new GridLength(3, GridUnitType.Star) },
                new TableViewColumn { Width = new GridLength(1, GridUnitType.Star) },
                new TableViewColumn { Width = new GridLength(1, GridUnitType.Star) },
            }
        };
        var window = new Window { Content = table, Width = 150, Height = 200 };
        window.Show();
        window.Measure(new Size(150, 200));
        window.Arrange(new Rect(0, 0, 150, 200));
        Settle(window);
        AssertAllAtLeastMinimum(table);

        window.Width = 900;
        window.Measure(new Size(900, 200));
        window.Arrange(new Rect(0, 0, 900, 200));
        Settle(window);

        Assert.All(table.Columns, static column => Assert.True(column.Width.IsStar));
        Assert.Equal(3, table.Columns[0].Width.Value);
    }

    private static void AssertAllAtLeastMinimum(TableView table)
    {
        foreach (var column in table.Columns)
        {
            Assert.True(
                column.ActualWidth >= TableColumnRules.MinimumWidth - 1,
                $"A column in a {table.Columns.Count}-column table {table.Bounds.Width:0} wide is " +
                $"{column.ActualWidth:0} wide. Columns: " +
                string.Join(", ", table.Columns.Select(static c => $"{c.Width} -> {c.ActualWidth:0}")));
        }
    }

    private static MainWindow Open(string screen, double width)
    {
        var model = screen switch
        {
            "sync" => ShellPreview.SampleOnSyncTasks,
            "workspace" => ShellPreview.CreateOnWorkspace(),
            _ => ShellPreview.Sample,
        };
        var window = new MainWindow { DataContext = model, Width = width, Height = 920 };
        window.Show();
        window.Measure(new Size(width, 920));
        window.Arrange(new Rect(0, 0, width, 920));
        Settle(window);
        return window;
    }

    private static TableView[] Tables(Window window) =>
        [.. window.GetVisualDescendants().OfType<TableView>().Where(static t => t.IsEffectivelyVisible && t.Columns.Count > 1)];

    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }
}
