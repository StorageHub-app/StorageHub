namespace StorageHub.Desktop.Tests;

public sealed class WorkspaceResizeTests
{
    [Fact]
    public void Resizing_a_split_workspace_does_not_mark_it_dirty()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var main = new MainForm();
            var page = main.AddWorkspace(2);
            var workspace = Assert.Single(page.Controls.OfType<WorkspaceControl>());
            workspace.Size = new Size(1000, 600);
            main.CreateControl();
            main.PerformLayout();
            workspace.PerformLayout();

            // A freshly added workspace is dirty by design; associating a file is how an opened
            // one becomes clean without touching the disk.
            workspace.AssociateFile(Path.Combine(Path.GetTempPath(), "resize-test.shw"));
            Assert.False(workspace.IsDirty);

            // Restoring the split ratio on a resize moves the splitter programmatically, and
            // that used to be indistinguishable from a drag: every window resize re-derived the
            // ratio and marked the workspace dirty, so an untouched workspace asked to be saved.
            workspace.Size = new Size(1200, 700);
            workspace.PerformLayout();
            workspace.Size = new Size(900, 500);
            workspace.PerformLayout();

            Assert.False(workspace.IsDirty);
        });
    }

    [Fact]
    public void Dragging_the_splitter_still_records_the_ratio_and_marks_it_dirty()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var main = new MainForm();
            // Sized explicitly: a workspace fills whatever the shell's menu, toolbar and queue
            // leave it, so dragging a splitter by a fixed number of pixels only means anything
            // when the panes have room for that drag. Without this the test's outcome depended on
            // how tall the chrome happened to be.
            main.Size = new Size(1280, 860);
            var page = main.AddWorkspace(2);
            var workspace = Assert.Single(page.Controls.OfType<WorkspaceControl>());
            workspace.Size = new Size(1000, 600);
            main.CreateControl();
            main.PerformLayout();
            workspace.PerformLayout();
            workspace.AssociateFile(Path.Combine(Path.GetTempPath(), "drag-test.shw"));
            Assert.False(workspace.IsDirty);

            // The workspace's own splitter, not a pane's. A browser pane carries a SplitContainer
            // of its own for its directory tree, and a depth-first walk reaches whichever came
            // first -- so this used to drag the tree divider of pane one about as often as not,
            // and a drag there records no workspace ratio at all.
            var split = WorkspaceSplitter(workspace);
            var root = Assert.IsType<WorkspaceSplitNode>(workspace.LayoutModel.Root);
            var before = root.Ratio;

            // A drag is a SplitterMoving followed by the new distance. Only that pairing counts
            // as the user's, which is what lets a resize move the same splitter without dirtying
            // the workspace, so this is the half of that rule worth pinning down.
            //
            // The distance is worked out from the room the splitter actually has rather than
            // nudged by a fixed number of pixels: a SplitContainer clamps a distance that would
            // take a panel below its minimum, and a clamped drag moves nothing, which used to
            // fail here whenever the shell's chrome left the panes a little shorter.
            var span = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
            var lowest = split.Panel1MinSize;
            var highest = span - split.SplitterWidth - split.Panel2MinSize;
            Assert.True(highest > lowest, $"The splitter has no room to drag in: {lowest}..{highest}.");
            var target = lowest + ((highest - lowest) / 4);

            // Layout is held off across the assignment, the way it is during a real drag. The
            // workspace restores the stored ratio from the splitter's Layout event, so a layout
            // that runs between the new distance being set and SplitterMoved reaching the
            // workspace pulls the splitter straight back to the old ratio and the drag reads as
            // if it never happened. Whether that layout ran turned out to depend on what else
            // had run in the process, which is what made this test look flaky.
            split.OnSplitterMoving(new SplitterCancelEventArgs(0, 0, 0, 0));
            split.SuspendLayout();
            split.SplitterDistance = target;
            split.ResumeLayout(performLayout: true);
            var landed = split.SplitterDistance;

            // Where the splitter ends up is not asserted: a layout pass can follow the assignment
            // and settle it somewhere else again, which it does or does not depending on what has
            // already run in the process. What a drag has to leave behind is the recorded ratio,
            // so that is what is checked -- the distances only go in the message, to tell a
            // clamped no-op drag apart from the rule itself having stopped working.
            Assert.True(
                workspace.IsDirty,
                $"A drag to {target} (landed at {landed}) left the workspace clean.");
            var after = Assert.IsType<WorkspaceSplitNode>(workspace.LayoutModel.Root);
            Assert.True(
                before != after.Ratio,
                $"A drag to {target} (landed at {landed}) recorded no new ratio; it is still {before}.");
        });
    }

    /// <summary>
    /// The split the workspace itself owns: the first one reached from the workspace without
    /// passing through a pane, which is the only one whose distance the layout model records.
    /// </summary>
    private static SplitContainer WorkspaceSplitter(Control workspace)
    {
        foreach (Control child in workspace.Controls)
        {
            if (child is BrowserPaneControl)
            {
                continue;
            }

            if (child is SplitContainer split)
            {
                return split;
            }

            if (TryFind(child) is { } nested)
            {
                return nested;
            }
        }

        throw new InvalidOperationException("The workspace has no splitter of its own.");

        static SplitContainer? TryFind(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is BrowserPaneControl)
                {
                    continue;
                }

                if (child is SplitContainer split)
                {
                    return split;
                }

                if (TryFind(child) is { } nested)
                {
                    return nested;
                }
            }

            return null;
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
