using Avalonia.Input;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop;
using StorageHub.Desktop.Localization;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The SSH session: opening it, reading it, typing at it, and losing it.
/// </summary>
/// <remarks>
/// <para>
/// In the WinForms shell this logic lived inside a 618-line <c>Form</c>, wound through three timers
/// and a status strip, and none of it could be reached without a window. The interesting cases are
/// all failures -- a stale agent, a read that throws, output the agent could not keep -- and every
/// one of them had to be argued about rather than run.
/// </para>
/// <para>
/// The client here is a fake, so each of those is one line to provoke.
/// </para>
/// </remarks>
public class SshTerminalSessionTests
{
    [Fact]
    public async Task OpeningASessionSendsTheMeasuredGrid()
    {
        var agent = new FakeAgent();
        await using var session = Session(agent);

        await session.OpenAsync(132, 43, CancellationToken.None);

        Assert.True(session.IsConnected);
        Assert.Equal(132, agent.Opened!.Columns);
        Assert.Equal(43, agent.Opened.Rows);

        // And the local emulator agrees, or the remote would wrap where the screen does not.
        Assert.Equal(132, session.Document.Columns);
        Assert.Equal(43, session.Document.Rows);
    }

    /// <summary>
    /// A pane too small for the contract still opens a terminal.
    /// </summary>
    /// <remarks>
    /// A quarter-width pane on a laptop is under twenty columns, and the agent rejects a size
    /// outside its bounds outright -- so without clamping, making a pane small would not give a
    /// small terminal, it would give no terminal.
    /// </remarks>
    [Fact]
    public async Task ATinyPaneIsClampedRatherThanRefused()
    {
        var agent = new FakeAgent();
        await using var session = Session(agent);

        await session.OpenAsync(3, 1, CancellationToken.None);

        Assert.True(session.IsConnected);
        Assert.Equal(SshTerminalIpcContract.MinimumColumns, agent.Opened!.Columns);
        Assert.Equal(SshTerminalIpcContract.MinimumRows, agent.Opened.Rows);
    }

    [Fact]
    public async Task OutputReachesTheScreen()
    {
        var agent = new FakeAgent();
        agent.Output("hello world");
        await using var session = Session(agent);

        await session.OpenAsync(80, 24, CancellationToken.None);
        await agent.Settled;

        Assert.Contains("hello world", Text(session));
    }

    /// <summary>
    /// A character split across two reads arrives whole.
    /// </summary>
    /// <remarks>
    /// The agent returns bytes and the emulator consumes characters. Decoding each chunk on its own
    /// turns every multi-byte character unlucky enough to straddle a boundary into a replacement
    /// character -- rare enough to survive testing by hand, common enough to be reported as
    /// "sometimes the box-drawing is wrong".
    /// </remarks>
    [Fact]
    public async Task ACharacterSplitAcrossTwoReadsIsNotMangled()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("──");
        var agent = new FakeAgent();
        agent.Output(bytes[..3]);
        agent.Output(bytes[3..]);
        await using var session = Session(agent);

        await session.OpenAsync(80, 24, CancellationToken.None);
        await agent.Settled;

        Assert.Contains("──", Text(session));
        Assert.DoesNotContain("�", Text(session));
    }

    /// <summary>
    /// Every read acknowledges only what has actually been drawn.
    /// </summary>
    /// <remarks>
    /// This is what makes a lost response harmless: the agent keeps everything above the
    /// acknowledgement, so a read that vanished is replayed rather than mourned.
    /// </remarks>
    [Fact]
    public async Task TheAcknowledgementFollowsWhatWasConsumed()
    {
        var agent = new FakeAgent();
        agent.Output("abcde");
        await using var session = Session(agent);

        await session.OpenAsync(80, 24, CancellationToken.None);
        await agent.Settled;

        Assert.Equal(5, agent.LastAcknowledged);
    }

    /// <summary>
    /// A single failed read does not end the session.
    /// </summary>
    /// <remarks>
    /// The acknowledgement is what makes that safe -- it did not advance, so the agent still holds
    /// those bytes -- and an earlier version of this loop gave up here, losing a live session to
    /// one hiccup in a pipe.
    /// </remarks>
    [Fact]
    public async Task AFailedReadDoesNotEndTheSession()
    {
        var agent = new FakeAgent();
        agent.Output("first");
        agent.FailNextRead(new IOException("the pipe broke"));
        agent.Output("second");
        await using var session = Session(agent);

        await session.OpenAsync(80, 24, CancellationToken.None);
        await agent.Settled;

        var text = Text(session);
        Assert.Contains("first", text);
        Assert.Contains("second", text);
    }

    /// <summary>
    /// One blip is survivable; five in a row is not.
    /// </summary>
    /// <remarks>
    /// The behaviour that matters is the first half. An earlier version gave up on the first failed
    /// read, which threw a live session away -- along with whatever the agent was still holding for
    /// it -- because a pipe hiccupped once.
    /// </remarks>
    [Fact]
    public async Task RepeatedReadFailuresGiveUpOnTheSession()
    {
        var agent = new FakeAgent();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            agent.FailNextRead(new IOException("the pipe broke"));
        }

        await using var session = Session(agent);

        await session.OpenAsync(80, 24, CancellationToken.None);
        await agent.Settled;

        Assert.False(session.IsConnected);
        Assert.Equal(Ui.Connections.TerminalDisconnected, session.Status);
        Assert.Contains(Ui.Connections.TerminalLostAgent, Text(session));
    }

    /// <summary>
    /// Output the agent could not keep leaves a visible mark.
    /// </summary>
    /// <remarks>
    /// The alternative is a silent hole: the session simply continues from later on, and nothing
    /// on screen says that anything went missing.
    /// </remarks>
    [Fact]
    public async Task DroppedOutputIsAnnouncedRatherThanHidden()
    {
        var agent = new FakeAgent();
        agent.Output("resumed", truncated: true);
        await using var session = Session(agent);

        await session.OpenAsync(80, 24, CancellationToken.None);
        await agent.Settled;

        Assert.Contains(Ui.Connections.TerminalOutputDropped, Text(session));
    }

    /// <summary>A remote shell that exits says so, and the session stops being connected.</summary>
    [Fact]
    public async Task AClosedRemoteSessionEndsThisOne()
    {
        var agent = new FakeAgent();
        agent.Output("logout");
        agent.Disconnects();
        await using var session = Session(agent);

        await session.OpenAsync(80, 24, CancellationToken.None);
        await agent.Settled;

        Assert.False(session.IsConnected);
        Assert.Contains(Ui.Connections.TerminalSessionClosed, Text(session));

        // And the agent is told, rather than left holding a session nobody will read again.
        Assert.True(agent.WasClosed);
    }

    /// <summary>
    /// A stale agent is restarted rather than negotiated with.
    /// </summary>
    /// <remarks>
    /// The desktop and the agent ship in one bundle, so a version mismatch never means two
    /// independently deployed versions -- it means a build from an hour ago is still running.
    /// </remarks>
    [Fact]
    public async Task AVersionMismatchRestartsTheAgentAndRetriesOnce()
    {
        var agent = new FakeAgent();
        agent.FailNextOpen(Mismatch());
        var lifecycle = new FakeLifecycle(succeeds: true);
        await using var session = Session(agent, lifecycle);

        await session.OpenAsync(80, 24, CancellationToken.None);

        Assert.Equal(1, lifecycle.Restarts);
        Assert.True(session.IsConnected);
    }

    /// <summary>And is not restarted twice, which would be a loop rather than a recovery.</summary>
    [Fact]
    public async Task AMismatchThatSurvivesTheRestartIsReported()
    {
        var agent = new FakeAgent();
        agent.FailNextOpen(Mismatch());
        agent.FailNextOpen(Mismatch());
        var lifecycle = new FakeLifecycle(succeeds: true);
        await using var session = Session(agent, lifecycle);

        await session.OpenAsync(80, 24, CancellationToken.None);

        Assert.Equal(1, lifecycle.Restarts);
        Assert.False(session.IsConnected);
        Assert.Equal(Ui.Connections.TerminalConnectionFailed, session.Status);
    }

    /// <summary>Without a lifecycle controller the mismatch is still reported, just not fixed.</summary>
    [Fact]
    public async Task AMismatchWithNoLifecycleControllerStillSaysSo()
    {
        var agent = new FakeAgent();
        agent.FailNextOpen(Mismatch());
        await using var session = Session(agent);

        await session.OpenAsync(80, 24, CancellationToken.None);

        Assert.False(session.IsConnected);
        Assert.Contains("Stale agent.", Text(session));
    }

    /// <summary>An agent that cannot be reached at all is not an unhandled exception.</summary>
    [Fact]
    public async Task AnUnreachableAgentIsReportedRatherThanThrown()
    {
        var agent = new FakeAgent();
        agent.ThrowOnOpen(new IOException("there is no agent"));
        await using var session = Session(agent);

        await session.OpenAsync(80, 24, CancellationToken.None);

        Assert.False(session.IsConnected);
        Assert.Equal(Ui.Connections.TerminalAgentUnavailable, session.Status);
        Assert.Contains(Ui.Connections.TerminalCouldNotOpen, Text(session));
    }

    [Fact]
    public async Task TypingReachesTheRemoteShell()
    {
        var agent = new FakeAgent();
        await using var session = Session(agent);
        await session.OpenAsync(80, 24, CancellationToken.None);

        session.SendText("ls");
        await session.SendAsync("\r"u8.ToArray());

        Assert.Equal("ls\r", agent.Written);
    }

    /// <summary>A key with no terminal meaning is left for whoever else wants it.</summary>
    [Fact]
    public async Task AKeyTheTerminalDoesNotWantIsNotSwallowed()
    {
        var agent = new FakeAgent();
        await using var session = Session(agent);
        await session.OpenAsync(80, 24, CancellationToken.None);

        Assert.True(session.SendKey(Key.Enter, false, false, false));
        Assert.False(session.SendKey(Key.LeftShift, false, false, false));
    }

    /// <summary>Ctrl+C is an interrupt, not a copy -- which is why copying is Ctrl+Shift+C.</summary>
    [Fact]
    public async Task ControlCSendsAnInterrupt()
    {
        var agent = new FakeAgent();
        await using var session = Session(agent);
        await session.OpenAsync(80, 24, CancellationToken.None);

        Assert.True(session.SendKey(Key.C, false, false, control: true));

        Assert.Equal("\u0003", agent.Written);
    }

    /// <summary>
    /// A resize tells the remote and reflows the local screen.
    /// </summary>
    [Fact]
    public async Task ResizingTellsTheRemoteHowWideTheWindowIs()
    {
        var agent = new FakeAgent();
        await using var session = Session(agent);
        await session.OpenAsync(80, 24, CancellationToken.None);

        await session.ResizeAsync(100, 40, CancellationToken.None);

        Assert.Equal((100, 40), agent.Resized);
        Assert.Equal(100, session.Document.Columns);
        Assert.Equal(40, session.Document.Rows);
    }

    /// <summary>A resize to the size it already is costs nothing.</summary>
    [Fact]
    public async Task ResizingToTheSameGridSaysNothing()
    {
        var agent = new FakeAgent();
        await using var session = Session(agent);
        await session.OpenAsync(80, 24, CancellationToken.None);

        await session.ResizeAsync(80, 24, CancellationToken.None);

        Assert.Null(agent.Resized);
    }

    /// <summary>
    /// And a resize the agent refuses still reflows the screen.
    /// </summary>
    /// <remarks>
    /// The local emulator is the thing being painted. Leaving it at the old width because the
    /// agent could not be told would draw the session into a window it does not fit.
    /// </remarks>
    [Fact]
    public async Task AFailedResizeStillReflowsTheScreen()
    {
        var agent = new FakeAgent();
        await using var session = Session(agent);
        await session.OpenAsync(80, 24, CancellationToken.None);
        agent.ThrowOnResize(new IOException("the pipe broke"));

        await session.ResizeAsync(100, 40, CancellationToken.None);

        Assert.Equal(100, session.Document.Columns);
        Assert.Equal(Ui.Connections.TerminalResizeFailed, session.Status);
    }

    /// <summary>A host query is answered without anything above the session being involved.</summary>
    [Fact]
    public async Task TheEmulatorAnswersAHostQueryItself()
    {
        var agent = new FakeAgent();

        // Device Status Report: several full-screen programs block until it is answered.
        agent.Output("\u001b[6n");
        await using var session = Session(agent);

        await session.OpenAsync(80, 24, CancellationToken.None);
        await agent.Settled;

        Assert.StartsWith("\u001b[", agent.Written);
        Assert.EndsWith("R", agent.Written);
    }

    /// <summary>Closing the pane closes the session on the agent.</summary>
    [Fact]
    public async Task DisposingClosesTheRemoteSession()
    {
        var agent = new FakeAgent();
        var session = Session(agent);
        await session.OpenAsync(80, 24, CancellationToken.None);

        await session.DisposeAsync();

        Assert.True(agent.WasClosed);
        Assert.False(session.IsConnected);
    }

    /// <summary>A session that never opened is still safe to dispose.</summary>
    [Fact]
    public async Task DisposingBeforeOpeningIsNotAFailure()
    {
        var session = Session(new FakeAgent());

        await session.DisposeAsync();
        await session.DisposeAsync();
    }

    /// <summary>The failure a stale agent answers an open with.</summary>
    private static StorageIpcFailure Mismatch() => new(
        "ssh.terminal.version.mismatch",
        StorageIpcFailureCategory.Unsupported,
        "Stale agent.",
        IsTransient: false);

    private static SshTerminalSession Session(
        FakeAgent agent, IAgentLifecycleController? lifecycle = null) =>
        new(Guid.NewGuid(), agent, preferences: null, lifecycle);

    /// <summary>
    /// Everything on the screen, including the scrollback above it.
    /// </summary>
    /// <remarks>
    /// Wrapped lines are rejoined. A message longer than the screen is wide really is two rows in
    /// the buffer, and asserting against the rows would make these tests depend on where an eighty
    /// column screen happens to break an English sentence.
    /// </remarks>
    private static string Text(SshTerminalSession session) => session.Document.GetText(
        session.Document.FirstLineNumber, session.Document.TotalLineCount, joinWrapped: true);

    private sealed class FakeLifecycle(bool succeeds) : IAgentLifecycleController
    {
        internal int Restarts { get; private set; }

        public Task<AgentLifecycleResult> ExecuteAsync(
            AgentLifecycleAction action, CancellationToken cancellationToken = default)
        {
            if (action == AgentLifecycleAction.Restart) Restarts++;
            return Task.FromResult(new AgentLifecycleResult(succeeds, "restarted"));
        }
    }

    /// <summary>
    /// An agent with a script: a queue of things the next read will do.
    /// </summary>
    /// <remarks>
    /// Once the script runs out the reads answer empty for ever, which is what an idle session
    /// looks like. <see cref="Drained"/> completes when the script has been consumed, so a test can
    /// wait for exactly what it queued rather than for a duration.
    /// </remarks>
    private sealed class FakeAgent : ISshTerminalAgentClient
    {
        private readonly Queue<Func<SshTerminalReadResponse>> _script = new();
        private readonly Queue<StorageIpcFailure> _openFailures = new();
        private readonly TaskCompletionSource _settled =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly System.Text.StringBuilder _written = new();
        private Guid _sessionId = Guid.NewGuid();
        private Exception? _openThrows;
        private Exception? _resizeThrows;
        private long _sequence;

        internal SshTerminalOpenRequest? Opened { get; private set; }

        internal string Written => _written.ToString();

        internal long LastAcknowledged { get; private set; }

        internal (int Columns, int Rows)? Resized { get; private set; }

        internal bool WasClosed { get; private set; }

        /// <summary>
        /// Completes once the session has finished with the script.
        /// </summary>
        /// <remarks>
        /// Either it came back for a read the script had nothing left for -- which it only does
        /// after processing the previous answer -- or it gave up and closed. Waiting on this rather
        /// than on a duration is what keeps these tests from being timing guesses.
        /// </remarks>
        internal Task Settled => _settled.Task;

        internal void Output(string text, bool truncated = false) =>
            Output(System.Text.Encoding.UTF8.GetBytes(text), truncated);

        internal void Output(byte[] content, bool truncated = false) =>
            _script.Enqueue(() =>
            {
                var start = _sequence;
                _sequence += content.Length;
                return new SshTerminalReadResponse(
                    SshTerminalIpcContract.CurrentVersion, _sessionId, content,
                    IsConnected: true, Failure: null, StartSequence: start, Truncated: truncated);
            });

        internal void Disconnects() =>
            _script.Enqueue(() => new SshTerminalReadResponse(
                SshTerminalIpcContract.CurrentVersion, _sessionId, [], IsConnected: false));

        internal void FailNextRead(Exception error) =>
            _script.Enqueue(() => throw error);

        internal void FailNextOpen(StorageIpcFailure failure) => _openFailures.Enqueue(failure);

        internal void ThrowOnOpen(Exception error) => _openThrows = error;

        internal void ThrowOnResize(Exception error) => _resizeThrows = error;

        public Task<SshTerminalOpenResponse> OpenAsync(
            SshTerminalOpenRequest request, CancellationToken cancellationToken = default)
        {
            if (_openThrows is { } error) throw error;
            Opened = request;
            if (_openFailures.TryDequeue(out var failure))
            {
                return Task.FromResult(new SshTerminalOpenResponse(
                    SshTerminalIpcContract.CurrentVersion, Guid.Empty, string.Empty, failure));
            }

            _sessionId = Guid.NewGuid();
            return Task.FromResult(new SshTerminalOpenResponse(
                SshTerminalIpcContract.CurrentVersion, _sessionId, "fake-host"));
        }

        public Task<SshTerminalWriteResponse> WriteAsync(
            SshTerminalWriteRequest request, CancellationToken cancellationToken = default)
        {
            _ = _written.Append(System.Text.Encoding.UTF8.GetString(request.Content));
            return Task.FromResult(new SshTerminalWriteResponse(
                SshTerminalIpcContract.CurrentVersion, request.SessionId, request.Content.Length));
        }

        public Task<SshTerminalReadResponse> ReadAsync(
            SshTerminalReadRequest request, CancellationToken cancellationToken = default)
        {
            LastAcknowledged = request.AcknowledgedSequence;
            if (_script.TryDequeue(out var next))
            {
                return Task.FromResult(next());
            }

            _ = _settled.TrySetResult();
            return Task.FromResult(new SshTerminalReadResponse(
                SshTerminalIpcContract.CurrentVersion, request.SessionId, [], IsConnected: true,
                Failure: null, StartSequence: _sequence));
        }

        public Task<SshTerminalResizeResponse> ResizeAsync(
            SshTerminalResizeRequest request, CancellationToken cancellationToken = default)
        {
            if (_resizeThrows is { } error) throw error;
            Resized = (request.Columns, request.Rows);
            return Task.FromResult(new SshTerminalResizeResponse(
                SshTerminalIpcContract.CurrentVersion, request.SessionId, Resized: true));
        }

        public Task<SshTerminalCloseResponse> CloseAsync(
            SshTerminalCloseRequest request, CancellationToken cancellationToken = default)
        {
            WasClosed = true;
            _ = _settled.TrySetResult();
            return Task.FromResult(new SshTerminalCloseResponse(
                SshTerminalIpcContract.CurrentVersion, request.SessionId, Closed: true));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
