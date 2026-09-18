using System.Text.Json;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// What a failed agent call means, who decides it, and what the person is told.
/// </summary>
/// <remarks>
/// The window that prompted this showed three answers to one question at the same time: the
/// overview said "Overview unavailable: Pipe is broken.", the connections panel said the agent was
/// unavailable, and the status bar said "Agent: connected" -- the last because the status monitor
/// polls on a fresh connection and never sees the pipe a surface has been holding since before the
/// agent restarted. One place decides now, and these are its rules.
/// </remarks>
public sealed class AgentAvailabilityTests : IDisposable
{
    public AgentAvailabilityTests() => DesktopAgentAvailability.ResetForTests();

    public void Dispose() => DesktopAgentAvailability.ResetForTests();

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(ObjectDisposedException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(InvalidDataException))]
    [InlineData(typeof(InvalidOperationException))]
    public void TheWaysAPipeDiesAllCountAsTheAgentGoingAway(Type failure)
    {
        var error = (Exception)Activator.CreateInstance(failure, "broken")!;
        Assert.True(DesktopAgentAvailability.IsTransportFault(error));
    }

    [Fact]
    public void AProviderRefusalIsNotATransportFault()
    {
        // The agent answered; it simply said no. Reconnecting would not help, and telling somebody
        // that StorageHub is reconnecting would be a lie.
        Assert.False(DesktopAgentAvailability.IsTransportFault(new ArgumentException("no such profile")));
        Assert.Equal(
            Ui.Shell.AgentRequestFailed,
            DesktopAgentAvailability.Describe(new ArgumentException("no such profile")));
    }

    [Fact]
    public void TheMessageNeverCarriesTheExceptionsOwnWords()
    {
        // "Pipe is broken." is what .NET says and what the overview used to print: accurate,
        // untranslated, and of no use to anyone who did not write the IPC layer.
        var described = DesktopAgentAvailability.ReportFailure(new IOException("Pipe is broken."));

        Assert.DoesNotContain("Pipe", described, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Ui.Shell.AgentReconnecting, described);
    }

    [Fact]
    public void AFailedCallMarksTheAgentReconnectingRatherThanConnected()
    {
        DesktopAgentAvailability.ReportMonitor(AgentConnectionState.Connected);
        Assert.Equal(AgentAvailability.Online, DesktopAgentAvailability.Current);

        DesktopAgentAvailability.ReportFailure(new IOException("Pipe is broken."));

        Assert.Equal(AgentAvailability.Reconnecting, DesktopAgentAvailability.Current);
    }

    [Fact]
    public void TheMonitorIsWhatDecidesTheAgentIsGone()
    {
        // A surface's broken pipe says one connection is dead. Only a probe on a fresh connection
        // can say the agent is not running, which is why the wording differs between the two.
        DesktopAgentAvailability.ReportFailure(new IOException("Pipe is broken."));
        Assert.Equal(Ui.Shell.AgentReconnecting, DesktopAgentAvailability.Describe(new IOException("x")));

        DesktopAgentAvailability.ReportMonitor(AgentConnectionState.Disconnected);
        Assert.Equal(AgentAvailability.Offline, DesktopAgentAvailability.Current);
        Assert.Equal(Ui.Shell.AgentOffline, DesktopAgentAvailability.Describe(new IOException("x")));
    }

    [Fact]
    public void ComingBackIsAnnouncedOnceSoSurfacesCanReload()
    {
        var recoveries = 0;
        var changes = 0;
        DesktopAgentAvailability.Changed += (_, e) =>
        {
            changes++;
            if (e.Recovered)
            {
                recoveries++;
            }
        };

        DesktopAgentAvailability.ReportMonitor(AgentConnectionState.Connected);
        DesktopAgentAvailability.ReportFailure(new IOException("Pipe is broken."));
        DesktopAgentAvailability.ReportSuccess();

        Assert.Equal(1, recoveries);
        Assert.Equal(3, changes);

        // An unchanged state is not news; a surface that reloads on every report would reload on
        // every successful call.
        DesktopAgentAvailability.ReportSuccess();
        Assert.Equal(3, changes);
    }

    [Fact]
    public async Task ABurstOfFailuresProbesOnce()
    {
        // A restarted agent breaks every pipe in the process at once, so every surface reports a
        // failure within the same second. That must not become one reconnect attempt per surface.
        var probes = 0;
        var gate = new TaskCompletionSource();
        DesktopAgentAvailability.UseProbe(_ =>
        {
            Interlocked.Increment(ref probes);
            gate.TrySetResult();
            return Task.FromResult(true);
        });

        for (var attempt = 0; attempt < 10; attempt++)
        {
            DesktopAgentAvailability.ReportFailure(new IOException("Pipe is broken."));
        }

        await gate.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, probes);
    }

    [Fact]
    public async Task AProbeThatSucceedsPutsTheAgentBackOnline()
    {
        var recovered = new TaskCompletionSource();
        DesktopAgentAvailability.Changed += (_, e) =>
        {
            if (e.Recovered)
            {
                recovered.TrySetResult();
            }
        };
        DesktopAgentAvailability.UseProbe(_ => Task.FromResult(true));

        DesktopAgentAvailability.ReportFailure(new IOException("Pipe is broken."));

        await recovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(AgentAvailability.Online, DesktopAgentAvailability.Current);
    }

    [Fact]
    public async Task AProbeThatFailsSettlesOnOfflineRatherThanThrowing()
    {
        var settled = new TaskCompletionSource();
        DesktopAgentAvailability.Changed += (_, e) =>
        {
            if (e.Availability == AgentAvailability.Offline)
            {
                settled.TrySetResult();
            }
        };
        DesktopAgentAvailability.UseProbe(_ => throw new IOException("the pipe is not there either"));

        DesktopAgentAvailability.ReportFailure(new JsonException("truncated envelope"));

        await settled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(AgentAvailability.Offline, DesktopAgentAvailability.Current);
    }

    [Fact]
    public void TheStatusBarHasWordsForEveryStateIncludingTheNewOne()
    {
        foreach (var state in Enum.GetValues<AgentConnectionState>())
        {
            var snapshot = ShellStatusSnapshot.Initial with { AgentState = state };
            Assert.False(
                string.IsNullOrWhiteSpace(snapshot.AgentText),
                $"The status bar has nothing to say for {state}.");
        }

        Assert.Equal(
            Ui.Shell.AgentReconnectingStatus,
            (ShellStatusSnapshot.Initial with { AgentState = AgentConnectionState.Reconnecting }).AgentText);
    }
}
