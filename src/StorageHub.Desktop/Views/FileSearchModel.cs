using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>One match, as the results table shows it.</summary>
internal sealed record SearchHitRow(FileSearchHit Hit, string Name, string Folder, string Size, string Modified)
{
    internal static SearchHitRow For(FileSearchHit hit) => new(
        hit,
        hit.Name,
        hit.Folder.Length == 0 ? "/" : hit.Folder,
        hit.IsContainer ? Ui.Shell.SearchFolderKind : hit.Length is { } length ? UiFormatting.FormatBytes(length) : string.Empty,
        hit.ModifiedUtc is { } modified ? LocalBrowserPresentation.FormatModified(modified) : string.Empty);
}

/// <summary>
/// What the Search window shows: a name to look for, every match under the pane's folder, and a way
/// back to the pane with the one chosen.
/// </summary>
/// <remarks>
/// One search at a time. Starting another stops the one running, and so does closing the window,
/// since a walk of a large tree nobody is waiting for is only load on a server.
/// </remarks>
internal sealed class FileSearchModel : INotifyPropertyChanged, IDisposable
{
    private readonly Func<string, IProgress<int>, CancellationToken, Task<FileSearchResult>> _search;
    private readonly Func<FileSearchHit, Task>? _reveal;
    private readonly RelayCommand _searchCommand;
    private readonly RelayCommand _stopCommand;
    private readonly RelayCommand _revealCommand;
    private CancellationTokenSource? _running;
    private string _query = string.Empty;
    private SearchHitRow? _selected;
    private StatusLine _status = StatusLine.Muted(Ui.Shell.SearchReady);

    /// <param name="where">The folder searched, as the pane names it, for the line at the top.</param>
    /// <param name="reveal">Shows a match in the pane it was searched from; null leaves Go to out.</param>
    internal FileSearchModel(
        Func<string, IProgress<int>, CancellationToken, Task<FileSearchResult>> search,
        string where,
        Func<FileSearchHit, Task>? reveal)
    {
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _reveal = reveal;
        Intro = Ui.Format(Ui.Shell.SearchIntroFormat, where);
        _searchCommand = new RelayCommand(_ => _ = SearchAsync(), _ => !string.IsNullOrWhiteSpace(_query));
        _stopCommand = new RelayCommand(_ => Stop(), _ => IsSearching);
        _revealCommand = new RelayCommand(_ => _ = RevealAsync(), _ => _reveal is not null && _selected is not null);
    }

    public ObservableCollection<SearchHitRow> Rows { get; } = [];

    public string Query
    {
        get => _query;
        set
        {
            if (string.Equals(_query, value, StringComparison.Ordinal)) return;
            _query = value ?? string.Empty;
            Raise(nameof(Query));
            _searchCommand.RaiseCanExecuteChanged();
        }
    }

    public SearchHitRow? SelectedRow
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            Raise(nameof(SelectedRow));
            _revealCommand.RaiseCanExecuteChanged();
        }
    }

    public StatusLine Status
    {
        get => _status;
        private set
        {
            _status = value;
            Raise(nameof(Status));
        }
    }

    public bool IsSearching => _running is not null;

    public ICommand SearchCommand => _searchCommand;

    public ICommand StopCommand => _stopCommand;

    public ICommand RevealCommand => _revealCommand;

    public string Intro { get; }

    public static string Title => Ui.Shell.SearchTitle;

    public static string QueryLabel => Ui.Shell.SearchQueryLabel;

    public static string QueryHint => Ui.Shell.SearchQueryHint;

    public static string SearchLabel => Ui.Shell.SearchButton;

    public static string StopLabel => Ui.Shell.SearchStop;

    public static string RevealLabel => Ui.Shell.SearchGoTo;

    public static string CloseLabel => Ui.Dialogs.ButtonClose;

    public static string ColumnName => Ui.Shell.SearchColumnName;

    public static string ColumnFolder => Ui.Shell.SearchColumnFolder;

    public static string ColumnSize => Ui.Shell.SearchColumnSize;

    public static string ColumnModified => Ui.Shell.SearchColumnModified;

    /// <summary>Runs the search for what is in the box, replacing whatever was found before.</summary>
    internal async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(_query)) return;

        Stop();
        var running = new CancellationTokenSource();
        _running = running;
        RaiseRunning();
        Rows.Clear();
        SelectedRow = null;
        Status = StatusLine.Muted(Ui.Shell.Searching);
        var progress = new Progress<int>(scanned =>
        {
            if (_running == running) Status = StatusLine.Muted(Ui.Format(Ui.Shell.SearchingFormat, scanned));
        });

        try
        {
            var result = await _search(_query, progress, running.Token).ConfigureAwait(true);
            if (_running != running) return;
            foreach (var hit in result.Hits) Rows.Add(SearchHitRow.For(hit));
            Status = Describe(result);
        }
        catch (OperationCanceledException) when (running.IsCancellationRequested)
        {
            if (_running == running) Status = new StatusLine(Ui.Shell.SearchStopped, MetricTone.Warning);
        }
        finally
        {
            if (_running == running)
            {
                _running = null;
                RaiseRunning();
            }

            running.Dispose();
        }
    }

    /// <summary>Stops the search under way; the table stays empty rather than showing part of an answer.</summary>
    internal void Stop()
    {
        if (_running is not { } running) return;
        running.Cancel();
    }

    internal Task RevealAsync() =>
        _reveal is not null && _selected is { } row ? _reveal(row.Hit) : Task.CompletedTask;

    public void Dispose()
    {
        Stop();
    }

    private static StatusLine Describe(FileSearchResult result)
    {
        if (result.Failure is { } failure)
        {
            return new StatusLine(failure.Message, MetricTone.Danger);
        }

        if (result.Truncated)
        {
            return new StatusLine(
                Ui.Format(Ui.Shell.SearchLimitFormat, result.Hits.Count, result.Scanned),
                MetricTone.Warning);
        }

        return result.Hits.Count == 0
            ? StatusLine.Muted(Ui.Format(Ui.Shell.SearchNothingFormat, result.Scanned))
            : new StatusLine(Ui.Format(Ui.Shell.SearchFoundFormat, result.Hits.Count, result.Scanned), MetricTone.Success);
    }

    private void RaiseRunning()
    {
        Raise(nameof(IsSearching));
        _stopCommand.RaiseCanExecuteChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
