using System.Text;
using Avalonia.Input;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// A session, as the control that draws it sees one.
/// </summary>
/// <remarks>
/// The painter knows which key was pressed and nothing about agents; the session knows how to reach
/// the agent and nothing about focus or fonts. This is the seam between them. It is an interface so
/// a view test can press keys at a recorder rather than at a pipe, and so the painter can be shown
/// a document with no session behind it at all.
/// </remarks>
internal interface ITerminalSession
{
    /// <summary>The screen to draw.</summary>
    VtTerminalDocument Document { get; }

    /// <summary>Whether the cursor block is drawn, which a remote program can turn off.</summary>
    bool CursorVisible { get; }

    /// <summary>Raised when the screen has changed and wants painting.</summary>
    event EventHandler? OutputReceived;

    /// <summary>Sends a key, and says whether it meant anything to a terminal.</summary>
    /// <remarks>
    /// False leaves the key for whoever else wants it -- a shortcut, a focus move -- rather than
    /// swallowing every keystroke that happens to reach a focused terminal.
    /// </remarks>
    bool SendKey(Key key, bool shift, bool alt, bool control);

    /// <summary>Sends typed text, which has already been through the keyboard layout.</summary>
    void SendText(string text);

    /// <summary>Sends pasted text, bracketed if the remote program asked for that.</summary>
    void Paste(string text);

    /// <summary>
    /// Tells the remote program how wide its window is now.
    /// </summary>
    /// <remarks>
    /// Not awaited. A resize arrives from a layout pass, which cannot wait for a pipe, and a
    /// resize that is already in flight is superseded rather than queued.
    /// </remarks>
    void Resize(int columns, int rows);
}

/// <summary>
/// One SSH session: opening it, reading its output, sending it input, and closing it.
/// </summary>
/// <remarks>
/// <para>
/// Lifted out of the WinForms form that used to hold it, where it was tangled with three timers, a
/// font and a status strip. None of that is protocol. What is protocol is here, with no window
/// behind it, so the parts that actually go wrong in a session -- a stale agent, a dropped read, a
/// gap in the output -- can be provoked in a test instead of reasoned about.
/// </para>
/// <para>
/// The read pump is a loop rather than a timer. The agent holds each read open until it has
/// something to say, so output reaches the screen when it is produced rather than at the next tick,
/// and an idle session stops costing a request every interval. The loop is started on the UI thread
/// and never leaves it: every await resumes on the caller's context, so the emulator is fed from
/// the same thread that paints it and needs no lock.
/// </para>
/// </remarks>
internal sealed class SshTerminalSession : ITerminalSession, IAsyncDisposable
{
    /// <summary>How long the agent may hold a read open before answering empty.</summary>
    private const int ReadWaitMilliseconds = 400;

    /// <summary>How many reads in a row may fail before the session is given up on.</summary>
    /// <remarks>
    /// One failed read is survivable: the acknowledgement does not advance, so the agent still
    /// holds those bytes and the next read asks for them again. Giving up on the first blip would
    /// throw away a session -- and whatever it was holding -- over a hiccup in a pipe.
    /// </remarks>
    private const int MaximumConsecutiveReadFailures = 5;

    private readonly Guid _connectionId;
    private readonly ISshTerminalAgentClient _client;
    private readonly bool _ownsClient;
    private readonly IAgentLifecycleController? _agentLifecycle;
    private readonly SshTerminalPreferences _preferences;
    private readonly VtTerminalEmulator _emulator;
    private readonly CancellationTokenSource _lifetime = new();

    /// <summary>
    /// Kept across reads, because a chunk boundary can fall inside a character.
    /// </summary>
    /// <remarks>
    /// The agent returns bytes and the emulator consumes characters. A UTF-8 sequence split across
    /// two reads decodes to a replacement character if each chunk is decoded alone, so every
    /// multi-byte character unlucky enough to straddle a boundary would arrive as a question mark.
    /// A stateful decoder holds the tail until the rest of it turns up.
    /// </remarks>
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();

    private Guid _sessionId;

    /// <summary>
    /// How far into the session's output this terminal has actually got.
    /// </summary>
    /// <remarks>
    /// Advanced only once bytes have reached the emulator, and sent as the acknowledgement on every
    /// read, so a read that is cancelled, times out or throws replays rather than losing output.
    /// </remarks>
    private long _consumedSequence;
    private int _consecutiveReadFailures;
    private bool _restartedAgentForVersionMismatch;
    private bool _opening;
    private Task _pump = Task.CompletedTask;
    private Task _closing = Task.CompletedTask;
    private string _status = Ui.Connections.TerminalConnecting;
    private bool _disposed;

    internal SshTerminalSession(
        Guid connectionId,
        ISshTerminalAgentClient client,
        SshTerminalPreferences? preferences = null,
        IAgentLifecycleController? agentLifecycle = null,
        bool ownsClient = false)
    {
        if (connectionId == Guid.Empty)
        {
            throw new ArgumentException("An SSH connection ID is required.", nameof(connectionId));
        }

        _connectionId = connectionId;
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _ownsClient = ownsClient;
        _agentLifecycle = agentLifecycle;
        _preferences = SshTerminalPreferences.Resolve(preferences);

        // A placeholder grid. The real size is measured from the control and sent with the open, so
        // the remote's idea of the window matches the window from its first prompt.
        _emulator = new VtTerminalEmulator(80, 24, _preferences.ScrollbackLines);
        _emulator.ResponseRequested += OnResponseRequested;
        _emulator.TitleChanged += OnTitleChanged;
    }

    /// <summary>The screen to draw.</summary>
    public VtTerminalDocument Document => _emulator.Document;

    public bool CursorVisible => _emulator.CursorVisible;

    /// <summary>Whether there is a session on the other end taking input.</summary>
    internal bool IsConnected => _sessionId != Guid.Empty;

    /// <summary>What to say about this session, in the pane's status line.</summary>
    internal string Status
    {
        get => _status;
        private set
        {
            if (string.Equals(_status, value, StringComparison.Ordinal)) return;
            _status = value;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal event EventHandler? StatusChanged;

    /// <summary>Raised when the screen has changed and wants painting.</summary>
    public event EventHandler? OutputReceived;

    /// <summary>
    /// Opens the session and starts reading from it.
    /// </summary>
    /// <remarks>
    /// A version mismatch is not negotiated. The desktop and the agent ship in one bundle, so a
    /// mismatch means a stale agent is still running from a previous build; the fix is to restart
    /// it and try once more. Without a lifecycle controller the mismatch is still reported, it just
    /// cannot be fixed without the user restarting StorageHub.
    /// </remarks>
    internal async Task OpenAsync(int columns, int rows, CancellationToken cancellationToken = default)
    {
        if (_disposed || _opening || IsConnected) return;
        _opening = true;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _lifetime.Token);
            await OpenOnceAsync(columns, rows, linked.Token).ConfigureAwait(true);
        }
        finally
        {
            _opening = false;
        }
    }

    private async Task OpenOnceAsync(int columns, int rows, CancellationToken cancellationToken)
    {
        (columns, rows) = ClampGrid(columns, rows);
        try
        {
            _emulator.Resize(columns, rows);
            var response = await _client.OpenAsync(
                new SshTerminalOpenRequest(
                    SshTerminalIpcContract.CurrentVersion,
                    _connectionId,
                    columns,
                    rows,
                    _preferences.TerminalName,
                    _preferences.StartupCommand,
                    _preferences.KeepAliveSeconds),
                cancellationToken).ConfigureAwait(true);

            if (response.Failure is { Code: "ssh.terminal.version.mismatch" } &&
                !_restartedAgentForVersionMismatch &&
                _agentLifecycle is not null)
            {
                _restartedAgentForVersionMismatch = true;
                Status = Ui.Connections.TerminalRestartingAgent;
                var restarted = await _agentLifecycle
                    .ExecuteAsync(AgentLifecycleAction.Restart, cancellationToken)
                    .ConfigureAwait(true);
                if (restarted.Succeeded)
                {
                    await OpenOnceAsync(columns, rows, cancellationToken).ConfigureAwait(true);
                    return;
                }

                WriteSystemLine(Ui.Connections.TerminalAgentOutOfDate);
                Status = Ui.Connections.TerminalConnectionFailed;
                return;
            }

            if (response.Failure is not null)
            {
                WriteSystemLine(response.Failure.Message);
                Status = Ui.Connections.TerminalConnectionFailed;
                return;
            }

            _sessionId = response.SessionId;
            _consumedSequence = response.NextSequence;
            Status = Ui.Format(
                Ui.Connections.TerminalConnectedFormat, response.DisplayName, columns, rows);
            _pump = PumpAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error) when (IsTransportFailure(error))
        {
            WriteSystemLine(Ui.Connections.TerminalCouldNotOpen);
            Status = Ui.Connections.TerminalAgentUnavailable;
        }
    }

    /// <summary>
    /// Reads output until the session ends or the pane closes.
    /// </summary>
    /// <remarks>
    /// A failed read backs off before the next attempt. The timer this replaces got that spacing
    /// for free from its interval; a loop that retried immediately would burn its five failures in
    /// a few microseconds and declare the agent lost over a blip that had not finished happening.
    /// </remarks>
    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        // Off the caller's stack before the first read. Without this the loop runs inline for as
        // long as reads keep completing synchronously, and opening a session never returns.
        await Task.Yield();

        while (!cancellationToken.IsCancellationRequested && IsConnected)
        {
            try
            {
                var response = await _client.ReadAsync(
                    new SshTerminalReadRequest(
                        SshTerminalIpcContract.CurrentVersion,
                        _sessionId,
                        SshTerminalIpcContract.MaximumChunkBytes,
                        _consumedSequence,
                        ReadWaitMilliseconds),
                    cancellationToken).ConfigureAwait(true);

                _consecutiveReadFailures = 0;

                if (response.Failure is not null)
                {
                    WriteSystemLine(response.Failure.Message);
                    Disconnect();
                    return;
                }

                if (response.Truncated)
                {
                    // The agent could not hold everything from where this terminal had got to.
                    // Saying so leaves a visible mark rather than a silent hole in the session.
                    WriteSystemLine(Ui.Connections.TerminalOutputDropped);
                }

                if (response.Content.Length > 0)
                {
                    Feed(response.Content);

                    // Advanced only now, once the bytes are in the emulator. Anything that goes
                    // wrong before this point leaves the mark where it was and the next read
                    // replays from there.
                    _consumedSequence = response.StartSequence + response.Content.Length;
                }

                if (!response.IsConnected)
                {
                    WriteSystemLine(Ui.Connections.TerminalSessionClosed);
                    Disconnect();
                    return;
                }

                // An agent that answered early and empty is not long-polling, and asking it again
                // straight away is a spin at the speed of the pipe. The timer this loop replaces
                // got that floor for free from its interval; here it has to be asked for. Output
                // never waits: only a read that brought nothing back pauses.
                if (response.Content.Length == 0) await PauseAsync(cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception error) when (IsTransportFailure(error))
            {
                _consecutiveReadFailures++;
                if (_consecutiveReadFailures >= MaximumConsecutiveReadFailures)
                {
                    WriteSystemLine(Ui.Connections.TerminalLostAgent);
                    Disconnect();
                    return;
                }

                await PauseAsync(cancellationToken).ConfigureAwait(true);
            }
        }
    }

    /// <summary>The floor between two reads, and the one place cancellation is not an error.</summary>
    private async Task PauseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_preferences.RefreshIntervalMilliseconds, cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public bool SendKey(Key key, bool shift, bool alt, bool control)
    {
        var bytes = VtKeyEncoder.Encode(key, shift, alt, control, KeyModes);
        if (bytes is null) return false;
        _ = SendAsync(bytes);
        return true;
    }

    public void SendText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        _ = SendAsync(Encoding.UTF8.GetBytes(text));
    }

    public void Paste(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        _ = SendAsync(VtKeyEncoder.EncodePaste(text, KeyModes));
    }

    public void Resize(int columns, int rows) => _ = ResizeAsync(columns, rows);

    private VtKeyModes KeyModes => new(
        ApplicationCursorKeys: _emulator.ApplicationCursorKeys,
        BracketedPaste: _emulator.BracketedPaste);

    /// <summary>Sends bytes to the remote shell, reporting a failure into the session itself.</summary>
    internal async Task SendAsync(byte[] bytes)
    {
        if (!IsConnected || _lifetime.IsCancellationRequested || bytes.Length == 0) return;
        try
        {
            var response = await _client.WriteAsync(
                new SshTerminalWriteRequest(
                    SshTerminalIpcContract.CurrentVersion, _sessionId, bytes),
                _lifetime.Token).ConfigureAwait(true);
            if (response.Failure is not null)
            {
                WriteSystemLine(response.Failure.Message);
                Disconnect();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error) when (IsTransportFailure(error))
        {
            WriteSystemLine(Ui.Connections.TerminalCouldNotSendInput);
            Disconnect();
        }
    }

    /// <summary>
    /// Tells the remote program how wide its window is.
    /// </summary>
    /// <remarks>
    /// The local emulator is resized first and unconditionally, so the screen reflows even when the
    /// agent cannot be told. A resize that reached only one of the two would leave a shell drawing
    /// its prompt to a width the screen does not have.
    /// </remarks>
    internal async Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default)
    {
        (columns, rows) = ClampGrid(columns, rows);
        if (columns == _emulator.Columns && rows == _emulator.Rows) return;

        _emulator.Resize(columns, rows);
        OutputReceived?.Invoke(this, EventArgs.Empty);
        if (!IsConnected || _lifetime.IsCancellationRequested) return;

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _lifetime.Token);
            var response = await _client.ResizeAsync(
                new SshTerminalResizeRequest(
                    SshTerminalIpcContract.CurrentVersion, _sessionId, columns, rows),
                linked.Token).ConfigureAwait(true);
            if (response.Resized)
            {
                Status = Ui.Format(Ui.Connections.TerminalResizedFormat, columns, rows);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error) when (IsTransportFailure(error))
        {
            Status = Ui.Connections.TerminalResizeFailed;
        }
    }

    /// <summary>
    /// Closes the remote session, on a deadline of its own.
    /// </summary>
    /// <remarks>
    /// The deadline is not the session's lifetime token, which has already been cancelled by the
    /// time this runs -- closing has to outlive the thing being closed, or the agent is left
    /// holding a shell nobody is ever going to read from again.
    /// </remarks>
    internal async Task CloseAsync()
    {
        if (!IsConnected) return;
        var sessionId = _sessionId;
        _sessionId = Guid.Empty;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            _ = await _client.CloseAsync(
                new SshTerminalCloseRequest(SshTerminalIpcContract.CurrentVersion, sessionId),
                deadline.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (IsTransportFailure(error) || error is OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Gives up on the session, and tells the agent so.
    /// </summary>
    /// <remarks>
    /// Closing rather than only forgetting. The remote end may already be gone -- that is one of
    /// the ways this is reached -- but the agent still holds a session record with a keep-alive
    /// against it, and nothing else will ever come to collect it.
    /// </remarks>
    private void Disconnect()
    {
        Status = Ui.Connections.TerminalDisconnected;
        _closing = CloseAsync();
    }

    private void Feed(byte[] content)
    {
        var characters = new char[Encoding.UTF8.GetMaxCharCount(content.Length)];
        _decoder.Convert(content, characters, flush: false, out _, out var used, out _);
        if (used == 0) return;
        _emulator.Feed(characters.AsSpan(0, used));
        OutputReceived?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Writes one of StorageHub's own lines into the session output.
    /// </summary>
    /// <remarks>
    /// On its own line and tagged, so it cannot be mistaken for something the remote host said.
    /// </remarks>
    private void WriteSystemLine(string message)
    {
        _emulator.Feed("\r\n" + Ui.Format(Ui.Connections.TerminalSystemLineFormat, message) + "\r\n");
        OutputReceived?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The emulator answering a host query such as DSR or DA; several TUIs block on it.</summary>
    private void OnResponseRequested(object? sender, VtResponseEventArgs e) => _ = SendAsync(e.Response);

    private void OnTitleChanged(object? sender, VtTitleEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.Title)) Status = e.Title;
    }

    /// <summary>
    /// A grid the contract will accept.
    /// </summary>
    /// <remarks>
    /// A pane can be dragged narrower than twenty columns, and the agent rejects a request outside
    /// its bounds outright -- so an unclamped size turns a small pane into a failed resize rather
    /// than a small terminal.
    /// </remarks>
    private static (int Columns, int Rows) ClampGrid(int columns, int rows) => (
        Math.Clamp(columns, SshTerminalIpcContract.MinimumColumns, SshTerminalIpcContract.MaximumColumns),
        Math.Clamp(rows, SshTerminalIpcContract.MinimumRows, SshTerminalIpcContract.MaximumRows));

    /// <summary>The ways a pipe can fail that are the pipe's fault rather than a bug.</summary>
    private static bool IsTransportFailure(Exception error) =>
        error is IOException or UnauthorizedAccessException or InvalidDataException or
            InvalidOperationException or TimeoutException or System.Text.Json.JsonException;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _emulator.ResponseRequested -= OnResponseRequested;
        _emulator.TitleChanged -= OnTitleChanged;
        await _lifetime.CancelAsync().ConfigureAwait(false);

        try
        {
            await _pump.ConfigureAwait(false);
            await _closing.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        await CloseAsync().ConfigureAwait(false);
        _lifetime.Dispose();
        if (_ownsClient) await _client.DisposeAsync().ConfigureAwait(false);
    }
}
