using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// What a duplicated connection is called: the original's name with "(copy)" after it, then
/// "(copy 2)", "(copy 3)" and on, until one is free.
/// </summary>
internal static class ConnectionCopyNames
{
    /// <summary>The first name for a copy of <paramref name="name"/> that is not in <paramref name="taken"/>.</summary>
    /// <remarks>
    /// Names are unique ignoring case, as the profile store keeps them, so "x (Copy)" counts as taken
    /// by "x (copy)". A long name gives up its own end rather than the suffix, so the result still
    /// fits the contract's bound and still says it is a copy.
    /// </remarks>
    internal static string Next(string name, IEnumerable<string> taken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(taken);

        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        var stem = name.Trim();
        for (var number = 1; ; number++)
        {
            var candidate = Fit(stem, number);
            if (!used.Contains(candidate)) return candidate;
        }
    }

    private static string Fit(string stem, int number)
    {
        string Compose(string text) => number == 1
            ? Ui.Format(Ui.Connections.CopyNameFormat, text)
            : Ui.Format(Ui.Connections.CopyNumberedNameFormat, text, number);

        var composed = Compose(stem);
        var excess = composed.Length - ConnectionProfileIpcLimits.MaximumDisplayNameLength;
        return excess <= 0 ? composed : Compose(stem[..Math.Max(1, stem.Length - excess)].TrimEnd());
    }
}
