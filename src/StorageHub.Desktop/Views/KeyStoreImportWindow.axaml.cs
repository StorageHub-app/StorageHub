using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Services;

namespace StorageHub.Desktop.Views;

/// <summary>
/// Asks what to import into the key store.
/// </summary>
/// <remarks>
/// Answers with a draft, or with nothing when dismissed -- the same contract every dialog in the
/// shell has, so a caller that treats "nothing" as "do not import" handles the title bar and
/// Escape for free. The passphrase box is cleared on the way out, whichever way out it was.
/// </remarks>
public partial class KeyStoreImportWindow : Window
{
    public KeyStoreImportWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is KeyStoreImportModel model) model.Closed += (_, _) => Close();
        };

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape || DataContext is not KeyStoreImportModel model) return;
            model.CancelCommand.Execute(null);
            e.Handled = true;
        }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);

        Closed += (_, _) =>
        {
            if (this.FindControl<TextBox>("PART_Passphrase") is { } box) box.Clear();
        };
    }

    /// <summary>What to import, asked over a window, or nothing when dismissed.</summary>
    internal static async Task<KeyStoreImportDraft?> AskAsync(Window owner, KeyStoreMaterialKind kind)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var model = new KeyStoreImportModel(kind, ShellServices.FilePicker);
        var window = new KeyStoreImportWindow { DataContext = model };
        await window.ShowDialog(owner).ConfigureAwait(true);
        return model.Result;
    }
}
