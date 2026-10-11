using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;

namespace StorageHub.Desktop.Views;

/// <summary>One version of the object, as the provider reported it.</summary>
internal sealed record ObjectVersionRow(
    string Current,
    string VersionId,
    string Size,
    string Modified,
    string DeleteMarker,
    string EntityTag);

/// <summary>One name and its value, for attributes, metadata and tags alike.</summary>
internal sealed record ObjectDetailRow(string Name, string Value);

/// <summary>
/// The Properties window: an item's attributes, and for a file its versions, metadata and tags.
/// </summary>
/// <remarks>
/// <para>
/// A window over <see cref="ObjectInspectorController"/>, which serialises refresh, paging and the
/// two changes. What is here is words: each section keeps its own failure, so a provider that has
/// versions but refuses tags shows the versions and says which section it could not load, rather
/// than showing nothing and one error for all of them.
/// </para>
/// <para>
/// The General tab is the item's attributes as the connection reports them, with what it does not
/// report shown as not reported. Two things can be changed from it, the permission bits and the
/// modified time, each only where the connection advertises it; the button is dim otherwise and a
/// sentence under it says what this connection cannot do. Everything else stays read-only.
/// </para>
/// </remarks>
internal sealed class ObjectInspectorModel : INotifyPropertyChanged, IAsyncDisposable
{
    /// <summary>How times are written and read here: sortable, 24-hour, to the second.</summary>
    internal const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    private readonly ObjectInspectorController _controller;
    private readonly IDialogService? _dialogs;
    private readonly Func<string, int, Task<int?>>? _askPermissions;
    private readonly TimeZoneInfo _timeZone;
    private readonly CancellationTokenSource _lifetime = new();
    private StatusLine _status = StatusLine.Muted(Ui.Inspector.TheInspectorConnectsToTheBackgroundAgent);
    private StatusLine _generalNotice = StatusLine.Muted(Ui.Inspector.GeneralHasNotBeenLoaded);
    private StatusLine _versionsNotice = StatusLine.Muted(Ui.Inspector.VersionHistoryHasNotBeenLoaded);
    private StatusLine _metadataNotice = StatusLine.Muted(Ui.Inspector.MetadataHasNotBeenLoaded);
    private StatusLine _tagsNotice = StatusLine.Muted(Ui.Inspector.TagsHaveNotBeenLoaded);
    private StatusLine _checksumNotice = StatusLine.Muted(Ui.Inspector.ChecksumNotLoaded);
    private string _checksum = string.Empty;
    private ItemAttributes? _attributes;
    private bool _canLoadMore;
    private bool _isBusy;
    private int _selectedTab;

    /// <param name="controller">The item, and the agent client it is read and changed through.</param>
    /// <param name="dialogs">Asks for the new modified time. Null leaves Set modified time dim.</param>
    /// <param name="askPermissions">
    /// Asks for the new mode, given the item's name and its mode now, and answers the mode chosen
    /// or nothing. Null leaves Change permissions dim.
    /// </param>
    /// <param name="timeZone">The zone times are shown and typed in; this computer's unless a test says.</param>
    internal ObjectInspectorModel(
        ObjectInspectorController controller,
        IDialogService? dialogs = null,
        Func<string, int, Task<int?>>? askPermissions = null,
        TimeZoneInfo? timeZone = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _dialogs = dialogs;
        _askPermissions = askPermissions;
        _timeZone = timeZone ?? TimeZoneInfo.Local;
        Address = controller.State.Address;

        RefreshCommand = new RelayCommand(_ => _ = LoadAsync(), _ => !IsBusy);
        LoadMoreCommand = new RelayCommand(_ => _ = LoadMoreAsync(), _ => !IsBusy && CanLoadMore);
        ChangePermissionsCommand = new RelayCommand(_ => _ = ChangePermissionsAsync(), _ => !IsBusy && CanChangePermissions);
        SetModifiedTimeCommand = new RelayCommand(_ => _ = SetModifiedTimeAsync(), _ => !IsBusy && CanSetModifiedTime);
        CloseCommand = new RelayCommand(_ => Closed?.Invoke(this, EventArgs.Empty));
        GetChecksumCommand = new RelayCommand(_ => _ = GetChecksumAsync(preferServer: true), _ => !IsBusy);
        ComputeChecksumCommand = new RelayCommand(_ => _ = GetChecksumAsync(preferServer: false), _ => !IsBusy);
        Show(controller.State);
    }

    /// <summary>The Properties window for one item, over a client the window owns.</summary>
    internal static ObjectInspectorModel Create(
        ObjectInspectorAddress address,
        StorageItemKind kind,
        Func<IObjectInspectorAgentClient> clients,
        IDialogService? dialogs = null,
        Func<string, int, Task<int?>>? askPermissions = null)
    {
        ArgumentNullException.ThrowIfNull(clients);
        return new ObjectInspectorModel(
            new ObjectInspectorController(clients(), address, ownsClient: true, kind),
            dialogs,
            askPermissions);
    }

    public ObjectInspectorAddress Address { get; }

    /// <summary>The item's attributes, a row each.</summary>
    public ObservableCollection<ObjectDetailRow> General { get; } = [];

    public ObservableCollection<ObjectVersionRow> Versions { get; } = [];

    public ObservableCollection<ObjectDetailRow> Metadata { get; } = [];

    public ObservableCollection<ObjectDetailRow> Tags { get; } = [];

    /// <summary>The path, and for the title bar a version shortened from the left.</summary>
    public string Path => Address.RelativePath;

    /// <summary>The last part of the path, which is what the dialogs call the item.</summary>
    public string ItemName => Address.RelativePath[(Address.RelativePath.LastIndexOf('/') + 1)..];

    public string Title => Ui.Format(Ui.Inspector.PropertiesTitleFormat, ShortenForTitle(Address.RelativePath));

    public string Identity => Ui.Format(Ui.Inspector.ConnectionIdentityFormat, Address.ConnectionId);

    /// <summary>Whether the item is a file, which is all versions, metadata and tags describe.</summary>
    public bool IsFile => _controller.IsFile;

    public static string GeneralTabLabel => Ui.Inspector.TabGeneral;

    public string VersionsTabLabel => Ui.Format(Ui.Inspector.TabVersionsFormat, Versions.Count);

    public string MetadataTabLabel => Ui.Format(Ui.Inspector.TabMetadataFormat, Metadata.Count);

    public string TagsTabLabel => Ui.Format(Ui.Inspector.TabTagsFormat, Tags.Count);

    public StatusLine GeneralNotice
    {
        get => _generalNotice;
        private set => Set(ref _generalNotice, value);
    }

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

    /// <summary>Where the checksum came from, or why there is none.</summary>
    public StatusLine ChecksumNotice
    {
        get => _checksumNotice;
        private set => Set(ref _checksumNotice, value);
    }

    /// <summary>The SHA-256 in lowercase hex, empty until one has been fetched.</summary>
    public string Checksum
    {
        get => _checksum;
        private set
        {
            if (Set(ref _checksum, value)) Raise(nameof(HasChecksum));
        }
    }

    public bool HasChecksum => Checksum.Length != 0;

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

    /// <summary>Whether the connection advertises changing permissions, and there is a dialog to ask with.</summary>
    public bool CanChangePermissions => _attributes?.CanSetPermissions == true && _askPermissions is not null;

    /// <summary>Whether the connection advertises setting the modified time, and there is a prompt to ask with.</summary>
    public bool CanSetModifiedTime => _attributes?.CanSetModifiedTime == true && _dialogs is not null;

    /// <summary>
    /// What this connection cannot change, in a sentence under the dimmed buttons; empty when it
    /// can change both, or before the attributes are known.
    /// </summary>
    public string ChangesNotice => _attributes switch
    {
        null => string.Empty,
        { CanSetPermissions: false, CanSetModifiedTime: false } => Ui.Inspector.CannotChangeEither,
        { CanSetPermissions: false } => Ui.Inspector.CannotChangePermissions,
        { CanSetModifiedTime: false } => Ui.Inspector.CannotSetModifiedTime,
        _ => string.Empty
    };

    public bool HasChangesNotice => ChangesNotice.Length > 0;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            RaiseCommands();
        }
    }

    /// <summary>Which section is showing: general, versions, metadata, tags or checksum, in that order.</summary>
    public int SelectedTab
    {
        get => _selectedTab;
        set => Set(ref _selectedTab, value);
    }

    public ICommand RefreshCommand { get; }

    public ICommand LoadMoreCommand { get; }

    public ICommand ChangePermissionsCommand { get; }

    public ICommand SetModifiedTimeCommand { get; }

    public ICommand CloseCommand { get; }

    public ICommand GetChecksumCommand { get; }

    public ICommand ComputeChecksumCommand { get; }

    /// <summary>Raised when the window should close.</summary>
    internal event EventHandler? Closed;

    /// <summary>
    /// Raised after a change was made, so the pane the item is listed in can show it too.
    /// </summary>
    internal event EventHandler? AttributesChanged;

    /// <summary>Asks the agent for every section that describes this item.</summary>
    internal async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Status = StatusLine.Muted(IsFile
                ? Ui.Inspector.LoadingVersionHistoryMetadataAndTags
                : Ui.Inspector.LoadingProperties);
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

    /// <summary>
    /// Asks for the file's SHA-256: the server's own first when <paramref name="preferServer"/>,
    /// otherwise read and computed, and says which it is.
    /// </summary>
    internal async Task GetChecksumAsync(bool preferServer, CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Checksum = string.Empty;
            ChecksumNotice = StatusLine.Muted(Ui.Inspector.GettingChecksum);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            var response = await _controller.GetChecksumAsync(preferServer, linked.Token).ConfigureAwait(true);
            if (response.Failure is { } failure)
            {
                ChecksumNotice = new StatusLine(failure.Message, MetricTone.Warning);
                return;
            }

            Checksum = response.Value ?? string.Empty;
            ChecksumNotice = response.Source == ObjectChecksumSource.Server
                ? StatusLine.Muted(Ui.Format(Ui.Inspector.ChecksumFromServerFormat, response.BytesCovered))
                : new StatusLine(Ui.Format(Ui.Inspector.ChecksumComputedFormat, response.BytesCovered), MetricTone.Success);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error) when (IsExpected(error) || error is NotSupportedException)
        {
            ChecksumNotice = new StatusLine(
                error is TimeoutException
                    ? Ui.Inspector.TheObjectInspectorRequestTimedOut
                    : Ui.Inspector.TheObjectInspectorCouldNotLoadDetails,
                MetricTone.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Asks for a new mode and sends it, or says why this connection cannot.
    /// </summary>
    /// <remarks>
    /// The dialog starts from the mode the server reported; where it reported none (an FTP server
    /// whose listing has no permission column), from the usual 644 for a file and 755 otherwise,
    /// which is a starting point to edit, not a claim about the item.
    /// </remarks>
    internal async Task ChangePermissionsAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        if (!CanChangePermissions)
        {
            Status = new StatusLine(Ui.Inspector.CannotChangePermissions, MetricTone.Warning);
            return;
        }

        var current = _attributes!.UnixMode ?? (IsFile ? 0x1A4 : 0x1ED);
        var mode = await _askPermissions!(ItemName, current).ConfigureAwait(true);
        if (mode is not { } chosen) return;

        await ChangeAsync(
            Ui.Inspector.ChangingPermissions,
            token => _controller.SetPermissionsAsync(chosen, token),
            Ui.Format(Ui.Inspector.PermissionsChangedFormat, UnixPermissionText.Describe(chosen)),
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Asks for a new modified time and sends it, or says why this connection cannot.
    /// </summary>
    /// <remarks>
    /// Typed in this computer's time zone, the one every other time in the window is shown in, and
    /// sent in UTC. The prompt starts from the item's time now, so a nudge of a minute is an edit
    /// of two digits.
    /// </remarks>
    internal async Task SetModifiedTimeAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        if (!CanSetModifiedTime)
        {
            Status = new StatusLine(Ui.Inspector.CannotSetModifiedTime, MetricTone.Warning);
            return;
        }

        var example = FormatLocal(_attributes!.ModifiedUtc ?? DateTimeOffset.UtcNow);
        var answer = await _dialogs!.PromptAsync(new DialogPromptRequest
        {
            Title = Ui.Inspector.SetModifiedTimeTitle,
            Label = Ui.Format(Ui.Inspector.SetModifiedTimeLabelFormat, ItemName, OffsetText(), example),
            Value = example,
            Accept = Ui.Inspector.Set,
            Validate = text => TryParseModifiedTime(text, _timeZone, out _, out var problem) ? null : problem
        }, cancellationToken).ConfigureAwait(true);
        if (answer is null || !TryParseModifiedTime(answer, _timeZone, out var modifiedUtc, out _)) return;

        await ChangeAsync(
            Ui.Inspector.SettingModifiedTime,
            token => _controller.SetModifiedTimeAsync(modifiedUtc, token),
            Ui.Format(Ui.Inspector.ModifiedTimeSetFormat, FormatLocal(modifiedUtc)),
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// A typed time, read in the given zone and returned in UTC, or the sentence for why not.
    /// </summary>
    /// <remarks>
    /// Seconds may be left off. A time that does not exist in the zone (the hour skipped when
    /// clocks go forward) is not understood rather than moved, and the range is the contract's,
    /// which is what the narrowest provider can store.
    /// </remarks>
    internal static bool TryParseModifiedTime(
        string? text,
        TimeZoneInfo zone,
        out DateTimeOffset modifiedUtc,
        out string? problem)
    {
        ArgumentNullException.ThrowIfNull(zone);
        modifiedUtc = default;
        var example = DateTime.UtcNow.ToString(TimeFormat, CultureInfo.InvariantCulture);
        if (!DateTime.TryParseExact(
                text?.Trim(),
                [TimeFormat, "yyyy-MM-dd HH:mm"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var local) ||
            zone.IsInvalidTime(local))
        {
            problem = Ui.Format(Ui.Inspector.ModifiedTimeNotUnderstoodFormat, example);
            return false;
        }

        var utc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone), TimeSpan.Zero);
        if (!ItemAttributeIpcLimits.IsSettableModifiedTime(utc))
        {
            problem = Ui.Inspector.ModifiedTimeOutOfRange;
            return false;
        }

        modifiedUtc = utc;
        problem = null;
        return true;
    }

    private async Task ChangeAsync(
        string working,
        Func<CancellationToken, Task<StorageIpcFailure?>> change,
        string done,
        CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            Status = StatusLine.Muted(working);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            var failure = await change(linked.Token).ConfigureAwait(true);
            if (failure is not null)
            {
                Status = new StatusLine(failure.Message, MetricTone.Warning);
                return;
            }

            Show(_controller.State);
            Status = new StatusLine(done, MetricTone.Success);
            AttributesChanged?.Invoke(this, EventArgs.Empty);
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
        _attributes = state.Attributes;
        General.Clear();
        if (state.Attributes is { } attributes)
        {
            foreach (var row in GeneralRows(attributes)) General.Add(row);
        }

        GeneralNotice = state.AttributesFailure is { } attributesFailure
            ? new StatusLine(attributesFailure.Message, MetricTone.Warning)
            : StatusLine.Muted(state.Attributes is null
                ? Ui.Inspector.GeneralHasNotBeenLoaded
                : Ui.Format(Ui.Inspector.TimesAreLocalFormat, OffsetText()));

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
        Raise(nameof(CanChangePermissions));
        Raise(nameof(CanSetModifiedTime));
        Raise(nameof(ChangesNotice));
        Raise(nameof(HasChangesNotice));
        RaiseCommands();

        // Before the first load the controller's state is empty and not a failure, and the status
        // keeps saying the inspector will connect when shown.
        if (IsUntouched(state)) return;

        if (!IsFile)
        {
            Status = state.AttributesFailure is { } failure
                ? new StatusLine(failure.Message, MetricTone.Warning)
                : new StatusLine(Ui.Inspector.PropertiesLoaded, MetricTone.Success);
            return;
        }

        var failures = new[] { state.AttributesFailure, state.VersionsFailure, state.MetadataFailure, state.TagsFailure }
            .Count(static failure => failure is not null);
        Status = failures == 0
            ? new StatusLine(
                Ui.Format(Ui.Inspector.LoadedSummaryFormat, Versions.Count, Metadata.Count, Tags.Count),
                MetricTone.Success)
            : new StatusLine(Ui.Format(Ui.Inspector.LoadedWithFailuresFormat, failures), MetricTone.Warning);
    }

    /// <summary>
    /// The General tab's rows, in the order a file manager's properties put them.
    /// </summary>
    /// <remarks>
    /// Size is a file's only, and the link target a link's; every other row is shown for every
    /// item, saying not reported where the connection had nothing, so the same window on two
    /// connections shows what each one does not know rather than hiding it.
    /// </remarks>
    private IEnumerable<ObjectDetailRow> GeneralRows(ItemAttributes attributes)
    {
        yield return new ObjectDetailRow(Ui.Inspector.Kind, attributes.Kind switch
        {
            StorageItemKind.File => Ui.Inspector.KindFile,
            StorageItemKind.Directory or StorageItemKind.Prefix => Ui.Inspector.KindFolder,
            StorageItemKind.SymbolicLink => Ui.Inspector.KindLink,
            _ => Ui.Inspector.KindOther
        });

        if (attributes.Kind == StorageItemKind.File)
        {
            yield return new ObjectDetailRow(
                Ui.Inspector.Size,
                attributes.Size?.ToString("N0", CultureInfo.CurrentCulture) ?? Ui.Inspector.NotReported);
        }

        if (attributes.Kind == StorageItemKind.SymbolicLink)
        {
            yield return new ObjectDetailRow(Ui.Inspector.LinkTarget, attributes.LinkTarget ?? Ui.Inspector.NotReported);
        }

        yield return new ObjectDetailRow(Ui.Inspector.Modified, Time(attributes.ModifiedUtc));
        yield return new ObjectDetailRow(Ui.Inspector.Created, Time(attributes.CreatedUtc));
        yield return new ObjectDetailRow(Ui.Inspector.Accessed, Time(attributes.AccessedUtc));
        yield return new ObjectDetailRow(
            Ui.Inspector.Permissions,
            attributes.UnixMode is { } mode ? UnixPermissionText.Describe(mode) : Ui.Inspector.NotReported);
        yield return new ObjectDetailRow(Ui.Inspector.Owner, Principal(attributes.Owner, attributes.OwnerId));
        yield return new ObjectDetailRow(Ui.Inspector.Group, Principal(attributes.Group, attributes.GroupId));
    }

    /// <summary>A name, a numeric ID, both, or not reported: FTP gives names and SFTP gives IDs.</summary>
    private static string Principal(string? name, long? id) => (name, id) switch
    {
        ({ } both, { } number) => Ui.Format(Ui.Inspector.PrincipalWithIdFormat, both, number),
        ({ } onlyName, null) => onlyName,
        (null, { } onlyId) => onlyId.ToString(CultureInfo.InvariantCulture),
        _ => Ui.Inspector.NotReported
    };

    private string Time(DateTimeOffset? utc) => utc is { } value ? FormatLocal(value) : Ui.Inspector.NotReported;

    private string FormatLocal(DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTime(utc, _timeZone).ToString(TimeFormat, CultureInfo.InvariantCulture);

    /// <summary>This zone's offset from UTC now, as UTC+02:00, for the sentences that name the zone.</summary>
    private string OffsetText()
    {
        var offset = _timeZone.GetUtcOffset(DateTimeOffset.UtcNow);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        return string.Create(CultureInfo.InvariantCulture, $"UTC{sign}{offset.Duration():hh\\:mm}");
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
        state.Attributes is null && state.AttributesFailure is null &&
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

    public static string SafetyNotice => Ui.Inspector.WhatCanBeChangedHere;

    public static string RefreshLabel => Ui.Inspector.Refresh;

    public static string LoadMoreLabel => Ui.Inspector.LoadMoreVersions2;

    public static string ChangePermissionsLabel => Ui.Inspector.ChangePermissions;

    public static string SetModifiedTimeLabel => Ui.Inspector.SetModifiedTime;

    public static string CloseLabel => Ui.Inspector.Close;

    public static string ChecksumTabLabel => Ui.Inspector.TabChecksum;

    public static string ChecksumAlgorithmLabel => Ui.Inspector.ChecksumAlgorithm;

    public static string GetChecksumLabel => Ui.Inspector.GetChecksum;

    public static string ComputeChecksumLabel => Ui.Inspector.ComputeChecksum;

    public static string ComputeChecksumHint => Ui.Inspector.ComputeChecksumHint;

    public static string ChecksumAccessibleName => Ui.Inspector.ObjectChecksum;

    public static string ColumnCurrent => Ui.Inspector.Current;

    public static string ColumnVersion => Ui.Inspector.VersionID;

    public static string ColumnSize => Ui.Inspector.Size;

    public static string ColumnModified => Ui.Inspector.ModifiedUTC;

    public static string ColumnDeleteMarker => Ui.Inspector.DeleteMarker;

    public static string ColumnEntityTag => Ui.Inspector.EntityTag;

    public static string ColumnName => Ui.Inspector.Name;

    public static string ColumnValue => Ui.Inspector.Value;

    public static string PathAccessibleName => Ui.Inspector.InspectedObjectPath;

    public static string SafetyAccessibleName => Ui.Inspector.WhatCanBeChangedNotice;

    public static string TabsAccessibleName => Ui.Inspector.ObjectDetailCategories;

    public static string GeneralAccessibleName => Ui.Inspector.ItemProperties;

    public static string VersionsAccessibleName => Ui.Inspector.ObjectVersions;

    public static string MetadataAccessibleName => Ui.Inspector.ObjectMetadata;

    public static string TagsAccessibleName => Ui.Inspector.ObjectTags;

    public static string StatusAccessibleName => Ui.Inspector.ObjectInspectorStatus;

    public static string LoadMoreAccessibleName => Ui.Inspector.LoadTheNextObjectVersionPage;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RaiseCommands()
    {
        (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (LoadMoreCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ChangePermissionsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SetModifiedTimeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (GetChecksumCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ComputeChecksumCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
