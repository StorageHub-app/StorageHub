using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop;

/// <summary>
/// The icons a connection or a folder can be given, and how a stored key maps back to one.
///
/// Icons are stored as a short text key rather than as an enum value, because the key already
/// travels through the profile, the database and the IPC contract as <c>IconKey</c>. Keeping it
/// text means a key written by a newer build -- or one naming an icon this build does not have --
/// degrades to the provider's default instead of failing to load the profile.
///
/// It sat in the WinForms shell over one line: a FolderIconRequest at the bottom of the same file
/// carrying a System.Drawing.Point. Nothing in the catalog itself draws anything -- it maps a
/// stored key to a UiGlyph, and both shells need the same map.
/// </summary>
internal static class ConnectionIconCatalog
{
    /// <summary>
    /// The icons offered in the picker, in the order they are shown. Deliberately a curated subset
    /// of <see cref="UiGlyph"/>: the full set includes toolbar verbs like Cut and Paste, which mean
    /// nothing as a label for a connection.
    /// </summary>
    internal static readonly IReadOnlyList<(string Key, UiGlyph Glyph)> Choices =
    [
        ("cloud", UiGlyph.Cloud),
        ("server", UiGlyph.Server),
        ("folder", UiGlyph.Folder),
        ("file", UiGlyph.File),
        ("terminal", UiGlyph.Terminal),
        ("key", UiGlyph.Key),
        ("shield", UiGlyph.Shield),
        ("lock", UiGlyph.Lock),
        ("link", UiGlyph.Link),
        ("layers", UiGlyph.Layers),
        ("favorite", UiGlyph.Favorite),
        ("home", UiGlyph.Home),
        ("download", UiGlyph.Download),
        ("upload", UiGlyph.Upload),
        ("queue", UiGlyph.Queue),
        ("schedule", UiGlyph.Schedule),
        ("history", UiGlyph.History),
        ("profiles", UiGlyph.Profiles),
        ("settings", UiGlyph.Settings),
        ("speed", UiGlyph.Speed),
        ("checksum", UiGlyph.Checksum),
        ("tree", UiGlyph.Tree),
        ("log", UiGlyph.Log),
        ("info", UiGlyph.Info),
        ("warning", UiGlyph.Warning),
        ("bug", UiGlyph.Bug),
        ("connect", UiGlyph.Connect),
        ("search", UiGlyph.Search)
    ];

    private static readonly Dictionary<string, UiGlyph> ByKey =
        Choices.ToDictionary(choice => choice.Key, choice => choice.Glyph, StringComparer.OrdinalIgnoreCase);

    /// <summary>The glyph a stored key names, or null when it names nothing this build knows.</summary>
    internal static UiGlyph? Resolve(string? iconKey) =>
        !string.IsNullOrWhiteSpace(iconKey) && ByKey.TryGetValue(iconKey.Trim(), out var glyph)
            ? glyph
            : null;

    /// <summary>
    /// The icon to draw for a connection: the one it was given, else one inferred from its
    /// provider. The fallback is what keeps every existing profile looking right without anyone
    /// having to choose an icon for it.
    /// </summary>
    internal static UiGlyph ResolveForConnection(string? iconKey, StorageProviderKind provider, ConnectionProfileType type) =>
        Resolve(iconKey) ?? DefaultForProvider(provider, type);

    private static UiGlyph DefaultForProvider(StorageProviderKind provider, ConnectionProfileType type)
    {
        if (type == ConnectionProfileType.Client)
        {
            return UiGlyph.Terminal;
        }

        return provider switch
        {
            StorageProviderKind.Local => UiGlyph.Home,
            StorageProviderKind.Sftp or StorageProviderKind.Ftp or StorageProviderKind.Ftps => UiGlyph.Server,
            StorageProviderKind.Ssh => UiGlyph.Terminal,
            _ => UiGlyph.Cloud
        };
    }

    /// <summary>The key for a glyph, for writing a picker's choice back to the profile.</summary>
    internal static string? KeyFor(UiGlyph glyph)
    {
        foreach (var (key, candidate) in Choices)
        {
            if (candidate == glyph)
            {
                return key;
            }
        }

        return null;
    }
}
