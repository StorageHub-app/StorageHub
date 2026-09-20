using StorageHub.Ipc;
using StorageHub.Infrastructure.Unix;
using StorageHub.Ipc.Unix;
using StorageHub.Testing;

namespace StorageHub.Ipc.Unix.Tests;

/// <summary>
/// The uid check that is the whole boundary on this platform.
/// </summary>
/// <remarks>
/// The rule is asserted against synthesised identities rather than against a second process running
/// as a second user. Standing one up needs a privileged runner, and the named-pipe suite already
/// records what happens when a security rule is decided against whatever the test host happens to
/// be: it passed locally and failed on a build agent. What the kernel reports is exercised by the
/// round-trip cases; what is decided about it belongs here.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class UnixPeerCredentialTests
{
    [LinuxOnlyFact]
    public void ThisUsersOwnConnectionIsAdmitted()
    {
        var self = new IpcPeerIdentity(
            UnixPeerCredentials.PeerKind,
            UnixPeerCredentials.EffectiveUserId().ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.True(new UnixPeerCredentialAuthorizer().IsAuthorized(self, out var reason));
        Assert.Null(reason);
    }

    [LinuxOnlyFact]
    public void AnotherUsersConnectionIsRefused()
    {
        var other = new IpcPeerIdentity(UnixPeerCredentials.PeerKind, "65534");

        Assert.False(new UnixPeerCredentialAuthorizer().IsAuthorized(other, out var reason));
        Assert.NotNull(reason);
    }

    [LinuxOnlyFact]
    public void AnUnidentifiedPeerIsRefused()
    {
        // The named-pipe transport reports no peer, because Windows decided before the accept
        // returned. Here nothing has decided yet, so an unknown peer is a refusal rather than a
        // formality - the two transports must not share a default.
        Assert.False(new UnixPeerCredentialAuthorizer().IsAuthorized(IpcPeerIdentity.Unknown, out var reason));
        Assert.NotNull(reason);
    }

    [LinuxOnlyFact]
    public void AnIdentityFromAnotherKindOfSystemIsNotThisUser()
    {
        // A SID and a uid are both strings, and "1000" could be either. The kind keeps a value from
        // one authority from ever satisfying a check made against the other.
        var windowsShaped = new IpcPeerIdentity("windows-sid", "S-1-5-18");

        Assert.False(UnixPeerCredentials.IsSelf(windowsShaped));
    }

    [LinuxOnlyFact]
    public void TheOwnerOfAPathIsReported()
    {
        var file = Path.Combine(Path.GetTempPath(), $"storagehub-owner-{Guid.NewGuid():N}");
        File.WriteAllText(file, string.Empty);
        try
        {
            Assert.Equal(UnixPeerCredentials.EffectiveUserId(), UnixFileSystem.OwnerUserId(file));
        }
        finally
        {
            File.Delete(file);
        }
    }
}
