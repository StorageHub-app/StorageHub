using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace StorageHub.Desktop;

/// <summary>
/// Identifies the machine and account an export came from, so an import can tell a backup being
/// restored in place from a configuration arriving from somewhere else.
///
/// That distinction matters because connection profiles carry vault references. On the machine
/// that wrote them the references still resolve and the connections work untouched; anywhere else
/// they point at nothing, and those connections must arrive disabled and flagged rather than
/// failing later at connect time.
///
/// The raw machine name and account SID are never written to the file — only a truncated hash of
/// them, which is enough to compare and useless to read. An export is a file people share.
/// </summary>
internal static class SettingsExportFingerprint
{
    /// <summary>
    /// What identifies the account this export belongs to.
    /// </summary>
    /// <remarks>
    /// The SID on Windows rather than the user name: it is unique per account per machine and
    /// survives a rename. Linux has no equivalent that is both stable and cheap to read from here,
    /// so it falls back to the user name, which a rename does change. Getting this wrong only makes
    /// an export look foreign, and treating a foreign export as foreign is the safe direction.
    /// </remarks>
    private static string? ResolveAccount()
    {
        if (!OperatingSystem.IsWindows()) return Environment.UserName;

        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value;
    }

    /// <summary>
    /// Domain-separates the hash so it can never collide with another digest in this codebase,
    /// and pins the inputs: changing either would need a new label, not a silent redefinition.
    /// </summary>
    private const string Label = "storagehub.export.fingerprint.v1";

    private const int FingerprintBytes = 16;

    /// <summary>
    /// A stable identifier for this machine and user, or null when the account cannot be read —
    /// in which case an import simply treats the file as foreign, which is the safe direction.
    /// </summary>
    internal static string? Compute()
    {
        try
        {
            var account = ResolveAccount();
            if (string.IsNullOrEmpty(account)) return null;

            // Unit separators, so "machine" + "user" and "machine" + "user" cannot
            // hash alike.
            var material = Encoding.UTF8.GetBytes(
                $"{Label}{Environment.MachineName}{account}");
            return Convert.ToBase64String(SHA256.HashData(material).AsSpan(0, FingerprintBytes))
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a file was written by this machine and account. A null or absent fingerprint is
    /// treated as foreign: an import that wrongly assumes "same machine" would enable connections
    /// whose credentials are missing, while the reverse merely asks the user to re-enable them.
    /// </summary>
    internal static bool MatchesThisMachine(string? fingerprint) =>
        !string.IsNullOrEmpty(fingerprint) &&
        Compute() is { } current &&
        string.Equals(fingerprint, current, StringComparison.Ordinal);
}
