using System.Diagnostics;

namespace StorageHub.Agent;

/// <summary>
/// Answers <see cref="IInstallationProbe"/> from the machine itself.
///
/// Every method swallows the failure it can provoke and answers "no" rather than throwing: a
/// check that cannot inspect one thing should still report the other nine. An operator whose
/// service is unreachable is already having a bad time without the diagnostic also crashing.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsInstallationProbe : IInstallationProbe
{
    public PathVisibility InspectDirectory(string path) =>
        Inspect(path, Directory.Exists, probe => new DirectoryInfo(probe).EnumerateFileSystemInfos().Any());

    public PathVisibility InspectFile(string path) =>
        Inspect(path, File.Exists, probe => new FileInfo(probe).Length >= 0);

    /// <summary>
    /// Distinguishes "not there" from "not allowed to look".
    ///
    /// Directory.Exists and File.Exists both answer false for a path the caller cannot open, so
    /// a false is followed by an access attempt that throws a different exception for each case.
    /// Without this a machine-owned service root -- which denies the signed-in user by design --
    /// reads as a missing installation.
    /// </summary>
    private static PathVisibility Inspect(string path, Func<string, bool> exists, Func<string, bool> touch)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return PathVisibility.Missing;
        }

        if (exists(path))
        {
            return PathVisibility.Present;
        }

        try
        {
            _ = touch(path);
            return PathVisibility.Present;
        }
        catch (UnauthorizedAccessException)
        {
            return PathVisibility.Denied;
        }
        catch (Exception error) when (error is IOException or ArgumentException)
        {
            return PathVisibility.Missing;
        }
    }

    public long? FileLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Named pipes are enumerated rather than connected to. Opening one would consume a server
    /// instance and, for the secret pipe, look exactly like an unauthorised client.
    /// </summary>
    public bool PipeExists(string pipeName)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            return false;
        }

        try
        {
            return Directory
                .GetFiles(@"\\.\pipe\")
                .Any(entry => string.Equals(
                    Path.GetFileName(entry),
                    pipeName,
                    StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public AgentServiceState DescribeService()
    {
        try
        {
            return AgentServiceInstaller.Describe();
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new AgentServiceState(false, false);
        }
    }

    /// <summary>
    /// Reads the service's failure actions with <c>sc.exe qfailure</c>, matching how the installer
    /// already talks to the service control manager. A service with no actions configured prints
    /// a reset period of zero and no action lines.
    /// </summary>
    public bool ServiceRestartsOnFailure()
    {
        var output = RunServiceControl($"qfailure {AgentHostLayout.ServiceName}");
        return output is not null
            && output.Contains("RESTART", StringComparison.OrdinalIgnoreCase);
    }

    public string? ReadStagedVersion(string executableName)
    {
        try
        {
            return AgentServiceStaging.ReadStagedVersion(executableName);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public string StagedAgentDirectory => AgentServiceStaging.ResolveDirectory();

    public bool IsElevated() => AgentHostLayout.IsElevated();

    internal static string? RunServiceControl(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("sc.exe", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            return process.WaitForExit(TimeSpan.FromSeconds(15)) ? output : null;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
