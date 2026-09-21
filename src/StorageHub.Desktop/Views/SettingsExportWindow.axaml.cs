using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using StorageHub.Desktop.Services;

namespace StorageHub.Desktop.Views;

/// <summary>
/// Writes chosen settings to a file.
/// </summary>
/// <remarks>
/// Answers with the path it wrote, or with nothing when dismissed -- the same contract every
/// dialog in the shell has. Both password boxes are cleared on the way out, whichever way out it
/// was, so an export password does not outlive the dialog that asked for it.
/// </remarks>
public partial class SettingsExportWindow : Window
{
    public SettingsExportWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is SettingsExportModel model) model.Closed += (_, _) => Close();
        };

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape || DataContext is not SettingsExportModel model) return;
            model.CancelCommand.Execute(null);
            e.Handled = true;
        }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);

        Closed += (_, _) =>
        {
            foreach (var name in new[] { "PART_Password", "PART_Confirm" })
            {
                if (this.FindControl<TextBox>(name) is { } box) box.Clear();
            }
        };
    }

    /// <summary>The export dialog over the real settings file and the running agent.</summary>
    internal static SettingsExportWindow ForCurrentUser()
    {
        var model = new SettingsExportModel(
            SettingsTransferServices.Exporter(SettingsTransferServices.Store()),
            ShellServices.FilePicker);
        return new SettingsExportWindow { DataContext = model };
    }
}
