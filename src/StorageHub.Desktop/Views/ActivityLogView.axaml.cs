using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace StorageHub.Desktop.Views;

/// <summary>The activity log, as the queue's Logs tab draws it.</summary>
public partial class ActivityLogView : UserControl
{
    public ActivityLogView() => AvaloniaXamlLoader.Load(this);
}
