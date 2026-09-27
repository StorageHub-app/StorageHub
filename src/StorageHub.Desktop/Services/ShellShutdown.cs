using Avalonia.Controls;
using Avalonia.Input;
using StorageHub.Desktop.Framework;

namespace StorageHub.Desktop.Services;

/// <summary>
/// Holds the shell's window open on its way out until what it asked and what it held are done
/// with, then closes it for real without asking again.
/// </summary>
/// <remarks>
/// <para>
/// 1.x's MainForm asked about each changed workspace in FormClosing, stopped the agent there, and
/// disposed the rest synchronously as the form went, so all of it finished before the process did.
/// Avalonia does not wait for an async Closing handler: written as one, the cleanup was cut off at
/// its first await. So the first close is cancelled, the question and the cleanup run to the end,
/// and the window is closed again.
/// </para>
/// <para>
/// A close that arrives while that runs is cancelled too, and starts nothing. Exit can be chosen
/// again, and a window manager can send a second close, and neither may ask twice, dispose anything
/// twice, or end the process underneath the first. Once the question is answered the window takes
/// no more input, as 1.x's frozen form did, since everything behind it is being let go.
/// </para>
/// <para>
/// Windows signing out or shutting down is the one close not held when nothing is unsaved: 1.x let
/// the session end there and then, and the ending session takes the connections and the agent with
/// it. Held, it would have Windows name StorageHub as what keeps it from signing out.
/// </para>
/// </remarks>
internal sealed class ShellShutdown
{
    /// <summary>
    /// How long what the shell holds may take to let go. The agent is stopped after it either way:
    /// a connection that will not close must not keep running an agent that was promised to stop
    /// with the window.
    /// </summary>
    private static readonly TimeSpan ReleaseLimit = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long the agent may take to stop: a little over the lifecycle's own eight seconds, so its
    /// answer is heard, but a pipe that never answers cannot hold the window open.
    /// </summary>
    private static readonly TimeSpan AgentLimit = TimeSpan.FromSeconds(10);

    private readonly Window _window;
    private readonly Func<Task<bool>> _confirm;
    private readonly IEnumerable<Func<Task>> _held;
    private readonly Func<Task>? _stopAgent;
    private readonly Action<Exception> _log;
    private Stage _stage;

    private ShellShutdown(
        Window window,
        Func<Task<bool>> confirm,
        IEnumerable<Func<Task>> held,
        Func<Task>? stopAgent,
        Action<Exception>? log)
    {
        _window = window;
        _confirm = confirm;
        _held = held;
        _stopAgent = stopAgent;
        _log = log ?? (static error => DesktopErrorLog.Write("shutdown", error));
    }

    private enum Stage
    {
        Open,
        Asking,
        Closing,
        Closed
    }

    /// <summary>Makes every close of <paramref name="window"/> go through the question and the cleanup.</summary>
    /// <param name="confirm">Asks about unsaved work; false keeps the shell open.</param>
    /// <param name="held">
    /// What to let go, all started together. Read as the window closes rather than now, so a
    /// workspace opened after this was attached is among them.
    /// </param>
    /// <param name="stopAgent">Stops the agent, last, once everything that talks to it has gone.</param>
    /// <param name="log">Where a part that failed to close is written; the error log when null.</param>
    internal static void Attach(
        Window window,
        Func<Task<bool>> confirm,
        IEnumerable<Func<Task>> held,
        Func<Task>? stopAgent = null,
        Action<Exception>? log = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(confirm);
        ArgumentNullException.ThrowIfNull(held);
        window.Closing += new ShellShutdown(window, confirm, held, stopAgent, log).OnClosing;
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_stage == Stage.Closed) return;

        // Held at every stage before that: the first close waits for the answer and the cleanup,
        // and a later one must not end the process underneath them.
        e.Cancel = true;
        if (_stage != Stage.Open) return;

        _stage = Stage.Asking;
        var closing = false;
        try
        {
            var asking = _confirm();
            if (e.CloseReason == WindowCloseReason.OSShutdown &&
                asking is { IsCompletedSuccessfully: true, Result: true })
            {
                // Nothing was unsaved, so there was nothing to ask, and the session is ending.
                e.Cancel = false;
                _stage = Stage.Closed;
                return;
            }

            closing = await asking.ConfigureAwait(true);
        }
        finally
        {
            // Cancel leaves the shell exactly as it was, and so does a question that failed, which
            // the unhandled-error handler reports; either way the next close asks again.
            if (_stage == Stage.Asking) _stage = closing ? Stage.Closing : Stage.Open;
        }

        if (!closing) return;

        // Nothing may be saved, opened or shown against workspaces already being let go, and a
        // dialog opened now could take the final close with it as its owner went.
        _window.IsEnabled = false;
        _window.Cursor = new Cursor(StandardCursorType.Wait);

        await AttemptAsync(ReleaseAllAsync, ReleaseLimit).ConfigureAwait(true);
        if (_stopAgent is { } stopAgent) await AttemptAsync(stopAgent, AgentLimit).ConfigureAwait(true);

        _stage = Stage.Closed;
        _window.Close();
    }

    /// <summary>
    /// Lets everything go at once, each on its own, so one that fails or hangs does not keep the
    /// rest: the workspaces hold sessions and shells in an agent that may outlive the window.
    /// </summary>
    private Task ReleaseAllAsync() =>
        Task.WhenAll(_held.ToArray().Select(release => AttemptAsync(release)));

    private async Task AttemptAsync(Func<Task> step, TimeSpan? limit = null)
    {
        try
        {
            var running = step();
            await (limit is { } bound ? running.WaitAsync(bound) : running).ConfigureAwait(true);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Nothing on the way out may keep the window from closing. The log is where it can be
            // looked into; there is no shell left to say it in.
            _log(error);
        }
    }
}
