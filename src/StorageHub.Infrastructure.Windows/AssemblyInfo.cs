using System.Runtime.Versioning;

// DPAPI through Crypt32, and files restricted with a Windows DACL. The -windows target framework
// this replaces existed only to reach those, but they live in the portable reference pack; what it
// actually bought was an implicit platform declaration. Saying it explicitly keeps the guarantee and
// lets CA1416 report an unguarded caller, which a target framework never did.
[assembly: SupportedOSPlatform("windows")]
