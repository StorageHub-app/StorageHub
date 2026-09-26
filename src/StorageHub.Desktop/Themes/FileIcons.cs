using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Lucide.Avalonia;
using StorageHub.Desktop.Services;

namespace StorageHub.Desktop.Themes;

/// <summary>
/// The icon beside a name in a pane: the system's own where there is one, a glyph where there is not.
/// </summary>
/// <remarks>
/// <para>
/// 1.x showed Windows shell icons -- the folder, the drive, the icon Windows gives each file
/// type -- and 2.0's Name column was text alone. On Windows these are the shell's again
/// (<see cref="WindowsShellIcons"/>). On Linux, and anywhere the shell has nothing to give, a glyph
/// chosen from the extension stands in, so a folder still looks like a folder and a picture like a
/// picture.
/// </para>
/// <para>
/// A row's <see cref="BrowserListItem.Location"/> says which kind it is: a local row carries a
/// full path, a remote one a path relative to its connection. Only a local row is ever looked up
/// by path; a remote name is described to the shell by its extension alone, so Windows never
/// tries to reach a provider.
/// </para>
/// </remarks>
internal static class FileIcons
{
    private static readonly Dictionary<string, LucideIconKind> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = LucideIconKind.FileImage, [".jpg"] = LucideIconKind.FileImage,
        [".jpeg"] = LucideIconKind.FileImage, [".gif"] = LucideIconKind.FileImage,
        [".bmp"] = LucideIconKind.FileImage, [".webp"] = LucideIconKind.FileImage,
        [".svg"] = LucideIconKind.FileImage, [".tif"] = LucideIconKind.FileImage,
        [".tiff"] = LucideIconKind.FileImage, [".exr"] = LucideIconKind.FileImage,
        [".psd"] = LucideIconKind.FileImage, [".ico"] = LucideIconKind.FileImage,
        [".zip"] = LucideIconKind.FileArchive, [".7z"] = LucideIconKind.FileArchive,
        [".rar"] = LucideIconKind.FileArchive, [".tar"] = LucideIconKind.FileArchive,
        [".gz"] = LucideIconKind.FileArchive, [".tgz"] = LucideIconKind.FileArchive,
        [".bz2"] = LucideIconKind.FileArchive, [".xz"] = LucideIconKind.FileArchive,
        [".mp3"] = LucideIconKind.FileMusic, [".wav"] = LucideIconKind.FileMusic,
        [".flac"] = LucideIconKind.FileMusic, [".ogg"] = LucideIconKind.FileMusic,
        [".m4a"] = LucideIconKind.FileMusic, [".aac"] = LucideIconKind.FileMusic,
        [".mp4"] = LucideIconKind.FileVideoCamera, [".mov"] = LucideIconKind.FileVideoCamera,
        [".mkv"] = LucideIconKind.FileVideoCamera, [".avi"] = LucideIconKind.FileVideoCamera,
        [".webm"] = LucideIconKind.FileVideoCamera, [".wmv"] = LucideIconKind.FileVideoCamera,
        [".cs"] = LucideIconKind.FileCode, [".js"] = LucideIconKind.FileCode,
        [".ts"] = LucideIconKind.FileCode, [".py"] = LucideIconKind.FileCode,
        [".java"] = LucideIconKind.FileCode, [".c"] = LucideIconKind.FileCode,
        [".cpp"] = LucideIconKind.FileCode, [".h"] = LucideIconKind.FileCode,
        [".go"] = LucideIconKind.FileCode, [".rs"] = LucideIconKind.FileCode,
        [".html"] = LucideIconKind.FileCode, [".css"] = LucideIconKind.FileCode,
        [".json"] = LucideIconKind.FileCode, [".xml"] = LucideIconKind.FileCode,
        [".yaml"] = LucideIconKind.FileCode, [".yml"] = LucideIconKind.FileCode,
        [".axaml"] = LucideIconKind.FileCode, [".xaml"] = LucideIconKind.FileCode,
        [".sh"] = LucideIconKind.FileTerminal, [".ps1"] = LucideIconKind.FileTerminal,
        [".bat"] = LucideIconKind.FileTerminal, [".cmd"] = LucideIconKind.FileTerminal,
        [".exe"] = LucideIconKind.FileCog, [".dll"] = LucideIconKind.FileCog,
        [".msi"] = LucideIconKind.FileCog, [".so"] = LucideIconKind.FileCog,
        [".deb"] = LucideIconKind.FileCog,
        [".csv"] = LucideIconKind.FileSpreadsheet, [".xls"] = LucideIconKind.FileSpreadsheet,
        [".xlsx"] = LucideIconKind.FileSpreadsheet, [".ods"] = LucideIconKind.FileSpreadsheet,
        [".ppt"] = LucideIconKind.Presentation, [".pptx"] = LucideIconKind.Presentation,
        [".odp"] = LucideIconKind.Presentation,
        [".txt"] = LucideIconKind.FileText, [".md"] = LucideIconKind.FileText,
        [".log"] = LucideIconKind.FileText, [".pdf"] = LucideIconKind.FileText,
        [".doc"] = LucideIconKind.FileText, [".docx"] = LucideIconKind.FileText,
        [".rtf"] = LucideIconKind.FileText, [".odt"] = LucideIconKind.FileText,
        [".key"] = LucideIconKind.FileKey, [".pem"] = LucideIconKind.FileKey,
        [".pfx"] = LucideIconKind.FileKey, [".crt"] = LucideIconKind.FileKey,
        [".cer"] = LucideIconKind.FileKey, [".ppk"] = LucideIconKind.FileKey,
        [".db"] = LucideIconKind.Database, [".sqlite"] = LucideIconKind.Database,
        [".sql"] = LucideIconKind.Database,
    };

    /// <summary>Whether a row is one of This PC's drives: a local root such as C:\ or /.</summary>
    internal static bool IsDrive(BrowserListItem item) =>
        item.IsContainer && item.Location is { Length: > 0 } location &&
        Path.IsPathFullyQualified(location) &&
        string.Equals(Path.GetPathRoot(location), location, StringComparison.OrdinalIgnoreCase);

    internal static bool IsLocal(BrowserListItem item) =>
        item.Location is { Length: > 0 } location && Path.IsPathFullyQualified(location);

    /// <summary>The glyph for a row, when the system has no icon to give.</summary>
    internal static LucideIconKind Glyph(BrowserListItem item) =>
        item.IsParentNavigation ? LucideIconKind.FolderUp
        : IsDrive(item) ? LucideIconKind.HardDrive
        : item.IsContainer ? LucideIconKind.Folder
        : ByExtension.TryGetValue(Path.GetExtension(item.Name), out var kind) ? kind
        : LucideIconKind.File;

    /// <summary>The system's icon for a row, or null to fall back to the glyph.</summary>
    internal static IImage? SystemIcon(BrowserListItem item) =>
        OperatingSystem.IsWindows() && !item.IsParentNavigation ? WindowsShellIcons.For(item) : null;
}

/// <summary>A row's system icon, for an Image in the Name column. Null when there is none.</summary>
internal sealed class FileSystemIconConverter : IValueConverter
{
    public static FileSystemIconConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is BrowserListItem item ? FileIcons.SystemIcon(item) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A row's fallback glyph, for a LucideIcon in the Name column.</summary>
internal sealed class FileGlyphConverter : IValueConverter
{
    public static FileGlyphConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is BrowserListItem item ? FileIcons.Glyph(item) : LucideIconKind.File;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Whether a row is a folder, a drive or "..", which the glyph draws in the folder colour.</summary>
internal sealed class IsContainerRowConverter : IValueConverter
{
    public static IsContainerRowConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is BrowserListItem { IsContainer: true } or BrowserListItem { IsParentNavigation: true };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Whether a row has no system icon, so the glyph shows in its place.</summary>
internal sealed class HasNoSystemIconConverter : IValueConverter
{
    public static HasNoSystemIconConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not BrowserListItem item || FileIcons.SystemIcon(item) is null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
