using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace StorageHub.Desktop.Views;

/// <summary>The transfer queue and its activity log, below the workspaces.</summary>
public partial class TransferQueueView : UserControl
{
    public TransferQueueView() => AvaloniaXamlLoader.Load(this);
}
