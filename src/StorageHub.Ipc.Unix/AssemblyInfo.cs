using System.Runtime.Versioning;

// SO_PEERCRED and statx are Linux interfaces, not POSIX ones: macOS answers the same questions
// through LOCAL_PEERCRED and a different struct. Marking the assembly rather than each type means
// CA1416 reports a caller that reaches one of them without a guard, and makes adding macOS a
// deliberate act rather than something that appears to work until it is tried.
[assembly: SupportedOSPlatform("linux")]
