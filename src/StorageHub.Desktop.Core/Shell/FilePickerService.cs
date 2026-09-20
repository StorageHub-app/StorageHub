namespace StorageHub.Desktop.Shell;

/// <summary>One entry in a picker's type filter.</summary>
/// <param name="Name">What the person reads, e.g. "StorageHub workspace".</param>
/// <param name="Extensions">
/// Extensions without a dot or a wildcard, e.g. <c>["shw"]</c>. The shell wrote Windows filter
/// strings -- <c>"Workspaces (*.shw)|*.shw"</c> -- which no other platform parses, and which a
/// missing pipe turns into a dialog that silently shows nothing.
/// </param>
internal sealed record FilePickerFilter(string Name, IReadOnlyList<string> Extensions);

/// <summary>What to ask for.</summary>
internal sealed record FilePickerRequest
{
    public required string Title { get; init; }

    public IReadOnlyList<FilePickerFilter> Filters { get; init; } = [];

    /// <summary>The name to start with when saving.</summary>
    public string? SuggestedFileName { get; init; }

    /// <summary>Where to open. Ignored when it does not exist, rather than refused.</summary>
    public string? SuggestedDirectory { get; init; }
}

/// <summary>
/// Asks the operating system for a file or a folder.
/// </summary>
/// <remarks>
/// Every implementation is asynchronous because Avalonia's is: on X11 and Wayland the picker is a
/// portal call to a separate process, and there is no way to block on it without blocking the
/// compositor's reply. The Windows shell's eight call sites were all synchronous
/// <c>ShowDialog</c>, so this is one of the few places where the port changes a signature rather
/// than a type.
///
/// Every path returned is absolute. A picker that returns null was cancelled.
/// </remarks>
internal interface IFilePickerService
{
    Task<string?> PickFileAsync(FilePickerRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> PickFilesAsync(FilePickerRequest request, CancellationToken cancellationToken = default);

    Task<string?> PickFolderAsync(FilePickerRequest request, CancellationToken cancellationToken = default);

    Task<string?> SaveFileAsync(FilePickerRequest request, CancellationToken cancellationToken = default);
}
