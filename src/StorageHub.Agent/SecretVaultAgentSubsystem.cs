using System.Security.Cryptography;
using StorageHub.Contracts.Agent;
using StorageHub.Security;

namespace StorageHub.Agent;

/// <summary>
/// The credential vault, and proof that whatever protects it actually works.
/// </summary>
/// <remarks>
/// The protector arrives as a factory rather than an instance because building one can fail for
/// reasons the agent should survive: DPAPI is unavailable to this account, or a Unix key file has
/// the wrong mode. Those become a recovery-only start - the agent runs, reports itself degraded and
/// refuses credential work - instead of a process that will not launch at all.
///
/// It used to take a DPAPI scope, which made the subsystem Windows-only for the sake of one
/// constructor argument. What it actually needs is an ISecretProtector, and the platform decides
/// which one: the scope moved with the decision it belongs to.
/// </remarks>
public sealed class SecretVaultAgentSubsystem : IAgentSubsystem, IDisposable
{
    private readonly string _vaultDirectory;
    private readonly Func<ISecretProtector> _protectorFactory;
    private ISecretProtector? _protector;
    private VersionedFileSecretVault? _vault;
    private bool _healthy;
    private bool _disposed;

    public SecretVaultAgentSubsystem(string vaultDirectory, Func<ISecretProtector> protectorFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultDirectory);
        ArgumentNullException.ThrowIfNull(protectorFactory);
        _vaultDirectory = Path.GetFullPath(vaultDirectory);
        _protectorFactory = protectorFactory;
    }

    public string Name => "Credential vault";

    public bool CanRunInRecoveryMode => true;

    public ISecretVault Vault => _vault ?? throw new InvalidOperationException(
        "The credential vault is not initialized.");

    /// <summary>
    /// Builds the protector and round-trips a random probe through it before trusting it.
    /// </summary>
    /// <remarks>
    /// The vault stamps every envelope with the protector's scheme and refuses a foreign one, so a
    /// mode change surfaces as a clear failure rather than as silently unreadable credentials. The
    /// probe catches the other case: a protector that is present but cannot actually decrypt what it
    /// just encrypted, which is what a broken DPAPI profile or a replaced key file looks like.
    /// </remarks>
    public Task<SubsystemInitializationResult> InitializeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        Span<byte> probe = stackalloc byte[32];
        Span<byte> entropy = stackalloc byte[32];
        RandomNumberGenerator.Fill(probe);
        RandomNumberGenerator.Fill(entropy);
        byte[]? encrypted = null;
        byte[]? plaintext = null;
        try
        {
            _protector = _protectorFactory();
            encrypted = _protector.Protect(probe, entropy);
            plaintext = _protector.Unprotect(encrypted, entropy);
            if (!CryptographicOperations.FixedTimeEquals(probe, plaintext))
            {
                return Task.FromResult(SubsystemInitializationResult.RecoveryOnly(
                    $"The {_protector.Scheme} credential protector failed its integrity check."));
            }

            _vault = new VersionedFileSecretVault(_vaultDirectory, _protector);
            _healthy = true;
            return Task.FromResult(SubsystemInitializationResult.Ready());
        }
        catch (CryptographicException error)
        {
            return Task.FromResult(SubsystemInitializationResult.RecoveryOnly(
                $"The credential protector is unavailable: {error.Message}"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A key file with the wrong mode or owner. Reported rather than repaired, because the
            // intuitive repair would destroy every secret in the vault.
            return Task.FromResult(SubsystemInitializationResult.RecoveryOnly(
                $"The credential protector could not be opened: {error.Message}"));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(probe);
            CryptographicOperations.ZeroMemory(entropy);
            if (encrypted is not null)
            {
                CryptographicOperations.ZeroMemory(encrypted);
            }

            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<SubsystemHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_healthy
            ? SubsystemHealth.Healthy($"The {_protector?.Scheme} vault is available.")
            : SubsystemHealth.Unhealthy("The credential vault is unavailable."));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _healthy = false;
        _vault?.Dispose();
        (_protector as IDisposable)?.Dispose();
    }
}
