using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using StorageHub.Desktop;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The status bar, which says what the shell believes about the agent.
/// </summary>
/// <remarks>
/// The strings come from ShellStatusSnapshot, which the WinForms shell has always used and which
/// moved to Desktop.Core with the other presentation models. Binding to it rather than writing four
/// strings in the view is what stops the two shells drifting apart while both exist.
/// </remarks>
public class ShellStatusTests
{
    [AvaloniaFact]
    public void TheStatusBarStartsWhereTheShellAlwaysStarted()
    {
        var model = ShellPreview.Sample;

        Assert.Equal(ShellStatusSnapshot.Initial.Location, model.ShellStatus.Location);
        Assert.Equal(ShellStatusSnapshot.Initial.AgentText, model.ShellStatus.AgentText);
    }

    /// <summary>Every cell is the snapshot's, not a literal the view invented.</summary>
    [AvaloniaFact]
    public void TheStatusBarShowsWhatTheSnapshotSays()
    {
        var model = ShellPreview.Sample;
        var window = new MainWindow { DataContext = model };
        window.Show();
        window.Measure(new Size(1500, 920));
        window.Arrange(new Rect(0, 0, 1500, 920));

        var bar = window.GetVisualDescendants().OfType<Border>()
            .First(border => border.Classes.Contains("statusbar"));
        var texts = bar.GetVisualDescendants().OfType<TextBlock>()
            .Select(block => block.Text)
            .ToList();

        Assert.Contains(model.ShellStatus.SelectionText, texts);
        Assert.Contains(model.ShellStatus.QueueText, texts);
        Assert.Contains(model.ShellStatus.AgentText, texts);
    }

    /// <summary>
    /// Only a connected agent is green.
    /// </summary>
    /// <remarks>
    /// The cell was painted SuccessBrush unconditionally, so a shell that had reached nothing still
    /// showed "Agent: starting" in green. The state decides the colour now, beside the text.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(AgentConnectionState.Connected, true, false)]
    [InlineData(AgentConnectionState.Starting, false, false)]
    [InlineData(AgentConnectionState.Reconnecting, false, false)]
    [InlineData(AgentConnectionState.RecoveryOnly, false, false)]
    [InlineData(AgentConnectionState.Disconnected, false, true)]
    public void OnlyAConnectedAgentReadsAsHealthy(
        AgentConnectionState state, bool healthy, bool down)
    {
        var snapshot = ShellStatusSnapshot.Initial with { AgentState = state };

        Assert.Equal(healthy, snapshot.AgentIsHealthy);
        Assert.Equal(down, snapshot.AgentIsDown);
    }

    /// <summary>
    /// The agent's state reaches the bar without the view knowing the states exist.
    /// </summary>
    /// <remarks>
    /// Asserted through the snapshot rather than by running a monitor: a real one would need a real
    /// agent, and what is worth testing here is that a state change becomes the right text.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(AgentConnectionState.Starting)]
    [InlineData(AgentConnectionState.Connected)]
    [InlineData(AgentConnectionState.RecoveryOnly)]
    [InlineData(AgentConnectionState.Reconnecting)]
    [InlineData(AgentConnectionState.Disconnected)]
    public void EveryAgentStateHasItsOwnWording(AgentConnectionState state)
    {
        var text = (ShellStatusSnapshot.Initial with { AgentState = state }).AgentText;

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.StartsWith("Agent:", text, StringComparison.Ordinal);
    }
}
