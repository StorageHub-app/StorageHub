using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace StorageHub.Desktop.Views;

/// <summary>
/// The run history and review screen.
/// </summary>
/// <remarks>
/// Two things the view is responsible for rather than the model: loading the history the first time
/// the screen is actually shown, and turning a double-click on a history row into a load. Neither
/// can be expressed as a binding, and both were behaviours of the WinForms control.
/// </remarks>
public partial class SyncRunHistoryView : UserControl
{
    public SyncRunHistoryView()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(DoubleTappedEvent, OnDoubleTapped, RoutingStrategies.Bubble);
    }

    /// <summary>
    /// Asks for the history once the screen is on display.
    /// </summary>
    /// <remarks>
    /// Not in the constructor: the sub-tab is built with the workspace, so loading there would put
    /// a pipe connection and a listing behind a tab nobody has opened. The model only acts on the
    /// first of these.
    /// </remarks>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is SyncRunHistoryModel model) _ = model.EnsureHistoryAsync();
    }

    /// <summary>
    /// Opening a history row loads that run.
    /// </summary>
    /// <remarks>
    /// Guarded on the history table rather than the whole screen, so a double-click in the plan or
    /// the conflicts table does not reload the run underneath it.
    /// </remarks>
    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not SyncRunHistoryModel model) return;
        if (e.Source is not Visual source) return;
        if (source.FindAncestorOfType<TableView>() is not { Name: "PART_History" }) return;
        if (model.SelectedRun is not { } row || row.SyncRunId == Guid.Empty) return;

        e.Handled = true;
        _ = model.LoadRunAsync(row.SyncRunId);
    }
}
