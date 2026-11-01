namespace StorageHub.Desktop;

/// <summary>What one file in a previewed transfer does to its destination.</summary>
public enum TransferPreviewAction
{
    /// <summary>Nothing is there yet; the file is created.</summary>
    New,

    /// <summary>A file is there, and is replaced.</summary>
    Replace,

    /// <summary>A file is there, and is left as it is.</summary>
    Skip,

    /// <summary>A file is there; the transfer lands beside it under a free name.</summary>
    Rename
}

/// <summary>One file a transfer would write, and what is at its destination already.</summary>
public sealed record TransferPreviewRow(
    PaneTransferItem Source,
    string DestinationPath,
    PaneTransferItem? Existing)
{
    /// <summary>True when a file is already where this one would land.</summary>
    public bool IsConflict => Existing is not null;

    /// <summary>What <paramref name="choice"/> does to this file.</summary>
    public TransferPreviewAction ActionFor(TransferConflictChoice choice) =>
        Existing is null
            ? TransferPreviewAction.New
            : TransferConflictRules.Decide(choice, Source, Existing) switch
            {
                TransferConflictOutcome.Skip => TransferPreviewAction.Skip,
                TransferConflictOutcome.Rename => TransferPreviewAction.Rename,
                _ => TransferPreviewAction.Replace
            };
}

/// <summary>
/// Every file a transfer would write, read before anything is queued, so whoever is transferring
/// can see what it will replace and choose for each file. Nothing is created or queued to build it.
/// </summary>
public sealed record TransferPreview(IReadOnlyList<TransferPreviewRow> Rows)
{
    /// <summary>The files that land on a file already there.</summary>
    public int ConflictCount => Rows.Count(static row => row.IsConflict);
}
