using System.Collections.ObjectModel;
using System.ComponentModel;
using Lucide.Avalonia;

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
        _rootKey = null;
        Roots.Clear();
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
        }
        finally
        {
            _updating = false;
        }
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
