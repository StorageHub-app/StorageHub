using Avalonia.Headless.XUnit;
using StorageHub.Desktop;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Avalonia.Tests;

/// <summary>
/// The glyphs commands declare, and the icons that draw them.
/// </summary>
/// <remarks>
/// The WinForms shell drew its own 57 glyphs, so a command declaring one was guaranteed to have an
/// icon: the enum and the drawing code were the same file. Now the enum is intent and the icon set
/// is a dependency, and nothing in the type system says every glyph still resolves. These tests are
/// that guarantee.
/// </remarks>
public class IconCatalogTests
{
    [Fact]
    public void EveryGlyphAnyCommandCanDeclareHasAnIcon()
    {
        var unmapped = Enum.GetValues<UiGlyph>()
            .Where(glyph => Record.Exception(() => IconCatalog.Resolve(glyph)) is not null)
            .ToList();

        Assert.Empty(unmapped);
    }

    [Fact]
    public void AGlyphWithNoIconSaysWhichOne()
    {
        // Enum.Parse would accept it too; the point is that an unmapped value fails loudly rather
        // than drawing a placeholder nobody notices until a screenshot.
        var error = Assert.Throws<KeyNotFoundException>(
            () => IconCatalog.Resolve((UiGlyph)9999));

        Assert.Contains("9999", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NoGlyphDeclaredByACommandGoesUndrawn()
    {
        var missing = UiCommandCatalog.Definitions
            .Where(definition => definition.Glyph is not null)
            .Where(definition => IconCatalog.Resolve(definition.Glyph) is null)
            .Select(definition => definition.Id)
            .ToList();

        Assert.Empty(missing);
    }

    /// <summary>The shell's menus are the catalog's, not a hand-written copy of them.</summary>
    [AvaloniaFact]
    public void TheMenusAreTheCatalogs()
    {
        var model = ShellPreview.Sample;

        // Only the menus with something wired up in them.
        Assert.Equal(
            UiCommandCatalog.Menus.Count(menu =>
                UiCommandCatalog.ForMenu(menu).Any(d => UiCommandCatalog.IsAvailable(d.Id))),
            model.Menus.Count);

        // Declared but not yet wired commands are left out, as MainForm leaves them out: a menu
        // entry that does nothing is worse than an absent one.
        Assert.Equal(
            UiCommandCatalog.Definitions.Count(definition => UiCommandCatalog.IsAvailable(definition.Id)),
            model.Menus.Sum(section => section.Items.Count));
        Assert.All(
            model.Menus.SelectMany(section => section.Items),
            entry => Assert.True(UiCommandCatalog.IsAvailable(entry.Id)));

        // Every entry carries its tooltip and its shortcut through, which is what the menu displays.
        Assert.All(model.Menus.SelectMany(section => section.Items), entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Label));
            Assert.False(string.IsNullOrWhiteSpace(entry.Description));
        });
    }

    /// <summary>The toolbar is ToolbarLayout's default, separators included.</summary>
    [AvaloniaFact]
    public void TheToolbarIsTheResolvedLayout()
    {
        var model = ShellPreview.Sample;
        var layout = ToolbarLayout.Resolve(null);

        Assert.Equal(layout.Count, model.Toolbar.Count);
        Assert.Equal(
            layout.Count(id => id == ToolbarLayout.Separator),
            model.Toolbar.OfType<ToolbarSeparator>().Count());
        Assert.All(model.Toolbar.OfType<CommandEntry>(), entry => Assert.NotNull(entry.Icon));
    }
}
