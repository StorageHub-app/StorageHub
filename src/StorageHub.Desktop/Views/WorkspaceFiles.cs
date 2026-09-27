using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;

namespace StorageHub.Desktop.Views;

/// <summary>
/// The Workspace menu's file commands: Open, Save, Save As and Rename, and the question a changed
/// workspace asks before it closes.
/// </summary>
/// <remarks>
/// <para>
/// What 1.x's MainForm did around <c>WorkspaceFileStore</c>, which came across to Desktop.Core
/// unchanged, so the <c>.shw</c> files are the same files: a workspace saved by either version
/// opens in the other.
/// </para>
/// <para>
/// Every route that opens a workspace comes through <see cref="OpenPathAsync"/> -- the Open
/// dialog, Welcome's list and the Workspace menu's pinned and recent entries -- so they agree about
/// a file that is already open, one that has gone, and what is recorded as recent.
/// </para>
/// </remarks>
internal sealed class WorkspaceFiles
{
    private readonly ShellPreviewModel _shell;
    private readonly IDialogService _dialogs;
    private readonly IFilePickerService _picker;
    private readonly Func<bool> _reconnectRemote;

    /// <param name="reconnectRemote">
    /// The "reconnect remote panes" setting, read when a file is opened rather than once, since
    /// Settings can change it in between.
    /// </param>
    internal WorkspaceFiles(
        ShellPreviewModel shell,
        IDialogService dialogs,
        IFilePickerService picker,
        WorkspaceBookmarks bookmarks,
        Func<bool>? reconnectRemote = null)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        Bookmarks = bookmarks ?? throw new ArgumentNullException(nameof(bookmarks));
        _reconnectRemote = reconnectRemote ?? (static () => true);
    }

    /// <summary>The pinned and recent lists that saving and opening record into.</summary>
    internal WorkspaceBookmarks Bookmarks { get; }

    /// <summary>What the pickers offer: workspaces, and everything for a file named otherwise.</summary>
    private static IReadOnlyList<FilePickerFilter> Filters =>
    [
        new(Ui.Shell.WorkspaceFileType, [WorkspaceShortcutSettings.Extension.TrimStart('.')]),
        new(Ui.KeyStore.AllFiles, ["*"])
    ];

    /// <summary>
    /// Saves a workspace to its file, or asks where when it has none or Save As was chosen.
    /// </summary>
    /// <param name="tab">The workspace to save; the one showing when null.</param>
    /// <returns>Whether it was saved, which a close waiting on it needs to know.</returns>
    internal async Task<bool> SaveAsync(bool saveAs, WorkspaceTab? tab = null)
    {
        var showing = _shell.Workspaces.ElementAtOrDefault(_shell.SelectedWorkspace);
        tab ??= showing;
        if (tab?.Workspace is not { } workspace) return false;

        // A workspace still being opened is saved once it is all back. Saved halfway, a pane still
        // connecting would go into the file with no folder, and the tab would then say saved.
        try
        {
            await workspace.Opening.ConfigureAwait(true);
        }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException)
        {
            // Closed while it opened: there is nothing left to save.
            return false;
        }

        var path = saveAs ? null : workspace.FilePath;
        if (path is null)
        {
            // Shown while it is asked about, as 1.x did, so the dialog is over the workspace it is
            // saving rather than over whichever tab happened to be open; and the tab that was
            // showing is shown again afterwards, as 1.x's SaveWorkspace switched back.
            _shell.SelectedWorkspace = _shell.Workspaces.IndexOf(tab);
            try
            {
                path = await _picker.SaveFileAsync(new FilePickerRequest
                {
                    Title = Ui.Dialogs.SaveWorkspaceCaption,
                    Filters = Filters,
                    SuggestedFileName = FileNameFor(workspace.Name) + WorkspaceShortcutSettings.Extension,
                    SuggestedDirectory = System.IO.Path.GetDirectoryName(workspace.FilePath)
                }).ConfigureAwait(true);
            }
            finally
            {
                if (showing is not null && _shell.Workspaces.IndexOf(showing) is >= 0 and var back)
                {
                    _shell.SelectedWorkspace = back;
                }
            }

            if (path is null) return false;
        }

        try
        {
            workspace.Save(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidDataException or ArgumentException or NotSupportedException)
        {
            await _dialogs.ShowAsync(new DialogRequest
            {
                Title = Ui.Dialogs.SaveWorkspaceCaption,
                Message = Ui.Format(Ui.Dialogs.SaveWorkspaceFailedFormat, error.Message),
                Severity = DialogSeverity.Error
            }).ConfigureAwait(true);
            return false;
        }

        // The workspace's own path, not the picker's: saving forces the .shw extension.
        if (workspace.FilePath is { } saved) Bookmarks.RecordOpened(saved, workspace.Name);
        return true;
    }

    /// <summary>Asks for a workspace file and opens it.</summary>
    internal async Task OpenAsync()
    {
        var path = await _picker.PickFileAsync(new FilePickerRequest
        {
            Title = Ui.Dialogs.OpenWorkspaceCaption,
            Filters = Filters
        }).ConfigureAwait(true);
        if (path is not null) await OpenPathAsync(path).ConfigureAwait(true);
    }

    /// <summary>
    /// Opens a workspace file, whichever route asked for it.
    /// </summary>
    /// <remarks>
    /// A file that is already open is shown rather than opened twice. A file that has gone offers
    /// to drop it from the lists; one that is there but will not load -- corrupt, too large, from a
    /// newer version -- says why and leaves the lists alone, since that is a problem that can be
    /// fixed.
    /// </remarks>
    /// <returns>Whether a tab for the file is now open and showing.</returns>
    internal async Task<bool> OpenPathAsync(string path)
    {
        string full;
        try
        {
            full = System.IO.Path.GetFullPath(path);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            await OfferToForgetAsync(path).ConfigureAwait(true);
            return false;
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (_shell.Workspaces.FirstOrDefault(tab =>
                string.Equals(tab.Workspace?.FilePath, full, comparison)) is { Workspace: { } open } existing)
        {
            // Showing it again is an open as far as the recent list is concerned.
            _shell.SelectedWorkspace = _shell.Workspaces.IndexOf(existing);
            Bookmarks.RecordOpened(full, open.Name);
            return true;
        }

        // Checked here rather than trusted from wherever the path came from: a recent entry can
        // be for a file deleted since the list was drawn.
        if (!File.Exists(full))
        {
            await OfferToForgetAsync(full).ConfigureAwait(true);
            return false;
        }

        WorkspaceFileDocument document;
        try
        {
            // Validated whole on the way in: the layout, every pane and the name. A file that
            // gets past this is one every pane can be put back from.
            document = WorkspaceFileStore.Load(full);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidDataException or ArgumentException)
        {
            await _dialogs.ShowAsync(new DialogRequest
            {
                Title = Ui.Dialogs.OpenWorkspaceCaption,
                Message = Ui.Format(Ui.Dialogs.OpenWorkspaceFailedFormat, error.Message),
                Severity = DialogSeverity.Error
            }).ConfigureAwait(true);
            return false;
        }

        // As many panes as the file has, so each is made once; the file's arrangement replaces
        // the preset's as it is opened.
        if (_shell.AddWorkspace(WorkspacePreset.Find(document.Panes.Count, WorkspaceLayout.SideBySide)!, document.Name)
            is not { Workspace: { } workspace })
        {
            return false;
        }

        try
        {
            await workspace.OpenAsync(document, full, _reconnectRemote()).ConfigureAwait(true);
        }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException)
        {
            // The tab was closed before every pane was back, which stops the restore with it.
            return false;
        }

        Bookmarks.RecordOpened(full, workspace.Name);
        return true;
    }

    /// <summary>Asks for a new name for the workspace showing, as Workspace > Rename does.</summary>
    internal async Task RenameAsync()
    {
        if (_shell.Workspaces.ElementAtOrDefault(_shell.SelectedWorkspace)?.Workspace is not { } workspace) return;

        var name = await _dialogs.PromptAsync(new DialogPromptRequest
        {
            Title = Ui.Shell.RenameWorkspaceTitle,
            Label = Ui.Shell.NewNameLabel,
            Value = workspace.Name,
            Accept = Ui.Shell.RenameWorkspaceAccept,

            // What the file can hold: 1.x's box stopped at the same length.
            Validate = static value => value.Trim().Length > WorkspaceModel.MaximumNameLength || value.Any(char.IsControl)
                ? Ui.KeyStore.NameTooLong
                : null
        }).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(name)) workspace.Rename(name);
    }

    /// <summary>
    /// Pins the workspace showing, or unpins it, as Workspace > Pin Workspace does.
    /// </summary>
    /// <remarks>
    /// One that has never been saved is saved first, through Save As, so that a pin always names
    /// a real file, as 1.x's did. Dismissing that picker pins nothing.
    /// </remarks>
    internal async Task TogglePinAsync()
    {
        if (_shell.Workspaces.ElementAtOrDefault(_shell.SelectedWorkspace)?.Workspace is not { } workspace) return;
        if (workspace.FilePath is null && !await SaveAsync(saveAs: true).ConfigureAwait(true)) return;
        if (workspace.FilePath is { } path) Bookmarks.TogglePin(path, workspace.Name);
    }

    /// <summary>
    /// Asks whether to save a changed workspace before it goes, and saves it when told to.
    /// </summary>
    /// <param name="caption">"Close Workspace" or "Exit StorageHub": what is about to happen.</param>
    /// <returns>Whether to go ahead: saved, or not wanted; false for Cancel or a save that failed.</returns>
    internal async Task<bool> ConfirmDiscardAsync(WorkspaceTab tab, string caption)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (tab.Workspace is not { IsDirty: true } workspace) return true;

        var choice = await _dialogs.ConfirmAsync(new DialogRequest
        {
            Title = caption,
            Message = Ui.Format(Ui.Dialogs.SaveWorkspaceChangesPromptFormat, workspace.Name),
            Severity = DialogSeverity.Question,
            Buttons = DialogButtons.YesNoCancel
        }).ConfigureAwait(true);
        return choice switch
        {
            DialogChoice.Yes => await SaveAsync(saveAs: false, tab).ConfigureAwait(true),
            DialogChoice.No => true,
            _ => false
        };
    }

    /// <summary>Asks about every changed workspace before the shell exits; false when one said Cancel.</summary>
    internal async Task<bool> ConfirmExitAsync()
    {
        foreach (var tab in _shell.Workspaces.ToArray())
        {
            if (!await ConfirmDiscardAsync(tab, Ui.Dialogs.ExitCaption).ConfigureAwait(true)) return false;
        }

        return true;
    }

    /// <summary>
    /// Offers to drop a remembered workspace whose file is gone. Only an explicit yes removes it:
    /// a disconnected drive, or a file about to come back, should not silently lose a pin.
    /// </summary>
    private async Task OfferToForgetAsync(string path)
    {
        var choice = await _dialogs.ConfirmAsync(new DialogRequest
        {
            Title = Ui.Dialogs.OpenWorkspaceCaption,
            Message = Ui.Format(Ui.Dialogs.WorkspaceMissingPromptFormat, path),
            Severity = DialogSeverity.Warning,
            Buttons = DialogButtons.YesNo,
            Default = DialogChoice.No
        }).ConfigureAwait(true);
        if (choice == DialogChoice.Yes) Bookmarks.Forget(path);
    }

    /// <summary>The name as a file name: anything a file system refuses becomes "_".</summary>
    private static string FileNameFor(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var value = new string([.. name.Select(character => invalid.Contains(character) ? '_' : character)]).Trim();
        return value.Length == 0 ? Ui.Shell.MenuWorkspace : value;
    }
}
