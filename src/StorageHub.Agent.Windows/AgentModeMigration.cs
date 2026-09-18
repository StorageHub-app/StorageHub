using StorageHub.Infrastructure.Windows;
using StorageHub.Security;

namespace StorageHub.Agent.Windows;

/// <summary>What a migration attempt did, for reporting back to the operator.</summary>
public sealed record AgentModeMigrationReport(
    bool Succeeded,
    int SecretsCopied,
    int SecretsAlreadyPresent,
    int SecretsFailed,
    bool DatabaseCopied,
    string Summary);

/// <summary>
/// Copies a user-session installation into the machine location a service reads.
///
/// This only ever copies. The user's database and vault are left exactly as they are, so choosing
/// the service and then changing your mind is just changing the setting back -- nothing has to be
/// restored, because nothing was moved. It also means a half-finished migration is safe to re-run:
/// entries that already arrived are counted and skipped rather than overwritten.
///
/// Re-protecting the secrets is the reason this cannot be a file copy. Each secret is sealed with
/// the signed-in user's DPAPI key, which a service running as LocalSystem cannot use, so every
/// entry is opened with the user's key and written back under the machine key -- keeping its
/// reference, because saved connections point at those references by name.
///
/// It must run elevated *as the user who owns the vault*: elevation grants the write access to
/// ProgramData, while the user identity is what still opens the user-scoped secrets. A service
/// account could do neither half.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class AgentModeMigration
{
    /// <summary>
    /// Copies the database and re-protects the vault from the user root into the machine root.
    /// </summary>
    public static async Task<AgentModeMigrationReport> CopyUserInstallationToMachineAsync(
        string userDataRoot,
        string machineDataRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(machineDataRoot);

        var sourceAgent = Path.Combine(userDataRoot, "Agent");
        var targetAgent = Path.Combine(machineDataRoot, "Agent");
        if (!Directory.Exists(sourceAgent))
        {
            return new AgentModeMigrationReport(
                Succeeded: true,
                SecretsCopied: 0,
                SecretsAlreadyPresent: 0,
                SecretsFailed: 0,
                DatabaseCopied: false,
                "There was no existing installation to copy.");
        }

        _ = Directory.CreateDirectory(targetAgent);

        // The database first: a vault whose secrets have no profiles referencing them is harmless,
        // whereas profiles whose secrets have not arrived yet would look like broken connections.
        var databaseCopied = false;
        var sourceDatabase = Path.Combine(sourceAgent, "storagehub.db");
        var targetDatabase = Path.Combine(targetAgent, "storagehub.db");
        if (File.Exists(sourceDatabase) && !File.Exists(targetDatabase))
        {
            File.Copy(sourceDatabase, targetDatabase);
            databaseCopied = true;
        }

        var sourceVault = Path.Combine(sourceAgent, "vault");
        if (!Directory.Exists(sourceVault))
        {
            return new AgentModeMigrationReport(
                true, 0, 0, 0, databaseCopied, "Copied the database; there were no stored secrets.");
        }

        var targetVault = Path.Combine(targetAgent, "vault");
        _ = Directory.CreateDirectory(targetVault);
        var reader = new VersionedFileSecretVault(
            sourceVault, new WindowsDpapiProtector(DpapiProtectionScope.CurrentUser));
        var writer = new VersionedFileSecretVault(
            targetVault, new WindowsDpapiProtector(DpapiProtectionScope.LocalMachine));

        var copied = 0;
        var existing = 0;
        var failed = 0;
        foreach (var file in Directory.EnumerateFiles(sourceVault, "*.shv"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!SecretReference.TryParse(Path.GetFileNameWithoutExtension(file), out var reference))
            {
                continue;
            }

            try
            {
                await using var lease = await reader.OpenAsync(reference, cancellationToken)
                    .ConfigureAwait(false);
                if (await writer.ImportAsync(reference, lease.Memory, cancellationToken)
                        .ConfigureAwait(false))
                {
                    copied++;
                }
                else
                {
                    existing++;
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // One unreadable secret must not abandon the rest: the connection that used it can
                // be re-entered, whereas a migration that stops halfway leaves every later
                // connection broken too.
                failed++;
            }
        }

        var succeeded = failed == 0;
        var summary = succeeded
            ? $"Copied {copied} secret(s) and the database to the machine location."
            : $"Copied {copied} secret(s), but {failed} could not be read and must be entered again.";
        return new AgentModeMigrationReport(succeeded, copied, existing, failed, databaseCopied, summary);
    }
}
