using System.Security.Cryptography;
using System.Text;

namespace StorageHub.Infrastructure.Tests;

/// <summary>
/// The vault's envelope, and the key the entries are bound to instead of the platform.
/// </summary>
/// <remarks>
/// Entries used to be encrypted directly with a platform secret, which tied a whole vault to that
/// secret: anything that changed it made every entry unreadable at once. Entries are encrypted with
/// a master key now, and only the key is wrapped by the platform - so the two platforms write the
/// same envelopes, and a vault is portable between them.
/// </remarks>
public sealed class KeyFileSecretProtectorTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"storagehub-keystore-{Guid.NewGuid():N}");

    /// <summary>A store that keeps the key in memory, standing in for a platform's protection.</summary>
    private sealed class FakeStore(string description) : IMasterKeyStore
    {
        private byte[]? _key;

        public string Description { get; } = description;

        public bool Exists => _key is not null;

        public byte[] LoadOrCreate()
        {
            _key ??= RandomNumberGenerator.GetBytes(KeyFileSecretProtector.MasterKeyLength);
            return (byte[])_key.Clone();
        }

        public void Write(ReadOnlySpan<byte> masterKey) => _key = masterKey.ToArray();
    }

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    [Fact]
    public void ASecretRoundTrips()
    {
        using var protector = new KeyFileSecretProtector(new FakeStore("test"));
        var entropy = Utf8("connection/42");

        var envelope = protector.Protect(Utf8("hunter2"), entropy);
        var restored = protector.Unprotect(envelope, entropy);

        Assert.Equal("hunter2", Encoding.UTF8.GetString(restored));
        Assert.NotEqual(Utf8("hunter2"), envelope);
    }

    /// <summary>
    /// The entropy is authenticated, not decorative.
    /// </summary>
    /// <remarks>
    /// The vault derives it from the reference and version, so an envelope lifted onto another
    /// reference has to fail rather than decrypt. This mirrors DPAPI's (data, entropy) contract,
    /// which is why the vault needed no changes to sit on top of it.
    /// </remarks>
    [Fact]
    public void AnEnvelopeMovedToAnotherReferenceFailsAuthentication()
    {
        using var protector = new KeyFileSecretProtector(new FakeStore("test"));

        var envelope = protector.Protect(Utf8("hunter2"), Utf8("connection/42"));

        Assert.Throws<AuthenticationTagMismatchException>(
            () => protector.Unprotect(envelope, Utf8("connection/43")));
    }

    [Fact]
    public void ATamperedEnvelopeFailsAuthentication()
    {
        using var protector = new KeyFileSecretProtector(new FakeStore("test"));
        var entropy = Utf8("connection/42");

        var envelope = protector.Protect(Utf8("hunter2"), entropy);
        envelope[^1] ^= 0xFF;

        Assert.Throws<AuthenticationTagMismatchException>(() => protector.Unprotect(envelope, entropy));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(27)]
    public void AnEnvelopeTooShortToBeOneIsRefused(int length)
    {
        using var protector = new KeyFileSecretProtector(new FakeStore("test"));

        Assert.Throws<CryptographicException>(
            () => protector.Unprotect(new byte[length], Utf8("connection/42")));
    }

    /// <summary>
    /// The scheme does not name the platform or the mode.
    /// </summary>
    /// <remarks>
    /// The vault stamps it into every envelope and refuses an entry whose scheme is not its own. A
    /// scheme that encoded how the key is protected would therefore make every entry unreadable the
    /// moment that changed - which is the bug this design removes.
    /// </remarks>
    [Fact]
    public void TheSchemeDescribesTheEnvelopeRatherThanTheKeysProtection()
    {
        using var user = new KeyFileSecretProtector(new FakeStore("your Windows account"));
        using var machine = new KeyFileSecretProtector(new FakeStore("this machine"));

        Assert.Equal(user.Scheme, machine.Scheme);
        Assert.DoesNotContain("dpapi", user.Scheme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user", user.Scheme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("machine", user.Scheme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("linux", user.Scheme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("windows", user.Scheme, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A vault is readable by any store holding the same key.
    /// </summary>
    /// <remarks>
    /// The property the separation buys. Entries are bound to the key, not to whatever protects the
    /// key file, so the same vault opens under DPAPI on Windows and a 0600 file on Linux - and a
    /// change to that protection is a change to one file rather than to every entry.
    /// </remarks>
    [Fact]
    public void AVaultIsReadableByAnyStoreHoldingTheSameKey()
    {
        var first = new FakeStore("one protection");
        var entropy = Utf8("connection/42");

        byte[] envelope;
        byte[] key;
        using (var writer = new KeyFileSecretProtector(first))
        {
            envelope = writer.Protect(Utf8("hunter2"), entropy);
            key = first.LoadOrCreate();
        }

        var second = new FakeStore("another protection");
        second.Write(key);

        using var reader = new KeyFileSecretProtector(second);
        Assert.Equal("hunter2", Encoding.UTF8.GetString(reader.Unprotect(envelope, entropy)));
    }

    /// <summary>A different key does not open it, which is the other half of that property.</summary>
    [Fact]
    public void AVaultIsNotReadableByAStoreHoldingADifferentKey()
    {
        var entropy = Utf8("connection/42");

        byte[] envelope;
        using (var writer = new KeyFileSecretProtector(new FakeStore("one")))
        {
            envelope = writer.Protect(Utf8("hunter2"), entropy);
        }

        using var other = new KeyFileSecretProtector(new FakeStore("another"));
        Assert.Throws<AuthenticationTagMismatchException>(() => other.Unprotect(envelope, entropy));
    }

    /// <summary>A key of the wrong length is refused rather than stretched into one.</summary>
    [Fact]
    public void AKeyOfTheWrongLengthIsRefused()
    {
        var store = new FakeStore("truncated");
        store.Write(new byte[16]);

        var error = Assert.Throws<CryptographicException>(() => new KeyFileSecretProtector(store));
        Assert.Contains("restore it from a backup", error.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
