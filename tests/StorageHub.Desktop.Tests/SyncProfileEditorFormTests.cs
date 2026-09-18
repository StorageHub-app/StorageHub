using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop.Tests;

public sealed class SyncProfileEditorFormTests
{
    /// <summary>
    /// The agent deliberately replaces failure text with vague category wording so it cannot leak
    /// endpoint detail, but it keeps an exact code. The dialog used to drop that code and show the
    /// category alone, so a profile the operator had simply left disabled was reported as "the
    /// sync service or an endpoint is temporarily unavailable" -- untrue, and not actionable.
    /// </summary>
    [Theory]
    [InlineData("sync.profile.disabled")]
    [InlineData("sync.profile.not_found")]
    [InlineData("storage.connection.unavailable")]
    public void A_known_agent_failure_code_is_explained_rather_than_shown_as_a_category(string code)
    {
        var described = SyncFailureMessages.Describe(new StorageIpcFailure(
            code,
            StorageIpcFailureCategory.Unavailable,
            "The sync service or an endpoint is temporarily unavailable.",
            IsTransient: true));

        Assert.DoesNotContain("temporarily unavailable", described, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(described);
    }

    [Fact]
    public void A_disabled_profile_is_explained_as_something_the_operator_can_fix()
    {
        var described = SyncFailureMessages.Describe(new StorageIpcFailure(
            "sync.profile.disabled",
            StorageIpcFailureCategory.Validation,
            "The sync profile is disabled, so it cannot run on a schedule.",
            IsTransient: false));

        Assert.Contains("disabled", described, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An unrecognised code still has to be traceable, so it is kept alongside the agent's text
    /// rather than discarded the way the original code path discarded every code.
    /// </summary>
    [Fact]
    public void An_unknown_failure_code_is_preserved_for_reporting()
    {
        var described = SyncFailureMessages.Describe(new StorageIpcFailure(
            "sync.preview.faulted",
            StorageIpcFailureCategory.Unexpected,
            "The sync preview could not be generated.",
            IsTransient: false));

        Assert.Contains("sync.preview.faulted", described, StringComparison.Ordinal);
    }

    [Fact]
    public void Editor_is_inert_until_loaded_then_really_saves_previews_and_dispatches()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            var sync = new FakeSyncManagementClient();
            var storage = new FakeRemoteStorageClient(
            [
                FakeRemoteStorageClient.CreateConnection("Source"),
                FakeRemoteStorageClient.CreateConnection("Remote")
            ]);
            using var form = new SyncProfileEditorForm(sync, storage);
            _ = form.Handle;

            var browseA = Assert.IsAssignableFrom<Button>(form.Controls.Find("BrowseLocationA", true).Single());
            var browseB = Assert.IsAssignableFrom<Button>(form.Controls.Find("BrowseLocationB", true).Single());
            var folderA = Assert.IsType<StorageHubTextField>(form.Controls.Find("LocationAFolder", true).Single());
            var folderB = Assert.IsType<StorageHubTextField>(form.Controls.Find("LocationBFolder", true).Single());

            Assert.Equal(0, sync.ListProfilesCount);
            Assert.Equal(0, storage.ListCount);
            Assert.False(browseA.Enabled);
            Assert.False(browseB.Enabled);
            Assert.False(folderA.Enabled);
            Assert.False(folderB.Enabled);

            form.LoadProfilesAsync().GetAwaiter().GetResult();
            Assert.True(browseA.Enabled);
            Assert.True(browseB.Enabled);
            Assert.True(folderA.Enabled);
            Assert.True(folderB.Enabled);
            Assert.Contains("Connection root", folderA.PlaceholderText, StringComparison.Ordinal);
            Assert.Contains("Connection root", folderB.PlaceholderText, StringComparison.Ordinal);
            var compatibility = Assert.IsType<StorageHubCheckBox>(
                form.Controls.Find("AllowNonAtomicDestinationWrites", true).Single());
            Assert.False(compatibility.Checked);
            compatibility.Checked = true;
            var saved = form.SaveCurrentProfileAsync().GetAwaiter().GetResult();
            var run = form.GeneratePreviewAsync().GetAwaiter().GetResult();

            Assert.Equal(1, sync.ListProfilesCount);
            Assert.Equal(1, storage.ListCount);
            Assert.Equal(1, sync.CreateProfileCount);
            Assert.Equal(1, sync.UpdateProfileCount);
            Assert.True(saved.Draft.AllowNonAtomicDestinationWrites);
            Assert.Equal(saved.ProfileId, run.ProfileId);
            Assert.Equal(1, sync.PreviewCount);
            Assert.Equal(1, form.Review.LoadedOperationCount);
            Assert.True(form.Review.ApproveAndDispatchAsync().GetAwaiter().GetResult());
            Assert.Contains("completed", form.Review.StatusText, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Connections_remain_selectable_when_sync_profile_loading_fails()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            var sync = new FakeSyncManagementClient
            {
                ListProfilesError = new InvalidDataException("The local agent rejected the sync request.")
            };
            var storage = new FakeRemoteStorageClient(
            [
                FakeRemoteStorageClient.CreateConnection("Local files"),
                FakeRemoteStorageClient.CreateConnection("S3 archive")
            ]);
            using var form = new SyncProfileEditorForm(sync, storage);
            _ = form.Handle;

            form.LoadProfilesAsync().GetAwaiter().GetResult();

            var browseA = Assert.IsAssignableFrom<Button>(form.Controls.Find("BrowseLocationA", true).Single());
            var browseB = Assert.IsAssignableFrom<Button>(form.Controls.Find("BrowseLocationB", true).Single());
            Assert.True(browseA.Enabled);
            Assert.True(browseB.Enabled);
            Assert.Contains("Connections loaded", form.StatusText, StringComparison.Ordinal);
            Assert.Contains("sync profiles are unavailable", form.StatusText, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Descriptive_behavior_menu_exposes_and_persists_every_preset()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            var sync = new FakeSyncManagementClient();
            var storage = new FakeRemoteStorageClient(
            [
                FakeRemoteStorageClient.CreateConnection("Location A"),
                FakeRemoteStorageClient.CreateConnection("Location B")
            ]);
            using var form = new SyncProfileEditorForm(sync, storage);
            _ = form.Handle;
            form.LoadProfilesAsync().GetAwaiter().GetResult();

            var picker = Assert.IsType<SyncBehaviorPickerControl>(
                form.Controls.Find("SyncBehaviorPicker", true).Single());
            var choices = Enum.GetValues<SyncIpcBehavior>();
            Assert.Equal(SyncIpcBehavior.UpdateAToB, picker.SelectedBehavior);
            foreach (var behavior in choices)
            {
                var button = Assert.IsAssignableFrom<Button>(
                    picker.Controls.Find($"Behavior{behavior}", true).Single());
                Assert.False(string.IsNullOrWhiteSpace(button.AccessibleDescription));
            }

            picker.SelectedBehavior = SyncIpcBehavior.MirrorBToA;
            var saved = form.SaveCurrentProfileAsync().GetAwaiter().GetResult();

            Assert.Equal(SyncIpcBehavior.MirrorBToA, saved.Draft.Behavior);
        });
    }
}
