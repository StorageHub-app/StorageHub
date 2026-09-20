using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace StorageHub.Infrastructure.Windows;

/// <summary>
/// Keeps the vault's master key in a file DPAPI has wrapped.
/// </summary>
/// <remarks>
/// The key is wrapped for the current user, which no other account on the machine can unwrap. That
/// is the same protection the Linux store gets from a 0600 file, which is what lets both platforms
/// share one envelope format.
///
/// The separation of key from entries is worth keeping even though StorageHub now runs one agent
/// per user and never re-protects. It is what makes a vault portable, and what would make a
/// machine-scoped variant a new store rather than a rewrite of every entry.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsDpapiMasterKeyStore : IMasterKeyStore
{
    /// <summary>
    /// Ties the wrapped key to StorageHub, so a blob lifted from here cannot be unwrapped by
    /// another application that happens to call DPAPI with the same scope.
    /// </summary>
    private static readonly byte[] Entropy = "storagehub.vault.masterkey.v1"u8.ToArray();

    private readonly string _keyPath;
    private readonly string _directory;
    private readonly WindowsDpapiProtector _dpapi;

    public WindowsDpapiMasterKeyStore(string secretsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretsDirectory);
        if (!Path.IsPathFullyQualified(secretsDirectory))
        {
            throw new ArgumentException("The secrets directory must be an absolute path.", nameof(secretsDirectory));
        }

        _directory = secretsDirectory;
        _keyPath = Path.Combine(secretsDirectory, KeyFileSecretProtector.KeyFileName);

        // The existing protector rather than System.Security.Cryptography.ProtectedData: it already
        // P/Invokes CryptProtectData with CRYPTPROTECT_UI_FORBIDDEN and is already tested, and a
        // second way of calling the same API is a second way to get the flags wrong.
        _dpapi = new WindowsDpapiProtector(DpapiProtectionScope.CurrentUser);
    }

    public string Description => "your Windows account";

    public bool Exists => File.Exists(_keyPath);

    public byte[] LoadOrCreate()
    {
        Directory.CreateDirectory(_directory);

        if (File.Exists(_keyPath))
        {
            var wrapped = File.ReadAllBytes(_keyPath);
            try
            {
                return _dpapi.Unprotect(wrapped, Entropy);
            }
            catch (CryptographicException error)
            {
                // The key exists but this account cannot unwrap it - it belongs to a different
                // Windows user. Deleting it is the intuitive recovery and would destroy every
                // secret in the vault, so say what it is instead.
                throw new CryptographicException(
                    $"The StorageHub vault key at '{_keyPath}' was not protected for {Description}. "
                        + "It belongs to another Windows account. Sign in as that account, or restore "
                        + "this one's key from a backup - deleting it would make every stored secret "
                        + "unreadable.",
                    error);
            }
        }

        var created = RandomNumberGenerator.GetBytes(KeyFileSecretProtector.MasterKeyLength);
        try
        {
            Write(created);
            return created;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(created);
            throw;
        }
    }

    /// <summary>Writes the key wrapped, publishing by move so a reader never sees a partial file.</summary>
    public void Write(ReadOnlySpan<byte> masterKey)
    {
        Directory.CreateDirectory(_directory);

        var wrapped = _dpapi.Protect(masterKey, Entropy);
        var temporaryPath = _keyPath + ".new-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(temporaryPath, wrapped);
            File.Move(temporaryPath, _keyPath, overwrite: true);
        }
        catch
        {
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
