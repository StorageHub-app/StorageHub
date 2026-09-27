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
