using StorageHub.Agent;

namespace StorageHub.Desktop.Framework;

/// <summary>
/// What the splash says at each step, and when a step fails.
/// </summary>
/// <remarks>
/// English on purpose, as 1.x's splash was: most of it is on screen before the language files are
/// loaded -- loading them is one of the steps -- and a failure to load them has to be readable too.
/// </remarks>
internal static class BootText
{
    internal const string Patience = "This can take a few seconds the first time.";

    internal const string Quit = "Quit";

    internal const string Retry = "Retry";

    internal const string CopyDetails = "Copy details";

    internal const string CheckInstallation = "Check installation";

    internal const string CouldNotStart = "StorageHub could not start.";

    internal static string Describe(BootStage stage) => stage switch
    {
        BootStage.PreparingData => "Preparing StorageHub data...",
        BootStage.StartingFramework => "Starting the application framework...",
        BootStage.CheckingEnvironment => "Checking the StorageHub folders...",
        BootStage.LoadingSettings => "Loading your settings...",
        BootStage.LoadingLanguage => "Loading language files...",
        BootStage.StartingAgent => "Starting the background agent...",
        BootStage.Opening => "Opening StorageHub...",
        _ => "Starting StorageHub..."
    };

    /// <summary>
    /// The step the starting agent says it is on, as a line for the splash. The stage is an
    /// identifier the agent wrote and the name a subsystem's; neither is trusted further than that.
    /// </summary>
    internal static string AgentProgress(AgentStartupStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        var subsystem = string.IsNullOrWhiteSpace(step.Detail) ? "its services" : step.Detail;
        var what = step.Stage switch
        {
            AgentStartupStages.Launched => "loading",
            AgentStartupStages.Framework => "its framework is up",
            AgentStartupStages.Initializing => $"preparing {subsystem}",
            AgentStartupStages.Starting => $"starting {subsystem}",
            AgentStartupStages.Ready => "ready",
            _ => "working",
        };
        return $"Starting the background agent: {what}...";
    }

    internal static string DescribeFailure(BootStage stage) => stage switch
    {
        BootStage.PreparingData => "StorageHub could not prepare its data folder.",
        BootStage.StartingFramework => "StorageHub could not start its application framework.",
        BootStage.CheckingEnvironment => "StorageHub found a problem with its folders.",
        BootStage.LoadingSettings => "StorageHub could not read its settings.",
        BootStage.LoadingLanguage => "StorageHub could not load its configuration and language files.",
        _ => CouldNotStart
    };
}
