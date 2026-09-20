using System.Runtime.Versioning;

// Every type here is named-pipe security: ACLs, SIDs, and the owner check that stands in for
// CurrentUserOnly on a service pipe. Marking the assembly rather than each type means CA1416
// reports a caller that reaches one of them without a Windows guard, which is the enforcement the
// port relies on now that no project carries a -windows target framework.
[assembly: SupportedOSPlatform("windows")]
