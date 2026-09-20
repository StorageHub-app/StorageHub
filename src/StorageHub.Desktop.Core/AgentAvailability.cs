using System.Text.Json;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>What the desktop currently believes about the background agent.</summary>
public enum AgentAvailability
{
    /// <summary>Nothing has been asked of the agent yet.</summary>
    Unknown = 0,

    /// <summary>The agent answered the last thing asked of it.</summary>
    Online = 1,

    /// <summary>A call failed on the transport and a probe is in flight.</summary>
    Reconnecting = 2,

    /// <summary>The agent is not answering, and the last probe agreed.</summary>
    Offline = 3,
}

public sealed class AgentAvailabilityChangedEventArgs(AgentAvailability availability, bool recovered) : EventArgs
{
    public AgentAvailability Availability { get; } = availability;

    /// <summary>
    /// Set on the change that ends an outage, which is the signal a surface waits for to reload
    /// whatever it could not load while the agent was away.
    /// </summary>
    public bool Recovered { get; } = recovered;
}

/// <summary>
/// The one place that decides what a failed agent call means, what the person is told about it,
/// and when to try again.
/// </summary>
/// <remarks>
/// Before this existed, every surface handled its own agent failure, which produced three separate
/// problems at once and all of them visible in the same window: the overview printed
/// <c>exception.Message</c> and so showed "Overview unavailable: Pipe is broken.", the connections
/// panel printed a sentence of its own, and the status bar still read "Agent: connected" because
/// the status monitor polls on a fresh connection every eight seconds and never sees the broken
/// one. Three surfaces, three answers, two of them wrong.
///
/// The rules are now here. A transport fault means the agent went away -- almost always because it
/// was restarted, which leaves every persistent pipe in the process broken. The desktop says so
/// once, in its own words, marks itself reconnecting, and asks the prober to look again straight
/// away rather than waiting out the poll interval. When the agent answers, <see cref="Changed"/>
/// is raised with <see cref="AgentAvailabilityChangedEventArgs.Recovered"/> set, and each surface
/// reloads itself instead of leaving somebody looking at a stale error.
/// </remarks>
public static class DesktopAgentAvailability
{
    /// <summary>
    /// How close together two probes may be. A burst of failed calls -- which is what a restarted
    /// agent produces, one per surface -- must not become a burst of probes.
    /// </summary>
    private static readonly TimeSpan MinimumProbeInterval = TimeSpan.FromSeconds(2);

    private static readonly Lock Gate = new();
    private static Func<CancellationToken, Task<bool>>? _probe;
    private static DateTimeOffset _lastProbeStartedUtc = DateTimeOffset.MinValue;
    private static bool _probeInFlight;
    private static AgentAvailability _current = AgentAvailability.Unknown;

    /// <summary>Raised whenever the belief changes. Never raised for an unchanged state.</summary>
    public static event EventHandler<AgentAvailabilityChangedEventArgs>? Changed;

    public static AgentAvailability Current
    {
        get
        {
            lock (Gate)
            {
                return _current;
            }
        }
    }

    /// <summary>
    /// Registers what to call to find out whether the agent is back. The status monitor owns the
    /// one connection attempt worth making, so this is its poll rather than a second prober with
    /// its own idea of timeouts.
    /// </summary>
    public static void UseProbe(Func<CancellationToken, Task<bool>>? probe)
    {
        lock (Gate)
        {
            _probe = probe;
        }
    }

    /// <summary>
    /// Whether this exception means the agent could not be reached, as opposed to the agent
    /// answering and refusing.
    /// </summary>
    /// <remarks>
    /// A broken pipe is the common one and the reason this type exists: restarting the agent
    /// leaves every pipe a long-lived client holds unusable, and the next call on one throws
    /// <see cref="IOException"/> with the text "Pipe is broken." An unreadable or out-of-order
    /// envelope counts too -- the pipe is there but no longer carrying a conversation this
    /// process can follow, and reconnecting is the answer to both.
    /// </remarks>
    public static bool IsTransportFault(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error is IOException
            or TimeoutException
            or ObjectDisposedException
            or UnauthorizedAccessException
            or InvalidDataException
            or JsonException
            or InvalidOperationException;
    }

    /// <summary>
    /// What to show somebody when a call to the agent failed.
    /// </summary>
    /// <remarks>
    /// Never the exception's own message. "Pipe is broken." is accurate, untranslated, and of no
    /// use to anyone who did not write the IPC layer; what a person needs to know is that the
    /// background work is briefly unavailable and that StorageHub is dealing with it.
    /// </remarks>
    public static string Describe(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (!IsTransportFault(error))
        {
            return Ui.Shell.AgentRequestFailed;
        }

        return Current == AgentAvailability.Offline
            ? Ui.Shell.AgentOffline
            : Ui.Shell.AgentReconnecting;
    }

    /// <summary>
    /// Reports a failed agent call. Returns what to show for it, so a caller does both in one
    /// line and cannot report the failure without telling the person, or the other way round.
    /// </summary>
    public static string ReportFailure(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (!IsTransportFault(error))
        {
            return Ui.Shell.AgentRequestFailed;
        }

        Set(AgentAvailability.Reconnecting);
        StartProbe();
        return Describe(error);
    }

    /// <summary>Reports a call that the agent answered.</summary>
    public static void ReportSuccess() => Set(AgentAvailability.Online);

    /// <summary>
    /// Reports what the status monitor saw. The monitor probes on a fresh connection, so it is
    /// the authority on whether the agent is running -- a surface's broken pipe only says that
    /// one connection is dead.
    /// </summary>
    public static void ReportMonitor(AgentConnectionState state) =>
        Set(state switch
        {
            AgentConnectionState.Connected or AgentConnectionState.RecoveryOnly => AgentAvailability.Online,
            AgentConnectionState.Starting => AgentAvailability.Reconnecting,
            _ => AgentAvailability.Offline,
        });

    internal static void ResetForTests()
    {
        lock (Gate)
        {
            _current = AgentAvailability.Unknown;
            _probe = null;
            _probeInFlight = false;
            _lastProbeStartedUtc = DateTimeOffset.MinValue;
        }

        Changed = null;
    }

    private static void Set(AgentAvailability availability)
    {
        bool changed;
        bool recovered;
        lock (Gate)
        {
            changed = _current != availability;
            recovered = changed &&
                availability == AgentAvailability.Online &&
                _current is AgentAvailability.Reconnecting or AgentAvailability.Offline;
            _current = availability;
        }

        if (changed)
        {
            Changed?.Invoke(null, new AgentAvailabilityChangedEventArgs(availability, recovered));
        }
    }

    private static void StartProbe()
    {
        Func<CancellationToken, Task<bool>>? probe;
        lock (Gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (_probe is null || _probeInFlight || now - _lastProbeStartedUtc < MinimumProbeInterval)
            {
                return;
            }

            _probeInFlight = true;
            _lastProbeStartedUtc = now;
            probe = _probe;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var reachable = await probe(CancellationToken.None).ConfigureAwait(false);
                Set(reachable ? AgentAvailability.Online : AgentAvailability.Offline);
            }
            catch (Exception error) when (IsTransportFault(error) || error is OperationCanceledException)
            {
                // A probe that cannot complete has answered the question it was asked.
                Set(AgentAvailability.Offline);
            }
            finally
            {
                lock (Gate)
                {
                    _probeInFlight = false;
                }
            }
        });
    }
}
