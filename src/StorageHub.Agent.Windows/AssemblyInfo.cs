using System.Runtime.Versioning;

// The agent's Windows host: the service control manager and DPAPI vault composition. None of it
// needed a -windows target framework - WinForms was the only thing that ever did - so the
// declaration moves here where CA1416 can act on it. The Explorer drop broker's server half was
// here too, and moved to the agent host, since its import review serves Linux drops as well.
[assembly: SupportedOSPlatform("windows")]
