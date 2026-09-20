using Avalonia.Media;

namespace StorageHub.Desktop.Shell;

/// <summary>
/// The icon a listing row draws, when the system has a better one than the generic glyph.
/// </summary>
/// <remarks>
/// <para>
/// On Windows this is the file association icon -- a .docx looks like Word whether or not the
/// person has Word. On Linux it is the icon theme's MIME icon. Either way the answer depends on
/// the machine, so it cannot be a token and cannot be part of the catalog.
/// </para>
/// <para>
/// Returning null is normal and means "use the glyph for this kind". Every implementation must do
/// that rather than invent a placeholder, because a wrong icon reads as a wrong file type.
/// </para>
/// <para>
/// <b>Keyed on the extension, not the row.</b> A listing is virtualized over SQLite and a filter
/// keystroke re-queries it, so one lookup per row would page the whole table in per keystroke. The
/// Windows implementation this replaces already cached by extension for that reason, and the
/// contract keeps it: <paramref name="path"/> is read for its extension and for nothing else when
/// <paramref name="isLocal"/> is false.
/// </para>
/// </remarks>
internal interface IFileIconProvider
{
    /// <summary>
    /// The icon for a path, or null to fall back to the kind's glyph.
    /// </summary>
    /// <param name="path">
    /// A local path, or a remote name. Never touched on disk when <paramref name="isLocal"/> is
    /// false: a provider path may be on a network the shell must not block on.
    /// </param>
    /// <param name="isContainer">Whether the row is a folder.</param>
    /// <param name="isLocal">Whether <paramref name="path"/> names something on this machine.</param>
    IImage? Resolve(string path, bool isContainer, bool isLocal);
}
