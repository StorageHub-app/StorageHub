using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>What the batch rename window shows, and the plan it answers with.</summary>
internal sealed class BatchRenameModel : INotifyPropertyChanged
{
    private readonly IReadOnlyList<string> _sources;
    private readonly IReadOnlyList<string> _occupied;
    private string _find = string.Empty;
    private string _replace = string.Empty;
    private BatchRenamePlan _plan;

    internal BatchRenameModel(IReadOnlyList<string> sources, IReadOnlyList<string> occupied)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _occupied = occupied ?? throw new ArgumentNullException(nameof(occupied));
        _plan = BatchRenamePlan.Build(_sources, _occupied, _find, _replace);
        // Every line, unchanged ones included, so the pane can pair them with the selection by
        // position: a folder can hold two entries with one name, a prefix and an object.
        RenameCommand = new RelayCommand(_ => Finish(_plan.Lines), _ => _plan.CanApply);
        CancelCommand = new RelayCommand(_ => Finish(null));
        Show();
    }

    public string Find
    {
        get => _find;
        set
        {
            _find = value ?? string.Empty;
            Rebuild();
        }
    }

    public string Replace
    {
        get => _replace;
        set
        {
            _replace = value ?? string.Empty;
            Rebuild();
        }
    }

    public ObservableCollection<string> Lines { get; } = [];

    public string Summary => _plan.Summary;

    public bool HasProblem => !_plan.CanApply;

    public ICommand RenameCommand { get; }

    public ICommand CancelCommand { get; }

    /// <summary>One line per selected name, in order, or null when the window was dismissed.</summary>
    internal IReadOnlyList<BatchRenameLine>? Result { get; private set; }

    internal event EventHandler? Closed;

    private void Rebuild()
    {
        _plan = BatchRenamePlan.Build(_sources, _occupied, _find, _replace);
        Show();
        (RenameCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void Show()
    {
        Lines.Clear();
        foreach (var line in _plan.Lines) Lines.Add(line.Text);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasProblem)));
    }

    private void Finish(IReadOnlyList<BatchRenameLine>? result)
    {
        Result = result;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    public static string Title => Ui.Shell.BatchRenameTitle;

    public static string FindLabel => Ui.Shell.BatchRenameFind;

    public static string ReplaceLabel => Ui.Shell.BatchRenameReplaceWith;

    public static string FindAccessibleName => Ui.Shell.BatchRenameFindAccessibleName;

    public static string ReplaceAccessibleName => Ui.Shell.BatchRenameReplaceAccessibleName;

    public static string PreviewAccessibleName => Ui.Shell.BatchRenamePreviewAccessibleName;

    public static string RenameLabel => Ui.Shell.RenameWorkspaceAccept;

    public static string CancelLabel => Ui.Dialogs.ButtonCancel;

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Find and replace across several selected names, previewed before anything changes.</summary>
public partial class BatchRenameWindow : Window
{
    public BatchRenameWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is BatchRenameModel model) model.Closed += (_, _) => Close();
        };
        Opened += (_, _) => this.FindControl<TextBox>("PART_Find")?.Focus();
    }

    /// <summary>The renames to make, asked over a window, or null when dismissed.</summary>
    internal static async Task<IReadOnlyList<BatchRenameLine>?> AskAsync(
        Window? owner, IReadOnlyList<string> sources, IReadOnlyList<string> occupied)
    {
        if (owner is null) return null;
        var model = new BatchRenameModel(sources, occupied);
        await new BatchRenameWindow { DataContext = model }.ShowDialog(owner).ConfigureAwait(true);
        return model.Result;
    }
}
