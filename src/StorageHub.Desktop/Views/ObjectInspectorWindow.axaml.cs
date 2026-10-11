using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Services;

namespace StorageHub.Desktop.Views;

/// <summary>
/// The Properties window, over one file, folder or link on a saved connection.
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

    /// <summary>Opens the Properties window for an item, over the shell or on its own.</summary>
    /// <param name="changed">
    /// Called after the permissions or the modified time were changed, so the shell can reload the
    /// pane the item is listed in.
    /// </param>
    /// <remarks>
    /// The permissions dialog and the modified-time prompt are owned by this window rather than the
    /// shell's, so they open over it and it stays modal behind them.
    /// </remarks>
    internal static Task ShowAsync(
        Window? owner,
        ObjectInspectorAddress address,
        StorageItemKind kind,
        Action? changed = null)
    {
        var window = new ObjectInspectorWindow();
        var model = ObjectInspectorModel.Create(
            address,
            kind,
            static () => new NamedPipeObjectInspectorAgentClient(),
            new AvaloniaDialogService(() => window),
            (name, mode) => PermissionsWindow.AskAsync(window, name, mode));
        if (changed is not null) model.AttributesChanged += (_, _) => changed();
        window.DataContext = model;

        if (owner is not null) return window.ShowDialog(owner);
        window.Show();
        return Task.CompletedTask;
    }
}
