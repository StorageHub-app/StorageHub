using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;

namespace StorageHub.Desktop.Views;

/// <summary>One leftover, as the table shows it.</summary>
internal sealed record LeftoverRow(LeftoverItem Item, string Name, string Kind, string Folder, string Size, string Modified, string State)
{
    public bool InUse => Item.InUse;

    internal static LeftoverRow For(LeftoverItem item) => new(
        item,
        item.Name,
        item.Kind switch
        {
            LeftoverKind.ResumePartial => Ui.Settings.CleanupKindResumePartial,
            LeftoverKind.TransferStaging => Ui.Settings.CleanupKindTransferStaging,
            LeftoverKind.WriteProbe => Ui.Settings.CleanupKindWriteProbe,
            LeftoverKind.RenameTemporary => Ui.Settings.CleanupKindRenameTemporary,
            _ => Ui.Settings.CleanupKindEditSession
        },
        item.Folder,
        UiFormatting.FormatBytes(item.Size),
        LocalBrowserPresentation.FormatModified(item.LastModifiedUtc),
        item.InUse ? Ui.Settings.CleanupInUse : Ui.Settings.CleanupRemovable);
}

/// <summary>
/// What the Clean Up Leftover Files window shows: every leftover found, with its size and whether it
/// can go, and a removal of the selected ones once it has been confirmed.
/// </summary>
/// <remarks>
/// Nothing is selected to begin with, and nothing goes without the question: a partial copy is what
/// an interrupted transfer would have resumed from, and somebody should choose to lose that.
/// </remarks>
internal sealed class StagingCleanupModel : INotifyPropertyChanged, IDisposable
{
    private readonly Func<bool, CancellationToken, Task<LeftoverScan>> _scan;
    private readonly Func<IReadOnlyList<LeftoverItem>, CancellationToken, Task<LeftoverRemoval>> _remove;
    private readonly IDialogService? _dialogs;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly RelayCommand _searchCommand;
    private readonly RelayCommand _removeCommand;
    private StatusLine _status = StatusLine.Muted(Ui.Settings.CleanupScanning);
    private bool _busy;
    private bool _includeRemote = true;

    internal StagingCleanupModel(
        Func<bool, CancellationToken, Task<LeftoverScan>> scan,
        Func<IReadOnlyList<LeftoverItem>, CancellationToken, Task<LeftoverRemoval>> remove,
        IDialogService? dialogs)
    {
        _scan = scan ?? throw new ArgumentNullException(nameof(scan));
        _remove = remove ?? throw new ArgumentNullException(nameof(remove));
        _dialogs = dialogs;
        _searchCommand = new RelayCommand(_ => _ = SearchAsync(), _ => !_busy);
        _removeCommand = new RelayCommand(_ => _ = RemoveAsync(), _ => !_busy && Removable().Count > 0);
        SelectedRows.CollectionChanged += SelectionChanged;
    }

    public ObservableCollection<LeftoverRow> Rows { get; } = [];

    public ObservableCollection<LeftoverRow> SelectedRows { get; } = [];

    public bool IncludeRemote
    {
        get => _includeRemote;
        set
        {
            if (_includeRemote == value) return;
            _includeRemote = value;
            Raise(nameof(IncludeRemote));
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

    public ICommand SearchCommand => _searchCommand;

    public ICommand RemoveCommand => _removeCommand;

    public static string Title => Ui.Settings.CleanupTitle;

    public static string Intro => Ui.Settings.CleanupIntro;

    public static string ColumnName => Ui.Settings.CleanupColumnName;

    public static string ColumnKind => Ui.Settings.CleanupColumnKind;

    public static string ColumnFolder => Ui.Settings.CleanupColumnFolder;

    public static string ColumnSize => Ui.Settings.CleanupColumnSize;

    public static string ColumnModified => Ui.Settings.CleanupColumnModified;

    public static string ColumnState => Ui.Settings.CleanupColumnState;

    public static string IncludeRemoteLabel => Ui.Settings.CleanupIncludeRemote;

    public static string SearchLabel => Ui.Settings.CleanupSearchAgain;

    public static string RemoveLabel => Ui.Settings.CleanupRemoveSelected;

    public static string CloseLabel => Ui.Dialogs.ButtonClose;

    /// <summary>Looks for leftovers again and shows what was found.</summary>
    internal async Task SearchAsync()
    {
        if (_busy) return;
        SetBusy(true);
        Status = StatusLine.Muted(Ui.Settings.CleanupScanning);
        try
        {
            var scan = await _scan(_includeRemote, _lifetime.Token).ConfigureAwait(true);
            SelectedRows.Clear();
            Rows.Clear();
            foreach (var item in scan.Items
                .OrderBy(static item => item.InUse)
                .ThenByDescending(static item => item.Size))
            {
                Rows.Add(LeftoverRow.For(item));
            }

            Status = Describe(scan);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Removes the selected leftovers that can go, once that has been confirmed.</summary>
    internal async Task RemoveAsync()
    {
        if (_busy) return;
        var chosen = Removable();
        if (chosen.Count == 0)
        {
            Status = new StatusLine(Ui.Settings.CleanupSelectFirst, MetricTone.Warning);
            return;
        }

        if (_dialogs is not null)
        {
            var answer = await _dialogs.ConfirmAsync(
                new DialogRequest
                {
                    Title = Ui.Settings.CleanupConfirmCaption,
                    Message = Ui.Format(
                        Ui.Settings.CleanupConfirmFormat,
                        chosen.Count,
                        UiFormatting.FormatBytes(chosen.Sum(static item => item.Size))),
                    Detail = chosen.Any(static item => item.Kind == LeftoverKind.ResumePartial)
                        ? Ui.Settings.CleanupConfirmDetail
                        : null,
                    Severity = DialogSeverity.Warning,
                    Buttons = DialogButtons.YesNo,
                    Default = DialogChoice.No
                },
                _lifetime.Token).ConfigureAwait(true);
            if (answer != DialogChoice.Yes) return;
        }

        SetBusy(true);
        try
        {
            var removal = await _remove(chosen, _lifetime.Token).ConfigureAwait(true);
            var kept = removal.Kept.Select(static kept => kept.Item).ToHashSet();
            foreach (var row in Rows.Where(row => chosen.Contains(row.Item) && !kept.Contains(row.Item)).ToArray())
            {
                SelectedRows.Remove(row);
                Rows.Remove(row);
            }

            Status = Describe(removal);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            SetBusy(false);
        }
    }

    internal static StatusLine Describe(LeftoverScan scan)
    {
        var parts = new List<string>();
        if (scan.Items.Count == 0)
        {
            parts.Add(Ui.Settings.CleanupNoneFound);
        }
        else
        {
            parts.Add(Ui.Format(
                Ui.Settings.CleanupFoundFormat,
                scan.Items.Count,
                UiFormatting.FormatBytes(scan.Items.Sum(static item => item.Size)),
                scan.Items.Count(static item => !item.InUse)));
        }

        if (scan.SyncRunning) parts.Add(Ui.Settings.CleanupSyncRunning);
        if (scan.FoldersUnreachable > 0) parts.Add(Ui.Format(Ui.Settings.CleanupUnreachableFormat, scan.FoldersUnreachable));
        if (scan.Truncated) parts.Add(Ui.Settings.CleanupTruncated);
        if (scan.AgentFailure is { } failure) parts.Add(failure.Message);
        var tone = scan.AgentFailure is not null || scan.FoldersUnreachable > 0 || scan.SyncRunning
            ? MetricTone.Warning
            : MetricTone.Neutral;
        return new StatusLine(string.Join(" ", parts), tone);
    }

    internal static StatusLine Describe(LeftoverRemoval removal)
    {
        var text = Ui.Format(Ui.Settings.CleanupRemovedFormat, removal.Removed, UiFormatting.FormatBytes(removal.BytesRemoved));
        if (removal.Kept.Count == 0)
        {
            return new StatusLine(text, MetricTone.Success);
        }

        var reasons = string.Join(" ", removal.Kept.Select(static kept => kept.Reason).Distinct(StringComparer.Ordinal));
        return new StatusLine(text + " " + Ui.Format(Ui.Settings.CleanupKeptFormat, removal.Kept.Count, reasons), MetricTone.Warning);
    }

    /// <summary>The selected leftovers that nothing is using.</summary>
    private List<LeftoverItem> Removable() =>
        [.. SelectedRows.Where(static row => !row.InUse).Select(static row => row.Item)];

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _searchCommand.RaiseCanExecuteChanged();
        _removeCommand.RaiseCanExecuteChanged();
    }

    private void SelectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        _removeCommand.RaiseCanExecuteChanged();

    public void Dispose()
    {
        if (_lifetime.IsCancellationRequested) return;
        _lifetime.Cancel();
        _lifetime.Dispose();
        SelectedRows.CollectionChanged -= SelectionChanged;
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public event PropertyChangedEventHandler? PropertyChanged;
}
