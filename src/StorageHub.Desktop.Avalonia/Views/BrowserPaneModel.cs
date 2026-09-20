using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Lucide.Avalonia;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>A connection a pane can be pointed at.</summary>
internal sealed record PaneConnection(Guid Id, string Name, LucideIconKind Icon);

/// <summary>
/// One browser pane: a connection, a path, and what is in it.
/// </summary>
/// <remarks>
/// <para>
/// A view model over <see cref="RemoteBrowserController"/>, which already owns connecting,
/// listing, paging, history and path normalisation, and has its own suite. What is here is the
/// part BrowserPaneControl mixed into 3,621 lines of drawing: which commands are available, what
/// the address bar says, and what the pane shows while it is waiting or after it failed.
/// </para>
/// <para>
/// Nothing here touches a control. That is what lets a headless test drive a pane through an
/// entire navigation - open a connection, enter a folder, go up, fail, recover - against a fake
/// agent, which is the thing the WinForms pane could never be tested for.
/// </para>
/// </remarks>
internal sealed class BrowserPaneModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly RemoteBrowserController _controller;
    private string _status = string.Empty;
    private bool _busy;
    private bool _isActive;
    private PaneConnection? _connection;
    private string _path = "/";
    private BrowserListItem? _selected;

    internal BrowserPaneModel(IRemoteStorageAgentClient? client = null)
    {
        _controller = new RemoteBrowserController(client);

        OpenCommand = new RelayCommand(_ => _ = OpenSelectedAsync(), _ => Selected?.IsContainer == true);
        UpCommand = new RelayCommand(_ => _ = UpAsync(), _ => _controller.CanGoUp);
        BackCommand = new RelayCommand(_ => _ = BackAsync(), _ => _controller.CanGoBack);
        ForwardCommand = new RelayCommand(_ => _ = ForwardAsync(), _ => _controller.CanGoForward);
        RefreshCommand = new RelayCommand(_ => _ = MoveAsync(RemoteBrowserNavigationKind.Refresh));
    }

    public ObservableCollection<PaneConnection> Connections { get; } = [];

    public ObservableCollection<BrowserListItem> Rows { get; } = [];

    /// <summary>The path as the address bar shows it: always rooted, never empty.</summary>
    public string Path
    {
        get => _path;
        private set
        {
            if (string.Equals(_path, value, StringComparison.Ordinal)) return;
            _path = value;
            Raise(nameof(Path));
        }
    }

    /// <summary>What the pane is doing, or why it is not showing anything.</summary>
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

    public bool HasStatus => !string.IsNullOrEmpty(_status);

    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (_busy == value) return;
            _busy = value;
            Raise(nameof(IsBusy));
        }
    }

    /// <summary>Whether this is the pane a pane command would act on.</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            Raise(nameof(IsActive));
        }
    }

    public PaneConnection? Connection
    {
        get => _connection;
        set
        {
            if (_connection == value) return;
            _connection = value;
            Raise(nameof(Connection));
            Raise(nameof(Title));
            if (value is not null) _ = OpenConnectionAsync(value.Id);
        }
    }

    /// <summary>The pane's heading: the connection, or an invitation to choose one.</summary>
    public string Title => _connection?.Name ?? Ui.Pane.SelectProfileToConnect;

    public BrowserListItem? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            Raise(nameof(Selected));
            RaiseCommands();
        }
    }

    public ICommand OpenCommand { get; }

    public ICommand UpCommand { get; }

    public ICommand BackCommand { get; }

    public ICommand ForwardCommand { get; }

    public ICommand RefreshCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Reads the connections this pane can be pointed at.
    /// </summary>
    /// <remarks>
    /// Storage connections and SSH clients only, which is the same filter the sidebar and the
    /// connection picker use: the others cannot be browsed, and listing them produces a pane that
    /// fails the moment somebody chooses one.
    /// </remarks>
    internal async Task LoadConnectionsAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        Status = Ui.Pane.LoadingConnections;
        try
        {
            var result = await _controller.LoadConnectionsAsync(cancellationToken).ConfigureAwait(true);
            Connections.Clear();
            if (result.Status != RemoteBrowserOperationStatus.Succeeded)
            {
                Status = result.ErrorMessage ?? Ui.Pane.Disconnected;
                return;
            }

            foreach (var connection in result.Connections.Where(CanBrowse))
            {
                Connections.Add(new PaneConnection(
                    connection.ConnectionId,
                    connection.DisplayName,
                    Themes.IconCatalog.Resolve(
                        ConnectionIconCatalog.ResolveForConnection(
                            connection.IconKey, MapProvider(connection.Provider), connection.Type))
                        ?? LucideIconKind.Cloud));
            }

            Status = Connections.Count == 0
                ? Ui.Pane.SelectProfileToConnect
                : string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal async Task OpenConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            Report(await _controller
                .SelectConnectionAsync(connectionId, cancellationToken)
                .ConfigureAwait(true));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Goes to a path, or says why it could not.</summary>
    internal async Task NavigateAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        if (_controller.SelectedConnection is null) return;

        IsBusy = true;
        try
        {
            Report(await _controller
                .NavigateAsync(RemoteBrowserNavigationKind.Navigate, relativePath, cancellationToken)
                .ConfigureAwait(true));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Opens the selected row when it is a container.
    /// </summary>
    /// <remarks>
    /// The location rather than the name, because the two differ under a prefix: an S3 entry's name
    /// is its last segment and navigating to that from a nested path would look up the wrong key.
    /// </remarks>
    internal Task OpenSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (Selected is not { IsContainer: true } row) return Task.CompletedTask;

        return row.IsParentNavigation
            ? UpAsync(cancellationToken)
            : NavigateAsync(row.Location ?? row.Name, cancellationToken);
    }

    internal Task UpAsync(CancellationToken cancellationToken = default) =>
        MoveAsync(RemoteBrowserNavigationKind.Up, cancellationToken);

    internal Task BackAsync(CancellationToken cancellationToken = default) =>
        MoveAsync(RemoteBrowserNavigationKind.Back, cancellationToken);

    internal Task ForwardAsync(CancellationToken cancellationToken = default) =>
        MoveAsync(RemoteBrowserNavigationKind.Forward, cancellationToken);

    /// <summary>
    /// Back, forward, up and refresh, which the controller already knows how to do.
    /// </summary>
    /// <remarks>
    /// Asking it for Up rather than computing the parent here is the difference between one
    /// definition of "the folder above" and two. RemoteBrowserPath owns that rule, including what
    /// the parent of a prefix is, and it has its own tests.
    /// </remarks>
    private async Task MoveAsync(
        RemoteBrowserNavigationKind kind,
        CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            Report(await _controller
                .NavigateAsync(kind, cancellationToken: cancellationToken)
                .ConfigureAwait(true));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async ValueTask DisposeAsync() => await _controller.DisposeAsync().ConfigureAwait(false);

    /// <summary>
    /// Shows a navigation result, whatever it turned out to be.
    /// </summary>
    /// <remarks>
    /// A failed navigation leaves the rows alone. The old pane cleared them, which meant a typo in
    /// the address bar lost the listing somebody was looking at and cost another round trip to get
    /// back to it.
    /// </remarks>
    private void Report(RemoteBrowserNavigationResult result)
    {
        if (result.Status != RemoteBrowserOperationStatus.Succeeded || result.Snapshot is null)
        {
            Status = result.ErrorMessage ?? Ui.Pane.Disconnected;
            RaiseCommands();
            return;
        }

        var snapshot = result.Snapshot;
        Path = snapshot.DisplayPath;

        if (!result.AppendedPage)
        {
            Rows.Clear();

            // The ".." row is part of the listing rather than a button, which is how every file
            // manager since Norton Commander has done it and what makes double-click enough.
            if (snapshot.RelativePath.Length > 0)
            {
                Rows.Add(BrowserParentNavigation.Item);
            }
        }

        foreach (var entry in snapshot.Entries)
        {
            Rows.Add(BrowserRowFactory.FromRemote(entry));
        }

        // A navigation can succeed and still have something to say. Asking for a folder that has
        // been deleted lands on the nearest parent that does exist, and the message is the only
        // thing that explains why the listing is not the one that was asked for.
        Status = result.ErrorMessage is { Length: > 0 } note
            ? note
            : Rows.Count == 0 ? Ui.Pane.FolderIsEmpty : string.Empty;
        Raise(nameof(Title));
        RaiseCommands();
    }

    private static bool CanBrowse(ConnectionSummary connection) =>
        connection.IsEnabled &&
        (connection.Type == ConnectionProfileType.Storage ||
            connection is { Type: ConnectionProfileType.Client, Provider: StorageConnectionProvider.Ssh });

    private static StorageProviderKind MapProvider(StorageConnectionProvider provider) => provider switch
    {
        StorageConnectionProvider.Local => StorageProviderKind.Local,
        StorageConnectionProvider.S3 => StorageProviderKind.S3,
        StorageConnectionProvider.Ftp => StorageProviderKind.Ftp,
        StorageConnectionProvider.Ftps => StorageProviderKind.Ftps,
        StorageConnectionProvider.Sftp => StorageProviderKind.Sftp,
        StorageConnectionProvider.Ssh => StorageProviderKind.Ssh,
        _ => StorageProviderKind.Local
    };

    private void RaiseCommands()
    {
        (OpenCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (UpCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (BackCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ForwardCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
