using StorageHub.Contracts.Ipc;
using StorageHub.Desktop;
using StorageHub.Desktop.Localization;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// What a pane must have selected before the object inspector can be pointed at it.
/// </summary>
/// <remarks>
/// Three refusals, each its own sentence, because they are answers to a menu click and nothing
/// dims on that path: the button already knows, the menu has to say.
/// </remarks>
public class PaneInspectionTests
{
    private static readonly Guid Connection = Guid.NewGuid();

    [Fact]
    public void OneFileOnASavedConnectionHasAnAddress()
    {
        var address = PaneInspection.AddressFor(Saved(), [File("render.exr", "shots/render.exr")], out var problem);

        Assert.Null(problem);
        Assert.NotNull(address);
        Assert.Equal(Connection, address!.ConnectionId);
        Assert.Equal("root", address.RootIdentity);
        Assert.Equal("shots/render.exr", address.RelativePath);
        Assert.Equal("etag-1", address.EntityTag);
        Assert.True(address.HasValidBounds);
    }

    [Fact]
    public void MoreThanOneOrAFolderIsNotInspectable()
    {
        Assert.Null(PaneInspection.AddressFor(Saved(), [], out var none));
        Assert.Equal(Ui.Shell.SelectOneToInspect, none);

        Assert.Null(PaneInspection.AddressFor(
            Saved(), [File("a.txt", "a.txt"), File("b.txt", "b.txt")], out var two));
        Assert.Equal(Ui.Shell.SelectOneToInspect, two);

        Assert.Null(PaneInspection.AddressFor(Saved(), [Folder("shots")], out var folder));
        Assert.Equal(Ui.Shell.SelectOneToInspect, folder);
    }

    /// <summary>This PC has no object identities, and says so rather than asking the agent.</summary>
    [Fact]
    public void AFileOutsideASavedConnectionIsNotInspectable()
    {
        var thisPc = PaneTransferContext.Create(PaneTransferContextKind.ThisPc, null, null, "C:/data").Value;

        Assert.Null(PaneInspection.AddressFor(thisPc, [File("a.txt", "C:/data/a.txt")], out var problem));
        Assert.Equal(Ui.Shell.InspectionRequiresConnection, problem);

        Assert.Null(PaneInspection.AddressFor(null, [File("a.txt", "a.txt")], out var nowhere));
        Assert.Equal(Ui.Shell.InspectionRequiresConnection, nowhere);
    }

    [Fact]
    public void CanInspectAgreesWithTheAddress()
    {
        Assert.True(PaneInspection.CanInspect(Saved(), [File("a.txt", "a.txt")]));
        Assert.False(PaneInspection.CanInspect(Saved(), [Folder("shots")]));
    }

    /// <summary>
    /// Properties asks for any kind: a folder and a link have permissions and times too, while
    /// external editing, which asks for the same address, still takes a file only.
    /// </summary>
    [Fact]
    public void PropertiesTakesAFolderOrALink()
    {
        var folder = PaneInspection.AddressFor(Saved(), [Folder("shots")], out var problem, anyKind: true);
        Assert.Null(problem);
        Assert.Equal("shots", folder?.RelativePath);

        var link = PaneTransferItem.Create("latest", "latest", StorageItemKind.SymbolicLink, null).Value;
        Assert.True(PaneInspection.CanInspect(Saved(), [link], anyKind: true));
        Assert.False(PaneInspection.CanInspect(Saved(), [link]));

        Assert.Null(PaneInspection.AddressFor(Saved(), [Folder("a"), Folder("b")], out var two, anyKind: true));
        Assert.Equal(Ui.Shell.SelectOneToInspect, two);
    }

    private static PaneTransferContext Saved() =>
        PaneTransferContext.Create(PaneTransferContextKind.SavedConnection, Connection, "root", "shots").Value;

    private static PaneTransferItem File(string name, string path) =>
        PaneTransferItem.Create(name, path, StorageItemKind.File, 1024, entityTag: "etag-1").Value;

    private static PaneTransferItem Folder(string name) =>
        PaneTransferItem.Create(name, name, StorageItemKind.Directory, null).Value;
}
