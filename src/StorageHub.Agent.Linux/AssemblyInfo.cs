using System.Runtime.Versioning;

// XDG paths, systemd user units and loginctl lingering. The path handling would work on macOS; the
// unit management would not, and neither would the SO_PEERCRED this depends on through
// StorageHub.Ipc.Unix. Declaring linux rather than a vague "not Windows" makes adding macOS a
// deliberate act instead of something that appears to work until it is tried.
[assembly: SupportedOSPlatform("linux")]
