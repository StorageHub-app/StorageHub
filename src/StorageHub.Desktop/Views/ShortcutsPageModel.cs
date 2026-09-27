using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Input;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Settings;
using InputKey = Avalonia.Input.Key;

namespace StorageHub.Desktop.Views;

/// <summary>One command in the shortcuts table.</summary>
internal sealed class ShortcutRowModel(string id, string command, string defaultShortcut) : INotifyPropertyChanged
{
    private string _shortcut = string.Empty;

    internal string Id { get; } = id;

    /// <summary>"Menu: Command", so two commands with one label in different menus can be told apart.</summary>
    public string Command { get; } = command;

    public string Shortcut
    {
        get => _shortcut;
        internal set
        {
            if (string.Equals(_shortcut, value, StringComparison.Ordinal)) return;
            _shortcut = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Shortcut)));
        }
    }

    public string Default { get; } = defaultShortcut;

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// The Shortcuts page: every rebindable command, what it is bound to, and a box that takes a new
/// chord.
/// </summary>
/// <remarks>
/// <para>
/// 1.4's ShortcutSettingsControl, as a page model: a table of Command, Shortcut and Default, then
/// the capture box with Assign, Clear and Restore defaults, and a line saying what happened. A
/// proposed table is checked whole by <see cref="ShortcutSettings.Validate"/> before it is taken,
/// so a chord already bound elsewhere is refused with the name of the command that has it rather
/// than silently stolen from it.
/// </para>
/// <para>
/// Edits reach the working preferences through the same callback the toolbar uses, so they are
/// written by Apply like every other setting.
/// </para>
/// </remarks>
internal sealed class ShortcutsPageModel : SettingsPageModel, INotifyPropertyChanged
{
    private readonly Action<ShortcutsPageModel> _changed;
    private Dictionary<string, KeyGesture?> _shortcuts;
    private ShortcutRowModel? _selected;
    private KeyGesture? _captured;
    private string _message = Ui.Settings.ShortcutSelectCommandHint;

    internal ShortcutsPageModel(
        SettingsPageDefinition definition,
        IReadOnlyDictionary<string, KeyGesture?>? shortcuts,
        Action<ShortcutsPageModel> changed)
        : base(definition, [])
    {
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        _shortcuts = ShortcutSettings.Resolve(shortcuts);
        foreach (var command in ShortcutSettings.Commands)
        {
            Commands.Add(new ShortcutRowModel(
                command.Id,
                Ui.Format(Ui.Settings.ShortcutCommandLabelFormat, UiCommandCatalog.MenuTitle(command.Menu), command.Label),
                ShortcutSettings.Format(command.Shortcut)));
        }

        AssignCommand = new RelayCommand(_ => SetSelected(_captured), _ => _selected is not null && _captured is not null);
        ClearCommand = new RelayCommand(_ => SetSelected(null), _ => _selected is not null);
        RestoreDefaultsCommand = new RelayCommand(_ =>
        {
            _shortcuts = ShortcutSettings.Resolve(null);
            RefreshRows();
            _changed(this);
        });

        RefreshRows();
    }

    public ObservableCollection<ShortcutRowModel> Commands { get; } = [];

    public ShortcutRowModel? SelectedCommand
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value)) return;
            _selected = value;
            Raise(nameof(SelectedCommand));

            // A chord pressed for one command is not carried over to the next.
            _captured = null;
            Raise(nameof(CapturedText));
            Message = Ui.Settings.ShortcutSelectCommandHint;
            RaiseCommands();
        }
    }

    /// <summary>The chord pressed into the capture box, as a menu would show it.</summary>
    public string CapturedText => _captured is null ? string.Empty : ShortcutSettings.Format(_captured);

    /// <summary>What the last action did, or what to do next.</summary>
    public string Message
    {
        get => _message;
        private set
        {
            if (string.Equals(_message, value, StringComparison.Ordinal)) return;
            _message = value;
            Raise(nameof(Message));
        }
    }

    public ICommand AssignCommand { get; }

    public ICommand ClearCommand { get; }

    public ICommand RestoreDefaultsCommand { get; }

    /// <summary>The table as it stands, for the working preferences.</summary>
    internal IReadOnlyDictionary<string, KeyGesture?> Shortcuts => new Dictionary<string, KeyGesture?>(_shortcuts, StringComparer.Ordinal);

    public static string CommandColumn => Ui.Settings.ShortcutColumnCommand;

    public static string ShortcutColumn => Ui.Settings.ShortcutColumnShortcut;

    public static string DefaultColumn => Ui.Settings.ShortcutColumnDefault;

    public static string AccessibleName => Ui.Settings.ShortcutsGridAccessibleName;

    public static string CaptureAccessibleName => Ui.Settings.ShortcutCaptureAccessibleName;

    public static string CapturePlaceholder => Ui.Settings.ShortcutCapturePlaceholder;

    public static string AssignLabel => Ui.Settings.ShortcutAssign;

    public static string ClearLabel => Ui.Settings.ShortcutClear;

    public static string RestoreDefaultsLabel => Ui.Settings.ShortcutRestoreDefaults;

    public static string MessageAccessibleName => Ui.Settings.ShortcutStatusAccessibleName;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Takes a chord pressed in the capture box. A modifier on its own is not a chord yet, so it
    /// leaves the box as it was until the key that completes it arrives.
    /// </summary>
    internal void Capture(KeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        if (gesture.Key is InputKey.None or InputKey.LeftCtrl or InputKey.RightCtrl or InputKey.LeftShift
            or InputKey.RightShift or InputKey.LeftAlt or InputKey.RightAlt or InputKey.LWin or InputKey.RWin)
        {
            return;
        }

        _captured = gesture;
        Raise(nameof(CapturedText));
        RaiseCommands();
    }

    private void SetSelected(KeyGesture? gesture)
    {
        if (_selected is not { } row) return;

        var candidate = new Dictionary<string, KeyGesture?>(_shortcuts, StringComparer.Ordinal) { [row.Id] = gesture };
        if (ShortcutSettings.Validate(candidate) is { } error)
        {
            Message = error;
            return;
        }

        _shortcuts = candidate;
        RefreshRows();
        Message = Ui.Settings.ShortcutUpdatedHint;
        _changed(this);
    }

    private void RefreshRows()
    {
        foreach (var row in Commands)
        {
            row.Shortcut = ShortcutSettings.Format(_shortcuts.GetValueOrDefault(row.Id));
        }
    }

    private void RaiseCommands()
    {
        (AssignCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ClearCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
