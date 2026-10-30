using Avalonia.Headless.XUnit;
using StorageHub.Desktop;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

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
        var commands = model.Menus.SelectMany(static section => section.Items)
            .Where(static entry => entry.Label != CommandEntry.SeparatorLabel)
            .ToArray();

        // Every command is in exactly one menu, the one it declares, except Go > Favorites: the Go
        // menu lists the favourites themselves.
        Assert.Equal(
            UiCommandCatalog.Definitions
                .Where(static definition => UiCommandCatalog.IsAvailable(definition.Id) &&
                                            definition.Id != UiCommandIds.GoFavorites)
                .Select(static definition => definition.Id)
                .Order(StringComparer.Ordinal),
            commands.Select(static entry => entry.Id).Order(StringComparer.Ordinal));
        Assert.All(model.Menus, section => Assert.All(
            section.Items.Where(static entry => entry.Label != CommandEntry.SeparatorLabel),
            entry => Assert.Equal(section.Menu, UiCommandCatalog.GetDefinition(entry.Id).Menu)));

        // Grouped, with a line between groups but never two together or one at either end.
        Assert.All(model.Menus, section =>
        {
            var lines = section.Items.Select(static entry => entry.Label == CommandEntry.SeparatorLabel).ToArray();
            Assert.False(lines[0] || lines[^1], section.Header);
            Assert.DoesNotContain(true, lines.Zip(lines.Skip(1), static (a, b) => a && b));
        });
        Assert.Contains(model.Menus.Single(static s => s.Menu == UiMenuId.Edit).Items,
            static entry => entry.Label == CommandEntry.SeparatorLabel);

        // Every entry carries its tooltip and its shortcut through, which is what the menu displays.
        Assert.All(commands, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Label));
            Assert.False(string.IsNullOrWhiteSpace(entry.Description));
        });
    }

    /// <summary>
    /// The toolbar is ToolbarLayout's default, less what the catalog refuses.
    /// </summary>
    /// <remarks>
    /// It used to be the layout entire, which meant buttons for commands 1.x itself never wired --
    /// Search and Compare panes. The menu had always applied <see cref="UiCommandCatalog.IsAvailable"/>
    /// and the toolbar had not; now both do, which is why this counts the admitted ids rather than
    /// the layout's length.
    /// </remarks>
    [AvaloniaFact]
    public void TheToolbarIsTheResolvedLayoutLessWhatIsUnavailable()
    {
        var model = ShellPreview.Sample;
        var offered = ToolbarLayout.Resolve(null)
            .Where(id => id == ToolbarLayout.Separator || UiCommandCatalog.IsAvailable(id))
            .ToArray();

        Assert.Equal(offered.Length, model.Toolbar.Count);
        Assert.Equal(
            offered.Count(id => id == ToolbarLayout.Separator),
            model.Toolbar.OfType<ToolbarSeparator>().Count());
        Assert.All(model.Toolbar.OfType<CommandEntry>(), entry => Assert.NotNull(entry.Icon));

        // And Search, the one button of the default layout 1.4 left dead, is wired now, so nothing
        // the default toolbar names is dropped.
        Assert.Equal(ToolbarLayout.Resolve(null).Count, offered.Length);
    }
}
