using StorageHub.Contracts.Ipc;
using StorageHub.Desktop;
using StorageHub.Desktop.Localization;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Files dropped from the desktop become transfer selections, one per folder they came from.
/// </summary>
public sealed class LocalDropsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "storagehub-drops-" + Guid.NewGuid().ToString("N"));

    public LocalDropsTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void FilesFromOneFolderAreOneSelection()
    {
        var a = Write("a.txt", "aa");
        var b = Write("b.txt", "bbb");

        var result = LocalDrops.From([a, b]);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var selection = Assert.Single(result.Value);
        Assert.Equal(PaneTransferContextKind.ThisPc, selection.Context.Kind);
        Assert.Equal(_root, selection.Context.RelativePath);
        Assert.Equal(["a.txt", "b.txt"], selection.Items.Select(static item => item.Name));
        Assert.Equal([2L, 3L], selection.Items.Select(static item => item.Length));
        Assert.All(selection.Items, item => Assert.Equal(StorageItemKind.File, item.Kind));
    }

    /// <summary>A drop can gather files from several folders; each folder is its own transfer.</summary>
    [Fact]
    public void FilesFromTwoFoldersAreTwoSelections()
    {
        var nested = Path.Combine(_root, "nested");
        Directory.CreateDirectory(nested);
        var a = Write("a.txt", "aa");
        var c = Path.Combine(nested, "c.txt");
        File.WriteAllText(c, "c");

        var result = LocalDrops.From([a, c]);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Contains(result.Value, selection => selection.Context.RelativePath == _root);
        Assert.Contains(result.Value, selection => selection.Context.RelativePath == nested);
    }

    /// <summary>A folder is carried as a folder, which the recursive controller then expands.</summary>
    [Fact]
    public void AFolderIsAFolder()
    {
        var folder = Path.Combine(_root, "photos");
        Directory.CreateDirectory(folder);

        var result = LocalDrops.From([folder]);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(Assert.Single(result.Value).Items);
        Assert.Equal(StorageItemKind.Directory, item.Kind);
        Assert.Null(item.Length);
        Assert.True(item.IsContainer);
    }

    /// <summary>Anything that is not a file or folder here refuses the whole drop, with a sentence.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("relative/path.txt")]
    public void AnythingElseRefusesTheDrop(string path)
    {
        var good = Write("a.txt", "aa");

        var result = LocalDrops.From([good, path]);

        Assert.True(result.IsFailure);
        Assert.Equal(Ui.Pane.DroppedItemsUnusable, result.Error.Message);
    }

    [Fact]
    public void AMissingFileRefusesTheDrop()
    {
        var result = LocalDrops.From([Path.Combine(_root, "not-there.txt")]);

        Assert.True(result.IsFailure);
        Assert.Equal(Ui.Pane.DroppedItemsUnusable, result.Error.Message);
        Assert.True(LocalDrops.From([]).IsFailure);
    }

    private string Write(string name, string contents)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, contents);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
