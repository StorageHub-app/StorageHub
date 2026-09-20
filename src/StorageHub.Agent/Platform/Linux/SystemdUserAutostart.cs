using System.Diagnostics;
using System.Globalization;
using StorageHub.Agent;

namespace StorageHub.Agent.Linux;

/// <summary>
/// Starting the agent again without the desktop asking, as a systemd user unit.
/// </summary>
/// <remarks>
/// The counterpart of the HKCU Run entry on Windows, and chosen over a system-wide unit for the
/// same reason the whole Linux agent is per-user: the vault, the socket and the data root all
/// belong to one account, and a unit running as root would have none of them. It also needs no
/// elevation, which keeps the awkward part of the Windows design - that an administrative step must
/// somehow still read the calling user's own secrets - from ever arising here.
///
/// Surviving sign-out is a second, separate step. A user unit stops when the last session ends
/// unless lingering is enabled, and enabling it is a decision about a machine rather than about an
/// application, so it is offered rather than assumed.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class SystemdUserAutostart : IAutostartRegistration
{
    internal const string UnitName = "storagehub-agent.service";

    private readonly string _unitPath;

    public SystemdUserAutostart()
        : this(DefaultUnitDirectory())
    {
    }

    internal SystemdUserAutostart(string unitDirectory) =>
        _unitPath = Path.Combine(unitDirectory, UnitName);

    public bool IsRegistered => File.Exists(_unitPath);

    /// <summary>
    /// True once this account lingers, which is what lets a user unit run with nobody signed in.
    /// </summary>
    /// <remarks>
    /// Read rather than assumed: a registered unit that stops at sign-out would otherwise be
    /// presented as an agent that keeps running, which is exactly the promise a scheduled sync
    /// depends on.
    /// </remarks>
    public bool SurvivesSignOut => ReadLingering();

    public bool TryRegister(string commandLine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandLine);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_unitPath)!);
            File.WriteAllText(_unitPath, BuildUnit(commandLine));
            return RunSystemctl("daemon-reload") && RunSystemctl("enable", "--now", UnitName);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool TryRemove()
    {
        try
        {
            if (File.Exists(_unitPath))
            {
                _ = RunSystemctl("disable", "--now", UnitName);
                File.Delete(_unitPath);
            }

            return RunSystemctl("daemon-reload");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// The unit text.
    /// </summary>
    /// <remarks>
    /// ExecStart records the path as it is at registration, which an AppImage invalidates the moment
    /// it is moved or replaced. The host re-registers when the recorded path no longer matches its
    /// own, so a moved bundle repairs itself rather than silently failing to start.
    /// </remarks>
    internal static string BuildUnit(string commandLine) =>
        BuildUnit(commandLine, Environment.GetEnvironmentVariable("DOTNET_ROOT"));

    internal static string BuildUnit(string commandLine, string? dotnetRoot)
    {
        List<string> lines =
        [
            "[Unit]",
            "Description=StorageHub Agent",
            "PartOf=graphical-session.target",
            string.Empty,
            "[Service]",
            "Type=simple",
        ];

        // A systemd user unit inherits almost nothing from the shell that registered it, so a
        // framework-dependent build cannot find a runtime installed under the home directory: it
        // exits 131 before logging anything of its own, which reads as the agent crashing rather
        // than as the unit being wrong. A released build is self-contained and ignores this, so the
        // line is carried only when the registering process had one.
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            lines.Add("Environment=DOTNET_ROOT=" + dotnetRoot);
        }

        lines.AddRange(
        [
            "ExecStart=" + commandLine,
            "Restart=on-failure",
            "RestartSec=5",
            string.Empty,
            "[Install]",
            "WantedBy=default.target",
            string.Empty,
        ]);

        return string.Join(LineFeed, lines);
    }

    private const char LineFeed = (char)10;

    private static string DefaultUnitDirectory()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configHome))
        {
            var home = Environment.GetEnvironmentVariable("HOME")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            configHome = Path.Combine(home, ".config");
        }

        return Path.Combine(configHome, "systemd", "user");
    }

    private static bool ReadLingering()
    {
        var output = Run("loginctl", ["show-user", CurrentUserId(), "--property=Linger"]);
        return output is not null &&
            output.Contains("Linger=yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string CurrentUserId() =>
        Ipc.Unix.UnixPeerCredentials.EffectiveUserId().ToString(CultureInfo.InvariantCulture);

    private static bool RunSystemctl(params string[] arguments) =>
        Run("systemctl", ["--user", .. arguments]) is not null;

    private static string? Run(string fileName, string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(TimeSpan.FromSeconds(15)))
            {
                return null;
            }

            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // A machine without systemd or loginctl is a machine where autostart is simply not
            // available, which the caller reports rather than crashing over.
            return null;
        }
    }
}
