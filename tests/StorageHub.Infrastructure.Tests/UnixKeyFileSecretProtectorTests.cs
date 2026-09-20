using System.Security.Cryptography;
using StorageHub.Infrastructure.Unix;
using StorageHub.Security;
using StorageHub.Testing;

namespace StorageHub.Infrastructure.Unix.Tests;

/// <summary>
/// Protecting the vault with a key file, and refusing to when the file cannot be trusted.
/// </summary>
/// <remarks>
/// The cases that matter most are the refusals. Encrypting and decrypting correctly is table
/// stakes; what protects a vault is that a key file with the wrong mode or the wrong owner stops
/// the agent rather than being quietly adopted, and that a vault written under one scheme is never
/// opened under another.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class UnixKeyFileSecretProtectorTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"storagehub-vaultkey-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string Secrets => Path.Combine(_root, "secrets");

    [LinuxOnlyFact]
    public void ASecretRoundTripsWithItsOwnEntropy()
    {
        using var protector = new UnixKeyFileSecretProtector(Secrets);
        byte[] entropy = [9, 8, 7];
        byte[] plaintext = [1, 2, 3, 4, 5];

        var envelope = protector.Protect(plaintext, entropy);

        Assert.NotEqual(plaintext, envelope);
        Assert.Equal(plaintext, protector.Unprotect(envelope, entropy));
    }

    [LinuxOnlyFact]
    public void TheWrongEntropyFailsAuthenticationRatherThanDecrypting()
    {
        // The vault derives entropy per secret from its reference and version, so this is what stops
        // an envelope being moved to another reference and still opening. AES-GCM authenticates the
        // AAD, so the failure is a refusal rather than plausible-looking garbage.
        using var protector = new UnixKeyFileSecretProtector(Secrets);
        var envelope = protector.Protect([1, 2, 3], [9, 8, 7]);

        _ = Assert.Throws<AuthenticationTagMismatchException>(
            () => protector.Unprotect(envelope, [9, 8, 6]));
    }

    [LinuxOnlyFact]
    public void ATamperedEnvelopeIsRefused()
    {
        using var protector = new UnixKeyFileSecretProtector(Secrets);
        var envelope = protector.Protect([1, 2, 3], [9]);
        envelope[^1] ^= 0xFF;

        _ = Assert.Throws<AuthenticationTagMismatchException>(() => protector.Unprotect(envelope, [9]));
    }

    [LinuxOnlyFact]
    public void TheKeyFileIsPrivateFromTheMomentItExists()
    {
        using var protector = new UnixKeyFileSecretProtector(Secrets);

        var keyPath = Path.Combine(Secrets, UnixKeyFileSecretProtector.KeyFileName);

        // Created with the mode rather than chmod-ed afterwards, so there is no window in which the
        // vault's master key is readable by anyone else.
        Assert.True(File.Exists(keyPath));
        Assert.Equal(UnixFileSystem.PrivateFileMode, File.GetUnixFileMode(keyPath));
        Assert.Equal(UnixFileSystem.PrivateDirectoryMode, File.GetUnixFileMode(Secrets));
    }

    [LinuxOnlyFact]
    public void TheSameKeyFileKeepsOpeningWhatItWrote()
    {
        byte[] envelope;
        using (var first = new UnixKeyFileSecretProtector(Secrets))
        {
            envelope = first.Protect([4, 5, 6], [1]);
        }

        using var second = new UnixKeyFileSecretProtector(Secrets);
        Assert.Equal<byte[]>([4, 5, 6], second.Unprotect(envelope, [1]));
    }

    [LinuxOnlyFact]
    public void AKeyFileOtherUsersCanReadStopsTheAgent()
    {
        // Regenerating would be the intuitive recovery and would destroy every secret in the vault,
        // so a permissions anomaly is reported rather than repaired.
        using (var seed = new UnixKeyFileSecretProtector(Secrets))
        {
        }

        var keyPath = Path.Combine(Secrets, UnixKeyFileSecretProtector.KeyFileName);
        File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);

        var error = Assert.Throws<IOException>(() => new UnixKeyFileSecretProtector(Secrets));
        Assert.Contains("only by its owner", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [LinuxOnlyFact]
    public void ASymlinkedKeyFileIsRefused()
    {
        Directory.CreateDirectory(Secrets, UnixFileSystem.PrivateDirectoryMode);
        var elsewhere = Path.Combine(_root, "elsewhere.key");
        File.WriteAllBytes(elsewhere, RandomNumberGenerator.GetBytes(32));
        File.SetUnixFileMode(elsewhere, UnixFileSystem.PrivateFileMode);
        File.CreateSymbolicLink(Path.Combine(Secrets, UnixKeyFileSecretProtector.KeyFileName), elsewhere);

        var error = Assert.Throws<IOException>(() => new UnixKeyFileSecretProtector(Secrets));
        Assert.Contains("symbolic link", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [LinuxOnlyFact]
    public void ATruncatedKeyFileIsReportedRatherThanReplaced()
    {
        Directory.CreateDirectory(Secrets, UnixFileSystem.PrivateDirectoryMode);
        var keyPath = Path.Combine(Secrets, UnixKeyFileSecretProtector.KeyFileName);
        File.WriteAllBytes(keyPath, RandomNumberGenerator.GetBytes(16));
        File.SetUnixFileMode(keyPath, UnixFileSystem.PrivateFileMode);

        var error = Assert.Throws<CryptographicException>(() => new UnixKeyFileSecretProtector(Secrets));
        Assert.Contains("restore it from a backup", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A vault carries the scheme that wrote it, and refuses any other.
    /// </summary>
    /// <remarks>
    /// This is what makes moving between Windows and Linux an explicit re-write rather than a
    /// silent failure: a DPAPI envelope handed to this protector is refused by the vault before it
    /// is ever decrypted, and the same holds in the other direction.
    /// </remarks>
    [LinuxOnlyFact]
    public void TheSchemeNamesThisPlatformAndItsAlgorithm()
    {
        using var protector = new UnixKeyFileSecretProtector(Secrets);

        Assert.Equal("linux-aesgcm-keyfile-v1", protector.Scheme);
        Assert.NotEqual("windows-dpapi-current-user-v1", protector.Scheme);
    }

    [LinuxOnlyFact]
    public async Task TheVaultRoundTripsThroughThisProtector()
    {
        // The protector is only useful if the vault it was built for accepts it, so this exercises
        // the pairing rather than the protector alone.
        using var protector = new UnixKeyFileSecretProtector(Secrets);
        var vault = new VersionedFileSecretVault(Path.Combine(_root, "vault"), protector);

        var written = await vault.CreateAsync(new byte[] { 1, 1, 2, 3, 5, 8 });
        await using var lease = await vault.OpenAsync(written.Reference);

        Assert.Equal<byte[]>([1, 1, 2, 3, 5, 8], lease.Memory.ToArray());
    }
}
