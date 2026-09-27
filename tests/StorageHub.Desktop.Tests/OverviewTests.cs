using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StorageHub.Desktop;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The Welcome tab, against docs/ui-reference/01-welcome-overview.png.
/// </summary>
/// <remarks>
/// The first screen anyone sees, and the first one ported. What these assert is that it says what
/// the WinForms screen says - the wording all comes from OverviewStrings, which already had every
/// string including the empty states.
/// </remarks>
public class OverviewTests
{
    [Fact]
    public void TheOverviewSaysWhatTheOldOneSaid()
    {
        var model = OverviewModel.Create(ShellStatusSnapshot.Initial);

        Assert.Equal(Ui.Overview.Headline, model.Headline);
        Assert.Equal(Ui.Overview.Subheading, model.Subheading);
        Assert.Equal(4, model.Metrics.Count);
        Assert.Equal(
            [Ui.Overview.MetricAgent, Ui.Overview.MetricActiveTransfers, Ui.Overview.MetricQueued, Ui.Overview.MetricNeedsAttention],
            model.Metrics.Select(metric => metric.Caption));
    }

    /// <summary>
    /// The empty states are written out, not left blank.
    /// </summary>
    /// <remarks>
    /// This screen spends most of its life empty, and the WinForms one fills each table with a line
    /// saying what would be there. An empty grid reads as a bug; "No workspaces yet" does not.
    /// </remarks>
    [Fact]
    public void AnEmptyOverviewStillSaysSomethingInEveryTable()
    {
        var model = OverviewModel.Create(ShellStatusSnapshot.Initial);

        Assert.Equal(Ui.Overview.WorkspacesEmpty, model.Workspaces.Single().Name);
        Assert.Equal(Ui.Overview.WorkspacesEmptyHint, model.Workspaces.Single().Location);
        Assert.Equal(Ui.Overview.ConnectionsEmpty, model.RecentConnections.Single().Name);
        Assert.Equal(Ui.Overview.AttentionEmpty, model.Attention.Single().Name);
    }

    /// <summary>
    /// An empty table's message is read, not used: it does not light up, take a click, or stay
    /// selected, while a real row beside it still can.
    /// </summary>
    /// <remarks>
    /// "No workspaces yet" highlighted under the pointer and was selected when clicked, which is
    /// the first step to opening it. 1.4's lists just showed the message. Every placeholder in the
    /// real shell is checked, then an empty Sync tasks, then a list holding both kinds, for Select all.
    /// </remarks>
    [AvaloniaFact]
    public void AnEmptyTablesMessageCannotBeSelected()
    {
        var window = new MainWindow { DataContext = ShellPreview.Sample };
        window.Show();
        window.Measure(new Size(1500, 920));
        window.Arrange(new Rect(0, 0, 1500, 920));
        window.UpdateLayout();

        var rows = window.GetVisualDescendants().OfType<TableViewRow>()
            .Where(row => PlaceholderRows.IsPlaceholder(row.DataContext))
            .ToArray();
        Assert.Equal(3, rows.Length);
        foreach (var row in rows)
        {
            var table = row.FindAncestorOfType<TableView>()!;
            var centre = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
            window.MouseMove(centre, RawInputModifiers.None);
            window.MouseDown(centre, MouseButton.Left);
            window.MouseUp(centre, MouseButton.Left);

            Assert.False(row.IsPointerOver);
            Assert.Null(table.SelectedItem);

            table.SelectedIndex = 0;
            Dispatcher.UIThread.RunJobs();
            Assert.Null(table.SelectedItem);
        }

        // Sync tasks says the same kind of thing, through the same flag.
        var sync = SyncTasksModel.Create();
        Assert.All(sync.Tasks.Concat<object>(sync.LastSyncs), item => Assert.True(PlaceholderRows.IsPlaceholder(item)));

        var real = new WorkspaceRow("Design", @"C:\Work\design.shw", "Pinned");
        var list = new ListBox { SelectionMode = SelectionMode.Multiple, ItemsSource = new[] { real, rows[0].DataContext } };
        var host = new Window { Content = list };
        host.Show();
        list.SelectAll();
        Dispatcher.UIThread.RunJobs();

        Assert.Same(real, Assert.Single(list.SelectedItems!));
        host.Close();
        window.Close();
    }

    /// <summary>
    /// The agent card uses the card's wording, not the status bar's.
    /// </summary>
    /// <remarks>
    /// The bar says "Agent: connected" because nothing labels it; the card says "Connected" under a
    /// caption that already reads "Agent". OverviewStrings has always carried both sets, and using
    /// the wrong one would read as "Agent / Agent: connected".
    /// </remarks>
    [Theory]
    [InlineData(AgentConnectionState.Connected)]
    [InlineData(AgentConnectionState.Disconnected)]
    [InlineData(AgentConnectionState.RecoveryOnly)]
    [InlineData(AgentConnectionState.Starting)]
    public void TheAgentCardDoesNotRepeatItsOwnCaption(AgentConnectionState state)
    {
        var snapshot = ShellStatusSnapshot.Initial with { AgentState = state };
        var card = OverviewModel.Create(snapshot).Metrics[0];

        Assert.DoesNotContain(Ui.Overview.MetricAgent, card.Value, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(card.Value));
    }

    [Fact]
    public void OnlyAConnectedAgentColoursItsCardAsHealthy()
    {
        var connected = OverviewModel.Create(
            ShellStatusSnapshot.Initial with { AgentState = AgentConnectionState.Connected });
        var starting = OverviewModel.Create(ShellStatusSnapshot.Initial);

        Assert.True(connected.Metrics[0].IsSuccess);
        Assert.False(starting.Metrics[0].IsSuccess);
    }

    /// <summary>The Welcome tab renders the overview rather than an empty pane grid.</summary>
    [AvaloniaFact]
    public void TheWelcomeTabShowsTheOverview()
    {
        var window = new MainWindow { DataContext = ShellPreview.Sample };
        window.Show();
        window.Measure(new Size(1500, 920));
        window.Arrange(new Rect(0, 0, 1500, 920));

        Assert.Single(window.GetVisualDescendants().OfType<OverviewView>());

        var texts = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(block => block.Text)
            .ToList();
        Assert.Contains(Ui.Overview.Headline, texts);
        Assert.Contains(Ui.Overview.WorkspacesTitle, texts);
    }
}
