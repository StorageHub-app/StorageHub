using System.Runtime.Versioning;

// statx and the 0600/0700 rules are Linux as StorageHub uses them: macOS has statx only through a
// different path and answers peer identity another way entirely. The counterpart of
// Infrastructure.Windows, which says the same thing about DPAPI and DACLs.
[assembly: SupportedOSPlatform("linux")]
