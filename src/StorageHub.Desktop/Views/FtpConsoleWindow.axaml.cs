using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace StorageHub.Desktop.Views;

/// <summary>
/// Connections, FTP Command Console: raw commands to the active pane's FTP or FTPS server, one at
/// a time, with a transcript of what went out and what came back.
/// </summary>
public partial class FtpConsoleWindow : Window
{
    private FtpConsoleModel? _model;

    public FtpConsoleWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // Enter sends and Up and Down walk the history, taken before the text box treats Up and
        // Down as moving the caret to either end of its one line.
        if (this.FindControl<TextBox>("PART_Command") is { } command)
        {
            command.AddHandler(KeyDownEvent, OnCommandKeyDown, RoutingStrategies.Tunnel);
        }

        Opened += (_, _) => this.FindControl<TextBox>("PART_Command")?.Focus();

        // Closing gives up on a reply still outstanding.
        Closed += (_, _) => (DataContext as FtpConsoleModel)?.Dispose();
    }

    /// <summary>A console on a saved FTP or FTPS connection, talking to the agent this desktop uses.</summary>
    internal static FtpConsoleWindow For(Guid connectionId, string connectionName)
    {
        var client = new NamedPipeFtpConsoleAgentClient();
        var model = new FtpConsoleModel(
            connectionId,
            connectionName,
            (request, cancellationToken) => client.ExecuteAsync(request, cancellationToken),
            Services.ShellServices.Clipboard);
        var window = new FtpConsoleWindow { DataContext = model };
        window.Closed += (_, _) => _ = client.DisposeAsync().AsTask();
        return window;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null) _model.LinesAdded -= OnLinesAdded;
        _model = DataContext as FtpConsoleModel;
        if (_model is not null) _model.LinesAdded += OnLinesAdded;
    }

    private void OnCommandKeyDown(object? sender, KeyEventArgs e)
    {
        if (_model is null || e.KeyModifiers != KeyModifiers.None) return;
        switch (e.Key)
        {
            case Key.Enter:
                if (_model.SendCommand.CanExecute(null)) _model.SendCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Up:
                _model.HistoryUp();
                MoveCaretToEnd(sender);
                e.Handled = true;
                break;
            case Key.Down:
                _model.HistoryDown();
                MoveCaretToEnd(sender);
                e.Handled = true;
                break;
        }
    }

    private static void MoveCaretToEnd(object? sender)
    {
        if (sender is TextBox box) box.CaretIndex = box.Text?.Length ?? 0;
    }

    /// <summary>Keeps the newest line in view, once the new lines have been laid out.</summary>
    private void OnLinesAdded(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(
            () => this.FindControl<ScrollViewer>("PART_Scroller")?.ScrollToEnd(),
            DispatcherPriority.Background);

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
