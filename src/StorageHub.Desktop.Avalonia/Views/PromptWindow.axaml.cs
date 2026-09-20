using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace StorageHub.Desktop.Views;

/// <summary>
/// Asks for a name.
/// </summary>
/// <remarks>
/// Dismissing answers with nothing, the same contract <see cref="DialogWindow"/> has, so a caller
/// that treats "nothing" as "do not do it" handles the title bar and Escape for free.
/// </remarks>
public partial class PromptWindow : Window
{
    public PromptWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is PromptModel model) model.Closed += (_, _) => Close();
        };

        // Focused and selected, so typing replaces the suggested name rather than appending to it.
        // A rename that starts with the cursor after the extension makes somebody clear the box
        // before they can do the thing they opened the dialog for.
        Opened += (_, _) =>
        {
            if (this.FindControl<TextBox>("PART_Value") is not { } box) return;
            box.Focus();
            box.SelectAll();
        };

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape || DataContext is not PromptModel model) return;
            model.CancelCommand.Execute(null);
            e.Handled = true;
        }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }
}
