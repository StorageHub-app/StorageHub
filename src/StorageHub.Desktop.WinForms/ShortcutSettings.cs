using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

internal static class ShortcutSettings
{
    /// <summary>
    /// The rebindable commands, resolved for the current language.
    /// </summary>
    /// <remarks>
    /// Deliberately a property rather than a cached array: the labels follow the current language,
    /// and caching them at type-initialization time would pin the menus to whatever language
    /// happened to be active when this type was first touched. The filter runs on the id, which
    /// does not move.
    /// </remarks>
    internal static IReadOnlyList<UiCommandDefinition> Commands =>
        [.. UiCommandCatalog.Definitions.Where(command => MainForm.IsAvailableCommand(command.Id))];

    internal static Dictionary<string, Keys> Resolve(IReadOnlyDictionary<string, Keys>? overrides)
    {
        var commands = Commands;
        var result = commands.ToDictionary(command => command.Id, command => command.Shortcut, StringComparer.Ordinal);
        if (overrides is null) return result;
        foreach (var command in commands)
            if (overrides.TryGetValue(command.Id, out var keys)) result[command.Id] = keys;
        return Validate(result) is null ? result : Resolve(null);
    }

    internal static string Format(Keys keys) => keys == Keys.None
        ? Ui.Settings.ShortcutUnassigned
        : new KeysConverter().ConvertToString(keys) ?? keys.ToString();

    internal static bool IsValid(Keys keys)
    {
        if (keys == Keys.None) return true;
        var code = keys & Keys.KeyCode;
        if (!Enum.IsDefined(code) || code is Keys.None or Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            return false;
        if ((keys & ~(Keys.KeyCode | Keys.Control | Keys.Shift | Keys.Alt)) != 0) return false;
        if (keys is (Keys.Alt | Keys.F4) or (Keys.Control | Keys.Alt | Keys.Delete)) return false;
        return (keys & (Keys.Control | Keys.Alt)) != 0 || code is >= Keys.F1 and <= Keys.F24 || keys == Keys.Delete;
    }

    internal static string? Validate(IReadOnlyDictionary<string, Keys> shortcuts)
    {
        var assigned = new Dictionary<Keys, string>();
        foreach (var command in Commands)
        {
            var keys = shortcuts.GetValueOrDefault(command.Id, command.Shortcut);
            if (!IsValid(keys)) return Ui.Format(Ui.Validation.ChooseCtrlAltWithAKeyFormat, command.Label);
            if (keys == Keys.None) continue;
            if (assigned.TryGetValue(keys, out var other))
                return Ui.Format(Ui.Validation.IsAlreadyAssignedToClearThatFormat, Format(keys), other);
            assigned.Add(keys, command.Label);
        }
        return null;
    }

    internal static bool IsPaneCommand(UiCommandDefinition command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.Menu is UiMenuId.Edit or UiMenuId.Go || command.Id == UiCommandIds.ViewRefresh;
    }

    internal static bool CanDispatch(UiCommandDefinition command, bool sshFocused, bool textFocused, bool hasPane) =>
        !sshFocused && !textFocused && (!IsPaneCommand(command) || hasPane);
}
