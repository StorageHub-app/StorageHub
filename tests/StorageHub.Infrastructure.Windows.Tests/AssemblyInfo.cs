using System.Runtime.Versioning;

// Every case in this suite drives a Windows interface - the service control manager, DPAPI, a pipe
// ACL, a restricted file. The project used to say so with a -windows target framework, which meant
// it simply did not build elsewhere. Now it builds everywhere and its cases skip, so one
// solution-wide `dotnet test` gives an honest result on both platforms.
[assembly: SupportedOSPlatform("windows")]
