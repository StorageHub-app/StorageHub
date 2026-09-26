using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace StorageHub.Desktop.Services;

/// <summary>
/// The icons Windows gives files, folders and drives, as Avalonia bitmaps, cached.
/// </summary>
/// <remarks>
/// <para>
/// 1.x's WindowsShellIconProvider, without System.Drawing: the icon comes from SHGetFileInfo and
/// its pixels are read straight out of the icon's bitmap into a WriteableBitmap.
/// </para>
/// <para>
/// Cached by extension, as 1.x did, except for local folders and drives, which Windows draws per
/// item (a drive's type, a folder with its own desktop.ini). A remote name is only ever described
/// to the shell by its extension, with USEFILEATTRIBUTES, so Windows never tries to open a
/// provider path. The large (32 px) icon is asked for and drawn at 16, which stays sharp at 125%
/// and 150% where the small one is stretched.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class WindowsShellIcons
{
    private const uint ShgfiIcon = 0x100;
    private const uint ShgfiLargeIcon = 0x0;
    private const uint ShgfiUseFileAttributes = 0x10;
    private const uint FileAttributeNormal = 0x80;
    private const uint FileAttributeDirectory = 0x10;

    /// <summary>Keys that failed, so a type Windows has nothing for is asked about once.</summary>
    private static readonly Dictionary<string, IImage?> Cache = new(StringComparer.OrdinalIgnoreCase);

    internal static IImage? For(BrowserListItem item)
    {
        var local = Themes.FileIcons.IsLocal(item);
        var perItem = local && item.IsContainer;
        var key = perItem ? "item:" + item.Location
            : item.IsContainer ? "folder"
            : "ext:" + Path.GetExtension(item.Name);

        if (Cache.TryGetValue(key, out var cached)) return cached;

        var icon = Load(
            perItem ? item.Location! : item.IsContainer ? "folder" : "file" + Path.GetExtension(item.Name),
            item.IsContainer ? FileAttributeDirectory : FileAttributeNormal,
            useAttributes: !perItem);
        Cache[key] = icon;
        return icon;
    }

    private static WriteableBitmap? Load(string path, uint attributes, bool useAttributes)
    {
        var flags = ShgfiIcon | ShgfiLargeIcon | (useAttributes ? ShgfiUseFileAttributes : 0);
        if (SHGetFileInfo(path, attributes, out var info, (uint)Marshal.SizeOf<ShFileInfo>(), flags) == IntPtr.Zero ||
            info.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return ToBitmap(info.hIcon);
        }
        finally
        {
            _ = DestroyIcon(info.hIcon);
        }
    }

    /// <summary>The icon's colour bitmap as premultiplied-free BGRA, with its mask applied if it has no alpha.</summary>
    private static WriteableBitmap? ToBitmap(IntPtr icon)
    {
        if (!GetIconInfo(icon, out var iconInfo)) return null;
        try
        {
            if (iconInfo.hbmColor == IntPtr.Zero) return null;
            if (GetObject(iconInfo.hbmColor, Marshal.SizeOf<BitmapHeader>(), out var header) == 0) return null;

            var width = header.bmWidth;
            var height = header.bmHeight;
            var pixels = ReadBits(iconInfo.hbmColor, width, height);
            if (pixels is null) return null;

            // Old-style icons carry no alpha and draw their shape through the mask instead.
            if (!HasAlpha(pixels))
            {
                var mask = iconInfo.hbmMask != IntPtr.Zero ? ReadBits(iconInfo.hbmMask, width, height) : null;
                for (var index = 0; index < pixels.Length; index += 4)
                {
                    pixels[index + 3] = mask is not null && mask[index] != 0 ? (byte)0 : (byte)255;
                }
            }

            var bitmap = new WriteableBitmap(
                new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using var buffer = bitmap.Lock();
            for (var row = 0; row < height; row++)
            {
                Marshal.Copy(pixels, row * width * 4, buffer.Address + row * buffer.RowBytes, width * 4);
            }

            return bitmap;
        }
        finally
        {
            if (iconInfo.hbmColor != IntPtr.Zero) _ = DeleteObject(iconInfo.hbmColor);
            if (iconInfo.hbmMask != IntPtr.Zero) _ = DeleteObject(iconInfo.hbmMask);
        }
    }

    private static byte[]? ReadBits(IntPtr bitmap, int width, int height)
    {
        var info = new BitmapInfoHeader
        {
            biSize = Marshal.SizeOf<BitmapInfoHeader>(),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0
        };
        var pixels = new byte[width * height * 4];
        var screen = GetDC(IntPtr.Zero);
        try
        {
            return GetDIBits(screen, bitmap, 0, (uint)height, pixels, ref info, 0) == 0 ? null : pixels;
        }
        finally
        {
            _ = ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private static bool HasAlpha(byte[] pixels)
    {
        for (var index = 3; index < pixels.Length; index += 4)
        {
            if (pixels[index] != 0) return true;
        }

        return false;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapHeader
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, out ShFileInfo info, uint size, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr handle, int size, out BitmapHeader header);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BitmapInfoHeader info, uint usage);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);
}
