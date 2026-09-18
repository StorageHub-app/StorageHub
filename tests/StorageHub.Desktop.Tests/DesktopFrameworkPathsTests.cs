using StorageHub.Desktop.Framework;

namespace StorageHub.Desktop.Tests;

public sealed class DesktopFrameworkPathsTests
{
    [Fact]
    public void DesktopTreeSitsBesideTheAgentTreeRatherThanInsideIt()
    {
        var paths = DesktopFrameworkPaths.Create(@"C:\data\StorageHub");

        Assert.Equal(@"C:\data\StorageHub", paths.DataRoot);
        Assert.Equal(@"C:\data\StorageHub\Desktop", paths.ApplicationRoot);
        Assert.Equal(@"C:\data\StorageHub\Desktop\CodeLogic", paths.FrameworkRoot);
        Assert.Equal(@"C:\data\StorageHub\Desktop\localization", paths.LocalizationDirectory);
    }

    /// <summary>
    /// The agent holds a single-instance lease on its own directory. Sharing a tree would mean two
    /// processes scaffolding the same framework root, so this separation is load-bearing rather
    /// than cosmetic.
    /// </summary>
    [Fact]
    public void FrameworkRootIsNotInsideTheAgentDirectory()
    {
        var paths = DesktopFrameworkPaths.Create(@"C:\data\StorageHub");

        var agentDirectory = Path.Combine(paths.DataRoot, "Agent");
        Assert.False(paths.FrameworkRoot.StartsWith(agentDirectory, StringComparison.OrdinalIgnoreCase));
        Assert.False(paths.ApplicationRoot.StartsWith(agentDirectory, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Settings already live in this directory, so config migration happens in place and the
    /// agent's cross-process read keeps looking in one folder.
    /// </summary>
    [Fact]
    public void ApplicationRootIsTheDirectoryThatAlreadyHoldsDesktopSettings()
    {
        var paths = DesktopFrameworkPaths.Create(@"C:\data\StorageHub");

        Assert.Equal(
            Path.Combine(paths.DataRoot, "Desktop", "settings.json"),
            Path.Combine(paths.ApplicationRoot, "settings.json"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"relative\path")]
    public void RelativeOrEmptyDataRootsAreRejected(string dataRoot) =>
        Assert.ThrowsAny<ArgumentException>(() => DesktopFrameworkPaths.Create(dataRoot));

    [Fact]
    public void EnsureCreatedIsIdempotentAndCreatesTheWholeTree()
    {
        var root = Path.Combine(Path.GetTempPath(), "storagehub-paths-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = DesktopFrameworkPaths.Create(root);

            paths.EnsureCreated();
            paths.EnsureCreated();

            Assert.True(Directory.Exists(paths.ApplicationRoot));
            Assert.True(Directory.Exists(paths.FrameworkRoot));
            Assert.True(Directory.Exists(paths.LocalizationDirectory));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
