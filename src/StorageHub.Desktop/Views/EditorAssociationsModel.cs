using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using StorageHub.Desktop.Configuration;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

internal sealed record EditorAssociationRow(string Extension, string Editor, ICommand RemoveCommand);

internal sealed class EditorAssociationsModel : INotifyPropertyChanged
{
    private readonly Action<IReadOnlyDictionary<string, string>> _changed;
    private readonly Func<string, Task<string?>>? _browse;
    private string _extensions = string.Empty;
    private string _editor = string.Empty;
    private readonly RelayCommand _add;

    internal EditorAssociationsModel(IReadOnlyDictionary<string, string>? rules,
        Action<IReadOnlyDictionary<string, string>> changed, Func<string, Task<string?>>? browse)
    {
        _changed = changed;
        _browse = browse;
        _add = new RelayCommand(_ => Add(), _ => Valid);
        BrowseCommand = new RelayCommand(async _ =>
        {
            if (await _browse!(Ui.Settings.ChooseEditorTitle).ConfigureAwait(true) is { } path) Editor = path;
        }, _ => _browse is not null);
        foreach (var pair in EditorAssociations.Normalize(rules)) AddRow(pair.Key, pair.Value);
    }

    public ObservableCollection<EditorAssociationRow> Rows { get; } = [];
    public string Extensions { get => _extensions; set { _extensions = value ?? string.Empty; Refresh(); } }
    public string Editor { get => _editor; set { _editor = value ?? string.Empty; Refresh(); } }
    public ICommand AddCommand => _add;
    public ICommand BrowseCommand { get; }
    public static string Heading => Ui.Settings.FileTypeEditors;
    public static string Hint => Ui.Settings.FileTypeEditorsHint;
    public static string ExtensionsPlaceholder => Ui.Settings.EditorExtensionsPlaceholder;
    public static string EditorPlaceholder => Ui.Settings.EditorPlaceholder;
    public static string BrowseLabel => Ui.Settings.ButtonBrowse;
    public static string AddLabel => Ui.Settings.AddEditorRule;
    public static string RemoveLabel => Ui.Settings.RemoveEditorRule;
    public string Problem => _extensions.Length == 0 && _editor.Length == 0 || Valid ? string.Empty : Ui.Settings.EditorRuleInvalid;
    private string[] Parts => _extensions.Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private bool Valid => Parts.Length > 0 && Parts.All(part => EditorAssociations.NormalizeExtension(part) is not null) &&
        !string.IsNullOrWhiteSpace(_editor) && DesktopConfigRepair.IsValidEditorPath(_editor.Trim()) &&
        Rows.Select(row => row.Extension).Concat(Parts.Select(part => EditorAssociations.NormalizeExtension(part)!))
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() <= EditorAssociations.MaximumRules;

    private void Add()
    {
        if (!Valid) return;
        foreach (var part in Parts)
        {
            var extension = EditorAssociations.NormalizeExtension(part)!;
            if (Rows.FirstOrDefault(row => row.Extension == extension) is { } existing) Rows.Remove(existing);
            AddRow(extension, _editor.Trim());
        }
        Extensions = string.Empty;
        Changed();
    }

    private void AddRow(string extension, string editor) => Rows.Add(new EditorAssociationRow(extension, editor,
        new RelayCommand(_ => { Rows.Remove(Rows.Single(row => row.Extension == extension)); Changed(); })));

    private void Changed() => _changed(Rows.ToDictionary(row => row.Extension, row => row.Editor, StringComparer.OrdinalIgnoreCase));
    private void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        _add.RaiseCanExecuteChanged();
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
