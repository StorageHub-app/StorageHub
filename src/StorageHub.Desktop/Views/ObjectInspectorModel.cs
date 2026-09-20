using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>One version of the object, as the provider reported it.</summary>
internal sealed record ObjectVersionRow(
    string Current,
    string VersionId,
    string Size,
    string Modified,
    string DeleteMarker,
    string EntityTag);

/// <summary>One name and its value, for metadata and for tags alike.</summary>
internal sealed record ObjectDetailRow(string Name, string Value);

/// <summary>
/// The read-only object inspector: versions, metadata and tags for one file on a saved connection.
/// </summary>
/// <remarks>
/// <para>
/// A window over <see cref="ObjectInspectorController"/>, which was already in Core and already
/// serialised refresh against paging. What is here is words: the three sections each keep their
/// own failure, so a provider that has versions but refuses tags shows the versions and says which
/// section it could not load, rather than showing nothing and one error for all three.
/// </para>
/// <para>
/// Read-only is the point and the heading says so. Nothing here signs a link or changes an
/// object; the same client's mutation calls are made from the pane, not from this screen.
/// </para>
/// </remarks>
internal sealed class ObjectInspectorModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly ObjectInspectorController _controller;
    private readonly CancellationTokenSource _lifetime = new();
    private StatusLine _status = StatusLine.Muted(Ui.Inspector.TheInspectorConnectsToTheBackgroundAgent);
    private StatusLine _versionsNotice = StatusLine.Muted(Ui.Inspector.VersionHistoryHasNotBeenLoaded);
    private StatusLine _metadataNotice = StatusLine.Muted(Ui.Inspector.MetadataHasNotBeenLoaded);
    private StatusLine _tagsNotice = StatusLine.Muted(Ui.Inspector.TagsHaveNotBeenLoaded);
    private bool _canLoadMore;
    private bool _isBusy;
    private int _selectedTab;

    internal ObjectInspectorModel(ObjectInspectorController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        Address = controller.State.Address;

        RefreshCommand = new RelayCommand(_ => _ = LoadAsync(), _ => !IsBusy);
        LoadMoreCommand = new RelayCommand(_ => _ = LoadMoreAsync(), _ => !IsBusy && CanLoadMore);
        CloseCommand = new RelayCommand(_ => Closed?.Invoke(this, EventArgs.Empty));
        Show(controller.State);
    }

    /// <summary>An inspector for one object, over a client the window owns.</summary>
    internal static ObjectInspectorModel Create(
        ObjectInspectorAddress address,
        Func<IObjectInspectorAgentClient> clients)
    {
        ArgumentNullException.ThrowIfNull(clients);
        return new ObjectInspectorModel(new ObjectInspectorController(clients(), address, ownsClient: true));
    }

    public ObjectInspectorAddress Address { get; }

    public ObservableCollection<ObjectVersionRow> Versions { get; } = [];

    public ObservableCollection<ObjectDetailRow> Metadata { get; } = [];

    public ObservableCollection<ObjectDetailRow> Tags { get; } = [];

    /// <summary>The path, and for the title bar a version shortened from the left.</summary>
    public string Path => Address.RelativePath;

    public string Title => Ui.Format(Ui.Inspector.WindowTitleFormat, ShortenForTitle(Address.RelativePath));

    public string Identity => Ui.Format(Ui.Inspector.ConnectionIdentityFormat, Address.ConnectionId);

    public string VersionsTabLabel => Ui.Format(Ui.Inspector.TabVersionsFormat, Versions.Count);

    public string MetadataTabLabel => Ui.Format(Ui.Inspector.TabMetadataFormat, Metadata.Count);

    public string TagsTabLabel => Ui.Format(Ui.Inspector.TabTagsFormat, Tags.Count);

    public StatusLine VersionsNotice
    {
        get => _versionsNotice;
        private set => Set(ref _versionsNotice, value);
    }

    public StatusLine MetadataNotice
    {
        get => _metadataNotice;
        private set => Set(ref _metadataNotice, value);
    }

    public StatusLine TagsNotice
    {
        get => _tagsNotice;
        private set => Set(ref _tagsNotice, value);
    }

    public StatusLine Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool CanLoadMore
    {
        get => _canLoadMore;
        private set
        {
            if (!Set(ref _canLoadMore, value)) return;
            (LoadMoreCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (LoadMoreCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Which section is showing: versions, metadata or tags, in that order.</summary>
    public int SelectedTab
    {
        get => _selectedTab;
        set => Set(ref _selectedTab, value);
    }

    public ICommand RefreshCommand { get; }

    public ICommand LoadMoreCommand { get; }

    public ICommand CloseCommand { get; }

    /// <summary>Raised when the window should close.</summary>
    internal event EventHandler? Closed;

    /// <summary>Asks the agent for all three sections.</summary>
    internal async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Status = StatusLine.Muted(Ui.Inspector.LoadingVersionHistoryMetadataAndTags);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            Show(await _controller.RefreshAsync(linked.Token).ConfigureAwait(true));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error) when (IsExpected(error))
        {
            ShowFailure(error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Asks for the next page of versions, keeping the ones already shown.</summary>
    internal async Task LoadMoreAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || !CanLoadMore) return;
        IsBusy = true;
        try
        {
            Status = StatusLine.Muted(Ui.Inspector.LoadingTheNextVersionPage);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            Show(await _controller.LoadMoreVersionsAsync(linked.Token).ConfigureAwait(true));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error) when (IsExpected(error))
        {
            ShowFailure(error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Show(ObjectInspectorState state)
    {
        Versions.Clear();
        foreach (var version in state.Versions)
        {
            Versions.Add(new ObjectVersionRow(
                version.IsLatest ? Ui.Inspector.Latest : string.Empty,
                version.VersionId,
                version.Size?.ToString("N0", CultureInfo.CurrentCulture) ?? Ui.Inspector.NotReported,
                version.LastModifiedUtc?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
                    ?? Ui.Inspector.NotReported,
                version.IsDeleteMarker ? Ui.Inspector.Yes : Ui.Inspector.No,
                version.EntityTag ?? Ui.Inspector.NotReported));
        }

        Metadata.Clear();
        foreach (var entry in state.Metadata) Metadata.Add(new ObjectDetailRow(entry.Name, entry.Value));

        Tags.Clear();
        foreach (var entry in state.Tags) Tags.Add(new ObjectDetailRow(entry.Name, entry.Value));

        VersionsNotice = Notice(
            state.VersionsFailure, Versions.Count, Ui.Inspector.NoVersionsReturned, Ui.Inspector.LoadedVersionsFormat);
        MetadataNotice = Notice(
            state.MetadataFailure, Metadata.Count, Ui.Inspector.NoMetadataReturned, Ui.Inspector.LoadedMetadataFormat);
        TagsNotice = Notice(
            state.TagsFailure, Tags.Count, Ui.Inspector.NoTagsReturned, Ui.Inspector.LoadedTagsFormat);
        CanLoadMore = state.CanLoadMoreVersions;

        Raise(nameof(VersionsTabLabel));
        Raise(nameof(MetadataTabLabel));
        Raise(nameof(TagsTabLabel));

        // Before the first load the controller's state is empty and not a failure, and the status
        // keeps saying the inspector will connect when shown.
        if (IsUntouched(state)) return;

        var failures = new[] { state.VersionsFailure, state.MetadataFailure, state.TagsFailure }
            .Count(static failure => failure is not null);
        Status = failures == 0
            ? new StatusLine(
                Ui.Format(Ui.Inspector.LoadedSummaryFormat, Versions.Count, Metadata.Count, Tags.Count),
                MetricTone.Success)
            : new StatusLine(Ui.Format(Ui.Inspector.LoadedWithFailuresFormat, failures), MetricTone.Warning);
    }

    /// <summary>
    /// A section's own line: its failure, that it was empty, or how much it holds.
    /// </summary>
    /// <remarks>
    /// The empty and loaded sentences are passed in already localised rather than composed from a
    /// noun: only English makes a plural by adding an "s" to the word the caller supplied.
    /// </remarks>
    private static StatusLine Notice(StorageIpcFailure? failure, int count, string emptyText, string loadedFormat) =>
        failure is not null
            ? new StatusLine(failure.Message, MetricTone.Warning)
            : StatusLine.Muted(count == 0 ? emptyText : Ui.Format(loadedFormat, count));

    private void ShowFailure(Exception error) => Status = new StatusLine(
        error switch
        {
            UnauthorizedAccessException => Ui.Inspector.StorageHubCouldNotAuthenticateToTheLocal,
            TimeoutException => Ui.Inspector.TheObjectInspectorRequestTimedOut,
            _ => Ui.Inspector.TheObjectInspectorCouldNotLoadDetails
        },
        MetricTone.Warning);

    /// <summary>The state the controller starts with: nothing loaded and nothing failed.</summary>
    private static bool IsUntouched(ObjectInspectorState state) =>
        state.Versions.Count == 0 && state.Metadata.Count == 0 && state.Tags.Count == 0 &&
        state.VersionsFailure is null && state.MetadataFailure is null && state.TagsFailure is null &&
        state.VersionContinuationToken is null;

    /// <summary>
    /// The last part of a long path, for a title bar that cannot show all of it.
    /// </summary>
    /// <remarks>
    /// From the left, because the end of a path is the part that tells objects apart. A cut that
    /// lands on the second half of a surrogate pair moves one further, so the title never starts
    /// with half a character.
    /// </remarks>
    internal static string ShortenForTitle(string relativePath, int maximumLength = 96)
    {
        if (relativePath.Length <= maximumLength) return relativePath;
        var tail = relativePath[^maximumLength..];
        if (char.IsLowSurrogate(tail[0])) tail = tail[1..];
        return "…" + tail;
    }

    private static bool IsExpected(Exception error) => error is
        IOException or TimeoutException or UnauthorizedAccessException or InvalidDataException or
        InvalidOperationException or ObjectDisposedException or System.Text.Json.JsonException;

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        await _controller.DisposeAsync().ConfigureAwait(false);
    }

    public static string SafetyNotice => Ui.Inspector.READONLYVersionPagesMetadataAndTags;

    public static string RefreshLabel => Ui.Inspector.Refresh;

    public static string LoadMoreLabel => Ui.Inspector.LoadMoreVersions2;

    public static string CloseLabel => Ui.Inspector.Close;

    public static string ColumnCurrent => Ui.Inspector.Current;

    public static string ColumnVersion => Ui.Inspector.VersionID;

    public static string ColumnSize => Ui.Inspector.Size;

    public static string ColumnModified => Ui.Inspector.ModifiedUTC;

    public static string ColumnDeleteMarker => Ui.Inspector.DeleteMarker;

    public static string ColumnEntityTag => Ui.Inspector.EntityTag;

    public static string ColumnName => Ui.Inspector.Name;

    public static string ColumnValue => Ui.Inspector.Value;

    public static string PathAccessibleName => Ui.Inspector.InspectedObjectPath;

    public static string SafetyAccessibleName => Ui.Inspector.ReadOnlyInspectorSafetyNotice;

    public static string TabsAccessibleName => Ui.Inspector.ObjectDetailCategories;

    public static string VersionsAccessibleName => Ui.Inspector.ObjectVersions;

    public static string MetadataAccessibleName => Ui.Inspector.ObjectMetadata;

    public static string TagsAccessibleName => Ui.Inspector.ObjectTags;

    public static string StatusAccessibleName => Ui.Inspector.ObjectInspectorStatus;

    public static string LoadMoreAccessibleName => Ui.Inspector.LoadTheNextObjectVersionPage;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
