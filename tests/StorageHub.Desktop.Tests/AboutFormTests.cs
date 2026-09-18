using System.Reflection;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// About: the version, and the way back to the project.
/// </summary>
public sealed class AboutFormTests
{
    [Fact]
    public void TheProjectLinkPointsAtTheSamePlaceTheUpdaterTrusts()
    {
        // One address, not two. An About box that names a repository the updater does not use is
        // how a fork ends up sending people to the original project's issue tracker.
        Assert.Equal("https://github.com/StorageHub-app/StorageHub", AboutForm.ProjectUrl);
        Assert.Equal(VelopackDesktopUpdateEngineFactory.TrustedRepositoryUrl, AboutForm.ProjectUrl);
    }

    [Fact]
    public void TheDialogShowsTheVersionAndOffersTheLink()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var about = Create();
            about.CreateControl();
            about.PerformLayout();

            var labels = Descendants(about).OfType<Label>().Select(label => label.Text).ToList();
            Assert.Contains(labels, text => text.Contains(DesktopApplicationVersion.Current, StringComparison.Ordinal));

            var link = Assert.Single(Descendants(about).OfType<LinkLabel>());
            Assert.Equal("The project on GitHub", link.Text);

            // The address is what a screen reader needs; the caption alone does not say where the
            // link goes.
            Assert.Equal(AboutForm.ProjectUrl, link.AccessibleDescription);
        });
    }

    [Fact]
    public void TheLinkIsTranslatedWithTheRestOfTheDialog()
    {
        SyncRunReviewControlTests.RunOnSta(() => ShippedTranslationProvider.InCulture("da-DK", () =>
        {
            using var about = Create();
            about.CreateControl();

            var link = Assert.Single(Descendants(about).OfType<LinkLabel>());
            Assert.Equal("Projektet på GitHub", link.Text);

            // The address itself is never translated, whatever the caption says.
            Assert.Equal(AboutForm.ProjectUrl, link.AccessibleDescription);
        }));
    }

    private static AboutForm Create()
    {
        var constructor = typeof(AboutForm).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [],
            modifiers: null);
        Assert.NotNull(constructor);
        return Assert.IsType<AboutForm>(constructor.Invoke([]));
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
