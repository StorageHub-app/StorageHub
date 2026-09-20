using System.Security.Cryptography;
using StorageHub.Agent.Host;

namespace StorageHub.Agent.Windows.Tests;

/// <summary>
/// A fingerprint names 32 bytes, whichever way it is written.
/// </summary>
/// <remarks>
/// The trust store keeps a hexadecimal fingerprint as hexadecimal and a base64 one as base64, and
/// the SSH library reports base64. The terminal service compared the two as strings, so a host
/// pinned in hexadecimal could open an SFTP listing and never a shell. These pin the comparison
/// that replaced it.
/// </remarks>
public sealed class SshFingerprintsTests
{
    private static readonly byte[] Hash = SHA256.HashData("ssh-ed25519 AAAA host key bytes"u8);

    private static string Hex => Convert.ToHexString(Hash);

    private static string Base64 => "SHA256:" + Convert.ToBase64String(Hash).TrimEnd('=');

    [Fact]
    public void Hexadecimal_and_base64_name_the_same_key()
    {
        Assert.True(SshFingerprints.Equivalent(Hex, Base64));
        Assert.True(SshFingerprints.Equivalent(Hex.ToLowerInvariant(), Base64));
        Assert.True(SshFingerprints.Equivalent(WithColons(Hex), Base64));
        Assert.True(SshFingerprints.Equivalent("sha256:" + Base64[7..], Hex));
    }

    [Fact]
    public void A_received_key_is_trusted_by_bytes_against_either_spelling()
    {
        Assert.True(SshFingerprints.IsTrusted(Hash, [Hex]));
        Assert.True(SshFingerprints.IsTrusted(Hash, [Base64]));
        Assert.True(SshFingerprints.IsTrusted(Hash, ["not a fingerprint", Hex]));

        var other = SHA256.HashData("some other key"u8);
        Assert.False(SshFingerprints.IsTrusted(other, [Hex, Base64]));
        Assert.False(SshFingerprints.IsTrusted(Hash, []));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SHA256:")]
    [InlineData("SHA256:not base64 at all!!")]
    [InlineData("ABCDEF")]
    [InlineData("MD5:aa:bb:cc")]
    public void Anything_else_is_not_a_fingerprint(string value)
    {
        Assert.False(SshFingerprints.TryDecode(value, out var bytes));
        Assert.Empty(bytes);
        Assert.False(SshFingerprints.Equivalent(value, Hex));
    }

    private static string WithColons(string hex) =>
        string.Join(':', Enumerable.Range(0, hex.Length / 2).Select(index => hex.Substring(index * 2, 2)));
}
