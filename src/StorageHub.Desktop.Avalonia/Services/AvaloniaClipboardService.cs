using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using StorageHub.Desktop.Shell;

namespace StorageHub.Desktop.Services;

/// <summary>
/// The clipboard belonging to a window.
/// </summary>
/// <remarks>
/// Scoped to a <see cref="TopLevel"/> because that is what Avalonia exposes, and because on X11 it
/// is literally true: the clipboard is a selection owned by a window, and a copy is a promise that
/// window makes to serve the text when somebody asks for it.
///
/// With no window there is no clipboard. A copy is then dropped and a paste answers null, which is
/// what happens during shutdown and in a headless test -- both cases where throwing would turn a
/// convenience into a crash.
/// </remarks>
internal sealed class AvaloniaClipboardService(Func<TopLevel?> topLevel) : IClipboardService
{
    private readonly Func<TopLevel?> _topLevel = topLevel ?? throw new ArgumentNullException(nameof(topLevel));

    public async Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (_topLevel()?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text).ConfigureAwait(true);
            }
        }).ConfigureAwait(false);
    }

    public async Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var clipboard = _topLevel()?.Clipboard;
            // TryGetTextAsync, not GetTextAsync: a clipboard holding an image or a file list has
            // no text to give, and that is an ordinary answer rather than a failure.
            return clipboard is null ? null : await clipboard.TryGetTextAsync().ConfigureAwait(true);
        }).ConfigureAwait(false);
    }
}
