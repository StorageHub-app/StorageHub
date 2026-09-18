using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("StorageHub.Storage.CodeLogic.Tests")]

// The scheduled-sync end-to-end test registers live provider sessions directly so the scheduler
// and outbox can be proven against a real server rather than a double.
[assembly: InternalsVisibleTo("StorageHub.Agent.IntegrationTests")]
