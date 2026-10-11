using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace StorageHub.Desktop.Views;

/// <summary>Compare Folders: the two panes' folders as one list of differences, copied either way.</summary>
public partial class FolderCompareWindow : Window
{
    public FolderCompareWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Closed += (_, _) => (DataContext as FolderCompareModel)?.Dispose();
    }

    /// <summary>A window over <paramref name="model"/>, comparing as soon as it opens.</summary>
    internal static FolderCompareWindow For(FolderCompareModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var window = new FolderCompareWindow { DataContext = model };
        window.Opened += (_, _) => _ = model.CompareAsync();
        return window;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
