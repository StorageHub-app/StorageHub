namespace StorageHub.Desktop.Tests;

/// <summary>
/// Saving a workspace under a chosen filename names it after that file. The two used to be
/// independent, so picking "Fraghunt.shw" left the tab still reading "Workspace 1" and the
/// filename visible only in a tooltip.
/// </summary>
public sealed class WorkspaceNamingTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"storagehub-workspace-name-{Guid.NewGuid():N}");

    [Fact]
    public void Saving_a_new_workspace_names_it_after_the_file()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var workspace = NewWorkspace("Workspace 1");
            var path = Path.Combine(_directory, "Fraghunt.shw");

            workspace.Save(path);

            Assert.Equal("Fraghunt", workspace.WorkspaceName);

            // The rename is folded into the same save, so the workspace is not left asking to be
            // saved again over a change it made itself.
            Assert.False(workspace.IsDirty);
        });
    }

    [Fact]
    public void The_saved_file_carries_the_new_name()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var workspace = NewWorkspace("Workspace 1");
            var path = Path.Combine(_directory, "Servers.shw");

            workspace.Save(path);

            // Named before the capture, so reopening shows the same thing the tab does.
            Assert.Equal("Servers", WorkspaceFileStore.Load(path).Name);
        });
    }

    [Fact]
    public void A_name_somebody_chose_survives_being_saved_under_another_filename()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var workspace = NewWorkspace("Workspace 1")!;
            workspace.WorkspaceName = "Production";

            workspace.Save(Path.Combine(_directory, "backup-2026.shw"));

            // Renaming is deliberate; a filename chosen afterwards must not undo it.
            Assert.Equal("Production", workspace.WorkspaceName);
        });
    }

    [Fact]
    public void Saving_again_over_the_same_file_does_not_rename_anything()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var workspace = NewWorkspace("Workspace 1");
            var path = Path.Combine(_directory, "First.shw");
            workspace.Save(path);
            workspace.WorkspaceName = "Renamed later";

            workspace.Save(path);

            Assert.Equal("Renamed later", workspace.WorkspaceName);
        });
    }

    [Fact]
    public void A_workspace_opened_from_a_file_keeps_the_name_the_file_carried()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var workspace = NewWorkspace("Nightly audit");
            workspace.MarkNameAsExplicit();

            workspace.Save(Path.Combine(_directory, "totally-different.shw"));

            Assert.Equal("Nightly audit", workspace.WorkspaceName);
        });
    }

    private WorkspaceControl NewWorkspace(string name)
    {
        Directory.CreateDirectory(_directory);
        return new WorkspaceControl(name, WorkspaceLayoutModel.CreatePreset(1, WorkspaceLayout.SideBySide));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
