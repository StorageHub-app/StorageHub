using Xunit;

namespace StorageHub.Testing;

/// <summary>
/// Why a platform-gated case is not running.
/// </summary>
/// <remarks>
/// The reason is written once here rather than at each attribute, because a skipped test that does
/// not say which platform it wanted reads as a test that was quietly abandoned.
/// </remarks>
internal static class PlatformSkipReasons
{
    internal const string RequiresWindows =
        "This behaviour is implemented against Windows APIs and only runs on Windows.";

    internal const string RequiresLinux =
        "This behaviour is implemented against Linux APIs and only runs on Linux.";
}

/// <summary>A test that only has meaning on Windows.</summary>
/// <remarks>
/// StorageHub keeps its Windows integrations - the service host, named-pipe ACLs, DPAPI, the
/// Explorer drop broker - rather than dropping them for portability, so a share of the suite is
/// genuinely Windows-only. Marking those cases up front is what lets the Linux job run the whole
/// solution and report an honest result, instead of failing and being narrowed afterwards.
/// </remarks>
public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = PlatformSkipReasons.RequiresWindows;
        }
    }
}

/// <inheritdoc cref="WindowsOnlyFactAttribute"/>
public sealed class WindowsOnlyTheoryAttribute : TheoryAttribute
{
    public WindowsOnlyTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = PlatformSkipReasons.RequiresWindows;
        }
    }
}

/// <summary>A test that only has meaning on Linux.</summary>
/// <remarks>
/// The counterpart of <see cref="WindowsOnlyFactAttribute"/>, for the Unix socket transport, the
/// file-mode and ownership checks around the vault, and the systemd user unit. These run on the
/// Linux job and skip on Windows, so one solution-wide `dotnet test` is correct on both.
/// </remarks>
public sealed class LinuxOnlyFactAttribute : FactAttribute
{
    public LinuxOnlyFactAttribute()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip = PlatformSkipReasons.RequiresLinux;
        }
    }
}

/// <inheritdoc cref="LinuxOnlyFactAttribute"/>
public sealed class LinuxOnlyTheoryAttribute : TheoryAttribute
{
    public LinuxOnlyTheoryAttribute()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip = PlatformSkipReasons.RequiresLinux;
        }
    }
}
