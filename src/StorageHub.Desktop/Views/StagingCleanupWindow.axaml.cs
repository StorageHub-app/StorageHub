using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace StorageHub.Desktop.Views;

/// <summary>Clean Up Leftover Files: what transfers and the external editor left, removed on request.</summary>
public partial class StagingCleanupWindow : Window
{
    public StagingCleanupWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Closed += (_, _) => (DataContext as StagingCleanupModel)?.Dispose();
    }

    /// <summary>
    /// A window over the agent and this desktop's own editor sessions, searching as soon as it opens.
    /// One controller for the window's life, so the agent connection is opened once and closed with it.
    /// </summary>
    internal static StagingCleanupWindow ForCurrentAgent()
    {
        var controller = new StagingCleanupController(Services.ShellServices.OpenEditSessionDirectories);
        var model = new StagingCleanupModel(
            controller.ScanAsync,
            (items, cancellationToken) => controller.RemoveAsync(items, cancellationToken),
            Services.ShellServices.Dialogs);
        var window = new StagingCleanupWindow { DataContext = model };
        window.Opened += (_, _) => _ = model.SearchAsync();
        window.Closed += (_, _) => _ = controller.DisposeAsync().AsTask();
        return window;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
