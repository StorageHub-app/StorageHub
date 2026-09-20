using Avalonia.Controls;
using Avalonia.Threading;
using StorageHub.Desktop.Shell;
using StorageHub.Desktop.Views;

namespace StorageHub.Desktop.Services;

/// <summary>
/// Shows <see cref="DialogWindow"/> over a window.
/// </summary>
/// <remarks>
/// <para>
/// Takes a <see cref="Func{TResult}"/> rather than a <see cref="Window"/> because a view model is
/// built before the window it belongs to is shown, and a modal needs the owner that exists at the
/// moment it opens -- not the one that existed at construction.
/// </para>
/// <para>
/// With no owner the dialog opens on its own, which is what happens during startup, before the
/// shell window exists. That is worth allowing rather than throwing: the messages that arrive then
/// are the ones explaining why the shell is not going to open.
/// </para>
/// </remarks>
internal sealed class AvaloniaDialogService(Func<Window?> owner) : IDialogService
{
    private readonly Func<Window?> _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public Task ShowAsync(DialogRequest request, CancellationToken cancellationToken = default) =>
        ConfirmAsync(request, cancellationToken);

    public async Task<DialogChoice> ConfirmAsync(
        DialogRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // Callers are view models, which run wherever their awaits resumed them. A window may only
        // be built on the UI thread, and the failure otherwise is a throw from deep inside layout.
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var window = DialogWindow.For(request);
            if (_owner() is { } parent)
            {
                await window.ShowDialog(parent).ConfigureAwait(true);
                return window.Result;
            }

            // Modal to nothing, so there is no ShowDialog to await. A window cannot own itself.
            var closed = new TaskCompletionSource();
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show();
            await closed.Task.ConfigureAwait(true);
            return window.Result;
        }).ConfigureAwait(false);
    }
}
