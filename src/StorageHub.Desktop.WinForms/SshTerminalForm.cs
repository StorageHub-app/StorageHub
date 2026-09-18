using System.Runtime.InteropServices;
using System.Text;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

public sealed class SshTerminalForm : Form
{
    /// <summary>
    /// How long the agent may hold a read open waiting for output. Long-polling means output
    /// reaches the screen when it is produced rather than up to one poll interval later, and an
    /// idle session stops costing a request every interval.
    /// </summary>
    private const int ReadWaitMilliseconds = 400;

    /// <summary>How many reads in a row may fail before the session is given up on.</summary>
    private const int MaximumConsecutiveReadFailures = 5;

    /// <summary>
    /// How a terminal reaches the agent's lifecycle when it was not handed one. A terminal can be
    /// opened from a pane, a workspace or the connection manager, and threading a controller
    /// through all of those to serve one recovery path would be a lot of plumbing for it.
    /// </summary>
    internal static Func<IAgentLifecycleController?>? DefaultAgentLifecycleProvider { get; set; }

    private readonly Guid _connectionId;
    private readonly IAgentLifecycleController? _agentLifecycle;
    private readonly ISshTerminalAgentClient _client;
    private readonly bool _ownsClient;
    private readonly TerminalView _terminal;
    private readonly ToolStripStatusLabel _status;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private readonly System.Windows.Forms.Timer _renderTimer;
    private readonly System.Windows.Forms.Timer _resizeTimer;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Decoder _utf8Decoder = Encoding.UTF8.GetDecoder();
    private readonly VtTerminalEmulator _buffer;
    private readonly SshTerminalPreferences _preferences;
    private readonly Font _terminalFont;
    private Guid _sessionId;

    /// <summary>
    /// How far into the session's output this terminal has actually got. It advances only after
    /// bytes have reached the emulator, and every read sends it as the acknowledgement, so a read
    /// that is cancelled, times out or throws simply replays instead of losing that output.
    /// </summary>
    private long _consumedSequence;
    private int _consecutiveReadFailures;
    private bool _restartedAgentForVersionMismatch;
    private bool _polling;
    private bool _closingSession;
    private bool _shutdownStarted;
    private bool _openStarted;
    private Task _closeTask = Task.CompletedTask;

    public SshTerminalForm(
        Guid connectionId,
        string displayName,
        ISshTerminalAgentClient? client = null,
        IAgentLifecycleController? agentLifecycle = null)
        : this(connectionId, displayName, client, preferences: null, agentLifecycle)
    {
    }

    internal SshTerminalForm(
        Guid connectionId,
        string displayName,
        ISshTerminalAgentClient? client,
        SshTerminalPreferences? preferences,
        IAgentLifecycleController? agentLifecycle = null)
    {
        if (connectionId == Guid.Empty)
        {
            throw new ArgumentException("An SSH connection ID is required.", nameof(connectionId));
        }

        _connectionId = connectionId;

        // Optional: without a controller a stale agent still reports the mismatch, it just cannot
        // be restarted automatically and the user is told to restart StorageHub instead.
        _agentLifecycle = agentLifecycle ?? DefaultAgentLifecycleProvider?.Invoke();
        _ownsClient = client is null;
        _client = client ?? new NamedPipeSshTerminalAgentClient();
        _preferences = SshTerminalPreferences.Resolve(
            preferences ?? DesktopConfigStore.CreateDefault().Load().SshTerminal);
        _terminalFont = CreateTerminalFont(_preferences.FontFamily, _preferences.FontSize);

        // The starting grid is a placeholder only: the real size is measured from the control once
        // it has a handle, and sent to the remote when the session opens.
        _buffer = new VtTerminalEmulator(80, 24, _preferences.ScrollbackLines);
        _buffer.ResponseRequested += TerminalResponseRequested;
        _buffer.TitleChanged += TerminalTitleChanged;
        Text = Ui.Format(Ui.Connections.TerminalCaptionFormat, displayName);
        AccessibleName = Ui.Format(Ui.Connections.TerminalAccessibleNameFormat, displayName);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(640, 400);
        Size = new Size(1000, 680);
        BackColor = Color.FromArgb(12, 18, 28);
        KeyPreview = true;

        _terminal = new TerminalView(_buffer, _terminalFont)
        {
            RenderBoldText = _preferences.RenderBoldText,
            AccessibleName = Ui.Connections.TerminalOutputAccessibleName,
            AccessibleDescription = Ui.Connections.TerminalOutputAccessibleDescription
        };
        _terminal.KeyDown += TerminalKeyDown;
        _terminal.KeyPress += TerminalKeyPress;
        _terminal.MouseUp += TerminalMouseUp;
        _terminal.Resize += TerminalResize;

        var statusStrip = new StatusStrip
        {
            SizingGrip = false,
            BackColor = Color.FromArgb(20, 29, 43)
        };
        _status = new ToolStripStatusLabel(Ui.Connections.TerminalConnecting)
        {
            ForeColor = Color.FromArgb(148, 163, 184),
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        statusStrip.Items.Add(_status);
        Controls.Add(_terminal);
        Controls.Add(statusStrip);

        _pollTimer = new System.Windows.Forms.Timer { Interval = _preferences.RefreshIntervalMilliseconds };
        _pollTimer.Tick += PollTimerTick;

        // Reading and painting are separate clocks. Output is fed as it arrives, while painting is
        // capped, so a keystroke echoes back quickly without the paint rate following the data.
        _renderTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _renderTimer.Tick += RenderTimerTick;
        _resizeTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _resizeTimer.Tick += ResizeTimerTick;
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await StartSessionAsync();
    }

    public async Task StartSessionAsync()
    {
        if (_shutdownStarted || _openStarted)
        {
            return;
        }

        _openStarted = true;
        await OpenSessionAsync(_lifetime.Token);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        BeginShutdown();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            BeginShutdown();
            _pollTimer.Tick -= PollTimerTick;
            _renderTimer.Tick -= RenderTimerTick;
            _resizeTimer.Tick -= ResizeTimerTick;
            _buffer.ResponseRequested -= TerminalResponseRequested;
            _buffer.TitleChanged -= TerminalTitleChanged;
            _terminal.KeyDown -= TerminalKeyDown;
            _terminal.KeyPress -= TerminalKeyPress;
            _terminal.MouseUp -= TerminalMouseUp;
            _terminal.Resize -= TerminalResize;
            _terminalFont.Dispose();
            _pollTimer.Dispose();
            _renderTimer.Dispose();
            _resizeTimer.Dispose();
            _lifetime.Dispose();
            if (_ownsClient)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _closeTask.ConfigureAwait(false);
                        await _client.DisposeAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                });
            }
        }
        base.Dispose(disposing);
    }

    private void BeginShutdown()
    {
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        _pollTimer.Stop();
        _renderTimer.Stop();
        _resizeTimer.Stop();
        _lifetime.Cancel();
        _closeTask = CloseSessionAsync();
    }

    private async Task OpenSessionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var (columns, rows) = GetTerminalSize();
            _buffer.Resize(columns, rows);
            var response = await _client.OpenAsync(new SshTerminalOpenRequest(
                SshTerminalIpcContract.CurrentVersion,
                _connectionId,
                columns,
                rows,
                _preferences.TerminalName,
                _preferences.StartupCommand,
                _preferences.KeepAliveSeconds), cancellationToken);
            if (response.Failure is { Code: "ssh.terminal.version.mismatch" }
                && !_restartedAgentForVersionMismatch
                && _agentLifecycle is not null)
            {
                // A stale agent is still running from a previous build. The desktop and the agent
                // ship together, so the fix is to restart it rather than to negotiate a version.
                _restartedAgentForVersionMismatch = true;
                _status.Text = Ui.Connections.TerminalRestartingAgent;
                var restarted = await _agentLifecycle
                    .ExecuteAsync(AgentLifecycleAction.Restart, cancellationToken)
                    .ConfigureAwait(true);
                if (restarted.Succeeded)
                {
                    await OpenSessionAsync(cancellationToken).ConfigureAwait(true);
                    return;
                }

                AppendSystemLine(Ui.Connections.TerminalAgentOutOfDate);
                _status.Text = Ui.Connections.TerminalConnectionFailed;
                return;
            }

            if (response.Failure is not null)
            {
                AppendSystemLine(response.Failure.Message);
                _status.Text = Ui.Connections.TerminalConnectionFailed;
                return;
            }

            _sessionId = response.SessionId;
            _consumedSequence = response.NextSequence;
            _status.Text = Ui.Format(
                Ui.Connections.TerminalConnectedFormat, response.DisplayName, columns, rows);
            _pollTimer.Start();
            _renderTimer.Start();
            _terminal.Focus();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or
            InvalidOperationException or TimeoutException or System.Text.Json.JsonException)
        {
            AppendSystemLine(Ui.Connections.TerminalCouldNotOpen);
            _status.Text = Ui.Connections.TerminalAgentUnavailable;
        }
    }

    private async void PollTimerTick(object? sender, EventArgs e)
    {
        if (_polling || _sessionId == Guid.Empty || _lifetime.IsCancellationRequested)
        {
            return;
        }

        _polling = true;
        try
        {
            var response = await _client.ReadAsync(new SshTerminalReadRequest(
                SshTerminalIpcContract.CurrentVersion,
                _sessionId,
                SshTerminalIpcContract.MaximumChunkBytes,
                _consumedSequence,
                ReadWaitMilliseconds), _lifetime.Token);
            if (response.Failure is not null)
            {
                AppendSystemLine(response.Failure.Message);
                DisconnectUi();
                return;
            }

            if (response.Truncated)
            {
                // The agent could not hold everything from where this terminal had got to. Saying
                // so leaves a visible mark rather than a silent hole in the session.
                AppendSystemLine(Ui.Connections.TerminalOutputDropped);
            }

            if (response.Content.Length > 0)
            {
                FeedTerminalBytes(response.Content);

                // Advanced only now, once the bytes are in the emulator. Anything that goes wrong
                // before this point leaves the cursor where it was and the next read replays.
                _consumedSequence = response.StartSequence + response.Content.Length;
            }
            if (!response.IsConnected)
            {
                AppendSystemLine(Ui.Connections.TerminalSessionClosed);
                DisconnectUi();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or
            InvalidOperationException or TimeoutException or System.Text.Json.JsonException)
        {
            // One failed read is survivable now: the acknowledgement did not advance, so the agent
            // still holds those bytes and the next tick asks for them again. Giving up here -- as
            // this used to -- threw the session away over a blip and lost whatever it was holding.
            _consecutiveReadFailures++;
            if (_consecutiveReadFailures >= MaximumConsecutiveReadFailures)
            {
                AppendSystemLine(Ui.Connections.TerminalLostAgent);
                DisconnectUi();
            }
        }
        finally
        {
            _polling = false;
        }
    }

    private void TerminalKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+Shift+C copies and Ctrl+Shift+V pastes, which leaves plain Ctrl+C free to always
        // mean SIGINT. Binding copy to Ctrl+C would make interrupting a runaway process depend on
        // whether a stray selection happened to exist.
        if (e.Control && e.Shift && e.KeyCode == Keys.C)
        {
            CopySelection();
            e.SuppressKeyPress = true;
            return;
        }

        if ((e.Control && e.Shift && e.KeyCode == Keys.V)
            || (e.Shift && e.KeyCode == Keys.Insert))
        {
            PasteClipboard();
            e.SuppressKeyPress = true;
            return;
        }

        // Shift+PageUp/PageDown and Ctrl+Home/End scroll this window's own history. They are never
        // sent on: scrolling back through what already arrived is a local act.
        if (e.Shift && e.KeyCode is Keys.PageUp or Keys.PageDown)
        {
            _terminal.ScrollByPages(e.KeyCode == Keys.PageUp ? -1 : 1);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.End)
        {
            _terminal.ScrollToBottom();
            e.SuppressKeyPress = true;
            return;
        }

        var bytes = VtKeyEncoder.Encode(e.KeyCode, e.Shift, e.Alt, e.Control, CurrentKeyModes);
        if (bytes is not null)
        {
            e.SuppressKeyPress = true;

            // Typing is a statement of intent to be at the live end of the session.
            _terminal.ScrollToBottom();
            _ = SendAsync(bytes);
        }
    }

    private VtKeyModes CurrentKeyModes => new(
        ApplicationCursorKeys: _buffer.ApplicationCursorKeys,
        BracketedPaste: _buffer.BracketedPaste);

    private void CopySelection()
    {
        if (!_terminal.HasSelection)
        {
            return;
        }

        var text = _terminal.GetSelectedText();
        if (text.Length == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
        }
        catch (ExternalException)
        {
            // Another process can hold the clipboard open; a failed copy is not worth a dialog.
        }
    }

    private void PasteClipboard()
    {
        string text;
        try
        {
            text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
        }
        catch (ExternalException)
        {
            // Another process can hold the clipboard open; a failed paste is not worth a dialog.
            return;
        }

        if (text.Length > 0)
        {
            _ = SendAsync(VtKeyEncoder.EncodePaste(text, CurrentKeyModes));
        }
    }

    private void TerminalKeyPress(object? sender, KeyPressEventArgs e)
    {
        // Control characters have already gone out through the encoder on KeyDown; sending them
        // again here would double every Ctrl+letter and every Enter.
        if (!char.IsControl(e.KeyChar))
        {
            e.Handled = true;
            _ = SendAsync(Encoding.UTF8.GetBytes(e.KeyChar.ToString()));
        }
    }

    private void TerminalMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            PasteClipboard();
        }
    }

    private async Task SendAsync(byte[] bytes)
    {
        if (_sessionId == Guid.Empty || _lifetime.IsCancellationRequested)
        {
            return;
        }
        try
        {
            var response = await _client.WriteAsync(new SshTerminalWriteRequest(
                SshTerminalIpcContract.CurrentVersion,
                _sessionId,
                bytes), _lifetime.Token);
            if (response.Failure is not null)
            {
                AppendSystemLine(response.Failure.Message);
                DisconnectUi();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or
            InvalidOperationException or TimeoutException or System.Text.Json.JsonException)
        {
            AppendSystemLine(Ui.Connections.TerminalCouldNotSendInput);
            DisconnectUi();
        }
    }

    private void TerminalResize(object? sender, EventArgs e)
    {
        if (_sessionId != Guid.Empty)
        {
            _resizeTimer.Stop();
            _resizeTimer.Start();
        }
    }

    private async void ResizeTimerTick(object? sender, EventArgs e)
    {
        _resizeTimer.Stop();
        if (_sessionId == Guid.Empty || _lifetime.IsCancellationRequested)
        {
            return;
        }
        var (columns, rows) = GetTerminalSize();
        _buffer.Resize(columns, rows);
        _terminal.ApplyDamage();
        try
        {
            var response = await _client.ResizeAsync(new SshTerminalResizeRequest(
                SshTerminalIpcContract.CurrentVersion,
                _sessionId,
                columns,
                rows), _lifetime.Token);
            if (response.Resized)
            {
                _status.Text = Ui.Format(Ui.Connections.TerminalResizedFormat, columns, rows);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or
            InvalidOperationException or TimeoutException or System.Text.Json.JsonException)
        {
            _status.Text = Ui.Connections.TerminalResizeFailed;
            _status.ForeColor = StorageHubTheme.Warning;
        }
    }

    private async Task CloseSessionAsync()
    {
        if (_closingSession || _sessionId == Guid.Empty)
        {
            return;
        }
        _closingSession = true;
        var sessionId = _sessionId;
        _sessionId = Guid.Empty;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            _ = await _client.CloseAsync(new SshTerminalCloseRequest(
                SshTerminalIpcContract.CurrentVersion,
                sessionId), deadline.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or
            InvalidOperationException or TimeoutException or OperationCanceledException or System.Text.Json.JsonException)
        {
        }
    }

    /// <summary>The grid the window can hold, measured from the control's real cell metrics.</summary>
    private (int Columns, int Rows) GetTerminalSize() => _terminal.GetGridSize();

    private void FeedTerminalBytes(byte[] content)
    {
        var characters = new char[Encoding.UTF8.GetMaxCharCount(content.Length)];
        _utf8Decoder.Convert(content, characters, flush: false, out _, out var used, out _);
        if (used > 0)
        {
            _buffer.Feed(characters.AsSpan(0, used));
        }
    }

    private void AppendSystemText(string text) => _buffer.Feed(text);

    /// <summary>
    /// Writes one of StorageHub's own lines into the session output, on its own line and tagged so
    /// it cannot be mistaken for something the remote host said.
    /// </summary>
    private void AppendSystemLine(string message) =>
        AppendSystemText("\r\n" + Ui.Format(Ui.Connections.TerminalSystemLineFormat, message) + "\r\n");

    /// <summary>
    /// Presents whatever changed since the last frame. Painting is driven off accumulated damage
    /// rather than rebuilding the document, so a selection and a scrolled-back viewport both
    /// survive output arriving underneath them.
    /// </summary>
    private void RenderTimerTick(object? sender, EventArgs e)
    {
        if (_terminal.IsDisposed)
        {
            return;
        }

        _terminal.ApplyDamage();
    }

    /// <summary>The emulator answering a host query such as DSR or DA; several TUIs block on it.</summary>
    private void TerminalResponseRequested(object? sender, VtResponseEventArgs e) => _ = SendAsync(e.Response);

    private void TerminalTitleChanged(object? sender, VtTitleEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.Title))
        {
            _status.Text = e.Title;
        }
    }

    private void DisconnectUi()
    {
        _pollTimer.Stop();
        _status.Text = Ui.Connections.TerminalDisconnected;
        _closeTask = CloseSessionAsync();
    }

    private static Font CreateTerminalFont(string family, float size)
    {
        try
        {
            return new Font(family, size, FontStyle.Regular, GraphicsUnit.Point);
        }
        catch (ArgumentException)
        {
            return new Font(
                SshTerminalPreferences.Defaults.FontFamily,
                SshTerminalPreferences.Defaults.FontSize,
                FontStyle.Regular,
                GraphicsUnit.Point);
        }
    }

}
