using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// A real local delete goes to the Recycle Bin or the Trash, and the file is gone from where it was.
/// </summary>
/// <remarks>
/// Only when asked, with STORAGEHUB_TOUCH_RECYCLE_BIN, because it leaves a small file in the
/// Recycle Bin or Trash of whoever runs it.
/// </remarks>
public class RecycleBinTests
{
    [Fact]
    public void ARecycledFileLeavesItsFolder()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("STORAGEHUB_TOUCH_RECYCLE_BIN") == "1",
            "Set STORAGEHUB_TOUCH_RECYCLE_BIN=1 to send a temporary file to the Recycle Bin or Trash.");

        var mutations = new LocalMutations();
        Assert.True(mutations.CanRecycle);

        var folder = Directory.CreateTempSubdirectory("storagehub-recycle-");
        var file = Path.Combine(folder.FullName, "storagehub-recycle-test.txt");
        File.WriteAllText(file, "StorageHub test file; safe to delete.");

        mutations.Recycle(file, container: false);

        Assert.False(File.Exists(file));
        folder.Delete(recursive: true);
    }
}
