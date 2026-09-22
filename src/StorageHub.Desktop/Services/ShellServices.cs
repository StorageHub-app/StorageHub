using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Configuration;
using StorageHub.Desktop.Framework;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;
using StorageHub.Desktop.Views;

namespace StorageHub.Desktop.Services;

/// <summary>
/// The shell's side of the dialog vocabulary, wired to whichever window is open.
/// </summary>
/// <remarks>
/// <para>
/// One place rather than a container. StorageHub has never had dependency injection in the shell
/// and adding one to hand out three objects would be the larger change; what these need is a live
/// window, and the only thing that knows which window that is, is the application lifetime.
/// </para>
/// <para>
/// The window is resolved per call rather than captured, because a view model outlives any
/// particular window: the shell replaces its main window on a restart-in-place, and a service
/// holding the old one would put a modal behind a window nobody can see.
/// </para>
/// </remarks>
internal static class ShellServices
{
    /// <summary>Where the services look for a window. Replaced in tests.</summary>
    internal static Func<Window?> MainWindow { get; set; } = () =>
        (global::Avalonia.Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    internal static IDialogService Dialogs { get; } = new AvaloniaDialogService(() => MainWindow());

    internal static IFilePickerService FilePicker { get; } =
        new AvaloniaFilePickerService(() => MainWindow());

    internal static IClipboardService Clipboard { get; } =
        new AvaloniaClipboardService(() => MainWindow());

    /// <summary>
    /// Opens the object inspector over the shell.
    /// </summary>
    /// <remarks>
    /// Here rather than in App because panes are made on demand, by a factory, and there is no
    /// moment at which App could subscribe to each one. The pane asks; this answers with a window
    /// over whichever main window is open, on the UI thread, since the pane's await may resume
    /// anywhere.
    /// </remarks>
    internal static Task InspectObjectAsync(ObjectInspectorAddress address) =>
        Dispatcher.UIThread.InvokeAsync(() => ObjectInspectorWindow.ShowAsync(MainWindow(), address));

    private static ExternalEditController? _editing;

    /// <summary>Raised on the UI thread after an edited file has been uploaded.</summary>
    internal static event EventHandler? EditedFileUploaded;

    /// <summary>
    /// Opens a remote file in the external editor.
    /// </summary>
    /// <remarks>
    /// One controller for the shell, made the first time something is edited, because it holds
    /// every open session: they are watched for as long as the shell runs and closed with it. A
    /// failure to start is said in a dialog rather than thrown, since the pane that asked has
    /// already moved on.
    /// </remarks>
    internal static async Task EditExternallyAsync(ObjectInspectorAddress address, string fileName, long? length)
    {
        try
        {
            _editing ??= CreateEditing();
            await _editing.OpenAsync(address, fileName, length).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidDataException or InvalidOperationException or TimeoutException)
        {
            await Dialogs.ShowAsync(new DialogRequest
            {
                Title = Ui.Dialogs.ExternalEditorCaption,
                Message = Ui.Format(Ui.Dialogs.ExternalEditorOpenFailedFormat, error.Message),
                Severity = DialogSeverity.Warning
            }).ConfigureAwait(false);
        }
    }

    /// <summary>Stops watching every edited file, at shutdown.</summary>
    internal static async ValueTask CloseEditingAsync()
    {
        if (_editing is { } editing)
        {
            _editing = null;
            await editing.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static ExternalEditController CreateEditing()
    {
        var store = new DesktopConfigStore(DesktopFrameworkPaths.Resolve().ApplicationRoot);
        store.Preflight();
        var editing = new ExternalEditController(
            store,
            static () => new NamedPipeObjectInspectorAgentClient(),
            new AvaloniaExternalEditPrompts(Dialogs, () => MainWindow()));
        editing.FileUploaded += (_, _) =>
            Dispatcher.UIThread.Post(() => EditedFileUploaded?.Invoke(null, EventArgs.Empty));
        return editing;
    }
}
