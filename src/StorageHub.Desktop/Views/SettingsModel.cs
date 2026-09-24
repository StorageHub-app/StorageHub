using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using Lucide.Avalonia;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Settings;
using StorageHub.Desktop.Themes;

namespace StorageHub.Desktop.Views;

/// <summary>One editable setting, bound by the one template that serves every kind.</summary>
internal sealed class SettingsRowModel : INotifyPropertyChanged
{
    private readonly SettingsRowDefinition _definition;
    private readonly Action<SettingsRowModel> _changed;
    private string _value;

    /// <param name="browse">
    /// Asks for a file, for a Path row. Null leaves Browse dim, which is what a test and a shell
    /// with no window to hang a picker on both want.
    /// </param>
    internal SettingsRowModel(
        SettingsRowDefinition definition,
        string value,
        Action<SettingsRowModel> changed,
        Func<string, Task<string?>>? browse = null)
    {
        _definition = definition;
        _value = value;
        _changed = changed;
        Choices = [.. definition.Choices.Select(choice => choice.Label)];
        BrowseCommand = new RelayCommand(
            _ => _ = BrowseAsync(browse!),
            _ => browse is not null && IsPath);
    }

    internal SettingsRowDefinition Definition => _definition;

    public string Key => _definition.Key;

    public string Label => _definition.Label;

    public string Hint => _definition.Hint ?? string.Empty;

    public bool HasHint => !string.IsNullOrWhiteSpace(_definition.Hint);

    public bool IsToggle => _definition.Kind == SettingsControlKind.Toggle;

    public bool IsChoice => _definition.Kind == SettingsControlKind.Choice;

    public bool IsNumber => _definition.Kind == SettingsControlKind.Number;

    public bool IsPath => _definition.Kind == SettingsControlKind.Path;

    /// <summary>
    /// A Path row's text, exactly as typed.
    /// </summary>
    /// <remarks>
    /// Kept as typed even when it will not be saved, so a half-typed path is not snatched away
    /// between keystrokes. What is kept is decided by the row's Write, and <see cref="Problem"/>
    /// says when that is not what is on screen.
    /// </remarks>
    public string Text
    {
        get => _value;
        set => Value = value ?? string.Empty;
    }

    /// <summary>Why what is typed will not be kept, or empty.</summary>
    public string Problem => _definition.Validate?.Invoke(_value) ?? string.Empty;

    public bool HasProblem => Problem.Length > 0;

    public ICommand BrowseCommand { get; }

    public static string BrowseLabel => Ui.Settings.ButtonBrowse;

    private async Task BrowseAsync(Func<string, Task<string?>> browse)
    {
        if (await browse(_definition.BrowseTitle ?? Label).ConfigureAwait(true) is { } chosen) Text = chosen;
    }

    public IReadOnlyList<string> Choices { get; }

    public int Minimum => _definition.Minimum;

    public int Maximum => _definition.Maximum;

    /// <summary>The stored form. Everything below is a view onto this one string.</summary>
    internal string Value
    {
        get => _value;
        private set
        {
            if (string.Equals(_value, value, StringComparison.Ordinal)) return;
            _value = value;
            _changed(this);
            Raise(nameof(Text));
            Raise(nameof(Problem));
            Raise(nameof(HasProblem));
            Raise(nameof(IsOn));
            Raise(nameof(SelectedChoice));
            Raise(nameof(NumberValue));
        }
    }

    public bool IsOn
    {
        get => SettingsPageCatalog.Flag(_value);
        set => Value = SettingsPageCatalog.Text(value);
    }

    /// <summary>
    /// The index rather than the label, because two schemes could one day share a display name and
    /// a ComboBox bound to a string would then select the wrong one.
    /// </summary>
    public int SelectedChoice
    {
        get
        {
            for (var index = 0; index < _definition.Choices.Count; index++)
            {
                if (string.Equals(_definition.Choices[index].Value, _value, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return _definition.Choices.Count == 0 ? -1 : 0;
        }
        set
        {
            if (value >= 0 && value < _definition.Choices.Count)
            {
                Value = _definition.Choices[value].Value;
            }
        }
    }

    public decimal NumberValue
    {
        get => SettingsPageCatalog.Number(_value, Minimum, Maximum, Minimum);
        set => Value = ((int)value).ToString(CultureInfo.InvariantCulture);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A page in the navigation list.</summary>
internal class SettingsPageModel(SettingsPageDefinition definition, IReadOnlyList<SettingsRowModel> rows)
{
    public string Title => definition.Title;

    public string Description => definition.Description;

    public LucideIconKind Icon => Themes.IconCatalog.Resolve(definition.Glyph) ?? LucideIconKind.Settings;

    public IReadOnlyList<SettingsRowModel> Rows => rows;
}

/// <summary>
/// The Settings window: the pages, the pending edits, and what saving them does.
/// </summary>
/// <remarks>
/// <para>
/// Edits land in a working copy of the preferences and are written once, which is what makes
/// Cancel mean something. The colour scheme is the exception: it previews live, because a scheme
/// chosen from a list of twenty-two cannot be judged from its name.
/// </para>
/// <para>
/// The store is injected so the headless tests can drive the whole window against a temporary
/// directory rather than the real settings file.
/// </para>
/// </remarks>
internal sealed class SettingsModel : INotifyPropertyChanged
{
    private readonly Func<DesktopUpdatePreferences> _load;
    private readonly Action<DesktopUpdatePreferences> _save;
    private readonly Action<DesktopUpdatePreferences> _preview;
    private DesktopUpdatePreferences _working;
    private DesktopUpdatePreferences _saved;
    private int _selectedPage;

    /// <param name="files">
    /// What a Path row's Browse asks. Null leaves Browse dim rather than failing.
    /// </param>
    internal SettingsModel(
        Func<DesktopUpdatePreferences> load,
        Action<DesktopUpdatePreferences> save,
        Action<DesktopUpdatePreferences>? preview = null,
        Shell.IFilePickerService? files = null)
    {
        Func<string, Task<string?>>? browse = files is null
            ? null
            : title => files.PickFileAsync(new Shell.FilePickerRequest { Title = title });
        _load = load ?? throw new ArgumentNullException(nameof(load));
        _save = save ?? throw new ArgumentNullException(nameof(save));
        _preview = preview ?? (_ => { });
        _saved = _load();
        _working = _saved;

        // One page per catalog entry, but not all of them are lists of rows: the toolbar is
        // arranged with two lists and five buttons, and gets a page model of its own.
        Pages = [.. SettingsPageCatalog.Pages.Select(SettingsPageModel (page) =>
            string.Equals(page.Key, SettingsPageCatalog.ToolbarPageKey, StringComparison.Ordinal)
                ? new ToolbarPageModel(page, _working.ToolbarItems, _working.ToolbarLabels, OnToolbarChanged)
                : new SettingsPageModel(
                    page,
                    [.. page.Rows.Select(row => new SettingsRowModel(row, row.Read(_working), OnRowChanged, browse))]))];

        ApplyCommand = new RelayCommand(_ => Apply(), _ => IsDirty);
        SaveCommand = new RelayCommand(_ => { Apply(); Closed?.Invoke(this, true); });
        CancelCommand = new RelayCommand(_ =>
        {
            // Put back whatever the live preview changed, so cancelling a scheme cancels it.
            _preview(_saved);
            Closed?.Invoke(this, false);
        });
    }

    public ObservableCollection<SettingsPageModel> Pages { get; }

    public static string Title => Ui.Settings.WindowTitle;

    public static string NavigationTitle => Ui.Settings.NavigationTitle;

    public static string SaveLabel => Ui.Dialogs.ButtonOk;

    public static string ApplyLabel => Ui.Dialogs.ButtonApply;

    public static string CancelLabel => Ui.Dialogs.ButtonCancel;

    public string DirtyLabel => IsDirty ? Ui.Settings.SettingsUnsaved : string.Empty;

    public int SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (_selectedPage == value) return;
            _selectedPage = value;
            Raise(nameof(SelectedPage));
            Raise(nameof(SelectedPageModel));
        }
    }

    /// <summary>
    /// The page on screen, which is whichever one the navigation has selected.
    /// </summary>
    /// <remarks>
    /// An ordinary property rather than an expression in the view, because "the selected item of
    /// that list" is not something a compiled binding can say without a converter. The window used
    /// to wire the two together in code-behind instead, from DataContextChanged -- which runs
    /// before the visual tree exists, so the list it went looking for was never there and choosing
    /// a category did nothing at all.
    /// </remarks>
    public SettingsPageModel SelectedPageModel =>
        Pages[Math.Clamp(_selectedPage, 0, Pages.Count - 1)];

    public bool IsDirty => _working != _saved;

    public ICommand ApplyCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand CancelCommand { get; }

    /// <summary>Raised with true when the edits were kept.</summary>
    internal event EventHandler<bool>? Closed;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Opens on the page with this catalog key, or leaves the selection alone if there is none.</summary>
    internal void SelectPage(string key)
    {
        var index = SettingsPageCatalog.Pages
            .Select((page, position) => (page.Key, position))
            .FirstOrDefault(entry => string.Equals(entry.Key, key, StringComparison.Ordinal), (Key: string.Empty, position: -1))
            .position;
        if (index >= 0)
        {
            SelectedPage = index;
        }
    }

    /// <summary>What would be written. Exposed so a test can assert without saving.</summary>
    internal DesktopUpdatePreferences Working => _working;

    internal void Apply()
    {
        if (!IsDirty)
        {
            return;
        }

        _save(_working);
        _saved = _working;
        Raise(nameof(IsDirty));
        Raise(nameof(DirtyLabel));
        (ApplyCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Takes the toolbar's layout and label style into the working copy.
    /// </summary>
    /// <remarks>
    /// Sanitised on the way in, so what is stored is what the toolbar will actually render: a
    /// layout that had been left with a trailing divider would otherwise come back changed on the
    /// next load and look like the edit had not been saved.
    /// </remarks>
    private void OnToolbarChanged(ToolbarPageModel page)
    {
        _working = _working with
        {
            ToolbarItems = [.. ToolbarLayout.Sanitise(page.Items)],
            ToolbarLabels = page.Labels
        };

        Raise(nameof(IsDirty));
        Raise(nameof(DirtyLabel));
        (ApplyCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void OnRowChanged(SettingsRowModel row)
    {
        _working = row.Definition.Write(_working, row.Value);

        // Colour is the one setting nobody can evaluate from a label, so it previews as it is
        // chosen. Everything else waits for Apply, which is what lets Cancel undo it.
        if (string.Equals(row.Key, "color-scheme", StringComparison.Ordinal) ||
            string.Equals(row.Key, "appearance", StringComparison.Ordinal))
        {
            _preview(_working);
        }

        Raise(nameof(IsDirty));
        Raise(nameof(DirtyLabel));
        (ApplyCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// The smallest ICommand that does the job.
/// </summary>
/// <remarks>
/// Hand-written rather than taken from a toolkit: the shell has no MVVM framework, adding one to
/// get a command object would be a dependency for twenty lines, and this is those twenty lines.
/// </remarks>
internal sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => execute(parameter);

    internal void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
