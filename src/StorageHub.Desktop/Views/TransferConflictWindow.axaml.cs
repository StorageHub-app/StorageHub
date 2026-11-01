using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>One action the file-exists window offers, as a radio button.</summary>
internal sealed class TransferConflictOption(TransferConflictChoice choice, bool selected) : INotifyPropertyChanged
{
    private bool _isSelected = selected;

    public TransferConflictChoice Choice { get; } = choice;

    public string Label { get; } = TransferConflictText.Label(choice);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// What the file-exists window shows: the file arriving and the one already there, and the
/// answer it gives back.
/// </summary>
internal sealed class TransferConflictModel : INotifyPropertyChanged
{
    private bool _applyToRest;
    private bool _remember;

    internal TransferConflictModel(
        PaneTransferItem source,
        PaneTransferItem existing,
        string sourcePlace,
        string targetPlace,
        bool canRemember)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(existing);
        Prompt = Ui.Format(Ui.Dialogs.TransferConflictPromptFormat, existing.Name);
        SourcePlace = sourcePlace;
        TargetPlace = targetPlace;
        SourceSize = SizeLine(source);
        TargetSize = SizeLine(existing);
        SourceModified = ModifiedLine(source);
        TargetModified = ModifiedLine(existing);
        CanRemember = canRemember;
        Options = [.. TransferConflictText.All.Select(static choice =>
            new TransferConflictOption(choice, choice == TransferConflictChoice.Overwrite))];
        OkCommand = new RelayCommand(_ => Finish(new TransferConflictAnswer(
            Options.FirstOrDefault(static option => option.IsSelected)?.Choice ?? TransferConflictChoice.Overwrite,
            ApplyToRest,
            ApplyToRest && Remember && CanRemember)));
        CancelCommand = new RelayCommand(_ => Finish(null));
    }

    public string Prompt { get; }

    public string SourcePlace { get; }

    public string TargetPlace { get; }

    public string SourceSize { get; }

    public string TargetSize { get; }

    public string SourceModified { get; }

    public string TargetModified { get; }

    public bool CanRemember { get; }

    public IReadOnlyList<TransferConflictOption> Options { get; }

    public bool ApplyToRest
    {
        get => _applyToRest;
        set
        {
            if (_applyToRest == value) return;
            _applyToRest = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ApplyToRest)));
        }
    }

    public bool Remember
    {
        get => _remember;
        set
        {
            if (_remember == value) return;
            _remember = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Remember)));
        }
    }

    public ICommand OkCommand { get; }

    public ICommand CancelCommand { get; }

    /// <summary>The answer, or null when the transfer was cancelled or the window closed.</summary>
    internal TransferConflictAnswer? Result { get; private set; }

    internal event EventHandler? Closed;

    public static string Title => Ui.Dialogs.TransferConflictCaption;

    public static string SourceHeading => Ui.Dialogs.ConflictSourceHeading;

    public static string TargetHeading => Ui.Dialogs.ConflictTargetHeading;

    public static string ActionHeading => Ui.Dialogs.ConflictActionHeading;

    public static string ApplyToRestLabel => Ui.Dialogs.ConflictApplyToRest;

    public static string RememberLabel => Ui.Dialogs.ConflictRememberDefault;

    public static string OkLabel => Ui.Dialogs.ButtonOk;

    public static string CancelLabel => Ui.Dialogs.ConflictCancelTransfer;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Finish(TransferConflictAnswer? answer)
    {
        Result = answer;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    internal static string SizeText(long? length) =>
        length is { } bytes ? UiFormatting.FormatBytes(bytes) : Ui.Dialogs.ConflictUnknown;

    internal static string ModifiedText(DateTimeOffset? modified) =>
        modified is { } time ? LocalBrowserPresentation.FormatModified(time) : Ui.Dialogs.ConflictUnknown;

    private static string SizeLine(PaneTransferItem item) =>
        Ui.Dialogs.ConflictSizeLabel + ": " + SizeText(item.Length);

    private static string ModifiedLine(PaneTransferItem item) =>
        Ui.Dialogs.ConflictModifiedLabel + ": " + ModifiedText(item.ModifiedUtc);
}

/// <summary>"File already exists": asked at each file a transfer finds already there.</summary>
public partial class TransferConflictWindow : Window
{
    public TransferConflictWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is TransferConflictModel model) model.Closed += (_, _) => Close();
        };
        Opened += (_, _) => this.FindControl<Button>("PART_Ok")?.Focus();
    }

    /// <summary>The answer for one file, asked over a window, or null when cancelled.</summary>
    internal static async Task<TransferConflictAnswer?> AskAsync(
        Window? owner,
        PaneTransferItem source,
        PaneTransferItem existing,
        string sourcePlace,
        string targetPlace,
        bool canRemember)
    {
        if (owner is null) return null;
        var model = new TransferConflictModel(source, existing, sourcePlace, targetPlace, canRemember);
        await new TransferConflictWindow { DataContext = model }.ShowDialog(owner).ConfigureAwait(true);
        return model.Result;
    }
}
