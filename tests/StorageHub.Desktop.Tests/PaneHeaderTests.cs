using System.Reflection;

namespace StorageHub.Desktop.Tests;

public sealed class PaneHeaderTests
{
    [Fact]
    public void The_connection_header_is_one_row_without_a_manage_button()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var pane = new BrowserPaneControl("Pane 1", showLocalDefault: true);
            using var host = new Form { Size = new Size(1000, 700) };
            host.Controls.Add(pane);
            host.Show();
            System.Windows.Forms.Application.DoEvents();

            var header = Header(pane);
            Assert.Equal(1, header.RowCount);
            Assert.Equal(2, header.ColumnCount);

            // The old three-column header was 88px and named the connection twice. Anything
            // taller than a single line of text plus its frame means a row crept back in.
            Assert.InRange(header.Height, 30, 46);

            // Manage duplicated the toolbar, the Welcome page, the connections panel and
            // Settings, so it left the pane entirely rather than moving elsewhere in it.
            Assert.DoesNotContain(
                Descendants<StorageHubButton>(header),
                button => button.Text.StartsWith("Manage", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void Hiding_the_header_gives_the_pane_its_full_height_and_reports_the_change()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var pane = new BrowserPaneControl("Pane 1", showLocalDefault: true);
            using var host = new Form { Size = new Size(1000, 700) };
            host.Controls.Add(pane);
            host.Show();
            System.Windows.Forms.Application.DoEvents();

            var changes = 0;
            pane.StateChanged += (_, _) => changes++;
            Assert.True(pane.HeaderVisible);

            pane.HeaderVisible = false;

            Assert.False(Header(pane).Visible);
            Assert.Equal(1, changes);

            // Setting it to what it already is must not dirty the workspace.
            pane.HeaderVisible = false;
            Assert.Equal(1, changes);
        });
    }

    [Fact]
    public void The_hidden_header_rides_along_in_captured_pane_state()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var pane = new BrowserPaneControl("Pane 1", showLocalDefault: true);
            using var host = new Form { Size = new Size(1000, 700) };
            host.Controls.Add(pane);
            host.Show();
            System.Windows.Forms.Application.DoEvents();

            Assert.False(pane.CaptureState().HeaderHidden);

            pane.HeaderVisible = false;

            Assert.True(pane.CaptureState().HeaderHidden);
        });
    }

    [Fact]
    public void The_hidden_header_round_trips_through_a_saved_workspace_file()
    {
        var paneId = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), $"header-{Guid.NewGuid():N}.shw");
        try
        {
            var captured = WorkspaceFileStore.Capture(
                "Headers",
                paneId,
                new WorkspacePaneLeaf(paneId),
                new Dictionary<Guid, BrowserPaneState>
                {
                    [paneId] = new(PaneContentKind.ThisPc, HeaderHidden: true)
                });
            WorkspaceFileStore.Save(path, captured);

            var reloaded = WorkspaceFileStore.Load(path);

            Assert.True(reloaded.Panes[paneId].HeaderHidden);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void A_workspace_file_written_before_the_toggle_still_loads_with_the_header_shown()
    {
        var paneId = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), $"legacy-{Guid.NewGuid():N}.shw");
        try
        {
            // Exactly what 1.2.2 wrote: no headerHidden member at all. The flag is appended and
            // optional precisely so this keeps working without a schema bump.
            File.WriteAllText(path, $$"""
            {
              "schemaVersion": 1,
              "name": "Legacy",
              "activePaneId": "{{paneId:D}}",
              "layout": { "kind": "leaf", "paneId": "{{paneId:D}}" },
              "panes": {
                "{{paneId:D}}": {
                  "contentKind": "thisPc",
                  "sortColumn": "name",
                  "sortAscending": true
                }
              }
            }
            """);

            var reloaded = WorkspaceFileStore.Load(path);

            Assert.False(reloaded.Panes[paneId].HeaderHidden);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static TableLayoutPanel Header(BrowserPaneControl pane) =>
        Assert.IsType<TableLayoutPanel>(
            typeof(BrowserPaneControl)
                .GetField("_paneHeader", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(pane));

    private static IEnumerable<T> Descendants<T>(Control root)
        where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
