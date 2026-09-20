using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// Creating, renaming and deleting on this computer.
/// </summary>
/// <remarks>
/// A seam so <see cref="PaneMutationController"/> can be driven without touching a disk, and so the
/// rules below are stated once rather than at each call site. Every method throws on failure; the
/// controller turns that into a sentence, which is the one place that translation happens.
/// </remarks>
public interface ILocalMutations
{
    /// <summary>Makes an empty folder or file directly inside <paramref name="parent"/>.</summary>
    void Create(string parent, string name, bool container);

    /// <summary>Renames <paramref name="path"/> to a new name in the folder it is already in.</summary>
    void Rename(string path, string newName, bool container);

    /// <summary>Removes <paramref name="path"/>, recursively when it is a folder.</summary>
    void Delete(string path, bool container);
}

/// <summary>The real filesystem.</summary>
internal sealed class LocalMutations : ILocalMutations
{
    public void Create(string parent, string name, bool container)
    {
        var target = Child(parent, name);
        if (Directory.Exists(target) || File.Exists(target))
        {
            throw new IOException(Ui.Shell.NameAlreadyExists);
        }

        if (container)
        {
            Directory.CreateDirectory(target);
            return;
        }

        // CreateNew rather than Create: two panes on the same folder should not have one of them
        // silently empty a file the other just made.
        using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
    }

    /// <summary>
    /// Renames in place, including when only the letter case changed.
    /// </summary>
    /// <remarks>
    /// A case-only rename goes through a temporary name, because Windows and macOS compare paths
    /// without case and would see the move as a no-op onto an existing file -- so "readme.txt" to
    /// "README.txt" either does nothing or is refused. The temporary is moved back if the second
    /// step fails, so a failure leaves the file where it started rather than under a generated
    /// name nobody would recognise.
    /// </remarks>
    public void Rename(string path, string newName, bool container)
    {
        var source = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(source)
            ?? throw new IOException(Ui.Shell.ParentFolderUnavailable);
        var destination = Child(parent, newName);

        var caseOnly =
            string.Equals(source, destination, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(source, destination, StringComparison.Ordinal);

        if (!caseOnly && (File.Exists(destination) || Directory.Exists(destination)))
        {
            throw new IOException(Ui.Shell.NameAlreadyExists);
        }

        if (!caseOnly)
        {
            Move(source, destination, container);
            return;
        }

        var temporary = Child(parent, $".storagehub-rename-{Guid.NewGuid():N}.tmp");
        Move(source, temporary, container);
        try
        {
            Move(temporary, destination, container);
        }
        catch
        {
            Move(temporary, source, container);
            throw;
        }
    }

    public void Delete(string path, bool container)
    {
        var target = Path.GetFullPath(path);
        if (container) Directory.Delete(target, recursive: true);
        else File.Delete(target);
    }

    private static void Move(string source, string destination, bool container)
    {
        if (container) Directory.Move(source, destination);
        else File.Move(source, destination, overwrite: false);
    }

    /// <summary>
    /// A path directly inside a folder, and nowhere else.
    /// </summary>
    /// <remarks>
    /// The check is the point: "../elsewhere" combines into a perfectly valid path outside the
    /// folder somebody is looking at, and a rename is not a way to move something.
    /// </remarks>
    private static string Child(string parent, string name)
    {
        var root = Path.GetFullPath(parent);
        var target = Path.GetFullPath(Path.Combine(root, name));
        var actual = Path.GetDirectoryName(target) ?? string.Empty;

        return Path.TrimEndingDirectorySeparator(actual)
            .Equals(Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase)
            ? target
            : throw new ArgumentException(Ui.Shell.NameMustBeDirectChild);
    }
}
