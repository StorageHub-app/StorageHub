using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The X on a workspace tab, which 1.x had and the port had dropped (ui-reference 05).
/// </summary>
public class WorkspaceCloseTests
{
    [AvaloniaFact]
    public void OnlyAWorkspaceTabCarriesACloseButton()
    {
        var model = ShellPreview.CreateOnWorkspace();
        var window = new MainWindow { DataContext = model };
        window.Show();
        window.Measure(new Size(1500, 920));
        window.Arrange(new Rect(0, 0, 1500, 920));
        window.UpdateLayout();

        var closers = window.GetVisualDescendants()
            .OfType<Button>()
            .Where(static button => button.Classes.Contains("tab-close") && button.IsVisible)
            .ToArray();

        Assert.Equal(model.Workspaces.Count(static tab => tab.IsClosable), closers.Length);
        Assert.All(closers, static closer => Assert.True(closer.Command?.CanExecute(closer.CommandParameter)));
    }

    [AvaloniaFact]
    public async Task ClosingAWorkspaceRemovesItsTabAndShowsTheOneBeforeIt()
    {
        var model = ShellPreview.CreateOnWorkspace();
        var workspace = model.Workspaces.Last(static tab => tab.IsClosable);
        model.SelectedWorkspace = model.Workspaces.IndexOf(workspace);
        var before = model.SelectedWorkspace - 1;

        await model.CloseWorkspaceAsync(workspace);

        Assert.DoesNotContain(workspace, model.Workspaces);
        Assert.Equal(before, model.SelectedWorkspace);
    }

    [AvaloniaFact]
    public async Task ThePagesOfTheShellCannotBeClosed()
    {
        var model = ShellPreview.CreateOnWorkspace();
        var page = model.Workspaces.First(static tab => !tab.IsClosable);
        var count = model.Workspaces.Count;

        Assert.False(model.CloseWorkspaceCommand.CanExecute(page));
        await model.CloseWorkspaceAsync(page);

        Assert.Equal(count, model.Workspaces.Count);
    }
}
