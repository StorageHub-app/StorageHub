using System.Security.Cryptography;
using StorageHub.Security;

namespace StorageHub.Infrastructure.Unix;

/// <summary>
/// Protects the vault's entries with a master key only this user can read.
/// </summary>
/// <remarks>
/// <para>
/// The obvious answer here is libsecret, and it is the wrong one. A StorageHub agent registered as
/// a lingering systemd user unit is designed to run with nobody signed in - that is the whole point
/// of the mode, and what makes a scheduled sync trustworthy. In that state there is no session bus
/// and no unlocked keyring, so every vault read would fail. It also adds a native dependency that
/// is absent on headless servers, in containers, under WSL and over SSH. The same objection applies
/// to a passphrase-derived key: it prompts, and unattended work must not.
/// </para>
/// <para>
/// So it is worth being exact about what DPAPI-CurrentUser actually buys on Windows: protection
/// from another local user, and nothing at all against code already running as the owner. A 0600
/// file owned by this user provides precisely that. This is parity, not a downgrade.
/// </para>
/// <para>
/// The scheme string is stamped into every envelope and the vault refuses an entry whose scheme is
/// not its own, so a vault written under DPAPI fails loudly on Linux rather than decrypting to
/// nonsense - and moving between them stays an explicit re-write.
/// </para>
/// </remarks>
public sealed class UnixKeyFileSecretProtector : ISecretProtector, IDisposable
{
    /// <summary>The file holding the master key, inside the agent's directory.</summary>
    public const string KeyFileName = "master.key";

    private const int MasterKeyLength = 32;
    private const int SubKeyLength = 32;
    private const int NonceLength = 12;
    private const int TagLength = 16;

    private readonly byte[] _subKey;
    private bool _disposed;

    public UnixKeyFileSecretProtector(string secretsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretsDirectory);
        if (!Path.IsPathFullyQualified(secretsDirectory))
        {
            throw new ArgumentException("The secrets directory must be an absolute path.", nameof(secretsDirectory));
        }

        var master = LoadOrCreateMasterKey(Path.Combine(secretsDirectory, KeyFileName), secretsDirectory);
        try
        {
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

    public string Scheme => "linux-aesgcm-keyfile-v1";

    /// <summary>
    /// Encrypts with the entropy as additional authenticated data.
    /// </summary>
    /// <remarks>
    /// AAD rather than part of the key, which mirrors DPAPI's (data, entropy) contract exactly. The
    /// vault already passes per-secret entropy derived from the reference and version, so binding it
    /// here means an envelope moved to another reference fails authentication rather than decrypting.
    /// </remarks>
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CryptographicOperations.ZeroMemory(_subKey);
    }

    /// <summary>
    /// Reads the master key, creating one only when there is none.
    /// </summary>
    /// <remarks>
    /// A key file with the wrong mode or the wrong owner is a hard error. Regenerating one would be
    /// the intuitive recovery and would destroy every secret in the vault, so an anomaly is reported
    /// with what to do about it instead.
    /// </remarks>
    private static byte[] LoadOrCreateMasterKey(string keyPath, string secretsDirectory)
    {
        UnixFileSystem.EnsurePrivateDirectory(secretsDirectory);

        if (File.Exists(keyPath))
        {
            UnixFileSystem.VerifyPrivate(keyPath, UnixFileSystem.PrivateFileMode, "vault key");
            var existing = File.ReadAllBytes(keyPath);
            if (existing.Length != MasterKeyLength)
            {
                CryptographicOperations.ZeroMemory(existing);
                throw new CryptographicException(
                    $"The StorageHub vault key at '{keyPath}' is not {MasterKeyLength} bytes. "
                        + "It has been truncated or replaced; restore it from a backup rather than "
                        + "deleting it, which would make every stored secret unreadable.");
            }

            return existing;
        }

        var created = RandomNumberGenerator.GetBytes(MasterKeyLength);
        var temporaryPath = keyPath + ".new-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = UnixFileSystem.CreatePrivateFile(temporaryPath))
            {
                stream.Write(created);
                stream.Flush(flushToDisk: true);
            }

            // Published by move, so a reader never sees a partially written key.
            File.Move(temporaryPath, keyPath);
            return created;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(created);
            TryDelete(temporaryPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }
}
