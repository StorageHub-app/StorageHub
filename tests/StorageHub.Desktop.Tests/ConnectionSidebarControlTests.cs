using System.Reflection;
using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop.Tests;

public sealed class ConnectionSidebarControlTests
{
    [Fact]
    public void SidebarBuildsNestedSearchableGroupsAndRaisesConnectionSelection()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            var productionId = Guid.NewGuid();
            var shellId = Guid.NewGuid();
            ConnectionCardModel[] cards =
            [
                Card(productionId, "Production S3", StorageProviderKind.S3, "FRAGHUNT/Production"),
                Card(Guid.NewGuid(), "Loose SFTP", StorageProviderKind.Sftp, null),
                Card(shellId, "Admin shell", StorageProviderKind.Ssh, "Servers/Linux")
            ];
            using var sidebar = new ConnectionSidebarControl { Size = new Size(320, 700) };
            sidebar.CreateControl();
            ConnectionCardModel? selected = null;
            sidebar.ConnectionSelected += (_, card) => selected = card;

            sidebar.SetConnections(cards, searchText: null, selectedConnectionId: null);

            Assert.Equal(
                ["Storage", "Remote clients"],
                Descendants<ConnectionSidebarSectionHeader>(sidebar).Select(static header => header.Text));
            Assert.Contains(Descendants<ConnectionSidebarGroup>(sidebar), static group => group.Name == "storage/FRAGHUNT");
            Assert.Contains(Descendants<ConnectionSidebarGroup>(sidebar), static group => group.Name == "storage/FRAGHUNT/Production");
            Assert.Contains(Descendants<ConnectionSidebarGroup>(sidebar), static group => group.Name == "storage/Unsorted");
            var shell = Assert.Single(Descendants<ConnectionSidebarItem>(sidebar), item => item.Connection.ConnectionId == shellId);

            typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(shell, [EventArgs.Empty]);

            Assert.Equal(shellId, sidebar.SelectedConnectionId);
            Assert.Equal(shellId, selected?.ConnectionId);

            sidebar.SetConnections(cards, "production", productionId);
            var result = Assert.Single(Descendants<ConnectionSidebarItem>(sidebar));
            Assert.Equal(productionId, result.Connection.ConnectionId);
            Assert.True(result.Selected);
        });
    }

    [Fact]
    public void AFavouriteIsListedUnderFavouritesAndAgainInItsOwnFolder()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            var favouriteId = Guid.NewGuid();
            var retiredId = Guid.NewGuid();
            ConnectionCardModel[] cards =
            [
                Card(favouriteId, "Pinned S3", StorageProviderKind.S3, "FRAGHUNT") with { IsFavorite = true },
                Card(retiredId, "Old FTP", StorageProviderKind.Ftp, "FRAGHUNT") with { IsEnabled = false },
                Card(Guid.NewGuid(), "Plain S3", StorageProviderKind.S3, "FRAGHUNT")
            ];
            using var sidebar = new ConnectionSidebarControl { Size = new Size(320, 700) };
            sidebar.CreateControl();

            sidebar.SetConnections(cards, searchText: null, selectedConnectionId: null);

            Assert.Equal(
                ["Favorites", "Storage", "Disabled"],
                Descendants<ConnectionSidebarSectionHeader>(sidebar).Select(static header => header.Text));

            // Twice: once as the shortcut, once where it actually lives. Losing it from its folder
            // is confusing when the folder is how you think about it.
            Assert.Equal(
                2,
                Descendants<ConnectionSidebarItem>(sidebar).Count(item => item.Connection.ConnectionId == favouriteId));
            Assert.Single(Descendants<ConnectionSidebarItem>(sidebar), item => item.Connection.ConnectionId == retiredId);
        });
    }

    [Fact]
    public void Both_copies_of_a_favourite_select_together()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            var favouriteId = Guid.NewGuid();
            ConnectionCardModel[] cards =
            [
                Card(favouriteId, "Pinned S3", StorageProviderKind.S3, "FRAGHUNT") with { IsFavorite = true }
            ];
            using var sidebar = new ConnectionSidebarControl { Size = new Size(320, 700) };
            sidebar.CreateControl();

            sidebar.SetConnections(cards, searchText: null, selectedConnectionId: favouriteId);

            // The rows share an id, so highlighting only one would leave its twin looking dead.
            var rows = Descendants<ConnectionSidebarItem>(sidebar)
                .Where(item => item.Connection.ConnectionId == favouriteId)
                .ToArray();
            Assert.Equal(2, rows.Length);
            Assert.All(rows, row => Assert.True(row.Selected));
        });
    }

    [Fact]
    public void Turning_the_option_off_lists_a_favourite_only_under_favourites()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            var favouriteId = Guid.NewGuid();
            ConnectionCardModel[] cards =
            [
                Card(favouriteId, "Pinned S3", StorageProviderKind.S3, "FRAGHUNT") with { IsFavorite = true },
                Card(Guid.NewGuid(), "Plain S3", StorageProviderKind.S3, "FRAGHUNT")
            ];
            using var sidebar = new ConnectionSidebarControl
            {
                Size = new Size(320, 700),

                // With many folders and connections the repetition is more noise than help.
                ShowFavoritesInTheirFolders = false
            };
            sidebar.CreateControl();

            sidebar.SetConnections(cards, searchText: null, selectedConnectionId: null);

            Assert.Single(Descendants<ConnectionSidebarItem>(sidebar), item => item.Connection.ConnectionId == favouriteId);
        });
    }

    [Fact]
    public void RowActionsStayOutOfTheWayUntilTheListAsksForThem()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var sidebar = new ConnectionSidebarControl { Size = new Size(320, 700) };
            sidebar.CreateControl();
            var edits = 0;
            sidebar.ConnectionEditRequested += (_, _) => edits++;

            sidebar.SetConnections([Card(Guid.NewGuid(), "Plain S3", StorageProviderKind.S3, null)], null, null);
            var row = Assert.Single(Descendants<ConnectionSidebarItem>(sidebar));
            RaiseMouseUp(row, Centre(row.EditBounds));

            // Without ShowRowActions the pencil is not there, so that click is an ordinary select.
            Assert.Equal(0, edits);
            Assert.Equal(0, row.AccessibilityObject.GetChildCount());
        });
    }

    [Fact]
    public void ClickingTheInlineIconsEditsAndDeletesThatConnection()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            var id = Guid.NewGuid();
            using var sidebar = new ConnectionSidebarControl { Size = new Size(320, 700), ShowRowActions = true };
            sidebar.CreateControl();
            Guid? edited = null;
            Guid? deleted = null;
            sidebar.ConnectionEditRequested += (_, card) => edited = card.ConnectionId;
            sidebar.ConnectionDeleteRequested += (_, card) => deleted = card.ConnectionId;

            sidebar.SetConnections([Card(id, "Plain S3", StorageProviderKind.S3, null)], null, null);
            var row = Assert.Single(Descendants<ConnectionSidebarItem>(sidebar));

            RaiseMouseUp(row, Centre(row.EditBounds));
            Assert.Equal(id, edited);

            RaiseMouseUp(row, Centre(row.DeleteBounds));
            Assert.Equal(id, deleted);
        });
    }

    [Fact]
    public void ClickingTheRowBodyStillSelectsRatherThanActingOnIt()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            var id = Guid.NewGuid();
            using var sidebar = new ConnectionSidebarControl { Size = new Size(320, 700), ShowRowActions = true };
            sidebar.CreateControl();
            var actions = 0;
            sidebar.ConnectionEditRequested += (_, _) => actions++;
            sidebar.ConnectionDeleteRequested += (_, _) => actions++;
            sidebar.SetConnections([Card(id, "Plain S3", StorageProviderKind.S3, null)], null, null);
            var row = Assert.Single(Descendants<ConnectionSidebarItem>(sidebar));

            RaiseMouseUp(row, new Point(70, row.Height / 2));

            Assert.Equal(0, actions);
            Assert.Equal(id, sidebar.SelectedConnectionId);
        });
    }

    [Fact]
    public void TheKeyboardReachesOpenEditAndDeleteWithoutTheMouse()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            var id = Guid.NewGuid();
            using var sidebar = new ConnectionSidebarControl { Size = new Size(320, 700), ShowRowActions = true };
            sidebar.CreateControl();
            Guid? activated = null;
            Guid? edited = null;
            Guid? deleted = null;
            sidebar.ConnectionActivated += (_, card) => activated = card.ConnectionId;
            sidebar.ConnectionEditRequested += (_, card) => edited = card.ConnectionId;
            sidebar.ConnectionDeleteRequested += (_, card) => deleted = card.ConnectionId;
            sidebar.SetConnections([Card(id, "Plain S3", StorageProviderKind.S3, null)], null, null);
            var row = Assert.Single(Descendants<ConnectionSidebarItem>(sidebar));

            RaiseKeyDown(row, Keys.Enter);
            RaiseKeyDown(row, Keys.F2);
            RaiseKeyDown(row, Keys.Delete);

            Assert.Equal(id, activated);
            Assert.Equal(id, edited);
            Assert.Equal(id, deleted);
        });
    }

    [Fact]
    public void EachRowPublishesItsIconsAsNamedAccessibleButtons()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var sidebar = new ConnectionSidebarControl { Size = new Size(320, 700), ShowRowActions = true };
            sidebar.CreateControl();
            sidebar.SetConnections([Card(Guid.NewGuid(), "Plain S3", StorageProviderKind.S3, null)], null, null);
            var row = Assert.Single(Descendants<ConnectionSidebarItem>(sidebar));

            var accessible = row.AccessibilityObject;

            Assert.Equal(2, accessible.GetChildCount());
            var edit = accessible.GetChild(0)!;
            var delete = accessible.GetChild(1)!;
            Assert.Equal("Edit connection Plain S3", edit.Name);
            Assert.Equal("Delete connection Plain S3", delete.Name);
            Assert.Equal(AccessibleRole.PushButton, edit.Role);
            Assert.Equal(AccessibleRole.PushButton, delete.Role);

            var deleted = false;
            sidebar.ConnectionDeleteRequested += (_, _) => deleted = true;
            delete.DoDefaultAction();
            Assert.True(deleted);
        });
    }

    [Fact]
    public void DoubleClickingARowOpensItAndSelectsIt()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            var id = Guid.NewGuid();
            using var sidebar = new ConnectionSidebarControl { Size = new Size(320, 700), ShowRowActions = true };
            sidebar.CreateControl();
            var activations = 0;
            sidebar.ConnectionActivated += (_, _) => activations++;
            sidebar.SetConnections([Card(id, "Plain S3", StorageProviderKind.S3, null)], null, null);
            var row = Assert.Single(Descendants<ConnectionSidebarItem>(sidebar));

            typeof(Control).GetMethod("OnDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(row, [EventArgs.Empty]);

            Assert.Equal(1, activations);
            Assert.Equal(id, sidebar.SelectedConnectionId);
        });
    }

    private static Point Centre(Rectangle bounds) =>
        new(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));

    [Fact]
    public void Groups_span_the_pane_even_once_the_list_is_long_enough_to_scroll()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            var cards = Enumerable.Range(0, 30)
                .Select(index => Card(Guid.NewGuid(), $"Server {index}", StorageProviderKind.Sftp, "FRAGHUNT"))
                .ToArray();
            using var sidebar = new ConnectionSidebarControl { Size = new Size(320, 400) };
            sidebar.CreateControl();

            sidebar.SetConnections(cards, searchText: null, selectedConnectionId: null);

            var content = Assert.Single(sidebar.Controls.OfType<FlowLayoutPanel>());
            Assert.True(content.VerticalScroll.Visible, "the list should be long enough to scroll");
            var expected = content.ClientSize.Width - content.Padding.Horizontal;
            foreach (Control row in content.Controls)
            {
                Assert.Equal(expected, row.Width);
            }
        });
    }

    private static void RaiseMouseUp(Control control, Point location) =>
        typeof(Control).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(control, [new MouseEventArgs(MouseButtons.Left, 1, location.X, location.Y, 0)]);

    private static void RaiseKeyDown(Control control, Keys key) =>
        typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(control, [new KeyEventArgs(key)]);

    private static ConnectionCardModel Card(
        Guid id,
        string name,
        StorageProviderKind provider,
        string? folder) => new(
        name,
        provider,
        $"{provider} endpoint",
        "Saved",
        ConnectionId: id,
        FolderPath: folder);

    private static IEnumerable<T> Descendants<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
