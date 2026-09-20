using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace StorageHub.Infrastructure.Unix;

/// <summary>
/// Keeps the vault's master key in a 0600 file this user owns.
/// </summary>
/// <remarks>
/// <para>
/// The obvious answer here is libsecret, and it is the wrong one. A StorageHub agent registered as a
/// lingering systemd user unit is designed to run with nobody signed in - that is the whole point of
/// the mode, and what makes a scheduled sync trustworthy. In that state there is no session bus and
/// no unlocked keyring, so every vault read would fail. It also adds a native dependency absent on
/// headless servers, in containers, under WSL and over SSH. The same objection applies to a
/// passphrase-derived key: it prompts, and unattended work must not.
/// </para>
/// <para>
/// So it is worth being exact about what DPAPI-CurrentUser buys on Windows: protection from another
/// local user, and nothing at all against code already running as the owner. A 0600 file owned by
/// this user provides precisely that. This is parity, not a downgrade.
/// </para>
/// <para>
/// Linux has no service mode, so unlike Windows there is only ever one protection here. The store
/// exists so that both platforms produce the same envelopes from the same kind of key.
/// </para>
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class UnixKeyFileMasterKeyStore : IMasterKeyStore
{
    private readonly string _keyPath;
    private readonly string _directory;

    public UnixKeyFileMasterKeyStore(string secretsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretsDirectory);
        if (!Path.IsPathFullyQualified(secretsDirectory))
        {
            throw new ArgumentException("The secrets directory must be an absolute path.", nameof(secretsDirectory));
        }

        _directory = secretsDirectory;
        _keyPath = Path.Combine(secretsDirectory, KeyFileSecretProtector.KeyFileName);
    }

    public string Description => "your account";

    public bool Exists => File.Exists(_keyPath);

    /// <remarks>
    /// A key file with the wrong mode or the wrong owner is a hard error. Regenerating one would be
    /// the intuitive recovery and would destroy every secret in the vault, so an anomaly is reported
    /// with what to do about it instead.
    /// </remarks>
    public byte[] LoadOrCreate()
    {
        UnixFileSystem.EnsurePrivateDirectory(_directory);

        if (File.Exists(_keyPath))
        {
            UnixFileSystem.VerifyPrivate(_keyPath, UnixFileSystem.PrivateFileMode, "vault key");
            return File.ReadAllBytes(_keyPath);
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

    public void Write(ReadOnlySpan<byte> masterKey)
    {
        UnixFileSystem.EnsurePrivateDirectory(_directory);

        var temporaryPath = _keyPath + ".new-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = UnixFileSystem.CreatePrivateFile(temporaryPath))
            {
                stream.Write(masterKey);
                stream.Flush(flushToDisk: true);
            }

            // Published by move, so a reader never sees a partially written key.
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
