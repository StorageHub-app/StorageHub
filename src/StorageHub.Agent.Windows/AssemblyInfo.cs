using System.Runtime.Versioning;

// The agent's Windows host: the service control manager, DPAPI vault composition, and the Explorer
// drop broker's server half. None of it needed a -windows target framework - WinForms was the only
// thing that ever did - so the declaration moves here where CA1416 can act on it.
[assembly: SupportedOSPlatform("windows")]
