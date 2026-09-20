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
    /// The ".." row's caption. Its Name is always "..", so the column worth reading is the type.
    /// </summary>
    /// <remarks>
    /// This reached into BrowserPaneControl by reflection, because the row was a private static on
    /// the control. It is BrowserParentNavigation now, and asking it directly is both the honest
    /// way to observe it and what lets this suite leave the Windows-only project.
    /// </remarks>
    private static string ParentRowText() => BrowserParentNavigation.Item.Type;
}
