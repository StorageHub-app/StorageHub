using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows.Input;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;

namespace StorageHub.Desktop.Views;

/// <summary>One file in the Checksums window, filled in as its checksum arrives.</summary>
internal sealed class ChecksumRow(PaneTransferItem item) : INotifyPropertyChanged
{
    internal PaneTransferItem Item { get; } = item;

    public string Name { get; } = item.Name;

    public string Size { get; } = item.Length is { } length ? UiFormatting.FormatBytes(length) : string.Empty;

    /// <summary>The SHA-256 in hex, or empty until it is known.</summary>
    public string Sha256 { get; private set; } = string.Empty;

    /// <summary>Where it came from, or why there is none.</summary>
    public string Source { get; private set; } = Ui.Shell.ChecksumPending;

    /// <summary>Whether it matches what was expected, or which other file it is the same as.</summary>
    public string Match { get; private set; } = string.Empty;

    public bool Failed { get; private set; }

    public bool Matches { get; private set; }

    public bool Differs { get; private set; }

    internal string? Value { get; private set; }

    internal void Show(ChecksumOutcome outcome)
    {
        Value = outcome.Sha256;
        Failed = outcome.Sha256 is null;
        Sha256 = outcome.Sha256 ?? string.Empty;
        Source = outcome.Failure ?? (outcome.Source == ObjectChecksumSource.Server
            ? Ui.Shell.ChecksumFromServer
            : Ui.Shell.ChecksumComputed);
        Raise(nameof(Value), nameof(Failed), nameof(Sha256), nameof(Source));
    }

    internal void ShowMatch(string text, bool matches, bool differs)
    {
        Match = text;
        Matches = matches;
        Differs = differs;
        Raise(nameof(Match), nameof(Matches), nameof(Differs));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(params string[] names)
    {
        foreach (var name in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>
/// What the Checksums window shows: each selected file's SHA-256, and how they compare, with each
/// other or with one pasted in.
/// </summary>
/// <remarks>
/// One file at a time, in the order they were selected, so a large selection fills in from the top
/// rather than asking the agent to read many files at once. Closing the window stops the rest.
/// </remarks>
internal sealed class ChecksumsModel : INotifyPropertyChanged, IDisposable
{
    private readonly Func<PaneTransferItem, CancellationToken, Task<ChecksumOutcome>> _compute;
    private readonly IClipboardService? _clipboard;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly RelayCommand _copyCommand;
    private string _expected = string.Empty;
    private StatusLine _status = StatusLine.Muted(Ui.Shell.ChecksumWorking);
    private bool _done;
    private int _finished;

    /// <param name="skipped">How many selected items were left out: folders, and any past the limit.</param>
    internal ChecksumsModel(
        IReadOnlyList<PaneTransferItem> files,
        int skipped,
        Func<PaneTransferItem, CancellationToken, Task<ChecksumOutcome>> compute,
        IClipboardService? clipboard)
    {
        ArgumentNullException.ThrowIfNull(files);
        _compute = compute ?? throw new ArgumentNullException(nameof(compute));
        _clipboard = clipboard;
        Skipped = skipped;
        foreach (var file in files) Rows.Add(new ChecksumRow(file));
        _copyCommand = new RelayCommand(_ => _ = CopyAsync(), _ => _clipboard is not null && Rows.Any(static row => row.Value is not null));
    }

    public ObservableCollection<ChecksumRow> Rows { get; } = [];

    internal int Skipped { get; }

    /// <summary>A checksum to compare every file with, as pasted from a download page or a sum file.</summary>
    public string Expected
    {
        get => _expected;
        set
        {
            if (string.Equals(_expected, value, StringComparison.Ordinal)) return;
            _expected = value ?? string.Empty;
            Raise(nameof(Expected));
            Compare();
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

    public ICommand CopyCommand => _copyCommand;

    public static string Title => Ui.Shell.ChecksumsTitle;

    public static string Intro => Ui.Shell.ChecksumsIntro;

    public static string ExpectedLabel => Ui.Shell.ChecksumExpected;

    public static string ExpectedHint => Ui.Shell.ChecksumExpectedHint;

    public static string CopyLabel => Ui.Shell.ChecksumCopy;

    public static string CloseLabel => Ui.Dialogs.ButtonClose;

    public static string ColumnName => Ui.Shell.SearchColumnName;

    public static string ColumnSize => Ui.Shell.SearchColumnSize;

    public static string ColumnSha256 => Ui.Shell.ChecksumColumnSha256;

    public static string ColumnSource => Ui.Shell.ChecksumColumnSource;

    public static string ColumnMatch => Ui.Shell.ChecksumColumnMatch;

    /// <summary>Works through the files, then says how they compare.</summary>
    internal async Task ComputeAsync()
    {
        try
        {
            foreach (var row in Rows)
            {
                _lifetime.Token.ThrowIfCancellationRequested();
                row.Show(await _compute(row.Item, _lifetime.Token).ConfigureAwait(true));
                _finished++;
                Compare();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }

        _done = true;
        Compare();
        _copyCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Puts every checksum on the clipboard as sha256sum writes them, "hash  name" a line, which
    /// sha256sum -c and its Windows equivalents read back.
    /// </summary>
    internal Task CopyAsync()
    {
        if (_clipboard is null) return Task.CompletedTask;
        var text = new StringBuilder();
        foreach (var row in Rows.Where(static row => row.Value is not null))
        {
            text.Append(row.Value).Append("  ").Append(row.Name).Append('\n');
        }

        return _clipboard.SetTextAsync(text.ToString(), _lifetime.Token);
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    /// <summary>Marks each file against the expected checksum, or against the others, and sums it up.</summary>
    private void Compare()
    {
        var expected = ChecksumCalculator.Normalize(_expected);
        var known = Rows.Where(static row => row.Value is not null).ToArray();
        foreach (var row in Rows)
        {
            if (row.Value is not { } value)
            {
                row.ShowMatch(string.Empty, matches: false, differs: false);
            }
            else if (expected is not null)
            {
                var same = string.Equals(value, expected, StringComparison.Ordinal);
                row.ShowMatch(same ? Ui.Shell.ChecksumMatchesExpected : Ui.Shell.ChecksumDiffersFromExpected, same, !same);
            }
            else if (known.Length > 1)
            {
                var twin = known.FirstOrDefault(other => other != row && string.Equals(other.Value, value, StringComparison.Ordinal));
                row.ShowMatch(
                    twin is null ? Ui.Shell.ChecksumUnique : Ui.Format(Ui.Shell.ChecksumSameAsFormat, twin.Name),
                    twin is not null,
                    differs: false);
            }
            else
            {
                row.ShowMatch(string.Empty, matches: false, differs: false);
            }
        }

        if (!_done)
        {
            Status = StatusLine.Muted(Ui.Format(Ui.Shell.ChecksumProgressFormat, _finished, Rows.Count));
            return;
        }

        Status = Summarize(expected, known);
    }

    private StatusLine Summarize(string? expected, ChecksumRow[] known)
    {
        var failed = Rows.Count(static row => row.Failed);
        var tail = (failed > 0 ? " " + Ui.Format(Ui.Shell.ChecksumFailedFormat, failed) : string.Empty) +
                   (Skipped > 0 ? " " + Ui.Format(Ui.Shell.ChecksumSkippedFormat, Skipped) : string.Empty);
        if (known.Length == 0)
        {
            return new StatusLine(Ui.Shell.ChecksumNoneRead + tail, MetricTone.Danger);
        }

        if (expected is not null)
        {
            var matching = known.Count(row => string.Equals(row.Value, expected, StringComparison.Ordinal));
            return new StatusLine(
                Ui.Format(Ui.Shell.ChecksumExpectedSummaryFormat, matching, known.Length) + tail,
                matching == known.Length && failed == 0 ? MetricTone.Success : MetricTone.Warning);
        }

        if (known.Length == 1)
        {
            return new StatusLine(Ui.Shell.ChecksumReady + tail, failed == 0 ? MetricTone.Success : MetricTone.Warning);
        }

        var distinct = known.Select(static row => row.Value).Distinct(StringComparer.Ordinal).Count();
        return distinct == 1
            ? new StatusLine(Ui.Format(Ui.Shell.ChecksumAllIdenticalFormat, known.Length) + tail, MetricTone.Success)
            : new StatusLine(Ui.Format(Ui.Shell.ChecksumDistinctFormat, distinct, known.Length) + tail, MetricTone.Neutral);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
