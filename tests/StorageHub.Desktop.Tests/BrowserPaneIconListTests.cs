using System.Reflection;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The folder tree does not share the file list's growing ImageList.
/// </summary>
/// <remarks>
/// Association icons are resolved lazily into the file list's ImageList, and adding to a live
/// ImageList recreates its native handle. Every control bound to that list loses its images until
/// it is invalidated again — so while the tree shared it, moving the pointer across the file list
/// made the folder icons in the tree disappear and come back.
///
/// The tree only ever shows "connection" and "folder". Keeping it on its own list is what stops a
/// file-list icon lookup from reaching it at all.
/// </remarks>
public sealed class BrowserPaneIconListTests
{
    [Fact]
    public void GrowingTheFileListIconsLeavesTheTreeUntouched()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var pane = new BrowserPaneControl("Pane 1", showLocalDefault: true);

            var fileImages = Field<ImageList>(pane, "_browserImages");

            // Read from the control, not from the field that is meant to feed it. Comparing the
            // two fields passes even when the tree is still bound to the file list's images, which
            // is exactly the state being guarded against.
            var tree = Field<TreeView>(pane, "_directoryTree");
            var treeImages = tree.ImageList;
            Assert.NotNull(treeImages);

            Assert.False(
                ReferenceEquals(fileImages, treeImages),
                "The tree and the file list share one ImageList, so resolving a file icon blanks " +
                "the tree's folder icons until it repaints.");

            var before = treeImages!.Images.Count;

            // What a shell-icon lookup does: add to the file list's list while both controls live.
            using var added = new Bitmap(16, 16);
            fileImages.Images.Add("shell:.probe:128", added);

            Assert.Equal(before, treeImages.Images.Count);
            Assert.True(treeImages.Images.ContainsKey("folder"), "The tree lost its folder icon.");
            Assert.True(
                treeImages.Images.ContainsKey("connection"),
                "The tree lost its connection icon.");
        });
    }

    private static T Field<T>(object instance, string name)
        where T : class
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsAssignableFrom<T>(field.GetValue(instance));
    }
}
