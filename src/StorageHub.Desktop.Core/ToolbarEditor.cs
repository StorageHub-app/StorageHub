using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>One command that can be put on the toolbar.</summary>
/// <param name="Id">What is stored. Stable across releases and languages.</param>
/// <param name="Label">
/// What is read: the menu the command lives under, then its name. The menu is part of the label
/// because a list of every command is otherwise a hundred unrelated verbs, and "Copy" means
/// something different under Edit than it would anywhere else.
/// </param>
internal sealed record ToolbarCommandChoice(string Id, string Label)
{
    /// <summary>What a list shows, so the row needs no template of its own.</summary>
    public override string ToString() => Label;
}

/// <summary>
/// The toolbar's contents while they are being arranged: which commands are on it, in what order,
/// and what is still available to add.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="ToolbarLayout"/>, which owns what a stored layout means. This owns the
/// editing of one, and holds the only mutable copy: every operation returns whether it changed
/// anything, so the screen can decide whether to mark itself unsaved without comparing lists.
/// </para>
/// <para>
/// Extracted from <c>ToolbarSettingsControl</c>, which mixed these rules into its list boxes and
/// so could only be tested by driving a UserControl.
/// </para>
/// </remarks>
internal sealed class ToolbarEditor
{
    private readonly List<string> _layout;

    internal ToolbarEditor(IReadOnlyList<string>? items) => _layout = [.. ToolbarLayout.Resolve(items)];

    /// <summary>The layout as it now stands, ready to be stored.</summary>
    internal IReadOnlyList<string> Items => _layout;

    /// <summary>
    /// Everything not already on the toolbar, in menu order.
    /// </summary>
    /// <remarks>
    /// Narrowed to the commands that are actually wired up. The toolbar renders only those, so
    /// offering the rest would let somebody add a button that never appears and give them no way
    /// to find out why. This is the same honesty the menus got when they started dimming the
    /// commands this shell has not reached yet.
    /// </remarks>
    internal IReadOnlyList<ToolbarCommandChoice> Available()
    {
        var used = _layout.ToHashSet(StringComparer.Ordinal);
        return
        [
            .. UiCommandCatalog.Definitions
                .Where(command => !used.Contains(command.Id))
                .Where(command => UiCommandCatalog.IsAvailable(command.Id))
                .Select(command => new ToolbarCommandChoice(
                    command.Id,
                    $"{UiCommandCatalog.MenuTitle(command.Menu)}  ·  {StripMnemonics(command.Label)}"))
        ];
    }

    /// <summary>What an entry on the toolbar reads as, divider included.</summary>
    internal static string Describe(string entry) =>
        string.Equals(entry, ToolbarLayout.Separator, StringComparison.Ordinal)
            ? Ui.Settings.ToolbarSeparator
            : StripMnemonics(UiCommandCatalog.GetDefinition(entry).Label);

    /// <summary>The whole toolbar in words, which is what the list on the right shows.</summary>
    internal IReadOnlyList<string> Describe() => [.. _layout.Select(Describe)];

    /// <summary>
    /// Puts a command after <paramref name="at"/>, or at the end when nothing is selected.
    /// </summary>
    /// <remarks>
    /// Refused when the command is already on the toolbar: a layout is a set of buttons, and
    /// <see cref="ToolbarLayout.Sanitise"/> would drop the duplicate on the next load anyway,
    /// which would look like the edit had been forgotten.
    /// </remarks>
    internal bool Add(string commandId, int at = -1)
    {
        if (string.IsNullOrWhiteSpace(commandId) ||
            string.Equals(commandId, ToolbarLayout.Separator, StringComparison.Ordinal) ||
            _layout.Contains(commandId, StringComparer.Ordinal))
        {
            return false;
        }

        _layout.Insert(Insertion(at), commandId);
        return true;
    }

    /// <summary>Puts a divider after <paramref name="at"/>, or at the end.</summary>
    internal bool AddSeparator(int at = -1)
    {
        _layout.Insert(Insertion(at), ToolbarLayout.Separator);
        return true;
    }

    internal bool RemoveAt(int at)
    {
        if (at < 0 || at >= _layout.Count) return false;
        _layout.RemoveAt(at);
        return true;
    }

    /// <summary>Swaps an entry with its neighbour, and answers where it ended up.</summary>
    internal int Move(int at, int delta)
    {
        var target = at + delta;
        if (at < 0 || at >= _layout.Count || target < 0 || target >= _layout.Count)
        {
            return -1;
        }

        (_layout[at], _layout[target]) = (_layout[target], _layout[at]);
        return target;
    }

    internal bool Reset(ToolbarPreset preset)
    {
        var replacement = ToolbarLayout.Preset(preset);
        if (_layout.SequenceEqual(replacement, StringComparer.Ordinal)) return false;
        _layout.Clear();
        _layout.AddRange(replacement);
        return true;
    }

    /// <summary>
    /// Where an insertion lands: after the selected entry, or at the end when there is none.
    /// </summary>
    private int Insertion(int at) => at >= 0 && at < _layout.Count ? at + 1 : _layout.Count;

    /// <summary>Menu mnemonics mean nothing in a list, so the ampersand is dropped.</summary>
    private static string StripMnemonics(string label) =>
        label.Replace("&", string.Empty, StringComparison.Ordinal);
}
