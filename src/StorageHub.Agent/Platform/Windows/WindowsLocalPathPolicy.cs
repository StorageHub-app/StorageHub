using StorageHub.Agent.Transfers;

namespace StorageHub.Agent.Windows;

/// <summary>Where a transfer may write on Windows.</summary>
/// <remarks>
/// The rules this carries were already in LocalUserPathTransferEndpoint; moving them here changed
/// none of them. What changed is that the endpoint now asks a policy rather than assuming one, so
/// the Linux answer can differ where it genuinely does.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsLocalPathPolicy : ILocalPathPolicy
{
    /// <summary>NTFS is case-insensitive in practice, so two spellings name one folder.</summary>
    public StringComparison PathComparison => StringComparison.OrdinalIgnoreCase;

    public string AccountDescription => "your Windows account";

    /// <summary>
    /// Extended-length and device paths bypass the normalisation every check below depends on, so
    /// they are refused rather than canonicalised.
    /// </summary>
    public bool IsRejectedSyntax(string root, out string reason)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            root.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            reason = "Device and extended-length paths cannot be used for transfers.";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    public IEnumerable<(string Folder, string Reason)> ProtectedRoots()
    {
        yield return (
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StorageHub"),
            "StorageHub's own data folder cannot be a transfer destination.");
        yield return (
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Windows system folders cannot be a transfer destination.");
        yield return (
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "Windows system folders cannot be a transfer destination.");
        yield return (
            Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
            "Windows system folders cannot be a transfer destination.");
        yield return (
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Installed program folders cannot be a transfer destination.");
        yield return (
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Installed program folders cannot be a transfer destination.");
    }
}
