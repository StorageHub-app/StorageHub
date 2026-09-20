using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop.Views;

/// <summary>
/// The read-only object inspector, over one file on a saved connection.
/// </summary>
/// <remarks>
/// Loads when opened rather than when built, so a window that never opens never asks the agent,
/// and disposes its model when closed, which cancels anything still in flight and closes the pipe.
/// </remarks>
public partial class ObjectInspectorWindow : Window
{
    public ObjectInspectorWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ObjectInspectorModel model) model.Closed += (_, _) => Close();
        };

        Opened += (_, _) =>
        {
            if (DataContext is ObjectInspectorModel model) _ = model.LoadAsync();
        };

        Closed += (_, _) =>
        {
            if (DataContext is ObjectInspectorModel model) _ = model.DisposeAsync().AsTask();
        };

        // F5 refreshes and Ctrl+W closes, as the 1.x menu had them.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (DataContext is not ObjectInspectorModel model) return;
            if (e.Key == Key.F5)
            {
                model.RefreshCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.W && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                model.CloseCommand.Execute(null);
                e.Handled = true;
            }
        }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>Opens the inspector for an object, over the shell or on its own.</summary>
    internal static Task ShowAsync(Window? owner, ObjectInspectorAddress address)
    {
        var window = new ObjectInspectorWindow
        {
            DataContext = ObjectInspectorModel.Create(
                address, static () => new NamedPipeObjectInspectorAgentClient())
        };

        if (owner is not null) return window.ShowDialog(owner);
        window.Show();
        return Task.CompletedTask;
    }
}
