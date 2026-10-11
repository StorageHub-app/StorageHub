using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop.Views;

/// <summary>Tools, Checksums: the SHA-256 of each selected file, compared with each other or with one pasted in.</summary>
public partial class ChecksumsWindow : Window
{
    public ChecksumsWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // Closing stops the files not yet read.
        Closed += (_, _) => (DataContext as ChecksumsModel)?.Dispose();
    }

    /// <summary>
    /// The selected files, up to <see cref="ChecksumCalculator.MaximumFiles"/>, read through the
    /// object inspector's checksum on a connection and here for this computer, starting as it opens.
    /// </summary>
    internal static ChecksumsWindow For(PaneSelectionSnapshot selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var files = selection.Items
            .Where(static item => item.Kind == StorageItemKind.File)
            .Take(ChecksumCalculator.MaximumFiles)
            .ToArray();
        var calculator = new ChecksumCalculator(static () => new NamedPipeObjectInspectorAgentClient());
        var model = new ChecksumsModel(
            files,
            selection.Items.Count - files.Length,
            (item, cancellationToken) => calculator.ComputeAsync(selection.Context, item, cancellationToken),
            Services.ShellServices.Clipboard);
        var window = new ChecksumsWindow { DataContext = model };
        window.Opened += (_, _) => _ = model.ComputeAsync();
        return window;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
