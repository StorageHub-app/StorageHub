using System.Collections.ObjectModel;
using System.ComponentModel;
using Lucide.Avalonia;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>One folder in a pane's tree.</summary>
/// <param name="Target">
/// Where the pane goes when this is chosen: a full path on this computer, a path relative to the
/// connection's root, or empty for the root itself (This PC, or the connection's top level).
/// </param>
internal sealed class PaneTreeNode(string name, string target, LucideIconKind icon) : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;

    public string Name { get; set; } = name;

    /// <summary>When this folder's own folders were last read for the tree; null if they never were.</summary>
    internal DateTimeOffset? ScannedUtc { get; set; }

    /// <summary>When a read of this folder last failed; it is not tried again for a little while.</summary>
    internal DateTimeOffset? ScanFailedUtc { get; set; }

    /// <summary>Told when the node is opened, so the tree can read what is inside it.</summary>
    internal Action<PaneTreeNode>? Expanded { get; set; }

    public string Target { get; } = target;

    public LucideIconKind Icon { get; } = icon;

    /// <summary>This PC or the connection itself, drawn in the text colour rather than as a folder.</summary>
    public bool IsRoot => Target.Length == 0;

    public ObservableCollection<PaneTreeNode> Children { get; } = [];

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            if (value) Expanded?.Invoke(this);
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// The folder tree beside a pane's list, as 1.x drew it.
/// </summary>
/// <remarks>
/// <para>
/// Built from what the pane has listed rather than by listing on its own: the path to the current
/// folder is always there and open, the current folder's children are the folders in its listing,
/// and a folder passed through on the way stays in the tree. That is 1.x's rule, and it means the
/// tree never costs a request the list did not already make.
/// </para>
/// <para>
/// Choosing a node sends the pane there; the pane's next listing then selects it here. The flag
/// that tells the two apart is what keeps a selection the tree made itself from sending the pane
/// somewhere a second time.
/// </para>
/// </remarks>
internal sealed class PaneTreeModel
{
    private const int MaximumChildren = 500;
    private string? _rootKey;
    private bool _updating;
    private bool _local;
    private CancellationTokenSource? _scan;

    /// <summary>How far and how hard a scan goes, which is gentler on a server than on a disk.</summary>
    /// <param name="Depth">Levels below the folder the pane is in; the folders it lists are the first.</param>
    /// <param name="Requests">The most folders read for one scan.</param>
    /// <param name="Parallel">How many are read at once.</param>
    internal sealed record ScanLimits(int Depth, int Requests, int Parallel)
    {
        internal static ScanLimits Remote { get; } = new(2, 100, 2);

        internal static ScanLimits Local { get; } = new(3, 500, 4);
    }

    /// <summary>How long what a scan read is believed before an opened folder is read again.</summary>
    internal static readonly TimeSpan ScanFreshFor = TimeSpan.FromMinutes(5);

    /// <summary>How long a folder that could not be read is left alone before it is tried again.</summary>
    internal static readonly TimeSpan ScanRetryAfter = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Whether the tree reads folders ahead of being asked, which is the Scan subfolders setting.
    /// Null, as a preview has it, means it does not.
    /// </summary>
    internal Func<bool>? ScanEnabled { get; set; }

    /// <summary>Whether the pane lists hidden folders, which the tree follows.</summary>
    internal Func<bool>? ShowHidden { get; set; }

    /// <summary>Reads the folders inside one, without navigating. Null where the pane cannot.</summary>
    internal Func<string, int, CancellationToken, Task<IReadOnlyList<PaneFolder>?>>? ListFolders { get; set; }

    private bool Scanning => ScanEnabled?.Invoke() == true && ListFolders is not null;

    public ObservableCollection<PaneTreeNode> Roots { get; } = [];

    /// <summary>Raised when somebody chooses a node, with where the pane should go.</summary>
    internal event EventHandler<string>? NavigateRequested;

    /// <summary>Called by the view when the tree's selection changes.</summary>
    internal void Chosen(PaneTreeNode? node)
    {
        if (_updating || node is null) return;
        NavigateRequested?.Invoke(this, node.Target);
    }

    /// <summary>Empties the tree, for a pane with nothing to show or a terminal.</summary>
    internal void Clear()
    {
        CancelScan();
        _rootKey = null;
        Roots.Clear();
    }

    /// <summary>
    /// The one node Connections Home has, "Connections", with nothing under it, as 1.x drew it.
    /// </summary>
    internal void ShowConnections()
    {
        CancelScan();
        _rootKey = null;
        Roots.Clear();
        Roots.Add(new PaneTreeNode(Ui.Pane.Connections, string.Empty, LucideIconKind.Link));
    }

    /// <summary>
    /// Follows a listing: the path to <paramref name="current"/> made to exist and opened, its
    /// children replaced by the folders listed, and it selected.
    /// </summary>
    /// <param name="rootKey">Which root this is: a connection id, or This PC. A new one starts a new tree.</param>
    /// <param name="rootName">What the root node says.</param>
    /// <param name="local">Whether targets are paths on this computer or relative to a connection.</param>
    /// <param name="current">The current folder's target: full path, relative path, or empty for the root.</param>
    /// <param name="rows">The listing, of which only the folders are taken.</param>
    /// <param name="append">A further page of the same folder: add its folders, remove nothing.</param>
    internal void Follow(
        string rootKey,
        string rootName,
        bool local,
        string current,
        IEnumerable<BrowserListItem> rows,
        bool append = false)
    {
        CancelScan();
        _local = local;
        _updating = true;
        try
        {
            if (!string.Equals(_rootKey, rootKey, StringComparison.Ordinal) || Roots.Count == 0)
            {
                Roots.Clear();
                Roots.Add(new PaneTreeNode(rootName, string.Empty,
                    local ? LucideIconKind.Monitor : LucideIconKind.Server) { IsExpanded = true });
                _rootKey = rootKey;
            }

            var root = Roots[0];
            root.Name = rootName;
            var node = Ensure(root, current, local);

            // A listing is the whole truth about this folder, and it was read now.
            node.ScannedUtc = DateTimeOffset.UtcNow;

            var folders = rows
                .Where(static row => row.IsContainer && !row.IsParentNavigation && row.Location is not null)
                .Take(MaximumChildren)
                .Select(row => (row.Name, Target: row.Location!, Drive: local && node == root))
                .ToArray();
            var wanted = folders.Select(static folder => folder.Target).ToHashSet(StringComparer.Ordinal);

            if (!append)
            {
                // Children that are no longer there go; the ones that are keep their own subtrees,
                // so a folder visited below one of them is still there when it is opened again.
                for (var index = node.Children.Count - 1; index >= 0; index--)
                {
                    if (!wanted.Contains(node.Children[index].Target)) node.Children.RemoveAt(index);
                }
            }

            foreach (var folder in folders)
            {
                if (node.Children.Any(child => string.Equals(child.Target, folder.Target, StringComparison.Ordinal))) continue;
                Insert(node, new PaneTreeNode(folder.Name, folder.Target,
                    folder.Drive ? LucideIconKind.HardDrive : LucideIconKind.Folder));
            }

            node.IsExpanded = true;
            Select(node);
            Hook(Roots);
            if (Scanning) StartScan(node);
        }
        finally
        {
            _updating = false;
        }
    }

    // ------------------------------------------------------------------ the scan

    private void CancelScan()
    {
        var scan = Interlocked.Exchange(ref _scan, null);
        if (scan is null) return;
        scan.Cancel();
        scan.Dispose();
    }

    /// <summary>Starts a scan below the folder the pane is in, replacing one still running.</summary>
    private void StartScan(PaneTreeNode from)
    {
        var limits = _local ? ScanLimits.Local : ScanLimits.Remote;
        var scan = new CancellationTokenSource();
        _scan = scan;

        // This PC's children are the drives. The fixed ones are read, one level, so they show
        // whether they hold folders; a card reader, an optical drive or a network share is not
        // woken for a tree nobody asked to fill.
        if (_local && from.Target.Length == 0)
        {
            _ = ScanAsync([.. from.Children.Where(drive => NeedsReading(drive) && IsFixedDrive(drive.Target))], from, 1, limits, scan.Token);
            return;
        }

        _ = ScanAsync([.. from.Children.Where(NeedsReading)], from, limits.Depth - 1, limits, scan.Token);
    }

    /// <summary>
    /// Reads the folders inside <paramref name="first"/>, then inside what that finds, a level at a
    /// time and a few at once, as far as <paramref name="levels"/> and the limits allow. A folder
    /// shows an expander only once it is known to hold folders, so there is never one that opens
    /// onto nothing; the folders it has not read yet are read when its parent is opened.
    /// </summary>
    private async Task ScanAsync(
        IReadOnlyList<PaneTreeNode> first,
        PaneTreeNode from,
        int levels,
        ScanLimits limits,
        CancellationToken cancellationToken)
    {
        try
        {
            var list = ListFolders!;
            var requests = limits.Requests;
            var level = first.ToList();
            for (var depth = 0; depth < levels && level.Count > 0 && requests > 0; depth++)
            {
                var batch = level.Take(requests).ToList();
                requests -= batch.Count;
                using var gate = new SemaphoreSlim(limits.Parallel);
                var found = await Task.WhenAll(batch.Select(async node =>
                {
                    await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        return await list(node.Target, MaximumChildren, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        _ = gate.Release();
                    }
                })).ConfigureAwait(true);
                cancellationToken.ThrowIfCancellationRequested();

                // Applied here, one at a time, rather than as each answer arrives: the tree is read
                // by the window, which is not for several answers to change at once.
                var next = new List<PaneTreeNode>();
                _updating = true;
                try
                {
                    for (var index = 0; index < batch.Count; index++)
                    {
                        Apply(batch[index], found[index]);
                        next.AddRange(batch[index].Children.Where(NeedsReading));
                    }
                }
                finally
                {
                    _updating = false;
                }

                level = next;
            }

            _updating = true;
            try
            {
                Hook(Roots);
            }
            finally
            {
                _updating = false;
            }
        }
        catch (OperationCanceledException)
        {
            // Moved on, or closed. Whatever was read is applied or dropped as a whole.
        }
    }

    private static bool NeedsReading(PaneTreeNode node)
    {
        var now = DateTimeOffset.UtcNow;
        if (node.ScanFailedUtc is { } failed && now - failed < ScanRetryAfter) return false;
        return node.ScannedUtc is not { } read || now - read > ScanFreshFor;
    }

    private static bool IsFixedDrive(string target)
    {
        try
        {
            var drive = new DriveInfo(target);
            return drive.DriveType == DriveType.Fixed && drive.IsReady;
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Puts what was read inside a folder, in name order, leaving what is already there.</summary>
    private void Apply(PaneTreeNode node, IReadOnlyList<PaneFolder>? folders)
    {
        // A read that failed is not "nothing inside": it is tried again after a little, and until
        // then the folder simply shows what is known.
        if (folders is null)
        {
            node.ScanFailedUtc = DateTimeOffset.UtcNow;
            return;
        }

        node.ScanFailedUtc = null;
        node.ScannedUtc = DateTimeOffset.UtcNow;

        var showHidden = ShowHidden?.Invoke() ?? true;
        foreach (var folder in folders)
        {
            if (folder.IsHidden && !showHidden) continue;
            if (node.Children.Any(child => string.Equals(child.Target, folder.Target, StringComparison.Ordinal))) continue;
            Insert(node, new PaneTreeNode(folder.Name, folder.Target, LucideIconKind.Folder));
        }
    }

    private void Hook(IEnumerable<PaneTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            node.Expanded ??= OnExpanded;
            Hook(node.Children);
        }
    }

    /// <summary>
    /// A folder opened by somebody has its folders read, so that the ones now in view show an
    /// expander only if they hold folders. The tree opening folders itself, to show where the
    /// pane is, does not read anything.
    /// </summary>
    private void OnExpanded(PaneTreeNode node)
    {
        if (_updating || node.IsRoot || !Scanning || _scan is not { } scan) return;

        // Bounded like the first scan, and smaller: it is one folder's worth.
        var limits = (_local ? ScanLimits.Local : ScanLimits.Remote) with { Requests = 50 };
        if (NeedsReading(node))
        {
            // Not read yet: it, and then what it holds.
            _ = ScanAsync([node], node, 2, limits, scan.Token);
            return;
        }

        var children = node.Children.Where(NeedsReading).ToList();
        if (children.Count > 0) _ = ScanAsync(children, node, 1, limits, scan.Token);
    }

    /// <summary>The node for a folder, made with every folder above it if it is not there yet.</summary>
    private static PaneTreeNode Ensure(PaneTreeNode root, string target, bool local)
    {
        var node = root;
        foreach (var (name, path) in Ancestry(target, local))
        {
            var next = node.Children.FirstOrDefault(child => string.Equals(child.Target, path, StringComparison.Ordinal));
            if (next is null)
            {
                next = new PaneTreeNode(name, path,
                    local && node == root ? LucideIconKind.HardDrive : LucideIconKind.Folder);
                Insert(node, next);
            }

            next.IsExpanded = true;
            node = next;
        }

        return node;
    }

    /// <summary>Each folder from the root down to <paramref name="target"/>, with its name.</summary>
    private static IEnumerable<(string Name, string Path)> Ancestry(string target, bool local)
    {
        if (string.IsNullOrEmpty(target)) return [];

        if (!local)
        {
            var parts = target.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Select((part, index) => (part, string.Join('/', parts.Take(index + 1))));
        }

        var chain = new List<(string, string)>();
        for (var path = target; !string.IsNullOrEmpty(path); path = Path.GetDirectoryName(path))
        {
            var trimmed = Path.TrimEndingDirectorySeparator(path);
            var root = Path.GetPathRoot(path);
            var isRoot = string.Equals(root, path, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.TrimEndingDirectorySeparator(root ?? string.Empty), trimmed, StringComparison.OrdinalIgnoreCase);
            chain.Add(isRoot
                ? (trimmed.Length == 0 ? path : trimmed, root ?? path)
                : (Path.GetFileName(trimmed), trimmed));
            if (isRoot) break;
        }

        chain.Reverse();
        return chain;
    }

    /// <summary>In name order, as the list shows them, so the tree reads the same way.</summary>
    private static void Insert(PaneTreeNode parent, PaneTreeNode child)
    {
        var index = 0;
        while (index < parent.Children.Count &&
               string.Compare(parent.Children[index].Name, child.Name, StringComparison.OrdinalIgnoreCase) < 0)
        {
            index++;
        }

        parent.Children.Insert(index, child);
    }

    private void Select(PaneTreeNode node)
    {
        foreach (var other in Flatten(Roots)) other.IsSelected = ReferenceEquals(other, node);
    }

    private static IEnumerable<PaneTreeNode> Flatten(IEnumerable<PaneTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }
}
