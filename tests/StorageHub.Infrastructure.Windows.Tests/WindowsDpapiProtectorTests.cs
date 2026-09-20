using System.Security.Cryptography;
using System.Text;
using StorageHub.Infrastructure.Windows;
using StorageHub.Testing;

namespace StorageHub.Infrastructure.Windows.Tests;

/// <summary>
/// The vault keys every stored envelope by its protector's scheme and rejects anything else, so a
/// service reading a vault written by a signed-in user must fail rather than improvise. These
/// tests pin the two halves of that contract: the scopes are separate schemes, and each one really
/// does round-trip through DPAPI.
/// </summary>
public sealed class WindowsDpapiProtectorTests
{
    [WindowsOnlyFact]
    public void The_two_scopes_are_distinct_schemes()
    {
        var user = new WindowsDpapiProtector(DpapiProtectionScope.CurrentUser);
        var machine = new WindowsDpapiProtector(DpapiProtectionScope.LocalMachine);

        Assert.NotEqual(user.Scheme, machine.Scheme);
        Assert.Contains("current-user", user.Scheme, StringComparison.Ordinal);
        Assert.Contains("local-machine", machine.Scheme, StringComparison.Ordinal);
    }

    /// <summary>
    /// Defaulting to the user scope matters: an existing installation must keep reading the vault
    /// it already wrote, so the parameterless shape cannot quietly become machine-scoped.
    /// </summary>
    [WindowsOnlyFact]
    public void The_default_scope_stays_the_signed_in_user()
    {
        Assert.Equal(
            new WindowsDpapiProtector(DpapiProtectionScope.CurrentUser).Scheme,
            new WindowsDpapiProtector().Scheme);
    }

    [WindowsOnlyTheory]
    [InlineData(DpapiProtectionScope.CurrentUser)]
    [InlineData(DpapiProtectionScope.LocalMachine)]
    public void A_payload_round_trips_within_its_own_scope(DpapiProtectionScope scope)
    {
        var protector = new WindowsDpapiProtector(scope);
        var plaintext = Encoding.UTF8.GetBytes("lab-passphrase");
        var entropy = RandomNumberGenerator.GetBytes(32);

        var sealedBytes = protector.Protect(plaintext, entropy);
        var opened = protector.Unprotect(sealedBytes, entropy);

        Assert.NotEqual(plaintext, sealedBytes);
        Assert.Equal(plaintext, opened);
    }

    /// <summary>
    /// Entropy is what keeps a machine-scoped vault from being readable by anything that merely
    /// runs on the machine, so a wrong value must fail rather than return garbage.
    /// </summary>
    [WindowsOnlyTheory]
    [InlineData(DpapiProtectionScope.CurrentUser)]
    [InlineData(DpapiProtectionScope.LocalMachine)]
    public void The_wrong_entropy_cannot_open_a_payload(DpapiProtectionScope scope)
    {
        var protector = new WindowsDpapiProtector(scope);
        var sealedBytes = protector.Protect(
            Encoding.UTF8.GetBytes("lab-passphrase"),
            RandomNumberGenerator.GetBytes(32));

        _ = Assert.Throws<CryptographicException>(() =>
            protector.Unprotect(sealedBytes, RandomNumberGenerator.GetBytes(32)));
    }

    [WindowsOnlyFact]
    public void An_undefined_scope_is_rejected()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => new WindowsDpapiProtector((DpapiProtectionScope)42));
    }
}
