using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>One file in the preview: where it lands, what is there, and what will be done.</summary>
internal sealed class TransferPreviewRowModel : INotifyPropertyChanged
{
    private TransferConflictChoice _choice;

    internal TransferPreviewRowModel(TransferPreviewRow row, TransferConflictChoice choice)
    {
        Row = row;
        _choice = choice;
        SourceSize = TransferConflictModel.SizeText(row.Source.Length);
        SourceModified = TransferConflictModel.ModifiedText(row.Source.ModifiedUtc);
        TargetSize = row.Existing is { } existing ? TransferConflictModel.SizeText(existing.Length) : string.Empty;
        TargetModified = row.Existing is { } there ? TransferConflictModel.ModifiedText(there.ModifiedUtc) : string.Empty;
    }

    internal TransferPreviewRow Row { get; }

    public string Path => Row.DestinationPath;

    public bool IsConflict => Row.IsConflict;

    public bool IsNew => !Row.IsConflict;

    public string SourceSize { get; }

    public string SourceModified { get; }

    public string TargetSize { get; }

    public string TargetModified { get; }

    /// <summary>The choices a conflicting row offers, labelled as the file-exists window labels them.</summary>
    public static IReadOnlyList<string> ChoiceLabels { get; } =
        [.. TransferConflictText.All.Select(TransferConflictText.Label)];

    public TransferConflictChoice Choice
    {
        get => _choice;
        set
        {
            if (_choice == value) return;
            _choice = value;
            Raise(nameof(Choice));
            Raise(nameof(ChoiceIndex));
            Raise(nameof(ActionText));
            Raise(nameof(IsReplace));
            Raise(nameof(IsSkip));
            ChoiceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The choice as a position in <see cref="ChoiceLabels"/>, for the row's drop-down.</summary>
    public int ChoiceIndex
    {
        get => Math.Max(0, IndexOf(_choice));
        set
        {
            if (value >= 0 && value < TransferConflictText.All.Count) Choice = TransferConflictText.All[value];
        }
    }

    internal TransferPreviewAction Action => Row.ActionFor(_choice);

    /// <summary>What happens to the file, in a word: New, Replace, Skip, Rename.</summary>
    public string ActionText => Action switch
    {
        TransferPreviewAction.New => Ui.Dialogs.TransferPreviewActionNew,
        TransferPreviewAction.Skip => Ui.Dialogs.TransferPreviewActionSkip,
        TransferPreviewAction.Rename => Ui.Dialogs.TransferPreviewActionRename,
        _ => Ui.Dialogs.TransferPreviewActionReplace
    };

    public bool IsReplace => Action == TransferPreviewAction.Replace;

    public bool IsSkip => Action is TransferPreviewAction.Skip;

    internal event EventHandler? ChoiceChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    private static int IndexOf(TransferConflictChoice choice)
    {
        for (var index = 0; index < TransferConflictText.All.Count; index++)
        {
            if (TransferConflictText.All[index] == choice) return index;
        }

        return -1;
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// What the preview window shows: every file a transfer would write, with a choice for each one
/// that lands on a file already there, and the choices it answers with.
/// </summary>
internal sealed class TransferPreviewModel : INotifyPropertyChanged
{
    private readonly IReadOnlyList<TransferPreviewRowModel> _all;
    private bool _onlyConflicts;

    internal TransferPreviewModel(TransferPreview preview, TransferConflictChoice initial)
    {
        ArgumentNullException.ThrowIfNull(preview);
        _all = [.. preview.Rows.Select(row => new TransferPreviewRowModel(row, initial))];
        foreach (var row in _all) row.ChoiceChanged += (_, _) => RaiseSummary();
        _onlyConflicts = preview.ConflictCount > 0 && preview.Rows.Count > 200;
        StartCommand = new RelayCommand(_ => Finish(Overrides()), _ => _all.Count > 0);
        CancelCommand = new RelayCommand(_ => Finish(null));
        ReplaceAllCommand = new RelayCommand(_ => SetAll(TransferConflictChoice.Overwrite));
        SkipAllCommand = new RelayCommand(_ => SetAll(TransferConflictChoice.Skip));
        ReplaceIfNewerCommand = new RelayCommand(_ => SetAll(TransferConflictChoice.OverwriteIfNewer));
        Filter();
    }

    public ObservableCollection<TransferPreviewRowModel> Rows { get; } = [];

    public bool HasConflicts => _all.Any(static row => row.IsConflict);

    public bool OnlyConflicts
    {
        get => _onlyConflicts;
        set
        {
            if (_onlyConflicts == value) return;
            _onlyConflicts = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OnlyConflicts)));
            Filter();
        }
    }

    public string Summary => _all.Count == 0
        ? Ui.Dialogs.TransferPreviewEmpty
        : Ui.Format(
            Ui.Dialogs.TransferPreviewSummaryFormat,
            Count(TransferPreviewAction.New),
            Count(TransferPreviewAction.Replace),
            Count(TransferPreviewAction.Skip),
            Count(TransferPreviewAction.Rename));

    public ICommand StartCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand ReplaceAllCommand { get; }

    public ICommand SkipAllCommand { get; }

    public ICommand ReplaceIfNewerCommand { get; }

    /// <summary>
    /// The choice for each file that lands on one already there, by the source's relative path, or
    /// null when the window was cancelled.
    /// </summary>
    internal IReadOnlyDictionary<string, TransferConflictChoice>? Result { get; private set; }

    internal event EventHandler? Closed;

    public static string Title => Ui.Dialogs.TransferPreviewCaption;

    public static string OnlyConflictsLabel => Ui.Dialogs.TransferPreviewOnlyConflicts;

    public static string ReplaceAllLabel => Ui.Dialogs.TransferPreviewReplaceAll;

    public static string SkipAllLabel => Ui.Dialogs.TransferPreviewSkipAll;

    public static string ReplaceIfNewerLabel => Ui.Dialogs.TransferPreviewReplaceIfNewer;

    public static string StartLabel => Ui.Dialogs.TransferPreviewStart;

    public static string CancelLabel => Ui.Dialogs.ButtonCancel;

    public static string ColumnAction => Ui.Dialogs.TransferPreviewColumnAction;

    public static string ColumnFile => Ui.Dialogs.TransferPreviewColumnFile;

    public static string ColumnSourceSize => Ui.Dialogs.TransferPreviewColumnSourceSize;

    public static string ColumnSourceModified => Ui.Dialogs.TransferPreviewColumnSourceModified;

    public static string ColumnTargetSize => Ui.Dialogs.TransferPreviewColumnTargetSize;

    public static string ColumnTargetModified => Ui.Dialogs.TransferPreviewColumnTargetModified;

    public event PropertyChangedEventHandler? PropertyChanged;

    private int Count(TransferPreviewAction action) => _all.Count(row => row.Action == action);

    private void SetAll(TransferConflictChoice choice)
    {
        foreach (var row in _all.Where(static row => row.IsConflict)) row.Choice = choice;
    }

    private void Filter()
    {
        Rows.Clear();
        foreach (var row in _all.Where(row => !_onlyConflicts || row.IsConflict)) Rows.Add(row);
        RaiseSummary();
    }

    private void RaiseSummary() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));

    private Dictionary<string, TransferConflictChoice> Overrides()
    {
        var overrides = new Dictionary<string, TransferConflictChoice>(StringComparer.Ordinal);
        foreach (var row in _all.Where(static row => row.IsConflict))
        {
            overrides[row.Row.Source.RelativePath] = row.Choice;
        }

        return overrides;
    }

    private void Finish(IReadOnlyDictionary<string, TransferConflictChoice>? result)
    {
        Result = result;
        Closed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Preview changes: every file a transfer would write, chosen for before it starts.</summary>
public partial class TransferPreviewWindow : Window
{
    public TransferPreviewWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is TransferPreviewModel model) model.Closed += (_, _) => Close();
        };
    }

    /// <summary>The choices made in the preview, or null when it was cancelled.</summary>
    internal static async Task<IReadOnlyDictionary<string, TransferConflictChoice>?> AskAsync(
        Window? owner,
        TransferPreview preview,
        TransferConflictChoice initial)
    {
        if (owner is null) return null;
        var model = new TransferPreviewModel(preview, initial);
        await new TransferPreviewWindow { DataContext = model }.ShowDialog(owner).ConfigureAwait(true);
        return model.Result;
    }
}
