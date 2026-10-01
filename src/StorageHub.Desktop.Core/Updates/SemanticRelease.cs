namespace StorageHub.Desktop.Updates;

/// <summary>
/// Comparing one release to another.
/// </summary>
/// <remarks>
/// Deliberately small. The only question the updater asks is "is the published release newer than
/// the one running", and the answer has to be wrong in the safe direction when a version cannot be
/// read - offering nothing rather than offering a downgrade.
///
/// Pre-release builds sort before the release they lead to, as semver requires, so 2.0.0-beta.1 is
/// not offered to someone already on 2.0.0.
/// </remarks>
internal static class SemanticRelease
{
    /// <summary>Longest version this will read, so a hostile feed cannot make it work hard.</summary>
    private const int MaximumLength = 64;

    internal static bool IsWellFormed(string? value) => TryParse(value, out _, out _);

    /// <summary>
    /// Whether <paramref name="candidate"/> is a release later than <paramref name="current"/>.
    /// </summary>
    /// <remarks>
    /// False whenever either cannot be read. An update that cannot be compared is an update that
    /// should not be offered, and a machine that stays on a working build is a better failure than
    /// one that installs something because a string looked odd.
    /// </remarks>
    internal static bool IsNewer(string? candidate, string? current)
    {
        if (!TryParse(candidate, out var right, out var rightPre)) return false;
        if (!TryParse(current, out var left, out var leftPre)) return false;

        for (var index = 0; index < 3; index++)
        {
            if (right[index] != left[index]) return right[index] > left[index];
        }

        // Same numbers: a release beats a pre-release of it, and two pre-releases sort as semver
        // says, identifier by identifier.
        if (leftPre is null && rightPre is null) return false;
        if (leftPre is null) return false;
        if (rightPre is null) return true;

        return ComparePrerelease(rightPre, leftPre) > 0;
    }

    /// <summary>
    /// Orders two pre-release labels identifier by identifier, numbers as numbers.
    /// </summary>
    /// <remarks>
    /// A candidate is <c>rc.&lt;run&gt;.g&lt;sha&gt;</c>, and compared as text rc.10 sorted before
    /// rc.9, so the tenth candidate was never offered to anybody on the ninth.
    /// </remarks>
    private static int ComparePrerelease(string left, string right)
    {
        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        for (var index = 0; index < Math.Min(leftParts.Length, rightParts.Length); index++)
        {
            var leftNumeric = long.TryParse(leftParts[index], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var leftNumber);
            var rightNumeric = long.TryParse(rightParts[index], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var rightNumber);
            var order = (leftNumeric, rightNumeric) switch
            {
                (true, true) => leftNumber.CompareTo(rightNumber),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(leftParts[index], rightParts[index])
            };
            if (order != 0) return order;
        }

        return leftParts.Length.CompareTo(rightParts.Length);
    }

    /// <summary>
    /// Reads "major.minor.patch", with an optional "-prerelease" and an ignored "+build".
    /// </summary>
    /// <remarks>
    /// Build metadata is discarded rather than compared, which is what semver says and what the
    /// release process needs: two builds of the same commit differ only there and are the same
    /// release.
    /// </remarks>
    private static bool TryParse(string? value, out int[] numbers, out string? prerelease)
    {
        numbers = [0, 0, 0];
        prerelease = null;

        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumLength) return false;

        var text = value.Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];

        var build = text.IndexOf('+', StringComparison.Ordinal);
        if (build >= 0) text = text[..build];

        var dash = text.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            prerelease = text[(dash + 1)..];
            text = text[..dash];
            if (prerelease.Length == 0) return false;
        }

        var parts = text.Split('.');
        if (parts.Length is < 1 or > 3) return false;

        for (var index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var number))
            {
                return false;
            }

            numbers[index] = number;
        }

        return true;
    }
}
