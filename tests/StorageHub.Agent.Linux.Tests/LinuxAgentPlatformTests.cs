using StorageHub.Agent;
using StorageHub.Agent.Linux;
using StorageHub.Ipc;
using StorageHub.Ipc.Unix;
using StorageHub.Testing;

namespace StorageHub.Agent.Linux.Tests;

/// <summary>
/// Where a Linux agent puts its files, and which modes it admits.
/// </summary>
/// <remarks>
/// The cases that matter are the ones Environment.SpecialFolder would have got wrong:
/// LocalApplicationData resolves to the config directory here, and CommonApplicationData to a
/// root-owned /usr/share, so a straight port of the Windows layout would have produced a data root
/// that is either the wrong kind of directory or unwritable - and both only fail at the first write.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class LinuxAgentPlatformTests : IDisposable
{
    private readonly Dictionary<string, string?> _restore = [];

    public void Dispose()
    {
        foreach (var (name, value) in _restore)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    private void Set(string name, string? value)
    {
        _restore.TryAdd(name, Environment.GetEnvironmentVariable(name));
        Environment.SetEnvironmentVariable(name, value);
    }

    [LinuxOnlyFact]
    public void TheDataRootFollowsXdgDataHome()
    {
        Set("XDG_DATA_HOME", "/tmp/storagehub-xdg-data");
        Set(AgentHostLayout.DataRootVariable, null);

        Assert.Equal("/tmp/storagehub-xdg-data/storagehub", LinuxAgentPaths.ResolveDataRoot());
    }

    [LinuxOnlyFact]
    public void WithoutXdgDataHomeTheSpecifiedDefaultIsUsed()
    {
        Set("XDG_DATA_HOME", null);
        Set(AgentHostLayout.DataRootVariable, null);
        Set("HOME", "/home/someone");

        // ~/.local/share, which is the XDG default - and notably not ~/.config, where
        // Environment.SpecialFolder.LocalApplicationData would have landed.
        Assert.Equal("/home/someone/.local/share/storagehub", LinuxAgentPaths.ResolveDataRoot());
    }

    [LinuxOnlyFact]
    public void TheDataRootOverrideStillWins()
    {
        Set("XDG_DATA_HOME", "/tmp/storagehub-xdg-data");
        Set(AgentHostLayout.DataRootVariable, "/tmp/storagehub-explicit");

        Assert.Equal("/tmp/storagehub-explicit", LinuxAgentPaths.ResolveDataRoot());
    }

    [LinuxOnlyFact]
    public void TheRuntimeRootFollowsXdgRuntimeDir()
    {
        Set("XDG_RUNTIME_DIR", "/run/user/4242");

        Assert.Equal("/run/user/4242/storagehub", LinuxAgentPaths.ResolveRuntimeRoot());
    }

    [LinuxOnlyFact]
    public void WithoutXdgRuntimeDirAPerUserFallbackIsUsed()
    {
        // Absent on plain SSH sessions, in containers, and under cron - common enough that falling
        // back matters, and the fallback is still per-uid so two users cannot collide.
        Set("XDG_RUNTIME_DIR", null);

        var runtimeRoot = LinuxAgentPaths.ResolveRuntimeRoot();

        Assert.Contains(
            UnixPeerCredentials.EffectiveUserId().ToString(System.Globalization.CultureInfo.InvariantCulture),
            runtimeRoot,
            StringComparison.Ordinal);
    }

    [LinuxOnlyFact]
    public void RuntimeStateIsSeparatedFromDurableState()
    {
        Set("XDG_DATA_HOME", "/tmp/storagehub-xdg-data");
        Set("XDG_RUNTIME_DIR", "/run/user/4242");
        Set(AgentHostLayout.DataRootVariable, null);

        var paths = new LinuxAgentPlatform().ResolvePaths(AgentHostMode.UserSession);

        // The database survives a reboot; the materialised key material must not, which is the
        // whole reason these are two roots rather than one.
        Assert.StartsWith("/tmp/storagehub-xdg-data", paths.DatabasePath, StringComparison.Ordinal);
        Assert.StartsWith("/run/user/4242", paths.RuntimeSecretsDirectory, StringComparison.Ordinal);
    }

    [LinuxOnlyFact]
    public void ThereIsNoServiceModeOnLinux()
    {
        var platform = new LinuxAgentPlatform();

        Assert.DoesNotContain(AgentHostMode.WindowsService, platform.SupportedHostModes);
        _ = Assert.Throws<PlatformNotSupportedException>(
            () => platform.ResolvePaths(AgentHostMode.WindowsService));
    }

    [LinuxOnlyFact]
    public void BothSessionModesShareEverythingButTheirRegistration()
    {
        var platform = new LinuxAgentPlatform();

        // Same uid, same data root, same socket. The only difference is whether an autostart
        // registration outlives the app, which is why neither needs a mode of its own.
        Assert.Equal(
            platform.ResolvePaths(AgentHostMode.UserSession),
            platform.ResolvePaths(AgentHostMode.AppSession));
        Assert.Equal(
            platform.ResolveEndpoint(AgentHostMode.UserSession, AgentIpcChannel.Normal),
            platform.ResolveEndpoint(AgentHostMode.AppSession, AgentIpcChannel.Normal));
    }

    [LinuxOnlyFact]
    public void TheTwoChannelsGetDistinctSockets()
    {
        var platform = new LinuxAgentPlatform();

        var normal = Assert.IsType<UnixSocketEndpoint>(
            platform.ResolveEndpoint(AgentHostMode.UserSession, AgentIpcChannel.Normal));
        var secret = Assert.IsType<UnixSocketEndpoint>(
            platform.ResolveEndpoint(AgentHostMode.UserSession, AgentIpcChannel.Secret));

        Assert.NotEqual(normal.SocketPath, secret.SocketPath);
    }

    [LinuxOnlyFact]
    public void EveryModeIsSameUserTrust()
    {
        var platform = new LinuxAgentPlatform();

        Assert.Equal(IpcTrustModel.SameUser, platform.ResolveTrustModel(AgentHostMode.UserSession));
        Assert.Equal(IpcTrustModel.SameUser, platform.ResolveTrustModel(AgentHostMode.AppSession));
    }

    [LinuxOnlyFact]
    public void NothingHereAsksForPrivilegeItWasNotStartedWith()
    {
        Assert.False(new LinuxAgentPlatform().CanElevate);
    }
}
