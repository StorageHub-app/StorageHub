using System.Security.Cryptography;
using StorageHub.Security;

namespace StorageHub.Infrastructure;

/// <summary>
/// Holds the master key the vault's entries are encrypted with.
/// </summary>
/// <remarks>
/// The key is kept apart from the entries on purpose. Encrypting each entry directly with a platform
/// secret ties the whole vault to that secret, so anything that changes it invalidates every entry
/// at once; with a key in between, such a change is one file.
///
/// Implementations differ only in how the key file is protected at rest: DPAPI on Windows, file
/// permissions on Linux. What is inside is the same 32 bytes either way, which is what lets
/// <see cref="KeyFileSecretProtector"/> be one implementation across both - and what makes a vault
/// portable between them.
/// </remarks>
public interface IMasterKeyStore
{
    /// <summary>How this store protects the key file, for diagnostics and for refusing a mismatch.</summary>
    string Description { get; }

    /// <summary>Whether a key already exists here.</summary>
    bool Exists { get; }

    /// <summary>
    /// Reads the master key, creating one only when there is none.
    /// </summary>
    /// <remarks>
    /// The caller owns the array and should zero it. A key that exists but cannot be read is a hard
    /// error rather than a reason to make a new one: regenerating is the intuitive recovery and it
    /// would silently destroy every secret in the vault.
    /// </remarks>
    byte[] LoadOrCreate();

    /// <summary>Writes a master key here, replacing any that exists.</summary>
    void Write(ReadOnlySpan<byte> masterKey);
}

/// <summary>
/// Encrypts vault entries with a key from an <see cref="IMasterKeyStore"/>.
/// </summary>
/// <remarks>
/// One scheme for every platform. That is deliberate: the scheme is stamped into each envelope and
/// the vault refuses an entry whose scheme is not its own, so a scheme that named the platform or
/// the way the key is protected would make a vault unreadable the moment either changed. Here it
/// names the envelope format, which does not change.
///
/// The envelope is nonce(12) || ciphertext || tag(16) under AES-256-GCM, with the vault's per-secret
/// entropy as additional authenticated data - mirroring DPAPI's (data, entropy) contract exactly, so
/// an envelope moved to another reference fails authentication rather than decrypting.
/// </remarks>
public sealed class KeyFileSecretProtector : ISecretProtector, IDisposable
{
    /// <summary>The name of the key file inside whichever directory a store is given.</summary>
    public const string KeyFileName = "master.key";

    /// <summary>The length of the master key every store holds.</summary>
    public const int MasterKeyLength = 32;

    private const int SubKeyLength = 32;
    private const int NonceLength = 12;
    private const int TagLength = 16;

    private readonly byte[] _subKey;
    private bool _disposed;

    public KeyFileSecretProtector(IMasterKeyStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var master = store.LoadOrCreate();
        try
        {
            if (master.Length != MasterKeyLength)
            {
                throw new CryptographicException(
                    $"The StorageHub vault key is not {MasterKeyLength} bytes. It has been truncated "
                        + "or replaced; restore it from a backup rather than deleting it, which would "
                        + "make every stored secret unreadable.");
            }

            // A derived subkey rather than the master itself, so the same file can serve a later
            // purpose without two uses ever sharing a key.
            _subKey = HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                master,
                SubKeyLength,
                salt: null,
                info: "storagehub-secret-v1"u8.ToArray());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(master);
        }
    }

    /// <summary>
    /// The envelope format, which is the same wherever the key came from.
    /// </summary>
    /// <remarks>
    /// Not the platform, and not how the key is protected. Encoding either is what made a vault
    /// stop working when the thing that protected it changed.
    /// </remarks>
    public string Scheme => "storagehub-aesgcm-keyfile-v1";

    public byte[] Protect(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> entropy)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var envelope = new byte[NonceLength + plaintext.Length + TagLength];
        var nonce = envelope.AsSpan(0, NonceLength);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_subKey, TagLength);
        aes.Encrypt(
            nonce,
            plaintext,
            envelope.AsSpan(NonceLength, plaintext.Length),
            envelope.AsSpan(NonceLength + plaintext.Length, TagLength),
            entropy);
        return envelope;
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedData, ReadOnlySpan<byte> entropy)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (protectedData.Length < NonceLength + TagLength)
        {
            throw new CryptographicException("The protected envelope is too short to be valid.");
        }

        var cipherLength = protectedData.Length - NonceLength - TagLength;
        var plaintext = new byte[cipherLength];
        using var aes = new AesGcm(_subKey, TagLength);
        aes.Decrypt(
            protectedData[..NonceLength],
            protectedData.Slice(NonceLength, cipherLength),
            protectedData.Slice(NonceLength + cipherLength, TagLength),
            plaintext,
            entropy);
        return plaintext;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        CryptographicOperations.ZeroMemory(_subKey);
    }
}
