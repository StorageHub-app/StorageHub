using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>What the unsafe-edit warning asks, and what was answered.</summary>
internal sealed class UnsafeEditWarningModel
{
    internal UnsafeEditWarningModel(string fileName)
    {
        Message = Ui.Format(Ui.Dialogs.UnsafeExternalEditBodyFormat, fileName);
        ContinueCommand = new RelayCommand(_ => Finish(true));
        CancelCommand = new RelayCommand(_ => Finish(false));
    }

    public string Message { get; }

    /// <summary>
    /// Whether to stop warning. Restored from Settings, under Confirmations, which is what the
    /// checkbox's hint says.
    /// </summary>
    public bool DontShowAgain { get; set; }

    public ICommand ContinueCommand { get; }

    public ICommand CancelCommand { get; }

    /// <summary>What was decided. Dismissing the window is a cancel, and does not stop the warning.</summary>
    internal UnsafeExternalEditDecision Decision { get; private set; } = new(Continue: false, DontShowAgain: false);

    internal event EventHandler? Closed;

    private void Finish(bool proceed)
    {
        // Stopping the warning only means something for an edit that goes ahead; ticking the box and
        // cancelling would otherwise silence a warning nobody acted on.
        Decision = new UnsafeExternalEditDecision(proceed, proceed && DontShowAgain);
        Closed?.Invoke(this, EventArgs.Empty);
    }

    public static string Title => Ui.Dialogs.UnsafeExternalEditCaption;

    public static string AccessibleName => Ui.Dialogs.UnsafeExternalEditAccessibleName;

    public static string DontShowAgainLabel => Ui.Dialogs.DontShowWarningAgain;

    public static string DontShowAgainHint => Ui.Dialogs.RestoreWarningInSettingsHint;

    public static string ContinueLabel => Ui.Dialogs.ButtonContinueAnyway;

    public static string CancelLabel => Ui.Dialogs.ButtonCancel;
}

/// <summary>
/// Warns before an edit whose upload cannot be checked against the version downloaded.
/// </summary>
/// <remarks>
/// Its own window rather than a message box because of the checkbox, which the shell's dialog
/// service has no place for. It is one of the small dialogs the plan listed, pulled forward because
/// external editing cannot go ahead without it.
/// </remarks>
public partial class UnsafeEditWarningWindow : Window
{
    public UnsafeEditWarningWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is UnsafeEditWarningModel model) model.Closed += (_, _) => Close();
        };
    }

    /// <summary>Asks over a window, or declines when there is none to ask over.</summary>
    internal static async Task<UnsafeExternalEditDecision> AskAsync(Window? owner, string fileName)
    {
        if (owner is null) return new UnsafeExternalEditDecision(Continue: false, DontShowAgain: false);

        var model = new UnsafeEditWarningModel(fileName);
        await new UnsafeEditWarningWindow { DataContext = model }.ShowDialog(owner).ConfigureAwait(true);
        return model.Decision;
    }
}
