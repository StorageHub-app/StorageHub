using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace StorageHub.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // Tunnelling, so the shell sees a shortcut before the focused control does - which is what
        // ProcessCmdKey did. ShellCommandRouter explains why that ordering is worth restoring.
        if (DataContext is ShellPreviewModel model) model.Router.Attach(this);
    }
}
