using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// Turns an agent sync failure into something the operator can act on.
///
/// The agent deliberately replaces every failure message with generic category text so a response
/// can never leak endpoint detail, but it does preserve an exact code. Both sync surfaces used to
/// throw that code away and show the category alone, so a profile somebody had simply left
/// disabled was reported as "the sync service or an endpoint is temporarily unavailable" -- not
/// true, not transient, and no hint of the one-click fix.
///
/// Known codes get their own sentence. Anything else keeps the agent's wording with the code
/// appended, so an unfamiliar failure is still reportable rather than anonymous.
/// </summary>
internal static class SyncFailureMessages
{
    internal static string Describe(StorageIpcFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var known = failure.Code switch
        {
            "sync.profile.disabled" => Ui.Sync.ProfileDisabledForSchedule,
            "sync.approval.profile_disabled" => Ui.Sync.ApprovalBlockedByDisabledProfile,
            "sync.profile.not_found" => Ui.Sync.ProfileNotFound,
            "storage.profile.not_found" => Ui.Sync.ProfileNotFound,
            "storage.profile.disabled" => Ui.Sync.ConnectionMissing,
            "storage.connection.unavailable" => Ui.Sync.EndpointUnreachable,
            "storage.profile.store_unavailable" => Ui.Sync.EndpointUnreachable,
            "sync.scan.root_not_found" => Ui.Sync.LocationMissing,
            _ => null
        };
        if (known is not null)
        {
            return known;
        }

        return failure.Category == StorageIpcFailureCategory.Unauthorized
            ? Ui.Sync.EndpointUnauthorized
            : Ui.Format(Ui.Sync.AgentFailureWithCodeFormat, failure.Message, failure.Code);
    }
}
