using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace StorageHub.Desktop.Views;

/// <summary>Tools, Search: every name under the active pane's folder that matches, at any depth.</summary>
public partial class FileSearchWindow : Window
{
    public FileSearchWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // The name box has the cursor, so typing and Enter is all a search takes.
        Opened += (_, _) => this.FindControl<TextBox>("QueryBox")?.Focus();

        // Closing stops a walk nobody is waiting for any more.
        Closed += (_, _) => (DataContext as FileSearchModel)?.Dispose();
    }

    /// <summary>
    /// A search of the pane's folder, through the agent for a connection. Go to shows the chosen
    /// match in that same pane, which stays usable beside this window.
    /// </summary>
    internal static FileSearchWindow For(BrowserPaneModel pane, PaneTransferContext where)
    {
        var search = new FileSearch(static () => new NamedPipeRemoteStorageAgentClient());
        var model = new FileSearchModel(
            (pattern, progress, cancellationToken) => search.SearchAsync(where, pattern, progress, cancellationToken),
            Describe(pane, where),
            hit => pane.RevealAsync(hit.Folder, hit.Name));
        return new FileSearchWindow { DataContext = model };
    }

    /// <summary>"Studio SFTP · /photos", as the queue names a place.</summary>
    private static string Describe(BrowserPaneModel pane, PaneTransferContext where) =>
        where.Kind == PaneTransferContextKind.ThisPc
            ? where.RelativePath
            : $"{pane.Title} · {(where.RelativePath.Length == 0 ? "/" : where.RelativePath)}";

    private void OnResultDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is FileSearchModel model) _ = model.RevealAsync();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
