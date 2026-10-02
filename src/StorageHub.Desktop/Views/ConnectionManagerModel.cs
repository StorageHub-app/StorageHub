using System.Windows.Input;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;

namespace StorageHub.Desktop.Views;

/// <summary>
/// The Edit Connection dialog: the connection editor on its own, for one connection.
/// </summary>
/// <remarks>
/// <para>
/// 1.x's ConnectionManagerForm began as a list beside the editor, and so did this. The list went
/// once the connections panel could create, open, test, edit and delete a connection itself: it was
/// a second route to each of those, and what was left was a plain dialog opened on a new
/// connection or on the one Edit was pressed on. The name stayed, as it did in 1.x.
/// </para>
/// <para>
/// Saving is the end of the task, so a save that is accepted closes the dialog, as 1.x's did.
/// One that is refused leaves it open with the edit still in it and the reason in the footer.
/// </para>
/// </remarks>
internal sealed class ConnectionManagerModel
{
    /// <param name="files">Picks a certificate or a key file to enrol. Null leaves that unavailable.</param>
    /// <param name="keyStore">Lists what the key store holds, for the editor's material fields.</param>
    /// <param name="pickKey">
    /// Chooses one of those entries, or imports one; the window supplies a dialog.
    /// </param>
    /// <param name="connectionDefaults">Each provider's new-connection defaults from Settings.</param>
    /// <param name="hostKeyDiscovery">Settings' SSH host-key discovery, for the Trust tab.</param>
    internal ConnectionManagerModel(
        Func<ConnectionManagerController> controller,
        Func<IRemoteStorageAgentClient>? storage = null,
        IDialogService? dialogs = null,
        IFilePickerService? files = null,
        Func<IKeyStoreAgentClient>? keyStore = null,
        Func<KeyStoreMaterialKind, IReadOnlyList<KeyStoreEntryDocument>, Task<KeyStoreEntryDocument?>>? pickKey = null,
        Func<string?, string, Task<IconChoice>>? pickIcon = null,
        IReadOnlyDictionary<string, string>? connectionDefaults = null,
        SshHostKeyDiscoveryMode hostKeyDiscovery = SshHostKeyDiscoveryMode.Manual)
    {
        ArgumentNullException.ThrowIfNull(controller);
        Editor = new ConnectionEditorModel(
            controller, storage, dialogs, files, keyStore, pickKey, pickIcon, connectionDefaults, hostKeyDiscovery);
        Editor.Written += (_, _) => ProfilesChanged?.Invoke(this, EventArgs.Empty);
        Editor.Saved += (_, _) => Close();
    }

    public ConnectionEditorModel Editor { get; }

    /// <summary>
    /// The tab the dialog opens on: General, or the one a details panel's attention button names,
    /// as 1.x's "Fix credentials…" opened Authentication and "Review trust…" TLS / SSH Trust.
    /// </summary>
    internal ConnectionEditorTab Tab { get; init; }

    /// <summary>
    /// Raised whenever the agent has accepted a write, for what lists connections to list again,
    /// as 1.x's ProfilesChanged was. Raised even when the pin that followed was refused, because
    /// the profile is written by then.
    /// </summary>
    internal event EventHandler? ProfilesChanged;

    /// <summary>Cancel, in the footer: closes the dialog, leaving anything unsaved unsaved.</summary>
    public ICommand CloseCommand => _close ??= new RelayCommand(_ => Close());

    private RelayCommand? _close;

    public static string CancelLabel => Ui.ConnectionEditor.Cancel;

    /// <summary>"Edit Connection — StorageHub", for a new connection as well, as 1.x titled it.</summary>
    public static string Title => Ui.ConnectionEditor.EditTitle;

    public static string AccessibleName => Ui.ConnectionEditor.EditAccessibleName;

    public static string AccessibleDescription => Ui.ConnectionEditor.WindowAccessibleDescription;

    /// <summary>Raised when the window should close.</summary>
    internal event EventHandler? Closed;

    internal void Close() => Closed?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Loads the connection to edit, or starts a new one when there is none, on S3 unless a
    /// provider is named, as 1.x's dialog did.
    /// </summary>
    /// <param name="initialGroup">The group a new connection starts in; null is Ungrouped.</param>
    internal async Task OpenAsync(
        Guid? connectionId,
        StorageProviderKind initialProvider = StorageProviderKind.S3,
        Guid? initialGroup = null,
        CancellationToken cancellationToken = default)
    {
        if (connectionId is { } id)
        {
            await Editor.OpenAsync(id, cancellationToken).ConfigureAwait(true);
        }
        else
        {
            Editor.StartNew(initialProvider, initialGroup);
        }

        await Editor.LoadGroupsAsync(cancellationToken).ConfigureAwait(true);
    }
}
