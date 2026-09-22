using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>One line of the activity log, as the table shows it.</summary>
internal sealed record ActivityRow(
    string Key,
    string Updated,
    string Area,
    string Item,
    string State,
    string Details)
{
    internal static ActivityRow From(ActivityEntry entry) => new(
        entry.Key,
        entry.UpdatedUtc.LocalDateTime.ToString("g", CultureInfo.CurrentCulture),
        entry.Area,
        entry.Item,
        entry.State,
        entry.Details);
}

/// <summary>
/// The Logs tab of the queue: recent transfers and sync runs, newest first.
/// </summary>
/// <remarks>
/// <para>
/// It rides the queue's timer rather than keeping one of its own, and only while its tab is the
/// one showing -- which is what the WinForms control did by stopping its timer when hidden. A
/// background poll is throttled to every five seconds and stays quiet: announcing "refreshing" on
/// every tick made the status line flicker between two strings for work nobody asked for.
/// </para>
/// <para>
/// Rows are reconciled by key rather than rebuilt. Clearing and refilling made every row blink on
/// each poll in 1.x; its fix compared cells by position, which still rewrote every row whenever a
/// new transfer arrived at the top. Matching by key means a new record is one insert, a changed one
/// is one replace, and the selection survives both.
/// </para>
/// </remarks>
internal sealed class ActivityLogModel : INotifyPropertyChanged
{
    /// <summary>How often a background poll is allowed to reach the agent.</summary>
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly Func<CancellationToken, Task<ActivityLogResult>> _read;
    private readonly Func<DateTimeOffset> _clock;
    private DateTimeOffset? _lastRead;
    private StatusLine _status = StatusLine.Muted(Ui.Transfer.ActivityNotLoaded);
    private ActivityRow? _selected;
    private bool _busy;

    /// <param name="read">
    /// How the log is read -- <see cref="ActivityLogReader.ReadAsync"/> in the shell. A function
    /// rather than the reader so the screen can be tested without standing up two agent clients;
    /// the reader has tests of its own.
    /// </param>
    internal ActivityLogModel(
        Func<CancellationToken, Task<ActivityLogResult>> read,
        Func<DateTimeOffset>? clock = null)
    {
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public ObservableCollection<ActivityRow> Rows { get; } = [];

    public ActivityRow? Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public StatusLine Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>
    /// Reads the log and reconciles the table against it.
    /// </summary>
    /// <param name="background">
    /// A timer tick rather than somebody asking. Throttled to <see cref="PollInterval"/>, and it
    /// does not say "refreshing" while it works.
    /// </param>
    internal async Task RefreshAsync(bool background = false, CancellationToken cancellationToken = default)
    {
        if (_busy) return;
        if (background && _lastRead is { } last && _clock() - last < PollInterval) return;

        _busy = true;
        if (!background) Status = StatusLine.Muted(Ui.Transfer.ActivityRefreshing);
        try
        {
            var result = await _read(cancellationToken).ConfigureAwait(true);
            _lastRead = _clock();
            if (result.Failed)
            {
                // The rows stay. What was last read is still true as far as anybody knows, and a
                // table that empties whenever the agent restarts is worse than one that is a poll old.
                Status = new StatusLine(result.Describe(), MetricTone.Danger);
                return;
            }

            Reconcile([.. result.Entries.Select(ActivityRow.From)]);
            Status = new StatusLine(result.Describe(), MetricTone.Success);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Brings <see cref="Rows"/> to <paramref name="wanted"/> with as few edits as it takes.
    /// </summary>
    /// <remarks>
    /// Walks the wanted order once. A row already in place is replaced only if its text changed; a
    /// row further down is moved up, which keeps the object and so the selection; anything else is
    /// inserted. What is left over at the end is what the log no longer holds.
    /// </remarks>
    internal void Reconcile(IReadOnlyList<ActivityRow> wanted)
    {
        ArgumentNullException.ThrowIfNull(wanted);

        for (var index = 0; index < wanted.Count; index++)
        {
            var row = wanted[index];
            if (index < Rows.Count && string.Equals(Rows[index].Key, row.Key, StringComparison.Ordinal))
            {
                if (Rows[index] != row) Replace(index, row);
                continue;
            }

            var existing = IndexOf(row.Key, from: index + 1);
            if (existing >= 0)
            {
                Rows.Move(existing, index);
                if (Rows[index] != row) Replace(index, row);
            }
            else
            {
                Rows.Insert(index, row);
            }
        }

        while (Rows.Count > wanted.Count)
        {
            Rows.RemoveAt(Rows.Count - 1);
        }
    }

    /// <summary>
    /// Swaps a row's text for newer text, keeping it selected if it was.
    /// </summary>
    /// <remarks>
    /// The row is a record, so newer text is a new object, and a table selects objects. Without
    /// carrying the selection across, a transfer that ticked from Transferring to Completed would
    /// deselect itself under the pointer.
    /// </remarks>
    private void Replace(int index, ActivityRow row)
    {
        var wasSelected = ReferenceEquals(Rows[index], _selected);
        Rows[index] = row;
        if (wasSelected) Selected = row;
    }

    private int IndexOf(string key, int from)
    {
        for (var index = from; index < Rows.Count; index++)
        {
            if (string.Equals(Rows[index].Key, key, StringComparison.Ordinal)) return index;
        }

        return -1;
    }

    public static string ColumnUpdated => Ui.Transfer.ActivityColumnUpdated;

    public static string ColumnArea => Ui.Transfer.ActivityColumnArea;

    public static string ColumnItem => Ui.Transfer.ActivityColumnItem;

    public static string ColumnState => Ui.Transfer.ActivityColumnState;

    public static string ColumnDetails => Ui.Transfer.ActivityColumnDetails;

    public static string GridAccessibleName => Ui.Transfer.ActivityGridAccessibleName;

    public static string StatusAccessibleName => Ui.Transfer.ActivityStatusAccessibleDescription;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
