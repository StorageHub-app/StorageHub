namespace StorageHub.Domain.Storage;

/// <summary>Which of StorageHub's working files a name is.</summary>
public enum StagingFileKind
{
    ResumePartial,
    TransferStaging,
    WriteProbe,
    RenameTemporary
}

/// <summary>
/// The names StorageHub gives the files and folders it stages its own work in, wherever they end up.
/// </summary>
/// <remarks>
/// <para>
/// Each of these is made by one part of StorageHub and recognised by that part alone, which was
/// enough while nothing else had to know them. Comparing two folders and cleaning up after an
/// interrupted run both have to: a partial download is not a difference between two folders, and
/// it is exactly what a clean-up is looking for. One list, so the two cannot disagree about what is
/// StorageHub's.
/// </para>
/// <list type="bullet">
/// <item><c>.storagehub-{key}.partial</c>: a resumable write staged beside its destination, on this
/// PC or on an FTP or SFTP server, renamed into place once it is whole.</item>
/// <item><c>.storagehub-{id}.staging</c>: a whole-file write staged beside its destination by the
/// transfer engine.</item>
/// <item><c>.storagehub-internal</c>: the Local provider's reserved folder, whose <c>staging</c>
/// holds that provider's partial files.</item>
/// <item><c>.storagehub-write-{id}.tmp</c> and <c>.storagehub-rename-{id}.tmp</c>: the probe that
/// proves a folder is writable, and the stand-in a case-only rename passes through.</item>
/// <item><c>.storagehub-drop</c>: the marker an Explorer drag leaves in its staging folder.</item>
/// <item><c>.storagehub</c>: the folder sync keeps beside its data and excludes by default.</item>
/// </list>
/// </remarks>
public static class StorageHubStagingNames
{
    internal const string Prefix = ".storagehub-";

    /// <summary>What every one of the working files a transfer or a mutation leaves starts with.</summary>
    public const string FilePrefix = Prefix;

    /// <summary>The Local provider's reserved folder.</summary>
    public const string InternalFolderName = ".storagehub-internal";

    /// <summary>The folder sync keeps its own state in.</summary>
    public const string SyncFolderName = ".storagehub";

    /// <summary>The marker an Explorer drag leaves in its staging folder.</summary>
    public const string DropMarkerName = ".storagehub-drop";

    /// <summary>Whether a file or folder name is one StorageHub stages its own work under.</summary>
    public static bool IsStagingName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (name.Equals(InternalFolderName, StringComparison.OrdinalIgnoreCase) ||
            name.Equals(SyncFolderName, StringComparison.OrdinalIgnoreCase) ||
            name.Equals(DropMarkerName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) &&
            (name.EndsWith(".partial", StringComparison.OrdinalIgnoreCase) ||
             name.EndsWith(".staging", StringComparison.OrdinalIgnoreCase) ||
             name.StartsWith(Prefix + "write-", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
             name.StartsWith(Prefix + "rename-", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Which working file <paramref name="name"/> is, for the files a transfer or a mutation leaves
    /// beside its destination; false for anything else, the folders and the drop marker included.
    /// </summary>
    /// <remarks>
    /// The probes are tested first: <c>.storagehub-write-*.tmp</c> would otherwise need its own
    /// exclusion from every rule after it. A name is only one of these with something between
    /// the prefix and the suffix, so <c>.storagehub-.partial</c> is nobody's.
    /// </remarks>
    public static bool TryClassifyFile(string? name, out StagingFileKind kind)
    {
        kind = default;
        if (string.IsNullOrEmpty(name) || !name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var (start, end, candidate) in Rules)
        {
            if (name.Length > start.Length + end.Length &&
                name.StartsWith(start, StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(end, StringComparison.OrdinalIgnoreCase))
            {
                kind = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The transfer a <c>.storagehub-{id}.staging</c> file belongs to, which is what tells whether
    /// that transfer is still under way.
    /// </summary>
    public static bool TryReadStagingJobId(string? name, out Guid jobId)
    {
        jobId = Guid.Empty;
        return TryClassifyFile(name, out var kind) && kind == StagingFileKind.TransferStaging &&
            Guid.TryParseExact(name![Prefix.Length..^".staging".Length], "N", out jobId);
    }

    private static readonly (string Start, string End, StagingFileKind Kind)[] Rules =
    [
        (Prefix + "write-", ".tmp", StagingFileKind.WriteProbe),
        (Prefix + "rename-", ".tmp", StagingFileKind.RenameTemporary),
        (Prefix, ".partial", StagingFileKind.ResumePartial),
        (Prefix, ".staging", StagingFileKind.TransferStaging)
    ];

    /// <summary>
    /// Whether any segment of a <c>/</c>-separated relative path is a staging name, which is what
    /// puts everything inside <c>.storagehub-internal</c> out of sight along with the folder.
    /// </summary>
    public static bool IsWithinStaging(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return false;
        }

        foreach (var segment in relativePath.Split('/'))
        {
            if (IsStagingName(segment))
            {
                return true;
            }
        }

        return false;
    }
}
