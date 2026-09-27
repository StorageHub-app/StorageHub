using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace StorageHub.Desktop.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // A borderless window can still be moved, so a slow start is not pinned to the centre.
        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };

        DataContextChanged += (_, _) =>
        {
            if (DataContext is SplashModel model)
            {
                // The service already treats a clipboard somebody else holds as nothing to fail over,
                // which is what the one screen explaining a failure needs.
                var clipboard = new Services.AvaloniaClipboardService(() => this);
                model.CopyRequested += async (_, text) => await clipboard.SetTextAsync(text).ConfigureAwait(true);
                model.CheckInstallationRequested += async (_, _) =>
                    await InstallationCheckWindow.ShowForThisMachineAsync(this).ConfigureAwait(true);
            }
        };
    }
}
