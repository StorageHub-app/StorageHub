using System.Reflection;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// A workspace tab draws the close affordance its click handler depends on.
/// </summary>
/// <remarks>
/// The tab strip is owner-drawn, so the close cross is painted by hand rather than being a control
/// with its own state. That means nothing fails if it stops being drawn: the click target in
/// <c>WorkspaceTabsMouseDown</c> keeps working, the command keeps working, and the only symptom is
/// that the way out of a workspace becomes invisible.
///
/// This renders the real strip and looks at the pixels where the cross belongs, because the
/// question is what the user sees and no assertion about <c>Closable</c> answers it.
/// </remarks>
public sealed class WorkspaceTabCloseTests
{
    /// <summary>
    /// Run in every shipped language, because the failure this guards against was language-only.
    /// </summary>
    /// <remarks>
    /// The shared renderer used to be kept off this strip by comparing the control's accessible
    /// name to the English literal "Workspace tabs". Translating that name silently attached the
    /// shared renderer on top of the workspace one, which then painted over the icon and the
    /// cross — in Danish and German only. An English-only test saw nothing wrong.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ShippedTranslationProvider.Cultures), MemberType = typeof(ShippedTranslationProvider))]
    public void AWorkspaceTabDrawsItsCloseCross(string culture)
    {
        ShippedTranslationProvider.InCulture(culture, () =>
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var main = new MainForm();
            var page = main.AddWorkspace(2);
            main.Size = new Size(1400, 900);
            main.CreateControl();
            main.PerformLayout();

            // The shell applies the configured appearance after the window is built, and that walk
            // re-runs ConfigureTabs. Without reproducing it here the shared renderer stays
            // subscribed ahead of the workspace one and is harmlessly painted over, so the very
            // ordering that hides the close cross in the running app never occurs.
            StorageHubTheme.Apply(main);

            var tabs = Field<TabControl>(main, "_workspaceTabs");
            var index = tabs.TabPages.IndexOf(page);
            Assert.True(index >= 0, "The workspace tab was not added to the strip.");
            tabs.SelectedIndex = index;
            main.PerformLayout();
            System.Windows.Forms.Application.DoEvents();

            var tabBounds = tabs.GetTabRect(index);
            var closeBounds = CloseBounds(main, tabBounds);

            using var rendered = new Bitmap(tabs.Width, tabs.Height);
            tabs.DrawToBitmap(rendered, new Rectangle(0, 0, tabs.Width, tabs.Height));

            // Matched against the pen colour rather than "differs from the background". Sampling a
            // background pixel risks landing on the tab's own border, which would make almost any
            // pixel look like a drawn cross and leave this test unable to fail.
            var ink = StorageHubTheme.Text;

            var drawn = 0;
            for (var x = closeBounds.Left; x < closeBounds.Right && x < rendered.Width; x++)
            {
                for (var y = closeBounds.Top; y < closeBounds.Bottom && y < rendered.Height; y++)
                {
                    if (x < 0 || y < 0)
                    {
                        continue;
                    }

                    if (Distance(rendered.GetPixel(x, y), ink) < 60)
                    {
                        drawn++;
                    }
                }
            }

            Assert.True(
                drawn > 0,
                $"No close cross was painted in {closeBounds} on the workspace tab at {tabBounds}. " +
                "The tab is closable and its click target is live, so the affordance is invisible " +
                $"rather than absent. Culture: {culture}.");
        }));
    }

    private static int Distance(Color left, Color right) =>
        Math.Abs(left.R - right.R) + Math.Abs(left.G - right.G) + Math.Abs(left.B - right.B);

    /// <summary>
    /// Asks MainForm where the close box is, rather than restating its arithmetic.
    /// </summary>
    /// <remarks>
    /// A copy of the formula would agree with the original at 96 DPI and quietly disagree
    /// everywhere else, which is precisely the bug this area already had.
    /// </remarks>
    private static Rectangle CloseBounds(MainForm main, Rectangle tabBounds)
    {
        var method = typeof(MainForm).GetMethod(
            "GetWorkspaceCloseBounds",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<Rectangle>(method.Invoke(main, [tabBounds]));
    }

    private static T Field<T>(object instance, string name)
        where T : class
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsAssignableFrom<T>(field.GetValue(instance));
    }
}
