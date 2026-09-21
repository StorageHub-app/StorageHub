using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using StorageHub.Desktop.Services;

namespace StorageHub.Desktop.Views;

/// <summary>
/// Reviews a settings file and applies what was chosen.
/// </summary>
/// <remarks>
/// <para>
/// The file picker opens as the window does, because choosing a file is the whole of the first
/// step and there is nothing else the window can offer until one has been chosen. Dismissing the
/// picker leaves the window on its first step rather than closing it, so Browse is still there --
/// the WinForms version closed outright, which made a mis-click cost the dialog.
/// </para>
/// <para>
/// The password box is cleared on the way out, whichever way out it was.
/// </para>
/// </remarks>
public partial class SettingsImportWindow : Window
{
    public SettingsImportWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is SettingsImportModel model) model.Closed += (_, _) => Close();
        };

        // Enter opens the file rather than falling through to the review step's Import, which is
        // what IsDefault would do on a step where Import is not even visible.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (DataContext is not SettingsImportModel model) return;
            switch (e.Key)
            {
                case Key.Escape:
                    model.CloseCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.Enter when model.IsChoosingFile && model.CanOpen:
                    model.OpenCommand.Execute(null);
                    e.Handled = true;
                    break;
            }
        }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);

        Closed += (_, _) =>
        {
            if (this.FindControl<TextBox>("PART_Password") is { } box) box.Clear();
        };
    }

    /// <summary>The import wizard over the real settings file and the running agent.</summary>
    internal static SettingsImportWindow ForCurrentUser()
    {
        var store = SettingsTransferServices.Store();
        var model = new SettingsImportModel(
            SettingsTransferServices.Importer(store),
            ShellServices.FilePicker,
            ShellServices.Dialogs);

        var window = new SettingsImportWindow { DataContext = model };
        window.Opened += (_, _) => _ = model.PromptForFileAsync();
        return window;
    }
}
