using Avalonia.Headless.XUnit;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;
using StorageHub.Desktop.Views;
using Xunit;
using static StorageHub.Desktop.Tests.WorkspaceFakes;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Making, renaming and removing things from a pane.
/// </summary>
/// <remarks>
/// <see cref="PaneMutationController"/> is tested in Desktop.Core against both a fake agent and a
/// recorder, so what matters here is the half above it: that the pane asks before it acts, that
/// dismissing the question does nothing, that a refusal is shown rather than thrown, and that the
/// listing is reloaded afterwards rather than being edited in place.
/// </remarks>
public class PaneFileOperationTests
{
    [AvaloniaFact]
    public async Task MakingAFolderAsksForANameAndThenReloads()
    {
        await using var fixture = await OpenedAsync();
        var (pane, agent, dialogs) = fixture;
        dialogs.Answer = "drafts";

        await pane.CreateAsync(container: true, TestContext.Current.CancellationToken);

        Assert.Equal("drafts", Assert.Single(agent.CreatedDirectories));
        Assert.Equal(Ui.Shell.FolderName, dialogs.LastPrompt!.Label);

        // Reloaded rather than added to: what the provider actually stored is its answer, and a
        // name can come back normalised.
        Assert.Equal(2, agent.Listings);
    }

    /// <summary>A dismissed prompt does nothing at all.</summary>
    [AvaloniaFact]
    public async Task DismissingTheNamePromptCreatesNothing()
    {
        await using var fixture = await OpenedAsync();
        var (pane, agent, dialogs) = fixture;
        dialogs.Answer = null;

        await pane.CreateAsync(container: true, TestContext.Current.CancellationToken);

        Assert.Empty(agent.CreatedDirectories);
        Assert.Equal(1, agent.Listings);
    }

    /// <summary>A new file starts with a suggested name, because most of them are the same shape.</summary>
    [AvaloniaFact]
    public async Task ANewFileIsOfferedADefaultName()
    {
        await using var fixture = await OpenedAsync();
        var (pane, _, dialogs) = fixture;
        dialogs.Answer = "notes.txt";

        await pane.CreateAsync(container: false, TestContext.Current.CancellationToken);

        Assert.Equal(Ui.Shell.DefaultFileName, dialogs.LastPrompt!.Value);
    }

    /// <summary>
    /// The prompt refuses a name the storage would refuse, while it is being typed.
    /// </summary>
    /// <remarks>
    /// The same rule the controller applies, handed to the dialog rather than restated, so a name
    /// the box accepts is one the agent will take.
    /// </remarks>
    [AvaloniaFact]
    public async Task TheNamePromptCarriesTheStorageRules()
    {
        await using var fixture = await OpenedAsync();
        var (pane, _, dialogs) = fixture;
        dialogs.Answer = "ok";

        await pane.CreateAsync(container: true, TestContext.Current.CancellationToken);

        var validate = dialogs.LastPrompt!.Validate;
        Assert.NotNull(validate);
        Assert.Null(validate!("drafts"));
        Assert.NotNull(validate("with/separator"));
        Assert.NotNull(validate(string.Empty));
    }

    [AvaloniaFact]
    public async Task RenamingOneItemSendsItsOldAndNewNames()
    {
        await using var fixture = await OpenedAsync();
        var (pane, agent, dialogs) = fixture;
        pane.SelectedRows.Add(pane.Rows.Single(static row => row.Name == "render.exr"));
        dialogs.Answer = "final.exr";

        await pane.RenameAsync(TestContext.Current.CancellationToken);

        var (from, to) = Assert.Single(agent.Renames);
        Assert.Equal("render.exr", from);
        Assert.Equal("final.exr", to);
        Assert.Equal("render.exr", dialogs.LastPrompt!.Value);
    }

    /// <summary>Renaming is a one-item operation, and says so rather than picking one.</summary>
    [AvaloniaFact]
    public async Task RenamingIsUnavailableWithoutExactlyOneSelection()
    {
        await using var fixture = await OpenedAsync();
        var pane = fixture.Pane;

        Assert.False(pane.RenameCommand.CanExecute(null));

        pane.SelectedRows.Add(pane.Rows[0]);
        Assert.True(pane.RenameCommand.CanExecute(null));

        pane.SelectedRows.Add(pane.Rows[1]);
        Assert.False(pane.RenameCommand.CanExecute(null));
    }

    /// <summary>Deleting asks first, and a "no" leaves everything where it was.</summary>
    [AvaloniaFact]
    public async Task DecliningTheDeleteConfirmationDeletesNothing()
    {
        await using var fixture = await OpenedAsync();
        var (pane, agent, dialogs) = fixture;
        pane.SelectedRows.Add(pane.Rows.Single(static row => row.Name == "render.exr"));
        dialogs.Choice = DialogChoice.No;

        await pane.DeleteAsync(TestContext.Current.CancellationToken);

        Assert.Empty(agent.Deletes);
    }

    /// <summary>
    /// The review names what is about to go and says a connection's delete is for good, as 1.x's did.
    /// </summary>
    [AvaloniaFact]
    public async Task TheDeleteReviewNamesTheItemsAndSaysARemoteDeleteIsPermanent()
    {
        await using var fixture = await OpenedAsync();
        var (pane, _, dialogs) = fixture;
        pane.SelectedRows.Add(pane.Rows[0]);
        dialogs.Choice = DialogChoice.No;

        await pane.DeleteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Ui.Dialogs.ReviewDeleteCaption, dialogs.LastRequest!.Title);
        Assert.Contains(Ui.Format(Ui.Dialogs.DeletePreviewItemFormat, pane.Rows[0].Name), dialogs.LastRequest.Detail, StringComparison.Ordinal);
        Assert.EndsWith(Ui.Dialogs.DeleteRemotePermanent, dialogs.LastRequest.Detail, StringComparison.Ordinal);
        Assert.Equal(DialogSeverity.Warning, dialogs.LastRequest.Severity);
        Assert.Equal(DialogChoice.No, dialogs.LastRequest.Default);
    }

    /// <summary>On this computer the review says Recycle Bin, and the delete goes there.</summary>
    [AvaloniaFact]
    public async Task ALocalDeleteGoesToTheRecycleBin()
    {
        var folder = Directory.CreateTempSubdirectory("storagehub-recycle-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder.FullName, "old.txt"), "x", TestContext.Current.CancellationToken);
            var local = new RecordingLocal();
            var dialogs = new RecordingDialogs { Choice = DialogChoice.Yes };
            await using var pane = new BrowserPaneModel(
                new RecordingAgent([]),
                mutations: () => new PaneMutationController(static () => throw new InvalidOperationException(), local),
                dialogs: dialogs);
            await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
            await pane.OpenAsync(pane.Connections.Single(static c => c.Kind == PaneContentKind.ThisPc), TestContext.Current.CancellationToken);
            await pane.NavigateAsync(folder.FullName, TestContext.Current.CancellationToken);
            pane.SelectedRows.Add(pane.Rows.Single(static row => row.Name == "old.txt"));

            await pane.DeleteAsync(TestContext.Current.CancellationToken);

            Assert.EndsWith(Ui.Dialogs.DeleteLocalToRecycleBin, dialogs.LastRequest!.Detail, StringComparison.Ordinal);
            Assert.Equal("old.txt", Path.GetFileName(Assert.Single(local.Recycled)));
            Assert.Empty(local.Deleted);
            Assert.Equal(Ui.Format(Ui.Shell.SentToRecycleBinFormat, 1), pane.Status);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    /// <summary>"Don't show this warning again" turns the setting off; with it off, nothing asks.</summary>
    [AvaloniaFact]
    public async Task TheWarningCanBeTurnedOffAndThenIsNotShown()
    {
        await using var fixture = await OpenedAsync();
        var (pane, agent, dialogs) = fixture;
        var confirm = true;
        pane.DeleteConfirmation = () => confirm;
        pane.StopDeleteConfirmation = () => confirm = false;
        pane.SelectedRows.Add(pane.Rows.Single(static row => row.Name == "render.exr"));
        dialogs.Choice = DialogChoice.Yes;
        dialogs.Tick = true;

        await pane.DeleteAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Ui.Dialogs.DontShowWarningAgain, dialogs.LastRequest!.CheckBoxLabel);
        Assert.False(confirm);

        dialogs.LastRequest = null;
        pane.SelectedRows.Clear();
        pane.SelectedRows.Add(pane.Rows.Single(static row => row.Name == "reports"));
        await pane.DeleteAsync(TestContext.Current.CancellationToken);

        Assert.Null(dialogs.LastRequest);
        Assert.Equal(2, agent.Deletes.Count);
    }

    [AvaloniaFact]
    public async Task ConfirmingTheDeleteRemovesTheSelectionAndReloads()
    {
        await using var fixture = await OpenedAsync();
        var (pane, agent, dialogs) = fixture;
        pane.SelectedRows.Add(pane.Rows.Single(static row => row.Name == "render.exr"));
        dialogs.Choice = DialogChoice.Yes;

        await pane.DeleteAsync(TestContext.Current.CancellationToken);

        Assert.Equal("render.exr", Assert.Single(agent.Deletes));
        Assert.Equal(2, agent.Listings);
        Assert.Equal(Ui.Format(Ui.Shell.DeletedItemsFormat, 1), pane.Status);
    }

    /// <summary>The way back out of a folder is not something that can be deleted.</summary>
    [AvaloniaFact]
    public async Task TheParentRowIsNeverDeleted()
    {
        await using var fixture = await OpenedAsync();
        var (pane, agent, dialogs) = fixture;
        await pane.NavigateAsync("reports", TestContext.Current.CancellationToken);
        foreach (var row in pane.Rows) pane.SelectedRows.Add(row);
        dialogs.Choice = DialogChoice.Yes;

        await pane.DeleteAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("..", agent.Deletes);
        Assert.Equal(["reports/q1.pdf"], agent.Deletes);
    }

    /// <summary>A terminal has no folder, so none of these are offered in one.</summary>
    [AvaloniaFact]
    public async Task ATerminalPaneOffersNoFileOperations()
    {
        var shell = Summary("build-box", Contracts.Ipc.StorageConnectionProvider.Ssh,
            Contracts.Ipc.ConnectionProfileType.Client);
        var agent = new RecordingAgent([shell]);
        await using var pane = new BrowserPaneModel(
            agent,
            mutations: () => new PaneMutationController(() => agent),
            dialogs: new RecordingDialogs());
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
        await pane.OpenConnectionAsync(shell.ConnectionId, TestContext.Current.CancellationToken);

        Assert.False(pane.NewFolderCommand.CanExecute(null));
        Assert.False(pane.NewFileCommand.CanExecute(null));
        Assert.False(pane.DeleteCommand.CanExecute(null));
    }

    /// <summary>A pane with nothing to ask with offers nothing, rather than failing when pressed.</summary>
    [AvaloniaFact]
    public async Task APaneWithoutMutationsOffersNoFileOperations()
    {
        var connection = Summary("Studio Assets");
        var agent = new RecordingAgent([connection]);
        await using var pane = new BrowserPaneModel(agent);
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
        await pane.OpenConnectionAsync(connection.ConnectionId, TestContext.Current.CancellationToken);

        Assert.False(pane.NewFolderCommand.CanExecute(null));
    }

    /// <summary>A pane on a connection, with the agent and the dialogs it is talking to.</summary>
    private sealed record Fixture(
        BrowserPaneModel Pane,
        RecordingAgent Agent,
        RecordingDialogs Dialogs) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Pane.DisposeAsync();
    }

    private static async Task<Fixture> OpenedAsync()
    {
        var connection = Summary("Studio Assets");
        var agent = new RecordingAgent([connection]);
        var dialogs = new RecordingDialogs();

        var pane = new BrowserPaneModel(
            agent,
            mutations: () => new PaneMutationController(() => agent),
            dialogs: dialogs);
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
        await pane.OpenConnectionAsync(connection.ConnectionId, TestContext.Current.CancellationToken);
        return new Fixture(pane, agent, dialogs);
    }

    /// <summary>The dialogs, answered by the test instead of by a person.</summary>
    /// <summary>This computer, recording what it was asked to recycle or delete, and doing neither.</summary>
    private sealed class RecordingLocal : ILocalMutations
    {
        internal List<string> Recycled { get; } = [];

        internal List<string> Deleted { get; } = [];

        public bool CanRecycle => true;

        public void Create(string parent, string name, bool container) => throw new NotSupportedException();

        public void Rename(string path, string newName, bool container) => throw new NotSupportedException();

        public void Delete(string path, bool container) => Deleted.Add(path);

        public void Recycle(string path, bool container) => Recycled.Add(path);
    }

    private sealed class RecordingDialogs : IDialogService
    {
        internal string? Answer { get; set; }

        internal DialogChoice Choice { get; set; } = DialogChoice.No;

        internal DialogPromptRequest? LastPrompt { get; private set; }

        internal DialogRequest? LastRequest { get; set; }

        public Task ShowAsync(DialogRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.CompletedTask;
        }

        /// <summary>Whether the dialog's checkbox is ticked when it is answered.</summary>
        internal bool Tick { get; set; }

        public Task<DialogChoice> ConfirmAsync(
            DialogRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            if (Choice is not (DialogChoice.Cancel or DialogChoice.No)) request.CheckBoxAnswered?.Invoke(Tick);
            return Task.FromResult(Choice);
        }

        public Task<string?> PromptAsync(
            DialogPromptRequest request,
            CancellationToken cancellationToken = default)
        {
            LastPrompt = request;
            return Task.FromResult(Answer);
        }
    }
}
