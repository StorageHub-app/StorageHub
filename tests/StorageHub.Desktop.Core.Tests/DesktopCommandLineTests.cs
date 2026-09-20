namespace StorageHub.Desktop.Tests;

public sealed class DesktopCommandLineTests
{
    /// <summary>
    /// CodeLogic's parser reads the process command line unconditionally and cannot be suppressed.
    /// Left to it, these would print into a console a WinExe does not own and then exit with no
    /// window — so the shell has to claim them first.
    /// </summary>
    [Theory]
    [InlineData("--version")]
    [InlineData("--info")]
    [InlineData("--health")]
    [InlineData("--dry-run")]
    [InlineData("--generate-configs")]
    [InlineData("--generate-configs-force")]
    public void ArgumentsCodeLogicWouldClaimAreHandledBeforeTheFrameworkStarts(string argument)
    {
        Assert.True(DesktopCommandLine.TryHandleFrameworkArguments([argument], out _));
    }

    [Fact]
    public void VersionReportsSuccessSoScriptsCanReadIt()
    {
        Assert.True(DesktopCommandLine.TryHandleFrameworkArguments(["--version"], out var exitCode));
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void FrameworkAdministrationArgumentsAreRefusedRatherThanSilentlyIgnored()
    {
        Assert.True(DesktopCommandLine.TryHandleFrameworkArguments(["--generate-configs"], out var exitCode));
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public void ArgumentMatchingIgnoresCaseTheWayTheAgentOnlySwitchDoes()
    {
        Assert.True(DesktopCommandLine.TryHandleFrameworkArguments(["--VERSION"], out _));
    }

    [Fact]
    public void OrdinaryLaunchArgumentsAreLeftAlone()
    {
        Assert.False(DesktopCommandLine.TryHandleFrameworkArguments([], out var exitCode));
        Assert.Equal(0, exitCode);
        Assert.False(DesktopCommandLine.TryHandleFrameworkArguments(["--agent-only"], out _));
        Assert.False(DesktopCommandLine.TryHandleFrameworkArguments([@"C:\some\workspace.storagehub"], out _));
    }
}
