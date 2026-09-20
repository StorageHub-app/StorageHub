using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using StorageHub.Contracts.Ipc;
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
}
