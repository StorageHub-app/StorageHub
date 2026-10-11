using StorageHub.Agent;
using StorageHub.Desktop.Framework;
using Xunit;

namespace StorageHub.Desktop.Tests;

public sealed class AgentStartupProgressTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"storagehub-progress-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    /// <summary>
    /// A starting agent says how far it has got in a file, and the splash words it. Only what was
    /// said after the wait began counts, and a file that is not the agent's is not believed: the
    /// reader is another process, and the file is the only way to hear a start that has not opened
    /// its pipe yet.
    /// </summary>
    [Fact]
    public void AStartingAgentSaysWhereItIsAndOnlyThisStartsWordsAreHeard()
    {
        var path = Path.Combine(_directory, "Agent", AgentStartupProgress.FileName);
        var progress = new AgentStartupProgress(path, "2.0.1");

        // Nothing said yet.
        Assert.Null(AgentStartupProgress.Read(path, DateTimeOffset.MinValue));

        progress.Report(AgentStartupStages.Launched);
        progress.Report(AgentStartupStages.Initializing, "StorageHub database");
        var step = Assert.IsType<AgentStartupStep>(AgentStartupProgress.Read(path, DateTimeOffset.MinValue));
        Assert.Equal(AgentStartupStages.Initializing, step.Stage);
        Assert.Equal("StorageHub database", step.Detail);
        Assert.Equal(Environment.ProcessId, step.ProcessId);
        Assert.Equal(
            "Starting the background agent: preparing StorageHub database...",
            BootText.AgentProgress(step));

        progress.Report(AgentStartupStages.Starting, "Local IPC");
        Assert.Equal(
            "Starting the background agent: starting Local IPC...",
            BootText.AgentProgress(Assert.IsType<AgentStartupStep>(
                AgentStartupProgress.Read(path, DateTimeOffset.MinValue))));

        // A previous run's last word is older than the wait that is going on now.
        progress.Report(AgentStartupStages.Ready);
        Assert.Null(AgentStartupProgress.Read(path, DateTimeOffset.UtcNow.AddMinutes(1)));
        Assert.Equal(
            "Starting the background agent: ready...",
            BootText.AgentProgress(Assert.IsType<AgentStartupStep>(
                AgentStartupProgress.Read(path, DateTimeOffset.MinValue))));

        // Garbage, a giant file and an over-long name are all ignored rather than put on a splash.
        File.WriteAllText(path, "{ not json");
        Assert.Null(AgentStartupProgress.Read(path, DateTimeOffset.MinValue));
        File.WriteAllText(path, new string('x', 10_000));
        Assert.Null(AgentStartupProgress.Read(path, DateTimeOffset.MinValue));
        progress.Report(AgentStartupStages.Starting, new string('n', 200));
        Assert.Null(AgentStartupProgress.Read(path, DateTimeOffset.MinValue));
    }
}
