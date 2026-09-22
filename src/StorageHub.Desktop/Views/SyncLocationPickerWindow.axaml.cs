using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>
/// Chooses a folder of one connection as a sync location's root.
/// </summary>
/// <remarks>
/// The browsing is <see cref="SyncLocationBrowser"/>'s; what is here is keeping the window in step
/// with it and not letting two listings run at once.
/// </remarks>
internal sealed class SyncLocationPickerModel : INotifyPropertyChanged
{
    private readonly SyncLocationBrowser _browser;
    private readonly string _initialPath;
    private string _address;
    private SyncLocationFolder? _selectedFolder;
    private StatusLine _status = StatusLine.Muted(Ui.Sync.PickerLoadingFolders);
    private bool _isBusy;
    private readonly ICommand[] _commands;

    internal SyncLocationPickerModel(
        SyncLocationBrowser browser,
        ConnectionChoice connection,
        string initialPath,
        string locationName)
    {
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        ArgumentNullException.ThrowIfNull(connection);
        _initialPath = initialPath ?? string.Empty;
        _address = _initialPath;
        Title = Ui.Format(Ui.Sync.PickerTitleWithConnectionFormat, locationName, connection.DisplayName);
        AccessibleTitle = Ui.Format(Ui.Sync.PickerTitleFormat, locationName);
        Heading = Ui.Format(Ui.Sync.PickerFoldersOfFormat, connection.DisplayName);
        Hint = Ui.Format(Ui.Sync.PickerBrowseHintFormat, connection.ProviderName);

        UpCommand = new RelayCommand(_ => _ = RunAsync(_browser.UpAsync), _ => !IsBusy && _browser.CanGoUp);
        RootCommand = new RelayCommand(_ => _ = GoAsync(string.Empty), _ => !IsBusy);
        RefreshCommand = new RelayCommand(_ => _ = GoAsync(_browser.CurrentPath), _ => !IsBusy);
        GoCommand = new RelayCommand(_ => _ = GoAsync(Address), _ => !IsBusy);
        OpenCommand = new RelayCommand(
            _ => _ = SelectedFolder is { } folder ? GoAsync(folder.RelativePath) : Task.CompletedTask,
            _ => !IsBusy && SelectedFolder is not null);
        LoadMoreCommand = new RelayCommand(
            _ => _ = RunAsync(_browser.LoadMoreAsync), _ => !IsBusy && _browser.CanLoadMore);
        SelectCommand = new RelayCommand(
            _ => Finish(_browser.CurrentPath), _ => !IsBusy && _browser.HasLoaded);
        CancelCommand = new RelayCommand(_ => Finish(null));
        _commands = [UpCommand, RootCommand, RefreshCommand, GoCommand, OpenCommand, LoadMoreCommand, SelectCommand];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal event EventHandler? Closed;

    public string Title { get; }

    public string AccessibleTitle { get; }

    public string Heading { get; }

    public string Hint { get; }

    public ObservableCollection<SyncLocationFolder> Folders { get; } = [];

    /// <summary>The path box: where the picker is, or what somebody typed to go to.</summary>
    public string Address
    {
        get => _address;
        set => Set(ref _address, value ?? string.Empty);
    }

    public SyncLocationFolder? SelectedFolder
    {
        get => _selectedFolder;
        set { if (Set(ref _selectedFolder, value)) RaiseCommands(); }
    }

    public StatusLine Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set { if (Set(ref _isBusy, value)) RaiseCommands(); }
    }

    public ICommand UpCommand { get; }

    public ICommand RootCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand GoCommand { get; }

    public ICommand OpenCommand { get; }

    public ICommand LoadMoreCommand { get; }

    public ICommand SelectCommand { get; }

    public ICommand CancelCommand { get; }

    /// <summary>The folder chosen, relative to the connection root; null when dismissed.</summary>
    internal string? Result { get; private set; }

    public static string UpLabel => Ui.Commands.GoUp;

    public static string RootLabel => Ui.Sync.PickerRoot;

    public static string RefreshLabel => Ui.Sync.PickerRefresh;

    public static string LoadMoreLabel => Ui.Sync.PickerLoadMore;

    public static string SelectLabel => Ui.Sync.PickerSelectFolder;

    public static string CancelLabel => Ui.Dialogs.ButtonCancel;

    public static string AddressPlaceholder => Ui.Sync.PickerConnectionRoot;

    public static string AddressAccessibleName => Ui.Sync.PickerAddressAccessibleName;

    public static string FoldersAccessibleName => Ui.Sync.PickerFoldersAccessibleName;

    public static string StatusAccessibleName => Ui.Sync.PickerStatusAccessibleName;

    /// <summary>Lists the folder the location already names, or the root when it names none.</summary>
    internal Task StartAsync() => GoAsync(_initialPath);

    internal Task GoAsync(string path) => RunAsync(cancellationToken => _browser.NavigateAsync(path, cancellationToken));

    private async Task RunAsync(Func<CancellationToken, Task> step)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var running = step(CancellationToken.None);
            Show();
            await running.ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            Show();
        }
    }

    /// <summary>Brings the list, the path box and the status up to where the browser is.</summary>
    private void Show()
    {
        // Appending keeps what is listed, and the selection in it, where it was.
        if (Folders.Count > _browser.Folders.Count ||
            (Folders.Count > 0 && !ReferenceEquals(Folders[0], _browser.Folders.ElementAtOrDefault(0))))
        {
            Folders.Clear();
        }

        for (var index = Folders.Count; index < _browser.Folders.Count; index++)
        {
            Folders.Add(_browser.Folders[index]);
        }

        if (_browser.HasLoaded) Address = _browser.CurrentPath;
        Status = new StatusLine(_browser.Status, _browser.IsFailure ? MetricTone.Danger : MetricTone.Neutral);
        RaiseCommands();
    }

    private void RaiseCommands()
    {
        foreach (var command in _commands) ((RelayCommand)command).RaiseCanExecuteChanged();
    }

    private void Finish(string? result)
    {
        Result = result;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

/// <summary>
/// The folders of one connection, for a sync location, against 1.x's SyncLocationPickerForm.
/// </summary>
public partial class SyncLocationPickerWindow : Window
{
    public SyncLocationPickerWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is SyncLocationPickerModel model) model.Closed += (_, _) => Close();
        };
        Opened += (_, _) =>
        {
            if (DataContext is SyncLocationPickerModel model) _ = model.StartAsync();
        };
    }

    /// <summary>Opens a folder by double-clicking it, as the listing in a pane does.</summary>
    private void FolderDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is SyncLocationPickerModel model && model.OpenCommand.CanExecute(null))
        {
            model.OpenCommand.Execute(null);
        }
    }

    /// <summary>Asks over a window; answers null when there is none to ask over.</summary>
    internal static async Task<string?> AskAsync(
        Window? owner,
        Func<IRemoteStorageAgentClient> clients,
        ConnectionChoice connection,
        string initialPath,
        string locationName)
    {
        if (owner is null) return null;
        var model = new SyncLocationPickerModel(
            new SyncLocationBrowser(clients, connection.ConnectionId), connection, initialPath, locationName);
        await new SyncLocationPickerWindow { DataContext = model }.ShowDialog(owner).ConfigureAwait(true);
        return model.Result;
    }
}
