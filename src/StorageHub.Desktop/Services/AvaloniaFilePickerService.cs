using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using StorageHub.Desktop.Shell;

namespace StorageHub.Desktop.Services;

/// <summary>
/// The platform's own file and folder pickers, through Avalonia's storage provider.
/// </summary>
/// <remarks>
/// <para>
/// On Windows this is the common item dialog. On Linux it is an XDG desktop portal call, which
/// runs in another process and hands back a document portal path -- which is why every method here
/// is asynchronous and why the result is read through <c>TryGetLocalPath</c> rather than assumed
/// to be a filename.
/// </para>
/// <para>
/// A file the portal cannot map to a real path is skipped rather than returned. That happens for a
/// sandboxed selection, and the shell has nothing that can open such a handle: every consumer of
/// these paths hands them to the agent, which is a separate process with no share of the grant.
/// </para>
/// </remarks>
internal sealed class AvaloniaFilePickerService(Func<TopLevel?> topLevel) : IFilePickerService
{
    private readonly Func<TopLevel?> _topLevel = topLevel ?? throw new ArgumentNullException(nameof(topLevel));

    public async Task<string?> PickFileAsync(
        FilePickerRequest request,
        CancellationToken cancellationToken = default)
    {
        var files = await PickFilesCoreAsync(request, allowMultiple: false, cancellationToken)
            .ConfigureAwait(false);
        return files.Count == 0 ? null : files[0];
    }

    public Task<IReadOnlyList<string>> PickFilesAsync(
        FilePickerRequest request,
        CancellationToken cancellationToken = default) =>
        PickFilesCoreAsync(request, allowMultiple: true, cancellationToken);

    public async Task<string?> PickFolderAsync(
        FilePickerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await OnUiThreadAsync(async provider =>
        {
            var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = request.Title,
                AllowMultiple = false,
                SuggestedStartLocation = await StartAsync(provider, request).ConfigureAwait(true)
            }).ConfigureAwait(true);

            return folders.Count == 0 ? null : LocalPath(folders[0]);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> SaveFileAsync(
        FilePickerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await OnUiThreadAsync(async provider =>
        {
            var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = request.Title,
                SuggestedFileName = request.SuggestedFileName,
                FileTypeChoices = TypesFor(request),
                // The first filter's first extension, so a name typed without one still saves as
                // the type the dialog said it would.
                DefaultExtension = DefaultExtensionFor(request),
                SuggestedStartLocation = await StartAsync(provider, request).ConfigureAwait(true)
            }).ConfigureAwait(true);

            return file is null ? null : LocalPath(file);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> PickFilesCoreAsync(
        FilePickerRequest request,
        bool allowMultiple,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var picked = await OnUiThreadAsync(async provider =>
        {
            var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = request.Title,
                AllowMultiple = allowMultiple,
                FileTypeFilter = TypesFor(request),
                SuggestedStartLocation = await StartAsync(provider, request).ConfigureAwait(true)
            }).ConfigureAwait(true);

            return (IReadOnlyList<string>)[.. files.Select(LocalPath).OfType<string>()];
        }, cancellationToken).ConfigureAwait(false);

        return picked ?? [];
    }

    private static string? LocalPath(IStorageItem item) => item.TryGetLocalPath();

    private static string? DefaultExtensionFor(FilePickerRequest request) =>
        request.Filters.Count > 0 && request.Filters[0].Extensions.Count > 0
            ? request.Filters[0].Extensions[0]
            : null;

    private static IReadOnlyList<FilePickerFileType>? TypesFor(FilePickerRequest request) =>
        request.Filters.Count == 0
            ? null
            : [.. request.Filters.Select(filter => new FilePickerFileType(filter.Name)
            {
                // Avalonia wants glob patterns; the contract takes bare extensions so a caller
                // cannot write a pattern that means something different on each platform.
                Patterns = [.. filter.Extensions.Select(extension => "*." + extension.TrimStart('.'))]
            })];

    /// <summary>
    /// Where to open, when the suggestion still exists.
    /// </summary>
    /// <remarks>
    /// Null is the right answer for a directory that has been deleted or is on a disconnected
    /// share: the picker then opens wherever the platform last left it, which is what a person
    /// expects, rather than failing or landing somewhere arbitrary.
    /// </remarks>
    private static async Task<IStorageFolder?> StartAsync(IStorageProvider provider, FilePickerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SuggestedDirectory) ||
            !Path.IsPathFullyQualified(request.SuggestedDirectory))
        {
            return null;
        }

        try
        {
            return await provider.TryGetFolderFromPathAsync(request.SuggestedDirectory).ConfigureAwait(true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Runs on the UI thread, and answers null when there is no window to hang a picker on.
    /// </summary>
    /// <remarks>
    /// That happens during shutdown and in a headless test. Returning null rather than throwing is
    /// the same answer as a cancelled picker, which every call site already handles.
    /// </remarks>
    private async Task<T?> OnUiThreadAsync<T>(
        Func<IStorageProvider, Task<T?>> pick,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var provider = _topLevel()?.StorageProvider;
            return provider is null ? default : await pick(provider).ConfigureAwait(true);
        }).ConfigureAwait(false);
    }
}
