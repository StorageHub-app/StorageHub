using System.Security.Principal;

namespace StorageHub.Agent;

/// <summary>
/// The accounts allowed to talk to the agent when it runs as a service.
///
/// In user-session mode the pipe is restricted to the account that created it and no list is
/// needed. A service serves whoever is signed in, so the set of permitted accounts has to be
/// written down somewhere both the elevated installer and the service can reach -- a file in the
/// machine data root, which only SYSTEM and Administrators can write.
/// </summary>
public static class AgentServiceClients
{
    private const string FileName = "service-clients.txt";

    /// <summary>The file listing permitted accounts, one SID per line.</summary>
    public static string ResolvePath(string dataRoot) =>
        Path.Combine(
            string.IsNullOrWhiteSpace(dataRoot)
                ? throw new ArgumentException("A data root is required.", nameof(dataRoot))
                : dataRoot,
            "Agent",
            FileName);

    /// <summary>
    /// Reads the permitted accounts, ignoring blank lines, comments and anything that is not a
    /// valid SID. Unreadable entries are dropped rather than throwing: a corrupt line must not
    /// stop the service, and the pipe refuses to start on an empty list anyway, which is a far
    /// clearer failure than a half-applied ACL.
    /// </summary>
    public static IReadOnlyList<string> ReadPermittedSids(string dataRoot)
    {
        var path = ResolvePath(dataRoot);
        if (!File.Exists(path))
        {
            return [];
        }

        var permitted = new List<string>();
        foreach (var line in File.ReadLines(path))
        {
            var candidate = line.Trim();
            if (candidate.Length == 0 || candidate.StartsWith('#'))
            {
                continue;
            }

            if (IsSid(candidate) && !permitted.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                permitted.Add(candidate);
            }
        }

        return permitted;
    }

    /// <summary>Replaces the permitted accounts with exactly the supplied SIDs.</summary>
    public static void WritePermittedSids(string dataRoot, IEnumerable<string> sids)
    {
        ArgumentNullException.ThrowIfNull(sids);
        var path = ResolvePath(dataRoot);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var lines = new List<string>
        {
            "# Accounts permitted to connect to the StorageHub agent service.",
            "# One security identifier per line; managed by StorageHub."
        };
        foreach (var sid in sids)
        {
            var candidate = sid?.Trim();
            if (!string.IsNullOrEmpty(candidate) && IsSid(candidate) &&
                !lines.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add(candidate);
            }
        }

        File.WriteAllLines(path, lines);
    }

    private static bool IsSid(string candidate)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            _ = new SecurityIdentifier(candidate);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
