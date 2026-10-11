using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>What a connection card says about its link, and in which colour.</summary>
internal enum ConnectionLinkTone
{
    Warning,
    Danger,
    Success
}

/// <summary>The line a card shows under its subtitle while its link is, or just was, in trouble.</summary>
internal sealed record ConnectionLinkBadge(string Text, string Since, ConnectionLinkTone Tone);

/// <summary>
/// How the agent's link changes read on a connection's card and in the activity log.
/// </summary>
/// <remarks>
/// A card shows the latest change for as long as it stands -- retrying and lost until the
/// connection works again -- and "connected again" only for a while after, so the panel goes back
/// to how it looked before the outage on its own rather than carrying a green line forever. The
/// log keeps every change, so nothing is lost by the card forgetting.
/// </remarks>
internal static class ConnectionLinkPresentation
{
    /// <summary>How long a card says "connected again" after an outage.</summary>
    internal static readonly TimeSpan BackShownFor = TimeSpan.FromMinutes(2);

    internal const string ActivityKeyPrefix = "link:";

    internal static ConnectionLinkBadge? Badge(ConnectionLinkEvent? latest, DateTimeOffset now)
    {
        if (latest is null) return null;
        var since = Ui.Format(Ui.Connections.LinkSinceFormat, latest.OccurredUtc.ToLocalTime());
        return latest.Kind switch
        {
            ConnectionLinkEventKind.Retrying => new ConnectionLinkBadge(
                latest.Attempt is { } attempt
                    ? Ui.Format(Ui.Connections.LinkRetryingAttemptFormat, attempt)
                    : Ui.Connections.LinkRetrying,
                since,
                ConnectionLinkTone.Warning),
            ConnectionLinkEventKind.Lost => new ConnectionLinkBadge(Ui.Connections.LinkLost, since, ConnectionLinkTone.Danger),
            ConnectionLinkEventKind.Back when now - latest.OccurredUtc < BackShownFor =>
                new ConnectionLinkBadge(Ui.Connections.LinkBack, since, ConnectionLinkTone.Success),
            _ => null
        };
    }

    /// <summary>
    /// One line of the activity log. Keyed by the journal's sequence and the time together,
    /// because the sequence starts again when the agent does.
    /// </summary>
    internal static ActivityEntry ToActivity(ConnectionLinkEvent change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return new ActivityEntry(
            $"{ActivityKeyPrefix}{change.Sequence}:{change.OccurredUtc.UtcTicks}",
            change.OccurredUtc,
            Ui.Transfer.ActivityAreaConnection,
            change.DisplayName,
            change.Kind switch
            {
                ConnectionLinkEventKind.Retrying => Ui.Transfer.ActivityLinkRetrying,
                ConnectionLinkEventKind.Lost => Ui.Transfer.ActivityLinkLost,
                _ => Ui.Transfer.ActivityLinkBack
            },
            change.Kind switch
            {
                ConnectionLinkEventKind.Retrying when change.Attempt is { } attempt =>
                    Ui.Format(Ui.Transfer.ActivityLinkRetryingFormat, attempt),
                ConnectionLinkEventKind.Retrying => Ui.Transfer.ActivityLinkRetryingDetails,

                // The library's code says which way it was lost -- refused, dropped, timed out --
                // in the words the pane's banner uses for the same failure.
                ConnectionLinkEventKind.Lost when change.ErrorCode is { } code =>
                    RemoteBrowserErrors.ForFailure(new StorageIpcFailure(
                        code,
                        StorageIpcFailureCategory.Unavailable,
                        Ui.Transfer.ActivityLinkLostDetails,
                        IsTransient: true)),
                ConnectionLinkEventKind.Lost => Ui.Transfer.ActivityLinkLostDetails,
                _ => Ui.Transfer.ActivityLinkBackDetails
            });
    }
}
