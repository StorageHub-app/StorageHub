using System.Text.Json;
using StorageHub.Testing;
using StorageHub.Agent.Host;

namespace StorageHub.Agent.Windows.Tests;

public sealed class AgentConcurrencyConfigurationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"storagehub-concurrency-config-{Guid.NewGuid():N}");

    [WindowsOnlyFact]
    public void Loads_bounded_desktop_policy_for_every_agent_worker_type()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "settings.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 4,
            adaptiveConcurrency = true,
            minimumConcurrency = 2,
            maximumTransferConcurrency = 12,
            perConnectionConcurrency = 3,
            maximumSyncConcurrency = 5
        }));

        var result = AgentConcurrencyConfiguration.Load(_directory);

        Assert.True(result.Adaptive);
        Assert.Equal(2, result.Minimum);
        Assert.Equal(12, result.MaximumTransfers);
        Assert.Equal(3, result.PerConnection);
        Assert.Equal(5, result.MaximumSyncs);
    }

    [WindowsOnlyFact]
    public void Rejects_out_of_bounds_policy_as_a_complete_unit()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "settings.json"), """
            {"schemaVersion":4,"adaptiveConcurrency":true,"minimumConcurrency":1,
             "maximumTransferConcurrency":99,"perConnectionConcurrency":2,"maximumSyncConcurrency":2}
            """);

        Assert.Equal(AgentConcurrencyConfiguration.Defaults, AgentConcurrencyConfiguration.Load(_directory));
    }

    /// <summary>
    /// After the desktop's settings migration the policy lives in <c>config.json</c>, and reading
    /// the old file instead would leave the agent at default concurrency with nothing to say so.
    /// </summary>
    [WindowsOnlyFact]
    public void Reads_the_migrated_config_file()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "config.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            adaptiveConcurrency = false,
            minimumConcurrency = 2,
            maximumTransferConcurrency = 10,
            perConnectionConcurrency = 4,
            maximumSyncConcurrency = 6
        }));

        var result = AgentConcurrencyConfiguration.Load(_directory);

        Assert.False(result.Adaptive);
        Assert.Equal(10, result.MaximumTransfers);
        Assert.Equal(4, result.PerConnection);
    }

    /// <summary>
    /// A migration leaves <c>settings.json.migrated</c> behind, but an interrupted one can leave
    /// the original too. The desktop's current answer wins.
    /// </summary>
    [WindowsOnlyFact]
    public void Prefers_the_new_file_when_both_are_present()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "config.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            maximumTransferConcurrency = 10,
            minimumConcurrency = 1,
            perConnectionConcurrency = 2,
            maximumSyncConcurrency = 2
        }));
        File.WriteAllText(Path.Combine(_directory, "settings.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 4,
            maximumTransferConcurrency = 31,
            minimumConcurrency = 1,
            perConnectionConcurrency = 2,
            maximumSyncConcurrency = 2
        }));

        Assert.Equal(10, AgentConcurrencyConfiguration.Load(_directory).MaximumTransfers);
    }

    /// <summary>An installation that has not migrated yet still gets its policy honoured.</summary>
    [WindowsOnlyFact]
    public void Falls_back_to_the_legacy_file_when_there_is_no_new_one()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "settings.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 15,
            maximumTransferConcurrency = 7,
            minimumConcurrency = 1,
            perConnectionConcurrency = 2,
            maximumSyncConcurrency = 2
        }));

        Assert.Equal(7, AgentConcurrencyConfiguration.Load(_directory).MaximumTransfers);
    }

    [WindowsOnlyFact]
    public void Defaults_when_the_desktop_has_never_run()
    {
        Directory.CreateDirectory(_directory);

        Assert.Equal(AgentConcurrencyConfiguration.Defaults, AgentConcurrencyConfiguration.Load(_directory));
    }

    /// <summary>
    /// A file the agent cannot trust is not a reason to fall through to an older one: a superseded
    /// answer is not a better answer than the defaults.
    /// </summary>
    [WindowsOnlyFact]
    public void An_out_of_bounds_new_file_does_not_fall_back_to_the_legacy_file()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "config.json"),
            """{"schemaVersion":1,"maximumTransferConcurrency":99}""");
        File.WriteAllText(Path.Combine(_directory, "settings.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 15,
            maximumTransferConcurrency = 7,
            minimumConcurrency = 1,
            perConnectionConcurrency = 2,
            maximumSyncConcurrency = 2
        }));

        Assert.Equal(AgentConcurrencyConfiguration.Defaults, AgentConcurrencyConfiguration.Load(_directory));
    }

    [WindowsOnlyFact]
    public void An_oversize_file_is_ignored()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "config.json"), new string('x', 65 * 1024));

        Assert.Equal(AgentConcurrencyConfiguration.Defaults, AgentConcurrencyConfiguration.Load(_directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
