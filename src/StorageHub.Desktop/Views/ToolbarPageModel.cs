using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Settings;

namespace StorageHub.Desktop.Views;

/// <summary>
/// Arranges the main toolbar: which commands appear, in what order, and whether they are labelled.
/// </summary>
/// <remarks>
/// <para>
/// The settings page whose editor is a screen rather than a list of rows, so it is a page model of
/// its own with its own template. The rules live in <see cref="ToolbarEditor"/>; what is here is
/// the two lists, the buttons between them, and which row is selected in each.
/// </para>
/// <para>
/// Edits reach the working preferences through the same callback the ordinary rows use, so the
/// toolbar marks the window unsaved and is written by Apply exactly as every other setting is.
/// </para>
/// </remarks>
internal sealed class ToolbarPageModel : SettingsPageModel, INotifyPropertyChanged
{
    private readonly ToolbarEditor _editor;
    private readonly Action<ToolbarPageModel> _changed;
    private int _selectedAvailable = -1;
    private int _selectedCurrent = -1;
    private int _selectedLabelStyle;

    internal ToolbarPageModel(
        SettingsPageDefinition definition,
        IReadOnlyList<string>? items,
        ToolbarLabelStyle labels,
        Action<ToolbarPageModel> changed)
        : base(definition, [])
    {
        _editor = new ToolbarEditor(items);
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        _selectedLabelStyle = (int)labels;

        AddCommand = new RelayCommand(_ => AddSelected(), _ => SelectedAvailable >= 0);
        RemoveCommand = new RelayCommand(_ => RemoveSelected(), _ => SelectedCurrent >= 0);
        AddSeparatorCommand = new RelayCommand(_ => AddSeparator());
        MoveUpCommand = new RelayCommand(_ => MoveSelected(-1), _ => SelectedCurrent > 0);
        MoveDownCommand = new RelayCommand(
            _ => MoveSelected(1), _ => SelectedCurrent >= 0 && SelectedCurrent < Current.Count - 1);
        ResetEssentialCommand = new RelayCommand(_ => Reset(ToolbarPreset.Essential));
        ResetExpandedCommand = new RelayCommand(_ => Reset(ToolbarPreset.Expanded));

        Refresh(keepCurrentAt: -1);
    }

    /// <summary>Everything not already on the toolbar.</summary>
    public ObservableCollection<ToolbarCommandChoice> Available { get; } = [];

    /// <summary>What is on the toolbar, in order and in words.</summary>
    public ObservableCollection<string> Current { get; } = [];

    public static IReadOnlyList<string> LabelStyles { get; } =
    [
        Ui.Settings.ToolbarLabelsIconsOnly,
        Ui.Settings.ToolbarLabelsIconsAndText,
        Ui.Settings.ToolbarLabelsTextUnderIcon
    ];

    public int SelectedAvailable
    {
        get => _selectedAvailable;
        set
        {
            if (_selectedAvailable == value) return;
            _selectedAvailable = value;
            Raise(nameof(SelectedAvailable));
            (AddCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public int SelectedCurrent
    {
        get => _selectedCurrent;
        set
        {
            if (_selectedCurrent == value) return;
            _selectedCurrent = value;
            Raise(nameof(SelectedCurrent));
            RaiseCurrentCommands();
        }
    }

    public int SelectedLabelStyle
    {
        get => _selectedLabelStyle;
        set
        {
            if (_selectedLabelStyle == value || value < 0 || value >= LabelStyles.Count) return;
            _selectedLabelStyle = value;
            Raise(nameof(SelectedLabelStyle));
            _changed(this);
        }
    }

    /// <summary>The layout to store.</summary>
    internal IReadOnlyList<string> Items => _editor.Items;

    /// <summary>The label style to store.</summary>
    internal ToolbarLabelStyle Labels => (ToolbarLabelStyle)_selectedLabelStyle;

    public ICommand AddCommand { get; }

    public ICommand RemoveCommand { get; }

    public ICommand AddSeparatorCommand { get; }

    public ICommand MoveUpCommand { get; }

    public ICommand MoveDownCommand { get; }

    public ICommand ResetEssentialCommand { get; }

    public ICommand ResetExpandedCommand { get; }

    /// <summary>Adds the selected command after the selected toolbar entry, or at the end.</summary>
    internal void AddSelected()
    {
        if (SelectedAvailable < 0 || SelectedAvailable >= Available.Count) return;
        var landing = Landing();
        if (!_editor.Add(Available[SelectedAvailable].Id, SelectedCurrent)) return;
        Mutate(landing);
    }

    internal void AddSeparator()
    {
        var landing = Landing();
        if (!_editor.AddSeparator(SelectedCurrent)) return;
        Mutate(landing);
    }

    internal void RemoveSelected()
    {
        var at = SelectedCurrent;
        if (!_editor.RemoveAt(at)) return;
        // The row under the one just removed, or the new last row when it was the last.
        Mutate(Math.Min(at, _editor.Items.Count - 1));
    }

    internal void MoveSelected(int delta)
    {
        var landed = _editor.Move(SelectedCurrent, delta);
        if (landed < 0) return;
        Mutate(landed);
    }

    internal void Reset(ToolbarPreset preset)
    {
        if (!_editor.Reset(preset)) return;
        Mutate(-1);
    }

    /// <summary>
    /// Where a new entry will land, worked out before the insert rather than after.
    /// </summary>
    /// <remarks>
    /// It has to be, because the selection is what decides it and the insert is what invalidates
    /// the selection. This mirrors the rule in <see cref="ToolbarEditor.Add"/>: after the selected
    /// entry, or at the end when nothing is selected.
    /// </remarks>
    private int Landing() =>
        SelectedCurrent >= 0 && SelectedCurrent < _editor.Items.Count
            ? SelectedCurrent + 1
            : _editor.Items.Count;

    /// <summary>
    /// Rebuilds both lists and tells the window something changed.
    /// </summary>
    /// <remarks>
    /// The selection is restored by index rather than kept, because adding or removing an entry
    /// renumbers the list under it. The caller says where it should land, since only the operation
    /// knows -- a move follows the entry, a removal stays where it was.
    /// </remarks>
    private void Mutate(int landing)
    {
        Refresh(landing);
        _changed(this);
    }

    private void Refresh(int keepCurrentAt)
    {
        var availableAt = SelectedAvailable;

        Available.Clear();
        foreach (var choice in _editor.Available()) Available.Add(choice);

        Current.Clear();
        foreach (var entry in _editor.Describe()) Current.Add(entry);

        // Clamped rather than cleared: adding a command shortens the available list by one, and
        // dropping the selection every time would make adding several in a row a chore.
        SelectedAvailable = Available.Count == 0 ? -1 : Math.Clamp(availableAt, -1, Available.Count - 1);
        SelectedCurrent = Current.Count == 0 ? -1 : Math.Clamp(keepCurrentAt, -1, Current.Count - 1);
        RaiseCurrentCommands();
    }

    private void RaiseCurrentCommands()
    {
        (RemoveCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (MoveUpCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (MoveDownCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public static string AvailableCaption => Ui.Settings.ToolbarAvailable;

    public static string CurrentCaption => Ui.Settings.ToolbarCurrent;

    public static string AvailableAccessibleName => Ui.Settings.ToolbarAvailableAccessibleName;

    public static string CurrentAccessibleName => Ui.Settings.ToolbarCurrentAccessibleName;

    public static string AddLabel => Ui.Settings.ToolbarAdd;

    public static string RemoveLabel => Ui.Settings.ToolbarRemove;

    public static string AddSeparatorLabel => Ui.Settings.ToolbarAddSeparator;

    public static string MoveUpLabel => Ui.Settings.ToolbarMoveUp;

    public static string MoveDownLabel => Ui.Settings.ToolbarMoveDown;

    public static string LabelsCaption => Ui.Settings.ToolbarLabels;

    public static string LabelsAccessibleName => Ui.Settings.ToolbarLabelsAccessibleName;

    public static string ResetEssentialLabel => Ui.Settings.ToolbarResetEssential;

    public static string ResetExpandedLabel => Ui.Settings.ToolbarResetExpanded;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
