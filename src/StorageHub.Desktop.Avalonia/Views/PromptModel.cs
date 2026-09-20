using System.ComponentModel;
using System.Windows.Input;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;

namespace StorageHub.Desktop.Views;

/// <summary>
/// The question behind <see cref="PromptWindow"/>: a name, and whether it will do.
/// </summary>
/// <remarks>
/// The validation runs as the name is typed rather than when Create is pressed, so the button is
/// dim with the reason showing instead of enabled onto a refusal. It is the storage layer's own
/// rule -- <see cref="PaneItemNameRules"/> -- so a name this accepts is one the agent will take.
/// </remarks>
internal sealed class PromptModel : INotifyPropertyChanged
{
    private readonly DialogPromptRequest _request;
    private string _value;

    internal PromptModel(DialogPromptRequest request)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _value = request.Value;

        AcceptCommand = new RelayCommand(_ => Finish(_value), _ => !HasError);
        CancelCommand = new RelayCommand(_ => Finish(null));
    }

    public string Title => _request.Title;

    public string Label => _request.Label;

    public string Accept => _request.Accept;

    public static string CancelLabel => Ui.Dialogs.ButtonCancel;

    public string Value
    {
        get => _value;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_value, value, StringComparison.Ordinal)) return;
            _value = value;
            Raise(nameof(Value));
            Raise(nameof(Error));
            Raise(nameof(HasError));
            (AcceptCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Why this name will not do, or nothing.</summary>
    public string Error => _request.Validate?.Invoke(_value) ?? string.Empty;

    public bool HasError => Error.Length > 0;

    public ICommand AcceptCommand { get; }

    public ICommand CancelCommand { get; }

    /// <summary>The name given, or nothing when the dialog was dismissed.</summary>
    internal string? Answer { get; private set; }

    /// <summary>Raised once there is an answer and the window should close.</summary>
    internal event EventHandler? Closed;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Finish(string? answer)
    {
        Answer = answer;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
