using System.Diagnostics;
using System.Text.Json;

namespace StorageHub.Agent;

/// <summary>The steps an agent reports while it starts, as stable identifiers rather than words.</summary>
public static class AgentStartupStages
{
    /// <summary>The agent's own code is running; nothing has been set up yet.</summary>
    public const string Launched = "launched";

    /// <summary>The application framework is up.</summary>
    public const string Framework = "framework";

    /// <summary>A subsystem is being initialised; the detail names it.</summary>
    public const string Initializing = "initializing";

    /// <summary>A subsystem is being started; the detail names it.</summary>
    public const string Starting = "starting";

    /// <summary>Everything is started and the agent is answering.</summary>
    public const string Ready = "ready";
}

/// <summary>What an agent last said about how far its start has got.</summary>
public sealed record AgentStartupStep(
    int ProcessId,
    string Version,
    string Stage,
    string? Detail,
    DateTimeOffset UpdatedUtc);

/// <summary>
/// Lets a starting agent say how far it has got, in a small file the desktop can read before the
/// agent answers on its pipe.
/// </summary>
/// <remarks>
/// The pipe cannot say it: the first start after an install or an update can spend ten seconds or
/// more before the agent's own code runs at all, and then the agent opens its database and starts
/// its subsystems, so for that whole stretch a desktop waiting on the pipe knew nothing and the
/// splash said the same sentence throughout. A file needs no connection, and holds only a stage, the
/// name of the subsystem being started and a time -- nothing a person has saved. Writing it is best
/// effort: a start must never fail because its progress could not be recorded.
/// </remarks>
public sealed class AgentStartupProgress(string path, string version)
{
    public const string FileName = "startup-status.json";

    /// <summary>The most the reader will take from the file, which is a few hundred bytes.</summary>
    private const int MaximumFileBytes = 4096;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _path = path ?? throw new ArgumentNullException(nameof(path));
    private readonly string _version = version ?? throw new ArgumentNullException(nameof(version));
    private readonly Lock _gate = new();

    public static string PathIn(AgentPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Path.Combine(paths.AgentDirectory, FileName);
    }

    /// <summary>Records a step. Never throws.</summary>
    public void Report(string stage, string? detail = null)
    {
        var step = new AgentStartupStep(
            Environment.ProcessId,
            _version,
            stage,
            detail,
            DateTimeOffset.UtcNow);
        try
        {
            lock (_gate)
            {
                _ = Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

                // Written beside it and moved over, so a reader never sees half a file.
                var temporary = _path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(step, Json));
                File.Move(temporary, _path, overwrite: true);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"The agent could not record its start-up progress: {error.Message}");
        }
    }

    /// <summary>
    /// What the agent last said, or null when it has said nothing recent enough to be about the
    /// start being waited for.
    /// </summary>
    /// <param name="notBefore">
    /// Earlier than this is a previous run's, such as the "ready" an agent left behind when it was
    /// last stopped, and says nothing about the one now starting.
    /// </param>
    public static AgentStartupStep? Read(string path, DateTimeOffset notBefore)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is 0 or > MaximumFileBytes)
            {
                return null;
            }

            // The file is the agent's and the reader is another process: nothing in it is trusted
            // further than being a stage and a short name to put on a splash.
            var step = JsonSerializer.Deserialize<AgentStartupStep>(File.ReadAllText(path), Json);
            if (step is null ||
                step.UpdatedUtc < notBefore ||
                string.IsNullOrWhiteSpace(step.Stage) ||
                step.Stage.Length > 32 ||
                step.Detail is { Length: > 80 })
            {
                return null;
            }

            return step;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
