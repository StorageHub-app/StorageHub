using System.Reflection;
using System.Text;
using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The acknowledgement cursor is what makes a lost read harmless: it advances only once bytes are
/// in the emulator, so a read that is cancelled, times out or throws leaves it where it was and
/// the next read replays from there.
/// </summary>
public sealed class SshTerminalReplayTests
{
    [Fact]
    public void A_read_that_never_arrives_is_re_requested_from_the_same_place()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            var client = new ScriptedSshTerminalClient();

            // The first read throws after the agent had already produced the bytes -- the exact
            // shape that used to lose output permanently.
            client.FailNextRead = true;
            using var terminal = new SshTerminalForm(Guid.NewGuid(), "SSH test", client);
            terminal.StartSessionAsync().GetAwaiter().GetResult();

            Poll(terminal);
            Poll(terminal);

            Assert.Equal(2, client.ReadRequests.Count);

            // Both reads ask from zero, because nothing was ever consumed.
            Assert.Equal(0, client.ReadRequests[0].AcknowledgedSequence);
            Assert.Equal(0, client.ReadRequests[1].AcknowledgedSequence);
            Assert.Contains("hello", ScreenText(terminal), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_acknowledgement_advances_only_after_the_bytes_reach_the_emulator()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            var client = new ScriptedSshTerminalClient();
            using var terminal = new SshTerminalForm(Guid.NewGuid(), "SSH test", client);
            terminal.StartSessionAsync().GetAwaiter().GetResult();

            Poll(terminal);
            Poll(terminal);

            Assert.Equal(0, client.ReadRequests[0].AcknowledgedSequence);
            Assert.Equal("hello".Length, client.ReadRequests[1].AcknowledgedSequence);
        });
    }

    [Fact]
    public void A_read_asks_the_agent_to_hold_it_open_briefly()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            var client = new ScriptedSshTerminalClient();
            using var terminal = new SshTerminalForm(Guid.NewGuid(), "SSH test", client);
            terminal.StartSessionAsync().GetAwaiter().GetResult();

            Poll(terminal);

            // Long-polling is what puts output on screen when it is produced rather than up to one
            // poll interval later.
            Assert.InRange(
                client.ReadRequests[0].WaitMilliseconds,
                1,
                SshTerminalIpcContract.MaximumReadWaitMilliseconds);
        });
    }

    [Fact]
    public void Dropped_output_is_reported_in_the_session_rather_than_left_as_a_silent_gap()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            var client = new ScriptedSshTerminalClient { ReportTruncated = true };
            using var terminal = new SshTerminalForm(Guid.NewGuid(), "SSH test", client);
            terminal.StartSessionAsync().GetAwaiter().GetResult();

            Poll(terminal);

            Assert.Contains("dropped", ScreenText(terminal), StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void An_out_of_date_agent_is_restarted_and_the_session_retried()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            var client = new ScriptedSshTerminalClient { FailFirstOpenWithVersionMismatch = true };
            var lifecycle = new RecordingAgentLifecycle();
            using var terminal = new SshTerminalForm(Guid.NewGuid(), "SSH test", client, lifecycle);

            terminal.StartSessionAsync().GetAwaiter().GetResult();

            // The desktop and the agent ship together, so a mismatch means a stale agent is still
            // running; restarting it is the fix, not negotiating a version.
            Assert.Equal([AgentLifecycleAction.Restart], lifecycle.Actions);
            Assert.Equal(2, client.OpenCount);
            Assert.NotEqual(Guid.Empty, SessionId(terminal));
        });
    }

    [Fact]
    public void Without_a_lifecycle_controller_the_mismatch_is_reported_instead_of_retried()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            var client = new ScriptedSshTerminalClient { FailFirstOpenWithVersionMismatch = true };
            using var terminal = new SshTerminalForm(Guid.NewGuid(), "SSH test", client);

            terminal.StartSessionAsync().GetAwaiter().GetResult();

            Assert.Equal(1, client.OpenCount);
            Assert.Equal(Guid.Empty, SessionId(terminal));
        });
    }

    private static void Poll(SshTerminalForm terminal)
    {
        typeof(SshTerminalForm)
            .GetMethod("PollTimerTick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(terminal, [null, EventArgs.Empty]);
        System.Windows.Forms.Application.DoEvents();
    }

    private static Guid SessionId(SshTerminalForm terminal) =>
        (Guid)typeof(SshTerminalForm)
            .GetField("_sessionId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(terminal)!;

    private static string ScreenText(SshTerminalForm terminal)
    {
        var emulator = (VtTerminalEmulator)typeof(SshTerminalForm)
            .GetField("_buffer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(terminal)!;
        return emulator.Document.GetText(
            emulator.Document.FirstLineNumber,
            emulator.Document.TotalLineCount,
            joinWrapped: true);
    }

    private sealed class RecordingAgentLifecycle : IAgentLifecycleController
    {
        internal List<AgentLifecycleAction> Actions { get; } = [];

        public Task<AgentLifecycleResult> ExecuteAsync(
            AgentLifecycleAction action,
            CancellationToken cancellationToken = default)
        {
            Actions.Add(action);
            return Task.FromResult(new AgentLifecycleResult(true, "restarted"));
        }
    }

    private sealed class ScriptedSshTerminalClient : ISshTerminalAgentClient
    {
        private static readonly byte[] Payload = Encoding.UTF8.GetBytes("hello");
        private readonly Guid _sessionId = Guid.NewGuid();

        internal List<SshTerminalReadRequest> ReadRequests { get; } = [];

        internal int OpenCount { get; private set; }

        internal bool FailNextRead { get; set; }

        internal bool ReportTruncated { get; set; }

        internal bool FailFirstOpenWithVersionMismatch { get; set; }

        public Task<SshTerminalOpenResponse> OpenAsync(
            SshTerminalOpenRequest request,
            CancellationToken cancellationToken = default)
        {
            OpenCount++;
            if (FailFirstOpenWithVersionMismatch && OpenCount == 1)
            {
                return Task.FromResult(new SshTerminalOpenResponse(
                    request.ContractVersion,
                    Guid.Empty,
                    string.Empty,
                    new StorageIpcFailure(
                        "ssh.terminal.version.mismatch",
                        StorageIpcFailureCategory.Validation,
                        "out of date",
                        IsTransient: false)));
            }

            return Task.FromResult(new SshTerminalOpenResponse(request.ContractVersion, _sessionId, "SSH test"));
        }

        public Task<SshTerminalWriteResponse> WriteAsync(
            SshTerminalWriteRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(
                new SshTerminalWriteResponse(request.ContractVersion, request.SessionId, request.Content.Length));

        public Task<SshTerminalReadResponse> ReadAsync(
            SshTerminalReadRequest request,
            CancellationToken cancellationToken = default)
        {
            ReadRequests.Add(request);
            if (FailNextRead)
            {
                FailNextRead = false;
                throw new IOException("the response never arrived");
            }

            // The agent replays from wherever the desktop says it has got to.
            var remaining = Math.Max(0, Payload.Length - (int)request.AcknowledgedSequence);
            var content = remaining == 0 ? [] : Payload[^remaining..];
            return Task.FromResult(new SshTerminalReadResponse(
                request.ContractVersion,
                request.SessionId,
                content,
                IsConnected: true,
                Failure: null,
                request.AcknowledgedSequence,
                ReportTruncated));
        }

        public Task<SshTerminalResizeResponse> ResizeAsync(
            SshTerminalResizeRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(
                new SshTerminalResizeResponse(request.ContractVersion, request.SessionId, Resized: true));

        public Task<SshTerminalCloseResponse> CloseAsync(
            SshTerminalCloseRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(
                new SshTerminalCloseResponse(request.ContractVersion, request.SessionId, Closed: true));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
