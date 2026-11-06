namespace StorageHub.Sync.Tests;

public sealed class SyncPathFilterPolicyTests
{
    [Theory]
    [InlineData("report.txt", true)]
    [InlineData("folder/report.txt", true)]
    [InlineData("folder/nested/report.txt", true)]
    [InlineData("report.bin", false)]
    public void Recursive_directory_glob_includes_root_and_nested_files(string path, bool expected)
    {
        var policy = new SyncPathFilterPolicy(includeGlobs: ["**/*.txt"]);
        Assert.Equal(expected, policy.Includes(path, caseSensitive: true));
    }

    [Theory]
    [InlineData("cache/file.txt")]
    [InlineData("folder/cache/file.txt")]
    public void Recursive_directory_exclusion_also_applies_at_the_root(string path)
    {
        var policy = new SyncPathFilterPolicy(excludeGlobs: ["**/cache/**"]);
        Assert.False(policy.Includes(path, caseSensitive: true));
    }

    [Fact]
    public void Single_star_still_cannot_cross_directory_boundaries()
    {
        var policy = new SyncPathFilterPolicy(includeGlobs: ["*.txt"]);
        Assert.True(policy.Includes("report.txt", caseSensitive: true));
        Assert.False(policy.Includes("folder/report.txt", caseSensitive: true));
    }
}
