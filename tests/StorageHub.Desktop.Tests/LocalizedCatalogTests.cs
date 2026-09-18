using StorageHub.Contracts.Ipc;
using StorageHub.Domain;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The catalogs of text that the shell builds once and hands to many windows.
/// </summary>
/// <remarks>
/// Each of these used to be a static field, so its language was decided by whichever window
/// happened to be built first in the process and never changed again. In the application that is
/// invisible -- the language is chosen before the first window exists, and changing it restarts
/// the shell -- but it means the text is correct by luck rather than by rule, and a test that
/// rendered one window in German left every later test reading German provider names.
/// </remarks>
public sealed class LocalizedCatalogTests
{
    [Fact]
    public void ProviderTextFollowsTheCurrentLanguage()
    {
        var english = ConnectionProviderCatalog.Get(StorageProviderKind.S3);
        Assert.Equal("S3 / Object Storage", english.DisplayName);

        ShippedTranslationProvider.InCulture("de-DE", () =>
        {
            var german = ConnectionProviderCatalog.Get(StorageProviderKind.S3);
            Assert.Equal("S3 / Objektspeicher", german.DisplayName);

            // The fields too: the failure this guards against reached them and not just the name.
            Assert.All(
                german.AuthenticationFields,
                field => Assert.NotEqual(string.Empty, field.Label));
        });

        Assert.Equal("S3 / Object Storage", ConnectionProviderCatalog.Get(StorageProviderKind.S3).DisplayName);
    }

    [Fact]
    public void SyncModeTextFollowsTheCurrentLanguage()
    {
        var english = Mode(SyncModeKind.ExactMirror);

        string? german = null;
        ShippedTranslationProvider.InCulture("de-DE", () => german = Mode(SyncModeKind.ExactMirror));

        Assert.NotNull(german);
        Assert.NotEqual(english, german);
        Assert.Equal(english, Mode(SyncModeKind.ExactMirror));
    }

    [Fact]
    public void ParentFolderRowFollowsTheCurrentLanguage()
    {
        var english = ParentRowText();

        string? danish = null;
        ShippedTranslationProvider.InCulture("da-DK", () => danish = ParentRowText());

        Assert.NotNull(danish);
        Assert.NotEqual(english, danish);
        Assert.Equal(english, ParentRowText());
    }

    private static string Mode(SyncModeKind kind) =>
        SyncPresentationCatalog.AllModes.Single(mode => mode.Kind == kind).DisplayName;

    /// <summary>
    /// The ".." row's caption, read through the pane the way a listing does. Reflection rather
    /// than a public seam: the row is an implementation detail of the pane, and making it public
    /// to observe it would be a worse trade than reaching for it here.
    /// </summary>
    private static string ParentRowText()
    {
        var property = typeof(BrowserPaneControl).GetProperty(
            "ParentNavigationItem",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(property);
        // Its Name is always "..", so the caption worth reading is the type column.
        return Assert.IsType<BrowserListItem>(property.GetValue(null)).Type;
    }
}
