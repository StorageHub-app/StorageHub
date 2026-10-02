using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop.Views;

/// <summary>
/// Chooses an already-imported key or certificate for a connection field.
/// </summary>
/// <remarks>
/// Answers with an entry, or with nothing when dismissed -- the same contract every dialog in the
/// shell has. Only metadata is shown; the material itself never leaves the vault.
/// </remarks>
public partial class KeyStorePickerWindow : Window
{
    public KeyStorePickerWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is KeyStorePickerModel model) model.Closed += (_, _) => Close();
        };

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape || DataContext is not KeyStorePickerModel model) return;
            model.CancelCommand.Execute(null);
            e.Handled = true;
        }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);

        // A double-click on a row is Use, as it was in 1.x.
        Opened += (_, _) =>
        {
            if (this.FindControl<TableView>("PART_Entries") is not { } table) return;
            table.DoubleTapped += (_, _) =>
            {
                if (DataContext is KeyStorePickerModel { Selected: not null } model)
                {
                    model.UseCommand.Execute(null);
                }
            };
        };
    }

    /// <summary>
    /// One of the entries, chosen over a window, or nothing when dismissed.
    /// </summary>
    /// <remarks>
    /// Import opens the Key Store's own import dialog over this one and imports through the same
    /// controller the Key Store window uses, so the rules, the vault and the warning for a key with
    /// no passphrase are the Key Store's, not a second copy of them.
    /// </remarks>
    internal static async Task<KeyStoreEntryDocument?> ChooseAsync(
        Window owner,
        KeyStoreMaterialKind kind,
        IReadOnlyList<KeyStoreEntryDocument> entries,
        KeyStoreController? controller = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        KeyStorePickerWindow? window = null;
        var model = new KeyStorePickerModel(
            entries,
            kind,
            controller,
            controller is null ? null : asked => KeyStoreImportWindow.AskAsync(window!, asked));
        window = new KeyStorePickerWindow { DataContext = model };
        await window.ShowDialog(owner).ConfigureAwait(true);
        return model.Chosen;
    }
}
