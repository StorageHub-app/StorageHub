using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace StorageHub.Desktop.Views;

/// <summary>Tools, Diagnostics: this installation in a few lines, and a support bundle to attach to a report.</summary>
public partial class DiagnosticsWindow : Window
{
    public DiagnosticsWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>The report as things stand: the agent as the shell last heard it, and the queue's counts.</summary>
    internal static DiagnosticsWindow For(
        AgentMonitorStatus? agent,
        IReadOnlyDictionary<string, int> queueCounts,
        Func<string, Task<bool>> openFolder)
    {
        var facts = DesktopDiagnostics.Collect(agent, queueCounts);
        var model = new DiagnosticsModel(
            facts,
            DesktopDiagnostics.LogFolder,
            Services.ShellServices.Clipboard,
            Services.ShellServices.FilePicker,
            openFolder,
            (path, collected) => DesktopDiagnostics.WriteBundleAsync(path, collected, Framework.DesktopErrorLog.FilePath));
        return new DiagnosticsWindow { DataContext = model };
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
