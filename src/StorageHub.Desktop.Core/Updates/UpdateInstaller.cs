using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StorageHub.Desktop.Updates;

/// <summary>
/// Hands a verified package to the platform's own installer.
/// </summary>
/// <remarks>
/// The one step that cannot look the same on both systems, and deliberately the only one. Windows
/// applies an MSI through msiexec, which raises its own elevation prompt; Linux applies a .deb
/// through the package manager under pkexec, which raises polkit's. Neither is StorageHub's prompt
/// to draw, and neither should be: a credential dialog that an application drew itself is the shape
/// of every phishing attempt, and people are right to distrust it.
///
/// StorageHub is being replaced by what it launches, so the installer is started detached and this
/// process exits rather than waiting for it.
/// </remarks>
internal interface IUpdateInstaller
{
    /// <summary>The package kind this machine can apply.</summary>
    UpdatePackageKind Kind { get; }

    /// <summary>The architecture to ask the feed for.</summary>
    string Architecture { get; }

    /// <summary>
    /// Starts the platform installer and reports whether it began.
    /// </summary>
    /// <remarks>
    /// Success means the installer started, not that the update completed - by then this process is
    /// on its way out. A refusal at the elevation prompt looks like success here and leaves the
    /// current version running, which is the correct outcome for somebody who changed their mind.
    /// </remarks>
    bool Launch(string packagePath);
}

/// <summary>Chooses the installer for the machine, or says there is none.</summary>
internal static class UpdateInstallers
{
    internal static IUpdateInstaller? ForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) return new MsiUpdateInstaller();
        if (OperatingSystem.IsLinux()) return new DebUpdateInstaller();
        return null;
    }

    /// <summary>The architecture name the feed uses, which is the runtime's own spelling.</summary>
    internal static string CurrentArchitecture => RuntimeInformation.ProcessArchitecture.ToString();
}

/// <summary>Applies an MSI through msiexec.</summary>
internal sealed class MsiUpdateInstaller : IUpdateInstaller
{
    public UpdatePackageKind Kind => UpdatePackageKind.Msi;

    public string Architecture => UpdateInstallers.CurrentArchitecture;

    /// <remarks>
    /// <c>/i</c> rather than <c>/fa</c>: the package carries a major-upgrade rule, so installing it
    /// over the current one is what removes the old version. <c>/qb</c> shows a progress bar and no
    /// questions - a silent install would leave somebody staring at an application that closed for
    /// no visible reason.
    /// </remarks>
    public bool Launch(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        if (!File.Exists(packagePath)) return false;

        var start = new ProcessStartInfo("msiexec.exe")
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(packagePath) ?? Environment.CurrentDirectory,
        };
        start.ArgumentList.Add("/i");
        start.ArgumentList.Add(packagePath);
        start.ArgumentList.Add("/qb");

        return Start(start);
    }

    private static bool Start(ProcessStartInfo start)
    {
        try
        {
            using var process = Process.Start(start);
            return process is not null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // The elevation prompt was refused, or msiexec is not where it should be. Either way
            // the running version is untouched, which is the safe outcome.
            return false;
        }
    }
}

/// <summary>Applies a .deb through the system package manager, elevated by polkit.</summary>
internal sealed class DebUpdateInstaller : IUpdateInstaller
{
    public UpdatePackageKind Kind => UpdatePackageKind.Deb;

    /// <summary>Debian names amd64 what the runtime calls X64.</summary>
    public string Architecture => UpdateInstallers.CurrentArchitecture;

    /// <remarks>
    /// <c>apt-get install</c> on the file rather than <c>dpkg -i</c>, because apt resolves whatever
    /// the new package depends on and dpkg would simply fail on a missing one. pkexec rather than
    /// sudo: it asks through polkit, which is the desktop's own prompt, and works without a
    /// terminal to type into.
    /// </remarks>
    public bool Launch(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        if (!File.Exists(packagePath)) return false;

        // An absolute path, because apt treats an argument without a separator as a package name
        // from the archive rather than as a file - and would install a different thing entirely.
        var absolute = Path.GetFullPath(packagePath);

        var start = new ProcessStartInfo("pkexec")
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(absolute) ?? Environment.CurrentDirectory,
        };
        start.ArgumentList.Add("apt-get");
        start.ArgumentList.Add("install");
        start.ArgumentList.Add("--yes");
        start.ArgumentList.Add("--allow-downgrades");
        start.ArgumentList.Add(absolute);

        try
        {
            using var process = Process.Start(start);
            return process is not null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // pkexec is absent, or the authentication was dismissed.
            return false;
        }
    }
}
