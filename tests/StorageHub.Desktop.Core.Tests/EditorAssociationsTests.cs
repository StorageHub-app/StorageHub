using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Configuration;

namespace StorageHub.Desktop.Tests;

public sealed class EditorAssociationsTests
{
    private static string Editor(string name) => TestPaths.Rooted("tools/" + name);

    [Theory]
    [InlineData("readme.TXT", "text")]
    [InlineData("archive.tar.gz", "archive")]
    [InlineData("archive.gz", "gzip")]
    [InlineData("readme.txt.bak", "default")]
    [InlineData("README", "default")]
    public void MatchingUsesTheLongestExtensionThenTheDefaultEditor(string fileName, string expected)
    {
        var preferences = DesktopUpdatePreferences.Defaults with
        {
            ExternalEditorPath = Editor("default"),
            FileTypeEditors = new Dictionary<string, string>
            {
                ["*.txt"] = Editor("text"), ["gz"] = Editor("gzip"), [".tar.gz"] = Editor("archive")
            }
        };
        Assert.Equal(Editor(expected), EditorAssociations.Resolve(preferences, fileName));
    }

    [Fact]
    public void AnUnmatchedFileUsesTheSystemAssociationWhenThereIsNoDefaultEditor() =>
        Assert.Null(EditorAssociations.Resolve(DesktopUpdatePreferences.Defaults, "file.unknown"));

    [Fact]
    public void InvalidRulesAreDroppedAndDuplicatesAreNormalized()
    {
        var rules = EditorAssociations.Normalize(new Dictionary<string, string>
        {
            [".TXT"] = Editor("first"), ["*.txt"] = Editor("second"), ["../txt"] = Editor("bad"),
            [".log"] = "relative/editor", ["*"] = Editor("bad")
        });
        Assert.Equal(Editor("second"), Assert.Single(rules).Value);
        Assert.Equal(".txt", Assert.Single(rules).Key);
    }

    [Fact]
    public void RulesPersistWithoutTriggeringRepeatedRepairAndTravelOnlyInMachineSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "storagehub-editor-rules-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DesktopConfigStore(root);
            store.Preflight();
            var expected = DesktopUpdatePreferences.Defaults with
            {
                ExternalEditorPath = Editor("default"),
                FileTypeEditors = new Dictionary<string, string> { [".txt"] = Editor("text") }
            };
            store.Save(expected);
            var reloaded = new DesktopConfigStore(root);
            Assert.False(reloaded.Preflight().HasFindings);
            Assert.Equal(Editor("text"), reloaded.Load().FileTypeEditors![".txt"]);
            var section = DesktopPreferenceSectionMapper.CaptureMachineSpecific(reloaded.Load());
            Assert.Equal(Editor("text"), section.FileTypeEditors![".txt"]);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void OldOneMiBDefaultMigratesButANewExplicitOneMiBLimitIsPreserved()
    {
        var root = Path.Combine(Path.GetTempPath(), "storagehub-editor-limit-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var store = new DesktopConfigStore(root);
            File.WriteAllText(store.GeneralPath, "{\"schemaVersion\":1,\"maximumEditableFileBytes\":1048576}");
            store.Preflight();
            Assert.Equal(100 * 1024 * 1024, store.Load().MaximumEditableFileBytes);
            store.Save(store.Load() with { MaximumEditableFileBytes = EditableFileIpcContract.InlineContentBytes });
            var reloaded = new DesktopConfigStore(root);
            reloaded.Preflight();
            Assert.Equal(EditableFileIpcContract.InlineContentBytes, reloaded.Load().MaximumEditableFileBytes);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
