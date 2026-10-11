using System.Globalization;
using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop;

/// <summary>
/// Unix permission bits as people write them: <c>rwxr-xr-x</c> and octal <c>0755</c>.
/// </summary>
/// <remarks>
/// The same twelve bits and the same letters as <c>ls -l</c> and CL.Storage's UnixPermissions, kept
/// here because the desktop does not reference the storage library. The special bits share the
/// execute column, as ls shows them: <c>s</c> where set-ID and execute are both set, <c>S</c> where
/// only set-ID is, and <c>t</c>/<c>T</c> likewise for sticky.
/// </remarks>
internal static class UnixPermissionText
{
    internal const int SetUserId = 0x800;
    internal const int SetGroupId = 0x400;
    internal const int Sticky = 0x200;

    /// <summary>Nine characters, owner, group and others, read-write-execute each.</summary>
    internal static string Format(int mode)
    {
        Span<char> text = stackalloc char[9];
        for (var who = 0; who < 3; who++)
        {
            var shift = 6 - who * 3;
            text[who * 3] = (mode & (4 << shift)) != 0 ? 'r' : '-';
            text[who * 3 + 1] = (mode & (2 << shift)) != 0 ? 'w' : '-';
            var execute = (mode & (1 << shift)) != 0;
            var special = who switch
            {
                0 => (mode & SetUserId) != 0,
                1 => (mode & SetGroupId) != 0,
                _ => (mode & Sticky) != 0
            };
            var letter = who == 2 ? 't' : 's';
            text[who * 3 + 2] = special
                ? execute ? letter : char.ToUpperInvariant(letter)
                : execute ? 'x' : '-';
        }

        return new string(text);
    }

    /// <summary>Four octal digits, as <c>0755</c> or <c>4755</c>.</summary>
    internal static string ToOctal(int mode) =>
        Convert.ToString(mode & ItemAttributeIpcLimits.MaximumUnixMode, 8)
            .PadLeft(4, '0');

    /// <summary>Both, as the Properties window shows a mode: <c>rwxr-xr-x (0755)</c>.</summary>
    internal static string Describe(int mode) =>
        string.Create(CultureInfo.InvariantCulture, $"{Format(mode)} ({ToOctal(mode)})");

    /// <summary>
    /// One to four octal digits, as typed: <c>755</c>, <c>0644</c> or <c>4755</c>.
    /// </summary>
    internal static bool TryParseOctal(string? text, out int mode)
    {
        mode = 0;
        var digits = text?.Trim();
        if (string.IsNullOrEmpty(digits) || digits.Length > 4) return false;

        foreach (var digit in digits)
        {
            if (digit is < '0' or > '7') return false;
            mode = mode * 8 + (digit - '0');
        }

        return true;
    }
}
