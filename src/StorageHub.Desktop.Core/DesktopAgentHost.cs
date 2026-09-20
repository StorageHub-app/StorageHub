using StorageHub.Agent;
using StorageHub.Ipc;

namespace StorageHub.Desktop;

/// <summary>
/// Which agent the desktop should talk to, and how.
/// </summary>
/// <remarks>
/// The endpoint, its trust model and whether the desktop may start an agent of its own all follow
/// from one fact: what kind of agent this machine has. Deciding it in one place stops the three from
/// disagreeing - connecting to a machine endpoint with same-user trust, or launching a session agent
/// beside a running service, are both silent failures that look like "the agent is offline".
///
/// What used to be a Windows-only question with a named pipe for an answer is now
/// <see cref="IAgentPlatform"/>'s. The platform discovers the mode, names the endpoint and pairs the
/// authenticator; this type caches the answer and says what the desktop is allowed to do with it.
/// </remarks>
internal static class DesktopAgentHost
{
    private static readonly Lock Gate = new();
    private static AgentHostMode? _mode;
    private static IAgentPlatform? _platform;

    /// <summary>The platform in force. Selected once, and the only place the desktop branches.</summary>
    internal static IAgentPlatform Platform
    {
        get
        {
            lock (Gate)
            {
                return _platform ??= AgentPlatforms.ForCurrentOperatingSystem();
            }
        }
    }

    /// <summary>The mode in force, discovered from the machine and then cached.</summary>
    internal static AgentHostMode Mode
    {
        get
        {
            lock (Gate)
            {
                _mode ??= Platform.DiscoverHostMode();
                return _mode.Value;
            }
        }
    }

    internal static IpcEndpoint NormalEndpoint =>
        Platform.ResolveEndpoint(Mode, AgentIpcChannel.Normal);

    internal static IpcEndpoint SecretEndpoint =>
        Platform.ResolveEndpoint(Mode, AgentIpcChannel.Secret);

    internal static IpcTrustModel TrustModel => Platform.ResolveTrustModel(Mode);

    /// <summary>True when the desktop owns the agent's lifetime and may start one.</summary>
    internal static bool DesktopStartsAgent => Mode != AgentHostMode.WindowsService;

    /// <summary>
    /// True when the agent should be stopped as the desktop closes, rather than left running for
    /// the rest of the session.
    /// </summary>
    internal static bool DesktopStopsAgent => Mode == AgentHostMode.AppSession;

    /// <summary>
    /// True only when an autostart registration should exist. Deliberately narrower than
    /// <see cref="DesktopStartsAgent"/>: the desktop starts the agent in both session modes, but
    /// only one of them wants it started again at sign-in. Conflating the two made an update
    /// re-register the entry and silently move anyone on "only while StorageHub is open" back to
    /// starting at sign-in.
    /// </summary>
    internal static bool RegistersAutostart => Mode == AgentHostMode.UserSession;

    /// <summary>
    /// Forgets the cached mode, so the next connection re-reads it. Called after the mode is
    /// changed from Settings: the answer is otherwise fixed for the life of the process, and the
    /// desktop would keep talking to the agent it found at startup.
    /// </summary>
    internal static void Invalidate()
    {
        lock (Gate)
        {
            _mode = null;
        }
    }

    /// <summary>Overrides the platform, for tests that must not ask the real machine.</summary>
    internal static void OverridePlatform(IAgentPlatform? platform)
    {
        lock (Gate)
        {
            _platform = platform;
            _mode = null;
        }
    }
}
