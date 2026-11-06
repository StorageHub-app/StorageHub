using StorageHub.Desktop.Configuration;

namespace StorageHub.Desktop;

internal static class EditorAssociations
{
    internal const int MaximumRules = 64;

    internal static string? NormalizeExtension(string? value)
    {
        var extension = value?.Trim().TrimStart('*', '.').ToLowerInvariant();
        return string.IsNullOrEmpty(extension) || extension.Length > 64 ||
            extension.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-' or '+'))
            ? null : "." + extension;
    }

    internal static IReadOnlyDictionary<string, string> Normalize(IReadOnlyDictionary<string, string>? rules) =>
        (rules ?? new Dictionary<string, string>()).Take(MaximumRules)
        .Where(pair => NormalizeExtension(pair.Key) is not null &&
            !string.IsNullOrWhiteSpace(pair.Value) && DesktopConfigRepair.IsValidEditorPath(pair.Value.Trim()))
        .GroupBy(pair => NormalizeExtension(pair.Key)!, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.Last().Value.Trim(), StringComparer.OrdinalIgnoreCase);

    internal static string? Resolve(DesktopUpdatePreferences preferences, string fileName) =>
        Normalize(preferences.FileTypeEditors).OrderByDescending(pair => pair.Key.Length)
            .FirstOrDefault(pair => fileName.EndsWith(pair.Key, StringComparison.OrdinalIgnoreCase)).Value
        ?? preferences.ExternalEditorPath;
}
