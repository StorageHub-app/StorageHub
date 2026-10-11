using System.Globalization;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>What a transfer does with a file that is already at its destination.</summary>
/// <remarks>The names are saved in the settings file, so they are never renamed.</remarks>
public enum TransferConflictChoice
{
    /// <summary>Replace it.</summary>
    Overwrite,

    /// <summary>Leave it, and do not transfer that file.</summary>
    Skip,

    /// <summary>Replace it when the file being transferred was changed later than it was.</summary>
    OverwriteIfNewer,

    /// <summary>Replace it when the two are not the same size.</summary>
    OverwriteIfSizeDiffers,

    /// <summary>Leave it, and transfer the file under a free name beside it: "name (1).ext".</summary>
    Rename,

    /// <summary>
    /// Treat a shorter file as an unfinished copy and finish it; leave one that is as long as the
    /// source or longer.
    /// </summary>
    Resume
}

/// <summary>What one conflict comes to once a choice is applied to it.</summary>
internal enum TransferConflictOutcome
{
    Overwrite,
    Skip,
    Rename
}

/// <summary>The rules each <see cref="TransferConflictChoice"/> stands for.</summary>
internal static class TransferConflictRules
{
    /// <summary>
    /// Two times this close are the same time. FAT keeps two seconds, and FTP servers that list
    /// to the minute are not rare; a file copied a moment ago must not look newer than its copy.
    /// </summary>
    internal static readonly TimeSpan TimeTolerance = TimeSpan.FromSeconds(2);

    /// <summary>The most numbered names tried before a rename gives up.</summary>
    internal const int MaximumRenameAttempts = 9_999;

    /// <summary>
    /// What to do with <paramref name="existing"/>. When something the rule needs is not known --
    /// a size or a time the listing did not report -- the file is replaced, because a rule that
    /// cannot tell the two apart cannot say the destination is already right.
    /// </summary>
    internal static TransferConflictOutcome Decide(
        TransferConflictChoice choice,
        PaneTransferItem source,
        PaneTransferItem existing)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(existing);
        return choice switch
        {
            TransferConflictChoice.Overwrite => TransferConflictOutcome.Overwrite,
            TransferConflictChoice.Skip => TransferConflictOutcome.Skip,
            TransferConflictChoice.OverwriteIfNewer =>
                source.ModifiedUtc is { } from && existing.ModifiedUtc is { } to && from - to <= TimeTolerance
                    ? TransferConflictOutcome.Skip
                    : TransferConflictOutcome.Overwrite,
            TransferConflictChoice.OverwriteIfSizeDiffers =>
                source.Length is { } length && existing.Length == length
                    ? TransferConflictOutcome.Skip
                    : TransferConflictOutcome.Overwrite,
            TransferConflictChoice.Rename => TransferConflictOutcome.Rename,
            // A file as long as the source is finished, or is not a piece of it; either way
            // finishing it would be wrong. A shorter one is replaced, and the agent continues
            // from what an earlier attempt of its own staged when there is such a thing.
            TransferConflictChoice.Resume =>
                source.Length is { } total && existing.Length is { } have && have >= total
                    ? TransferConflictOutcome.Skip
                    : TransferConflictOutcome.Overwrite,
            _ => throw new ArgumentOutOfRangeException(nameof(choice))
        };
    }

    /// <summary>
    /// The first of "name (1).ext", "name (2).ext", ... that <paramref name="isTaken"/> says is free,
    /// or null when none of the first <see cref="MaximumRenameAttempts"/> is.
    /// </summary>
    /// <remarks>
    /// The number goes before the last extension, as Explorer and the file managers on Linux put
    /// it, so the copy still opens with the same program. A name that starts with its only dot,
    /// ".bashrc", has no extension.
    /// </remarks>
    internal static string? FreeName(string name, Func<string, bool> isTaken)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(isTaken);
        var dot = name.LastIndexOf('.');
        var (stem, extension) = dot > 0 ? (name[..dot], name[dot..]) : (name, string.Empty);
        for (var number = 1; number <= MaximumRenameAttempts; number++)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{stem} ({number}){extension}");
            if (!isTaken(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

/// <summary>
/// How one transfer meets the files already at its destination: a choice made beforehand, or one
/// asked for when the first conflict is found and kept for the rest.
/// </summary>
/// <remarks>
/// Asked at most once, so a folder of a thousand files that are all there already is one
/// question rather than a thousand, and only when there is a conflict, so a transfer into an
/// empty folder asks nothing. What it decided is counted for the sentence that reports the
/// transfer.
/// </remarks>
public sealed class TransferConflictPolicy
{
    private readonly Func<PaneTransferItem, CancellationToken, Task<TransferConflictChoice?>>? _ask;
    private TransferConflictChoice? _choice;
    private bool _declined;

    /// <summary>A choice already made, from Settings or by whoever is transferring.</summary>
    public TransferConflictPolicy(TransferConflictChoice choice)
    {
        if (!Enum.IsDefined(choice))
        {
            throw new ArgumentOutOfRangeException(nameof(choice));
        }

        _choice = choice;
    }

    /// <summary>
    /// A choice to ask for, given the first file found already at the destination. Null as the
    /// answer stops the transfer.
    /// </summary>
    public TransferConflictPolicy(Func<PaneTransferItem, CancellationToken, Task<TransferConflictChoice?>> ask)
    {
        _ask = ask ?? throw new ArgumentNullException(nameof(ask));
    }

    /// <summary>What every transfer did before there was a choice: replace.</summary>
    public static TransferConflictPolicy Replace { get; } = new(TransferConflictChoice.Overwrite);

    /// <summary>The choice, once it is known.</summary>
    public TransferConflictChoice? Choice => _choice;

    /// <summary>Files left as they were because they were already at the destination.</summary>
    public int Skipped { get; private set; }

    /// <summary>Files transferred under a new name because the name was taken.</summary>
    public int Renamed { get; private set; }

    /// <summary>
    /// The choice for <paramref name="existing"/>, asking for it if it is the first conflict.
    /// Null when the question was answered with Cancel.
    /// </summary>
    internal async Task<TransferConflictChoice?> ResolveAsync(
        PaneTransferItem existing,
        CancellationToken cancellationToken)
    {
        if (_choice is { } known) return known;
        if (_declined || _ask is null) return null;

        _choice = await _ask(existing, cancellationToken).ConfigureAwait(true);
        if (_choice is { } chosen && !Enum.IsDefined(chosen))
        {
            _choice = null;
        }

        _declined = _choice is null;
        return _choice;
    }

    internal void Count(int skipped, int renamed)
    {
        Skipped += skipped;
        Renamed += renamed;
    }
}

/// <summary>What each choice is called, in the dialog that asks and in Settings alike.</summary>
internal static class TransferConflictText
{
    /// <summary>The choices in the order they are offered.</summary>
    internal static IReadOnlyList<TransferConflictChoice> All { get; } =
    [
        TransferConflictChoice.Overwrite,
        TransferConflictChoice.OverwriteIfNewer,
        TransferConflictChoice.OverwriteIfSizeDiffers,
        TransferConflictChoice.Resume,
        TransferConflictChoice.Rename,
        TransferConflictChoice.Skip
    ];

    internal static string Label(TransferConflictChoice choice) => choice switch
    {
        TransferConflictChoice.Overwrite => Ui.Transfer.ConflictOverwrite,
        TransferConflictChoice.Skip => Ui.Transfer.ConflictSkip,
        TransferConflictChoice.OverwriteIfNewer => Ui.Transfer.ConflictOverwriteIfNewer,
        TransferConflictChoice.OverwriteIfSizeDiffers => Ui.Transfer.ConflictOverwriteIfSizeDiffers,
        TransferConflictChoice.Rename => Ui.Transfer.ConflictRename,
        TransferConflictChoice.Resume => Ui.Transfer.ConflictResume,
        _ => choice.ToString()
    };
}
