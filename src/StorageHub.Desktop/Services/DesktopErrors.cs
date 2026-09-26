using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using StorageHub.Desktop.Framework;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;

namespace StorageHub.Desktop.Services;

/// <summary>
/// What happens to an exception nothing caught.
/// </summary>
/// <remarks>
/// 2.0 installed no handler, so an exception on the UI thread ended the process with no message and
/// nothing on disk. 1.x's WinForms default at least offered to continue. Here every one is written
/// to <see cref="DesktopErrorLog"/>; one on the UI thread is also said on screen and the shell keeps
/// running, which is the choice 1.x's dialog defaulted to.
/// </remarks>
internal static class DesktopErrors
{
    private static bool _showing;

    internal static void Install(IClassicDesktopStyleApplicationLifetime desktop)
    {
        ArgumentNullException.ThrowIfNull(desktop);

        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            DesktopErrorLog.Write("ui", e.Exception);
            e.Handled = true;
            _ = ShowAsync(desktop, e.Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            DesktopErrorLog.Write("task", e.Exception);
            e.SetObserved();
        };

        // The process is ending; the most that can be done is to leave a record of why.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception error) DesktopErrorLog.Write("process", error);
        };
    }

    private static async Task ShowAsync(IClassicDesktopStyleApplicationLifetime desktop, Exception error)
    {
        // One at a time: an error that repeats on every frame would otherwise stack dialogs until
        // the window could not be reached.
        if (_showing) return;
        _showing = true;
        try
        {
            await new AvaloniaDialogService(() => desktop.MainWindow).ShowAsync(new DialogRequest
            {
                Title = Ui.Dialogs.UnexpectedErrorCaption,
                Message = Ui.Format(Ui.Dialogs.UnexpectedErrorFormat, error.Message, DesktopErrorLog.FilePath),
                Severity = DialogSeverity.Error,
                Buttons = DialogButtons.Ok
            }).ConfigureAwait(true);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // Saying so failed too. It is in the log either way.
            DesktopErrorLog.Write("ui-dialog", failure);
        }
        finally
        {
            _showing = false;
        }
    }
}
