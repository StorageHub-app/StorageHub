namespace StorageHub.Storage.CodeLogic;

/// <summary>
/// What an open connection says about itself and the server behind it: where it goes, how it is
/// secured, what the server presented and runs, what was negotiated, and how its sessions are
/// doing. It never carries a secret.
/// </summary>
public sealed record StorageConnectionDetails(
    string Provider,
    string? Host,
    int? Port,
    string TransportSecurity,
    string? ServerSystem,
    string? ServerSoftware,
    IReadOnlyList<string> ServerFeatures,
    IReadOnlyList<KeyValuePair<string, string>> Negotiated,
    StoragePresentedIdentity? PresentedIdentity,
    StorageSessionCounts? Sessions,
    StorageHealthCheck? LastHealthCheck);

/// <summary>The host key or certificate the server presented, and whether it was trusted.</summary>
public sealed record StoragePresentedIdentity(
    string Kind,
    string Fingerprint,
    string? Algorithm,
    string? Subject,
    string? Issuer,
    DateTimeOffset? NotAfter,
    bool Trusted);

public sealed record StorageSessionCounts(int Idle, int InUse, int Opened, int Closed, int FailedProbes);

public sealed record StorageHealthCheck(bool Healthy, string? ErrorCode, TimeSpan Latency, DateTimeOffset CheckedUtc);
