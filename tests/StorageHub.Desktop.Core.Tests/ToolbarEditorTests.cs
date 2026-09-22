namespace StorageHub.Desktop.Tests;

/// <summary>
/// Arranging a toolbar: what can be added, where it lands, and what the lists read as.
/// </summary>
/// <remarks>
/// What a stored layout <em>means</em> is ToolbarLayoutTests' job. This is about editing one, and
/// it exists because these rules used to live inside a UserControl and could only be reached by
/// driving list boxes.
/// </remarks>
public sealed class ToolbarEditorTests
{
    [Fact]
    public void AnEmptyLayoutStartsFromTheDefault()
    {
        Assert.Equal(ToolbarLayout.Default, new ToolbarEditor(null).Items);
        Assert.Equal(ToolbarLayout.Default, new ToolbarEditor([]).Items);
    }

    /// <summary>
    /// The toolbar renders only the commands that are wired up, so those are the only ones
    /// offered: adding any other would put a button on the bar that never appears.
    /// </summary>
    [Fact]
    public void OnlyWiredCommandsAreOffered()
    {
        var available = new ToolbarEditor([]).Available();

        Assert.NotEmpty(available);
        Assert.All(available, choice => Assert.True(UiCommandCatalog.IsAvailable(choice.Id)));
    }

    /// <summary>What is already on the toolbar is not offered again.</summary>
    [Fact]
    public void WhatIsAlreadyOnTheToolbarIsNotOffered()
    {
        var editor = new ToolbarEditor([]);
        var onBar = editor.Items.Where(entry => entry != ToolbarLayout.Separator).ToHashSet(StringComparer.Ordinal);

        Assert.All(editor.Available(), choice => Assert.DoesNotContain(choice.Id, onBar));
    }

    /// <summary>A command names the menu it lives under, in the language the shell is in.</summary>
    [Fact]
    public void ACommandIsLabelledWithItsMenu()
    {
        var choice = new ToolbarEditor([]).Available()[0];
        var definition = UiCommandCatalog.GetDefinition(choice.Id);

        Assert.StartsWith(UiCommandCatalog.MenuTitle(definition.Menu), choice.Label, StringComparison.Ordinal);
        Assert.DoesNotContain('&', choice.Label);
        // The list shows the label, so a row needs no template of its own.
        Assert.Equal(choice.Label, choice.ToString());
    }

    [Fact]
    public void AddingPutsACommandAfterTheSelectedEntry()
    {
        var editor = new ToolbarEditor([UiCommandIds.ViewRefresh, UiCommandIds.ToolsSettings]);

        Assert.True(editor.Add(UiCommandIds.GoUp, at: 0));

        Assert.Equal([UiCommandIds.ViewRefresh, UiCommandIds.GoUp, UiCommandIds.ToolsSettings], editor.Items);
    }

    [Fact]
    public void AddingWithNothingSelectedPutsItAtTheEnd()
    {
        var editor = new ToolbarEditor([UiCommandIds.ViewRefresh]);

        Assert.True(editor.Add(UiCommandIds.GoUp, at: -1));

        Assert.Equal([UiCommandIds.ViewRefresh, UiCommandIds.GoUp], editor.Items);
    }

    /// <summary>
    /// A command can be on the toolbar once. Sanitise drops a duplicate on the next load, so
    /// accepting one here would look like the edit had been forgotten.
    /// </summary>
    [Fact]
    public void ACommandCannotBeAddedTwice()
    {
        var editor = new ToolbarEditor([UiCommandIds.ViewRefresh]);

        Assert.False(editor.Add(UiCommandIds.ViewRefresh));

        Assert.Equal([UiCommandIds.ViewRefresh], editor.Items);
    }

    /// <summary>Dividers are the one entry that may repeat, so they go in by their own door.</summary>
    [Fact]
    public void ASeparatorIsNotAddedAsACommand()
    {
        var editor = new ToolbarEditor([UiCommandIds.ViewRefresh]);

        Assert.False(editor.Add(ToolbarLayout.Separator));
        Assert.True(editor.AddSeparator());
        Assert.True(editor.AddSeparator());

        Assert.Equal(
            [UiCommandIds.ViewRefresh, ToolbarLayout.Separator, ToolbarLayout.Separator],
            editor.Items);
    }

    [Fact]
    public void RemovingTakesTheEntryAtThatIndex()
    {
        var editor = new ToolbarEditor([UiCommandIds.ViewRefresh, UiCommandIds.GoUp]);

        Assert.False(editor.RemoveAt(-1));
        Assert.False(editor.RemoveAt(2));
        Assert.True(editor.RemoveAt(0));

        Assert.Equal([UiCommandIds.GoUp], editor.Items);
    }

    [Fact]
    public void MovingSwapsWithTheNeighbourAndSaysWhereItLanded()
    {
        var editor = new ToolbarEditor([UiCommandIds.ViewRefresh, UiCommandIds.GoUp]);

        Assert.Equal(1, editor.Move(0, 1));
        Assert.Equal([UiCommandIds.GoUp, UiCommandIds.ViewRefresh], editor.Items);

        // Off either end is refused rather than clamped: a button already at the top has not moved.
        Assert.Equal(-1, editor.Move(0, -1));
        Assert.Equal(-1, editor.Move(1, 1));
        Assert.Equal([UiCommandIds.GoUp, UiCommandIds.ViewRefresh], editor.Items);
    }

    [Fact]
    public void ResettingReplacesTheLayoutWithThePreset()
    {
        var editor = new ToolbarEditor([UiCommandIds.ViewRefresh]);

        Assert.True(editor.Reset(ToolbarPreset.Expanded));
        Assert.Equal(ToolbarLayout.Preset(ToolbarPreset.Expanded), editor.Items);

        // Already that preset, so nothing changed and the window should not go unsaved.
        Assert.False(editor.Reset(ToolbarPreset.Expanded));
    }

    /// <summary>
    /// A divider reads as one; everything else reads as its command, without mnemonics.
    /// </summary>
    /// <remarks>
    /// The divider goes in the middle because a trailing one is trimmed on the way in -- a
    /// toolbar cannot end in a divider, which is <see cref="ToolbarLayout.Sanitise"/>'s rule.
    /// </remarks>
    [Fact]
    public void TheToolbarIsDescribedInWords()
    {
        var editor = new ToolbarEditor(
            [UiCommandIds.ViewRefresh, ToolbarLayout.Separator, UiCommandIds.ToolsSettings]);

        var described = editor.Describe();

        Assert.Equal(3, described.Count);
        Assert.Equal(Localization.Ui.Settings.ToolbarSeparator, described[1]);
        Assert.DoesNotContain('&', described[0]);
        Assert.False(string.IsNullOrWhiteSpace(described[0]));
        Assert.False(string.IsNullOrWhiteSpace(described[2]));
    }

    /// <summary>
    /// What the editor produces is what the toolbar will render, so a round trip through the
    /// sanitiser cannot change it.
    /// </summary>
    [Fact]
    public void WhatTheEditorProducesSurvivesBeingStored()
    {
        var editor = new ToolbarEditor([]);
        editor.Reset(ToolbarPreset.Expanded);
        _ = editor.AddSeparator(0);
        _ = editor.RemoveAt(2);

        Assert.Equal(editor.Items, ToolbarLayout.Sanitise(editor.Items));
    }
}
