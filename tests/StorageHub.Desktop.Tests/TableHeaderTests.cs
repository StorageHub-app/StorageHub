using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Every table's column dividers stay inside its heading band.
/// </summary>
/// <remarks>
/// The dividers are the resize grips, stretched by a negative margin that has to match the band's
/// padding. When the band was made denser the margin was not, and the dividers stuck out above the
/// headings and down into the first row of every table in the app.
/// </remarks>
public class TableHeaderTests
{
    [AvaloniaFact]
    public void TheColumnDividersStayInsideTheHeadingBand()
    {
        var window = new MainWindow { DataContext = ShellPreview.CreateOnWorkspace() };
        window.Show();
        window.Measure(new Size(1500, 920));
        window.Arrange(new Rect(0, 0, 1500, 920));
        window.UpdateLayout();

        var headers = window.GetVisualDescendants().OfType<TableViewColumnHeader>().ToArray();
        Assert.NotEmpty(headers);
        foreach (var header in headers)
        {
            var band = header.FindAncestorOfType<Border>()!;
            foreach (var grip in header.GetVisualDescendants().OfType<Thumb>())
            {
                var top = grip.TranslatePoint(default, band)!.Value.Y;
                Assert.InRange(top, 0, band.Bounds.Height);
                Assert.InRange(top + grip.Bounds.Height, 0, band.Bounds.Height);
            }
        }
    }
}
