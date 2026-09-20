using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;

namespace StorageHub.Desktop.Views;

/// <summary>
/// The Connection Manager: every saved connection, and the editor for whichever is chosen.
/// </summary>
/// <remarks>
/// <para>
/// A list and a form, which is what the 1,784-line WinForms version was underneath. Most of those
/// lines were laying out fields by hand for six providers; here the fields come from
/// <see cref="ConnectionProviderCatalog"/> and the draft from
/// <see cref="ConnectionEditorDraftFactory"/>, both of which that shell already had and neither of
/// which it used for its layout.
/// </para>
/// <para>
/// Deleting asks first and is checked against the version that was listed, so a profile somebody
/// else changed in between fails as a conflict rather than being deleted at a revision nobody
/// reviewed.
/// </para>
/// </remarks>
internal sealed class ConnectionManagerModel : INotifyPropertyChanged
{
    private readonly Func<IRemoteStorageAgentClient> _storage;
    private readonly Func<ConnectionManagerController> _controller;
    private readonly IDialogService? _dialogs;
    private ConnectionListEntry? _selected;
    private string _status = string.Empty;
    private bool _isBusy;

    internal ConnectionManagerModel(
        Func<IRemoteStorageAgentClient> storage,
        Func<ConnectionManagerController> controller,
        IDialogService? dialogs = null)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _dialogs = dialogs;

        Editor = new ConnectionEditorModel(controller, storage);
        Editor.Saved += (_, _) => _ = RefreshAsync();

        NewCommand = new RelayCommand(_ => StartNew());
        DeleteCommand = new RelayCommand(
            _ => _ = DeleteAsync(), _ => !_isBusy && _selected is not null && _dialogs is not null);
    }

    /// <summary>One row in the list: enough to show it and enough to delete it.</summary>
    /// <param name="Version">
    /// The version this row was listed at. Carried so a delete can be checked against it, which is
    /// what turns "somebody else edited this while the dialog was open" into a refusal instead of
    /// a silent loss.
    /// </param>
    internal sealed record ConnectionListEntry(Guid Id, string Name, string Endpoint, string Badge, long Version);

    public ObservableCollection<ConnectionListEntry> Connections { get; } = [];

    public ConnectionEditorModel Editor { get; }

    /// <summary>Which connection the editor is showing.</summary>
    public ConnectionListEntry? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            Raise(nameof(Selected));
            Raise(nameof(HasSelection));
            RaiseCommands();
            if (value is not null) _ = Editor.OpenAsync(value.Id);
        }
    }

    public bool HasSelection => _selected is not null;

    public bool IsEmpty => Connections.Count == 0;

    public string Status
    {
        get => _status;
        private set
        {
            if (string.Equals(_status, value, StringComparison.Ordinal)) return;
            _status = value;
            Raise(nameof(Status));
            Raise(nameof(HasStatus));
        }
    }

    public bool HasStatus => _status.Length > 0;

    public ICommand NewCommand { get; }

    public ICommand DeleteCommand { get; }

    public static string Title => Ui.Connections.ManagerTitle;

    public static string NewLabel => Ui.Connections.NewConnection;

    public static string DeleteLabel => Ui.Commands.EditDelete;

    public static string EmptyLabel => Ui.Connections.EditorEmpty;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when the window should close.</summary>
    internal event EventHandler? Closed;

    internal void Close() => Closed?.Invoke(this, EventArgs.Empty);

    /// <summary>Lists what the agent has, keeping the selection if it is still there.</summary>
    internal async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsBusyInternal(true);
        try
        {
            await using var client = _storage();
            var response = await client
                .ListConnectionsAsync(new ConnectionListRequest(IncludeDisabled: true), cancellationToken)
                .ConfigureAwait(true);

            if (response.Failure is { } failure)
            {
                Status = failure.Message;
                return;
            }

            var chosen = _selected?.Id;
            Connections.Clear();
            foreach (var connection in response.Connections)
            {
                var card = ConnectionCardFactory.Create(connection);
                Connections.Add(new ConnectionListEntry(
                    connection.ConnectionId,
                    connection.DisplayName,
                    card.Endpoint,
                    ConnectionGrouping.BadgeFor(card),
                    connection.Version));
            }

            Raise(nameof(IsEmpty));
            Status = string.Empty;

            // Reattached without reopening the editor: the selection is the same connection and
            // the editor already has it, so reloading would throw away anything half-typed.
            _selected = Connections.FirstOrDefault(entry => entry.Id == chosen);
            Raise(nameof(Selected));
            Raise(nameof(HasSelection));
            RaiseCommands();
        }
        catch (Exception error) when (IsAgentFailure(error))
        {
            Status = Ui.Shell.AgentNotConnected;
        }
        finally
        {
            IsBusyInternal(false);
        }
    }

    /// <summary>Clears the selection and offers an empty editor.</summary>
    internal void StartNew()
    {
        _selected = null;
        Raise(nameof(Selected));
        Raise(nameof(HasSelection));
        RaiseCommands();
        Editor.StartNew();
    }

    /// <summary>Confirms, then deletes the selected connection at the version it was listed at.</summary>
    internal async Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        if (_selected is not { } entry || _dialogs is null) return;

        var choice = await _dialogs.ConfirmAsync(
            new DialogRequest
            {
                Title = Ui.Dialogs.DeleteConnectionCaption,
                Message = Ui.Format(Ui.Dialogs.DeleteConnectionPromptFormat, entry.Name),
                Severity = DialogSeverity.Warning,
                Buttons = DialogButtons.YesNo
            },
            cancellationToken).ConfigureAwait(true);
        if (choice != DialogChoice.Yes) return;

        IsBusyInternal(true);
        try
        {
            var response = await _controller()
                .DeleteAsync(entry.Id, entry.Version, cancellationToken)
                .ConfigureAwait(true);

            if (response.Failure is { } failure)
            {
                Status = failure.Message;
                return;
            }

            StartNew();
            await RefreshAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception error) when (IsAgentFailure(error))
        {
            Status = Ui.Shell.AgentNotConnected;
        }
        finally
        {
            IsBusyInternal(false);
        }
    }

    private static bool IsAgentFailure(Exception error) => error is
        IOException or TimeoutException or InvalidOperationException or ObjectDisposedException;

    private void IsBusyInternal(bool busy)
    {
        _isBusy = busy;
        RaiseCommands();
    }

    private void RaiseCommands() => (DeleteCommand as RelayCommand)?.RaiseCanExecuteChanged();

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
