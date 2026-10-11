using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using StorageHub.Contracts.Ipc;
using StorageHub.Contracts.Results;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>One path of a comparison, as the table shows it.</summary>
internal sealed record FolderCompareRow(
    FolderCompareEntry Entry,
    string Path,
    string LeftSize,
    string LeftModified,
    string Status,
    string RightSize,
    string RightModified,
    MetricTone Tone)
{
    public bool IsDifferent => Tone == MetricTone.Warning;

    public bool IsOnlyOneSide => Tone == MetricTone.Primary;

    public bool IsSame => Tone == MetricTone.Success;

    internal static FolderCompareRow For(FolderCompareEntry entry) => new(
        entry,
        entry.RelativePath,
        Size(entry.Left),
        Modified(entry.Left),
        entry.Kind switch
        {
            FolderCompareKind.OnlyLeft => Ui.Sync.CompareStatusOnlyLeft,
            FolderCompareKind.OnlyRight => Ui.Sync.CompareStatusOnlyRight,
            FolderCompareKind.Same => Ui.Sync.CompareStatusSame,
            _ => Ui.Format(Ui.Sync.CompareStatusDifferentFormat, Reasons(entry.Reasons))
        },
        Size(entry.Right),
        Modified(entry.Right),
        entry.Kind switch
        {
            FolderCompareKind.Different => MetricTone.Warning,
            FolderCompareKind.Same => MetricTone.Success,
            _ => MetricTone.Primary
        });

    /// <summary>What told the two apart, in the order somebody deciding which to keep reads it.</summary>
    internal static string Reasons(FolderCompareReasons reasons)
    {
        var parts = new List<string>(4);
        if (reasons.HasFlag(FolderCompareReasons.Type)) parts.Add(Ui.Sync.CompareReasonType);
        if (reasons.HasFlag(FolderCompareReasons.Checksum)) parts.Add(Ui.Sync.CompareReasonChecksum);
        if (reasons.HasFlag(FolderCompareReasons.Size)) parts.Add(Ui.Sync.CompareReasonSize);
        if (reasons.HasFlag(FolderCompareReasons.LeftNewer)) parts.Add(Ui.Sync.CompareReasonLeftNewer);
        if (reasons.HasFlag(FolderCompareReasons.RightNewer)) parts.Add(Ui.Sync.CompareReasonRightNewer);
        return string.Join(", ", parts);
    }

    private static string Size(FolderCompareItem? item) =>
        item?.Size is { } size && item.Kind == StorageItemKind.File ? UiFormatting.FormatBytes(size) : string.Empty;

    private static string Modified(FolderCompareItem? item) =>
        item is null ? string.Empty : LocalBrowserPresentation.FormatModified(item.LastModifiedUtc);
}

/// <summary>
/// What the Compare Folders window shows: the two panes' folders side by side as one list, filtered
/// by what kind of difference each path is, with the chosen differences copied either way.
/// </summary>
/// <remarks>
/// The comparing and the copying are handed in, so the window knows nothing of the agent or the
/// queue and a test can drive it with answers of its own.
/// </remarks>
internal sealed class FolderCompareModel : INotifyPropertyChanged, IDisposable
{
    private readonly Func<bool, CancellationToken, Task<StorageResult<FolderCompareOutcome>>> _compare;
    private readonly Func<FolderCompareOutcome, IReadOnlyList<FolderCompareEntry>, FolderCompareDirection,
        CancellationToken, Task<FolderCompareCopyResult>> _copy;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly RelayCommand _compareCommand;
    private readonly RelayCommand _copyToRight;
    private readonly RelayCommand _copyToLeft;
    private FolderCompareOutcome? _outcome;
    private FolderCompareRow[] _all = [];
    private StatusLine _status = StatusLine.Muted(Ui.Sync.CompareComparing);
    private bool _busy;
    private bool _showOnlyLeft = true;
    private bool _showOnlyRight = true;
    private bool _showDifferent = true;
    private bool _showSame;
    private bool _compareContents;
    private string _nameFilter = string.Empty;

    internal FolderCompareModel(
        string leftLabel,
        string rightLabel,
        Func<bool, CancellationToken, Task<StorageResult<FolderCompareOutcome>>> compare,
        Func<FolderCompareOutcome, IReadOnlyList<FolderCompareEntry>, FolderCompareDirection,
            CancellationToken, Task<FolderCompareCopyResult>> copy)
    {
        LeftLabel = leftLabel ?? string.Empty;
        RightLabel = rightLabel ?? string.Empty;
        _compare = compare ?? throw new ArgumentNullException(nameof(compare));
        _copy = copy ?? throw new ArgumentNullException(nameof(copy));
        _compareCommand = new RelayCommand(_ => _ = CompareAsync(), _ => !_busy);
        _copyToRight = new RelayCommand(
            _ => _ = CopyAsync(FolderCompareDirection.LeftToRight),
            _ => CanCopy(FolderCompareDirection.LeftToRight));
        _copyToLeft = new RelayCommand(
            _ => _ = CopyAsync(FolderCompareDirection.RightToLeft),
            _ => CanCopy(FolderCompareDirection.RightToLeft));
        SelectedRows.CollectionChanged += SelectionChanged;
    }

    public string LeftLabel { get; }

    public string RightLabel { get; }

    public ObservableCollection<FolderCompareRow> Rows { get; } = [];

    /// <summary>The rows picked in the table; what a copy copies.</summary>
    public ObservableCollection<FolderCompareRow> SelectedRows { get; } = [];

    public bool ShowOnlyLeft
    {
        get => _showOnlyLeft;
        set => SetFilter(ref _showOnlyLeft, value);
    }

    public bool ShowOnlyRight
    {
        get => _showOnlyRight;
        set => SetFilter(ref _showOnlyRight, value);
    }

    public bool ShowDifferent
    {
        get => _showDifferent;
        set => SetFilter(ref _showDifferent, value);
    }

    public bool ShowSame
    {
        get => _showSame;
        set => SetFilter(ref _showSame, value);
    }

    public string NameFilter
    {
        get => _nameFilter;
        set
        {
            _nameFilter = value ?? string.Empty;
            Filter();
        }
    }

    /// <summary>Whether the next comparison also reads both files of each same-sized pair.</summary>
    public bool CompareContents
    {
        get => _compareContents;
        set
        {
            if (_compareContents == value) return;
            _compareContents = value;
            Raise(nameof(CompareContents));
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

    public bool IsBusy => _busy;

    public string OnlyLeftLabel => Ui.Format(Ui.Sync.CompareOnlyLeftFormat, Count(FolderCompareKind.OnlyLeft));

    public string OnlyRightLabel => Ui.Format(Ui.Sync.CompareOnlyRightFormat, Count(FolderCompareKind.OnlyRight));

    public string DifferentLabel => Ui.Format(Ui.Sync.CompareDifferentFormat, Count(FolderCompareKind.Different));

    public string SameLabel => Ui.Format(Ui.Sync.CompareSameFormat, Count(FolderCompareKind.Same));

    public ICommand CompareCommand => _compareCommand;

    public ICommand CopyToRightCommand => _copyToRight;

    public ICommand CopyToLeftCommand => _copyToLeft;

    public static string Title => Ui.Sync.CompareWindowTitle;

    public static string LeftHeading => Ui.Sync.CompareLeft;

    public static string RightHeading => Ui.Sync.CompareRight;

    public static string ColumnPath => Ui.Sync.CompareColumnPath;

    public static string ColumnLeftSize => Ui.Sync.CompareColumnLeftSize;

    public static string ColumnLeftModified => Ui.Sync.CompareColumnLeftModified;

    public static string ColumnStatus => Ui.Sync.CompareColumnStatus;

    public static string ColumnRightSize => Ui.Sync.CompareColumnRightSize;

    public static string ColumnRightModified => Ui.Sync.CompareColumnRightModified;

    public static string NameFilterPlaceholder => Ui.Sync.CompareNameFilter;

    public static string CompareContentsLabel => Ui.Sync.CompareContents;

    public static string CompareAgainLabel => Ui.Sync.CompareAgain;

    public static string CopyToRightLabel => Ui.Sync.CompareCopyToRight;

    public static string CopyToLeftLabel => Ui.Sync.CompareCopyToLeft;

    public static string CloseLabel => Ui.Dialogs.ButtonClose;

    /// <summary>Compares the two folders again and shows what was found.</summary>
    internal async Task CompareAsync()
    {
        if (_busy) return;
        SetBusy(true);
        Status = StatusLine.Muted(Ui.Sync.CompareComparing);
        try
        {
            var result = await _compare(_compareContents, _lifetime.Token).ConfigureAwait(true);
            if (result.IsFailure)
            {
                Status = new StatusLine(result.Error!.Message, MetricTone.Danger);
                return;
            }

            _outcome = result.Value;
            _all = [.. result.Value.Entries.Select(FolderCompareRow.For)];
            Filter();
            Raise(nameof(OnlyLeftLabel));
            Raise(nameof(OnlyRightLabel));
            Raise(nameof(DifferentLabel));
            Raise(nameof(SameLabel));
            Status = Summary(result.Value);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Copies the selected differences one way, as ordinary queued transfers.</summary>
    internal async Task CopyAsync(FolderCompareDirection direction)
    {
        if (_busy || _outcome is null) return;
        var selected = SelectedRows.Select(static row => row.Entry).ToArray();
        if (selected.Length == 0)
        {
            Status = new StatusLine(Ui.Sync.CompareSelectDifferences, MetricTone.Warning);
            return;
        }

        SetBusy(true);
        try
        {
            var result = await _copy(_outcome, selected, direction, _lifetime.Token).ConfigureAwait(true);
            Status = Describe(result);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Stops whatever is under way, when the window closes.</summary>
    public void Dispose()
    {
        if (_lifetime.IsCancellationRequested) return;
        _lifetime.Cancel();
        _lifetime.Dispose();
        SelectedRows.CollectionChanged -= SelectionChanged;
    }

    internal static StatusLine Summary(FolderCompareOutcome outcome)
    {
        var counts = outcome.Entries.GroupBy(static entry => entry.Kind).ToDictionary(static g => g.Key, static g => g.Count());
        var text = Ui.Format(
            Ui.Sync.CompareSummaryFormat,
            counts.GetValueOrDefault(FolderCompareKind.OnlyLeft),
            counts.GetValueOrDefault(FolderCompareKind.OnlyRight),
            counts.GetValueOrDefault(FolderCompareKind.Different),
            counts.GetValueOrDefault(FolderCompareKind.Same));
        if (outcome.CaseInsensitive) text += " " + Ui.Sync.CompareCaseInsensitive;
        if (outcome.UncheckedPairs > 0)
        {
            return new StatusLine(text + " " + Ui.Format(Ui.Sync.CompareUncheckedFormat, outcome.UncheckedPairs), MetricTone.Warning);
        }

        return new StatusLine(text, MetricTone.Neutral);
    }

    internal static StatusLine Describe(FolderCompareCopyResult result)
    {
        if (result.Failure is { } failure)
        {
            return new StatusLine(
                result.Queued + result.FoldersCreated > 0
                    ? Ui.Format(Ui.Sync.CompareCopiedFormat, result.Queued, result.FoldersCreated) + " " + failure.Message
                    : failure.Message,
                failure.Kind == StorageFailureKind.Cancelled ? MetricTone.Neutral : MetricTone.Danger);
        }

        return result.Skipped + result.Renamed + result.LeftOut == 0
            ? new StatusLine(Ui.Format(Ui.Sync.CompareCopiedFormat, result.Queued, result.FoldersCreated), MetricTone.Success)
            : new StatusLine(
                Ui.Format(
                    Ui.Sync.CompareCopiedWithLeftOutFormat,
                    result.Queued,
                    result.FoldersCreated,
                    result.Skipped,
                    result.Renamed,
                    result.LeftOut),
                result.LeftOut > 0 ? MetricTone.Warning : MetricTone.Success);
    }

    /// <summary>
    /// Whether anything picked can go that way: something the source side has that is not the
    /// same on the other.
    /// </summary>
    private bool CanCopy(FolderCompareDirection direction) =>
        !_busy && _outcome is not null &&
        SelectedRows.Any(row => row.Entry.Kind != FolderCompareKind.Same &&
            (direction == FolderCompareDirection.LeftToRight ? row.Entry.Left : row.Entry.Right) is not null);

    private void Filter()
    {
        Rows.Clear();
        foreach (var row in _all)
        {
            var shown = row.Entry.Kind switch
            {
                FolderCompareKind.OnlyLeft => _showOnlyLeft,
                FolderCompareKind.OnlyRight => _showOnlyRight,
                FolderCompareKind.Different => _showDifferent,
                _ => _showSame
            };
            if (shown && LocalBrowserPresentation.MatchesFilter(row.Path, _nameFilter)) Rows.Add(row);
        }
    }

    private int Count(FolderCompareKind kind) => _all.Count(row => row.Entry.Kind == kind);

    private void SetFilter(ref bool field, bool value, [System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        if (field == value) return;
        field = value;
        Raise(name);
        Filter();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Raise(nameof(IsBusy));
        _compareCommand.RaiseCanExecuteChanged();
        _copyToRight.RaiseCanExecuteChanged();
        _copyToLeft.RaiseCanExecuteChanged();
    }

    private void SelectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _copyToRight.RaiseCanExecuteChanged();
        _copyToLeft.RaiseCanExecuteChanged();
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public event PropertyChangedEventHandler? PropertyChanged;
}
