using System.Buffers;
using System.Text.RegularExpressions;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// Whether a name may be given to a file or folder, and why not when it may not.
/// </summary>
/// <remarks>
/// Lifted out of PaneItemDialogs, where it sat above the two forms that call it. It has never
/// needed a window: it is twenty lines of rules about text, and the rename and new-folder dialogs
/// are two of several places that should be asking them.
///
/// The Windows reserved names are checked on every platform on purpose. A remote share can be
/// browsed from Linux and served from Windows, and a name that cannot be created there is worth
/// refusing before the transfer rather than after it.
/// </remarks>
internal static partial class PaneItemNameRules
{
    private const int MaximumNameLength = 255;
    private static readonly SearchValues<char> InvalidCharacters = SearchValues.Create(['<', '>', ':', '"', '/', '\\', '|', '?', '*']);

    internal static string? Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Ui.Validation.EnterAName;
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal) || value.EndsWith('.'))
            return Ui.Validation.NamesCannotBeginOrEndWithSpaces;
        if (value.Length > MaximumNameLength)
            return Ui.Format(Ui.Validation.NamesCannotExceedCharactersFormat, MaximumNameLength);
        if (value is "." or ".." || value.Any(char.IsControl) || value.AsSpan().ContainsAny(InvalidCharacters))
            return Ui.Validation.TheNameContainsCharactersThatAreNot;
        var stem = value.Split('.')[0];
        if (ReservedWindowsName().IsMatch(stem)) return Ui.Validation.ThatNameIsReservedByWindows;
        return null;
    }

    [GeneratedRegex("^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReservedWindowsName();
}
