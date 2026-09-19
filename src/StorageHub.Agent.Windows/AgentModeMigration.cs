using System.Globalization;
using StorageHub.Infrastructure.Windows;
using StorageHub.Persistence;
using StorageHub.Security;

namespace StorageHub.Agent.Windows;

/// <summary>
/// Where one host mode keeps its durable state, and which key protects its secrets.
///
/// The two travel together on purpose. A data root without its protection scope is how a migration
/// ends up writing the user's secrets into the machine location still sealed with the user's key,
/// where the service cannot open a single one of them.
/// </summary>
public readonly record struct AgentDataLocation(string DataRoot, DpapiProtectionScope Scope)
{
    /// <summary>The location a given host mode reads and writes.</summary>
    public static AgentDataLocation For(AgentHostMode mode) => new(
        AgentHostLayout.ResolveDataRoot(mode),
        mode == AgentHostMode.WindowsService
            ? DpapiProtectionScope.LocalMachine
            : DpapiProtectionScope.CurrentUser);

    /// <summary>The same location with an explicitly supplied data root, keeping the mode's scope.</summary>
    public static AgentDataLocation For(AgentHostMode mode, string dataRoot) =>
        For(mode) with { DataRoot = dataRoot };

    internal string AgentDirectory => Path.Combine(DataRoot, "Agent");

    internal string DatabasePath => Path.Combine(AgentDirectory, "storagehub.db");

    internal string VaultDirectory => Path.Combine(AgentDirectory, "vault");
}

/// <summary>What a migration did, for reporting back to the operator.</summary>
public sealed record AgentModeMigrationReport(
    bool Succeeded,
    int SecretsCopied,
    int SecretsFailed,
    bool DatabaseCopied,
    string? ArchivedDirectory,
    string Summary);

/// <summary>
/// Moves an installation between the per-user and machine locations, in either direction.
///
/// Switching how the agent is hosted changes where it looks for everything it owns, so the switch
/// has to bring the installation with it. That is one job in two directions, not two jobs: choosing
/// the service copies the user's installation up to the machine location, and choosing either
/// session mode copies the machine's installation back down. The previous version only did the
/// first, so switching back silently presented whatever the user root happened to still contain --
/// usually a stale installation from before the switch, and everything done under the service
/// stranded in ProgramData.
///
/// It copies rather than moves, and archives anything it replaces, so a switch is reversible and a
/// half-finished one is safe to re-run: the source is left exactly as it was, and the destination's
/// previous contents are set aside rather than overwritten.
///
/// Re-protecting the secrets is why this cannot be a file copy. Each secret is sealed with the key
/// of the scope it was written in, so every entry is opened with the source's key and written back
/// under the destination's -- keeping its reference, because saved connections point at those
/// references by name.
///
/// It must run elevated *as the user who owns the user-scoped vault*: elevation grants access to
/// the machine location, while the user identity is what opens and writes the user's own secrets.
/// A service account could do neither half.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class AgentModeMigration
{
    /// <summary>
    /// Copies the database and re-protects the vault from one location into the other.
    /// </summary>
    public static async Task<AgentModeMigrationReport> MigrateAsync(
        AgentDataLocation source,
        AgentDataLocation destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source.DataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination.DataRoot);

        var sourceAgent = source.AgentDirectory;
        if (PathsMatch(sourceAgent, destination.AgentDirectory))
        {
            return Nothing("The installation is already in that location.");
        }

        if (!Directory.Exists(sourceAgent))
        {
            return Nothing("There was no existing installation to copy.");
        }

        var hasDatabase = File.Exists(source.DatabasePath);
        var secretFiles = EnumerateSecretFiles(source.VaultDirectory);
        if (!hasDatabase && secretFiles.Count == 0)
        {
            return Nothing("There was nothing stored to copy.");
        }

        _ = Directory.CreateDirectory(destination.AgentDirectory);

        // Anything already at the destination is set aside first. It is normally a stale copy from
        // an earlier switch, which must not be left to shadow what is arriving -- and is never
        // simply deleted, because "stale" is a guess about somebody else's data.
        var archived = ArchiveExistingInstallation(destination);

        // The database first: a vault whose secrets have no profiles referencing them is harmless,
        // whereas profiles whose secrets have not arrived yet would look like broken connections.
        var databaseCopied = false;
        if (hasDatabase)
        {
            await SqliteDatabaseCopy
                .CopyAsync(source.DatabasePath, destination.DatabasePath, cancellationToken)
                .ConfigureAwait(false);
            databaseCopied = true;
        }

        var (copied, failed) = await ReprotectSecretsAsync(
                source,
                destination,
                secretFiles,
                cancellationToken)
            .ConfigureAwait(false);

        return new AgentModeMigrationReport(
            failed == 0,
            copied,
            failed,
            databaseCopied,
            archived,
            BuildSummary(databaseCopied, copied, failed));

        static AgentModeMigrationReport Nothing(string summary) =>
            new(true, 0, 0, false, null, summary);
    }

    private static string BuildSummary(bool databaseCopied, int copied, int failed)
    {
        var database = databaseCopied ? "Copied the database" : "There was no database to copy";
        if (failed > 0)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                "{0} and {1} secret(s), but {2} could not be read and must be entered again.",
                database,
                copied,
                failed);
        }

        return copied == 0
            ? $"{database}; there were no stored secrets."
            : string.Format(
                CultureInfo.CurrentCulture,
                "{0} and re-protected {1} secret(s) for the new location.",
                database,
                copied);
    }

    private static IReadOnlyList<string> EnumerateSecretFiles(string vaultDirectory) =>
        Directory.Exists(vaultDirectory)
            ? [.. Directory.EnumerateFiles(vaultDirectory, "*.shv")]
            : [];

    private static async Task<(int Copied, int Failed)> ReprotectSecretsAsync(
        AgentDataLocation source,
        AgentDataLocation destination,
        IReadOnlyList<string> secretFiles,
        CancellationToken cancellationToken)
    {
        if (secretFiles.Count == 0)
        {
            return (0, 0);
        }

        _ = Directory.CreateDirectory(destination.VaultDirectory);
        using var reader = new VersionedFileSecretVault(
            source.VaultDirectory, new WindowsDpapiProtector(source.Scope));
        using var writer = new VersionedFileSecretVault(
            destination.VaultDirectory, new WindowsDpapiProtector(destination.Scope));

        var copied = 0;
        var failed = 0;
        foreach (var file in secretFiles)
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
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // One unreadable secret must not abandon the rest: the connection that used it can
                // be re-entered, whereas a migration that stops halfway leaves every later
                // connection broken too.
                failed++;
            }
        }

        return (copied, failed);
    }

    /// <summary>
    /// Moves a database and vault already at the destination into a dated folder beside them, and
    /// reports where they went, or null when there was nothing there.
    ///
    /// Both go together under one timestamp: a database separated from the vault its profiles point
    /// at is not something anybody can recover by hand.
    /// </summary>
    private static string? ArchiveExistingInstallation(AgentDataLocation destination)
    {
        var existing = new List<string>();
        if (File.Exists(destination.DatabasePath))
        {
            existing.Add(destination.DatabasePath);
        }

        existing.AddRange(
            SqliteDatabaseCopy.ResolveSidecarPaths(destination.DatabasePath).Where(File.Exists));
        var hasVault = Directory.Exists(destination.VaultDirectory) &&
            Directory.EnumerateFileSystemEntries(destination.VaultDirectory).Any();
        if (existing.Count == 0 && !hasVault)
        {
            return null;
        }

        var archive = Path.Combine(
            destination.AgentDirectory,
            "replaced-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        _ = Directory.CreateDirectory(archive);
        foreach (var file in existing)
        {
            File.Move(file, Path.Combine(archive, Path.GetFileName(file)));
        }

        if (hasVault)
        {
            Directory.Move(destination.VaultDirectory, Path.Combine(archive, "vault"));
        }

        return archive;
    }

    private static bool PathsMatch(string first, string second) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
            StringComparison.OrdinalIgnoreCase);
}
