using StorageHub.Agent;
using Xunit;

namespace StorageHub.Agent.IntegrationTests;

/// <summary>
/// What happens to the agent's paths when the data root turns out to be somewhere else.
/// </summary>
/// <remarks>
/// <para>
/// The host resolves a root, then leases a directory, and the leased one is authoritative --
/// STORAGEHUB_DATA_ROOT is read after the platform has already answered. Patching DataRoot alone
/// left RuntimeRoot pointing into the tree resolved first, so an agent told to use one root wrote
/// its runtime secrets into another. On a machine where that first root belonged to somebody else
/// the agent did not start at all, and said so with a stack trace about a path nobody had asked
/// for.
/// </para>
/// <para>
/// The distinction these tests hold is why the two are separate fields: Linux puts the runtime root
/// on tmpfs, deliberately outside the data root, and moving that with the data root would put key
/// material on a disk.
/// </para>
/// </remarks>
public class AgentPathsTests
{
    [Fact]
    public void MovingTheDataRootMovesRuntimeStateThatWasInsideIt()
    {
        var original = Rooted("old");
        var paths = new AgentPaths(original, Path.Combine(original, "Runtime"));

        var moved = paths.WithDataRoot(Rooted("new"));

        Assert.Equal(Rooted("new"), moved.DataRoot);
        Assert.Equal(Path.Combine(Rooted("new"), "Runtime"), moved.RuntimeRoot);
        Assert.Equal(
            Path.Combine(Rooted("new"), "Runtime", "runtime-secrets"),
            moved.RuntimeSecretsDirectory);
    }

    /// <summary>Deeper than one level, because the two roots need not be siblings.</summary>
    [Fact]
    public void ANestedRuntimeRootKeepsItsPositionUnderTheNewRoot()
    {
        var original = Rooted("old");
        var paths = new AgentPaths(original, Path.Combine(original, "var", "run"));

        var moved = paths.WithDataRoot(Rooted("new"));

        Assert.Equal(Path.Combine(Rooted("new"), "var", "run"), moved.RuntimeRoot);
    }

    /// <summary>
    /// A runtime root outside the data root stays exactly where it is.
    /// </summary>
    /// <remarks>
    /// This is the Linux case and the reason the rule is "what was inside moves" rather than
    /// "recompute from the new root": there, the runtime root is tmpfs, and following the data root
    /// would write key material to a disk.
    /// </remarks>
    [Fact]
    public void ARuntimeRootOutsideTheDataRootIsLeftAlone()
    {
        var elsewhere = Rooted("run", "user", "1000", "storagehub");
        var paths = new AgentPaths(Rooted("old"), elsewhere);

        var moved = paths.WithDataRoot(Rooted("new"));

        Assert.Equal(Rooted("new"), moved.DataRoot);
        Assert.Equal(elsewhere, moved.RuntimeRoot);
    }

    /// <summary>A runtime root that is the data root becomes the new one rather than a child of it.</summary>
    [Fact]
    public void ARuntimeRootEqualToTheDataRootBecomesTheNewRoot()
    {
        var original = Rooted("old");
        var paths = new AgentPaths(original, original);

        var moved = paths.WithDataRoot(Rooted("new"));

        Assert.Equal(Rooted("new"), moved.RuntimeRoot);
    }

    /// <summary>Moving to where it already is changes nothing, which a restart relies on.</summary>
    [Fact]
    public void MovingToTheSameRootIsANoOp()
    {
        var original = Rooted("same");
        var paths = new AgentPaths(original, Path.Combine(original, "Runtime"));

        var moved = paths.WithDataRoot(original);

        Assert.Equal(paths, moved);
    }

    /// <summary>The agent's own subtree follows the data root, because it is defined from it.</summary>
    [Fact]
    public void TheAgentSubtreeFollowsTheDataRoot()
    {
        var paths = new AgentPaths(Rooted("old"), Path.Combine(Rooted("old"), "Runtime"));

        var moved = paths.WithDataRoot(Rooted("new"));

        Assert.StartsWith(Rooted("new"), moved.AgentDirectory, StringComparison.Ordinal);
        Assert.StartsWith(Rooted("new"), moved.DatabasePath, StringComparison.Ordinal);
        Assert.StartsWith(Rooted("new"), moved.VaultDirectory, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyRootIsRefused(string root)
    {
        var paths = new AgentPaths(Rooted("old"), Rooted("old", "Runtime"));

        _ = Assert.Throws<ArgumentException>(() => paths.WithDataRoot(root));
    }

    /// <summary>
    /// An absolute path, spelled the way this platform spells one.
    /// </summary>
    /// <remarks>
    /// "C:\x" is a relative filename on Linux, which is the difference that has failed these suites
    /// before. Building the path rather than writing it is what lets one assertion hold on both.
    /// </remarks>
    private static string Rooted(params string[] segments) =>
        Path.GetFullPath(Path.Combine(
            OperatingSystem.IsWindows() ? @"C:\" : "/",
            Path.Combine(segments)));
}
