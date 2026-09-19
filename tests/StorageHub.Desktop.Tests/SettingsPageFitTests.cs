using System.Reflection;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The settings pages are laid out against whatever width they are given. Whether that width is
/// narrow enough to expose a bad fit depends on the display's scaling, which is why a page that
/// looks right at 125% can scroll sideways on a 100% build agent. These drive the fit directly at a
/// known width so the outcome does not depend on the machine running them.
/// </summary>
public sealed class SettingsPageFitTests
{
    /// <summary>
    /// Runs the fit in every shipped language, not only the one the pages were authored in.
    /// German runs roughly a third longer than English and Danish compounds its nouns, so a label
    /// that fits in English is not evidence that the page fits at all.
    /// </summary>
    [Theory]
    [MemberData(nameof(ShippedTranslationProvider.Cultures), MemberType = typeof(ShippedTranslationProvider))]
    public void FittingAPageNarrowerThanItsContentLeavesNothingForcingItWider(string culture)
    {
        ShippedTranslationProvider.InCulture(culture, () =>
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var settings = new SettingsForm();
            settings.MinimumSize = Size.Empty;
            settings.Show();

            var pages = GetField<Dictionary<string, Control>>(settings, "_pages");
            Assert.NotEmpty(pages);

            foreach (var entry in pages)
            {
                var page = Assert.IsType<SettingsPagePanel>(entry.Value);

                // The page is sized by its host, so it is detached first: the point here is the
                // fit's behaviour at a given width, and 420 is narrower than the 700px content
                // width the pages are authored against — so any child still pinned to that width
                // shows up here rather than only on a 100% display.
                page.Visible = true;
                page.Dock = DockStyle.None;
                page.Width = 420;
                InvokeFit(settings, page);

                // A maximum is only a cap and cannot widen anything, so the two things that can
                // actually force the page sideways are a minimum wider than it and a child that is
                // simply too wide.
                var available = page.ClientSize.Width;
                foreach (Control child in page.Controls)
                {
                    Assert.True(
                        child.MinimumSize.Width <= available,
                        $"{culture} / {entry.Key}: '{child.Name}' keeps a minimum width of {child.MinimumSize.Width} " +
                        $"inside a {available}px page, which forces a horizontal scrollbar.");
                    Assert.True(
                        child.Width <= available,
                        $"{culture} / {entry.Key}: '{child.Name}' is {child.Width}px wide inside a {available}px page.");
                }
            }
        }));
    }

    [Fact]
    public void RefittingAWiderPageGivesTheContentTheRoomBack()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var settings = new SettingsForm();
            settings.MinimumSize = Size.Empty;
            settings.Show();

            var page = Assert.IsType<SettingsPagePanel>(GetField<Dictionary<string, Control>>(settings, "_pages").Values.First());
            page.Visible = true;
            page.Dock = DockStyle.None;

            page.Width = 420;
            InvokeFit(settings, page);
            var wrapped = page.Controls.OfType<Label>().Where(static label => label.MaximumSize.Width > 0).ToArray();
            Assert.NotEmpty(wrapped);
            Assert.All(wrapped, label => Assert.True(
                label.MaximumSize.Width <= 420,
                $"'{label.Name}' was left wrapping at {label.MaximumSize.Width} in a 420px page."));

            page.Width = 900;
            InvokeFit(settings, page);

            // Shrinking then growing must not leave the text clamped to the narrow width: the fit
            // has to be repeatable, not a one-shot that latches on the first size it sees.
            Assert.All(wrapped, label => Assert.True(
                label.MaximumSize.Width > 420,
                $"'{label.Name}' stayed wrapping at {label.MaximumSize.Width} after the page grew to {page.ClientSize.Width}."));
        });
    }

    /// <summary>
    /// The fit belongs to a window rather than to the type: it caps the content against a width
    /// written in logical units, and a logical unit means nothing without a display to scale it
    /// for. So it is invoked against the form under test.
    /// </summary>
    private static void InvokeFit(SettingsForm settings, FlowLayoutPanel page)
    {
        var method = typeof(SettingsForm).GetMethod(
            "FitSettingsPageContent",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(FlowLayoutPanel)],
            modifiers: null);
        Assert.NotNull(method);
        _ = method.Invoke(settings, [page]);
        System.Windows.Forms.Application.DoEvents();
    }

    private static T GetField<T>(object instance, string name)
        where T : class
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsAssignableFrom<T>(field.GetValue(instance));
    }
}
