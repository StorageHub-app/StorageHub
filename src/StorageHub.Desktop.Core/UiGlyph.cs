namespace StorageHub.Desktop;

/// <summary>
/// The vector glyphs StorageHub draws for menus, toolbars, cards, and list rows. Every glyph is
/// authored on a 24x24 grid and stroked at render time, so one enum entry serves every size and
/// DPI without shipping bitmaps.
/// </summary>
public enum UiGlyph
{
    Add,
    Connections,
    Back,
    Forward,
    Up,
    Refresh,
    Compare,
    Run,
    Pause,
    Search,
    Folder,
    File,
    Save,
    Delete,
    Test,
    Terminal,
    Lock,
    Warning,
    More,
    Home,
    Settings,
    Info,
    Cut,
    Copy,
    Paste,
    Rename,
    SelectAll,
    Invert,
    Properties,
    Exit,
    Tree,
    Queue,
    Log,
    Hidden,
    Theme,
    Connect,
    Disconnect,
    Stop,
    Speed,
    Profiles,
    Schedule,
    History,
    Favorite,
    Key,
    Shield,
    Cloud,
    Server,
    Download,
    Upload,
    Keyboard,
    Documentation,
    Bug,
    Checksum,
    Diagnostics,
    Close,
    Link,
    Layers
}

/// <summary>
/// Which palette role an icon takes. Tracked icons resolve the tone again after an appearance
/// change, which is what keeps a glyph legible when the palette flips underneath it.
/// </summary>
public enum UiIconTone
{
    Text,
    Muted,
    Primary,
    OnPrimary,
    Success,
    Warning,
    Danger
}
