using StorageHub.Contracts.Agent;
using System.Security.Cryptography;
using StorageHub.Infrastructure.Windows;
using StorageHub.Security;

namespace StorageHub.Agent.Windows;

internal sealed class SecretVaultAgentSubsystem(
    string vaultDirectory,
    DpapiProtectionScope protectionScope = DpapiProtectionScope.CurrentUser)
    : IAgentSubsystem, IDisposable
{
    private readonly string _vaultDirectory = Path.GetFullPath(
        string.IsNullOrWhiteSpace(vaultDirectory)
            ? throw new ArgumentException("A vault directory is required.", nameof(vaultDirectory))
            : vaultDirectory);
    private VersionedFileSecretVault? _vault;
    private bool _healthy;
    private bool _disposed;

    public string Name => "Credential vault";

    public bool CanRunInRecoveryMode => true;

    public ISecretVault Vault => _vault ?? throw new InvalidOperationException(
        "The credential vault is not initialized.");

    public Task<SubsystemInitializationResult> InitializeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        // A service has no signed-in user to derive a key from, so it must protect with the
        // machine key. The vault stamps every envelope with the protector's scheme and
        // refuses a foreign one, so a mode change surfaces as a clear failure rather than
        // as silently unreadable credentials.
        var protector = new WindowsDpapiProtector(protectionScope);
        Span<byte> probe = stackalloc byte[32];
        Span<byte> entropy = stackalloc byte[32];
        RandomNumberGenerator.Fill(probe);
        RandomNumberGenerator.Fill(entropy);
        byte[]? encrypted = null;
        byte[]? plaintext = null;
        try
        {
            encrypted = protector.Protect(probe, entropy);
            plaintext = protector.Unprotect(encrypted, entropy);
            if (!CryptographicOperations.FixedTimeEquals(probe, plaintext))
            {
                return Task.FromResult(SubsystemInitializationResult.RecoveryOnly(
                    "The Windows credential protector failed its integrity check."));
            }

            _vault = new VersionedFileSecretVault(_vaultDirectory, protector);
            _healthy = true;
            return Task.FromResult(SubsystemInitializationResult.Ready());
        }
        catch (CryptographicException)
        {
            return Task.FromResult(SubsystemInitializationResult.RecoveryOnly(
                "The Windows credential protector is unavailable for the current user."));
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
            ? SubsystemHealth.Healthy(protectionScope == DpapiProtectionScope.LocalMachine
                ? "The machine DPAPI vault is available."
                : "The current-user DPAPI vault is available.")
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
        _vault = null;
    }
}
