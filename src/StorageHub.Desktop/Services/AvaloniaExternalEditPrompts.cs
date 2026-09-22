using Avalonia.Controls;
using Avalonia.Threading;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;
using StorageHub.Desktop.Views;

namespace StorageHub.Desktop.Services;

/// <summary>
/// External editing's questions, asked over the shell.
/// </summary>
/// <remarks>
/// The controller asks from wherever a file watcher raised, so every question is put on the UI
/// thread here -- the dialog service does that for itself, and the unsafe-edit warning, which is a
/// window of its own, is put there explicitly.
/// </remarks>
internal sealed class AvaloniaExternalEditPrompts(IDialogService dialogs, Func<Window?> owner) : IExternalEditPrompts
{
    private readonly IDialogService _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    private readonly Func<Window?> _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public Task<UnsafeExternalEditDecision> WarnUnsafeAsync(string fileName) =>
        Dispatcher.UIThread.InvokeAsync(() => UnsafeEditWarningWindow.AskAsync(_owner(), fileName));

    /// <summary>
    /// Yes uploads, No skips this change, Cancel stops watching -- 1.x's three answers.
    /// </summary>
    /// <remarks>
    /// Yes is the default because it is what a save in an editor opened for this purpose almost
    /// always means; the upload is still held to the version downloaded, so it cannot silently
    /// overwrite somebody else's change on a provider that can check.
    /// </remarks>
    public async Task<EditedFileChoice> ConfirmUploadAsync(string fileName)
    {
        var choice = await _dialogs.ConfirmAsync(new DialogRequest
        {
            Title = Ui.Dialogs.UploadEditedFileCaption,
            Message = Ui.Format(Ui.Dialogs.UploadEditedFilePromptFormat, fileName),
            Severity = DialogSeverity.Question,
            Buttons = DialogButtons.YesNoCancel,
            Default = DialogChoice.Yes
        }).ConfigureAwait(false);

        return choice switch
        {
            DialogChoice.Yes => EditedFileChoice.Upload,
            DialogChoice.No => EditedFileChoice.NotNow,
            _ => EditedFileChoice.StopWatching
        };
    }

    public Task ShowAsync(string message) => _dialogs.ShowAsync(new DialogRequest
    {
        Title = Ui.Dialogs.ExternalEditorCaption,
        Message = message,
        Severity = DialogSeverity.Warning
    });
}
