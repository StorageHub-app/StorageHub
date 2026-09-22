using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>One name in a batch rename preview.</summary>
internal sealed record BatchRenameLine(string Source, string Target)
{
    public bool Changes => !string.Equals(Source, Target, StringComparison.Ordinal);

    /// <summary>"old → new", or "old (unchanged)", as the preview reads it.</summary>
    public string Text => Changes
        ? Ui.Format(Ui.Shell.BatchRenameMappingFormat, Source, Target)
        : Ui.Format(Ui.Shell.BatchRenameUnchangedFormat, Source);
}

/// <summary>
/// What a find-and-replace across several names would do, and whether it may.
/// </summary>
/// <remarks>
/// <para>
/// Lifted out of 1.x's BatchRenameDialog, where these rules were a private method on a Form and so
/// could only be checked by typing into one. They are the same rules: every new name must be a
/// valid name, no two may land on the same name, none may land on something else in the folder,
/// and none may land on another selected item -- since the renames run one at a time, that item
/// could still be there when its name is taken.
/// </para>
/// <para>
/// Matching ignores case, as 1.x's did; replacing "img" in "IMG_0412.jpg" is what somebody means.
/// Collisions ignore case too, because a folder on Windows, and most object stores fronted by one,
/// cannot hold two names that differ only in case.
/// </para>
/// </remarks>
internal sealed class BatchRenamePlan
{
    private BatchRenamePlan(IReadOnlyList<BatchRenameLine> lines, string? problem)
    {
        Lines = lines;
        Problem = problem;
    }

    internal IReadOnlyList<BatchRenameLine> Lines { get; }

    /// <summary>Why the plan cannot be applied, or null when it can.</summary>
    internal string? Problem { get; }

    internal bool CanApply => Problem is null;

    /// <summary>Only the names that change, source to target, in selection order.</summary>
    internal IReadOnlyList<BatchRenameLine> Changes => [.. Lines.Where(static line => line.Changes)];

    /// <summary>The line under the preview: the problem, or how many will be renamed.</summary>
    internal string Summary => Problem ?? Ui.Format(Ui.Shell.BatchRenameSummaryFormat, Changes.Count);

    /// <param name="sources">The selected names, in order.</param>
    /// <param name="occupied">Every name in the folder, selected or not.</param>
    internal static BatchRenamePlan Build(
        IReadOnlyList<string> sources,
        IEnumerable<string> occupied,
        string? find,
        string? replace)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(occupied);
        find ??= string.Empty;
        replace ??= string.Empty;

        var others = new HashSet<string>(occupied, StringComparer.OrdinalIgnoreCase);
        var selected = new HashSet<string>(sources, StringComparer.OrdinalIgnoreCase);
        others.ExceptWith(selected);
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var lines = new List<BatchRenameLine>(sources.Count);
        string? problem = null;
        foreach (var source in sources)
        {
            var target = find.Length == 0
                ? source
                : source.Replace(find, replace, StringComparison.OrdinalIgnoreCase);
            var line = new BatchRenameLine(source, target);
            lines.Add(line);
            if (!line.Changes) continue;

            problem ??= PaneItemNameRules.Validate(target);
            if (!targets.Add(target)) problem ??= Ui.Validation.TwoSelectedItemsWouldReceiveTheSame;
            if (others.Contains(target)) problem ??= Ui.Format(Ui.Validation.AnItemNamedAlreadyExistsFormat, target);

            // A case-only rename of an item to itself is not a collision; landing on a different
            // selected item is, whatever that item is about to become.
            if (selected.Contains(target) && !string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            {
                problem ??= Ui.Validation.ATargetNameCollidesWithAnother;
            }
        }

        if (find.Length == 0) problem = Ui.Validation.EnterTextToFindInTheSelected;
        else if (!lines.Any(static line => line.Changes)) problem = Ui.Validation.NoneOfTheSelectedNamesContain;

        return new BatchRenamePlan(lines, problem);
    }
}
