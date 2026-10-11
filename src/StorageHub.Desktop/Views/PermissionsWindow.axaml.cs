using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace StorageHub.Desktop.Views;

/// <summary>
/// Asks which permission bits an item should have.
/// </summary>
/// <remarks>
/// Dismissing answers with nothing, as <see cref="PromptWindow"/> does, so the title bar and Escape
/// leave the item as it was.
/// </remarks>
public partial class PermissionsWindow : Window
{
    public PermissionsWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is PermissionsModel model) model.Closed += (_, _) => Close();
        };

        // The octal selected, so somebody who thinks in 755 can type it straight over.
        Opened += (_, _) =>
        {
            if (this.FindControl<TextBox>("PART_Octal") is not { } box) return;
            box.Focus();
            box.SelectAll();
        };

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape || DataContext is not PermissionsModel model) return;
            model.CancelCommand.Execute(null);
            e.Handled = true;
        }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>Asks over the given window, and reports the mode chosen or nothing.</summary>
    internal static async Task<int?> AskAsync(Window? owner, string itemName, int mode)
    {
        var model = new PermissionsModel(itemName, mode);
        var window = new PermissionsWindow { DataContext = model };
        if (owner is not null)
        {
            await window.ShowDialog(owner).ConfigureAwait(true);
            return model.Answer;
        }

        var closed = new TaskCompletionSource();
        window.Closed += (_, _) => closed.TrySetResult();
        window.Show();
        await closed.Task.ConfigureAwait(true);
        return model.Answer;
    }
}
