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
    private IReadOnlyList<string>? _suggestions;
    private string _value;
    private bool _isEnabled;
    private int _minimum;

    /// <param name="browse">
    /// Asks for a file, for a Path row. Null leaves Browse dim, which is what a test and a shell
    /// with no window to hang a picker on both want.
    /// </param>
    /// <param name="importKey">
    /// Enrols a key in the vault and answers with its reference, for a Secret row. Null leaves
    /// Import dim for the same reasons.
    /// </param>
    internal SettingsRowModel(
        SettingsRowDefinition definition,
        DesktopUpdatePreferences preferences,
        Action<SettingsRowModel> changed,
        Func<string, Task<string?>>? browse = null,
        Func<Task<string?>>? importKey = null)
    {
        _definition = definition;
        _value = definition.Read(preferences);
        _isEnabled = definition.Enabled?.Invoke(preferences) ?? true;
        _minimum = MinimumIn(preferences);
        _changed = changed;
        Choices = [.. definition.Choices.Select(choice => choice.Label)];
        BrowseCommand = new RelayCommand(
            _ => _ = BrowseAsync(browse!),
            _ => browse is not null && IsPath);
        ImportCommand = new RelayCommand(
            _ => _ = ImportAsync(importKey!),
            _ => importKey is not null && IsSecret);
        ClearCommand = new RelayCommand(_ => Text = string.Empty, _ => IsSecret);
    }

    internal SettingsRowDefinition Definition => _definition;

    public string Key => _definition.Key;

    public string Label => _definition.Label;

    /// <summary>The sentence under the label, which for some rows depends on what is chosen.</summary>
    public string Hint => _definition.HintFor?.Invoke(_value) ?? _definition.Hint ?? string.Empty;

    public bool HasHint => Hint.Length > 0;

    public bool HintIsWarning => _definition.HintIsWarning;

    /// <summary>
    /// Whether this is the first row in its card, which is the one without a hairline above it.
    /// </summary>
    public bool IsFirst { get; internal set; }

    public bool IsToggle => _definition.Kind == SettingsControlKind.Toggle;

    public bool IsChoice => _definition.Kind == SettingsControlKind.Choice;

    public bool IsNumber => _definition.Kind == SettingsControlKind.Number;

    public bool IsPath => _definition.Kind == SettingsControlKind.Path;

    public bool IsText => _definition.Kind == SettingsControlKind.Text;

    public bool IsEditableChoice => _definition.Kind == SettingsControlKind.EditableChoice;

    public bool IsSecret => _definition.Kind == SettingsControlKind.Secret;

    /// <summary>
    /// Whether the row can be changed now. Dimmed rows stay on screen, as 1.4 kept them, so what
    /// a setting would do is visible before whatever it depends on is turned on.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        private set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            Raise(nameof(IsEnabled));
        }
    }

    /// <summary>
    /// A Path, Text or editable choice row's text, exactly as typed.
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

    public string Placeholder => _definition.Placeholder ?? string.Empty;

    /// <summary>Why what is typed will not be kept, or empty.</summary>
    public string Problem => _definition.Validate?.Invoke(_value) ?? string.Empty;

    public bool HasProblem => Problem.Length > 0;

    public ICommand BrowseCommand { get; }

    public ICommand ImportCommand { get; }

    public ICommand ClearCommand { get; }

    public static string BrowseLabel => Ui.Settings.ButtonBrowse;

    public static string ImportLabel => Ui.Settings.ImportKey;

    public static string ClearLabel => Ui.Settings.ClearDefault;

    private async Task BrowseAsync(Func<string, Task<string?>> browse)
    {
        if (await browse(_definition.BrowseTitle ?? Label).ConfigureAwait(true) is { } chosen) Text = chosen;
    }

    private async Task ImportAsync(Func<Task<string?>> importKey)
    {
        if (await importKey().ConfigureAwait(true) is { } reference) Text = reference;
    }

    public IReadOnlyList<string> Choices { get; }

    /// <summary>What an editable choice offers, asked for the first time it is drawn.</summary>
    public IReadOnlyList<string> Suggestions => _suggestions ??= _definition.Suggestions?.Invoke() ?? [];

    /// <summary>The lowest the number goes, which for a maximum is wherever "Start with" is.</summary>
    public int Minimum
    {
        get => _minimum;
        private set
        {
            if (_minimum == value) return;
            _minimum = value;
            Raise(nameof(Minimum));
        }
    }

    public int Maximum => _definition.Maximum;

    public decimal Increment => _definition.Increment;

    public string FormatString => _definition.DecimalPlaces > 0
        ? "0." + new string('0', _definition.DecimalPlaces)
        : _definition.ThousandsSeparator ? "#,0" : "0";

    /// <summary>What the number counts, inside its field after the value.</summary>
    public string Unit => _definition.Unit ?? string.Empty;

    /// <summary>The most a text field takes, or 0 for no limit.</summary>
    public int MaxLength => _definition.MaxLength;

    /// <summary>The stored form. Everything below is a view onto this one string.</summary>
    internal string Value
    {
        get => _value;
        private set
        {
            if (string.Equals(_value, value, StringComparison.Ordinal)) return;
            _value = value;
            _changed(this);
            RaiseValue();
        }
    }

    /// <summary>
    /// Re-reads whether the row can be changed, how low it can go, and its value where an edit
    /// elsewhere moved it.
    /// </summary>
    /// <param name="before">The preferences before the edit.</param>
    /// <param name="after">The preferences after it, once the rows are back in agreement.</param>
    /// <param name="edited">Whether this is the row that was edited, whose value is left alone.</param>
    /// <remarks>
    /// <para>
    /// This is how a dimmed row follows what it depends on and how a mirrored value follows the one
    /// it mirrors: the operation timeout of a remote provider moves with its connection timeout,
    /// and raising "Start with" raises the maximums. A value is re-read only when its own
    /// preference moved, so a path that was typed and refused keeps its text and its Problem line
    /// while something else is changed, and what somebody is typing is never replaced under them.
    /// </para>
    /// <para>
    /// The value is taken before the floor is raised, so a field clamping itself to its new
    /// minimum finds the value already there rather than writing it back mid-edit.
    /// </para>
    /// </remarks>
    internal void Refresh(DesktopUpdatePreferences before, DesktopUpdatePreferences after, bool edited)
    {
        IsEnabled = _definition.Enabled?.Invoke(after) ?? true;
        var value = _definition.Read(after);
        var moved = !edited &&
            !string.Equals(_definition.Read(before), value, StringComparison.Ordinal) &&
            !string.Equals(_value, value, StringComparison.Ordinal);
        if (moved) _value = value;
        Minimum = MinimumIn(after);
        if (moved) RaiseValue();
    }

    private int MinimumIn(DesktopUpdatePreferences preferences) =>
        Math.Max(_definition.Minimum, _definition.MinimumFor?.Invoke(preferences) ?? _definition.Minimum);

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
        get => decimal.TryParse(_value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, Minimum, Maximum)
            : Minimum;
        set => Value = Math.Round(value, _definition.DecimalPlaces)
            .ToString(_definition.DecimalPlaces == 0 ? "0" : "0.#", CultureInfo.InvariantCulture);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RaiseValue()
    {
        Raise(nameof(Text));
        Raise(nameof(Problem));
        Raise(nameof(HasProblem));
        Raise(nameof(Hint));
        Raise(nameof(HasHint));
        Raise(nameof(IsOn));
        Raise(nameof(SelectedChoice));
        Raise(nameof(NumberValue));
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A card of rows under its caption.</summary>
internal sealed class SettingsGroupModel
{
    internal SettingsGroupModel(SettingsGroupDefinition definition, IReadOnlyList<SettingsRowModel> rows)
    {
        // Capitals are the caption's look rather than its words, so the translations stay in
        // sentence case and this is the one place they are raised.
        Caption = definition.Caption?.ToUpper(CultureInfo.CurrentCulture) ?? string.Empty;
        Note = definition.Note ?? string.Empty;
        NoteIsWarning = definition.NoteIsWarning;
        NoteIsTitle = definition.NoteIsTitle;
        Rows = rows;
        for (var index = 0; index < rows.Count; index++)
        {
            rows[index].IsFirst = index == 0;
        }
    }

    public string Caption { get; }

    public bool HasCaption => Caption.Length > 0;

    public IReadOnlyList<SettingsRowModel> Rows { get; }

    public string Note { get; }

    public bool HasNote => Note.Length > 0;

    public bool NoteIsWarning { get; }

    public bool NoteIsTitle { get; }
}

/// <summary>A page in the navigation list.</summary>
internal class SettingsPageModel
{
    private readonly SettingsPageDefinition _definition;

    internal SettingsPageModel(SettingsPageDefinition definition, IReadOnlyList<SettingsGroupModel> groups)
    {
        _definition = definition;
        Groups = groups;
    }

    internal string Key => _definition.Key;

    internal string? ParentKey => _definition.ParentKey;

    internal string? Group => _definition.Group;

    internal StorageProviderKind? Provider => _definition.Provider;

    /// <summary>What the navigation calls the page.</summary>
    public string Title => _definition.Title;

    /// <summary>The heading over the page, which can say more than the navigation does.</summary>
    public string Heading => _definition.Heading ?? _definition.Title;

    public string Description => _definition.Description;

    public LucideIconKind Icon => Themes.IconCatalog.Resolve(_definition.Glyph) ?? LucideIconKind.Settings;

    public IReadOnlyList<SettingsGroupModel> Groups { get; }

    public IReadOnlyList<SettingsRowModel> Rows => [.. Groups.SelectMany(group => group.Rows)];

    public string Footnote => _definition.Footnote ?? string.Empty;

    public bool HasFootnote => Footnote.Length > 0;

    /// <summary>
    /// "Create a FTP connection…" under a provider's defaults, which opens the Connection Manager
    /// on a new connection of that kind. Null where there is no provider or nowhere to open it.
    /// </summary>
    public ICommand? CreateConnectionCommand { get; internal set; }

    public bool HasCreateConnection => CreateConnectionCommand is not null;

    public string CreateConnectionLabel => Provider is { } provider
        ? Ui.Format(Ui.Settings.CreateProviderFormat, ConnectionProviderCatalog.Get(provider).DisplayName)
        : string.Empty;
}

/// <summary>
/// One line in the navigation: a page, or a caption over a group of pages.
/// </summary>
/// <remarks>
/// A list rather than a tree, because the tree 1.4 drew was never more than one level deep with
/// captions between, and a list keeps the keyboard, the selection and the accessibility that a
/// ListBox already has. Captions cannot be selected; the rows under them are the destinations.
/// </remarks>
internal sealed class SettingsNavigationEntry : INotifyPropertyChanged
{
    private readonly Action<SettingsNavigationEntry>? _toggle;
    private bool _isExpanded = true;

    private SettingsNavigationEntry(SettingsPageModel? page, string text, bool hasChildren, Action<SettingsNavigationEntry>? toggle)
    {
        Page = page;
        Text = text;
        HasChildren = hasChildren;
        _toggle = toggle;
        ToggleCommand = new RelayCommand(_ => _toggle?.Invoke(this), _ => HasChildren);
    }

    internal static SettingsNavigationEntry For(SettingsPageModel page, bool hasChildren, Action<SettingsNavigationEntry> toggle) =>
        new(page, page.Title, hasChildren, toggle);

    internal static SettingsNavigationEntry Caption(string text) =>
        new(null, text.ToUpper(CultureInfo.CurrentCulture), hasChildren: false, toggle: null);

    public SettingsPageModel? Page { get; }

    public string Text { get; }

    public LucideIconKind Icon => Page?.Icon ?? LucideIconKind.Settings;

    public bool IsCaption => Page is null;

    public bool IsSelectable => Page is not null;

    /// <summary>A page at the top of the list, drawn with its icon and in bold.</summary>
    public bool IsTopLevel => Page is { ParentKey: null };

    /// <summary>A page under another, drawn beside a guide line instead of an icon.</summary>
    public bool IsChild => Page is { ParentKey: not null };

    public bool HasChildren { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        internal set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChevronIcon)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToggleAccessibleName)));
        }
    }

    public LucideIconKind ChevronIcon => _isExpanded ? LucideIconKind.ChevronDown : LucideIconKind.ChevronRight;

    /// <summary>What the arrow does if pressed, since a screen reader cannot see which way it points.</summary>
    public string ToggleAccessibleName => Ui.Format(
        _isExpanded ? Ui.Settings.NavigationCollapseFormat : Ui.Settings.NavigationExpandFormat,
        Text);

    public ICommand ToggleCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>What the Background agent page reads and changes, so a test can stand in for the machine.</summary>
/// <param name="Current">How the agent is run now.</param>
/// <param name="Apply">
/// Changes it and says how that went. Null where this build cannot change it, which is 1.4's
/// answer everywhere but Windows.
/// </param>
internal sealed record AgentModeServices(
    Func<StorageHub.Agent.AgentHostMode> Current,
    Func<StorageHub.Agent.AgentHostMode, AgentModeChange>? Apply);

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
    private readonly Shell.IDialogService? _dialogs;
    private DesktopUpdatePreferences _working;
    private DesktopUpdatePreferences _saved;
    private int _selectedPage;

    /// <summary>Whether the screen shows a scheme or appearance that has not been saved.</summary>
    private bool _previewing;

    /// <param name="files">
    /// What a Path row's Browse asks. Null leaves Browse dim rather than failing.
    /// </param>
    /// <param name="importKey">What a key row's Import runs. Null leaves Import dim.</param>
    /// <param name="createConnection">
    /// Opens a new connection of a provider, from that provider's page. Null hides the button.
    /// </param>
    /// <param name="agent">What the Background agent page reads and changes.</param>
    /// <param name="dialogs">
    /// What says a save failed and asks whether to restart for a new language. A seam so a test can
    /// answer it: the rule worth testing is which saves are worth interrupting someone for, not the
    /// dialog. Null never asks, and a save that fails is left to the shell's own error handling.
    /// </param>
    internal SettingsModel(
        Func<DesktopUpdatePreferences> load,
        Action<DesktopUpdatePreferences> save,
        Action<DesktopUpdatePreferences>? preview = null,
        Shell.IFilePickerService? files = null,
        Func<Task<string?>>? importKey = null,
        Action<StorageProviderKind>? createConnection = null,
        AgentModeServices? agent = null,
        Shell.IDialogService? dialogs = null)
    {
        Func<string, Task<string?>>? browse = files is null
            ? null
            : title => files.PickFileAsync(new Shell.FilePickerRequest { Title = title });
        _load = load ?? throw new ArgumentNullException(nameof(load));
        _save = save ?? throw new ArgumentNullException(nameof(save));
        _preview = preview ?? (_ => { });
        _dialogs = dialogs;
        _saved = _load();
        _working = _saved;

        // One page per catalog entry, but not all of them are lists of rows: the toolbar, the
        // shortcuts and the agent each have a screen of their own, and a page model to go with it.
        Pages = [.. SettingsPageCatalog.Pages.Select(SettingsPageModel (page) => page.Key switch
        {
            SettingsPageCatalog.ToolbarPageKey =>
                new ToolbarPageModel(page, _working.ToolbarItems, _working.ToolbarLabels, OnToolbarChanged),
            SettingsPageCatalog.ShortcutsPageKey =>
                new ShortcutsPageModel(page, _working.Shortcuts, OnShortcutsChanged),
            SettingsPageCatalog.AgentPageKey =>
                new AgentPageModel(page, agent),
            _ => new SettingsPageModel(
                page,
                [.. page.Groups.Select(group => new SettingsGroupModel(
                    group,
                    [.. group.Rows.Select(row => new SettingsRowModel(row, _working, OnRowChanged, browse, importKey))]))])
        })];

        if (createConnection is not null)
        {
            foreach (var page in Pages.Where(page => page.Provider is not null))
            {
                var provider = page.Provider!.Value;
                page.CreateConnectionCommand = new RelayCommand(_ => createConnection(provider));
            }
        }

        RebuildNavigation();

        // Awaited rather than discarded, so a failure nothing here expects still reaches the
        // shell's error handling instead of vanishing with the task.
        ApplyCommand = new RelayCommand(async _ => await CommitAsync(close: false).ConfigureAwait(true), _ => IsDirty);
        SaveCommand = new RelayCommand(async _ => await CommitAsync(close: true).ConfigureAwait(true));
        CancelCommand = new RelayCommand(_ =>
        {
            Discard();
            Closed?.Invoke(this, false);
        });
    }

    /// <summary>Every page, in the order the navigation lists them.</summary>
    public ObservableCollection<SettingsPageModel> Pages { get; }

    /// <summary>What the navigation shows: the pages, with captions over the grouped ones.</summary>
    public ObservableCollection<SettingsNavigationEntry> Navigation { get; } = [];

    public static string Title => Ui.Settings.WindowTitle;

    public static string NavigationTitle => Ui.Settings.NavigationTitle;

    public static string NavigationAccessibleName => Ui.Settings.CategoriesAccessibleName;

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
            Raise(nameof(SelectedEntry));
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

    /// <summary>
    /// The navigation's selection. A caption cannot be chosen, and choosing nothing -- which the
    /// list does for a moment while it is rebuilt -- leaves the page where it was.
    /// </summary>
    public SettingsNavigationEntry? SelectedEntry
    {
        get => Navigation.FirstOrDefault(entry => ReferenceEquals(entry.Page, SelectedPageModel));
        set
        {
            if (value?.Page is { } page && Pages.IndexOf(page) is >= 0 and var index)
            {
                SelectedPage = index;
            }
            else
            {
                Raise(nameof(SelectedEntry));
            }
        }
    }

    public bool IsDirty => _working != _saved;

    public ICommand ApplyCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand CancelCommand { get; }

    /// <summary>
    /// Whether a restart was accepted for a new language. The shell acts on it once this window
    /// has closed; restarting from inside a modal window would tear down the one running the code.
    /// </summary>
    internal bool LanguageRestartRequested { get; private set; }

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

        // What is on screen is now what is saved, so there is no preview left to undo.
        _previewing = false;
        Raise(nameof(IsDirty));
        Raise(nameof(DirtyLabel));
        (ApplyCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Saves, offers the restart a new language needs, and closes after OK or an accepted restart.
    /// </summary>
    /// <remarks>
    /// A save that fails says so and goes no further, as 1.4's did: the window stays open with the
    /// edits in it, nothing is offered, and the file still holds what it held.
    /// </remarks>
    private async Task CommitAsync(bool close)
    {
        var before = _saved.Language;
        try
        {
            Apply();
        }
        catch (Exception error) when (_dialogs is not null &&
            error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            await _dialogs.ShowAsync(new Shell.DialogRequest
            {
                Title = Ui.Dialogs.SettingsCaption,
                Message = Ui.Dialogs.SettingsSaveFailed,
                Severity = Shell.DialogSeverity.Error,
                Buttons = Shell.DialogButtons.Ok
            }).ConfigureAwait(true);
            return;
        }

        if (await OfferLanguageRestartAsync(before).ConfigureAwait(true) || close)
        {
            Closed?.Invoke(this, true);
        }
    }

    /// <summary>
    /// Asks about restarting, but only when the save changed the language the shell would speak
    /// and that is not the one it already speaks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The comparison is between resolved cultures, not stored values, as 1.4 made it: on a Danish
    /// system, moving from "Same as the system" to Danish writes a different setting and changes
    /// nothing visible. Resolving both sides through <see cref="DesktopCulture.ResolveCurrent"/>
    /// also honours STORAGEHUB_LANGUAGE, so a launch pinned by the environment is never offered a
    /// restart that would change nothing.
    /// </para>
    /// <para>
    /// The language on screen is checked as well, which 1.4 did not: choosing Danish, declining,
    /// and later going back to English would otherwise offer a restart into the English already
    /// showing.
    /// </para>
    /// <para>
    /// Declining is not a failure. The setting is saved either way and takes effect at the next
    /// launch, which is what happened before there was a prompt at all.
    /// </para>
    /// </remarks>
    /// <returns>Whether the restart was accepted.</returns>
    private async Task<bool> OfferLanguageRestartAsync(string before)
    {
        var next = DesktopCulture.ResolveCurrent(_saved.Language);
        if (_dialogs is null ||
            string.Equals(DesktopCulture.ResolveCurrent(before), next, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Ui.Culture.Name, next, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Named in itself, so it is recognised whatever language is on screen.
        var answer = await _dialogs.ConfirmAsync(new Shell.DialogRequest
        {
            Title = Ui.Dialogs.LanguageRestartCaption,
            Message = Ui.Format(Ui.Dialogs.LanguageRestartPromptFormat, DesktopCulture.Describe(next)),
            Severity = Shell.DialogSeverity.Question,
            Buttons = Shell.DialogButtons.YesNo
        }).ConfigureAwait(true);
        if (answer != Shell.DialogChoice.Yes)
        {
            return false;
        }

        LanguageRestartRequested = true;
        return true;
    }

    /// <summary>
    /// Puts back whatever the live preview changed, so a scheme that was tried and not kept goes.
    /// </summary>
    /// <remarks>
    /// Cancel runs it, and so does the window however else it closes, the title bar's X or Alt+F4,
    /// as 1.4 undid the preview on every close that was not OK. It does nothing when there is
    /// nothing to undo, so running it twice repaints nothing.
    /// </remarks>
    internal void Discard()
    {
        if (!_previewing) return;
        _previewing = false;
        _preview(_saved);
    }

    /// <summary>
    /// Folds or opens the navigation from the keyboard, as Left and Right did in 1.4's tree.
    /// </summary>
    /// <remarks>
    /// On a page with others under it, Left folds them away and Right opens them. On a page under
    /// another, Left goes up to that page, so a second Left then folds the group.
    /// </remarks>
    /// <returns>Whether the key did something, so the list does not act on it as well.</returns>
    internal bool Fold(bool open)
    {
        if (SelectedEntry is not { Page: { } page } entry)
        {
            return false;
        }

        if (entry.HasChildren)
        {
            if (entry.IsExpanded == open) return false;
            ToggleExpanded(entry);
            return true;
        }

        if (open || page.ParentKey is not { } parentKey)
        {
            return false;
        }

        SelectPage(parentKey);
        return true;
    }

    /// <summary>
    /// The navigation list: every top-level page, and under an expanded one its children with
    /// a caption over each group, as 1.4's rail had Storage and Clients under Connections &amp; trust.
    /// </summary>
    private void RebuildNavigation()
    {
        var expanded = Navigation
            .Where(entry => entry.HasChildren)
            .ToDictionary(entry => entry.Page!.Key, entry => entry.IsExpanded, StringComparer.Ordinal);
        Navigation.Clear();

        foreach (var page in Pages.Where(page => page.ParentKey is null))
        {
            var children = Pages.Where(child => string.Equals(child.ParentKey, page.Key, StringComparison.Ordinal)).ToList();
            var entry = SettingsNavigationEntry.For(page, children.Count > 0, ToggleExpanded);
            entry.IsExpanded = expanded.GetValueOrDefault(page.Key, true);
            Navigation.Add(entry);
            if (!entry.IsExpanded) continue;

            string? group = null;
            foreach (var child in children)
            {
                if (child.Group is { } caption && !string.Equals(caption, group, StringComparison.Ordinal))
                {
                    Navigation.Add(SettingsNavigationEntry.Caption(caption));
                    group = caption;
                }

                Navigation.Add(SettingsNavigationEntry.For(child, hasChildren: false, ToggleExpanded));
            }
        }

        Raise(nameof(SelectedEntry));
    }

    /// <summary>
    /// Folds a group away or opens it. Folding away the group the page on screen belongs to moves
    /// the selection up to its parent, as a tree does, rather than leaving nothing selected.
    /// </summary>
    private void ToggleExpanded(SettingsNavigationEntry entry)
    {
        if (entry.Page is not { } parent) return;
        entry.IsExpanded = !entry.IsExpanded;
        if (!entry.IsExpanded && string.Equals(SelectedPageModel.ParentKey, parent.Key, StringComparison.Ordinal))
        {
            SelectedPage = Pages.IndexOf(parent);
        }

        RebuildNavigation();
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

        RaiseDirty();
    }

    /// <summary>
    /// Takes the whole shortcut table into the working copy, as 1.4 stored it: every command's
    /// binding, not only the ones that differ, so the file reads the same as the page did.
    /// </summary>
    private void OnShortcutsChanged(ShortcutsPageModel page)
    {
        _working = _working with { Shortcuts = page.Shortcuts };
        RaiseDirty();
    }

    private void OnRowChanged(SettingsRowModel row)
    {
        var before = _working;
        _working = SettingsPageCatalog.Settle(row.Definition.Write(_working, row.Value));

        // Rows that depend on this one follow it: dimming, mirrored values, raised maximums.
        foreach (var each in Pages.SelectMany(page => page.Rows))
        {
            each.Refresh(before, _working, edited: ReferenceEquals(each, row));
        }

        // Colour is the one setting nobody can evaluate from a label, so it previews as it is
        // chosen. Everything else waits for Apply, which is what lets Cancel undo it.
        if (string.Equals(row.Key, "color-scheme", StringComparison.Ordinal) ||
            string.Equals(row.Key, "appearance", StringComparison.Ordinal))
        {
            _preview(_working);
            _previewing = true;
        }

        RaiseDirty();
    }

    private void RaiseDirty()
    {
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
