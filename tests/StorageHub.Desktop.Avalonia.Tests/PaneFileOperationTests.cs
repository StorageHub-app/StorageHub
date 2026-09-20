using Avalonia.Headless.XUnit;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;
using StorageHub.Desktop.Views;
using Xunit;
using static StorageHub.Desktop.Avalonia.Tests.WorkspaceFakes;

namespace StorageHub.Desktop.Avalonia.Tests;

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
    /// The confirmation says the deletion cannot be undone, because in 2.0 it cannot.
    /// </summary>
    /// <remarks>
    /// 1.x sent local deletions to the Windows Recycle Bin. There is no cross-platform equivalent,
    /// so the honest thing is to say so rather than offer a recovery that exists on one platform.
    /// </remarks>
    [AvaloniaFact]
    public async Task TheDeleteConfirmationSaysItCannotBeUndone()
    {
        await using var fixture = await OpenedAsync();
        var (pane, _, dialogs) = fixture;
        pane.SelectedRows.Add(pane.Rows[0]);
        dialogs.Choice = DialogChoice.No;

        await pane.DeleteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Ui.Shell.DeleteItemsDetail, dialogs.LastRequest!.Detail);
        Assert.Equal(DialogSeverity.Warning, dialogs.LastRequest.Severity);
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
    private sealed class RecordingDialogs : IDialogService
    {
        internal string? Answer { get; set; }

        internal DialogChoice Choice { get; set; } = DialogChoice.No;

        internal DialogPromptRequest? LastPrompt { get; private set; }

        internal DialogRequest? LastRequest { get; private set; }

        public Task ShowAsync(DialogRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.CompletedTask;
        }

        public Task<DialogChoice> ConfirmAsync(
            DialogRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
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
